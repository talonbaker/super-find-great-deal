using System.Collections.Generic;
using MpFoundation.Net;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>Named feel presets for the movement playground (2026-08-28, Talon's session).</b> Fourteen
/// whole tunings across two banks — the number row, and SHIFT plus the number row — each carrying a
/// sentence saying <i>what you are supposed to be feeling</i> and a sentence saying <i>what to do to
/// feel it</i>.
///
/// <para><b>Why this exists.</b> The knob panel is 58 sliders and it answers "what happens if I move
/// this one". It does not answer the question Talon actually arrived with — <i>what is this
/// movement, as a whole, and what are the alternatives?</i> A slider cannot answer that, because
/// feel is a property of a whole tuning and not of any row in it: "heavy" is gravity AND
/// acceleration AND air control AND the landing dip, moving together, and a player who nudges
/// gravity alone gets a jump that is merely worse rather than a body that is heavier. <b>The unit
/// of a feel test is a tuning, not a knob.</b></para>
///
/// <para><b>Every preset is derived from <see cref="MotorTuning.Default"/> or from one of the named
/// fragments below, never from the live tuning.</b> That is the load-bearing decision in this file.
/// Derived-from-live would make a preset mean something different depending on what you pressed
/// before it, which is precisely the bug that makes A/B testing by feel worthless — you would be
/// comparing preset 2 against "preset 2 after preset 5" and could not tell. Pressing any key always
/// lands in the same place. <b>Presets do not compose by accident, and that is the feature</b>; the
/// SHIFT bank composes them <i>on purpose</i>, out of the same fragments, so a blend cannot drift
/// from the two things it claims to be a blend of.</para>
///
/// <para><b>Bank two exists because bank one worked.</b> Talon's first pass ruled 6 (FORGIVING),
/// 8 (KICK) and 9 (DOUBLE JUMP) his three favourites, with 5 (STRIDE) "interesting" but running
/// away by the end. Those are not competitors — <see cref="Forgiving"/> is a ground-and-forgiveness
/// tuning while the air jumps are an airborne verb, so they are orthogonal and can simply be added
/// together. The SHIFT bank is that 2&#215;2: which air jump, times chain on or off, on the
/// FORGIVING base. It is a designed experiment rather than four more opinions.</para>
///
/// <para><b>Every value is inside its knob's declared Min/Max</b> and every preset survives
/// <see cref="MotorTuning.Validate"/> unchanged, so what the banner names is what the motor runs.
/// <c>MovementPresetTests</c> sweeps all of them. <b>They do breach pins and invariants, and must</b>
/// — see that class for why, and for the standing consequence: nothing here is a shipping proposal
/// without revisiting the tests and derived profiles it breaks.</para>
/// </summary>
/// <summary>Which modifier reaches a preset. Three banks, because one number row could not hold
/// the questions this session generated and renumbering an existing bank would invalidate every
/// note already taken against it.</summary>
public enum PresetBank
{
    /// <summary>The number row alone — the single-axis probes.</summary>
    Plain,

    /// <summary>SHIFT — the blends, the speed ladder and the skip.</summary>
    Shift,

    /// <summary>CTRL — the unified button grammar (2026-08-28).</summary>
    Ctrl,
}

public static class MovementPresets
{
    /// <summary>
    /// One named tuning, plus the two sentences that make it testable by a human.
    /// </summary>
    /// <param name="Key">The number-row digit that selects it.</param>
    /// <param name="Bank">Which modifier reaches it.</param>
    /// <param name="Name">Short, shouted, and what a note will refer to it by.</param>
    /// <param name="Feel">What you should be feeling. Written as a claim Talon can disagree with —
    /// "you have mass" is testable, "improved game feel" is not.</param>
    /// <param name="Try">The specific thing to DO to feel it. A preset without this is a preset
    /// that gets pressed, looked at, and shrugged off.</param>
    /// <param name="Tuning">The whole tuning.</param>
    /// <param name="ChargeJump"><b>An INPUT SCHEME, not a tuning row</b>, which is why it rides on
    /// the preset rather than inside <see cref="MotorTuning"/>. Press-to-wind-up / release-to-jump
    /// lives in <see cref="ChargeJumpIntentSource"/> at the intent boundary; no knob can express
    /// it, and putting it in the tuning would have meant a wire-format change for a dev
    /// experiment.</param>
    public readonly record struct Preset(
        int Key, PresetBank Bank, string Name, string Feel, string Try, MotorTuning Tuning,
        bool ChargeJump = false);

    /// <summary>
    /// <b>Both banks, in press order.</b>
    ///
    /// <para><b>Built lazily on first access, and that is a correctness fix rather than a
    /// performance one.</b> As a static field initialiser (<c>All { get; } = Build();</c>) this
    /// ran in TEXTUAL order with the other static fields in this class — and it is declared above
    /// the fragments <see cref="Build"/> reads, so <see cref="Forgiving"/> was still
    /// <c>default(MotorTuning)</c>, every field zero, at the moment the blends were composed from
    /// it. The result was five presets with <c>MoveSpeed = 0</c>: a body that cannot move, seated
    /// silently, under a banner naming a tuning it was not running. <c>MovementPresetTests</c>
    /// caught it; nothing in the running scene would have, short of pressing the key and finding
    /// the character frozen.</para>
    ///
    /// <para>A null-coalescing initialiser cannot be ordered wrongly, because it runs on first
    /// USE rather than at type-initialisation time. Keep it this way if a fragment is ever
    /// added.</para>
    /// </summary>
    public static IReadOnlyList<Preset> All => _all ??= Build();

    private static IReadOnlyList<Preset>? _all;

    /// <summary>The preset for a digit in a bank, or null if that combination is unbound.</summary>
    public static Preset? ForKey(int digit, PresetBank bank)
    {
        foreach (var p in All)
            if (p.Key == digit && p.Bank == bank)
                return p;
        return null;
    }

    /// <summary>The label a banner uses for a preset's key — "6", "SHIFT+2", "CTRL+5".</summary>
    public static string KeyLabel(in Preset p) => p.Bank switch
    {
        PresetBank.Shift => $"SHIFT+{p.Key}",
        PresetBank.Ctrl => $"CTRL+{p.Key}",
        _ => p.Key.ToString(),
    };

    // --- The fragments the blends are built from -------------------------------------------------
    //
    // Named once and reused, so a blend cannot drift from the preset it claims to contain. If
    // FORGIVING is retuned, every blend built on it moves with it, which is the only way a 2x2
    // experiment stays a 2x2 experiment.

    /// <summary>Talon's favourite of the first pass, unchanged: both forgiveness timers at their
    /// generous end, a touch of hang, and real air authority.</summary>
    private static readonly MotorTuning Forgiving = MotorTuning.Default with
    {
        Gravity = 24f,
        FallGravityMultiplier = 1.50f,
        ApexHangStrength = 0.30f,
        JumpReleaseGravityMultiplier = 4.00f,
        CoyoteTimeSec = 0.30f,
        JumpBufferSec = 0.30f,
        AirControlBuild = 0.70f,
        AirControlTurn = 0.60f,
        AirControlBrake = 0.55f,
    };

    /// <summary>The Kick: a mid-air jump that trades altitude for reach. Air-jump rows ONLY, so it
    /// can be laid over any base without smuggling a second opinion in with it.</summary>
    private static MotorTuning WithKick(MotorTuning t) => t with
    {
        AirJumpMode = 2f,
        AirJumpCountMax = 1f,
        KickConversionFraction = 0.35f,
        KickHorizontalGainMps = 2.00f,
        KickVerticalMps = 2.20f,
        KickMinSpeedMps = 3.00f,
    };

    /// <summary>The traditional double jump. Air-jump rows only, same rule as the Kick.</summary>
    private static MotorTuning WithDoubleJump(MotorTuning t) => t with
    {
        AirJumpMode = 1f,
        AirJumpCountMax = 1f,
        AirJumpVelocityFraction = 0.80f,
    };

    /// <summary>
    /// The chain jump, TAMED. Talon's first-pass verdict on the loud version was "interesting
    /// concept, I like the way it feels, but it goes way too fast by the end after a few
    /// chainings" — so the ladder is shorter and each rung is worth half of what it was.
    ///
    /// <para>The arithmetic behind the change, because "it felt fast" deserves a number: the loud
    /// version ran 4 rungs at 1.60 m/s over an 8.64 m/s sprint, which tops out at
    /// <b>15.04 m/s</b> — nearly double a sprint, and past the point where the ground acceleration
    /// can even reach the wish before the next takeoff. This runs 3 rungs at 0.80, topping out at
    /// <b>11.04 m/s</b>: still clearly a reward, still climbing visibly rung by rung, but inside
    /// the speed the body can actually deliver.</para>
    /// </summary>
    private static MotorTuning WithStride(MotorTuning t) => t with
    {
        ChainBonusMps = 0.80f,
        ChainMaxDepth = 3f,
        ChainGraceSec = 0.55f,
        ChainDecayIntervalSec = 0.80f,
    };

    /// <summary>
    /// <b>The one axis fourteen presets never touched.</b> Ground speed, and with it the sprint,
    /// the skid entry, the slide entry and the duck walk — all four of those are declared as
    /// FRACTIONS of <see cref="MotorTuning.MoveSpeed"/>, so this single row moves the whole gear
    /// ladder together and keeps their relationships intact.
    ///
    /// <para><b>Why it matters more than it looks.</b> The classic greybox is <b>1.20 m tall</b>
    /// (<c>ClassicGreyboxAvatarBody.CrownM</c>), and the shipped sprint is 5.4 &#215; 1.6 =
    /// <b>8.64 m/s</b> — which is <b>7.2 body-lengths per second</b>. An Olympic sprinter manages
    /// about 4.4. This body moves at roughly 1.6&#215; the relative speed of the fastest human
    /// there has ever been, and it does it while a walk-cycle greybox reads as a child.</para>
    ///
    /// <para><b>Not one preset in banks one or two moved this row</b>, which is why no A/B in
    /// either bank could ever have surfaced it: they all shared the same ground speed, so
    /// "everything felt a bit fast" was true of every single one and attributable to none. A
    /// blind spot in a set of controls is invisible in exactly this way — it does not produce a
    /// wrong answer, it produces a question that cannot be asked.</para>
    ///
    /// <para><b>The confound, stated rather than hidden:</b> jump reach is speed &#215; airtime,
    /// so lowering this makes every authored gap harder and raising it makes them easier. Judge
    /// these four on the FLOW course, which is the playable half; the CALIBRATION course exists to
    /// bracket the shipped reach and will simply report that the reach changed.</para>
    /// </summary>
    private static MotorTuning WithSpeed(MotorTuning t, float moveSpeed) =>
        t with { MoveSpeed = moveSpeed };

    /// <summary>Body-lengths per second at a sprint, the scale-invariant way to say "fast".
    /// Printed into each speed preset's own banner so the number travels with the feel.</summary>
    private static string BodyLengths(float moveSpeed)
    {
        const float crownM = 1.20f;   // ClassicGreyboxAvatarBody.CrownM
        float sprint = moveSpeed * MotorTuning.Default.SprintMultiplier;
        return $"{sprint:F2} m/s sprint = {sprint / crownM:F1} body-lengths/sec";
    }

    /// <summary>
    /// <b>The settled base: FORGIVING + DOUBLE JUMP at 3.8 m/s, sprint intact, chain off.</b>
    /// Every ruling Talon has made this session, in one tuning.
    ///
    /// <para><b>3.8 is measured, not chosen.</b> The speed ladder ran 3.8 / 4.4 / 5.4 / 6.4 with
    /// every other row pinned and he ruled <i>"SHIFT+5 feels the best"</i> against a control that
    /// was the shipped value. On a 1.20 m body that is 5.1 body-lengths/sec at a sprint, down from
    /// the 7.2 that shipped.</para>
    ///
    /// <para><b>The chain sits at its exact no-op</b> (<c>ChainBonusMps</c> inherits 0.00 from the
    /// default), so speed here is flat and predictable — no rungs, nothing accumulating across
    /// jumps. That is Talon's "more streamlined", and it is the right default for testing anything
    /// else, because a tuning that quietly gains speed underneath a test contaminates the test.</para>
    /// </summary>
    private static MotorTuning RollBase => WithSpeed(WithDoubleJump(Forgiving), 3.8f);

    private static List<Preset> Build() => new()
    {
        // ===== BANK ONE — the number row ==========================================================

        // 0 — THE BASELINE. Always reachable, always first, and the only one that is not an
        // opinion. Every other preset is a claim ABOUT this one.
        new(0, PresetBank.Plain, "SHIPPED",
            "The game as it stands today. Not heavy, not floaty — the middle that every other "
          + "preset here is an argument against.",
            "Press 0 between the others. The comparison is the measurement; a preset felt in "
          + "isolation tells you nothing.",
            MotorTuning.Default),

        // 1 — HEAVY. Mass in every axis at once.
        //
        // REVISED after Talon's first pass: "jump is too low to make it up the stairs". True, and
        // it was a flaw in the preset rather than a property of heaviness — gravity went to 30
        // while JumpVelocity stayed at the shipped 8.4, which drops the apex to 1.18 m against the
        // 1.60 m every ledge in these courses is authored against. So the preset was not testing
        // "heavy", it was testing "cannot reach the geometry", and the verdict it collected was
        // about the second thing. JumpVelocity now rides to 9.8, which restores the apex to
        // 1.60 m EXACTLY (9.8^2 / 2*30) while leaving every other heavy row alone.
        //
        // The lesson generalises and is worth keeping: a preset that changes gravity must move
        // JumpVelocity with it, or it silently changes what the level means as well as how the
        // body feels, and no note taken in it can tell the two apart.
        new(1, PresetBank.Plain, "HEAVY",
            "You have mass. Reaching speed takes ground, stopping takes ground, and a jump is a "
          + "decision you cannot revise once your feet leave. The apex is the same height as "
          + "shipped — only the WAY you get there is heavy.",
            "Sprint at a gap and try to change your mind halfway across; you can't. Then stop dead "
          + "from a sprint and see how far past the mark you go.",
            MotorTuning.Default with
            {
                Gravity = 30f,
                JumpVelocity = 9.8f,
                FallGravityMultiplier = 1.80f,
                Acceleration = 5.0f,
                Deceleration = 12.0f,
                TurnAcceleration = 18f,
                TurnLerp = 6f,
                AirControlBuild = 0.20f,
                AirControlTurn = 0.15f,
                AirControlBrake = 0.12f,
                CoyoteTimeSec = 0.08f,
                JumpBufferSec = 0.08f,
                SkidDeceleration = 8.0f,
                TakeoffKickSec = 0.24f,
                CameraDipStrengthM = 0.22f,
            }),

        // 2 — FLOATY. Ruled "way too floaty, hard to control, not good" and DELIBERATELY LEFT
        // ALONE. It is the far wall of the space, and a wall that gets moved every time someone
        // bounces off it stops telling you where the room ends. Its job is done: it is now the
        // known-bad end of the gravity axis, and 4 and 6 are read against it.
        new(2, PresetBank.Plain, "FLOATY",
            "Low gravity and near-total steering. The top of a jump is a place you live in rather "
          + "than a point you cross. RULED TOO FLOATY — kept as the far wall of the space, not as "
          + "a candidate.",
            "Go here when a preset feels 'a bit floaty' and you want to know how much room is "
          + "actually left in that direction. This is the end of it.",
            MotorTuning.Default with
            {
                Gravity = 11f,
                FallGravityMultiplier = 1.00f,
                JumpVelocity = 7.0f,
                ApexHangStrength = 0.55f,
                ApexHangWindowMps = 3.00f,
                AirControlBuild = 0.85f,
                AirControlTurn = 0.75f,
                AirControlBrake = 0.60f,
                CoyoteTimeSec = 0.20f,
                JumpBufferSec = 0.20f,
            }),

        // 3 — SNAPPY. REVISED: "acceleration far too fast on the initial, stopping too quick."
        // Acceleration 28 -> 16 and Deceleration 45 -> 28. Both are still well above shipped
        // (9 and 21), so the preset is still the responsive end of the axis; it is no longer the
        // instantaneous end, which was reading as teleporting rather than as responsive.
        new(3, PresetBank.Plain, "SNAPPY",
            "Responsive. You reach speed quickly and shed it quickly, and the jump goes up fast "
          + "and comes down faster — without the on/off quality the first version had.",
            "Tap SPACE, then hold SPACE, on the same flat ground. The gap between those two jumps "
          + "is wider here than anywhere else — if you can't feel it here, it isn't there.",
            MotorTuning.Default with
            {
                Acceleration = 16f,
                Deceleration = 28f,
                TurnAcceleration = 70f,
                TurnLerp = 28f,
                Gravity = 30f,
                JumpVelocity = 9.8f,
                FallGravityMultiplier = 1.90f,
                JumpReleaseGravityMultiplier = 5.00f,
                AirControlBuild = 0.60f,
                AirControlTurn = 0.55f,
                AirControlBrake = 0.50f,
                CoyoteTimeSec = 0.10f,
                JumpBufferSec = 0.14f,
                TakeoffKickSec = 0.08f,
                SkidDeceleration = 26f,
                SkidMaxSec = 0.25f,
            }),

        // 4 — HANG, ISOLATED. The single-variable control. Ruled "the best so far, though 0 is
        // still better by a little" — which is exactly the verdict this preset is shaped to be able
        // to collect, and the reason it moves two rows and nothing else.
        new(4, PresetBank.Plain, "HANG (isolated)",
            "Exactly the shipped game with ONE thing changed: gravity nearly stops near the top of "
          + "the arc. Everything else is byte-identical to preset 0.",
            "Stand at one gap. Press 0, jump. Press 4, jump. Repeat on the SAME gap — this is a "
          + "controlled A/B and it is worthless anywhere else.",
            MotorTuning.Default with
            {
                ApexHangStrength = 0.65f,
                ApexHangWindowMps = 2.50f,
            }),

        // 5 — STRIDE. The chain jump. REVISED down per the first pass; see WithStride for the
        // arithmetic. Still the one mechanic in this wave that ships at its exact no-op, so this
        // is the only way to feel it at all.
        new(5, PresetBank.Plain, "STRIDE (chain jump)",
            "Speed you earn by NOT stopping. Every hop landed inside the grace window adds a rung "
          + "and each rung raises your top speed. At the shipped tuning this mechanic is switched "
          + "completely off. Tamed since the first pass: 3 rungs to 11.0 m/s, not 4 to 15.0.",
            "Hop continuously in a straight line without pausing. Watch the speed climb rung by "
          + "rung, then deliberately break the rhythm and watch it fall. THE RHYTHM IS THE "
          + "MECHANIC.",
            WithStride(MotorTuning.Default with
            {
                // A chain is a wish-speed rise, so the ground must be able to REACH it before the
                // next takeoff and the body must not shed it on landing. Without these two the
                // ladder climbs on paper and nothing changes underfoot.
                Acceleration = 16f,
                Deceleration = 8.0f,
                // Air control down: a chain is a reward for a committed line, and full mid-air
                // steering lets you chain while wandering, which is the same as not earning it.
                AirControlTurn = 0.25f,
                AirControlBrake = 0.10f,
            })),

        // 6 — FORGIVING. Talon's favourite of the first pass, unchanged and now the base every
        // blend in bank two is built on.
        new(6, PresetBank.Plain, "FORGIVING",
            "Very hard to miss a jump. Run off a ledge late and it still fires; press early and it "
          + "still fires. YOUR FAVOURITE of the first pass, and the base of the whole SHIFT bank.",
            "Make mistakes ON PURPOSE: jump a beat AFTER you've left the edge, and press SPACE "
          + "well BEFORE you land. Both should just work.",
            Forgiving),

        // 7 — CROUCH GRAMMAR. REVISED: "the slide is a bit much." SlideMaxSec 2.20 -> 1.40 (the
        // shipped value) and SlideDeceleration 2.5 -> 5.0, so a slide now ends when it runs out of
        // speed rather than when it runs out of clock. The rest of the grammar is untouched,
        // because the grammar is what this preset is for.
        new(7, PresetBank.Plain, "CROUCH GRAMMAR",
            "The crouch verbs, exaggerated until you cannot miss them. Holding SPACE is a whole "
          + "input language and this preset shouts every word of it. The slide is shorter than the "
          + "first version — it now ends on speed rather than on a timer.",
            "Sprint, jump, and KEEP HOLDING SPACE through the landing. You slide. Keep holding: it "
          + "settles into a duck walk and STAYS there. You will not jump again until you let go "
          + "and press again. Now do it at walking pace — you tuck instead of sliding.",
            MotorTuning.Default with
            {
                TouchdownSlideImmediate = 1f,
                JumpHoldWindowSec = 0.12f,
                SlideEnterSpeedFraction = 0.80f,
                SlideExitSpeedMps = 1.00f,
                SlideDeceleration = 5.0f,
                SlideTurnRateDeg = 200f,
                SlideMaxSec = 1.40f,
                DuckWalkGuaranteed = 1f,
                DuckWalkSpeedFraction = 0.55f,
            }),

        // 8 and 9 — the two air-jump variants on the SHIPPED base, so the pair stays a clean
        // comparison of the two verbs and nothing else. Both ruled favourites.
        new(8, PresetBank.Plain, "KICK (air jump)",
            "The twisted double jump: a mid-air kick that buys DISTANCE instead of height — "
          + "measured at 94% of the reach for 39% of the altitude.",
            "Jump, and while moving fast press SPACE again. Then press 9 and do the identical "
          + "thing on the same gap. This pair is how you pick.",
            WithKick(MotorTuning.Default with { ApexHangStrength = 0.20f })),

        new(9, PresetBank.Plain, "DOUBLE JUMP",
            "The traditional one: a second, slightly smaller jump straight up from wherever you "
          + "are. Height, not distance.",
            "Same test as preset 8, same gap, same approach speed. The difference between 8 and 9 "
          + "is the only thing either preset is for.",
            WithDoubleJump(MotorTuning.Default)),

        // ===== BANK TWO — SHIFT + the number row ==================================================
        //
        // The 2x2 the first pass earned: FORGIVING (6) as the base, times which air jump (8 or 9),
        // times whether the chain (5) is armed. Four presses, and every pair of them differs in
        // exactly one thing, which is what makes a verdict from them mean something.
        //
        //            no chain          + chain
        //   Kick     SHIFT+1           SHIFT+3
        //   Double   SHIFT+2           SHIFT+4

        new(1, PresetBank.Shift, "FORGIVING + KICK",
            "Your two favourites at once: the forgiving ground game with the Kick in the air. This "
          + "is the leading candidate, not an experiment.",
            "Play it the way you'd play the game. Then press SHIFT+2 — the ONLY difference is "
          + "which air jump you get.",
            WithKick(Forgiving)),

        new(2, PresetBank.Shift, "FORGIVING + DOUBLE JUMP",
            "The same forgiving ground game with the traditional double jump instead of the Kick. "
          + "Height where the other one gives you reach.",
            "Same course, same gaps, straight after SHIFT+1. One variable apart — pick one.",
            WithDoubleJump(Forgiving)),

        new(3, PresetBank.Shift, "FORGIVING + KICK + STRIDE",
            "All three: forgiving ground, the Kick in the air, and the chain ladder armed so a "
          + "clean unbroken run keeps getting faster.",
            "Chain hops in a line to build speed, THEN jump a long gap and kick. The question is "
          + "whether the chain makes the Kick better or just makes it harder to aim.",
            WithStride(WithKick(Forgiving))),

        new(4, PresetBank.Shift, "FORGIVING + DOUBLE JUMP + STRIDE",
            "The fourth corner: forgiving ground, traditional double jump, chain armed. The "
          + "highest-verb tuning in the lab.",
            "Same as SHIFT+3, same route. If neither chain version is better than its no-chain "
          + "twin, the chain is not earning its rung and that is a real finding.",
            WithStride(WithDoubleJump(Forgiving))),

        // ===== BANK THREE — SHIFT+5..8, the speed ladder ==========================================
        //
        // Talon, after bank two: "everything was going a bit too fast in general... everything felt
        // fine... I'm actually not sure what I feel is slightly off." That is the signature of a
        // variable no control in the set varies: every preset felt fine BECAUSE the thing that is
        // off is common to all of them.
        //
        // So this bank holds the winner of bank two (SHIFT+2, FORGIVING + DOUBLE JUMP) completely
        // fixed and moves ONE row: MoveSpeed. SHIFT+7 is that winner untouched, which makes it the
        // control rather than a fifth opinion — press it in the middle of the ladder to check
        // whether the ladder is doing anything at all.

        new(5, PresetBank.Shift, "SPEED 70% — 6.1 m/s sprint",
            "The winning tuning at roughly two-thirds speed. " + BodyLengths(3.8f) + ". Around a "
          + "real sprinter's relative pace for the first time.",
            "Run the Flow course. Gaps will be HARDER — reach is speed x airtime — so judge the "
          + "feel of travelling, not whether you clear things.",
            WithSpeed(WithDoubleJump(Forgiving), 3.8f)),

        new(6, PresetBank.Shift, "SPEED 82% — 7.0 m/s sprint",
            "The same tuning a notch down. " + BodyLengths(4.4f) + ". The likeliest landing spot "
          + "if 'too fast' is real but 70% overshoots it.",
            "Straight after SHIFT+5 and SHIFT+7, on the same stretch of ground. This is a "
          + "three-point comparison, not a single test.",
            WithSpeed(WithDoubleJump(Forgiving), 4.4f)),

        new(7, PresetBank.Shift, "SPEED 100% — 8.6 m/s sprint (CONTROL)",
            "SHIFT+2 exactly, unchanged — the shipped ground speed. " + BodyLengths(5.4f)
          + ". An Olympic sprinter manages about 4.4 on a 1.8 m frame; this is a 1.20 m body.",
            "The control. If this feels indistinguishable from SHIFT+5, the speed is not what is "
          + "bothering you and the answer is somewhere else — which is itself worth knowing.",
            WithDoubleJump(Forgiving)),

        new(8, PresetBank.Shift, "SPEED 119% — 10.2 m/s sprint",
            "Faster than shipped. " + BodyLengths(6.4f) + ". Included so the ladder has a rung "
          + "ABOVE the current value.",
            "Press it once and go back to SHIFT+7. A ladder with no upper rung cannot tell you "
          + "whether shipped is near a boundary or sitting in the middle of a wide plateau.",
            WithSpeed(WithDoubleJump(Forgiving), 6.4f)),

        // ===== SHIFT+9 — THE SKIP =================================================================
        //
        // REBUILT after Talon's first pass, and the first version was wrong in an instructive way.
        //
        // What he said: "Players like to jump while they're roaming around because it's fun... they
        // would hop because they're bored initially but they would keep hopping because skipping
        // was fun and it was just a rhythm they could get into. The reason I don't like SHIFT+9 is
        // because there's that initial jump which makes them feel like the initial jump is broken.
        // Slowing them down and making them feel like the jump overall is tiny and weak."
        //
        // THE ERROR: the first version dropped JumpVelocity from 8.4 to 4.6 to make a skip cadence
        // reachable. That made EVERY jump small — including the very first one, the idle hop a
        // bored player takes before they know a rhythm exists. A mechanic whose invitation is "hop
        // around for fun" cannot open by making the hop feel broken. The preset destroyed the thing
        // it was built to enable.
        //
        // THE FIX, and it needed no new machinery at all: JumpVelocity goes back to the shipped 8.4
        // and the CADENCE COMES FROM THE PLAYER'S THUMB instead. The release cut already does this
        // — hold SPACE and you get the full 1.6 m jump; tap it and JumpReleaseGravityMultiplier
        // steepens gravity on the rise and you get a hop of about 0.4 m with roughly a third of a
        // second in the air. That IS a skip cadence, and it was reachable at the shipped tuning the
        // whole time. So:
        //
        //     hold SPACE  -> a full, satisfying jump. Nothing has been taken away.
        //     tap SPACE   -> a low quick hop.
        //     tap in rhythm -> the chain ladder climbs and you get faster.
        //
        // Which is also what a real skip is: the entry is an ordinary jump and the hops that follow
        // are small. The player is never told to jump differently — they discover that tapping
        // repeatedly is fast, and the mechanic is the discovery.
        //
        // Base speed is 3.8, Talon's own pick from the ladder. Six rungs at 0.80 tops out at
        // 8.6 m/s, which is the sprint speed that used to be free — so the chain does not merely
        // add speed, it hands back exactly what the slower base gave up, to a player who earns it.
        new(9, PresetBank.Shift, "SKIP (rhythm)",
            "Roam and hold SPACE for a normal full jump - nothing is taken away. Then start TAPPING "
          + "space in a rhythm: the hops go low and quick, and every one landed in time adds a rung "
          + "that makes you faster. Six rungs takes you back to the old sprint speed, earned.",
            "Just hop around like you are bored. Then keep hopping, in time, and watch the speed "
          + "climb and the SKIP line alternate L R L R. Hold SPACE any time you want the big jump "
          + "back - it is still there, unchanged.",
            WithSpeed(WithDoubleJump(Forgiving), 3.8f) with
            {
                // NOT lowered. This is the whole correction: the big jump stays big.
                JumpVelocity = 8.4f,
                // The tap is what makes the hop small, and the player owns it. 3.50 is firm enough
                // that a flick of the thumb is decisively a hop rather than a half-jump, without
                // going so steep that a slightly-late release falls off a cliff.
                JumpReleaseGravityMultiplier = 3.50f,
                // Hang stretches a hop, and a stretched hop cannot hold a tempo. Kept small rather
                // than zero so the FULL jump still has some float at its top.
                ApexHangStrength = 0.10f,
                // Generous, because rhythmic tapping is early far more often than it is late.
                JumpBufferSec = 0.25f,
                ChainBonusMps = 0.80f,
                ChainMaxDepth = 6f,
                ChainGraceSec = 0.35f,
                ChainDecayIntervalSec = 0.50f,
            }),

        // ===== SHIFT+0 — THE SKIP, WITH THE RAMP REMOVED ==========================================
        //
        // A ONE-ROW CONTROLLED TEST against SHIFT+9, and the hypothesis it exists to kill or confirm.
        //
        // Talon on SHIFT+9: "there's an initial pause where the player loses speed and this is not
        // what I want them to feel. I want a jump to feel good if they only do one jump."
        //
        // THE MECHANISM, read out of the motor rather than guessed. AvatarMotor.AirborneWishSpeed
        // enforces spec §2.3 — "nobody gains speed in the air":
        //
        //     desired = min(requested, max(flatSpeed, groundWish))
        //
        // While airborne your wish is capped by the speed you ALREADY CARRY, floored at the
        // non-sprint ground wish. So a jump taken before the ground ramp has finished freezes you
        // part-way up it: you cannot keep accelerating until you land. At Acceleration = 9 with a
        // sprint target of 3.8 x 1.6 = 6.08 m/s, that ramp takes about 0.68 SECONDS. Every jump
        // inside that window loses the acceleration it would have had.
        //
        // And the first jump is doubly exposed: MomentumGranted is false at chain depth 0, so hop
        // one is also the only hop where the ceiling can actively brake speed off rather than
        // merely refuse to add it. Hop two onward has the grant and holds what it has.
        //
        // THE TEST: this preset is SHIFT+9 with ONE ROW MOVED — Acceleration 9 -> 24, which shortens
        // the ramp from ~0.68 s to ~0.25 s so a jump can barely catch it mid-climb. Everything else
        // is byte-identical, so if the pause goes away the cause is the ramp and the fix is a knob;
        // if it does not, the cause is the airborne ceiling itself and the fix is motor work.
        // Either answer is worth the keystroke, which is what makes this a test rather than a guess.
        new(0, PresetBank.Shift, "SKIP, NO RAMP (A/B vs SHIFT+9)",
            "SHIFT+9 with exactly one number changed: how fast you reach top speed on the ground "
          + "(9 -> 24 m/s^2). If the 'initial pause' was the acceleration ramp being frozen by the "
          + "jump, it should be gone here. If it still feels the same, the cause is deeper and I "
          + "know where to look next.",
            "Do the SAME thing you just did: roam, then take ONE jump from a standing or "
          + "just-started run. Then press SHIFT+9 and take the same single jump. This is a "
          + "one-variable A/B - the ONLY question is whether the pause is still there.",
            WithSpeed(WithDoubleJump(Forgiving), 3.8f) with
            {
                JumpVelocity = 8.4f,
                JumpReleaseGravityMultiplier = 3.50f,
                ApexHangStrength = 0.10f,
                JumpBufferSec = 0.25f,
                ChainBonusMps = 0.80f,
                ChainMaxDepth = 6f,
                ChainGraceSec = 0.35f,
                ChainDecayIntervalSec = 0.50f,
                // THE ONE ROW UNDER TEST.
                Acceleration = 24f,
            }),

        // ===== BANK FOUR — CTRL, THE ROLL ==========================================================
        //
        // REPLACED 2026-08-28. This bank was the wind-up grammar (press to crouch, release to jump)
        // and Talon rejected it outright: "I hate all of them... there is no wind up." The reason
        // was structural rather than a tuning miss and is written up in the playtest notes —
        // JumpHoldWindowSec decides BOTH when the crouch becomes visible AND when the jump is lost,
        // so the crouch could only ever appear at the instant the jump was already gone. Fixing
        // that needs those two decoupled in AvatarMotor, which is motor work and not a preset.
        //
        // The keys are reused because the grammar bank is dead; the notes keep what it was.
        //
        // WHAT THIS BANK IS NOW: the roll, on the SHIPPED jump button, with sprint intact.
        //
        //     hold SPACE through a landing  ->  slide  ->  a roll that does not end
        //     a fresh press                 ->  jump out of it
        //
        // THREE RULINGS ARE BAKED IN HERE, all Talon's, all from this session:
        //
        //   1. SPRINT STAYS. "Let's keep the sprint button." The no-sprint experiment is closed;
        //      SprintMultiplier is back at the shipped 1.6 across the whole bank.
        //   2. THE CHAIN IS OFF. "When you jump, regardless of how you land or jump again, you end
        //      up gaining speed... the player keeps that speed and it just doesn't feel right, I
        //      would rather have it more streamlined." He is right about the mechanism, and it is
        //      worse than he thinks: §6.3's accrual rule consults only WHETHER you jumped again
        //      in time, and nothing whatever about HOW you jumped. It rewards REPETITION, not
        //      skill, so mashing earns exactly what rhythm earns. ChainBonusMps is back at its
        //      0.00 no-op here and speed is a flat, predictable thing again.
        //      (Stated without naming the state fields on purpose: spec §6.6 forbids any chain
        //      readout and ChainNoUiAuditTests sweeps this directory TEXTUALLY, so even a comment
        //      quoting them fails the audit. It caught this exact line.)
        //   3. NO WIND-UP. The jump button is the shipped jump button. No decorator, no charge.
        //
        // So the only variable left in this bank is the roll itself, which is what he asked to
        // test. Base is his settled tuning: FORGIVING + DOUBLE JUMP at 3.8 m/s.

        new(0, PresetBank.Ctrl, "NO ROLL (control)",
            "The settled tuning with the shipped crouch: holding SPACE through a landing gives you "
          + "a slide that runs out and stands you back up. Sprint works. No chain, no wind-up.",
            "Press CTRL+0 first and hold SPACE through a fast landing. Feel where the slide ENDS - "
          + "that ending is the thing the next two presets remove.",
            RollBase),

        // CTRL+1 — THE SURF. Pair it with K (slope momentum) on the SURF PARK course.
        //
        // Talon wants shield-surfing / hoverboard flow: "surfing is fluid... it's a nice rhythm to
        // be carving in and out." Every game he named for it is fundamentally about GRADIENTS, and
        // the motor is flat-world, so the slope term is a harness probe (see
        // MovementPlayground.UpdateSlopeMomentum) and this is the tuning that lets it be felt.
        //
        // DECELERATION IS THE WHOLE PRESET. The slope probe adds gravity's along-slope component to
        // velocity after the motor runs; the motor then decelerates that surplus back toward its
        // wish at Deceleration m/s^2, which is exactly the shape of friction. So the two numbers are
        // in a straight race:
        //
        //     downhill pull = Gravity * sin(angle)     friction = Deceleration
        //
        // At the shipped Deceleration of 21 NO reachable gradient wins — 22 m/s^2 of gravity needs
        // a 73 degree slope to make 21, and Godot stops calling anything past 45 a floor at all. The
        // effect would be perfectly invisible and would read as "the prototype does not work". At
        // 2.5 the break-even is about 6.5 degrees, so the 8 degree ramp is roughly neutral and every
        // steeper one accelerates. That is why the ladder starts at 8.
        //
        // The rest is a board rather than a body: no chain, no air jump to confuse a line, a long
        // steerable roll, and enough turn rate to carve.
        new(1, PresetBank.Ctrl, "SURF (needs K + the Surf Park)",
            "The roll, tuned so gravity can actually beat friction. Deceleration drops from 21 to "
          + "2.5, which puts break-even at about a 6.5 degree slope - so on the Surf Park's ladder "
          + "everything from 15 degrees up should genuinely accelerate you. Press K to switch the "
          + "slope term on, and TAB to the Surf Park.",
            "TAB to SURF PARK, press K, then drop in and hold SPACE. Ride the 8/15/22/30/38 ramps "
          + "back to back and find where it starts paying. Then try to carve a line round the BOWL "
          + "without losing it. The readout's SLOPE row says GAINING or losing, live.",
            RollBase with
            {
                // Friction. The one number that decides whether any of this is visible.
                Deceleration = 2.5f,
                Acceleration = 12f,
                // A board holds a line; it does not pivot. Low turn authority is what makes a carve
                // a carve rather than a strafe.
                TurnLerp = 5f,
                TurnAcceleration = 14f,
                TouchdownSlideImmediate = 1f,
                SlideEnterSpeedFraction = 0.50f,
                SlideExitSpeedMps = 1.20f,
                SlideDeceleration = 2.0f,
                SlideMinSec = 0.10f,
                SlideMaxSec = 0.30f,
                SlideTurnRateDeg = 150f,
                DuckWalkGuaranteed = 1f,
                DuckWalkSpeedFraction = 1.00f,
                DuckWalkEntryConeDeg = 180f,
                // Air control down hard: leaving a lip should commit you to the line you left on,
                // which is most of what makes a jump off a slope feel earned.
                AirControlBuild = 0.25f,
                AirControlTurn = 0.20f,
                AirControlBrake = 0.10f,
            }),
    };
}
