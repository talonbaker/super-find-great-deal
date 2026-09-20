using System;

namespace Sail.Game.Failure;

/// <summary>
/// What a player's body IS, as far as every other system is concerned. One machine, two skins
/// (beta plan §10) — and deliberately room for a third.
///
/// <para><b>The ordinals are a wire contract.</b> This enum rides two bits of the snapshot flags
/// byte (<c>NetCodec</c>, protocol v10), so the values are load-bearing and must never be
/// reordered or renumbered. Two bits hold four values; three are spent and <b>3 is reserved</b>
/// for the third state Talon may or may not add (beta plan §3.2 leaves "does Dead survive as a
/// category" explicitly to him — see PR #187's draft, which is written to read correctly under
/// either answer). Adding it later costs a doc line and a <c>StateForCause</c> row; it costs no
/// wire bits, no packet-length change and no restructuring. That headroom is the point.</para>
/// </summary>
public enum IncapacityState : byte
{
    /// <summary>Upright, steering, playing the game. The only state in which input reaches
    /// <c>AvatarMotor.Step</c>.</summary>
    Active = 0,

    /// <summary>Flat on your back with cartoon birds circling. Recoverable by a teammate's shake
    /// or by dawn.</summary>
    KnockedOut = 1,

    /// <summary>A comic solid block of ice. Recoverable by thawing at a lit fire or by dawn, and
    /// draggable by a teammate in the meantime.</summary>
    Frozen = 2,

    // 3 = RESERVED. See the class doc. Do not use without Talon's stamp on the §10 fork.
}

/// <summary>
/// What put a player into an <see cref="IncapacityState"/>. This is the <b>cause interface</b>
/// beta plan §10 asks for: none of the five creatures that will raise these exist yet (Long One
/// is phase 3a, Starer 3c, the stinging swarm 3d, Breaker 4a), so the causes ship first and the creatures
/// call in later. Only <see cref="NightWaterChill"/> has a live production caller today.
///
/// <para>Ordinals are replicated (they select the injury mark and the presentation skin), so they
/// are append-only, exactly like <c>WaterEventKind</c>.</para>
/// </summary>
public enum IncapacityCause : byte
{
    None = 0,

    /// <summary>The Long One ran you down. Real collision contact, never a distance check
    /// (BEHAVIOR-BIBLE §7 and the repo's own headbutt history). Phase 3a. → Knocked Out.</summary>
    LongOneContact = 1,

    /// <summary>The Breaker's arm-swings, accumulated past <see cref="IncapacityRules.ImpulseHitsToKnockout"/>.
    /// Phase 4a. → Knocked Out.</summary>
    BreakerRampage = 2,

    /// <summary>Stung once too often. Phase 3d. → Knocked Out.</summary>
    WaspStingStack = 3,

    /// <summary>The lake's cold collected you after dark. The one cause with a live caller —
    /// <c>WaterService</c>'s existing chill clock and sputter-out are the input, and they already
    /// deposit the body on the shore before this fires (see
    /// <c>IncapacitationService.OnSputterRecovered</c>). → Frozen.</summary>
    NightWaterChill = 4,

    /// <summary>You looked at the Starer and it looked back. Phase 3c. → Frozen — the same state
    /// as the lake's, which is the whole point of "one machine, two skins".</summary>
    StarerGaze = 5,

    /// <summary>Test/lab only: the headless suite and the dev labs need a door into every state
    /// while the creatures that will open it for real do not exist. Never raised by production
    /// code. Kept as a named cause rather than reusing a creature's, so a debug entry can never
    /// be mistaken for a real one in telemetry or in an injury history.</summary>
    Debug = 6,
}

/// <summary>How a player got back up. Reported with the recovery so presentation, telemetry and
/// the bus photo can tell "a friend came for me" apart from "I sat there until sunrise".</summary>
public enum IncapacityExit : byte
{
    None = 0,

    /// <summary>A teammate shook you awake (<see cref="IncapacityRules.ShakeSecondsToWake"/>).</summary>
    TeammateShake = 1,

    /// <summary>You thawed beside a lit fire (<see cref="IncapacityRules.ThawSecondsAtFire"/>).</summary>
    FireThaw = 2,

    /// <summary>Dawn came. It always does — this exit can never be refused, disabled or tuned
    /// away (MECHANICS-BIBLE §10.5, beta plan §4.1).</summary>
    Dawn = 3,
}

/// <summary>
/// One step of <see cref="IncapacitationMachine.Advance"/>. At most one boundary is crossed per
/// call — the same discipline <c>SputterSequence.Advance</c> uses, and for the same reason: a
/// 999-second hitch must not let a body skip straight through a state it was supposed to occupy.
/// </summary>
public enum IncapacityStep : byte
{
    /// <summary>Nothing crossed a boundary this call.</summary>
    None = 0,

    /// <summary>A teammate's shake completed — the player is Active again.</summary>
    Woke = 1,

    /// <summary>The thaw timer completed at a fire — the player is Active again.</summary>
    Thawed = 2,

    /// <summary>A momentary impulse ragdoll ended on its own. NOT a recovery from
    /// incapacitation — an impulse ragdoll was never incapacitation (beta plan §10).</summary>
    ImpulseEnded = 3,

    /// <summary>Impulse hits accumulated past the stacking threshold and became a real
    /// Knocked Out. This is the moment the Breaker's shoves stop being comic and start
    /// counting.</summary>
    StackedToKnockout = 4,
}

/// <summary>
/// The persistent comic marks a run leaves on a player, for the bus photo (beta plan §4.4,
/// phase 2c). Accumulated on entry to a state and <b>never cleared by recovery</b> — that is
/// the entire point: dawn gives you your body back, not your dignity.
///
/// <para><b>This is not an injury system and must not become one.</b> A fixed four-value flags
/// enum with no severity, no stacking, no timers and no gameplay effect whatsoever. Issue #179
/// says so in those words. If a future packet wants injuries to <i>do</i> something, that is a
/// new system with its own spec, not a widening of this byte.</para>
/// </summary>
[Flags]
public enum InjuryMark : byte
{
    None = 0,

    /// <summary>Bowled over by something large. From <see cref="IncapacityCause.LongOneContact"/>
    /// and <see cref="IncapacityCause.BreakerRampage"/>.</summary>
    SootFace = 1,

    /// <summary>Stung. From <see cref="IncapacityCause.WaspStingStack"/>.</summary>
    Bandage = 2,

    /// <summary>Still slightly frosty — the plan's own words for the bus photo. From either
    /// Frozen cause.</summary>
    FrostCoating = 4,

    /// <summary>The birds-hangover halo: you were knocked out at least once, however it
    /// happened.</summary>
    BirdsHalo = 8,
}

/// <summary>
/// Every tuned number and every cause→state mapping in one place, so no consumer re-derives one.
/// All durations are <c>[playtest]</c> defaults per beta plan §0: pick the stated default, ship
/// it, let the beta tune it.
/// </summary>
public static class IncapacityRules
{
    /// <summary>Seconds a teammate must keep shaking to wake a Knocked Out player.
    /// <c>[playtest: 5 s]</c> — beta plan §10, verbatim.</summary>
    public const float ShakeSecondsToWake = 5f;

    /// <summary>Seconds an ice block must spend inside a lit fire's warmth to thaw.
    /// <c>[playtest]</c>. Longer than the shake because thawing is meant to be the reason
    /// somebody drags the block across camp rather than standing over it.</summary>
    public const float ThawSecondsAtFire = 8f;

    /// <summary>How long one momentary impulse ragdoll lasts. <c>[playtest]</c>. Short enough to
    /// read as a comic stumble rather than a state.</summary>
    public const float ImpulseRagdollSec = 1.2f;

    /// <summary>Impulse hits inside <see cref="ImpulseStackWindowSec"/> that add up to a real
    /// Knocked Out. <c>[playtest: 3 hits → KO]</c> — beta plan §10 and Issue #179, verbatim.</summary>
    public const int ImpulseHitsToKnockout = 3;

    /// <summary>How long an impulse hit stays "recent" for stacking. Decays rather than
    /// resetting on a timer edge, so a fourth shove five seconds after three does not silently
    /// count as a first.</summary>
    public const float ImpulseStackWindowSec = 6f;

    /// <summary>How close a rescuer must be to begin (and to keep) shaking or dragging.
    /// <c>[playtest]</c>. Comfortably inside conversational range so the verb is discovered by
    /// standing over a friend, which is what a player does anyway.</summary>
    public const float AssistRadiusM = 1.8f;

    /// <summary>How far a dragged block trails behind its dragger before it is pulled along.
    /// <c>[playtest]</c>. Larger than <see cref="AssistRadiusM"/> is deliberate: the block should
    /// lag, slide and swing rather than glue itself to the dragger's heels.</summary>
    public const float DragTetherM = 2.2f;

    /// <summary>Past this separation the drag breaks on its own. <c>[playtest]</c>. Bounds the
    /// verb so a dragger who sprints away, falls, or is himself knocked out cannot tow a block
    /// across camp from arbitrary range.</summary>
    public const float DragBreakM = 4.5f;

    /// <summary>Metres per second an ice block slides while dragged. <c>[playtest]</c>. Well
    /// under <c>AvatarMotor.MoveSpeed</c> (3.6) — dragging is supposed to cost you the night.</summary>
    public const float DragSpeedMps = 1.5f;

    /// <summary>
    /// Which state a cause produces. The single mapping — the plan's "one machine, two skins"
    /// expressed as data rather than as a switch repeated at every call site, which is what lets
    /// the Starer's gaze and the lake's cold be provably the same state rather than two states
    /// that merely look alike.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="IncapacityCause.None"/> or an undeclared value. A cause with no home is a
    /// design fork, not a runtime fallback — Issue #179's "named open item" says in as many
    /// words: stop and surface it rather than inventing a category. Throwing here is what makes
    /// that structural instead of a code comment nobody reads.
    /// </exception>
    public static IncapacityState StateForCause(IncapacityCause cause) => cause switch
    {
        IncapacityCause.LongOneContact => IncapacityState.KnockedOut,
        IncapacityCause.BreakerRampage => IncapacityState.KnockedOut,
        IncapacityCause.WaspStingStack => IncapacityState.KnockedOut,
        IncapacityCause.NightWaterChill => IncapacityState.Frozen,
        IncapacityCause.StarerGaze => IncapacityState.Frozen,
        IncapacityCause.Debug => IncapacityState.KnockedOut,
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause,
            "no incapacitation state is declared for this cause — see IncapacityRules.StateForCause"),
    };

    /// <summary>The comic mark a cause leaves on the body for the bus photo. Every Knocked Out
    /// cause also leaves <see cref="InjuryMark.BirdsHalo"/>, so the photo can say "this one got
    /// flattened" without knowing by what.</summary>
    public static InjuryMark MarkForCause(IncapacityCause cause) => cause switch
    {
        IncapacityCause.LongOneContact => InjuryMark.SootFace | InjuryMark.BirdsHalo,
        IncapacityCause.BreakerRampage => InjuryMark.SootFace | InjuryMark.BirdsHalo,
        IncapacityCause.WaspStingStack => InjuryMark.Bandage | InjuryMark.BirdsHalo,
        IncapacityCause.NightWaterChill => InjuryMark.FrostCoating,
        IncapacityCause.StarerGaze => InjuryMark.FrostCoating,
        _ => InjuryMark.None,
    };

    /// <summary>Which recovery verb a state answers to, besides dawn. Frozen thaws; Knocked Out
    /// is shaken. Stated as a function so the assist resolver never hard-codes the pairing.</summary>
    public static IncapacityExit AssistExitFor(IncapacityState state) => state switch
    {
        IncapacityState.KnockedOut => IncapacityExit.TeammateShake,
        IncapacityState.Frozen => IncapacityExit.FireThaw,
        _ => IncapacityExit.None,
    };
}
