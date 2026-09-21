using Godot;

namespace MpFoundation.Net;

/// <summary>
/// <b>Which crouch verb this body is in</b> (MOVE-5, spec §3.1). Four values, two bits, and the
/// enum IS the duck walk's latch — there is no separate "latched" flag, because the difference
/// between <see cref="Tuck"/> and <see cref="DuckWalk"/> <i>is</i> the exit rule (§5.1).
///
/// <para><b>Law V0</b> (§3.2): every crouch verb requires <c>Grounded</c>, and leaving the floor
/// forces <see cref="Normal"/> on the tick <c>Grounded</c> goes false. That is expressed as a data
/// shape rather than a check somebody has to remember, and at no cost it kills the whole family of
/// bugs in which a duck-walking body walks off a ledge and stays ducked.</para>
///
/// <para>Ordinals are the wire encoding (<c>NetCodec</c>'s <c>flags2</c> bits 0-1, protocol v13).
/// All four two-bit values are legal, so — unlike the water and incapacity folds beside them —
/// there is no reserved ordinal a doctored packet could land on.</para>
/// </summary>
public enum MoveVerb : byte
{
    /// <summary>Standing, walking, running or sprinting — the three <i>gears</i> are readings of
    /// this one value, not states of their own (§3.1). Also the skid's verb.</summary>
    Normal = 0,

    /// <summary><b>Braced.</b> The stationary crouch, and the below-speed outcome of every crouch
    /// entry. <b>Held</b>: <c>!JumpHeld</c> pops it in one tick, unconditionally.</summary>
    Tuck = 1,

    /// <summary>The crouch-slide (§4). Speed decays at <c>SlideDeceleration</c> along the current
    /// heading, the heading carves toward the stick, and the stick contributes no acceleration and
    /// no braking.</summary>
    Slide = 2,

    /// <summary><b>Settled.</b> The one <b>latched</b> verb — a released button does NOT exit it
    /// (§3.3 T14). Entered by exactly one edge, a slide that settles (§5.2), which is what makes
    /// §3.5's Slide-DuckWalk no-cycle result structural rather than numeric.</summary>
    DuckWalk = 3,
}

/// <summary>
/// The complete simulation state of one avatar at one fixed tick — everything
/// <see cref="AvatarMotor.Step"/> needs to reproduce the next tick exactly. Snapshots of
/// this struct are what the server broadcasts and what the owning client rewinds to and
/// replays from during reconciliation, so every field that influences movement (including
/// the coyote/jump-buffer forgiveness timers) must live here, not in node-private fields.
/// </summary>
public struct MoveState
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Yaw;
    public float CoyoteRemaining;
    public float JumpBufferRemaining;

    /// <summary>
    /// <b>Seconds of turnaround skid left</b> (SKID-1, 2026-08-16); zero means not skidding, and the
    /// one number is both the flag and the hard cap's clock. Written only by
    /// <see cref="AvatarMotor.StepSkid"/>; ask <see cref="AvatarMotor.IsSkidding"/> rather than
    /// comparing it by hand.
    ///
    /// <para><b>It is in this struct because it changes where the body ends up, not how it looks.</b>
    /// While it is non-zero the horizontal velocity is pulled toward zero at
    /// <see cref="AvatarMotor.SkidDeceleration"/> and the movement intent is ignored, so a skid held
    /// in a node-private field would be a slide the owning client simulated and the server did not —
    /// and reconciliation would snap the player out of it every snapshot. Same argument
    /// <see cref="CoyoteRemaining"/> makes for the jump forgiveness, and the same one
    /// <c>STATE-CASCADE-TABLE.md</c> hard constraint 1 makes in general.</para>
    ///
    /// <para>A float on the wire rather than a bit, and it costs the snapshot four bytes
    /// (<c>NetCodec</c>, protocol v11): both flags bytes were already documented full, a bit could
    /// not carry the cap's clock, and a quantized byte would let an adopted authoritative timer
    /// disagree with the server's by a fraction of a tick — which is exactly the kind of small
    /// divergence that ends a skid one tick early on the client and re-corrects the position.</para>
    /// </summary>
    public float SkidRemaining;

    /// <summary>On-floor result of the previous MoveAndSlide — the gravity/coyote input
    /// for the next step. Carried in state (not queried live) so a replay that has just
    /// teleported the body does not read a stale physics flag.</summary>
    public bool Grounded;

    /// <summary>
    /// Water state (W2, lake-water contract §4). Lives here rather than in a node field for the
    /// reason this struct's own doc gives: it influences movement, and hysteresis makes it
    /// <i>history-dependent</i>, so a reconciliation replay that started from a different water
    /// state would resolve a different one and re-diverge forever. <see cref="AvatarMotor.Step"/>
    /// derives it from the resulting position each tick, which means owner prediction, server
    /// authority and replay all compute it identically from identical inputs with no extra
    /// plumbing and no client-reported value ever trusted.
    /// </summary>
    public Sail.Game.Water.WaterState Water;

    /// <summary>
    /// Soaked (lake-water contract §7): move speed x 0.9 until dried off in a heat source's warmth.
    /// Unlike <see cref="Water"/> this is <b>not</b> derivable from position — the server sets
    /// it at a sputter-out and clears it after 20 s of warmth — so it is written by
    /// <c>WaterService</c> on the server and rides the snapshot's flags byte to every client.
    /// It is in <c>MoveState</c> because it scales movement, and prediction has to see the same
    /// number the authority used or every soaked step mispredicts and re-corrects forever (the
    /// exact failure the carry-encumbrance comment in <c>SandboxAvatar.OwnerTick</c> describes).
    /// <see cref="AvatarMotor.Step"/> passes it through untouched.
    /// </summary>
    public bool Soaked;

    /// <summary>
    /// Control is locked (lake-water contract §6): the player is inside a sputter-out and is
    /// not steering. Set and cleared by <c>WaterService</c> on the server, replicated in the
    /// snapshot's flags byte, and consumed by <see cref="AvatarMotor.Step"/>, which discards the
    /// movement intent entirely while it is true. Carried in state, not in a node field, so a
    /// replay never re-grants control for the ticks the server had it locked — "dead and
    /// player-controlled is a bug" (MECHANICS-BIBLE §2) generalises to every control lock, and
    /// this is the mechanism that makes it structurally impossible rather than merely intended.
    /// </summary>
    public bool ControlLocked;

    /// <summary>
    /// What this body IS — Active, Knocked Out or Frozen (phase 1c, beta plan §10). Server-owned:
    /// <c>IncapacitationService</c> is the only writer, and it writes through
    /// <c>SandboxAvatar.ServerCommitIncapacity</c>.
    ///
    /// <para><b>It lives here for the reason this struct's own doc gives, and there was never an
    /// alternative.</b> <c>STATE-CASCADE-TABLE.md</c> hard constraint 1: anything that influences
    /// how the body moves must be in <c>MoveState</c> or a reconciliation replay diverges. This
    /// influences movement about as hard as anything can — <see cref="AvatarMotor.Step"/> discards
    /// the entire intent while it is not <c>Active</c> — so a node-private flag would have handed
    /// the owning client its steering back for every replayed tick. That is precisely the
    /// controllable-ragdoll defect this repo has already shipped once, and this field is the
    /// structural reason it cannot come back through the reconciliation door.</para>
    ///
    /// <para>Two bits on the wire (<c>NetCodec</c>, protocol v10). One of the four values is
    /// reserved for a third state; see <c>IncapacityState</c>.</para>
    /// </summary>
    public Sail.Game.Failure.IncapacityState Incapacity;

    /// <summary>
    /// A momentary, self-clearing impulse ragdoll — the Breaker's arm-swing (beta plan §8.1,
    /// §10). <b>Not incapacitation</b>: it does not scatter carried items, does not detach the
    /// camera, and does not count toward the all-incapacitated loss condition. It denies steering
    /// for its own short duration and nothing else.
    ///
    /// <para>A separate bit rather than a reuse of <see cref="ControlLocked"/>, and that is a
    /// decision worth stating: <c>WaterService</c> writes <c>ControlLocked</c> unconditionally
    /// every server tick from its own sputter-out phase, so a second writer would have its value
    /// stomped within one frame. Two owners, two bits — and the presentation layer can then tell
    /// "swallowed by the lake" apart from "bowled over", which one shared bit could not.</para>
    /// </summary>
    public bool ImpulseRagdoll;

    // --- MOVE-5: the verb state machine (spec §10.1, protocol v13) ------------------------------
    // All five ride the snapshot for the reason SkidRemaining's comment above gives and
    // STATE-CASCADE-TABLE.md hard constraint 1 states in general: each of them changes where the
    // body ends up, so a replay that started from a different value resolves a different trajectory
    // and re-diverges forever. Three bytes total (flags2 + two tick counters); zero input bits,
    // because every verb here is carried by MoveIntent.Jump, JumpHeld and MoveDir, all three
    // already on the wire.

    /// <summary>
    /// <b>Which crouch verb is active</b> (MOVE-5, spec §3). Two bits of the snapshot's
    /// <c>flags2</c> byte. It changes the wish speed, the deceleration authority and the steering
    /// rule, which is the identical argument <see cref="SkidRemaining"/> makes for itself.
    ///
    /// <para>Written only by <see cref="AvatarMotor.StepVerb"/>. The duck walk's latch is this
    /// value and nothing else (§10.1: "the latch IS the enum value"), so there is no second field
    /// that could disagree with it.</para>
    /// </summary>
    public MoveVerb Verb;

    /// <summary>
    /// <b>One shared clock, in whole ticks</b> (spec §10.1): the ground hold window while
    /// <see cref="Verb"/> is <see cref="MoveVerb.Normal"/>, and the slide's min and max duration
    /// while it is <see cref="MoveVerb.Slide"/>. The three are mutually exclusive by construction,
    /// which is what makes one byte legal.
    ///
    /// <para><b>A quantized byte where <see cref="SkidRemaining"/> refused one</b>, and §10.3 is
    /// the argument: this counter is an <i>integer by construction</i> — set from a knob at
    /// <c>round(sec x 60)</c>, decremented by exactly 1 per fixed tick, compared against integers —
    /// so a byte holds it EXACTLY and is in fact more robust than a float, which would accumulate
    /// dt-addition error over a long duck walk. <c>SkidRemaining</c> is decremented by <c>dt</c>
    /// and compared against a continuous speed threshold, so a byte would round it. <b>The one
    /// dependency, stated so it cannot be broken silently:</b> this holds because
    /// <c>AvatarMotor.TickDelta</c> is a fixed 1/60 on every peer. If it ever becomes variable,
    /// this and <see cref="ChainTimerTicks"/> must become floats.</para>
    ///
    /// <para><b><see cref="AvatarMotor.VerbClockAirborne"/> (255) is a distinguished value, not a
    /// count</b> — see that constant. It is how the touchdown tick is recognised without a second
    /// field or a second wire bit.</para>
    /// </summary>
    public byte VerbClockTicks;

    /// <summary>
    /// <b>Consecutive jumps beyond the first</b> (spec §6.2): hop 1 is depth 0, hop 2 is depth 1.
    /// Three bits of <c>flags2</c>, which is exactly why
    /// <c>MotorTuningKnobs.ChainMaxDepth</c>'s max of 7 is a hard clamp in the validator rather
    /// than a bound on a widget — a slider past it would not produce a bad feel, it would produce a
    /// truncated field and a peer whose chain depth disagrees with the server's.
    ///
    /// <para>It raises the <i>wish</i> speed, never the velocity (§6.1) — a takeoff velocity bonus
    /// does not survive the flight, which the spec proves by arithmetic. It also drives §12's
    /// animation tell for free, exactly as the skid pose reads <see cref="SkidRemaining"/>.</para>
    /// </summary>
    public byte ChainDepth;

    /// <summary>
    /// <b>Ticks until the next <see cref="ChainDepth"/> decrement</b> — one counter doing both the
    /// grace window and the gradual decay (§6.4). Set to <c>round(ChainGraceSec x 60)</c> on the
    /// touchdown tick, decremented on every grounded tick, and <b>not decremented while
    /// airborne</b>, so a long flight can never break a chain and the chain measures ground dwell,
    /// which is the thing the player actually controls. At zero it takes a level off and reloads to
    /// <c>round(ChainDecayIntervalSec x 60)</c>; at depth 0 it stops.
    ///
    /// <para>It is live simultaneously with <see cref="VerbClockTicks"/> — the chain decays during
    /// a slide — so the two cannot be folded into one byte. Checked, not assumed (§10.1).</para>
    /// </summary>
    public byte ChainTimerTicks;

    /// <summary>
    /// <b>Air jumps spent this flight</b> (spec §7). Two bits of <c>flags2</c>; resets to 0 on any
    /// grounded tick. A counter rather than a bool so <c>AirJumpCountMax</c> can be a real knob,
    /// and replicated so <b>a replay can never re-grant a spent air jump</b> — MECHANICS §4's
    /// idempotency rule, in the one place in this struct where it has teeth.
    ///
    /// <para>Zero at the shipped tuning, always: <c>AirJumpMode</c> ships at 0, the exact no-op.
    /// It is also one of the two gates on §9's momentum grant, which is why that clause is provably
    /// inert until Talon drags a slider.</para>
    /// </summary>
    public byte AirJumpsUsed;

    public static MoveState AtSpawn(Vector3 position) => new() { Position = position };

    /// <summary>
    /// <b>What a remote proxy adopts out of an authoritative snapshot</b> (MOVE-5e). A proxy never
    /// runs <see cref="AvatarMotor.Step"/>, so its <c>MoveState</c> is otherwise frozen at spawn
    /// forever — and that state is what every presentation reader on every role consults
    /// (<c>SandboxAvatar.AnimateVisual</c>, <c>SyncIncapacitySkin</c>, <c>UpdateNameplate</c>, and
    /// the <c>VerbNow</c> / <c>ChainDepthNow</c> seam the animation tells pose from).
    ///
    /// <para><b>The rule is ADOPT BY DEFAULT, and that direction is the whole point of this
    /// function existing.</b> The proxy branch used to be a hand-written list of the fields somebody
    /// had remembered, which meant every new field on this struct started life un-adopted and stayed
    /// that way until a teammate's body was visibly wrong — <see cref="SkidRemaining"/> was added to
    /// that list one wave late, and the five MOVE-5 verb fields plus
    /// <see cref="Incapacity"/>/<see cref="ImpulseRagdoll"/> were never added at all. Inverting the
    /// default retires the bug class rather than its latest instance: a field added tomorrow is
    /// replicated to proxies with no edit here, and the only way to get it wrong is to
    /// <i>deliberately</i> add it to the exclusion list below.</para>
    ///
    /// <para><b>The exclusion list is exactly four fields, and they share one reason:</b>
    /// <see cref="Position"/>, <see cref="Velocity"/>, <see cref="Yaw"/> and <see cref="Grounded"/>
    /// are the fields a proxy already receives through <c>SnapshotBuffer</c>, resolved at the
    /// RENDER tick — roughly two snapshots behind the newest one. Adopting the newest values here
    /// as well would give the same fact two disagreeing carriers on one role, which is the defect
    /// shape this function exists to prevent, pointed the other way. <c>RemoteFrame</c> is their
    /// single reader and their single writer; this function keeps its hands off them.</para>
    ///
    /// <para><b>Everything adopted is taken from the NEWEST snapshot, not the render tick</b>, and
    /// that is a deliberate, pre-existing trade rather than an oversight. The water, soaked and skid
    /// facts already worked this way: they are discrete — there is no such thing as half-soaked or
    /// half-a-verb to interpolate toward — so they are read off the latest authority the peer holds.
    /// The cost is that a discrete fact leads the interpolated body by the buffer delay. It is
    /// bounded, it is under a tenth of a second at the shipped 30 Hz broadcast, and it is the same
    /// lead the brake pose has shipped with since SKID-1.</para>
    ///
    /// <para>Pure and static so it can be tested field by field without a Godot runtime — see
    /// <c>ProxyAdoptionTests</c>, which fails if a field is ever quietly added to the exclusion
    /// list.</para>
    /// </summary>
    /// <param name="current">The proxy's state as it stands (supplies the excluded fields).</param>
    /// <param name="snapshot">The newest authoritative state this peer has received.</param>
    public static MoveState AdoptForProxy(in MoveState current, in MoveState snapshot)
    {
        MoveState adopted = snapshot;
        adopted.Position = current.Position;
        adopted.Velocity = current.Velocity;
        adopted.Yaw = current.Yaw;
        adopted.Grounded = current.Grounded;
        return adopted;
    }
}

/// <summary>
/// One-shot facts about a single <see cref="AvatarMotor.Step"/>, for the presentation
/// layer only. The step itself never plays SFX or triggers visuals — reconciliation
/// replays steps dozens of times a second and those replays must be silent. The caller
/// consumes these events only when a tick is simulated for the first time.
/// </summary>
public readonly struct StepEvents
{
    /// <summary>Jump fired this step (buffer + coyote both open).</summary>
    public bool Jumped { get; init; }

    /// <summary>Largest non-floor impact speed carried into an obstacle this step (0 = none).</summary>
    public float BumpImpact { get; init; }

    /// <summary>World position of that impact (for 3D bump SFX).</summary>
    public Vector3 BumpPosition { get; init; }

    /// <summary><b>What was bumped</b> — the collider of the hardest non-floor slide contact this
    /// step, or null (PHYS-1, 2026-09-20, ruling P1: "a Resting prop touched by a moving body — a
    /// held prop, a Loose prop, an avatar — wakes").
    ///
    /// <para><b>An output, never state.</b> <see cref="StepEvents"/> is what a step REPORTS;
    /// <see cref="MoveState"/> is what it carries forward, and only the latter is replayed by
    /// reconciliation. Putting the collider here rather than in the state is what keeps a
    /// prediction replay from waking a prop a second time: the server consumes this on its own
    /// authoritative step and a client's predicted step consumes nothing, because the wake funnel
    /// is server-gated (<c>PropManager.ServerBumpProp</c>).</para>
    ///
    /// <para>Godot's own <c>KinematicCollision3D.GetCollider</c> returns <c>GodotObject</c>; it is
    /// kept as that here so this file — the pure movement foundation — needs to know nothing
    /// about props.</para></summary>
    public GodotObject? BumpCollider { get; init; }

    /// <summary>Downward speed carried into MoveAndSlide this step while airborne;
    /// the presentation layer maxes this across a fall to scale the landing squash.</summary>
    public float FallSpeed { get; init; }
}
