namespace MpFoundation.Game.Watcher;

/// <summary>
/// Every number the watcher has, in one place, so a playtest retune is a diff of this file and
/// nothing else. `/direct` (THRILL-BIBLE §12, the watcher entry, 2026-08-09) explicitly handed
/// all of these back as builder's calls — "how rare is rare, how long a dwell, how far beyond
/// the lit radius, how fast the head turns" — so nothing here is doctrine and everything here
/// is provisional until somebody has stood in front of it after dark.
///
/// <para>What is NOT tuning, and must not be moved into this file as a value: the watcher never
/// pursues, never damages, and never stands on lit ground. Those are invariants, they live in
/// <see cref="WatcherBrain"/> and <see cref="WatcherPlacement"/> as code paths that cannot be
/// dialled to zero, and there is deliberately no knob here that softens any of them.</para>
/// </summary>
public sealed class WatcherTuning
{
    /// <summary>Exposure a player must reach before the watcher is willing to appear at all.
    /// Above the withdraw threshold by design, so a player hovering at the boundary does not
    /// summon and dismiss it repeatedly (that is over-exposure by flicker — THRILL §8.1).</summary>
    public float AppearVisibility { get; init; } = 0.35f;

    /// <summary>Exposure the current target may fall to before the exit arms.</summary>
    public float WithdrawVisibility { get; init; } = 0.20f;

    /// <summary>How much better a rival must score before attention transfers. Pure anti-jitter:
    /// two players within a hair of each other must not make the head oscillate, because the head
    /// turn is the entire readout (THRILL §12: "the head turn is the readout"). Small enough that
    /// the doe run — standing up and running — clears it instantly and by a mile.</summary>
    public float SwitchMargin { get; init; } = 0.06f;

    /// <summary>How long it is willing to stand there before it starts looking for a way out.
    /// Arms the exit; never forces it — see <see cref="WatcherBrain"/>'s unwitnessed-exit rule.</summary>
    public float DwellSeconds { get; init; } = 22f;

    /// <summary>Quiet period after a withdrawal before it may appear again. The one crude
    /// over-exposure brake in the build, and the §13 fork "how often the same player may be
    /// watched" is precisely the question of whether this is the right shape of brake.</summary>
    public float CooldownSeconds { get; init; } = 45f;

    /// <summary>Closing inside this arms the exit: a player who commits to walking at it gets
    /// nothing (THRILL §10, constraint (c) — it may never be approached and studied).</summary>
    public float ApproachArmM { get; init; } = 14f;

    /// <summary>Closing inside this ends the sighting immediately, witnessed or not. The one
    /// place the unwitnessed-exit rule yields, and it yields because at arm's length there is no
    /// ambiguity left to protect.</summary>
    public float HardStandoffM { get; init; } = 4f;

    /// <summary>Metres beyond <c>INightPressure.LitRadiusM</c> that the stand position is
    /// searched in. The near edge is a margin, not a preference — it may never be zero.</summary>
    public float StandoffMinM { get; init; } = 6f;
    public float StandoffMaxM { get; init; } = 26f;

    /// <summary>The band the stand position must sit in relative to the target. Nearer than
    /// <see cref="MinRangeM"/> is already an approach; further than <see cref="MaxRangeM"/> and
    /// the blockout is too small to resolve as a body at all (THRILL §6.0 affect condition (i)).</summary>
    public float MinRangeM { get; init; } = 18f;
    public float MaxRangeM { get; init; } = 62f;

    /// <summary>Half-angle of the cone a player is considered to be able to see. Used twice and
    /// for opposite reasons: to refuse a stand position inside anyone's view (§8.2 — it is never
    /// witnessed arriving) and to hold an armed exit until nobody is looking (§2.1 — the
    /// all-clear must never arrive).</summary>
    public float ViewHalfAngleDeg { get; init; } = 55f;

    /// <summary>Degrees per second the head yaws toward its target. Deliberately slow: the turn
    /// is the only output channel the creature has and it has to be legible from prone, in the
    /// dark, at distance.</summary>
    public float TurnDegPerSec { get; init; } = 38f;

    public static WatcherTuning Default { get; } = new();
}
