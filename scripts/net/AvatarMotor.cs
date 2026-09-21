using Godot;
using MpFoundation.Game.Sandbox;
using Sail.Game.Water;

namespace MpFoundation.Net;

/// <summary>
/// The deterministic avatar movement step — the single function that client prediction,
/// client reconciliation-replay, server authority, and the offline sandbox all run. The
/// server simulating the same step from the same inputs is what makes movement cheats
/// structurally impossible; the client replaying it over buffered inputs is what makes
/// prediction corrections exact.
///
/// It is "pure" in the netcode sense: deterministic given (state, intent, speedFactor, dt)
/// and the static collision world, with zero cosmetic side effects. It does mutate the
/// CharacterBody3D it is handed (MoveAndSlide is how Godot resolves collisions), but all
/// state it depends on comes in through <see cref="MoveState"/> and goes out the same way.
///
/// The movement constants are the signed-off iteration-1 feel, moved verbatim from
/// SandboxAvatar — this class is plumbing around them, never a retune.
/// </summary>
public static class AvatarMotor
{
    /// <summary>Simulation rate (aliases <see cref="Net.NetProfile.TickRate"/>). Matches the
    /// project's physics tick; every Step on every peer advances exactly one of these.</summary>
    public const float TickRate = Net.NetProfile.TickRate;
    public const float TickDelta = Net.NetProfile.TickDelta;

    // Movement feel. Gravity, the jump and the two ramp rates are the iteration-1 values — the
    // build whose feel was signed off: heavier gravity + higher jump than stock physics reads
    // snappy and cartoonish, and extra fall gravity makes arcs comedy-shaped (float up, plummet).
    //
    // THE TWO SPEEDS ARE NOT (CATCH-1, 2026-08-16). Taken as VALUES off feel/2026-08-16-movement
    // (PR #268), which is unmerged and which Talon has never launched — he has been judging the
    // whole catching loop at the old speed and reported the game "boring and slow". The rest of
    // that branch is deliberately NOT here: this packet brought the stride and the sprint and
    // nothing else.
    //
    //   value              was     now    why
    //   MoveSpeed          3.6     5.4    3.6 was never a design decision. It arrived in 65931fd
    //                                     (2026-07-12) as "generic foundation, Stage 1" — the
    //                                     mp-foundation default. Iteration 1 then reasoned it DOWN
    //                                     from 5.2 for a squat-blob toddle-comedy that the
    //                                     2026-08-11 prey pivot retired along with the whole
    //                                     avatar roster it belonged to. This is a return to that
    //                                     neighbourhood, not a new invention.
    //   SprintMultiplier   1.35    1.6    4.86 m/s was barely a jog and gave sprint nothing to be
    //                                     FOR. 8.64 m/s is a burst worth pressing a key for.
    //
    // THE RAMPS ARE NOW (MOVE-1, 2026-08-16). CATCH-1 left Acceleration and Deceleration alone and
    // flagged them; Talon then played that build and rejected the movement outright — "no feeling to
    // it, no weight behind it, there's no action which tells the player they're speeding up or
    // stopping — they just are at the speed and they just stopped." He was describing 0.25 s to a
    // dead sprint. Both ramps moved here, and a third constant (TurnAcceleration) was split out so
    // that slowing the ramp did not also slow the steering. Read those three doc comments; the whole
    // argument is on them, and JumpVelocity is still 8.4 because a jump is neither.
    //
    // The waddle those speeds used to drive is gone (MOVE-1). AvatarVisual now derives its cadence
    // and its stride from ground speed through LocomotionProfile, so there is no saturation constant
    // left to keep in step with these — a change here shows up in the gait as arithmetic rather than
    // as a paired edit somebody has to remember.
    public static float MoveSpeed => MotorTuning.Current.MoveSpeed;

    /// <summary>
    /// <b>How hard the body builds speed in the direction it is already going, m/s².</b>
    ///
    /// <para>MOVE-1, 2026-08-16: <b>34 → 9.</b> At 34 a dead sprint arrived in 0.25 s — about fifteen
    /// frames — which is a step function with a ramp below the threshold of perception, and Talon's
    /// "they just are at the speed" was an accurate description of the arithmetic rather than a feel
    /// complaint. At 9 the same sprint takes <b>0.96 s</b> and a jog takes 0.60 s: a ramp a player
    /// can see, which is the deliverable.</para>
    ///
    /// <para><b>Nothing got less responsive.</b> This rate applies only while the body is building
    /// speed along its current heading; redirecting or braking runs at
    /// <see cref="TurnAcceleration"/>, which is the value that shipped. See
    /// <see cref="RateFor"/> — that split is what buys a visible ramp without buying a body that
    /// takes a second to change its mind.</para>
    /// </summary>
    public static float Acceleration => MotorTuning.Current.Acceleration;

    /// <summary>
    /// <b>How hard the body sheds speed with no input, m/s².</b>
    ///
    /// <para>MOVE-1: <b>26 → 21.</b> A dead sprint now takes 0.41 s to stop against 0.96 s to reach,
    /// so braking reads as the harder of the two gestures without being instant — which is the shape
    /// the packet asks for.</para>
    ///
    /// <para><b>It may not go much lower, and the bound is not a preference.</b> A shove's
    /// launch-to-rest time has to finish inside <c>IncapacityRules.ImpulseRagdollSec</c> (1.2 s)
    /// so no player ever regains steering mid-slide; at the shipped flight time that puts the
    /// floor at about <b>18.4 m/s²</b>, and 21 leaves a little margin over it.</para>
    /// </summary>
    public static float Deceleration => MotorTuning.Current.Deceleration;

    /// <summary>
    /// <b>How hard the body changes direction, m/s² — the shipped 34, deliberately unchanged.</b>
    ///
    /// <para>A single acceleration constant has to be both "how fast do I get up to speed" and "how
    /// fast can I change my mind", and those two want opposite answers: the first should be slow
    /// enough to feel, the second must stay snappy or the character swims. At
    /// <see cref="Acceleration"/> = 9 a 180-degree turn at jog speed would take 1.2 s, which is a
    /// worse game than the one this packet is fixing.</para>
    ///
    /// <para>So <see cref="RateFor"/> blends between the two on the alignment between where the body
    /// is going and where it is being asked to go. Straight ahead gets the slow, visible ramp;
    /// sideways and backwards get exactly what shipped.</para>
    /// </summary>
    public static float TurnAcceleration => MotorTuning.Current.TurnAcceleration;

    // --- THE TURNAROUND SKID (SKID-1, 2026-08-16) -------------------------------------------------
    //
    // Talon named this one by reference: Super Mario 64's turnaround. Reverse at speed and the body
    // does not simply change its mind — it keeps sliding the way it was going, facing the way it was
    // going, while the input it has been given waits. Then it drives out of it.
    //
    // MOVE-1 built the exact opposite on purpose, and correctly: RateFor's opposed branch is
    // TurnAcceleration = 34, which resolves a full-sprint reversal in a fraction of a second so the
    // character never swims on ordinary steering. NOTHING BELOW RETUNES THAT. The skid is a STATE
    // that pre-empts that branch above a speed threshold; under the threshold, and after the state
    // exits, RateFor is untouched and behaves exactly as it shipped.
    //
    // It lives in the motor rather than in AvatarVisual because it changes where the body ENDS UP.
    // A slide simulated only on the owning client is a slide the server never ran, and reconciliation
    // would snap the player out of it every snapshot. The flag is therefore a MoveState field
    // (SkidRemaining) that crosses the wire, and Step stays a pure function of (prev, intent, dt).

    /// <summary>
    /// <b>Ground speed at or above which a reversal skids instead of simply turning, m/s.</b>
    ///
    /// <para><c>0.75 × <see cref="MoveSpeed"/></c> = <b>4.05 m/s</b>. Deliberately placed between the
    /// walk gear's top speed (2.43) and the jog's (5.40): holding the walk modifier never skids —
    /// creeping around a corner stays exact — while a full-stick jog reversal does, and a
    /// sprint reversal does hardest. Speed is the dial, which is the Mario 64 reading: the faster you
    /// were going, the more the turn costs you.</para>
    ///
    /// <para>The packet proposed <c>0.6 × MoveSpeed</c> (3.24 m/s) while also asking that "a jog turn
    /// stays crisp" — two statements that cannot both hold, since 3.24 is well below the jog speed.
    /// The jog was chosen to skid (Talon: a change he cannot feel is the failure), and the threshold
    /// moved up to the midpoint so a WALK is the gear that keeps its crispness.</para>
    /// </summary>
    public static float SkidEnterSpeedMps => MotorTuning.Current.SkidEnterSpeedMps;

    /// <summary>
    /// <b>How opposed the input must be to what the body is doing before it counts as a reversal.</b>
    /// The same normalized dot <see cref="RateFor"/> computes; <c>-0.5</c> is 120 degrees.
    ///
    /// <para>Chosen against the keyboard's actual geometry rather than by taste: on WASD the only
    /// alignments reachable from a held direction are 0 (a 90-degree turn, W→A), -0.707 (135,
    /// W→A+S) and -1 (180, W→S). A threshold anywhere in (-0.707, 0) therefore skids the true
    /// reversals and leaves every 90-degree corner crisp, and -0.5 sits in the middle of that gap
    /// where a gamepad stick's continuous range also lands sensibly.</para>
    /// </summary>
    public static float SkidAlignmentMax => MotorTuning.Current.SkidAlignmentMax;

    /// <summary>
    /// <b>How hard a skidding body sheds speed, m/s². 13 — and it is the number the whole feel
    /// rests on.</b>
    ///
    /// <para>A sprint enters at 8.64 m/s and leaves at <see cref="SkidExitSpeedMps"/>, so the slide
    /// runs <c>(8.64 - 1.2) / 13 = <b>0.57 s</b></c> and covers
    /// <c>(8.64² - 1.2²) / 26 = <b>2.82 m</b></c> — long enough to watch the body travel past the
    /// turn, short enough that it is a beat and not a loss of control. A full-stick jog enters at
    /// 5.40 and gets 0.32 s / 1.06 m, which reads as a scuff rather than a slide. That spread is the
    /// design: the state is one mechanism whose loudness is set by how committed you were.</para>
    ///
    /// <para><b>It is below <see cref="Deceleration"/> and below the 18.4 m/s² shove floor, and that
    /// is only safe because a skid can never happen to a launched body.</b> The floor is defined
    /// off <see cref="Deceleration"/>, which this does not touch, and <see cref="ShouldEnterSkid"/>
    /// refuses while airborne and while control is locked — two independent reasons, each
    /// sufficient.</para>
    /// </summary>
    public static float SkidDeceleration => MotorTuning.Current.SkidDeceleration;

    /// <summary>Ground speed at which the slide ends and the player's new direction takes over, m/s.
    /// 1.2 — low enough that the body has visibly finished travelling, high enough that the exit
    /// hands control back with momentum still in hand rather than from a dead stop.</summary>
    public static float SkidExitSpeedMps => MotorTuning.Current.SkidExitSpeedMps;

    /// <summary>
    /// <b>Hard ceiling on one skid, seconds.</b> 0.75 — comfortably above the 0.57 s a full sprint
    /// actually takes on flat ground, so it never clips the feel, and it exists purely so that no
    /// input pattern, slope or collision can park a player in a state they cannot leave
    /// (MECHANICS-BIBLE §2: every state needs an exit that does not depend on the player doing
    /// something). Also well inside <c>IncapacityRules.ImpulseRagdollSec</c>, so even a pathological
    /// skid is shorter than the shortest control denial the game already ships.
    /// </summary>
    public static float SkidMaxSec => MotorTuning.Current.SkidMaxSec;

    public static float Gravity => MotorTuning.Current.Gravity;
    public static float FallGravityMultiplier => MotorTuning.Current.FallGravityMultiplier;
    public static float JumpVelocity => MotorTuning.Current.JumpVelocity;
    public static float CoyoteTimeSec => MotorTuning.Current.CoyoteTimeSec;
    public static float JumpBufferSec => MotorTuning.Current.JumpBufferSec;
    public static float TurnLerp => MotorTuning.Current.TurnLerp;

    /// <summary><b>The apex hang</b> (MOVE-4c, spec §4): the share of gravity removed at exactly
    /// <c>v_y = 0</c>. <b>0.00 is the shipped value and an exact no-op</b> —
    /// <see cref="GravityFor"/> returns before any hang arithmetic, so every input produces the
    /// byte-identical result it produced before this term existed. See
    /// <see cref="ApexHangFactor"/> for the shape and why it is that shape.</summary>
    public static float ApexHangStrength => MotorTuning.Current.ApexHangStrength;

    /// <summary>The <c>|v_y|</c> at which the apex hang has fully decayed, m/s. Inert while
    /// <see cref="ApexHangStrength"/> is zero, so its 2.00 default is a slider start position and
    /// not a behaviour.</summary>
    public static float ApexHangWindowMps => MotorTuning.Current.ApexHangWindowMps;

    // --- THE AIRBORNE LAYER (MOVE-3, 2026-08-26) --------------------------------------------------
    //
    // Spec: docs/design/2026-08-26-airborne-control-and-jump-shape.md. Every constant below is
    // transcribed from it; none was chosen here.
    //
    // The defect being fixed: RateFor's output was applied identically in the air and on the
    // ground, so a sprint could be braked to a dead stop mid-flight in 0.41 s — comfortably inside
    // a 0.710 s jump. "The momentum you leave the ground with is momentum you keep" was not true of
    // any jump in the game. NOTHING BELOW RETUNES THE GROUND. MoveSpeed, Acceleration,
    // Deceleration, TurnAcceleration, Gravity, JumpVelocity and every Skid* constant are the
    // signed-off MOVE-1/SKID-1 values and are untouched.
    //
    // Three fractions rather than one, because RateFor's three rates do three different jobs and
    // Talon's feel target names two of them specifically (spec §2.1). All three sit inside the
    // 30-60% window he asked for, and the ordering Brake < Turn < Build IS the design.

    /// <summary>
    /// <b>Airborne share of <see cref="Acceleration"/> — building speed along the current
    /// heading, in the air.</b> <c>0.45 x 9 = 4.05 m/s²</c>.
    ///
    /// <para>The most generous of the three, and safely so: <see cref="AirWishSpeedFloorMps"/>'s
    /// ceiling bounds everything it can ever produce at the non-sprint ground wish speed. A
    /// standing jump has no momentum to keep and nothing to redirect, so letting it build a little
    /// speed toward a gap is the affordance that makes a standing jump worth having at all — it
    /// reaches 2.88 m/s by the end of a full-hold jump, never more.</para>
    /// </summary>
    public static float AirControlBuild => MotorTuning.Current.AirControlBuild;

    /// <summary>
    /// <b>Airborne share of <see cref="TurnAcceleration"/> — redirecting, in the air.</b>
    /// <c>0.35 x 34 = 11.90 m/s²</c>.
    ///
    /// <para>Redirect is what turns a committed run into free-floating drift, so it must be enough
    /// to steer with and not enough to reverse with. At this rate a 90-degree redirect at jog costs
    /// 0.454 s — 64% of the flight — while a 180-degree reversal needs 0.908 s at jog and 1.452 s
    /// at sprint, both longer than the 0.710 s maximum airtime. A reversal is therefore not
    /// available at any speed, and sprint commits harder than jog (spec §2.5).</para>
    /// </summary>
    public static float AirControlTurn => MotorTuning.Current.AirControlTurn;

    /// <summary>
    /// <b>Airborne share of <see cref="Deceleration"/> — braking with no input, in the air.</b>
    /// <c>0.30 x 21 = 6.30 m/s²</c>, the lowest of the three because braking to a stop in mid-air
    /// is the exact defect this packet fixes.
    ///
    /// <para><b>The momentum-keep proof</b> (spec §2.4): the most this can shed over the longest
    /// possible jump is <c>6.30 x 0.710 = 4.47 m/s</c>. A sprint jump (8.64) with input released
    /// for the whole flight lands at 4.17 m/s — 48% retained, uncancellable. A jog jump (5.40)
    /// would need 0.857 s to reach zero, longer than any jump lasts. A walk hop (2.43) <i>can</i>
    /// be stopped in the air, in 0.386 s, and that is deliberate: a hop in place is a thing every
    /// game has and a walk has no committed momentum to protect.</para>
    ///
    /// <para>The 4.47 m/s boundary sits just above <see cref="SkidEnterSpeedMps"/> (4.05), which is
    /// worth stating as a rule: <b>any run committed enough to skid on the ground is committed
    /// enough that it cannot be cancelled in the air.</b> One threshold, two systems.</para>
    ///
    /// <para><b>It must never be routed into a launched body's settle</b>, which derives off
    /// <see cref="Deceleration"/> itself and carries a hard 18.4 m/s² floor.</para>
    /// </summary>
    public static float AirControlBrake => MotorTuning.Current.AirControlBrake;

    /// <summary>
    /// <b>The floor of the airborne wish-speed ceiling, m/s.</b> While airborne the wish speed is
    /// <c>min(what the player asked for, max(current horizontal speed, the non-sprint ground wish
    /// speed))</c> — so nobody gains speed in the air, and sprint does nothing once you have left
    /// the ground.
    ///
    /// <para>This constant exists so the value has a name in this file; <b><see cref="Step"/>
    /// deliberately reads the <i>scaled</i> version</b> (<c>MoveSpeed * speedFactor * waterMul</c>)
    /// rather than the bare number, so a wading hop cannot exceed wading speed and a heavily-laden
    /// player cannot exceed their carry-limited speed.</para>
    ///
    /// <para><b>A ceiling rather than a remembered takeoff speed</b>, deliberately (spec §2.3): a
    /// <c>MoveState.LaunchSpeed</c> field would be marginally more faithful and would cost four
    /// more snapshot bytes on every packet for every player forever, since the snapshot flags byte
    /// has been full since v10. The ceiling is a pure function of state already on the wire and
    /// produces the same answer in every case a player can perceive.</para>
    /// </summary>
    public static float AirWishSpeedFloorMps => MoveSpeed;

    /// <summary>
    /// <b>Gravity multiplier applied while <i>rising</i> with the jump key released</b> —
    /// <c>3.0 x 22 = 66 m/s²</c>. This is the whole variable-jump model: full
    /// <see cref="JumpVelocity"/> on the press, and letting go cuts the ascent.
    ///
    /// <para><b>Apex 1.60 m held throughout, 0.67 m released immediately — 2.4x in height, 2.0x in
    /// airtime (0.710 s vs 0.357 s)</b>, and the range between is continuous and monotonic in
    /// release time.</para>
    ///
    /// <para><b>A gravity term rather than a one-shot <c>velocity.Y *= k</c> on the release edge,
    /// for three independent reasons, each sufficient — and MOVE-3b must not "simplify" it back
    /// (spec §3.2):</b>
    /// <list type="number">
    /// <item><b>No new <see cref="MoveState"/> field.</b> A one-shot cut needs a latch so it fires
    /// once rather than decaying the whole ascent; a latch is a state field, and the snapshot flags
    /// byte has been full since v10. The gravity form is idempotent per tick by construction. The
    /// snapshot stays at <c>NetCodec.SnapshotBytes</c>.</item>
    /// <item><b>Frame-rate independence.</b> A per-tick velocity multiply applies once per
    /// <i>tick</i> rather than once per <i>second</i> and is not a pure function of <c>dt</c>. This
    /// is <c>g * dt</c> like everything else in the block.</item>
    /// <item><b>MECHANICS §2, the flicker rule.</b> The cut is not a state, so there is nothing to
    /// enter, leave or chatter. A player mashing the key mid-ascent only swaps which gravity
    /// applies for that tick; the released branch is always the harsher one, so energy is never
    /// gained and the apex is bounded at the held-throughout value no matter what the input
    /// does.</item>
    /// </list></para>
    /// </summary>
    public static float JumpReleaseGravityMultiplier => MotorTuning.Current.JumpReleaseGravityMultiplier;

    /// <summary>
    /// <b>What a landing does to horizontal speed: nothing, deliberately.</b> 1.0, named with its
    /// value so this reads as an explicit decision rather than a blank somebody filled in.
    ///
    /// <para>A hop chain is the substrate for every emergent trick worth having, and it is exactly
    /// the case where a landing cost compounds: at a 10% tax, five pads leave you at 59% of your
    /// entry speed, so a chain the player cleared at hop two they fall short of at hop five for
    /// reasons they cannot see and cannot correct. That converts a skill expression into a decay
    /// curve. A player who wants to shed speed on landing already has a way — release the stick,
    /// and <see cref="Deceleration"/> takes over the moment <c>Grounded</c> returns.</para>
    ///
    /// <para>The game also already charges for momentum once, in the right place: SKID-1's
    /// turnaround is the cost of a <i>badly committed</i> direction change and the player chooses
    /// it. And the landing already <i>reads</i> as costly without being costly —
    /// <c>StepEvents.FallSpeed</c> is reported for exactly that and the presentation layer scales a
    /// squash from it. Selling the impact is presentation's job; taking the speed is not.</para>
    ///
    /// <para><b>It is referenced, not applied.</b> There is no multiply in <see cref="Step"/>,
    /// because the correct implementation of "1.0" is the absence of a line — see
    /// <see cref="LandingHorizontalSpeed"/>, which is what a test can hold on to.</para>
    /// </summary>
    public const float LandingSpeedMultiplier = 1.0f;

    /// <summary><b>The landing rule as a function:</b> horizontal speed carried through a touchdown
    /// is unchanged. Exists so spec §6.1's decision has something a test can assert against — a
    /// rule implemented as the <i>absence</i> of a line is otherwise unprovable, and "somebody
    /// added a landing tax" is precisely the regression worth catching. The movement playground's
    /// hop chains fail visibly at pad four or five if this ever stops returning its
    /// argument.</summary>
    public static float LandingHorizontalSpeed(float preLandingSpeed)
        => preLandingSpeed * LandingSpeedMultiplier;

    /// <summary>How fast the body swings onto an <i>aimed</i> facing — faster than
    /// <see cref="TurnLerp"/>, because turning to face the thing you are swinging at is a commitment
    /// the player has already made and a lazy turn there reads as the character arguing about it.
    /// Still a blend and not a snap: <c>0.09 s</c> to close 2/3 of the angle.</summary>
    public static float AimTurnLerp => MotorTuning.Current.AimTurnLerp;

    /// <summary><b>Knob 58 (FP-1): the body faces the look, not the travel.</b> On in this game —
    /// see <see cref="MotorTuning.BodyYawFollowsAim"/> for the argument and for what it costs a
    /// bot. Read as a threshold, the same way <see cref="TouchdownSlideImmediate"/> and
    /// <see cref="DuckWalkGuaranteed"/> read theirs, so one spelling of "is this toggle on"
    /// serves every 0/1 row.</summary>
    public static bool BodyYawFollowsAim => MotorTuning.Current.BodyYawFollowsAim > 0.5f;

    public static float SprintMultiplier => MotorTuning.Current.SprintMultiplier;

    /// <summary>Floor of the carry-encumbrance factor (mirrors CarryController): the
    /// server clamps client-reported speed factors into [this, 1] so a doctored packet
    /// can never claim a speed boost.</summary>
    public const float MinSpeedFactor = 0.55f;

    // =============================================================================================
    // MOVE-5 — the verb state machine. Twenty-two accessors, transcribed from spec §11.1, in the
    // same shape every knob above uses: a static property over MotorTuning.Current, so the name a
    // reader greps for finds both ends of the round trip.
    // =============================================================================================

    /// <summary>
    /// <b>The distinguished <see cref="MoveState.VerbClockTicks"/> value meaning "the previous tick
    /// was airborne"</b> — 255, the one byte value no counted clock can reach (every increment is
    /// clamped at 254, and the widest window any knob range can ask for is
    /// <c>SlideMaxSec</c>'s 3.00 s = 180 ticks).
    ///
    /// <para><b>Why a sentinel rather than a sixth field or the flags2 byte's last spare bit.</b>
    /// Spec §3.4 rule 2 zeroes the clock on every airborne tick, and T8 — the touchdown entry —
    /// needs to know that <i>this</i> grounded tick is the first one after a flight. A plain zero
    /// cannot say that: it is also what a fresh hold and a control-lock release leave behind. The
    /// clock byte is already on the wire and already has 75 unused values, so a distinguished one
    /// carries the fact for nothing — no new <see cref="MoveState"/> field, no new snapshot byte,
    /// and the flags2 byte keeps the spare bit §10.2 costed it with.</para>
    ///
    /// <para><b>It is a not-counting value, exactly as §3.4 rule 2 requires.</b> Nothing compares
    /// it against a window: <see cref="StepVerb"/> tests for it by identity, before any threshold,
    /// and replaces it in the same pass. A clock that has this value has never counted a tick
    /// toward anything.</para>
    /// </summary>
    public const byte VerbClockAirborne = 255;

    /// <summary>Knob 31. <b>How long the jump button must stay down on the ground before a crouch
    /// verb enters</b> — 0.20 s, twelve ticks (spec §4.2).
    ///
    /// <para><b>The window is only ever reachable after a landing</b>, and that is the price of one
    /// zero-latency button rather than a defect: coyote is refilled on every grounded tick, so a
    /// grounded press is never denied and always fires a jump. "Sprint, hold, slide" is really
    /// "sprint, hold, jump, land, slide" (spec §2, ruling 1 — settled, and fork O1's trigger is a
    /// hands verdict on <see cref="TouchdownSlideImmediate"/>, not a second entry path).</para></summary>
    public static float JumpHoldWindowSec => MotorTuning.Current.JumpHoldWindowSec;

    /// <summary>Knob 31, in whole ticks. Rounded once, in <c>MotorTuning</c>, so the motor and any
    /// readout cannot disagree about the integer.</summary>
    public static int JumpHoldWindowTicks => MotorTuning.Current.JumpHoldWindowTicks;

    /// <summary>Knob 32. Non-zero = the crouch verb starts on the touchdown tick itself (T8);
    /// zero = it waits out <see cref="JumpHoldWindowSec"/> on the ground. <b>A knob, not a
    /// decision</b> — both behaviours are built and Talon decides with his hands (spec §4.3).</summary>
    public static bool TouchdownSlideImmediate => MotorTuning.Current.TouchdownSlideImmediate > 0.5f;

    /// <summary>Knob 33, as the product. <b>6.588 m/s at the shipped tuning, which is
    /// <c>LocomotionProfile.SprintEnterMps</c> exactly</b> — so the rule has a name a player can
    /// hold: <b>you can only slide out of a sprint</b> (spec §4.3).
    ///
    /// <para><b>Deliberately NOT <see cref="SkidEnterSpeedMps"/>.</b> The two answer different
    /// questions: the skid asks "did you commit hard enough to a direction that reversing it should
    /// cost you", the slide asks "are you moving fast enough that a slide will read as a slide".
    /// 4.05 m/s is below jog, and a slide entered at a brisk walk decelerates over 0.34 s and 0.7 m,
    /// which is a stumble. Sharing the threshold would also mean tuning one verb silently retunes
    /// the other.</para></summary>
    public static float SlideEnterSpeedMps => MotorTuning.Current.SlideEnterSpeedMps;

    /// <summary>Knob 34. Ground speed at or below which a slide settles, m/s. Held strictly below
    /// <see cref="SlideEnterSpeedMps"/> by <c>MotorTuning.Validate</c> — the 4.588 m/s gap at the
    /// defaults IS the anti-chatter mechanism, exactly as the skid pair's is.</summary>
    public static float SlideExitSpeedMps => MotorTuning.Current.SlideExitSpeedMps;

    /// <summary>Knob 35. <b>The slide's single speed authority</b>, m/s². 6.0 — below ground
    /// <see cref="Deceleration"/> (21) and below <see cref="SkidDeceleration"/> (13), because a
    /// slide is <i>slippery</i> and that is the whole verb. The stick contributes no acceleration
    /// and no braking, so you cannot pump a slide and you cannot shorten one.</summary>
    public static float SlideDeceleration => MotorTuning.Current.SlideDeceleration;

    /// <summary>Knob 36. <b>The carve</b> — how fast the slide's heading rotates toward the stick,
    /// °/s. A rotation of the velocity vector, not a <c>MoveToward</c>, so it preserves speed
    /// exactly and cannot fight <see cref="SlideDeceleration"/>. This is what distinguishes the
    /// slide from the skid, which suppresses steering entirely.</summary>
    public static float SlideTurnRateDeg => MotorTuning.Current.SlideTurnRateDeg;

    /// <summary>Knob 37, in whole ticks. <b>It blocks ONLY the speed and duration exits, never the
    /// release exit</b> — releasing the button always exits on the next tick, from any state, at any
    /// time. That is what makes this bound legal under the weight principle: it is a floor on the
    /// slide's <i>self</i>-termination, not a commitment window (spec §4.5). 0.12 s deliberately
    /// equals <see cref="CoyoteTimeSec"/> and <see cref="JumpBufferSec"/>, so the body has one
    /// forgiveness constant rather than three.</summary>
    public static int SlideMinTicks => MotorTuning.Current.SlideMinTicks;

    /// <summary>Knob 38, in whole ticks. Inert at the defaults (the speed exit fires at 1.107 s,
    /// inside 1.40 s); it earns its keep at the edge of the knob range, where
    /// <see cref="SlideDeceleration"/> at its 0.5 minimum would take 13.3 s to reach the speed exit
    /// — a state with no exit reached by a slider, which MECHANICS §2 forbids.</summary>
    public static int SlideMaxTicks => MotorTuning.Current.SlideMaxTicks;

    /// <summary>Knob 39, as the product. <b>2.43 m/s, which is <c>LocomotionProfile.WalkSpeedMps</c>
    /// exactly</b>: the game already has a name for that speed and a fourth speed in a three-gear
    /// world is a number nobody can hold. <b>The duck walk therefore costs no speed at all relative
    /// to a walk</b> — "weight is skin, not friction" means the crouch's cost is its pose and its
    /// low profile, not a tax (spec §5.3).</summary>
    public static float DuckWalkSpeedMps => MotorTuning.Current.DuckWalkSpeedMps;

    /// <summary>Knob 40. Non-zero = every settling slide latches into a duck walk; zero = only when
    /// the stick is inside <see cref="DuckWalkEntryConeDeg"/> of travel. Ships at zero, the brief's
    /// literal reading — "hold <i>forward</i> through the slide", and forward is a condition.</summary>
    public static bool DuckWalkGuaranteed => MotorTuning.Current.DuckWalkGuaranteed > 0.5f;

    /// <summary>Knob 41. Largest stick-vs-travel deviation that still settles, degrees. Inert at
    /// <see cref="DuckWalkGuaranteed"/>.</summary>
    public static float DuckWalkEntryConeDeg => MotorTuning.Current.DuckWalkEntryConeDeg;

    /// <summary>Knob 42, in whole ticks — 21 at the shipped 0.35 s, which is <b>3.02 m of running
    /// between hops</b> at sprint, 2.9x the jump buffer and 4.4x what a precision-timed chain would
    /// be. A player who mashes gets the chain for free; a player who lands, takes a stride and jumps
    /// again still gets it (spec §6.4).</summary>
    public static int ChainGraceTicks => MotorTuning.Current.ChainGraceTicks;

    /// <summary>Knob 43, in whole ticks — 30 at the shipped 0.50 s. <b>Linear in depth, one level
    /// per half second:</b> fumble one landing and re-chain inside half a second and you lose
    /// exactly one level. Decays gradually; it is never reset to zero.</summary>
    public static int ChainDecayTicks => MotorTuning.Current.ChainDecayTicks;

    /// <summary>
    /// Knob 44. <b>Wish speed gained per chain level, m/s — and it is the WISH, never the
    /// velocity.</b> Spec §6.1 proves the alternative by arithmetic: a horizontal velocity bonus on
    /// the jump tick does not survive the flight, because the airborne ceiling resolves the wish
    /// back to the <i>unchained</i> sprint wish and then brakes the body down to it at
    /// <c>AirControlBuild x Acceleration</c> = 4.05 m/s², which over a 0.717 s flight sheds up to
    /// 2.90 m/s — the whole bonus and then some. <b>A velocity-boost chain lands at exactly the
    /// speed it took off at.</b>
    ///
    /// <para><b>0.00 ships, and it is the exact no-op</b>: at zero the depth term adds nothing and
    /// <see cref="MoveState.ChainDepth"/> never leaves 0, which is also one of the two gates on
    /// §9's momentum grant. The chain is the one verb in this wave that fires with <i>no new
    /// input at all</i>, so shipping it live would change the ordinary hop chains the playground's
    /// flow course exists to compare against (spec §11.2).</para></summary>
    public static float ChainBonusMps => MotorTuning.Current.ChainBonusMps;

    /// <summary>Knob 45. Cap on <see cref="MoveState.ChainDepth"/>. <b>Clamped to the three-bit
    /// wire width by <c>MotorTuning.Validate</c>, not by a widget</b> — past 7 the field truncates
    /// and a peer's depth disagrees with the server's (spec §11.3).</summary>
    public static int ChainMaxDepth => ClampCount(MotorTuning.Current.ChainMaxDepth, 7);

    /// <summary>Knob 46. <b>0 = off (the exact no-op, and what ships), 1 = traditional, 2 = the
    /// Kick.</b> A double jump changes what every gap distance in the playground means, and the
    /// baseline Talon approved is the only baseline the lab has — so he switches it on to feel it,
    /// which is literally what he asked for.</summary>
    public static int AirJumpMode => ClampCount(MotorTuning.Current.AirJumpMode, 2);

    /// <summary>Knob 47. Air jumps allowed per flight. <b>Clamped to the two-bit wire width</b>,
    /// same reason as <see cref="ChainMaxDepth"/>.</summary>
    public static int AirJumpCountMax => ClampCount(MotorTuning.Current.AirJumpCountMax, 3);

    /// <summary>Knob 48. Mode 1's launch as a fraction of <see cref="JumpVelocity"/> — 0.80 gives
    /// 6.72 m/s and a 1.026 m second apex. <b>It overwrites <c>velocity.Y</c>, which deletes all
    /// accumulated downward momentum in one frame</b> — the single most weightless thing a body can
    /// do, and the reason spec §7.2 proposes the Kick beside it.</summary>
    public static float AirJumpVelocityFraction => MotorTuning.Current.AirJumpVelocityFraction;

    /// <summary>Knob 49. The Kick's share of the fall it converts into forward speed. <b>Capped at
    /// 1.00:</b> above 1 the Kick returns more speed than the fall carried, which is energy from
    /// nothing and precisely the floaty read the variant exists to avoid.</summary>
    public static float KickConversionFraction => MotorTuning.Current.KickConversionFraction;

    /// <summary>Knob 50. Flat term in the Kick's gain, m/s.</summary>
    public static float KickHorizontalGainMps => MotorTuning.Current.KickHorizontalGainMps;

    /// <summary>Knob 51. What the Kick does to vertical speed, m/s. <b>1.60 arrests the fall; it
    /// does not climb</b> — 0.058 m of rise over 0.073 s. Capped at 6.00 so mode 2 can never become
    /// mode 1 wearing mode 2's name.</summary>
    public static float KickVerticalMps => MotorTuning.Current.KickVerticalMps;

    /// <summary>Knob 52. The committed-run floor the Kick needs, m/s. Under it the Kick does
    /// nothing at all, so <b>a standing hop has no second jump</b> — which is exactly where the
    /// traditional variant reads worst (spec §7.2, point 4).</summary>
    public static float KickMinSpeedMps => MotorTuning.Current.KickMinSpeedMps;

    /// <summary>Knob 53 (MOVE-5f). <b>0 = off and the exact no-op, 1 = the real coil pose,
    /// 2 = the coil baked into the rise curve.</b> Only mode 2 is this class's business at all —
    /// mode 1 is <c>AvatarVisual</c>'s and writes no velocity — but the toggle is one knob because
    /// it is one question, and both readers take it from here so they cannot disagree about which
    /// approach is running.</summary>
    public static int AnticipationMode => ClampCount(MotorTuning.Current.AnticipationMode, 2);

    /// <summary>Knob 56. Mode 2's extra gravity at the instant of launch, as a fraction of whatever
    /// gravity already applied. Inert unless <see cref="AnticipationMode"/> is 2.</summary>
    public static float AnticipationBakedStrength
        => MotorTuning.Current.AnticipationBakedStrength;

    /// <summary>Knob 57. The share of the launch speed over which mode 2's term decays. Inert
    /// unless <see cref="AnticipationMode"/> is 2.</summary>
    public static float AnticipationBakedWindow => MotorTuning.Current.AnticipationBakedWindow;

    /// <summary>Rounds a discrete knob to a whole count and clamps it into <c>[0, max]</c>. A
    /// second line of defence behind <c>MotorTuning.Validate</c>'s own clamp, for the two rows whose
    /// maxima are WIRE WIDTHS rather than taste: a truncated field is a peer disagreeing with the
    /// server, and that must be impossible from every direction, not merely from the panel.</summary>
    private static int ClampCount(float value, int max)
    {
        if (!float.IsFinite(value))
            return 0;
        return Mathf.Clamp((int)Mathf.Round(value), 0, max);
    }

    /// <summary>
    /// Advances one avatar one fixed tick. Sets the body's transform/velocity from
    /// <paramref name="prev"/>, integrates gravity + jump forgiveness + accel/decel +
    /// facing exactly as the signed-off feel, runs MoveAndSlide, and returns the
    /// resulting state. Cosmetic facts are reported through <paramref name="events"/>
    /// and never acted on here.
    /// </summary>
    /// <param name="faceYaw">World yaw the body must turn toward this tick instead of its travel
    /// direction, or null for the ordinary "face where you are going" rule. <b>Server-resolved, and
    /// derived rather than replicated</b> — the caller computes it from the peer's own already
    /// sanitized <see cref="MoveIntent.AimYaw"/> plus that peer's swing phase, which the authority
    /// owns and the owning client predicts identically. Nothing a client asserts reaches this: there
    /// is no target id, no position and no outcome in it, only an angle the aim clamp has already
    /// been through. Optional so every existing call site is byte-identical.</param>
    public static MoveState Step(CharacterBody3D body, in MoveState prev, in MoveIntent intent,
        float speedFactor, float dt, out StepEvents events, float? faceYaw = null)
    {
        // --- Water (W2, lake-water contract §4/§6) ------------------------------------------
        // Everything the water contract does to movement is derived HERE, from prev.Water /
        // prev.Soaked / prev.ControlLocked — state that already rides MoveState and is therefore
        // already identical on the server, in owner prediction and inside a reconciliation
        // replay. That is why the water contract needed no new parameter on this method and no
        // second derivation to keep in sync with this one: the three call paths cannot disagree
        // about a value they all read out of the state they were handed.
        WaterState water = prev.Water;

        // --- Failure states (phase 1c, beta plan §10) ---------------------------------------
        // Incapacitation and the momentary impulse ragdoll join the water sputter-out at the SAME
        // seam, deliberately, rather than each getting a branch of its own further down. Three
        // independent reasons a body is not steering, ONE place that stops it steering — which is
        // what makes "is this body under player control" a question with a single answer instead
        // of three that can disagree. All three read out of MoveState, so the server, owner
        // prediction and a reconciliation replay cannot resolve it differently.
        //
        // THIS IS THE PHYSICS-AUTHORITY ROW (STATE-CASCADE-TABLE row 2, BEHAVIOR-BIBLE §10.2):
        // "exactly one writer per tick". There is no second writer to disable, because this
        // package deliberately ships NO PhysicalBone3D ragdoll — CharacterBody3D + MoveAndSlide
        // stays the sole transform writer in every failure state, exactly as it is in every water
        // state. A comic ice block and a body flat on its back are POSES, not simulations, and
        // buying them with a physics ragdoll would have bought the cascade table's hard
        // constraint 2 (ragdoll physics cannot be client-predicted or replayed) for no gameplay
        // gain at all. The register law wants a slapstick shape, not a rag of bones.
        bool locked = ControlLockedBy(prev);

        // Control lock is total: no direction, no jump, no sprint. Gravity, decel and collision
        // still run, so a locked body settles and slides realistically instead of freezing in
        // mid-air. There is no partial-control state (MECHANICS-BIBLE §2).
        // MOVE-3: JumpHeld joins the zeroing, consistent with Jump and Sprint — a body that is not
        // steering is not holding anything either. GravityFor additionally carries an explicit
        // !locked guard, so this is belt-and-braces rather than the only thing standing between a
        // control-locked rising body and 3x gravity.
        MoveIntent effective = locked
            ? intent with { MoveDir = Vector3.Zero, Jump = false, JumpHeld = false, Sprint = false }
            : intent;

        Vector3 dir = SanitizeMoveDir(effective.MoveDir);
        float sf = SanitizeSpeedFactor(speedFactor);

        // Applied AFTER SanitizeSpeedFactor on purpose. That clamp's floor (MinSpeedFactor,
        // 0.55) is the anti-cheat bound on the CLIENT-REPORTED carry factor; the swim
        // multiplier (0.40) is a server-derived environmental fact and must be able to go
        // below it. Folding water into speedFactor would have let the clamp silently discard
        // the difference between wading and swimming.
        float waterMul = WaterGeometry.SpeedMultiplierFor(water);
        if (prev.Soaked)
            waterMul *= WaterGeometry.SoakedSpeedMul;

        // --- Vertical ---
        Vector3 velocity = prev.Velocity;
        float fallSpeed = 0f;
        bool swimming = water == WaterState.Swimming;
        if (swimming)
        {
            // Horizontal-only (spec §4). NOT buoyancy and NOT a force — spec §1.4 forbids both.
            // A deterministic MoveToward of the vertical velocity onto whatever line the body
            // should be holding, resolved by MoveAndSlide against the bed exactly like any other
            // motion, so the drop-off's shallow end stops the descent with no special case.
            //   - normal swimming: converge on the swim line, SwimSubmersionM below the surface
            //   - going under (control locked): sink at a flat SinkRate — the swallow
            //
            // Overwriting velocity.Y rather than adding to it is also the DEFINED ENTRY behaviour
            // for "arrived by falling": a running jump off the dock enters with a large negative
            // Y velocity, and the clamp arrests it inside one tick instead of letting the plunge
            // carry the player to the bed.
            // prev.ControlLocked SPECIFICALLY, not the general `locked` union above. The sink is
            // the lake's swallow — a water-contract behaviour that belongs to the sputter-out —
            // not a generic consequence of losing control. Widening it to the union would drown
            // a Frozen player in the shallows: an ice block would sink to the bed and become
            // unreachable for the drag verb, which is a soft-lock the dawn floor would have to
            // rescue every single time. Held at the swim line instead, it bobs where a teammate
            // can reach it — and "an ice block bobbing in the lake" is the register's reading too.
            velocity.Y = prev.ControlLocked
                ? -WaterGeometry.SinkRate
                : Mathf.Clamp(
                    (WaterGeometry.SwimLineY - prev.Position.Y) * WaterGeometry.SwimSettleRate,
                    -WaterGeometry.SwimSettleRate, WaterGeometry.SwimSettleRate);
        }
        else if (!prev.Grounded)
        {
            // MOVE-3: the rising-release cut lives inside GravityFor, which is a pure three-way
            // branch with a boundary case at exactly velocity.Y == 0 and therefore provable in the
            // fast test tier. Falling is unchanged; rising-and-held is unchanged; a locked body is
            // unchanged, bit for bit.
            float g = GravityFor(velocity.Y, effective.JumpHeld, locked);
            velocity.Y -= g * dt;
            fallSpeed = Mathf.Max(0f, -velocity.Y);
        }

        // Coyote time, jump buffering and the air jump: ONE resolution, through the pure StepJumps
        // below, for the reason ControlLockedBy and ShouldEnterSkid give — Step needs a live
        // CharacterBody3D, so forgiveness left inline would be provable only in the slowest test
        // tier, which is where the coyote window and the buffered-jump-on-landing chain had no
        // coverage at all before MOVE-3.
        //
        // W6-5: the ground jump and the air jump are ONE function and not two calls in a row.
        // What binds them is the rule that a press edge is a RESOURCE and the ground jump spends
        // it — and that rule used to live here, in a comment, asserting a guarantee that no line of
        // code provided. A press inside a live coyote window took the ground jump's 8.4 and then
        // had it overwritten by the air jump's 6.72, spending a double jump nobody had used.
        // The rule is now inside StepJumps, where a test can reach it; do not re-split this call.
        //
        // The sprint wish handed in uses prev.ChainDepth rather than this tick's: the chain is
        // resolved from `anyJump`, which depends on whether the air jump fired, so the Kick's
        // ceiling is priced at the depth the body CARRIED INTO the tick. One level of headroom, on
        // the one tick where the two can differ, and stating it is cheaper than a second pass.
        float groundWish = MoveSpeed * sf * waterMul;
        MotorTuning tuning = MotorTuning.Current;
        JumpsResolution jumps = StepJumps(tuning, velocity, prev.Grounded, prev.CoyoteRemaining,
            prev.JumpBufferRemaining, prev.AirJumpsUsed, effective.Jump,
            WaterGeometry.JumpAllowed(water), locked,
            groundWish * tuning.SprintMultiplier + prev.ChainDepth * tuning.ChainBonusMps, dt);
        velocity = jumps.Velocity;
        float coyote = jumps.CoyoteRemaining;
        float jumpBuffer = jumps.JumpBufferRemaining;
        bool jumped = jumps.Jumped;
        bool anyJump = jumps.AnyJump;

        // --- MOVE-5: the chain and the verb (spec §3.4 rules 3 and 4) --------------------------
        // The touchdown tick is recognised by the sentinel the previous airborne tick left in the
        // clock byte — see VerbClockAirborne for why that, rather than a sixth MoveState field or
        // the flags2 byte's last spare bit.
        bool touchdown = prev.Grounded && prev.VerbClockTicks == VerbClockAirborne;
        ChainResolution chain = StepChain(tuning, prev.ChainDepth, prev.ChainTimerTicks,
            prev.Grounded, touchdown, anyJump, locked);
        VerbResolution verb = StepVerb(tuning, prev.Verb, prev.VerbClockTicks, prev.Grounded,
            locked, anyJump, effective.JumpHeld, water, velocity, dir);
        bool sliding = verb.Verb == MoveVerb.Slide;

        // --- Horizontal ---
        float speed = groundWish;
        if (effective.Sprint && WaterGeometry.SprintAllowed(water) && dir.LengthSquared() > 0.01f)
            speed *= SprintMultiplier;
        // MOVE-5, the chain jump (spec §6.1): ONE term, applied to the WISH — grounded and airborne
        // alike — and never to the velocity. Grounded it lets the body accelerate past sprint;
        // airborne it raises the request so the ceiling clause preserves rather than brakes. Nobody
        // gains speed in the air, because the depth can only change on a jump tick and a jump tick
        // is grounded by definition. At the shipped ChainBonusMps of 0.00 this line adds exactly
        // zero — note that the DEPTH still accrues (§6.3's ladder does not consult the bonus, and
        // MomentumGranted's doc has the consequence); it is the product that is nothing.
        speed += chain.Depth * tuning.ChainBonusMps;
        // MOVE-5, the crouch cap (spec §5.1): the Tuck and the DuckWalk share one behaviour and
        // differ only in the latch. The wish speed is capped and EVERYTHING ELSE IS UNCHANGED —
        // full Acceleration, full Deceleration, full TurnLerp, full TurnAcceleration. There is no
        // responsiveness cost of any kind in either state; that is the point.
        //
        // MOVE-8 (MOVE-7 §3.2, Talon's observation traced to a mechanism): the cap reads the
        // SPRINT wish — `speed`, which already carries the sprint multiplier and the chain — and
        // not `groundWish`, which is the non-sprint one. The old line's fraction was of the JOG, so
        // the crouch's ceiling was the jog at EVERY DuckWalkSpeedFraction: entering a roll from a
        // sprint braked 6.08 → at most 3.8 m/s on the entry tick, and no value of the fraction
        // could buy it back. That is why every roll read as "braking to a walk". The cap is now a
        // fraction of the wish you actually had, so a sprint crouch is exactly as fast, relative to
        // its own entry speed, as a jog crouch is to its. `Mathf.Min` is kept because the knob's
        // range is [0.10, 1.00]: the fraction can never RAISE the wish, and the Min says so
        // structurally rather than by trusting the knob table.
        //
        // Against a wish rather than the bare MoveSpeed for the reason AirWishSpeedFloorMps gives:
        // a laden player stays capped by their carry factor here too, and a wading one by the water
        // multiplier — both of those ride inside `groundWish` and therefore inside `speed`.
        speed = CrouchCappedWish(tuning, verb.Verb, speed);
        // MOVE-3, the airborne wish-speed ceiling (spec §2.3). Nobody gains speed in the air: the
        // request is capped by the speed already carried, with the non-sprint GROUND wish as a
        // floor so a standing jump still has something to build toward. groundWish rather than
        // AirWishSpeedFloorMps on purpose — it is already scaled by the carry factor and the water
        // multiplier, so a wading hop cannot exceed wading speed. Sprint is left ungated and simply
        // neutralised here (spec §9.3): LocomotionProfile and the presentation layer read the
        // sprint bit, and gating it in the motor would change what they see.
        if (!prev.Grounded)
            speed = AirborneWishSpeed(speed, groundWish, velocity,
                MomentumGranted(tuning, chain.Depth, jumps.AirJumpsUsed));
        Vector3 wish = dir * speed;

        // --- THE TURNAROUND SKID (SKID-1) --------------------------------------------------------
        // Resolved BEFORE the rate, because a skid pre-empts RateFor entirely rather than adjusting
        // it. Pure: StepSkid reads only (prev state, this tick's velocity/wish, dt) and returns the
        // next tick's timer, so the server, owner prediction and a reconciliation replay all derive
        // the identical value from the identical inputs — the same derive-don't-trust shape
        // ControlLockedBy already uses.
        //
        // MOVE-5 (§3.4 rule 5): the skid is evaluated AFTER the verb and forced to zero while the
        // verb is not Normal. That one argument carries T15's refusal and T16's cancellation, and
        // T16 is the whole of §8's momentum share — deliberate player intent beats an automatic
        // consequence, and the velocity vector the skid was carrying passes into the slide
        // untouched.
        float skidRemaining = StepSkid(prev.SkidRemaining, prev.Grounded, locked,
            velocity, wish, dt, verb.Verb);
        bool skidding = skidRemaining > 0f;

        // MOVE-1: the rate is no longer a two-way switch on "is there input". Building speed along
        // the current heading is the slow, visible ramp; redirecting or braking is the shipped 34.
        // See RateFor — the whole argument is there, and it is a pure function so it has a test.
        //
        // MOVE-3: and it is no longer the same rate in the air. prev.Grounded, not a live query —
        // the same convention gravity and coyote already use, with two stated one-tick
        // consequences: the tick a jump fires still runs GROUND rates (wanted: the launch tick
        // keeps ground authority), and the tick the body touches down still runs air rates. One
        // tick is 16.7 ms at 60 Hz. Neither is a bug.
        float rate = RateFor(velocity, wish, prev.Grounded);
        // A control-locked body has no movement intent, so `wish` is zero and this pair decays the
        // horizontal velocity toward zero at Deceleration. That is correct for every shipped
        // control lock — a shove IS a shove, and a sputter-out should settle.
        if (skidding)
        {
            // STEERING IS SUPPRESSED, NOT REDUCED — and that is the whole feel. The player's new
            // direction does not begin to apply until the state exits, so the horizontal velocity is
            // pulled toward ZERO (not toward `wish`) at SkidDeceleration. A skid you can steer out
            // of is just a slower turn, and a slower turn is what MOVE-1 correctly refused to build.
            velocity.X = Mathf.MoveToward(velocity.X, 0f, SkidDeceleration * dt);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0f, SkidDeceleration * dt);
        }
        else if (sliding)
        {
            // THE CROUCH-SLIDE (MOVE-5 §4.4). Disjoint from the skid by construction — StepSkid
            // returns 0 while the verb is not Normal — so the branch order above is immaterial;
            // written as an else-if anyway, because a reader should not have to prove that to know
            // only one of them runs. The whole rule is in StepSlide, pure, so it has a test.
            velocity = StepSlide(tuning, velocity, wish, dt);
        }
        else
        {
            velocity.X = Mathf.MoveToward(velocity.X, wish.X, rate * dt);
            velocity.Z = Mathf.MoveToward(velocity.Z, wish.Z, rate * dt);
        }

        // Facing (the collision capsule is symmetric, so yaw is deterministic bookkeeping — it
        // never changes what MoveAndSlide does).
        //
        // MOVE-1: the shipped rule was "face travel, always", and it is the one line of policy
        // behind Talon's "if I'm going to swing a butterfly net at a bee I want to be facing the
        // bee" — and behind the backpedal awkwardness he reported the day before, where retreating
        // from a stinging creature meant turning your back on it to hit it. A caller with an aimed verb in flight
        // hands the yaw in; everyone else gets the travel rule unchanged.
        //
        // SKID-1: while skidding the body faces where it is TRAVELLING, not where it has been asked
        // to go — that is the difference between "sliding past the turn" and "turning slowly", and
        // it is why the facing rule is handed the velocity instead of the wish for those ticks. The
        // heading is stable through the whole slide (MoveToward toward zero never changes a vector's
        // direction), so the body holds its line and then swings onto the new one as the state exits
        // and `wish` takes over again. An aimed verb still wins, unchanged: turning to face the thing
        // you are swinging at is a commitment the player already made, skid or no skid.
        //
        // MOVE-5: the slide takes the same clause, and for the same reason — the body faces where
        // it is TRAVELLING while it carves, exactly as §4.4 says ("facing: travel, not wish,
        // exactly as the skid does"). The two states are disjoint, so this is one condition and not
        // a precedence.
        //
        // FP-1: with BodyYawFollowsAim on (this game's default), an intent's own already-sanitized
        // look yaw IS the facing, and it wins over the skid/slide clause for the same reason an
        // explicit faceYaw does — in first person the player is looking somewhere on purpose while
        // they slide, and a body that swung round to face its drift would be the camera arguing
        // with the mouse. An explicit faceYaw from a caller still beats the knob: it is derived
        // from the same aim PLUS something the authority owns, so it is the more informed answer.
        float? aimedFacing = faceYaw
            ?? (BodyYawFollowsAim ? SanitizeAimYaw(intent.AimYaw) : (float?)null);
        float yaw = ResolveYaw(prev.Yaw,
            skidding || sliding ? new Vector3(velocity.X, 0f, velocity.Z) : wish, aimedFacing, dt);

        body.Position = prev.Position;
        body.Rotation = new Vector3(0, yaw, 0);
        body.Velocity = velocity;
        body.MoveAndSlide();

        // Bump scan: speed carried into a non-floor surface (pre-slide velocity along
        // -normal). Reported, not reacted to — bumps are cosmetic-only by design rule.
        float bumpImpact = 0f;
        Vector3 bumpPosition = default;
        GodotObject? bumpCollider = null;
        for (int i = 0; i < body.GetSlideCollisionCount(); i++)
        {
            KinematicCollision3D hit = body.GetSlideCollision(i);
            Vector3 normal = hit.GetNormal();
            if (normal.Y > 0.5f)
                continue; // floor contact, not a bump
            float impact = Mathf.Max(0f, -velocity.Dot(normal));
            if (impact > bumpImpact)
            {
                bumpImpact = impact;
                bumpPosition = hit.GetPosition();
                // PHYS-1 (2026-09-20): and WHAT was bumped, so a player who walks into a shelf of
                // cans knocks them over. Read off the slide collision this scan already walks --
                // no second query, and nothing here reacts to it: bumps stay "reported, not
                // reacted to" in this file, exactly as the rule above says. The consumer is
                // SandboxAvatar, on the server only.
                bumpCollider = hit.GetCollider();
            }
        }

        Vector3 outVelocity = body.Velocity;

        events = new StepEvents
        {
            Jumped = jumped,
            BumpImpact = bumpImpact,
            BumpPosition = bumpPosition,
            BumpCollider = bumpCollider,
            FallSpeed = fallSpeed,
        };
        // Water state for the NEXT tick, resolved from where the body actually ended up. Placed
        // here, after MoveAndSlide, so the state always describes a position the collision world
        // agreed to — a player shoved out of the lake by geometry is Dry on the very next step,
        // never one tick of swimming on dry land. Hysteresis reads prev.Water, which is why the
        // value has to be carried in state (see MoveState.Water's doc comment).
        return new MoveState
        {
            Position = body.Position,
            Velocity = outVelocity,
            Yaw = yaw,
            CoyoteRemaining = coyote,
            JumpBufferRemaining = jumpBuffer,
            Grounded = body.IsOnFloor(),
            // SKID-1. Carried in state for the reason MoveState's own doc gives and CoyoteRemaining
            // already demonstrates: it influences how the body moves, so a replay that started from
            // a different value would resolve a different trajectory and re-diverge forever.
            SkidRemaining = skidRemaining,
            Water = WaterGeometry.ResolveAt(water, body.Position),
            // Pass-through: both are server-owned facts this step never authors. Carrying them
            // means a replay reproduces them exactly instead of losing them at the rewind.
            Soaked = prev.Soaked,
            ControlLocked = prev.ControlLocked,
            // Same pass-through, same reason: IncapacitationService owns both, this step only
            // obeys them. Carrying them means a reconciliation replay reproduces every locked
            // tick exactly rather than losing the lock at the rewind and re-granting the player
            // control for the replayed frames — the controllable-ragdoll defect, arriving through
            // the reconciliation door. See MoveState.Incapacity's own doc.
            Incapacity = prev.Incapacity,
            ImpulseRagdoll = prev.ImpulseRagdoll,
            // MOVE-5. Same argument as SkidRemaining above, five times over: each of these changes
            // where the body ends up — the verb changes the wish cap, the decel authority and the
            // steering rule; the depth changes the wish; the counter changes whether an air jump is
            // still available — so a replay that started from a different value resolves a different
            // trajectory and re-diverges forever (STATE-CASCADE-TABLE hard constraint 1).
            Verb = verb.Verb,
            VerbClockTicks = verb.ClockTicks,
            ChainDepth = chain.Depth,
            ChainTimerTicks = chain.TimerTicks,
            AirJumpsUsed = jumps.AirJumpsUsed,
        };
    }

    /// <summary>
    /// Input sanitation — the anti-cheat teeth. Non-finite components become zero and
    /// the direction is clamped to the unit disc, so no packet can ask for more than
    /// full-stick movement. Run on both server (authority) and client (so an honest
    /// client predicts exactly what the server will simulate).
    /// </summary>
    /// <summary>
    /// <b>The single definition of "this body is not steering".</b> Three independent systems can
    /// take control — the lake's sputter-out, incapacitation, and the momentary impulse ragdoll —
    /// and this is the one place their union is expressed. <see cref="Step"/> reads it to discard
    /// the intent; nothing else re-derives it.
    ///
    /// <para>Public and pure so the rule is provable without an engine. <see cref="Step"/> itself
    /// needs a live <c>CharacterBody3D</c> and therefore only the headless suite can exercise it,
    /// which would have left the most safety-critical decision in the movement path — "does this
    /// player's input do anything" — testable only in the slowest tier. Splitting the decision out
    /// costs one method call and buys it a home in <c>dotnet test</c>.</para>
    /// </summary>
    public static bool ControlLockedBy(in MoveState prev)
        => prev.ControlLocked
           || prev.Incapacity != Sail.Game.Failure.IncapacityState.Active
           || prev.ImpulseRagdoll;

    /// <summary>
    /// <b>How hard the horizontal velocity is pulled toward what the player asked for, m/s².</b>
    /// Pure, so the whole feel decision is one function with a test rather than a ternary buried in
    /// <see cref="Step"/>.
    ///
    /// <para><b>No input at all → <see cref="Deceleration"/>.</b> Unchanged in kind from what
    /// shipped.</para>
    ///
    /// <para><b>Input → a blend on alignment</b> between the current heading and the wished one:
    /// exactly aligned takes <see cref="Acceleration"/> (the slow, felt ramp — this is "get up to
    /// speed"), and anything perpendicular or opposed takes <see cref="TurnAcceleration"/> (the
    /// shipped 34 — this is "change direction", and it must not get slower). A body starting from
    /// rest has no heading to be aligned <i>with</i>, and is treated as fully aligned so a standing
    /// start is a ramp rather than a launch.</para>
    ///
    /// <para><b>MOVE-3: airborne runs the same three branches at a fraction of each rate</b> —
    /// <see cref="AirControlBrake"/>, <see cref="AirControlTurn"/>, <see cref="AirControlBuild"/>,
    /// one per job (spec §2.1/§2.2). Before this, <see cref="Step"/> applied this function's output
    /// identically in the air and on the ground, which is what let a sprint be braked to a dead
    /// stop mid-flight. <paramref name="grounded"/> <b>defaults to true</b> so every pre-existing
    /// call site and every pre-existing test compiles and passes byte-identically.</para>
    ///
    /// <para><b>It multiplies nothing else.</b> Not <see cref="MoveSpeed"/>, not
    /// <see cref="SprintMultiplier"/>, not the skid rates, not <see cref="TurnLerp"/> /
    /// <see cref="AimTurnLerp"/> (facing is unchanged in the air — the body still faces travel, per
    /// BEHAVIOR §2), not gravity, and never a launched body's settle.</para>
    /// </summary>
    /// <param name="grounded">Whether the body was on the floor at the START of this tick
    /// (<c>prev.Grounded</c>), not a live physics query.</param>
    public static float RateFor(Vector3 velocity, Vector3 wish, bool grounded = true)
    {
        float brakeRate = grounded ? Deceleration : Deceleration * AirControlBrake;
        float buildRate = grounded ? Acceleration : Acceleration * AirControlBuild;
        float turnRate = grounded ? TurnAcceleration : TurnAcceleration * AirControlTurn;

        if (!(wish.LengthSquared() > 0.01f))
            return brakeRate;

        var flat = new Vector2(velocity.X, velocity.Z);
        float speed = flat.Length();
        if (!(speed > 0.35f) || !float.IsFinite(speed))
            return buildRate; // standing start: the ramp IS the point.

        var want = new Vector2(wish.X, wish.Z);
        float alignment = flat.Dot(want) / (speed * want.Length());
        if (!float.IsFinite(alignment))
            return buildRate;
        return Mathf.Lerp(turnRate, buildRate, Mathf.Clamp(alignment, 0f, 1f));
    }

    /// <summary>
    /// <b>The airborne wish-speed ceiling</b> (MOVE-3, spec §2.3): while airborne, the wish speed
    /// is the smaller of what the player asked for and <c>max(current horizontal speed, the
    /// non-sprint ground wish speed)</c>. <b>Nobody gains speed in the air.</b>
    ///
    /// <list type="bullet">
    /// <item>Took off at sprint (8.64), holding forward + sprint → 8.64. Holds, cannot gain.</item>
    /// <item>Took off at sprint, decayed to 8.0 → capped at 8.0. Ratchets down only.</item>
    /// <item>Took off at jog (5.40), presses sprint mid-air → 5.40. <b>Sprint does nothing in the
    /// air.</b></item>
    /// <item>Standing jump (0) holding forward → 5.40, built at
    /// <see cref="AirControlBuild"/> × <see cref="Acceleration"/>.</item>
    /// </list>
    ///
    /// <para><paramref name="groundWishSpeed"/> is the caller's <i>already scaled</i> non-sprint
    /// ground wish (<c>MoveSpeed × speedFactor × waterMultiplier</c>), not the bare
    /// <see cref="AirWishSpeedFloorMps"/> — see that constant for why.</para>
    ///
    /// <para>Pure and public for the reason <see cref="ControlLockedBy"/> gives.</para>
    ///
    /// <para><b>MOVE-5 §9 — the momentum grant, and it is the wave's ONE amendment to a MOVE-3a
    /// clause.</b> It is raised as a finding rather than made silently, because §2.3 is an approved,
    /// argued clause and a wave should not amend one by inference (spec O3). It is required: without
    /// it neither the chain jump nor the Kick can exist, because the shipped clause resolves
    /// <c>desired</c> back to the unchained sprint wish and then brakes the body down to it at
    /// 4.05 m/s², eating any earned speed inside a single flight.
    /// <list type="bullet">
    /// <item><b>It never lets a body gain speed in the air.</b>
    /// <c>Max(desired, flatSpeed)</c> can only stop a brake; it cannot exceed the speed already
    /// carried. §2.3's actual guarantee is strengthened in letter and kept in spirit.</item>
    /// <item><b>It is gated on two fields already in <see cref="MoveState"/></b>
    /// (<see cref="MoveState.ChainDepth"/>, <see cref="MoveState.AirJumpsUsed"/>), so it is a pure
    /// function of replicated state: no wire cost and no determinism risk.</item>
    /// <item><b>At the shipped defaults it is the EXACT no-op.</b>
    /// <see cref="ChainBonusMps"/> = 0.00 means the depth never leaves 0, and
    /// <see cref="AirJumpMode"/> = 0 means the counter never leaves 0, so
    /// <paramref name="momentumGranted"/> is false on every tick of every flight and this method is
    /// character-identical to its pre-MOVE-5 self. The parameter is optional so every existing call
    /// site and every existing test is bit-identical too.</item>
    /// </list></para>
    /// </summary>
    /// <param name="prevVelocity">The body's velocity at the moment the ceiling is applied. <b>Step
    /// passes the LIVE velocity, not <c>prev.Velocity</c></b>, and the two are the same vector
    /// horizontally on every tick where no air jump fired — nothing between them writes X or Z. On
    /// a Kick tick they differ, and the live one is the only correct reading: a stale one would let
    /// the ceiling brake the Kick's gain straight back off, which is the exact defect §6.1
    /// describes.</param>
    /// <param name="momentumGranted">§9's gate — always <see cref="MomentumGranted"/>, never
    /// re-derived at a call site. False at every shipped default; see that predicate for the one
    /// place this implementation departs from §9's literal text, and for the arithmetic that forced
    /// it.</param>
    public static float AirborneWishSpeed(float requestedSpeed, float groundWishSpeed,
        Vector3 prevVelocity, bool momentumGranted = false)
    {
        var flat = new Vector2(prevVelocity.X, prevVelocity.Z);
        float flatSpeed = flat.Length();
        if (!float.IsFinite(flatSpeed))
            return requestedSpeed;
        float desired = Mathf.Min(requestedSpeed, Mathf.Max(flatSpeed, groundWishSpeed));
        if (momentumGranted)
            desired = Mathf.Max(desired, flatSpeed);    // hold what you have; never gain
        return desired;
    }

    /// <summary>
    /// <b>§9's gate: is this body carrying speed it EARNED, that the airborne ceiling would
    /// otherwise brake off?</b> One spelling, so the momentum grant cannot come to mean two
    /// slightly different things in two places.
    ///
    /// <para><b>This is the one place MOVE-5b departs from spec §9's literal text, and the
    /// departure is what makes §9's own stated property TRUE.</b> §9 writes the gate as
    /// <c>ChainDepth &gt; 0 || AirJumpsUsed &gt; 0</c> and justifies it two lines later with
    /// "<c>ChainBonusMps = 0.00</c> means <c>ChainDepth</c> never leaves 0". <b>That justification
    /// is false</b>, and the test that proves the no-op is what found it: §6.3's accrual is
    /// <c>if (ChainTimerTicks &gt; 0 &amp;&amp; ChainDepth &lt; ChainMaxDepth) ChainDepth++</c>,
    /// which does not consult the bonus at all. An ordinary hop chain at the SHIPPED defaults
    /// therefore reaches depth 1 on its second hop, the literal gate goes true, and the clause
    /// stops being a no-op — the observable difference being that releasing sprint in mid-air would
    /// no longer shed speed (<c>desired</c> would be lifted from the 5.40 ground wish back to the
    /// 8.64 already carried, instead of the body braking toward 5.40 at 6.3 m/s²). That is a change
    /// to the body Talon measured and approved, and the packet forbids one.</para>
    ///
    /// <para><b>Adding <c>ChainBonusMps &gt; 0</c> is faithful to what §9 is FOR rather than to how
    /// it is spelled.</b> The clause exists to stop the ceiling braking off speed the chain or the
    /// Kick earned; at a bonus of zero the chain earns nothing, so there is nothing to hold and the
    /// grant has no job. <c>AirJumpsUsed &gt; 0</c> needs no companion term because the counter can
    /// only leave zero when a mode actually fired.</para>
    ///
    /// <para>The alternative — gating the ACCRUAL so the depth really never leaves zero — was
    /// rejected: it would make <c>ChainDepth</c> and <c>ChainTimerTicks</c> permanently zero at the
    /// defaults, and MOVE-5c's animation tell and MOVE-5d's readout both consume them. A seam whose
    /// fields are dead until a knob moves is a seam neither packet can build against.</para>
    /// </summary>
    public static bool MomentumGranted(in MotorTuning t, byte chainDepth, byte airJumpsUsed)
        => (chainDepth > 0 && t.ChainBonusMps > 0f) || airJumpsUsed > 0;

    /// <summary>
    /// <b>The crouch's wish-speed cap, as a pure function</b> — the one line MOVE-8 changed, split
    /// out of <c>Step</c> so it can have a test at all. The Tuck and the DuckWalk share one
    /// behaviour and differ only in the latch; every other verb passes through untouched.
    /// </summary>
    /// <param name="t">The tuning to evaluate against, so a test can measure a candidate.</param>
    /// <param name="verb">This tick's resolved verb.</param>
    /// <param name="wishSpeed">The wish the body would have had standing up: <c>MoveSpeed</c>
    /// scaled by the carry factor and the water multiplier, then by <c>SprintMultiplier</c> if the
    /// sprint bit is set, then plus the chain bonus. <b>The sprint is INSIDE this argument</b>, and
    /// that is the whole of MOVE-8's fix — see the remark at the call site in <c>Step</c>.</param>
    public static float CrouchCappedWish(in MotorTuning t, MoveVerb verb, float wishSpeed)
        => verb is MoveVerb.Tuck or MoveVerb.DuckWalk
            ? Mathf.Min(wishSpeed, t.DuckWalkSpeedFraction * wishSpeed)
            : wishSpeed;

    /// <summary>
    /// <b>Which gravity applies to an airborne body this tick, m/s²</b> — the variable-jump cut
    /// (MOVE-3, spec §3.2), as a pure three-way branch.
    ///
    /// <list type="bullet">
    /// <item><b>Falling</b> (<c>velocityY &lt; 0</c>) → <c>Gravity * FallGravityMultiplier</c>.
    /// Unchanged.</item>
    /// <item><b>Rising with the key held</b> → <see cref="Gravity"/>. Unchanged.</item>
    /// <item><b>Rising with the key released</b> → <c>Gravity *
    /// <see cref="JumpReleaseGravityMultiplier"/></c>. The cut.</item>
    /// </list>
    ///
    /// <para><b>Boundary, stated (MECHANICS §1):</b> the cut condition is <c>velocityY &gt; 0</c>,
    /// <b>strictly</b> — at exactly zero (the apex tick) ordinary <see cref="Gravity"/> applies,
    /// matching the existing strict <c>&lt; 0</c> fall test. <c>velocityY</c> crosses zero once per
    /// jump, monotonically, so the threshold cannot chatter.</para>
    ///
    /// <para><b><paramref name="locked"/> is load-bearing, not defensive.</b> A control lock
    /// already forces <c>JumpHeld</c> false, so without this guard every control-locked rising
    /// body would silently get 3x gravity — turning a shove into a stumble. With it, every locked
    /// body gets exactly the gravity it gets today.</para>
    ///
    /// <para><b>THE APEX HANG (MOVE-4c, spec §4) is a fourth term, not a fourth branch.</b>
    /// Whichever of the three branches applied is multiplied by <see cref="ApexHangFactor"/>, so
    /// the branch ORDERING survives verbatim (<c>g_released &gt; g_held</c> at every
    /// <c>v_y</c>, because the same factor multiplies both) and no energy can be gained by mashing
    /// the key. The term is gated off for <paramref name="locked"/> for the reason that guard
    /// exists at all: a control-locked body must get exactly today's gravity, bit for bit, so a
    /// shove's arc is computed from bare <see cref="Gravity"/> and nothing else.</para>
    ///
    /// <para><b>THE BAKED COIL (MOVE-5f, approach 2) is a FIFTH term on the same footing</b>, and
    /// see <see cref="LaunchCoilFactor"/> for its shape. It multiplies the same way, is gated off
    /// for the same case, and is a pure function of this tick's <c>v_y</c> — no state, nothing
    /// to enter or leave.</para>
    ///
    /// <para><b>MOVE-8: the apex hang is LIVE now.</b> Talon ruled
    /// <see cref="ApexHangStrength"/> to 0.30 on 2026-08-28, so this method is no longer
    /// byte-identical to its pre-MOVE-4c self and cannot be — what it IS is that self multiplied by
    /// <see cref="ApexHangFactor"/> and by nothing else, which
    /// <c>ApexHangAndLandingDipTests.AtTheShippedStrength_GravityForIsThePreMove4cBranchScaledBy
    /// TheHangAndNothingElse</c> sweeps over 19,000 points. <see cref="AnticipationMode"/> still
    /// ships at 0, so the baked coil is still skipped by a predicate and <c>g</c> is still returned
    /// unmultiplied by it. <b>Both terms remain gated off entirely for the locked case</b>, which
    /// is the property the guard below exists for and the one MOVE-8 did not move.</para>
    /// </summary>
    public static float GravityFor(float velocityY, bool jumpHeld, bool locked)
    {
        float g;
        if (velocityY < 0f)
            g = Gravity * FallGravityMultiplier;
        else if (velocityY > 0f && !jumpHeld && !locked)
            g = Gravity * JumpReleaseGravityMultiplier;
        else
            g = Gravity;

        // BOTH TERMS BELOW ARE OFF FOR A LOCKED BODY: a shove's arc is computed from bare Gravity,
        // so a control-locked body must get exactly today's gravity or that arithmetic silently
        // stops describing the arc.
        if (locked)
            return g;

        // The apex hang. `!(s > 0f)` rather than `s <= 0f` so a NaN that somehow reached the
        // tuning cannot propagate into a position the body never returns from; MotorTuning.Validate
        // already replaces non-finite values, and for every value that passes it the two spellings
        // agree exactly.
        //
        // MOVE-5f RESHAPED THIS FROM AN EARLY RETURN INTO A MULTIPLY, AND IT IS THE IDENTICAL
        // FUNCTION: the old spelling was `if (locked || !(s > 0f)) return g;` followed
        // by one multiply. At s = 0 both return g unmultiplied; at s > 0 both return g times the
        // same factor, in the same order, so no rounding differs either. The reshape was needed
        // because the baked coil must be able to run with the hang switched OFF, which is the
        // shipped state, and an early return above it would have made mode 2 unreachable.
        float s = ApexHangStrength;
        if (s > 0f)
            g *= ApexHangFactor(velocityY, s, ApexHangWindowMps);

        // THE BAKED COIL (MOVE-5f, approach 2) — a FOURTH TERM, not a fourth branch, for the apex
        // hang's own reason: whichever of the three branches applied is multiplied, so the branch
        // ORDERING survives verbatim (g_released > g_held at every v_y, because the same factor
        // multiplies both) and no energy can be gained by mashing the key.
        //
        // A TERM AND NOT A STATE, per the packet's rule 2 and MECHANICS: it is a pure function of
        // this tick's v_y, so there is nothing to enter, nothing to leave, nothing to latch and
        // nothing that can be left set. Frame-rate independent for the same reason — it scales
        // gravity, and gravity is already integrated against dt.
        if (AnticipationMode == BakedCoilMode)
            g *= LaunchCoilFactor(velocityY, JumpVelocity, AnticipationBakedStrength,
                AnticipationBakedWindow);
        return g;
    }

    /// <summary>The <see cref="AnticipationMode"/> value that selects approach 2. Spelled once so
    /// the motor and <c>AnticipationCoil.BakedCoil</c> cannot drift apart; asserted equal in
    /// <c>AnticipationCoilTests</c>.</summary>
    private const int BakedCoilMode = 2;

    /// <summary>
    /// <b>The baked coil</b> (MOVE-5f, approach 2): the factor whichever gravity branch applied is
    /// multiplied by in the first moments of a rise. <c>1</c> means no coil.
    ///
    /// <para><b>What it is for.</b> The brief's second candidate is "the crouch faked — baked into
    /// the first few (already-in-motion) frames of the non-uniform rise curve". A body that leaves
    /// the ground at full speed and then climbs a little more reluctantly for the first few frames
    /// reads as one that gathered itself on the way up, with <i>no pose at all</i>. That is the
    /// whole of approach 2, and it is why it is a gravity term rather than an animation.</para>
    ///
    /// <para><c>u = clamp((launch − v_y) / (launch × window), 0, 1)</c> — 0 at the instant of
    /// launch and 1 once the body has shed <paramref name="windowFraction"/> of its launch speed —
    /// then <c>w = 1 − smoothstep(u)</c> and the answer is <c>1 + strength × w</c>. Smoothstep for
    /// <see cref="ApexHangFactor"/>'s reason, and it is the same reason: <c>w'(0) = w'(1) = 0</c>,
    /// so gravity is C¹ where the coil ends. <b>A linear ramp would be a jerk step at the window
    /// edge — a hitch exactly where the coil is supposed to be handing over to the ordinary
    /// rise.</b></para>
    ///
    /// <para><b>Rising only, and strictly.</b> At <c>v_y ≤ 0</c> it returns 1, so a body walking off
    /// a ledge is never coiled (it never rose), the apex tick is untouched, and the descent is the
    /// descent that shipped. An air jump launched slower than <paramref name="launchMps"/> simply
    /// starts part-way into the window, which is the honest read: it is a smaller launch, so it has
    /// less of a coil to spend.</para>
    ///
    /// <para><b>It TAKES height rather than adding it</b>, which is the direction that matters for
    /// the pin: <c>MotorTuningKnobs.AnticipationBakedStrength</c> is bounded by the held apex's
    /// FLOOR (1.45 m), not its ceiling, where the apex hang is bounded by the ceiling.</para>
    ///
    /// <para><b>Pure in its arguments</b>, deliberately — it reads no tuning of its own, for the
    /// reason <see cref="ApexHangFactor"/> gives: that is what lets the shape be swept at any
    /// strength and any window by a test that never parks a non-default
    /// <see cref="MotorTuning.Current"/>.</para>
    /// </summary>
    /// <param name="velocityY">Vertical velocity, m/s. Only a positive one coils.</param>
    /// <param name="launchMps">The speed the body left the ground at — <see cref="JumpVelocity"/>
    /// for an ordinary jump.</param>
    /// <param name="strength">Extra gravity at the launch instant, as a fraction, 0–2.</param>
    /// <param name="windowFraction">Share of <paramref name="launchMps"/> over which it decays.</param>
    public static float LaunchCoilFactor(float velocityY, float launchMps, float strength,
        float windowFraction)
    {
        if (!(strength > 0f) || !(windowFraction > 0f) || !(launchMps > 0f) || !(velocityY > 0f))
            return 1f;
        float u = Mathf.Clamp((launchMps - velocityY) / (launchMps * windowFraction), 0f, 1f);
        float w = 1f - (u * u * (3f - (2f * u)));     // 1 - smoothstep(u)
        return 1f + (strength * w);
    }

    /// <summary>
    /// <b>The apex-hang attenuation</b> (MOVE-4c, spec §4.1): the factor whichever gravity branch
    /// applied is multiplied by. <c>1</c> means no hang.
    ///
    /// <para><c>w = 1 − smoothstep(|v_y| / window)</c>, so <c>w(0) = 1</c> (full attenuation at the
    /// apex), <c>w(1) = 0</c> (none at the window edge) and — the property that costs two multiplies
    /// and buys the whole feel — <c>w'(0) = w'(1) = 0</c>. Gravity is therefore C¹ at the window
    /// edge, and the kink <c>|v_y|</c> has at zero is cancelled by <c>w'(0) = 0</c>. <b>A linear
    /// ramp <c>w = 1 − u</c> would be continuous in <c>g</c> but not in <c>dg/dv_y</c>, which is a
    /// jerk step at the window edge — a hitch rather than a float.</b></para>
    ///
    /// <para><b>One honest statement.</b> The motor already has an acceleration discontinuity at
    /// <c>v_y = 0</c> (rising uses 22, falling 29.7). This term neither removes it nor adds to it:
    /// at <c>v_y = 0</c> the SAME factor multiplies both sides, so the existing step is scaled,
    /// never reshaped. No new discontinuity is introduced; removing the pre-existing one would
    /// change the jump Talon approved and is not this term's job.</para>
    ///
    /// <para><b>Pure in its arguments</b>, deliberately — it reads no tuning of its own. That is
    /// what lets the shape be swept at any strength and any window by a test that never parks a
    /// non-default <see cref="MotorTuning.Current"/> (MOVE-4b's standing rule).</para>
    /// </summary>
    /// <param name="velocityY">Vertical velocity, m/s. Only its magnitude is read.</param>
    /// <param name="strength">Share of gravity removed at exactly <c>v_y = 0</c>, 0–0.90.</param>
    /// <param name="windowMps">The <c>|v_y|</c> at which the term has fully decayed, m/s.</param>
    public static float ApexHangFactor(float velocityY, float strength, float windowMps)
    {
        if (!(strength > 0f) || !(windowMps > 0f))
            return 1f;
        float u = Mathf.Min(1f, Mathf.Abs(velocityY) / windowMps);
        float w = 1f - (u * u * (3f - 2f * u));       // 1 - smoothstep(u)
        return 1f - strength * w;
    }

    /// <summary>The coyote/buffer forgiveness after one tick: the two timers to carry forward, and
    /// whether a jump fires. <c>Jumped</c> is the caller's cue to set <c>velocity.Y</c> to
    /// <see cref="JumpVelocity"/> — this function owns the decision, never the velocity.</summary>
    public readonly record struct JumpResolution(float CoyoteRemaining, float JumpBufferRemaining,
        bool Jumped);

    /// <summary>
    /// <b>Coyote time and jump buffering, as one pure function of the previous tick's timers.</b>
    /// The shipped rule, extracted verbatim from <see cref="Step"/> (MOVE-3) for the reason
    /// <see cref="ControlLockedBy"/> gives: <see cref="Step"/> needs a live
    /// <c>CharacterBody3D</c>, so this decision was provable only in the slowest test tier — and in
    /// practice had no coverage at all.
    ///
    /// <list type="bullet">
    /// <item><b>Coyote</b> refills to <see cref="CoyoteTimeSec"/> on any grounded tick and runs
    /// down by <paramref name="dt"/> otherwise. A successful jump zeroes it, which is what stops it
    /// double-serving a second jump inside the same window.</item>
    /// <item><b>The buffer</b> runs down by <paramref name="dt"/> and is re-armed to
    /// <see cref="JumpBufferSec"/> by a press <i>edge</i> only — so a player who holds the key
    /// through a landing gets nothing, and a jump requires a fresh press. Deliberate (spec §6.2):
    /// an auto-bounce would make the height of every hop in a held chain a function of when the
    /// player happened to be holding, which is the opposite of tight.</item>
    /// <item><b>A buffered jump fires on the tick after touchdown</b> — 16.7 ms at 60 Hz, as tight
    /// as a fixed-tick simulation can be — because that tick sees <c>grounded</c> true and refills
    /// coyote before the buffer is tested.</item>
    /// </list>
    /// </summary>
    /// <param name="jumpAllowed">The water contract's verdict
    /// (<c>WaterGeometry.JumpAllowed</c>): a swimming body neither arms the buffer nor fires.</param>
    /// <param name="locked">Control lock — incapacitation, the impulse ragdoll, the water
    /// sputter-out (<see cref="ControlLockedBy"/>). See the buffer rule below.</param>
    /// <remarks>
    /// <para><b>A control lock spends the buffer; it does not merely postpone it</b> (MECHANICS §2,
    /// "control lock is total; there is no partial-control state"). Before MRF-C this flag
    /// reached <see cref="StepJumps"/> and was spent only on the AIR half, so the ground half
    /// tested nothing but buffer + coyote + <paramref name="jumpAllowed"/>. <see cref="Step"/>
    /// zeroes <c>effective.Jump</c> while locked, which stops a NEW press — but a buffer armed up
    /// to <see cref="JumpBufferSec"/> (0.30 s, 18 ticks at 60 Hz) BEFORE the lock was still live
    /// and still fired, launching an incapacitated body at <see cref="JumpVelocity"/>. That is
    /// refused here now, and the buffer is CLEARED rather than left to
    /// decay — the same shape <see cref="StepAirJump"/> uses for its counter and
    /// <see cref="StepChain"/> and <see cref="StepVerb"/> use for theirs, so "a press does not
    /// survive a lock" means one thing across the whole tick instead of three that can
    /// disagree.</para>
    /// <para><b>Coyote keeps its ordinary bookkeeping</b> under a lock. It is a fact about ground
    /// contact, not an input: a locked body really is standing on the floor, and zeroing it would
    /// put the timer at odds with the collision result for no gain — nothing can read it into a
    /// jump while the lock holds, because the fire is refused above it.</para>
    /// </remarks>
    public static JumpResolution StepJump(bool grounded, float prevCoyote, float prevJumpBuffer,
        bool jumpEdge, bool jumpAllowed, bool locked, float dt)
    {
        float coyote = grounded ? CoyoteTimeSec : prevCoyote - dt;
        if (locked)
            return new JumpResolution(coyote, 0f, false);
        float jumpBuffer = prevJumpBuffer - dt;
        if (jumpEdge && jumpAllowed)
            jumpBuffer = JumpBufferSec;
        if (jumpBuffer > 0 && coyote > 0 && jumpAllowed)
            return new JumpResolution(0f, 0f, true);
        return new JumpResolution(coyote, jumpBuffer, false);
    }

    /// <summary>
    /// <b>Is this body mid-turnaround-skid?</b> One predicate over the replicated timer, so nothing
    /// anywhere re-derives "am I skidding" from a comparison it could get subtly different — the
    /// pose layer, the prediction comparison and <see cref="Step"/> itself all ask this.
    /// </summary>
    public static bool IsSkidding(in MoveState state) => state.SkidRemaining > 0f;

    /// <summary>
    /// <b>Does a reversal at this instant start a skid?</b> All three conditions, and every one of
    /// them is load-bearing:
    ///
    /// <list type="bullet">
    /// <item><b>Grounded and unlocked.</b> An airborne body has nothing to skid on, and a
    /// control-locked one is not steering at all — and must never enter a state whose deceleration
    /// is below the shove settle floor (see <see cref="SkidDeceleration"/>). Two independent
    /// refusals, each on its own sufficient.</item>
    /// <item><b>Above <see cref="SkidEnterSpeedMps"/>.</b> Only a committed run pays this cost.</item>
    /// <item><b>Alignment at or below <see cref="SkidAlignmentMax"/>.</b> The same normalized dot
    /// <see cref="RateFor"/> computes, so "opposed" means one thing in this file.</item>
    /// </list>
    ///
    /// <para>Pure and public for the reason <see cref="ControlLockedBy"/> gives: <see cref="Step"/>
    /// needs a live <c>CharacterBody3D</c>, so an entry rule left inline would be provable only in
    /// the slowest test tier.</para>
    /// </summary>
    public static bool ShouldEnterSkid(bool grounded, bool locked,
        Vector3 velocity, Vector3 wish)
    {
        if (!grounded || locked)
            return false;
        if (!(wish.LengthSquared() > 0.01f))
            return false;

        var flat = new Vector2(velocity.X, velocity.Z);
        float speed = flat.Length();
        if (!(speed >= SkidEnterSpeedMps) || !float.IsFinite(speed))
            return false;

        var want = new Vector2(wish.X, wish.Z);
        float wantLen = want.Length();
        if (!(wantLen > 1e-4f))
            return false;

        float alignment = flat.Dot(want) / (speed * wantLen);
        return float.IsFinite(alignment) && alignment <= SkidAlignmentMax;
    }

    /// <summary>
    /// <b>The skid's whole state machine, as one pure function of the previous timer.</b> Returns
    /// seconds of skid remaining after this tick; zero means "not skidding", and that single number
    /// is both the flag and the clock.
    ///
    /// <para><b>Entry</b> is <see cref="ShouldEnterSkid"/> and sets the clock to
    /// <see cref="SkidMaxSec"/>. <b>Exit</b> is any of four, checked every tick while skidding:
    /// the clock runs out (the hard cap), ground speed falls to <see cref="SkidExitSpeedMps"/> (the
    /// ordinary end), the player releases the stick (so letting go always returns control, and the
    /// stronger <see cref="Deceleration"/> takes over for the last of the stop), or the body leaves
    /// the floor / loses control. There is no path that holds a player in this
    /// state, which is the property MECHANICS-BIBLE §2 asks for and the reason the cap exists at all
    /// even though flat ground never reaches it.</para>
    ///
    /// <para><b>Re-entry is deliberately not blocked.</b> A player who ends one skid and immediately
    /// reverses again at speed starts another — but they cannot, because the exit floor is 1.2 m/s
    /// and the entry floor is 4.05, so the body must first spend a full acceleration ramp getting
    /// back up to speed. The gap between the two thresholds is the anti-chatter mechanism, exactly as
    /// it is for <c>LocomotionProfile</c>'s gears.</para>
    /// </summary>
    /// <param name="verb">MOVE-5 §3.2 B, the skid's one new clause: <b>a skid cannot start or
    /// continue inside a crouch verb.</b> It carries two rows of §3.3 at once — T15's refusal
    /// (<c>ShouldEnterSkid ∧ Verb == Normal</c>) and T16's cancellation, which IS the whole of §8's
    /// momentum share: entering a Slide sets the timer to zero and touches the velocity vector not
    /// at all, so the slide inherits the skid's magnitude, its direction and every bit of sideways
    /// component it was carrying, and then decelerates it at 6.0 instead of 13.
    ///
    /// <para>Optional, defaulting to <see cref="MoveVerb.Normal"/>, so every pre-existing call site
    /// and every pre-existing test is bit-identical. It lives in this pure function rather than at
    /// the call site so the rule has a home a test can reach without an engine.</para></param>
    public static float StepSkid(float prevRemaining, bool grounded, bool locked,
        Vector3 velocity, Vector3 wish, float dt, MoveVerb verb = MoveVerb.Normal)
    {
        if (!float.IsFinite(prevRemaining) || !float.IsFinite(dt))
            return 0f;
        if (verb != MoveVerb.Normal)
            return 0f;                                  // T15 refusal + T16 cancellation, one line

        // The cap is enforced on the way IN as well as on the way out, so this function is total:
        // it cannot return a timer outside [0, SkidMaxSec] for ANY input, including a value that
        // arrived from somewhere other than its own previous return. NetCodec already clamps the
        // wire field, and this makes that a second line of defence rather than the only one.
        prevRemaining = Mathf.Min(prevRemaining, SkidMaxSec);

        if (prevRemaining > 0f)
        {
            if (!grounded || locked)
                return 0f;
            float left = prevRemaining - dt;
            if (!(left > 0f))
                return 0f;                                  // the hard cap
            if (!(wish.LengthSquared() > 0.01f))
                return 0f;                                  // stick released
            var flat = new Vector2(velocity.X, velocity.Z);
            float speed = flat.Length();
            if (!float.IsFinite(speed) || speed <= SkidExitSpeedMps)
                return 0f;                                  // the ordinary end
            return left;
        }

        return ShouldEnterSkid(grounded, locked, velocity, wish) ? SkidMaxSec : 0f;
    }

    // =============================================================================================
    // MOVE-5 — the verb state machine, the chain and the air jump, as pure functions.
    //
    // All three are public and pure for the reason ControlLockedBy gives: Step needs a live
    // CharacterBody3D, so a rule left inline would be provable only in the slowest test tier — and
    // this wave's rules are exactly the kind (boundaries, precedence, idempotency) that MECHANICS
    // wants proven rather than inspected.
    // =============================================================================================

    /// <summary>The verb and its clock after this tick. <see cref="ClockTicks"/> is
    /// <see cref="VerbClockAirborne"/> on an airborne tick — a distinguished not-counting value,
    /// not a count.</summary>
    public readonly record struct VerbResolution(MoveVerb Verb, byte ClockTicks);

    /// <summary>
    /// <b>Spec §3's whole transition table, as one pure function.</b> Evaluated once per tick in
    /// <see cref="Step"/>, in §3.4's fixed order, and the order is what makes every one of the
    /// seven simultaneous cases deterministic on the server, in owner prediction and inside a
    /// reconciliation replay alike.
    ///
    /// <list type="number">
    /// <item><b>Control lock dominates</b> (T1). Verb <see cref="MoveVerb.Normal"/>, clock zero.
    /// No new state can survive a shove, a freeze or a sputter-out.</item>
    /// <item><b>Law V0 — the grounded test</b> (T2). A body cannot be grounded and airborne at
    /// once, so "every crouch verb requires <c>Grounded</c>" is a data shape rather than a check
    /// somebody must remember, and the duck-walk-off-a-ledge bug family cannot be
    /// written.</item>
    /// <item><b>Water</b> (T3, §4.7). <b>This guard would otherwise have shipped a bug:</b>
    /// <c>WaterGeometry.JumpAllowed</c> denies the jump while swimming, so a wading player holding
    /// the key is grounded, holding, and firing no jump — the crouch trigger's exact
    /// precondition — and the body would tuck in the shallows, which reads as a physics
    /// glitch.</item>
    /// <item><b>A jump fired</b> (T4/T5). <b>The most important precedence in the table:</b> rule 3
    /// runs before rule 4, so a jump edge and a verb entry on the same tick resolve to the jump.
    /// The jump is the one input carrying a hard responsiveness guarantee, and a verb that could
    /// swallow a jump press would cost a frame — which is forbidden outright.</item>
    /// <item><b>The verb transition</b>, T7-T14, per the previous verb.</item>
    /// </list>
    ///
    /// <para><b>Law V1</b> (no <see cref="MoveVerb.Normal"/> while grounded and held past the
    /// window) and <b>law V2</b> (the clock spends on release, so re-entry costs a full fresh
    /// window) are what make §3.5's flicker analysis hold. V2 is enforced by
    /// <see cref="AdvanceVerbClock"/>; V1 by the Slide's exits resolving to Tuck rather than Normal
    /// while the button is still down (T13).</para>
    /// </summary>
    /// <param name="grounded">On-floor at the START of this tick (<c>prev.Grounded</c>), the same
    /// convention gravity, coyote and the skid already use — never a live physics query.</param>
    /// <param name="jumped">Whether rule 3 fired a jump this tick, ground or air.</param>
    /// <param name="velocity">This tick's velocity; only its horizontal part is read.</param>
    /// <param name="stickDir">The sanitized movement direction — the STICK, not the wish, because
    /// the wish's magnitude depends on the verb this function is resolving.</param>
    /// <param name="t">The tuning to resolve against. <b>An explicit parameter rather than a read
    /// of <c>MotorTuning.Current</c>, and that is a test-shape decision, not a style one</b>
    /// (MOVE-4b's standing rule, restated in <c>MotorTuningTests</c>'s class doc): xUnit runs test
    /// classes in parallel and the movement suite reads <c>AvatarMotor</c>'s properties live, so a
    /// test that had to PARK a non-default tuning to exercise the Kick or the chain would surface
    /// as an intermittent red in a different class entirely. Passing the tuning in means every one
    /// of this wave's rules can be swept at any tuning with nothing parked at all — the same shape
    /// MOVE-4c gave <see cref="ApexHangFactor"/>, and the reason it needed no mirror.</param>
    public static VerbResolution StepVerb(in MotorTuning t, MoveVerb prevVerb, byte prevClockTicks,
        bool grounded, bool locked, bool jumped, bool jumpHeld,
        Sail.Game.Water.WaterState water, Vector3 velocity, Vector3 stickDir)
    {
        if (locked)
            return new VerbResolution(MoveVerb.Normal, 0);          // T1
        if (!grounded)
            return new VerbResolution(MoveVerb.Normal, VerbClockAirborne);   // T2, law V0
        if (water != Sail.Game.Water.WaterState.Dry)
            return new VerbResolution(MoveVerb.Normal, 0);          // T3
        if (jumped)
            return new VerbResolution(MoveVerb.Normal, 0);          // T4 / T5

        float speed = FlatSpeed(velocity);

        // T8 — the touchdown tick, recognised by the sentinel the previous (airborne) tick left.
        if (prevClockTicks == VerbClockAirborne)
        {
            if (!jumpHeld)
                return new VerbResolution(MoveVerb.Normal, 0);
            if (t.TouchdownSlideImmediate > 0.5f)
                return new VerbResolution(EntryVerbFor(t, speed), 0);
            // Otherwise the ground hold window starts here: this tick is grounded, held, Normal and
            // fired no jump, which is §4.2's accrual condition exactly.
            return new VerbResolution(MoveVerb.Normal, 1);
        }

        switch (prevVerb)
        {
            case MoveVerb.DuckWalk:
                // T14 — NO transition on a released button. The settle IS the latch, and the brief
                // requires the duck walk to be holdable indefinitely without a held button. Its
                // only exits are a press edge (handled above, as a jump), leaving the floor, a
                // control lock and water.
                return new VerbResolution(MoveVerb.DuckWalk,
                    AdvanceVerbClock(prevClockTicks, jumpHeld));

            case MoveVerb.Tuck:
                if (!jumpHeld)
                    return new VerbResolution(MoveVerb.Normal, 0);          // T9, the pop-up
                return new VerbResolution(MoveVerb.Tuck, AdvanceVerbClock(prevClockTicks, true));

            case MoveVerb.Slide:
            {
                // The release exit is tested FIRST and unconditionally: SlideMinSec blocks only the
                // self-termination, never this. Releasing always exits on the next tick, from any
                // state, at any time — that is what makes the min-duration bound legal under the
                // weight principle, and it is also §3.4's "an implicit settle must never override
                // an explicit input" (so a speed-exit and a release on the same tick go to Normal,
                // not DuckWalk).
                if (!jumpHeld)
                    return new VerbResolution(MoveVerb.Normal, 0);          // T9
                byte clock = AdvanceVerbClock(prevClockTicks, true);
                bool speedExit = speed <= t.SlideExitSpeedMps && clock >= t.SlideMinTicks;
                bool durationExit = clock >= t.SlideMaxTicks;
                if (speedExit || durationExit)
                    return new VerbResolution(
                        SlideSettles(t, velocity, stickDir) ? MoveVerb.DuckWalk : MoveVerb.Tuck, 0);
                return new VerbResolution(MoveVerb.Slide, clock);           // T12 / T13
            }

            default:
            {
                // Normal. Law V2: a release does not merely exit, it SPENDS the window, so re-entry
                // costs a full fresh JumpHoldWindowSec — twelve ticks at the default. That is the
                // anti-chatter mechanism on the button axis.
                if (!jumpHeld)
                    return new VerbResolution(MoveVerb.Normal, 0);
                byte clock = AdvanceVerbClock(prevClockTicks, true);
                if (clock >= t.JumpHoldWindowTicks)
                    return new VerbResolution(EntryVerbFor(t, speed), 0);    // T10 / T11
                return new VerbResolution(MoveVerb.Normal, clock);
            }
        }
    }

    /// <summary>
    /// <b>Which verb a crouch trigger lands in</b> (§4.2). <b>Boundary, stated (MECHANICS §1):</b>
    /// inclusive at the entry speed, matching <see cref="ShouldEnterSkid"/>'s
    /// <c>speed &gt;= SkidEnterSpeedMps</c> exactly, so "at or above" means one thing in this file.
    /// At exactly the threshold you slide.
    ///
    /// <para><b>Both branches land in a defined state, which is the point.</b> There is no input
    /// that produces nothing: above the line a Slide, below it a Tuck. A trigger with a dead zone
    /// on one side of a threshold is how a boundary becomes a dropped input.</para></summary>
    public static MoveVerb EntryVerbFor(in MotorTuning t, float flatSpeed)
        => float.IsFinite(flatSpeed) && flatSpeed >= t.SlideEnterSpeedMps
            ? MoveVerb.Slide
            : MoveVerb.Tuck;

    /// <summary>
    /// <b>Does a slide that ran itself out settle into a duck walk, or brace into a tuck?</b>
    /// (§5.2.) At <see cref="DuckWalkGuaranteed"/> it always settles; otherwise the stick must be
    /// held within <see cref="DuckWalkEntryConeDeg"/> of the direction of travel — the brief's
    /// sentence is "hold <i>forward</i> through the slide", and forward is a condition.
    ///
    /// <para>No stick, or no travel direction to compare against, fails the cone and produces a
    /// Tuck. Never <see cref="MoveVerb.Normal"/>: the button is still down, and law V1 forbids
    /// Normal-while-held.</para></summary>
    public static bool SlideSettles(in MotorTuning t, Vector3 velocity, Vector3 stickDir)
    {
        if (t.DuckWalkGuaranteed > 0.5f)
            return true;
        var flat = new Vector2(velocity.X, velocity.Z);
        var want = new Vector2(stickDir.X, stickDir.Z);
        float speed = flat.Length();
        float wantLen = want.Length();
        if (!(speed > 1e-4f) || !(wantLen > 1e-4f) || !float.IsFinite(speed))
            return false;
        float alignment = flat.Dot(want) / (speed * wantLen);
        if (!float.IsFinite(alignment))
            return false;
        return alignment
            >= Mathf.Cos(Mathf.DegToRad(Mathf.Clamp(t.DuckWalkEntryConeDeg, 0f, 180f)));
    }

    /// <summary><b>Law V2, as the one place the clock advances.</b> A tick without the button
    /// spends the window outright; otherwise the count rises by one, clamped one below
    /// <see cref="VerbClockAirborne"/> so a clock can never reach the airborne sentinel by counting
    /// (a duck walk held for 4.25 s would otherwise do exactly that).</summary>
    public static byte AdvanceVerbClock(byte prevClockTicks, bool jumpHeld)
    {
        if (!jumpHeld)
            return 0;
        if (prevClockTicks >= VerbClockAirborne - 1)
            return VerbClockAirborne - 1;
        return (byte)(prevClockTicks + 1);
    }

    /// <summary>The chain's depth and its one counter after this tick.</summary>
    public readonly record struct ChainResolution(byte Depth, byte TimerTicks);

    /// <summary>
    /// <b>The chain ladder and its decay, as one pure function</b> (spec §6.3, §6.4).
    /// <see cref="MoveState.ChainTimerTicks"/> has exactly one meaning — ticks until the next depth
    /// decrement — and it does the grace window and the gradual decay with one byte.
    ///
    /// <list type="bullet">
    /// <item><b>Reloaded to <see cref="ChainGraceTicks"/> on the touchdown tick</b>, and reloaded
    /// <i>before</i> the accrual reads it, per §6.3's "in all cases the timer is set to the grace on
    /// the following touchdown". That ordering is what makes the buffered chain work at all: a
    /// buffered jump fires on the touchdown tick itself, and reading a stale timer there would make
    /// every mashed hop start a fresh chain.</item>
    /// <item><b>It does not decrement while airborne.</b> A long flight can never break a chain,
    /// which is the athletic reading — you do not stop between bounds — and it means the chain
    /// measures GROUND DWELL, the thing the player actually controls.</item>
    /// <item><b>It does not decrement on a jump tick either.</b> §3.4 rule 4 is evaluated only if
    /// no jump fired, so the decrement lives there and the accrual lives in rule 3. That fixed
    /// order is what makes §3.4's "chain timer reaches 0 and a chained jump fires on the same tick"
    /// row deterministic — the jump wins, and it wins identically on every peer.</item>
    /// <item><b>At zero it takes one level, not the whole chain</b> (§6.4). Fumble one landing and
    /// re-chain inside half a second and you lose exactly one level.</item>
    /// </list>
    ///
    /// <para><b>MECHANICS §4, idempotency:</b> a jump edge cannot double-increment the depth,
    /// because a grounded tick must intervene between any two jumps and the depth changes only on a
    /// jump tick.</para>
    /// </summary>
    /// <param name="t">The tuning to resolve against — see <see cref="StepVerb"/>'s parameter for
    /// why this is passed rather than read off <c>MotorTuning.Current</c>.</param>
    public static ChainResolution StepChain(in MotorTuning t, byte prevDepth, byte prevTimerTicks,
        bool grounded, bool touchdown, bool jumped, bool locked)
    {
        if (locked)
            return new ChainResolution(0, 0);       // §3.2's control-lock row

        int max = ClampCount(t.ChainMaxDepth, 7);
        int depth = Mathf.Min((int)prevDepth, max);
        int timer = prevTimerTicks;

        if (touchdown)
            timer = t.ChainGraceTicks;

        if (jumped)
        {
            if (timer > 0)
            {
                if (depth < max)
                    depth++;                        // the ladder; at the cap, unchanged
            }
            else
            {
                depth = 0;                          // this jump STARTS a chain
            }
            return new ChainResolution((byte)depth, (byte)timer);
        }

        if (grounded && !touchdown)
        {
            if (timer > 0)
                timer--;
            if (timer == 0 && depth > 0)
            {
                depth--;
                timer = t.ChainDecayTicks;
            }
        }
        return new ChainResolution((byte)depth, (byte)timer);
    }

    /// <summary>The velocity after the air jump, the air jumps spent, and whether one
    /// fired.</summary>
    public readonly record struct AirJumpResolution(Vector3 Velocity, byte AirJumpsUsed, bool Fired);

    /// <summary>
    /// <b>The double jump — two variants behind one toggle, and it ships OFF</b> (spec §7).
    /// <see cref="AirJumpMode"/> 0 is the exact no-op and is the shipped default, because a double
    /// jump changes what every gap distance in the playground means and the baseline Talon approved
    /// is the only baseline the lab has.
    ///
    /// <para><b>Variant 1, traditional:</b> <c>velocity.Y</c> is <i>overwritten</i>, which deletes
    /// all accumulated downward momentum in one frame — the single most weightless thing a body can
    /// do. It also works at zero horizontal speed and at any moment of the fall, so it functions as
    /// a mid-air undo. That reading is the whole reason variant 2 exists.</para>
    ///
    /// <para><b>Variant 2, the Kick, is not a second launch — it is a drive.</b> It can be taken
    /// only while <i>descending</i> and only above a committed run, it converts a fall already
    /// accepted into forward speed along the CURRENT heading (the stick cannot turn it, so the arc
    /// becomes more ballistic, not less), and it arrests rather than climbs. It structurally cannot
    /// perform the mid-air undo: 94% of a traditional double jump's reach at 39% of its
    /// altitude.</para>
    ///
    /// <para><b>The runaway the <c>max</c> prevents, stated because the naive version is an
    /// exploit.</b> With a compounding <c>flatSpeed += gain</c> each flight adds up to
    /// <c>1.20 + 0.35 x 9.55 = 4.54 m/s</c> while one grounded tick costs only 0.15 m/s, so speed
    /// grows without bound. <c>max(flatSpeed, sprintWish + gain)</c> caps the Kick's OUTPUT at
    /// <c>sprintWish + gain</c>: <b>the Kick is how you reach the Kick's speed, not how you exceed
    /// it.</b></para>
    /// </summary>
    /// <param name="sprintWish">The body's own current sprint wish. <b>Already scaled by the carry
    /// factor and the water multiplier</b>, deliberately — the same argument
    /// <see cref="AirWishSpeedFloorMps"/> makes for the airborne ceiling: a laden player must not be
    /// able to Kick past their carry-limited speed. At the shipped factors it is
    /// <c>MoveSpeed x SprintMultiplier + ChainDepth x ChainBonusMps</c> exactly, which is §7.2's
    /// formula.</param>
    /// <param name="t">The tuning to resolve against — see <see cref="StepVerb"/>'s parameter for
    /// why this is passed rather than read off <c>MotorTuning.Current</c>.</param>
    public static AirJumpResolution StepAirJump(in MotorTuning t, Vector3 velocity, bool grounded,
        byte prevAirJumpsUsed, bool jumpEdge, bool jumpAllowed, bool locked,
        float sprintWish)
    {
        // Resets on any grounded tick (§7), and on a control lock alongside the verb and the chain
        // (§3.2's control-lock row).
        if (grounded || locked)
            return new AirJumpResolution(velocity, 0, false);

        int mode = ClampCount(t.AirJumpMode, 2);
        int max = ClampCount(t.AirJumpCountMax, 3);
        var used = (byte)Mathf.Min((int)prevAirJumpsUsed, max);
        if (mode <= 0 || !jumpEdge || !jumpAllowed || used >= max)
            return new AirJumpResolution(velocity, used, false);

        if (mode == 1)
        {
            velocity.Y = t.JumpVelocity * t.AirJumpVelocityFraction;
            return new AirJumpResolution(velocity, (byte)(used + 1), true);
        }

        // Mode 2 — the Kick. All three conditions, each load-bearing (§7.2).
        var flat = new Vector2(velocity.X, velocity.Z);
        float flatSpeed = flat.Length();
        if (!(velocity.Y < 0f) || !float.IsFinite(flatSpeed) || !(flatSpeed >= t.KickMinSpeedMps))
            return new AirJumpResolution(velocity, used, false);

        float gain = t.KickHorizontalGainMps + Mathf.Abs(velocity.Y) * t.KickConversionFraction;
        float ceiling = float.IsFinite(sprintWish) ? sprintWish + gain : flatSpeed;
        float next = Mathf.Max(flatSpeed, ceiling);     // a TOP-UP, never a compound add
        Vector2 heading = flat / flatSpeed;             // the CURRENT heading; the stick cannot turn it
        velocity.X = heading.X * next;
        velocity.Z = heading.Y * next;
        velocity.Y = t.KickVerticalMps;                 // arrests the fall, does not climb
        return new AirJumpResolution(velocity, (byte)(used + 1), true);
    }

    /// <summary>Everything one press can do to a body's vertical state in one tick: the velocity
    /// after both jumps, the two forgiveness timers to carry forward, whether the GROUND jump fired
    /// (<see cref="MoveState.Jumped"/>'s wire contract, ground only) and whether the AIR jump did,
    /// and the air-jump counter to carry forward.</summary>
    public readonly record struct JumpsResolution(Vector3 Velocity, float CoyoteRemaining,
        float JumpBufferRemaining, bool Jumped, byte AirJumpsUsed, bool AirJumped)
    {
        /// <summary>Either jump fired — what the chain, the verb and the landing rule ask.</summary>
        public bool AnyJump => Jumped || AirJumped;
    }

    /// <summary>
    /// <b>One press, at most one jump</b> (spec §6.2 and §7) — <see cref="StepJump"/> and
    /// <see cref="StepAirJump"/> composed, with the rule that binds them.
    ///
    /// <para><b>The press edge is a resource and the ground jump spends it.</b> That is the whole
    /// reason this function exists rather than two calls in a row inside <see cref="Step"/>. The
    /// call order there was always right — the ground jump resolves first, so a press a live coyote
    /// window can still turn into an ordinary jump IS an ordinary jump — but ordering alone was
    /// never the guarantee, and <see cref="Step"/> handed the air jump the SAME edge unchanged.
    /// During a coyote window <c>grounded</c> is false (that is what a coyote window is), so
    /// <see cref="StepAirJump"/>'s grounded early-out did not fire either, and at
    /// <c>AirJumpMode</c> 1 the press cost the player twice: <c>velocity.Y</c> was set to
    /// <c>JumpVelocity</c> and then OVERWRITTEN with <c>JumpVelocity x AirJumpVelocityFraction</c>
    /// — 20% weaker — while the counter spent the double jump they had not used. The defect was
    /// invisible for as long as <c>AirJumpMode</c> shipped at 0, its exact no-op; MOVE-8's ruling
    /// of 1 on 2026-08-28 made it live. W6-5, from REVIEW-1 finding 1.</para>
    ///
    /// <para><b>The two spend rules, stated so neither can be mistaken for the other:</b>
    /// <list type="bullet">
    /// <item><b>A fired ground jump withholds the edge from the air jump.</b> One press cannot buy
    /// both. A genuine air jump — pressed with the coyote window already expired, so no ground jump
    /// fires — is untouched: it still launches at exactly
    /// <c>JumpVelocity x AirJumpVelocityFraction</c> and still costs one from the counter.</item>
    /// <item><b>A fired air jump zeroes the buffer.</b> <see cref="StepJump"/> re-armed the buffer
    /// with this same edge; leaving it armed would let one press buy an air jump AND a buffered
    /// ground jump on a touchdown inside the next <see cref="JumpBufferSec"/>. The same idempotency
    /// argument (MECHANICS §4), the other half of the tick — and the ground jump needs no companion
    /// line because <see cref="StepJump"/> already zeroes BOTH timers when it fires.</item>
    /// </list></para>
    ///
    /// <para><b><paramref name="locked"/> is spent on BOTH halves</b> (MRF-C, from the 2026-08-30
    /// master review). It arrives here for the air jump's sake and used to be forwarded only to
    /// <see cref="StepAirJump"/>, which left the ground half able to fire a jump buffered before
    /// the lock out of the middle of it. It now reaches <see cref="StepJump"/> too; that
    /// function's remarks carry the rule.</para>
    ///
    /// <para>Pure and public for the reason <see cref="ControlLockedBy"/> gives:
    /// <see cref="Step"/> needs a live <c>CharacterBody3D</c>, so a composition left inline is
    /// provable only in the slowest test tier — which is exactly how this defect survived with both
    /// halves individually covered.</para>
    /// </summary>
    /// <param name="sprintWish">Passed through to <see cref="StepAirJump"/> unchanged; see that
    /// parameter's documentation for why it arrives already scaled.</param>
    /// <param name="t">The tuning the AIR half resolves against. The ground half is
    /// <see cref="StepJump"/>, which reads <c>CoyoteTimeSec</c> and <c>JumpBufferSec</c> off
    /// <c>MotorTuning.Current</c> rather than off a parameter — a pre-existing asymmetry, stated
    /// here so a caller handing in a tuning that is not <c>Current</c> knows the forgiveness timers
    /// will not follow it.</param>
    public static JumpsResolution StepJumps(in MotorTuning t, Vector3 velocity, bool grounded,
        float prevCoyote, float prevJumpBuffer, byte prevAirJumpsUsed, bool jumpEdge,
        bool jumpAllowed, bool locked, float sprintWish, float dt)
    {
        JumpResolution ground = StepJump(grounded, prevCoyote, prevJumpBuffer, jumpEdge,
            jumpAllowed, locked, dt);
        float jumpBuffer = ground.JumpBufferRemaining;
        if (ground.Jumped)
            velocity.Y = t.JumpVelocity;

        // Spend rule 1. `jumpEdge && !ground.Jumped`, not `jumpEdge` — this term is the whole fix.
        AirJumpResolution air = StepAirJump(t, velocity, grounded, prevAirJumpsUsed,
            jumpEdge && !ground.Jumped, jumpAllowed, locked, sprintWish);
        if (air.Fired)
            jumpBuffer = 0f;                            // spend rule 2

        return new JumpsResolution(air.Velocity, ground.CoyoteRemaining, jumpBuffer,
            ground.Jumped, air.AirJumpsUsed, air.Fired);
    }

    /// <summary>
    /// <b>The crouch-slide's own horizontal integration</b> (spec §4.4) — three rules, and the
    /// third is the one that makes it a slide rather than a skid:
    ///
    /// <list type="number">
    /// <item><b>Speed decays at <c>SlideDeceleration</c> along the current heading.</b> 6.0 at the
    /// default: below ground <see cref="Deceleration"/> (21) and below
    /// <see cref="SkidDeceleration"/> (13). <i>A slide is slippery, and that is the whole
    /// verb.</i></item>
    /// <item><b>The stick contributes no acceleration and no braking.</b> You cannot pump a slide
    /// and you cannot shorten one by letting go of forward; <c>SlideDeceleration</c> is the single
    /// speed authority in this state.</item>
    /// <item><b>The heading ROTATES toward the stick at <c>SlideTurnRateDeg</c></b> — a rotation of
    /// the velocity vector, not a <c>MoveToward</c>, <b>so the carve preserves speed exactly and
    /// cannot fight rule 1.</b> This is the carve the brief's stretch goal asks for, and it is what
    /// distinguishes the slide from the skid, which suppresses steering entirely.</item>
    /// </list>
    ///
    /// <para>The rotation is applied to the heading and the decay to the magnitude, then the two
    /// are recombined once. <b>The faster slide therefore turns LESS per metre</b> — 16.9 deg/m
    /// from a sprint landing against 21.0 deg/m from a minimum entry — which is the weighty reading
    /// of "speed is the dial" and the same shape SKID-1 already uses. A sprint slide runs 5.89 m in
    /// 1.107 s and can round a right-angle corner with 10 degrees to spare; a minimum-entry slide
    /// can only adjust a line.</para>
    ///
    /// <para>Pure and public for the reason <see cref="ControlLockedBy"/> gives, and tuning-passed
    /// for the reason <see cref="StepVerb"/>'s parameter gives.</para>
    /// </summary>
    public static Vector3 StepSlide(in MotorTuning t, Vector3 velocity, Vector3 wish, float dt)
    {
        var flat = new Vector2(velocity.X, velocity.Z);
        float speed = flat.Length();
        if (!float.IsFinite(speed) || !(speed > 1e-4f) || !float.IsFinite(dt))
        {
            velocity.X = 0f;
            velocity.Z = 0f;
            return velocity;
        }

        float heading = Mathf.Atan2(flat.Y, flat.X);
        if (wish.LengthSquared() > 0.01f)
        {
            float want = Mathf.Atan2(wish.Z, wish.X);
            float delta = Mathf.Wrap(want - heading, -Mathf.Pi, Mathf.Pi);
            float step = Mathf.DegToRad(Mathf.Max(0f, t.SlideTurnRateDeg)) * dt;
            heading += Mathf.Clamp(delta, -step, step);
        }
        float next = Mathf.MoveToward(speed, 0f, t.SlideDeceleration * dt);
        velocity.X = Mathf.Cos(heading) * next;
        velocity.Z = Mathf.Sin(heading) * next;
        return velocity;
    }

    /// <summary>Horizontal speed, non-finite folded to zero. One spelling, so "how fast is this
    /// body going along the ground" cannot mean two subtly different things across this file.</summary>
    private static float FlatSpeed(Vector3 velocity)
    {
        float speed = new Vector2(velocity.X, velocity.Z).Length();
        return float.IsFinite(speed) ? speed : 0f;
    }

    /// <summary>
    /// <b>Where the body is pointing after this tick.</b> Pure, for the same reason
    /// <see cref="ControlLockedBy"/> is: the facing policy is the thing MOVE-1 changed and a rule
    /// living inside <see cref="Step"/> would be provable only in the slowest test tier.
    ///
    /// <para><paramref name="faceYaw"/> wins when it is present, and it turns at
    /// <see cref="AimTurnLerp"/>; otherwise the body faces its travel direction at
    /// <see cref="TurnLerp"/>, exactly as it always has, and holds its last facing when standing
    /// still.</para>
    /// </summary>
    public static float ResolveYaw(float prevYaw, Vector3 wish, float? faceYaw, float dt)
    {
        if (faceYaw is float aimed && float.IsFinite(aimed))
            return Mathf.LerpAngle(prevYaw, aimed, Mathf.Min(1f, AimTurnLerp * dt));
        if (wish.LengthSquared() > 0.05f)
            return Mathf.LerpAngle(prevYaw, Mathf.Atan2(-wish.X, -wish.Z),
                Mathf.Min(1f, TurnLerp * dt));
        return prevYaw;
    }

    public static Vector3 SanitizeMoveDir(Vector3 dir)
    {
        if (!float.IsFinite(dir.X) || !float.IsFinite(dir.Z))
            return Vector3.Zero;
        dir.Y = 0; // movement input is a flat direction by contract
        if (dir.LengthSquared() > 1f)
            dir = dir.Normalized();
        return dir;
    }

    /// <summary>Clamps a client-reported carry-encumbrance factor into the legal range.</summary>
    public static float SanitizeSpeedFactor(float speedFactor)
    {
        if (!float.IsFinite(speedFactor))
            return 1f;
        return Mathf.Clamp(speedFactor, MinSpeedFactor, 1f);
    }

    /// <summary>Non-finite -&gt; 0. Aim yaw is periodic (only ever consumed through Cos/Sin via
    /// AimQuery.DirectionFromYawPitch), so — unlike pitch — no range clamp is needed, only a
    /// finiteness guard against a malformed packet.</summary>
    public static float SanitizeAimYaw(float yaw) => float.IsFinite(yaw) ? yaw : 0f;

    /// <summary>Non-finite -&gt; 0, then clamped into [SandboxCamera.PitchMin, PitchMax] — the
    /// exact range the local camera rig itself enforces, so a doctored packet can never claim
    /// an aim angle no legitimate camera could ever report (see PitchMin/PitchMax's doc comment
    /// for why this is a shared constant, not a duplicated one).</summary>
    public static float SanitizeAimPitch(float pitch)
    {
        if (!float.IsFinite(pitch))
            return 0f;
        return Mathf.Clamp(pitch, SandboxCamera.PitchMin, SandboxCamera.PitchMax);
    }
}
