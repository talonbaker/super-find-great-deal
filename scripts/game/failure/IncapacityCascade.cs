using System;
using System.Collections.Generic;

namespace Sail.Game.Failure;

/// <summary>
/// The systems one incapacitation transition must update, as an enum, so "did every row fire"
/// is a thing a test can ask rather than a thing a reviewer has to notice.
///
/// <para><b>These are not the 22 rows of <c>STATE-CASCADE-TABLE.md</c>.</b> That document is the
/// standing derivation and every one of its rows is answered in this packet's filled table. These
/// five are the subset that <i>actually changes something at the moment of transition</i> — the
/// rest are answered <c>UNCHANGED (why safe)</c>, which is an answer, not an action, and there is
/// nothing for a sink to do about it. Four of the five are exactly <c>BEHAVIOR-BIBLE</c> §10.2's
/// "missing API, not configuration" rows; the fifth (<see cref="InteractionGates"/>) is the
/// bundle of reach/target/UI rows that all key off the same predicate.</para>
///
/// <para>Ordinals are ordering. See <see cref="IncapacityCascade.Order"/>.</para>
/// </summary>
public enum CascadeRow : byte
{
    /// <summary>Table rows 7, 9, 14, 22. Can this body still grab, throw, vend, fire a verb,
    /// see an interact chip — and can a teammate act <i>on</i> it? One predicate, because every
    /// one of those systems asks the same question and answering them separately is how three of
    /// four get updated.</summary>
    InteractionGates = 0,

    /// <summary>Table row 5. <c>SandboxCamera</c> had no detach; §10.2 named that as missing API.
    /// It has one now.</summary>
    Camera = 1,

    /// <summary>Table rows 6, 11, 12, 20. What the body looks like, where its nameplate hangs,
    /// and how a player knows within a second which state they are in without reading text.</summary>
    Legibility = 2,

    /// <summary>Table rows 1, 2, 3, 4 — <b>the commit</b>. All four ride one carrier: the state
    /// goes into <c>MoveState</c>, which is simultaneously what disables input
    /// (<c>AvatarMotor.Step</c>), what keeps <c>CharacterBody3D</c> the single transform writer,
    /// what suspends owner prediction, and what replicates. That is not a coincidence — hard
    /// constraint 1 of the cascade table says anything influencing movement <i>must</i> live in
    /// <c>MoveState</c>, so putting it anywhere else was never available.</summary>
    ReplicatedState = 3,

    /// <summary>Table row 8. Everything you carry scatters (beta plan §10) — a gameplay fact that
    /// feeds the fire economy, not cleanup.</summary>
    CarriedProps = 4,
}

/// <summary>
/// The seam every cascade row is applied through. <c>IncapacitationService</c> implements it
/// against the live scene; the test suite implements it three more times (recording, partial,
/// throwing) to prove the transition is atomic and to prove the proof can fail.
/// </summary>
public interface IIncapacitySink
{
    /// <summary><see cref="CascadeRow.InteractionGates"/>.</summary>
    void SetInteractionGates(int peerId, bool denied);

    /// <summary><see cref="CascadeRow.Camera"/>.</summary>
    void SetCameraDetached(int peerId, bool detached);

    /// <summary><see cref="CascadeRow.Legibility"/>.</summary>
    void SetLegibility(int peerId, IncapacityState state, IncapacityCause cause);

    /// <summary><see cref="CascadeRow.ReplicatedState"/> — the commit.</summary>
    void CommitReplicatedState(int peerId, IncapacityState state, bool impulseRagdoll);

    /// <summary><see cref="CascadeRow.CarriedProps"/>. Only ever called on the way <i>into</i>
    /// incapacitation.</summary>
    void ScatterCarried(int peerId);
}

/// <summary>What one <see cref="IncapacityCascade.Apply"/> did.</summary>
/// <param name="Committed">The replicated state was written. <b>The caller must not advance its
/// own machine unless this is true</b> — that is the entire atomicity contract.</param>
/// <param name="Rows">Rows attempted, in order, up to and including any that threw.</param>
/// <param name="FailedAt">The row that threw, if one did.</param>
/// <param name="Error">Its message.</param>
public readonly record struct CascadeOutcome(
    bool Committed,
    IReadOnlyList<CascadeRow> Rows,
    CascadeRow? FailedAt,
    string? Error)
{
    /// <summary>Nothing went wrong.</summary>
    public bool Clean => FailedAt == null;
}

/// <summary>
/// Applies one player-state transition across every system that must change with it, in one
/// call, in a fixed order, with the replicated commit second-to-last.
///
/// <para><b>Why the order is what it is.</b> <c>BEHAVIOR-BIBLE</c> §10.2 says the transition is
/// atomic and <c>MECHANICS-BIBLE</c> §2 says every dependent system updates in the same commit.
/// In a single-threaded game loop there is no transaction to roll back to, so atomicity has to be
/// bought structurally instead: <b>every revocable row runs before the commit, and the one
/// irreversible row runs after it.</b></para>
///
/// <list type="number">
///   <item><description>Gates, camera, legibility — all revocable, all local, none observable by
///   another peer. If any of them throws, <b>nothing has been committed and the transition simply
///   did not happen</b>: the caller's machine is not advanced, no snapshot carries the new state,
///   and the next tick tries again from a consistent world. That is a clean abort, not a partial
///   update.</description></item>
///   <item><description>The replicated commit. Past this line the transition is real everywhere.</description></item>
///   <item><description>Scattering carried props, which cannot be undone — a prop released into
///   Loose has been broadcast to every peer. It runs <i>after</i> the commit deliberately, so its
///   failure mode is the mild one: the state is correct and consistent on every system and every
///   peer, and some of the player's items are still in their hands. Both Held and Loose are legal
///   prop modes, so even that leaves nothing inconsistent — it leaves a player who got to keep
///   their axe. If the order were reversed, the same failure would scatter a player's whole
///   held set and then leave them standing up holding nothing, with no record of why.</description></item>
/// </list>
///
/// <para><b>Why it catches.</b> A throw inside a Godot C# physics or network callback swallows the
/// rest of that callback silently — the trap <c>WaterService.ApplyEvent</c> already documents.
/// An unhandled exception halfway through this method is therefore the exact shape of defect the
/// cascade table exists to prevent, arriving through the error path instead of the design path.
/// Catching converts it into a reported, logged, structurally-consistent outcome.</para>
/// </summary>
public static class IncapacityCascade
{
    /// <summary>The rows a transition INTO incapacitation touches, in application order.</summary>
    public static readonly IReadOnlyList<CascadeRow> Order = new[]
    {
        CascadeRow.InteractionGates,
        CascadeRow.Camera,
        CascadeRow.Legibility,
        CascadeRow.ReplicatedState,
        CascadeRow.CarriedProps,
    };

    /// <summary>The rows a transition OUT of incapacitation touches. Identical minus the scatter:
    /// getting up does not un-drop what you dropped.</summary>
    public static readonly IReadOnlyList<CascadeRow> RecoveryOrder = new[]
    {
        CascadeRow.InteractionGates,
        CascadeRow.Camera,
        CascadeRow.Legibility,
        CascadeRow.ReplicatedState,
    };

    /// <summary>The rows expected for a given transition — what a verifier compares against.
    /// A pure function of the transition, so a test never hand-lists the rows it is checking
    /// (a hand-list would drift from <see cref="Apply"/>, and then the test would be asserting
    /// its own copy of the answer).</summary>
    public static IReadOnlyList<CascadeRow> ExpectedRows(IncapacityState from, IncapacityState to)
        => to != IncapacityState.Active && from == IncapacityState.Active ? Order : RecoveryOrder;

    /// <summary>Which expected rows an observed run did not touch. Empty means the cascade was
    /// complete. <b>This is the detector the negative control has to be able to trip</b> — a test
    /// that can only ever report "complete" certifies nothing.</summary>
    public static IReadOnlyList<CascadeRow> MissingRows(
        IncapacityState from, IncapacityState to, IReadOnlyCollection<CascadeRow> observed)
    {
        var missing = new List<CascadeRow>();
        foreach (CascadeRow row in ExpectedRows(from, to))
            if (!observed.Contains(row))
                missing.Add(row);
        return missing;
    }

    /// <summary>
    /// Apply one transition. See the class doc for the ordering contract.
    /// </summary>
    /// <param name="impulseRagdoll">The momentary-ragdoll bit to commit alongside the state.
    /// Carried through here rather than set separately because it rides the same replicated byte
    /// and a second write would be a second commit — which is the thing this method exists to
    /// make impossible.</param>
    public static CascadeOutcome Apply(
        IIncapacitySink sink,
        int peerId,
        IncapacityState from,
        IncapacityState to,
        IncapacityCause cause,
        bool impulseRagdoll)
    {
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));

        var rows = new List<CascadeRow>(Order.Count);
        bool denied = to != IncapacityState.Active || impulseRagdoll;
        bool entering = to != IncapacityState.Active && from == IncapacityState.Active;

        try
        {
            rows.Add(CascadeRow.InteractionGates);
            sink.SetInteractionGates(peerId, denied);

            // The camera follows INCAPACITATION only, never the momentary impulse: a comic
            // stumble that yanked the camera out of the player's hands for 1.2 s would read as a
            // bug, and beta plan §10 is explicit that an impulse ragdoll is not incapacitation.
            rows.Add(CascadeRow.Camera);
            sink.SetCameraDetached(peerId, to != IncapacityState.Active);

            rows.Add(CascadeRow.Legibility);
            sink.SetLegibility(peerId, to, cause);

            // --- the commit -------------------------------------------------------------------
            rows.Add(CascadeRow.ReplicatedState);
            sink.CommitReplicatedState(peerId, to, impulseRagdoll);
        }
        catch (Exception ex)
        {
            return new CascadeOutcome(false, rows, rows[^1], ex.Message);
        }

        if (!entering)
            return new CascadeOutcome(true, rows, null, null);

        try
        {
            rows.Add(CascadeRow.CarriedProps);
            sink.ScatterCarried(peerId);
        }
        catch (Exception ex)
        {
            // Committed stays TRUE — see the class doc. Every state system agrees; the player
            // kept some items. Reported so it is loud rather than silent.
            return new CascadeOutcome(true, rows, CascadeRow.CarriedProps, ex.Message);
        }

        return new CascadeOutcome(true, rows, null, null);
    }
}
