using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;
using MpFoundation.Net;

namespace Sail.Game.Failure;

/// <summary>
/// The one server-authoritative incapacitation machine (beta plan §10). Owns every player's
/// <see cref="IncapacitationMachine"/>, resolves the assist verbs from authoritative positions,
/// applies the dawn floor, and publishes the all-incapacitated signal the run layer consumes.
///
/// <para><b>Deliberately shaped like <c>WaterService</c>,</b> which is the closest working
/// precedent in this repo for "a server-owned per-peer clock that writes into replicated
/// <c>MoveState</c>": a singleton <c>Instance</c>, a <c>Setup</c> that takes providers rather than
/// reaching for node paths, a server-only <c>_PhysicsProcess</c>, reliable typed events on its own
/// channel, and a late-join dump. Following it rather than inventing a shape means every question
/// about lifecycle, replication and lab-inertness has already been answered once.</para>
///
/// <para><b>What it does not do.</b> It does not end the run (that is phase 2c — see
/// <see cref="AllConnectedIncapacitated"/> and <see cref="AllIncapacitatedSignal"/>, which are
/// published for exactly that consumer and deliberately drive nothing here). It does not know
/// what a creature is; the five causes in <see cref="IncapacityCause"/> are an interface waiting
/// for phases 3a/3c/3d/4a to call in. It does not decide what a state should FEEL like — the
/// comic register is the register law's, and the affect direction is <c>/direct</c>'s.</para>
/// </summary>
public sealed partial class IncapacitationService : Node, IIncapacitySink, Run.IWorldStateSlice
{
    /// <summary>Node name, matching the sibling services' convention.</summary>
    public const string NodeName = "IncapacitationService";

    /// <summary>The live service, or null in a world that has none (the labs, the playground,
    /// most of the headless suites). Every caller null-checks.</summary>
    public static IncapacitationService? Instance { get; private set; }

    /// <summary>How often a peer's assist progress is streamed while it is changing. Low
    /// frequency on purpose: it drives a progress cue, not a simulation.</summary>
    private const double ProgressIntervalSec = 0.1;

    private bool _isServer;
    private Func<IEnumerable<SandboxAvatar>>? _avatars;
    private Func<Vector3, bool>? _warmthProbe;
    private Action<int>? _scatterCarried;

    private readonly Dictionary<int, IncapacitationMachine> _machines = new();

    /// <summary>target peer -> the peer assisting them. One rescuer per body, and one body per
    /// rescuer (enforced in <see cref="ResolveAssistEdges"/>): two people shaking one player
    /// twice as fast is a tuning conversation nobody has had, and it would make the 5-second
    /// number mean nothing.</summary>
    private readonly Dictionary<int, int> _assistedBy = new();

    private readonly List<int> _scratchIds = new();
    private double _sinceProgress;
    private bool _runDriverHooked;
    private bool _waterHooked;
    private bool _allIncapacitatedLatch;
    private uint _seq;

    /// <summary>False until this peer has been told the authoritative failure state at least
    /// once. Same hard contract as <c>WaterService.Synced</c> and <c>CycleDriver.Synced</c>:
    /// nothing visible may be derived from this service while it is false, because a client's
    /// default "everyone is fine" is a guess, not a fact — and "everyone is fine" is the single
    /// most dangerous thing this service could guess wrong.</summary>
    public bool Synced { get; private set; }

    // --- Events (raised on every peer, from the replicated transition) -------------------------

    /// <summary>A player went down. (peer, state, cause)</summary>
    public event Action<int, IncapacityState, IncapacityCause>? Incapacitated;

    /// <summary>A player got back up. (peer, how)</summary>
    public event Action<int, IncapacityExit>? Recovered;

    /// <summary>A player was momentarily bowled over. (peer)</summary>
    public event Action<int>? ImpulseRagdolled;

    /// <summary>
    /// <b>Every connected player is down at once.</b> Raised on the server only, once per rising
    /// edge (it re-arms when anyone gets up).
    ///
    /// <para><b>This is the interface phase 2c consumes and it drives nothing here.</b> Beta plan
    /// §4.1 makes "every connected player simultaneously incapacitated" the run's only hard loss
    /// condition, and §10 puts that check in the run layer. This service publishes the fact;
    /// <c>RunDriver</c> decides what it means. 2c should subscribe to this (or poll
    /// <see cref="AllConnectedIncapacitated"/>) rather than re-deriving it — re-derivation is how
    /// two definitions of "incapacitated" come to disagree about whether Frozen counts, and it
    /// does count.</para>
    /// </summary>
    public event Action? AllIncapacitatedSignal;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Wire the service up. Providers rather than node paths or sibling statics, for the reason
    /// <c>WaterService.Setup</c> gives: this must stay harmless in a world that has no fires, no
    /// props and no run driver (the playground, the labs, CI).
    /// </summary>
    /// <param name="warmthProbe">Is this position inside a lit fire's warmth? A delegate, not a
    /// call into any warmth source, <b>specifically so that what "a lit fire" means can change
    /// underneath this</b> (it has, more than once). When it does, one closure at the call site
    /// changes and nothing in here does.</param>
    /// <param name="scatterCarried">Drop everything this peer carries — <c>PropManager.ScatterHeldBy</c>
    /// in a real session, and nothing at all in a world with no props.</param>
    public void Setup(bool isServer, Func<IEnumerable<SandboxAvatar>> avatars,
        Func<Vector3, bool> warmthProbe, Action<int> scatterCarried)
    {
        _isServer = isServer;
        _avatars = avatars;
        _warmthProbe = warmthProbe;
        _scatterCarried = scatterCarried;
        Synced = isServer;
        // CORE-PROG-A2 (spec §5.2's incapacitation row): registered HERE, at construction time,
        // so the fan order stays deterministic (= construction order) and the very first
        // boundary already includes this slice — HookDependencies runs a tick too late for
        // that. This edits only this file; the untouchable interim-death-model construction
        // block in Gameplay.cs (2026-08-12) is not entered. Null-safe for labs.
        Run.WorldStateStore.Instance?.Register(this);
    }

    // --- Public read surface ---------------------------------------------------------------------

    /// <summary>This peer's state. <see cref="IncapacityState.Active"/> for an unknown peer —
    /// "they are fine" is the only safe answer to "who?", because the alternative would let an
    /// unknown id count toward the run-ending loss condition.</summary>
    public IncapacityState StateOf(int peerId) =>
        _machines.TryGetValue(peerId, out IncapacitationMachine? m) ? m.State : IncapacityState.Active;

    /// <summary>What put this peer down.</summary>
    public IncapacityCause CauseOf(int peerId) =>
        _machines.TryGetValue(peerId, out IncapacitationMachine? m) ? m.Cause : IncapacityCause.None;

    /// <summary>Is this peer down (Knocked Out or Frozen)? An impulse ragdoll is not.</summary>
    public bool IsIncapacitated(int peerId) => StateOf(peerId) != IncapacityState.Active;

    /// <summary>How far through their rescue this peer is, <c>[0,1]</c>.</summary>
    public float AssistProgressOf(int peerId) =>
        _machines.TryGetValue(peerId, out IncapacitationMachine? m) ? m.AssistProgress01 : 0f;

    /// <summary>
    /// The comic marks this player has collected — soot, a bandage, frost, the birds halo.
    /// Accumulated on every incapacitation and <b>never cleared by recovery</b>, so the bus photo
    /// (beta plan §4.4, phase 2c) can read the whole run's history off it at dawn 5.
    ///
    /// <para>Exposed as the fixed four-value flags enum it is. It carries no severity, no count
    /// and no gameplay effect, and Issue #179 says in as many words that it must not grow any.</para>
    /// </summary>
    public InjuryMark InjuriesFor(int peerId) =>
        _machines.TryGetValue(peerId, out IncapacitationMachine? m) ? m.Injuries : InjuryMark.None;

    /// <summary>Peers currently tracked (one per live avatar).</summary>
    public int TrackedCount => _machines.Count;

    /// <summary>How many of them are down.</summary>
    public int IncapacitatedCount
    {
        get
        {
            int n = 0;
            foreach (IncapacitationMachine m in _machines.Values)
                if (m.Incapacitated)
                    n++;
            return n;
        }
    }

    /// <summary>
    /// <b>The run-end query</b> (beta plan §4.1), for phase 2c. True when at least one player is
    /// tracked and every one of them is down. <b>Frozen counts</b> — that is the plan's word and
    /// it is why this asks the machine rather than counting "knocked out".
    ///
    /// <para>An empty camp is deliberately false rather than vacuously true: zero connected
    /// players is a session that has not started or has ended, never a group that has lost.</para>
    /// </summary>
    public bool AllConnectedIncapacitated => IncapacitationMachine.AllIncapacitated(_machines.Values);

    // --- Public cause surface (the interface phases 3a/3c/3d/4a call into) ------------------------

    /// <summary>
    /// Server-only: put a player down. The single door for every creature that will ever cause a
    /// failure state — the Long One's contact, the Starer's gaze, a sting stack, the lake's cold.
    /// The cause selects the state (<see cref="IncapacityRules.StateForCause"/>), so no caller
    /// picks one, and the Starer's freeze is provably the same state as the lake's rather than a
    /// second one that resembles it.
    /// </summary>
    /// <returns>True if this call actually put an Active player down.</returns>
    public bool Incapacitate(int peerId, IncapacityCause cause)
    {
        if (!_isServer)
            return false;
        IncapacitationMachine m = MachineFor(peerId);
        IncapacityState from = m.State;
        IncapacityCause fromCause = m.Cause;
        // Snapshotted BEFORE Enter, which appends to the ledger: a rollback has to hand back the
        // exact set the player had, not an empty one.
        InjuryMark fromInjuries = m.Injuries;
        if (!m.Enter(cause))
            return false;
        // The machine advanced only because Enter said it could. The cascade is applied against
        // that decision, and if it fails before its commit the machine is rolled back — see
        // CommitTransition.
        return CommitTransition(peerId, m, from, cause, IncapacityExit.None, fromCause, fromInjuries);
    }

    /// <summary>
    /// Server-only: one momentary impulse ragdoll (the Breaker's arm-swing). Comic, self-clearing,
    /// and <b>not</b> incapacitation — it scatters nothing, detaches nothing and counts toward no
    /// loss condition — unless it is the hit that stacks
    /// (<see cref="IncapacityRules.ImpulseHitsToKnockout"/>), which turns it into a real Knocked
    /// Out through the same cascade as any other cause.
    /// </summary>
    public IncapacityStep ApplyImpulse(int peerId, IncapacityCause stackCause = IncapacityCause.BreakerRampage)
    {
        if (!_isServer)
            return IncapacityStep.None;
        IncapacitationMachine m = MachineFor(peerId);
        IncapacityState from = m.State;
        IncapacityCause fromCause = m.Cause;
        InjuryMark fromInjuries = m.Injuries;
        IncapacityStep step = m.Impulse(stackCause);
        if (step == IncapacityStep.StackedToKnockout)
        {
            CommitTransition(peerId, m, from, stackCause, IncapacityExit.None, fromCause, fromInjuries);
            return step;
        }
        if (m.ImpulseRagdolled)
        {
            // No cascade: an impulse ragdoll touches exactly one row (the replicated bit). Pushed
            // through the same commit method every tick anyway (see PushCommits), so this is only
            // making it land on the tick it happened rather than the next one.
            AvatarFor(peerId)?.ServerCommitIncapacity(m.State, true);
            BroadcastEvent(IncapacityEventKind.Impulse, peerId, m.State, IncapacityCause.None,
                IncapacityExit.None);
        }
        return step;
    }

    /// <summary>
    /// Server-only: a momentary impulse ragdoll that <b>cannot stack into a Knocked Out</b> — the
    /// landing surface for a since-removed prey creature's shock blast.
    /// See <see cref="IncapacitationMachine.ImpulseNonStacking"/> for the three reasons the
    /// exemption exists.
    ///
    /// <para>Deliberately a sibling of <see cref="ApplyImpulse"/> rather than a parameter on it: the
    /// Breaker's ledger and this exemption are two different rulings, and a bool argument would let
    /// a future caller pick the wrong one by defaulting. There is no cascade here for the same
    /// reason <see cref="ApplyImpulse"/> has none on its non-stacking path — an impulse ragdoll
    /// touches exactly one row, the replicated bit.</para>
    /// </summary>
    /// <returns>True if a ragdoll was actually armed. False on an already-incapacitated body, whose
    /// blast still propagates but whose body does not leave the ground.</returns>
    public bool ApplyBlastImpulse(int peerId)
    {
        if (!_isServer)
            return false;
        IncapacitationMachine m = MachineFor(peerId);
        if (!m.ImpulseNonStacking())
            return false;
        AvatarFor(peerId)?.ServerCommitIncapacity(m.State, true);
        BroadcastEvent(IncapacityEventKind.Impulse, peerId, m.State, IncapacityCause.None,
            IncapacityExit.None);
        return true;
    }

    /// <summary>Test/lab door into any state without a creature to open it. Never called by
    /// production code — see <see cref="IncapacityCause.Debug"/>.</summary>
    public bool DebugIncapacitate(int peerId, IncapacityState state)
    {
        IncapacityCause cause = state switch
        {
            IncapacityState.Frozen => IncapacityCause.StarerGaze,
            IncapacityState.KnockedOut => IncapacityCause.Debug,
            _ => IncapacityCause.None,
        };
        return cause != IncapacityCause.None && Incapacitate(peerId, cause);
    }

    /// <summary>Forget a peer entirely (they left). Mirrors <c>WaterService.ForgetPeer</c>.</summary>
    public void ForgetPeer(int peerId)
    {
        _machines.Remove(peerId);
        _assistedBy.Remove(peerId);
        BreakAssistsBy(peerId);
    }

    // --- The server tick ---------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (!_isServer || _avatars == null)
            return;

        HookDependencies();

        float dt = (float)delta;
        _scratchIds.Clear();

        // 1. Track every live avatar, and prune anyone who left. Machines are keyed by peer, and
        //    a peer with no avatar cannot be assisted, dragged, thawed or counted — leaving a
        //    stale one in the dictionary would let a departed player hold the whole group in the
        //    all-incapacitated condition forever, which is a run-ending bug rather than a leak.
        foreach (SandboxAvatar avatar in _avatars())
        {
            _scratchIds.Add(avatar.OwnerPeerId);
            MachineFor(avatar.OwnerPeerId);
        }
        PruneMissing();

        // 2. Assist edges: who just pressed Interact next to whom. Before the timers, so a press
        //    lands on the tick it happened rather than the next one.
        ResolveAssistEdges();

        // 3. Validate every standing assist, then advance the machines. Fixed order, stated
        //    (MECHANICS-BIBLE §3): break stale assists, gather inputs, advance, apply outcomes.
        ValidateAssists();
        AdvanceMachines(dt);

        // 4. Drag motion, then the per-tick commit of the replicated bits.
        ApplyDragMotion();
        PushCommits();

        // 5. The all-incapacitated edge, for phase 2c.
        UpdateAllIncapacitatedLatch();

        // 6. Assist-progress stream.
        _sinceProgress += delta;
        if (_sinceProgress >= ProgressIntervalSec)
        {
            _sinceProgress = 0;
            BroadcastProgress();
        }
    }

    /// <summary>Subscribes to the two sibling systems this service listens to, the first tick each
    /// of them exists. Lazy rather than done in <see cref="Setup"/> because neither is guaranteed
    /// to be in the tree yet at wiring time and neither exists at all in a lab.</summary>
    private void HookDependencies()
    {
        if (!_runDriverHooked && RunDriver.Instance is RunDriver run)
        {
            // CORE-PROG-A2: the RunReset subscription is gone — the reset is a registered store
            // slice (see Setup). PhaseCrossed stays direct: the dawn floor it carries is an
            // anti-unwinnable guarantee, not a resettable state.
            run.PhaseCrossed += OnPhaseCrossed;
            _runDriverHooked = true;
        }
        if (!_waterHooked && Water.WaterService.Instance is Water.WaterService water)
        {
            water.Sputtered += OnSputterRecovered;
            _waterHooked = true;
        }
    }

    /// <summary>
    /// <b>The dawn floor.</b> Every state, every cause, every player, unconditionally.
    ///
    /// <para>There is no guard in this method and there must never be one. It is
    /// <c>MECHANICS-BIBLE</c> §10.5's anti-unwinnable floor and beta plan §4.1's dawn floor, and
    /// it is the reason a group whose last rescuer went down still has a morning. It fires on the
    /// <see cref="PhaseEventKind.NightToDawn"/> crossing, which <c>RunDriver</c> derives
    /// on the server and broadcasts reliably — so it cannot be missed by a client whose clock
    /// drifted, and it cannot be skipped by a hitch (the crossing detector walks every ordinal
    /// between two samples rather than testing the current one).</para>
    ///
    /// <para>A player incapacitated one tick before the crossing recovers on it, because the
    /// crossing is an edge on the run's clock and not a sample of anybody's state.</para>
    /// </summary>
    private void OnPhaseCrossed(PhaseEventKind kind, int cyclesElapsedAfter)
    {
        if (!_isServer || kind != PhaseEventKind.NightToDawn)
            return;
        RecoverEveryoneAtDawn();
    }

    /// <summary>Dawn, applied. Public so the headless suite can reach the floor without waiting
    /// out a night, and so a future forced-dawn debug command has one door.</summary>
    public void RecoverEveryoneAtDawn()
    {
        if (!_isServer)
            return;
        // Its OWN list, not the tick's shared scratch buffer: this runs from RunDriver's
        // CallLocal RPC, which is a different call path from _PhysicsProcess. Borrowing a buffer
        // across two call paths is the kind of coupling that works until the day it does not, and
        // this is the one method in the file that must never misbehave.
        var ids = new List<int>(_machines.Keys);
        foreach (int peerId in ids)
        {
            IncapacitationMachine m = _machines[peerId];
            IncapacityState from = m.State;
            if (!m.RecoverAtDawn())
                continue;
            _assistedBy.Remove(peerId);
            BreakAssistsBy(peerId);
            CommitTransition(peerId, m, from, IncapacityCause.None, IncapacityExit.Dawn);
        }
    }

    // --- The world-state slice (CORE-PROG-A2, spec §5.2 incapacitation row) --------------------

    public string SliceId => "incapacitation";

    /// <summary>A new playthrough: everyone up, every injury forgotten, every assist dropped.
    /// Row 17 of the cascade table, answered rather than deferred — a summary screen that showed
    /// the previous run's frostbite would be exactly the "we forgot system X" defect that row
    /// exists to catch. Idempotent: a second pass finds every machine already Active. (The prey
    /// pivot may retire this system wholesale; the slice migrates as-is regardless — spec §5.2.)</summary>
    public void ResetForNewPlaythrough()
    {
        // Its own list, for the same reason RecoverEveryoneAtDawn has one: this can arrive on
        // RunDriver's RPC path (the RunReset fan), not the tick's.
        var ids = new List<int>(_machines.Keys);
        foreach (int peerId in ids)
        {
            IncapacitationMachine m = _machines[peerId];
            IncapacityState from = m.State;
            m.Reset();
            if (from != IncapacityState.Active)
                CommitTransition(peerId, m, from, IncapacityCause.None, IncapacityExit.None);
        }
        _assistedBy.Clear();
        _allIncapacitatedLatch = false;
    }

    /// <summary>
    /// <b>The night-water freeze, wired.</b> <c>WaterService</c>'s existing chill clock is the
    /// input beta plan §10 names, and this is the join.
    ///
    /// <para><b>It hangs off the sputter-out's RECOVERY, not off chill reaching 1.0</b>, and that
    /// is the load-bearing choice in this method. The sputter-out already runs its whole tested
    /// sequence at chill 1.0 — the swallow, the cut to black, the hard-cut relocation to the
    /// nearest shore point — and only then returns control. Freezing at the end of it means the
    /// ice block forms <i>on the shore</i>, where the fiction puts it ("somebody freezes solid at
    /// the shore and gets dragged", beta plan §2) and, more importantly, where a teammate can
    /// actually reach it to drag it. Freezing at chill 1.0 instead would have left a block
    /// bobbing in deep water with only the dawn floor to rescue it.</para>
    ///
    /// <para>It also means the day behaviour is <b>completely unchanged</b>: by day a sputter-out
    /// is exactly what it has always been. The night is what freezes you.</para>
    /// </summary>
    private void OnSputterRecovered(Water.WaterEvent evt)
    {
        if (!_isServer || !IsNight())
            return;
        Incapacitate(evt.PeerId, IncapacityCause.NightWaterChill);
    }

    /// <summary>Night by the shared run clock, not by a light sample — the parity law (beta plan
    /// §5.2): gameplay decisions read server-side data that is identical on every client.
    /// A world with no cycle driver is never night, which keeps the labs inert.
    ///
    /// CORE-PROG-A2 (the spec §1.2 gating rule, scope 6): a band is only night FOR GAMEPLAY
    /// while the playthrough is band-live — the clock free-runs behind the Loss screen, and a
    /// group sitting with it long enough reaches a real night, but no freeze pressure may fire
    /// there. This is the freeze pressure's input seam, gated HERE because the interim death
    /// model's construction block in Gameplay.cs is ruled untouchable (2026-08-12) and needs no
    /// edit this way. The dawn floor (RecoverEveryoneAtDawn) stays deliberately UNGATED — its
    /// own doc forbids any guard; a recovery behind the Loss screen is harmless, a missed one
    /// is the anti-unwinnable defect the floor exists to prevent. A world with no driver
    /// (labs) keeps today's always-live reading.</summary>
    private static bool IsNight()
    {
        if (CycleDriver.Instance is not CycleDriver cycle || !cycle.Synced)
            return false;
        if (Run.PlaythroughDriver.Instance is { IsBandLive: false })
            return false;
        CycleBands.Band band = CycleBands.GetBand(cycle.Phase, cycle.CyclesElapsed, out _);
        return band is CycleBands.Band.Night or CycleBands.Band.DuskSweep;
    }

    // --- Assist resolution -------------------------------------------------------------------------

    /// <summary>
    /// One Interact edge per rescuer, turned into an assist attach or detach.
    ///
    /// <para><b>Attach/detach, not hold.</b> Beta plan §10 says <c>[playtest: 5 s hold]</c>, and
    /// the five seconds are honoured exactly; the <i>hold</i> is not, because it cannot be. The
    /// input buttons byte is full (<c>NetCodec</c> says so at <c>FlagSelectSlotIndex</c>), so
    /// there is no bit for a new held action, and <c>MoveIntent.Interact</c> is documented "edge,
    /// not level" and is sampled from <c>IsActionJustPressed</c> — there is no held Interact to
    /// read. So the verb is: press to start, and <b>stay next to them</b>. Walking away stops it
    /// and resets it to zero, which is the same non-cumulative rule the fireside dry-off uses and
    /// gives the verb the same shape a hold would have had — you commit, you stand there, you are
    /// exposed while you do it. Pressing again lets go early. Recorded as a deviation because it
    /// is one; it is a cheaper and, in co-op, a better verb than a held key.</para>
    /// </summary>
    private void ResolveAssistEdges()
    {
        foreach (SandboxAvatar rescuer in _avatars!())
        {
            if (!rescuer.TakeServerInteractEdge())
                continue;
            int rescuerId = rescuer.OwnerPeerId;
            // A rescuer who is themselves down, ragdolled or mid-sputter is not helping anyone.
            if (rescuer.ControlDeniedNow)
                continue;
            // Already assisting? The press means "let go".
            if (TargetAssistedBy(rescuerId) is int current)
            {
                _assistedBy.Remove(current);
                continue;
            }
            if (NearestAssistTarget(rescuer) is not int target)
                continue;
            // One rescuer per body. A second presser is refused rather than stealing, so the
            // player being shaken never loses their progress to somebody walking past.
            if (_assistedBy.ContainsKey(target))
                continue;
            _assistedBy[target] = rescuerId;
        }
    }

    /// <summary>
    /// The nearest incapacitated teammate within <see cref="IncapacityRules.AssistRadiusM"/>, or
    /// null.
    ///
    /// <para><b>Static and pure over replicated state on purpose.</b> The server calls it to
    /// resolve the verb; <c>SandboxAvatar.HandleCarryIntent</c> calls it on the owning client to
    /// suppress a grab, so that standing over a frozen friend next to a log means "help them"
    /// on both sides. Both read the same replicated positions and the same replicated
    /// <c>MoveState.Incapacity</c>, so they cannot disagree — which is the only reason a
    /// client-side prediction of a server rule is safe at all.</para>
    /// </summary>
    public static int? NearestAssistTarget(SandboxAvatar rescuer)
    {
        int? best = null;
        float bestSq = IncapacityRules.AssistRadiusM * IncapacityRules.AssistRadiusM;
        Vector3 from = rescuer.GlobalPosition;
        foreach (SandboxAvatar other in SandboxAvatar.Live)
        {
            if (other == rescuer || !other.IncapacitatedNow)
                continue;
            float d = from.DistanceSquaredTo(other.GlobalPosition);
            if (d > bestSq)
                continue;
            bestSq = d;
            best = other.OwnerPeerId;
        }
        return best;
    }

    private int? TargetAssistedBy(int rescuerId)
    {
        foreach ((int target, int rescuer) in _assistedBy)
            if (rescuer == rescuerId)
                return target;
        return null;
    }

    private void BreakAssistsBy(int rescuerId)
    {
        if (TargetAssistedBy(rescuerId) is int target)
            _assistedBy.Remove(target);
    }

    /// <summary>Every standing assist re-checked against the world this tick. An assist that
    /// survives its own preconditions going away is how a player ends up being dragged by
    /// somebody who is unconscious on the other side of camp.</summary>
    private void ValidateAssists()
    {
        _scratchIds.Clear();
        _scratchIds.AddRange(_assistedBy.Keys);
        foreach (int target in _scratchIds)
        {
            int rescuerId = _assistedBy[target];
            SandboxAvatar? targetAvatar = AvatarFor(target);
            SandboxAvatar? rescuerAvatar = AvatarFor(rescuerId);
            if (targetAvatar == null || rescuerAvatar == null
                || !targetAvatar.IncapacitatedNow
                || rescuerAvatar.ControlDeniedNow)
            {
                _assistedBy.Remove(target);
                continue;
            }
            // Shaking needs you standing over them; dragging gets a longer leash, because the
            // block trails behind the dragger by design and a tether measured at the shake radius
            // would snap on the first step.
            float limit = targetAvatar.IncapacityNow == IncapacityState.Frozen
                ? IncapacityRules.DragBreakM
                : IncapacityRules.AssistRadiusM;
            if (rescuerAvatar.GlobalPosition.DistanceSquaredTo(targetAvatar.GlobalPosition) > limit * limit)
                _assistedBy.Remove(target);
        }
    }

    private void AdvanceMachines(float dt)
    {
        _scratchIds.Clear();
        _scratchIds.AddRange(_machines.Keys);
        foreach (int peerId in _scratchIds)
        {
            IncapacitationMachine m = _machines[peerId];
            SandboxAvatar? avatar = AvatarFor(peerId);
            var inputs = IncapacitationMachine.AssistInputs.None;
            if (avatar != null)
            {
                bool assisted = _assistedBy.ContainsKey(peerId);
                bool warm = _warmthProbe?.Invoke(avatar.GlobalPosition) ?? false;
                inputs = new IncapacitationMachine.AssistInputs(assisted, warm);
            }

            IncapacityState from = m.State;
            IncapacityStep step = m.Advance(dt, inputs);
            switch (step)
            {
                case IncapacityStep.Woke:
                case IncapacityStep.Thawed:
                    _assistedBy.Remove(peerId);
                    CommitTransition(peerId, m, from, IncapacityCause.None, m.LastExit);
                    break;
                case IncapacityStep.ImpulseEnded:
                    avatar?.ServerCommitIncapacity(m.State, false);
                    break;
            }
        }
    }

    /// <summary>
    /// The drag. A frozen body that has drifted past <see cref="IncapacityRules.DragTetherM"/>
    /// from its dragger is pulled toward them at <see cref="IncapacityRules.DragSpeedMps"/>.
    ///
    /// <para><b>The block never propels itself and the dragger never writes its transform.</b> The
    /// velocity goes onto the block's own authoritative <c>MoveState</c> and
    /// <c>AvatarMotor.Step</c> — still the one and only transform writer, cascade row 2 — moves
    /// it, collides it, and drops it down slopes. Which is also why it slides comically: it is a
    /// body being shoved along the ground by the same code that moves everyone else, with no
    /// steering of its own.</para>
    ///
    /// <para>Inside the tether the block is left entirely alone, so it coasts to a stop under the
    /// motor's own deceleration instead of being pinned — the slide, not a leash.</para>
    /// </summary>
    private void ApplyDragMotion()
    {
        foreach ((int target, int rescuerId) in _assistedBy)
        {
            SandboxAvatar? block = AvatarFor(target);
            SandboxAvatar? dragger = AvatarFor(rescuerId);
            if (block == null || dragger == null || block.IncapacityNow != IncapacityState.Frozen)
                continue;
            Vector3 to = dragger.GlobalPosition - block.GlobalPosition;
            to.Y = 0;
            float dist = to.Length();
            if (dist <= IncapacityRules.DragTetherM || dist < 0.001f)
                continue;
            block.ServerSetDragVelocity(to / dist * IncapacityRules.DragSpeedMps);
        }
    }

    /// <summary>
    /// Re-asserts the two replicated bits onto every avatar every tick.
    ///
    /// <para>Unconditional rather than on-change, and copied deliberately from
    /// <c>SandboxAvatar.ServerSetWaterFlags</c>'s own reasoning: they are two bits inside a struct
    /// that is rewritten every tick anyway, and an on-change version needs edge bookkeeping that
    /// can drift out of step with the machine's. It is also what makes a peer who reconnects, or
    /// an avatar that respawns mid-state, correct within one tick rather than within one
    /// transition that may never come.</para>
    /// </summary>
    private void PushCommits()
    {
        foreach ((int peerId, IncapacitationMachine m) in _machines)
            AvatarFor(peerId)?.ServerCommitIncapacity(m.State, m.ImpulseRagdolled);
    }

    private void UpdateAllIncapacitatedLatch()
    {
        bool all = AllConnectedIncapacitated;
        if (all == _allIncapacitatedLatch)
            return;
        _allIncapacitatedLatch = all;
        if (all)
            AllIncapacitatedSignal?.Invoke();
    }

    // --- The cascade -------------------------------------------------------------------------------

    /// <summary>
    /// Applies one transition across every dependent system and, only if the cascade committed,
    /// lets the machine's advance stand.
    ///
    /// <para><b>This is where atomicity is bought.</b> <see cref="IncapacityCascade.Apply"/> runs
    /// every revocable row before the replicated commit, so a failure before that line means
    /// nothing anywhere changed — and this method then <i>rolls the machine back</i>, so the
    /// service's own idea of the state matches the world's. Without the rollback the machine
    /// would believe a transition that no other system ever saw, which is the one-sided version
    /// of exactly the defect the whole cascade table exists to prevent.</para>
    /// </summary>
    private bool CommitTransition(int peerId, IncapacitationMachine m, IncapacityState from,
        IncapacityCause cause, IncapacityExit exit = IncapacityExit.None,
        IncapacityCause fromCause = IncapacityCause.None,
        InjuryMark fromInjuries = InjuryMark.None)
    {
        CascadeOutcome outcome = IncapacityCascade.Apply(
            this, peerId, from, m.State, cause, m.ImpulseRagdolled);

        if (!outcome.Committed)
        {
            ServerLog.Warn("incapacity cascade aborted",
                $"peer={peerId} {from}->{m.State} failedAt={outcome.FailedAt} err={outcome.Error}");
            // Roll the machine back to the state every other system still believes — including
            // the injury ledger, which a plain Reset would have silently wiped.
            m.RollBackTo(from, fromCause, fromInjuries);
            return false;
        }
        if (!outcome.Clean)
            // Committed, but the irreversible tail failed. The state is consistent everywhere and
            // the player kept some items; see IncapacityCascade's class doc for why this ordering
            // makes that the mild failure rather than the bad one.
            ServerLog.Warn("incapacity cascade committed with a failed tail",
                $"peer={peerId} {from}->{m.State} failedAt={outcome.FailedAt} err={outcome.Error}");

        if (m.State != IncapacityState.Active)
            BroadcastEvent(IncapacityEventKind.Down, peerId, m.State, cause, IncapacityExit.None);
        else
            BroadcastEvent(IncapacityEventKind.Up, peerId, m.State, IncapacityCause.None, exit);
        return true;
    }


    // --- IIncapacitySink: the production implementation of each cascade row ------------------------

    /// <summary>Rows 7, 9, 14, 22 — reach, targeting and world UI. Nothing to push: every one of
    /// those systems reads the same replicated predicate off the avatar
    /// (<c>SandboxAvatar.ControlDeniedNow</c>, gated in <c>HandleCarryIntent</c> and enforced in
    /// <c>PropManager.ControlDenied</c>), so the gate is <i>derived</i> rather than <i>set</i>.
    ///
    /// <para>It is still a row and it is still called, deliberately. The row's question is "did
    /// this system get told", and the honest answer here is "it asks, so it cannot fail to be
    /// told" — which is a stronger answer than a push, not a missing one. The call exists so the
    /// cascade's row list matches the table's, and so this reasoning has somewhere to live.</para></summary>
    void IIncapacitySink.SetInteractionGates(int peerId, bool denied)
    {
        // Verifying the avatar resolves is not ceremony: if it does not, the gates genuinely are
        // NOT in place, because there is no replicated field for anything to read.
        if (AvatarFor(peerId) == null)
            throw new InvalidOperationException(
                $"no avatar for peer {peerId}; interaction gates cannot be established");
    }

    /// <summary>Row 5 — the camera.</summary>
    void IIncapacitySink.SetCameraDetached(int peerId, bool detached)
        => AvatarFor(peerId)?.ApplyCameraDetach(detached);

    /// <summary>Rows 6, 11, 12, 20 — nameplate, avatar visual, presentation events, legibility.
    /// The nameplate re-anchors itself off the replicated state each frame
    /// (<c>SandboxAvatar.UpdateNameplate</c>) and the visual skin is driven from it in
    /// <c>AnimateVisual</c>, so this row's job is to raise the one-shot the presentation layer
    /// listens for. No <c>ActorEvent</c> ordinal is spent — this service raises its own event
    /// stream, exactly as <c>WaterService</c> does, rather than reusing the reserved 4/5
    /// slots.</summary>
    void IIncapacitySink.SetLegibility(int peerId, IncapacityState state, IncapacityCause cause)
        => AvatarFor(peerId)?.ApplyIncapacitySkin(state, cause);

    /// <summary>Rows 1, 2, 3, 4 — the commit.</summary>
    void IIncapacitySink.CommitReplicatedState(int peerId, IncapacityState state, bool impulseRagdoll)
    {
        SandboxAvatar? avatar = AvatarFor(peerId);
        if (avatar == null)
            throw new InvalidOperationException(
                $"no avatar for peer {peerId}; the replicated state cannot be committed");
        avatar.ServerCommitIncapacity(state, impulseRagdoll);
    }

    /// <summary>Row 8 — everything you carry scatters.</summary>
    void IIncapacitySink.ScatterCarried(int peerId) => _scatterCarried?.Invoke(peerId);

    // --- Replication -------------------------------------------------------------------------------

    private enum IncapacityEventKind : byte { Down = 0, Up = 1, Impulse = 2 }

    private void BroadcastEvent(IncapacityEventKind kind, int peerId, IncapacityState state,
        IncapacityCause cause, IncapacityExit exit)
    {
        // Applied locally AND sent, never sent-and-looped-back: the server is a real peer in this
        // codebase's hosted-server topology and an Rpc does not deliver to self. Same idiom, same
        // reason, as WaterService.BroadcastEvent.
        ApplyEvent((byte)kind, peerId, (byte)state, (byte)cause, (byte)exit);
        Rpc(MethodName.ReceiveIncapacityEvent, (byte)kind, peerId, (byte)state, (byte)cause, (byte)exit);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.IncapacityChannel)]
    private void ReceiveIncapacityEvent(byte kind, int peerId, byte state, byte cause, byte exit)
        => ApplyEvent(kind, peerId, state, cause, exit);

    private void ApplyEvent(byte kind, int peerId, byte state, byte cause, byte exit)
    {
        // Defensive by contract, exactly like NetCodec's parsers and WaterService.ApplyEvent: a
        // malformed or hostile packet yields a dropped event, never an out-of-range enum cast and
        // never an exception inside a Godot C# network callback (which silently swallows the rest
        // of that callback's work).
        if (kind > (byte)IncapacityEventKind.Impulse
            || state > (byte)IncapacityState.Frozen
            || cause > (byte)IncapacityCause.Debug
            || exit > (byte)IncapacityExit.Dawn)
            return;
        Synced = true;
        switch ((IncapacityEventKind)kind)
        {
            case IncapacityEventKind.Down:
                Incapacitated?.Invoke(peerId, (IncapacityState)state, (IncapacityCause)cause);
                break;
            case IncapacityEventKind.Up:
                Recovered?.Invoke(peerId, (IncapacityExit)exit);
                break;
            case IncapacityEventKind.Impulse:
                ImpulseRagdolled?.Invoke(peerId);
                break;
        }
    }

    private void BroadcastProgress()
    {
        foreach ((int peerId, IncapacitationMachine m) in _machines)
        {
            if (!m.Incapacitated)
                continue;
            Rpc(MethodName.ReceiveAssistProgress, peerId, m.AssistProgress01, ++_seq);
        }
    }

    private readonly Dictionary<int, float> _clientProgress = new();
    private readonly Dictionary<int, uint> _lastProgressSeq = new();

    /// <summary>Assist progress for a rescuer's own feedback (INTERACTION-BIBLE §2 — a verb that
    /// takes five seconds must show that it is working). Unreliable, throttled, and guarded by a
    /// per-peer sequence so a reordered datagram cannot walk a progress bar backwards — the same
    /// latest-wins staleness guard <c>CycleDriver.Apply</c> uses on its own unreliable stream.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable,
        TransferChannel = NetProfile.IncapacityChannel)]
    private void ReceiveAssistProgress(int peerId, float progress, uint seq)
    {
        if (!float.IsFinite(progress))
            return;
        if (_lastProgressSeq.TryGetValue(peerId, out uint last) && seq <= last)
            return;
        _lastProgressSeq[peerId] = seq;
        _clientProgress[peerId] = Mathf.Clamp(progress, 0f, 1f);
    }

    /// <summary>Assist progress as this peer knows it — the server's own machine when we are the
    /// server, the last streamed value otherwise.</summary>
    public float KnownAssistProgress(int peerId) => _isServer
        ? AssistProgressOf(peerId)
        : _clientProgress.TryGetValue(peerId, out float p) ? p : 0f;

    /// <summary>Server-only: called by Gameplay for every joining or resuming peer, the exact call
    /// site <c>CycleDriver.SendPhaseTo</c>, <c>RunDriver.SendRunStateTo</c> and
    /// <c>PropManager.SendDumpTo</c> already use. A late joiner must be told who is already down —
    /// the replicated <c>MoveState</c> carries the state itself, but not the CAUSE, and the cause
    /// is what selects the skin.</summary>
    public void SendStateTo(int peerId)
    {
        if (!_isServer)
            return;
        RpcId(peerId, MethodName.SyncDumpTo, PackDump(), ++_seq);
    }

    /// <summary>Six bytes per entry: a full int32 peer id, then state, then cause.
    ///
    /// <para><b>The peer id is four bytes, not one.</b> Godot's ENet peer ids are
    /// <c>generate_unique_id()</c> values — effectively random 31-bit ints, not a small
    /// sequence — so a byte-wide id would alias two players onto each other or silently address a
    /// third who does not exist. Cheap to get wrong and invisible until a session happens to deal
    /// out an id above 255.</para></summary>
    private const int DumpEntryBytes = 6;

    private byte[] PackDump()
    {
        var buf = new byte[_machines.Count * DumpEntryBytes];
        var span = buf.AsSpan();
        int i = 0;
        foreach ((int id, IncapacitationMachine m) in _machines)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[i..], id);
            span[i + 4] = (byte)m.State;
            span[i + 5] = (byte)m.Cause;
            i += DumpEntryBytes;
        }
        return buf;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.IncapacityChannel)]
    private void SyncDumpTo(byte[] packed, uint seq)
    {
        // Defensive by contract, like every other parser here: a malformed dump is dropped whole
        // rather than half-applied.
        if (packed == null || packed.Length % DumpEntryBytes != 0)
            return;
        Synced = true;
        var span = packed.AsSpan();
        for (int i = 0; i + DumpEntryBytes <= packed.Length; i += DumpEntryBytes)
        {
            int peerId = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span[i..]);
            byte state = span[i + 4];
            byte cause = span[i + 5];
            if (state > (byte)IncapacityState.Frozen || cause > (byte)IncapacityCause.Debug)
                continue;
            if (state != (byte)IncapacityState.Active)
                Incapacitated?.Invoke(peerId, (IncapacityState)state, (IncapacityCause)cause);
        }
    }

    // --- Bookkeeping -------------------------------------------------------------------------------

    private IncapacitationMachine MachineFor(int peerId)
    {
        if (_machines.TryGetValue(peerId, out IncapacitationMachine? m))
            return m;
        m = new IncapacitationMachine();
        _machines[peerId] = m;
        return m;
    }

    private void PruneMissing()
    {
        if (_machines.Count == _scratchIds.Count)
            return;
        var stale = new List<int>();
        foreach (int id in _machines.Keys)
            if (!_scratchIds.Contains(id))
                stale.Add(id);
        foreach (int id in stale)
            ForgetPeer(id);
    }

    private SandboxAvatar? AvatarFor(int peerId)
    {
        foreach (SandboxAvatar avatar in SandboxAvatar.Live)
            if (avatar.OwnerPeerId == peerId)
                return avatar;
        return null;
    }
}
