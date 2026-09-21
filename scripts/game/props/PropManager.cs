using Godot;
using MpFoundation.Net;
using MpFoundation.Game.Presentation;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.Props;

/// <summary>
/// The single netcode funnel for objects, living under <c>Gameplay</c>. On the server it owns the
/// authoritative <see cref="PropRegistry"/> and drives the <c>PropSpawner</c>; on every peer it
/// provides the shared spawn function so a prop is born identically everywhere. The reliable
/// ownership/discrete events, the late-join dump, and the loose-transform stream all hang off
/// this same node (a stable node name means its RPCs route on every peer).
/// </summary>
public partial class PropManager : Node, Sail.Game.Run.IMapScopedSlice
{
    public const string NodeName = "PropManager";

    /// <summary><b>The one prop manager in this process</b>, or null before <c>Gameplay</c> has
    /// built it. Written in <see cref="Setup"/> and cleared in <see cref="_ExitTree"/> — the
    /// <c>HideSeekDriver.Instance</c> / <c>RunDriver.Instance</c> / <c>CycleDriver.Instance</c>
    /// idiom this repo already uses three times, added here for the same reason they exist.
    ///
    /// <para>Added by DOOR-1 (2026-09-19), whose burst is raised by a node in the LEVEL: the door
    /// lives under <c>Gameplay/World/Supermarket/TaskRoom</c> and the manager under
    /// <c>Gameplay/PropManager</c>, so without this the door's two server-side calls
    /// (<see cref="ServerBurstImpulse"/> and <see cref="ScatterHeldBy"/>) would have to walk the
    /// tree by a hard-coded relative path that changes the day anyone re-parents a room.</para></summary>
    public static PropManager? Instance { get; private set; }

    /// <summary>How much further than the client's own reach the server will accept a grab.
    ///
    /// Client and server reach are ONE number with a stated tolerance, not two independently
    /// authored constants — they were 1.5 m client and 3.0 m server, a 2x divergence nobody
    /// had actually decided on (INTERACTION-BIBLE 4).
    ///
    /// The tolerance is not slack for its own sake: the client tests reach against its
    /// *predicted* position and the server against the *authoritative* one, and those differ
    /// by up to a round trip of movement. At the 4.86 m/s sprint ceiling
    /// (<c>AvatarMotor.MoveSpeed</c> x <c>SprintMultiplier</c>), 0.75 m is ~155 ms of travel
    /// — enough that a legitimately in-range grab is never rejected for lag, tight enough
    /// that the server is not quietly handing out double reach.</summary>
    internal const float GrabRangeTolerance = 0.75f;

    /// <summary>Server-side grab range: the client's reach plus the latency tolerance.</summary>
    private const float GrabRange = SandboxAvatar.PickupRadius + GrabRangeTolerance;

    /// <summary>Grab range, squared (server-side proximity check uses authoritative positions).</summary>
    private const float GrabRangeSq = GrabRange * GrabRange;

    /// <summary>Test hook: the derived server reach, so the self-test can assert it stays
    /// tied to the client's without reaching into private state.</summary>
    internal const float TestGrabRange = GrabRange;

    /// <summary>Why a grab request was refused. Ordinals cross the wire — append only.</summary>
    public enum GrabDenial
    {
        None = 0,
        /// <summary>The single-slot rule: holding anything refuses every further grab. Put the
        /// thing in your hand down (drop or throw) to take another. A refusal rather than a
        /// silent swap, so nothing a player is carrying is ever released by a press that was
        /// meant as a pickup.</summary>
        HandsFull = 1,
        /// <summary>Outside the server's authoritative reach.</summary>
        OutOfRange = 2,
        /// <summary>Someone else won the first-grab-wins race.</summary>
        Taken = 3,
        /// <summary>The prop is unknown, despawned, or has no node.</summary>
        Gone = 4,

        /// <summary>You are already carrying this exact prop. Reachable whenever the client's
        /// view of its own hand lags the server's — press E, packet in flight, press E again —
        /// and the honest answer is neither "taken" (nobody stole it) nor silence. It used to be
        /// a bare return, which meant that second press produced literally nothing: no cue, no
        /// reason, indistinguishable from an unresponsive game (INTERACTION-BIBLE 2, the same
        /// defect class the other four ordinals exist to avoid). Appended; ordinals ride the
        /// wire.</summary>
        AlreadyHeld = 5,
    }

    /// <summary>Why a PLACE request was refused. Ordinals cross the wire — append only.
    ///
    /// <para><b>Its own enum rather than five more <see cref="GrabDenial"/> ordinals</b> (CARRY-1).
    /// The two verbs refuse for disjoint reasons — nothing about a grab can be "it doesn't fit
    /// there", nothing about a place can be "someone else won the race" — and merging them would
    /// mean every consumer of a grab refusal switching over reasons a grab can never produce.
    /// Everything else about the channel is deliberately identical to <see cref="GrabDenial"/>'s:
    /// reliable, addressed to the one peer that asked, and never silent, because a refusal the
    /// player cannot perceive is the defect class (INTERACTION-BIBLE §2), not an
    /// implementation choice.</para></summary>
    public enum PlaceDenial
    {
        None = 0,

        /// <summary>The sender is not holding that prop (or anything). Reachable on a lagged
        /// double-press, the same way <see cref="GrabDenial.AlreadyHeld"/> is.</summary>
        NotHolding = 1,

        /// <summary>The holder's own body is outside the server's authoritative reach of the prop.
        /// The same slack rule grab uses — see <see cref="GrabRangeTolerance"/>.</summary>
        OutOfRange = 2,

        /// <summary>The intended transform is further from the holder's hand than
        /// <see cref="PlaceReachM"/>. This is the "you cannot set it down over there" case, and it
        /// is the one a doctored client would try.</summary>
        TooFarToPlace = 3,

        /// <summary>Placement integrity, test 2: the prop's shape at the intended transform
        /// penetrates static geometry or another prop deeper than
        /// <see cref="PlaceOverlapToleranceM"/>. "Doesn't fit there."</summary>
        DoesNotFitThere = 4,

        /// <summary>Placement integrity, test 1: the intended transform is not inside any room's
        /// authored bounds volume.</summary>
        OutsideRoom = 5,

        /// <summary>The prop is unknown, despawned, or has no node — or has no collision shape to
        /// test with, which is a level defect rather than a player action but must still refuse
        /// rather than wave the placement through.</summary>
        Gone = 6,

        /// <summary>A registered <see cref="IPlacementValidator"/> (a task pad, a bin) said no.
        /// The validator picks the ordinal it refuses with; this is the generic one for a surface
        /// that simply does not accept this prop.</summary>
        NotAllowedHere = 7,
    }

    /// <summary>Raised on the requesting peer when the server refuses a grab. The avatar
    /// subscribes to turn it into a cue the player can actually perceive.</summary>
    public event System.Action<GrabDenial>? GrabDenied;

    /// <summary>Raised on the requesting peer when the server refuses a PLACE. Same contract as
    /// <see cref="GrabDenied"/> — the avatar turns it into shake + click + the reason text.</summary>
    public event System.Action<PlaceDenial>? PlaceDenied;

    /// <summary>Test hook: the most recent denial delivered to this peer.</summary>
    internal GrabDenial LastDenial { get; private set; } = GrabDenial.None;

    /// <summary>The most recent place refusal delivered to this peer. Public (unlike
    /// <see cref="LastDenial"/>) because BotHarness logs it: a scene suite proving "the wall
    /// placement was refused, and refused for the RIGHT reason" needs the ordinal in the JSONL,
    /// and a suite that could only see "the prop is still held" could not tell a correct refusal
    /// from a dropped packet.</summary>
    public PlaceDenial LastPlaceDenial { get; private set; } = PlaceDenial.None;

    /// <summary>How far from the holder's HAND the intended transform of a place may sit, metres.
    ///
    /// <para>This is not the reach to the prop (that is <see cref="GrabRange"/>, unchanged and
    /// still checked) — it is how far the object itself may end up from the hand that is setting
    /// it down, and it is small on purpose. The verb is "put it where I am holding it": the player
    /// aims, the object is already out in front of them on the spring, and E sets it down there.
    /// A generous number here would quietly turn the verb into telekinesis and, worse, would hand
    /// a hider a way to post the target through a shelf they cannot reach.</para>
    ///
    /// <para>0.9 m is a little over the spring's own rest offset from the body, so the honest
    /// placement — exactly where the spring is holding the thing — always clears it with room for
    /// a round trip of lag, and nothing much further does.</para></summary>
    public const float PlaceReachM = 0.9f;

    /// <summary>Placement integrity's overlap tolerance for this game — see
    /// <see cref="PlacementIntegrity.DefaultOverlapToleranceM"/>. Named here as well so the knob
    /// a playtest would turn is in the same file as the verb it governs.</summary>
    public const float PlaceOverlapToleranceM = PlacementIntegrity.DefaultOverlapToleranceM;

    /// <summary>
    /// REACH-1, program doc §5b layer 2: how far the rest audit may push a prop to get it out of
    /// something, metres.
    ///
    /// <para>0.15 m is the packet's value and it is a deliberate ceiling rather than a
    /// tolerance. It is large enough to cover every overlap the solver actually produces on a
    /// settle (a crate that rolled a few centimetres into a wall) and far too small to move a
    /// crate out of the middle of a 1 m pillar — which is the point: a push that big would be
    /// the server silently relocating the object the hider chose a place for. Past this, the
    /// correction is "back to the last good transform", where the prop demonstrably fitted.</para>
    /// </summary>
    public const float DepenetrateMaxM = 0.15f;

    /// <summary>
    /// <b>A surface's own opinion about where a placed prop goes</b>, or null for free placement,
    /// which is the default and the game's normal verb. Server-side only; see
    /// <see cref="IPlacementValidator"/> for the contract and for why TASK-1's pads plug in here
    /// rather than teaching this class about pads.
    /// </summary>
    public IPlacementValidator? PlacementValidator { get; set; }

    // --- REACH-1: layer 2's public surface ---------------------------------------------------

    /// <summary>
    /// Server-only: raised every time a prop latches Resting (or is recovered off the kill
    /// plane), with the audit that ran on that latch. <b>This is the "re-evaluate when the target
    /// latches Resting" half of §5b layer 3</b> — the reachability fact source subscribes here
    /// rather than polling, so an audit costs one evaluation per settle and not one per tick.
    /// </summary>
    public event System.Action<int, RestAudit.Result>? RestLatched;

    /// <summary>Rest audits run since this manager was set up. Server-only.</summary>
    public long RestAuditCount { get; private set; }

    /// <summary>Rest audits that MOVED a prop. The ratio of this to
    /// <see cref="RestAuditCount"/> is the honest "how often does the physics actually clip a
    /// prop into something" number, which nothing in this lineage has ever measured.</summary>
    public long RestCorrectionCount { get; private set; }

    /// <summary>Physics queries the rest audits have issued, counted inside the code that issues
    /// them. The packet asks for queries/second under load, and an estimate from
    /// <see cref="RestAuditCount"/> would be wrong by whatever the correction path costs.</summary>
    public long IntegrityQueryCount { get; private set; }

    /// <summary>Audits that ended <see cref="RestAudit.Outcome.Stuck"/> — a prop that could
    /// neither be pushed out nor returned anywhere legal. Never expected to be non-zero in a
    /// real session; the count exists so a playtest can say so rather than assume it.</summary>
    public long StuckPropCount { get; private set; }

    // The last audit per prop, so layer 3 can ask "is this one inside the scenery" without
    // re-running layer 2 (two answers to one question is the defect PlacementIntegrity's own
    // header warns about). Server-only; small, one entry per prop that has ever rested.
    private readonly System.Collections.Generic.Dictionary<int, RestAudit.Result> _lastAudit = new();

    /// <summary>The most recent rest audit for a prop, or null if it has never latched Resting
    /// on this server. Null is meaningful: "never audited" is not "audited and fine".</summary>
    public RestAudit.Result? LastAuditFor(int propId) =>
        _lastAudit.TryGetValue(propId, out RestAudit.Result r) ? r : null;

    /// <summary>Every prop this peer has a node for, in registry order. Server-side the registry
    /// is authoritative, so this is every prop in the world; used by the reachability sampler to
    /// build its exclusion set and to classify a ray hit as movable.</summary>
    public System.Collections.Generic.IEnumerable<NetworkedProp> LiveProps
    {
        get
        {
            foreach (PropState p in _registry.All)
            {
                NetworkedProp? node = NodeFor(p.Id);
                if (node != null && GodotObject.IsInstanceValid(node))
                    yield return node;
            }
        }
    }

    /// <summary>
    /// <b>Layer 2, the whole of it, in one server-side method.</b> Audits <paramref name="propId"/>
    /// where it currently is, applies the correction §5b prescribes (depenetrate, else last
    /// good), latches the prop Resting at the resulting transform, broadcasts it through the
    /// ordinary funnel every peer already converges on, and raises
    /// <see cref="RestLatched"/>.
    ///
    /// <para><b>Three callers, one implementation, deliberately.</b> The settle latch in
    /// <c>_PhysicsProcess</c> is the live one; the planted self-test calls it on an authored pose
    /// (a prop that is frozen at rest has never latched, so there is no event to wait for and
    /// re-implementing the latch in a test would be testing the test); and §5b layer 3's
    /// precondition — "the target must be Resting and must pass layer 2" — calls it on the
    /// Confirm press. Two implementations of "may this prop stay here" would be two answers to
    /// one question, which is the same argument <see cref="PlacementIntegrity"/>'s header makes
    /// about layer 1.</para>
    ///
    /// <para>Off-server, or for an unknown prop, it does nothing and reports a default result
    /// (<see cref="RestAudit.Reason.None"/> / <see cref="RestAudit.Outcome.Good"/> with zero
    /// queries) — "there was nothing to audit" rather than "the audit passed".</para>
    /// </summary>
    /// <summary>What the registry says this prop is doing, or <c>null</c> for an id it does not
    /// know. Read-only, and the whole reason it is public: REACH-1's Confirm-time precondition
    /// has to be able to tell a target that is AT REST from one that is still in the air, and
    /// <see cref="ServerAuditRest"/> cannot answer that for it — the settle latch calls that
    /// method on a prop that is still <see cref="PropMode.Loose"/>, which is exactly the tick it
    /// is latching (REVIEW-1 C2, 2026-09-20).</summary>
    public PropMode? ModeOf(int propId) =>
        _registry.TryGet(propId, out PropState s) ? s.Mode : null;

    public RestAudit.Result ServerAuditRest(int propId)
    {
        if (!_isServer)
            return default;
        NetworkedProp? node = NodeFor(propId);
        if (node == null || !GodotObject.IsInstanceValid(node) || node.Body == null)
            return default;
        // NEVER on a held prop. The correction ends in SettleToRest, which unbinds the prop from
        // its holder — so auditing something in somebody's hands would take it out of them, and
        // the one caller that could reach this case (the Confirm-time precondition, on a hider
        // who has not put the object down) is precisely the one the round is about to refuse for
        // a different reason.
        if (_registry.TryGet(propId, out PropState held) && held.Mode == PropMode.Held)
            return default;

        Transform3D at = node.Body.GlobalTransform;
        RestAudit.Result audit = RestAudit.Audit(node, at, PlaceOverlapToleranceM, DepenetrateMaxM);

        // PHYS-1 (2026-09-20), ruling P3: THE AUDIT NEVER TELEPORTS A PROP THE PLAYER IS LOOKING
        // AT MOVING. Talon: "I want to know they won't freak out and make other objects jump
        // around randomly" -- and a prop snapping back to a pose it held ten seconds ago, half a
        // metre in front of the person who just nudged it, is that sentence exactly, even though
        // every line of REACH-1 is working as designed.
        //
        // DEPENETRATION IS NOT GATED and that is deliberate: pushing a crate 4 cm out of a wall
        // is the common case, it keeps the prop where the player left it, and gating it would
        // regress the defect REACH-1 exists to fix. Only the LAST-GOOD RESTORE waits.
        if (IsRestore(audit.Outcome) && !MayRestoreNow(propId, node))
        {
            // BOOK THE COST EVEN THOUGH NOTHING MOVED. The shape queries were issued; leaving
            // them off RestAuditCount/IntegrityQueryCount would quietly under-report REACH-1's
            // "one shape query per settle event" costing, which is the number that exercise
            // exists to protect. What is NOT booked is the CORRECTION: nothing was corrected,
            // and a `[reach] layer2 ... RestoredLastGood` line for a prop that did not move
            // would be a lie the next reader greps for.
            BookAuditCost(propId, audit);
            // And re-arm the settle counter so the retry is one per SettleTicks (~0.3 s) rather
            // than one per TICK. Without this the counter stays at its latch value, this method
            // is called sixty times a second for as long as the prop waits, and a prop a player
            // is standing next to waits indefinitely by design -- so the gate would turn a rare
            // correction into a permanent 60 Hz shape query.
            _looseSettle[propId] = 0;
            return audit;
        }
        ClearStuckClock(propId);

        NoteAudit(propId, audit);
        Transform3D settled = audit.Corrected ? audit.To : at;
        _registry.SetResting(propId, settled);
        node.SettleToRest(settled);
        // PropRelease.None (SFX-2's table: every Resting broadcast is None). A settle is the END
        // of a release, not a release — the verb that started this roll was announced when the
        // prop went Loose, and re-announcing it here would clank a second time on every peer.
        Rpc(MethodName.ApplyPropState, propId, (int)PropMode.Resting, 0, settled,
            (int)PropRelease.None);
        _looseSettle.Remove(propId);
        _lastStreamed.Remove(propId);
        RestLatched?.Invoke(propId, audit);
        return audit;
    }

    /// <summary>
    /// Server-side dev hook: put a resting prop back into loose physics with
    /// <paramref name="impulse"/>, from wherever it currently sits.
    ///
    /// <para><b>What it exists for, and nothing else.</b> §5b's sixth planted case is "pushed
    /// through the floor by a scripted impulse", and there is no other way to reach the
    /// kill-plane recovery path from a test: every shipped route into Loose goes through a HELD
    /// prop being dropped or thrown, and the planted room has no players in it. Server-only, and
    /// the only caller in the tree is the self-test the flag <c>--reach-selftest</c> arms.</para>
    /// </summary>
    public void ServerNudgeLoose(int propId, Vector3 impulse)
    {
        if (!_isServer)
            return;
        NetworkedProp? node = NodeFor(propId);
        if (node == null || !_registry.TryGet(propId, out PropState p) || p.Mode == PropMode.Held)
            return;
        Transform3D at = node.Body.GlobalTransform;
        // SetLoose, not Release: Release is the drop/throw verb and refuses anything that is not
        // HELD, which is correct for it and is exactly what this hook needs to get past — the
        // planted room has no players in it. See PropRegistry.SetLoose's own note.
        _registry.SetLoose(propId, at);
        // PropRelease.None (SFX-2). Nobody dropped, placed or threw this — a scripted impulse
        // shoved a RESTING prop, which is not one of the four release verbs. The sound this
        // produces is the impact when it lands, which SFX-2's PropImpact event carries.
        Rpc(MethodName.ApplyPropState, propId, (int)PropMode.Loose, 0, at, (int)PropRelease.None);
        node.BeginLooseServer(impulse);
    }

    // --- PHYS-1 (P3): the audit's restore, gated -------------------------------------------

    /// <summary>When each prop's current run of unfixable audits began, in engine milliseconds.
    /// Absent = not stuck. Cleared by any audit that passed or depenetrated, and by the prop
    /// leaving Loose, so the clock measures one episode rather than a lifetime.</summary>
    private readonly System.Collections.Generic.Dictionary<int, ulong> _stuckSinceMsec = new();

    /// <summary>The last time each waiting prop printed its <c>[phys] rest-wait</c> line. The
    /// settle latch re-audits a waiting prop every <see cref="SettleTicks"/> ticks — three times
    /// a second — and a prop a player is standing next to can wait indefinitely by design, so the
    /// line is throttled to one a second per prop. Throttled rather than dropped: "the audit
    /// wanted to move this and did not" is the whole evidence P3 leaves behind.</summary>
    private readonly System.Collections.Generic.Dictionary<int, ulong> _stuckLoggedMsec = new();

    /// <summary>Outcomes that MOVE the prop somewhere it was not — the ones P3 gates.
    /// <see cref="RestAudit.Outcome.Stuck"/> is included because it writes the last-good
    /// transform too; its only difference from a restore is that the pose it restores to is known
    /// to be bad as well.</summary>
    private static bool IsRestore(RestAudit.Outcome outcome) =>
        outcome is RestAudit.Outcome.RestoredLastGood or RestAudit.Outcome.Stuck;

    private void ClearStuckClock(int propId)
    {
        _stuckSinceMsec.Remove(propId);
        _stuckLoggedMsec.Remove(propId);
    }

    /// <summary>
    /// <b>P3's two conditions, asked of one prop.</b> Advances this prop's stuck clock, measures
    /// the nearest avatar's grab ray, and answers whether the restore may happen now.
    ///
    /// <para>A refusal leaves the prop exactly as it is — still Loose if it was Loose, so the
    /// settle latch brings it back here in another <see cref="SettleTicks"/> ticks and it
    /// restores itself the moment either condition clears. Nothing is dropped and nothing is
    /// retried by a timer of its own.</para>
    /// </summary>
    private bool MayRestoreNow(int propId, NetworkedProp node)
    {
        ulong now = Time.GetTicksMsec();
        if (!_stuckSinceMsec.TryGetValue(propId, out ulong since))
        {
            since = now;
            _stuckSinceMsec[propId] = since;
        }
        float stuckSec = (now - since) / 1000f;
        float nearest = NearestGrabRayM(node.Body.GlobalPosition);
        if (PropRestGate.MayRestoreLastGood(stuckSec, nearest))
            return true;

        // One line a second, naming both quantities, because the two reasons to wait want
        // different responses from whoever reads the log: a clock that is still climbing is the
        // solver being given its second, and a player 0.4 m away is the design refusing on
        // purpose.
        if (!_stuckLoggedMsec.TryGetValue(propId, out ulong lastLog) || now - lastLog >= 1000)
        {
            _stuckLoggedMsec[propId] = now;
            GD.Print($"[phys] rest-wait prop={propId} stuck={stuckSec:F2}s "
                + $"nearestGrabRay={(float.IsPositiveInfinity(nearest) ? "none" : $"{nearest:F2}m")} "
                + $"(needs >={PropRestGate.StuckHoldSec:F2}s and >{PropRestGate.PlayerAttentionM:F2}m)");
        }
        return false;
    }

    /// <summary>Distance from <paramref name="point"/> to the closest point on any live avatar's
    /// grab ray, or <see cref="float.PositiveInfinity"/> when there are no avatars — an empty
    /// room reads as FAR, never as zero, which is what lets the seeker's room correct itself
    /// while the hider is elsewhere.
    ///
    /// <para>The ray is rebuilt from the avatar's replicated aim (yaw + pitch through
    /// <c>AimQuery</c>), never from a camera node, for the reason
    /// <c>SandboxAvatar.AimedSurfaceWithinPlaceReach</c> gives: the server has no camera and the
    /// aim is the thing that is actually replicated.</para></summary>
    private static float NearestGrabRayM(Vector3 point)
    {
        float nearest = float.PositiveInfinity;
        foreach (SandboxAvatar a in SandboxAvatar.Live)
        {
            if (!GodotObject.IsInstanceValid(a) || !a.IsInsideTree())
                continue;
            Vector3 dir = MpFoundation.Game.Aim.AimQuery.DirectionFromYawPitch(a.AimYaw, a.AimPitch);
            float d = PropRestGate.DistanceToGrabRay(point, a.AimOriginGlobalPosition, dir, GrabRange);
            if (d < nearest)
                nearest = d;
        }
        return nearest;
    }

    // --- PHYS-1 (P1): the wake funnel --------------------------------------------------------

    /// <summary>How many times <see cref="ServerClampMotion"/>'s per-tick backstop has reduced a
    /// prop's velocity this session. P2 asks for this to be ~0 in ordinary play, which is a claim
    /// only a counter can support; the suite reads it and the server prints it.</summary>
    public long PropClampCount { get; private set; }

    /// <summary>How many resting props have been woken by a contact this session (P1). Read at
    /// rest by the suite: a wake storm in an untouched room would be 0 here and must stay 0.</summary>
    public long PropWakeCount { get; private set; }

    /// <summary>
    /// <b>Server-only: something moving touched <paramref name="struck"/> — wake it if it is a
    /// RESTING networked prop</b> (P1). The single funnel for all three contact sources: the
    /// holder's own hold sweep, a loose prop's <c>body_entered</c>, and an avatar's slide
    /// collision.
    ///
    /// <para><b>Every decision that is not "was there a contact" lives here</b>, on the server,
    /// in the class that owns prop authority — the physical body reports the contact and knows
    /// nothing else. Held props are refused by <c>PropRegistry.Wake</c> itself (waking something
    /// in a hand would take it off a player without any of the release funnel's broadcasts), and
    /// a prop that is already Loose needs no wake because it is already simulating: the solver is
    /// what moves it, and re-waking it would zero the velocity it already has.</para>
    /// </summary>
    /// <param name="struck">The body that was hit.</param>
    /// <param name="moverMassKg">Mass of the thing that hit it. 0 for a kinematic body nobody
    /// weighed, which <c>PropPhysics.WakeSpeed</c> reads as "the target's equal".</param>
    /// <param name="approachSpeedMps">Closing speed along the contact normal.</param>
    /// <param name="pushDirection">Which way the shove goes. Need not be normalised.</param>
    /// <param name="atWorld">Where the contact happened — the impulse's application point, and
    /// therefore the lever that makes a box topple rather than slide.</param>
    public void ServerBumpProp(Carryable struck, float moverMassKg, float approachSpeedMps,
        Vector3 pushDirection, Vector3 atWorld)
    {
        if (!_isServer || !PropPhysics.ShouldWake(approachSpeedMps))
            return;
        if (!GodotObject.IsInstanceValid(struck) || struck.GetParent() is not NetworkedProp target)
            return;
        if (!_registry.TryGet(target.PropId, out PropState s) || s.Mode != PropMode.Resting)
            return;

        float targetMass = struck.MassKg > 0f ? struck.MassKg : 1f;
        Vector3 impulse = PropPhysics.ContactImpulse(pushDirection, moverMassKg, targetMass,
            approachSpeedMps);
        if (impulse.LengthSquared() <= 0f)
            return;

        Transform3D at = struck.GlobalTransform;
        if (!_registry.Wake(target.PropId, at))
            return;
        ClearStuckClock(target.PropId);
        PropWakeCount++;
        // One line per wake, naming the speed it was woken at against the bar. Wakes are rare by
        // construction (a resting prop wakes once and is then Loose until it settles), so this is
        // not a per-tick log; and it is the evidence for BOTH halves of P1 -- that a contact woke
        // something, and that nothing left with more than the bar. "0 wakes over 30 s untouched"
        // is a claim the suite makes by counting these.
        GD.Print($"[phys] wake prop={target.PropId} at={approachSpeedMps:F2} m/s "
            + $"-> {impulse.Length() / targetMass:F2} m/s total={PropWakeCount}");
        // PropRelease.Bumped (PHYS-1). Nobody dropped, placed or threw this: it was knocked. The
        // byte is spent on the same every-peer path every other release verb uses, so the sound
        // layer can give a knock its own voice without a second message.
        Rpc(MethodName.ApplyPropState, target.PropId, (int)PropMode.Loose, 0, at,
            (int)PropRelease.Bumped);
        target.WakeFromContactServer(impulse, atWorld);
    }

    /// <summary>The half of <see cref="NoteAudit"/> that is about the QUERIES an audit issued
    /// rather than about what it did — split out by PHYS-1 (2026-09-20) so a restore the P3 gate
    /// refused still pays for the work it actually did. <see cref="LastAuditFor"/> is written here
    /// too, deliberately: a prop the gate is waiting on is still in the bad pose, and the
    /// Confirm-time precondition that reads it has to see that.</summary>
    private void BookAuditCost(int propId, RestAudit.Result audit)
    {
        RestAuditCount++;
        IntegrityQueryCount += audit.Queries;
        _lastAudit[propId] = audit;
    }

    /// <summary>Books one audit and logs the ones that acted. A passing audit is silent by
    /// design: 150 props settling after a shove would otherwise print 150 lines saying nothing
    /// happened, and the line that matters would be invisible inside them.</summary>
    private void NoteAudit(int propId, RestAudit.Result audit)
    {
        BookAuditCost(propId, audit);
        if (!audit.Corrected)
            return;
        RestCorrectionCount++;
        if (audit.Outcome == RestAudit.Outcome.Stuck)
        {
            StuckPropCount++;
            // LOUD. A prop that cannot be put anywhere legal is the case §5b exists to prevent,
            // and a warning is how a playtest proves the game did not hit it.
            GD.PushWarning($"[reach] STUCK {audit.Describe(propId)}");
        }
        GD.Print($"[reach] layer2 {audit.Describe(propId)}");
    }

    // --- Loose-physics tuning (server-only; see the _PhysicsProcess loop below) -----------
    /// <summary>Below this linear speed squared (~0.2 m/s), a Loose prop is considered
    /// candidate-at-rest and starts accumulating settle ticks.</summary>
    // PHYS-1 (2026-09-20): written as the square of the named speed rather than as the literal
    // 0.04f it was, so it and SettleSpinSq below cannot drift apart by hand. The value moves by
    // 3e-9 and nothing observable depends on a settle threshold to nine decimal places.
    private const float SettleSpeedSq = SettleSpeedMps * SettleSpeedMps;

    /// <summary><b>The same settle question asked of the spin</b> (PHYS-1, P4, 2026-09-20), rad/s
    /// squared. Without it a can that is still rolling — or a sphere rocking into a shelf lip at
    /// almost no linear speed — latches to Resting and freezes kinematic mid-motion on every
    /// peer, which is precisely the opposite of "cans roll around".
    ///
    /// <para><b>Derived from <see cref="SettleSpeedSq"/> through the smallest prop's radius, not
    /// picked.</b> The linear gate is 0.2 m/s; a can (<c>Carryable.CanRadiusM</c>, 35 mm) rolling
    /// at 0.2 m/s turns at 0.2/0.035 = 5.71 rad/s, so that is the spin a settling can genuinely
    /// has and the two gates close at the same physical moment. Anything blunter would either
    /// freeze a roll or leave a prop that has stopped jittering forever un-latched.</para></summary>
    private const float SettleSpinSq =
        (SettleSpeedMps / Carryable.CanRadiusM) * (SettleSpeedMps / Carryable.CanRadiusM);

    /// <summary>The linear settle speed itself, m/s — <see cref="SettleSpeedSq"/>'s root, named so
    /// <see cref="SettleSpinSq"/> can be written as the arithmetic that derives it rather than as
    /// a second literal that has to be kept in step by hand.</summary>
    private const float SettleSpeedMps = 0.2f;

    /// <summary>Consecutive slow ticks (~0.3s at 60 Hz) required before latching to Resting —
    /// long enough that a prop resting on an unstable stack or mid-bounce doesn't false-latch.</summary>
    private const int SettleTicks = 18;

    // Throw impulse: mirrors SandboxAvatar's own offline throw constants (ThrowForwardSpeed /
    // ThrowUpSpeed) so a networked throw feels identical to the sandbox one.
    private const float ThrowForwardSpeed = 7.5f;
    private const float ThrowUpSpeed = 3.2f;
    // Drop is a much gentler toss than a throw — matches Carryable.OnDropped's offline arc
    // (a light lob off the holder's facing, not a hurl) now that drops go through Loose too.
    private const float DropForwardSpeed = 1.3f;
    private const float DropUpSpeed = 2.2f;

    /// <summary>First id handed to an authored (adopted) prop — see <see cref="AdoptAuthoredProps"/>.
    /// Comfortably above any runtime-spawned prop's id (PropRegistry.Register starts at 1 and a
    /// world seeds at most a handful), so the two id spaces can never collide.</summary>
    private const int AuthoredIdBase = 1000;

    private MultiplayerSpawner _spawner = null!;
    private Node3D _propsRoot = null!;
    private bool _isServer;

    // Server-only source of truth. Clients learn prop state from spawns + events/dumps.
    private readonly PropRegistry _registry = new();

    // Authored props adopted (never spawned) into the netcode — see AdoptAuthoredProps. Populated
    // identically on every peer; on the server each entry is also mirrored into _registry.
    private readonly System.Collections.Generic.Dictionary<int, NetworkedProp> _adopted = new();

    // Server-only: consecutive low-speed ticks per Loose prop id, toward the settle latch.
    private readonly System.Collections.Generic.Dictionary<int, int> _looseSettle = new();

    // Reused every physics tick instead of allocating a fresh List<PropState> — this loop needs
    // a snapshot because a Loose prop settling to Resting mutates the registry mid-iteration, but
    // the snapshot itself doesn't need to be a new heap allocation every single tick (see
    // RISK-AUDIT-2026-07-12.md 2.2).
    private readonly System.Collections.Generic.List<PropState> _loosePropsScratch = new();

    // Server-only loose-stream throttle: a sim-tick counter for the 30 Hz cadence gate, the last
    // transform actually broadcast per prop (for skip-unchanged), and the position epsilon below
    // which a re-broadcast is redundant (~0.5 cm).
    private uint _streamTick;
    private readonly System.Collections.Generic.Dictionary<int, Transform3D> _lastStreamed = new();
    private const float StreamPosEpsilonSq = 0.005f * 0.005f;

    // Which prop is in each peer's HAND, ON EVERY PEER (server included) — the replicated answer
    // to "what is that player carrying". One prop per peer, by rule (see GrabDenial.HandsFull).
    // Maintained solely inside ApplyPropState (Authority + Reliable + CallLocal), which is the
    // single funnel every prop-state change runs through: a Held transition binds the holder, a
    // Resting/Loose transition releases whoever had it. There is no second source of truth for
    // holder state on a client, and on the server this agrees with the registry by construction
    // because both are written from the same broadcast. FindHeldBy is O(1) (a dictionary hit
    // plus NodeFor's own name/adopted lookup) — RISK-AUDIT-2026-07-12.md 2.3's reason for a
    // held-by-peer cache is preserved.
    private readonly System.Collections.Generic.Dictionary<int, int> _heldByPeer = new();

    /// <summary>Peer id -> that peer's avatar node, or null if not found. Set by Gameplay after
    /// avatars are spawnable, so the server can resolve a grabbing/dropping peer's authoritative
    /// position. Unused on clients (they never arbitrate).</summary>
    public System.Func<int, Node3D?>? AvatarResolver { get; set; }

    // The world string SpawnInitialProps last seeded (CORE-PROG-A2) — what the boundary restore
    // re-runs; empty until the server has seeded once.
    private string _world = "";

    // Adopted authored props' adoption-time transforms (CORE-PROG-A2): the boundary restore
    // returns them here rather than despawning them (they were never spawner-spawned).
    private readonly System.Collections.Generic.Dictionary<int, Transform3D> _adoptedInitial = new();

    // Server-only impact queue (SFX-2): contacts accepted by Carryable during the last physics
    // step, waiting to be ranked and announced at the top of the next one. Persistent, like
    // _loosePropsScratch above and for the same reason - a shelf collapse must not allocate a
    // fresh list 60 times a second.
    private readonly System.Collections.Generic.List<ImpactBudget.PendingImpact> _pendingImpacts = new();

    // Running totals for the budget measurement the packet asks for: how many contacts the
    // server was offered against how many it actually sent. Server-only, printed on the tick
    // that drops something so a suite can aggregate without a shutdown hook (a --server process
    // has no BotHarness.Finish to print a summary from).
    private long _impactsOffered;
    private long _impactsSent;

    // The largest number of contacts any single tick has offered. Tracked because a cap that is
    // never reached reports NOTHING, and "the limiter never engaged" is indistinguishable in a
    // log from "the limiter is not wired up". Measured on the 40-prop heap: the peak offer was
    // under the cap, so the per-body 0.4 s cooldown -- not this cap -- is what bounds that
    // fixture. That is a finding, and it needs a number rather than a silence.
    private int _impactsPeakOffered;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Setup(MultiplayerSpawner spawner, Node3D propsRoot, bool isServer)
    {
        Instance = this;
        _spawner = spawner;
        _propsRoot = propsRoot;
        _isServer = isServer;
        // Every peer runs its own copy of this to build the replicated prop locally.
        _spawner.SpawnFunction = new Callable(this, MethodName.SpawnFromData);
        // This manager is still the map-scoped world-state slice it was (see the interface on the
        // class declaration, and ResetForNewPlaythrough below): a round boundary that wants the
        // authored prop layout back calls that method. What is GONE since the fork (BASE-1,
        // 2026-09-19) is the old quota spine's WorldStateStore, which used to be registered here
        // and fan the reset automatically. Nothing fans it today — ROUND-1 owns wiring the
        // hide-seek loop's reset edge to it, and until then the slice is implemented but unfanned.
    }

    /// <summary>Server: spawns the initial networked props for a world. Only the dedicated
    /// "propsync" CI world seeds test props (keeping the plain "open" replication test
    /// prop-free and unperturbed) — a neutral crate/ball pair plus a third crate placed to
    /// prove out-of-bounds recovery (see the throw-test comment below). Every other world
    /// string starts prop-free; a game built on this foundation calls ServerSpawn itself
    /// (or extends this method) to seed its own layout.</summary>
    public void SpawnInitialProps(string world)
    {
        if (!_isServer)
            return;
        // Captured for the playthrough boundary's initial-dump restore (CORE-PROG-A2):
        // ResetForNewPlaythrough re-runs this exact method with this exact string.
        _world = world;

        // --seed-test-props: a CI fixture, in whatever world is running, ahead of the per-world
        // block below (CARRY-1, 2026-08-29). See LaunchOptions.SeedTestProps for why a launch flag
        // rather than level content: the networked-carry proof has to carry a prop through a TV
        // portal, portals live only in bubbletest, and bubbletest is deliberately prop-free. Empty
        // on every launch that did not ask, so this loop does nothing in a real session.
        //
        // Ordinary ServerSpawn, ordinary PropKind: a seeded prop is indistinguishable from an
        // authored one the moment it is in someone's hands, which is the whole point — a fixture
        // with its own carry path would prove nothing about carry. SFX-1 added the per-entry
        // kind so the material suite can seed a mixed heap; the default is still Crate, so every
        // caller written before it is unchanged.
        if (NetworkManager.Instance?.Options.SeedTestProps is { Count: > 0 } seeded)
        {
            foreach ((Vector3 at, PropKind kind) in seeded)
            {
                NetworkedProp? spawned = ServerSpawn(kind, PlaceAt(at));
                if (spawned != null)
                    _seededIds.Add(spawned.PropId);
            }
            GD.Print($"[props] --seed-test-props: seeded {seeded.Count} test prop(s) in world '{world}'");
        }

        if (world != "propsync")
            return;
        ServerSpawn(PropKind.Crate, PlaceAt(new Vector3(2.5f, 0.5f, 2.5f)));
        ServerSpawn(PropKind.Ball, PlaceAt(new Vector3(-2.5f, 0.5f, -2.5f)));
        // Prop 3: near-zero X, deep +Z, close to the open field's finite ground slab's edge
        // (a 64x64 box, so the floor ends at |z|=32). Near-zero X keeps every ring spawn
        // point's straight-line walk to it inside the x in [-4,4] corridor. Run-ThrowTest.ps1's
        // OOB-recovery bot throws it from here — easily clearing the slab edge — so it falls into
        // the void and past the shared kill-plane, proving a networked prop recovers to its
        // spawn transform.
        ServerSpawn(PropKind.Crate, PlaceAt(new Vector3(2f, 0.5f, 30.5f)));
    }

    /// <summary>Server: permanently removes a prop — e.g. a game-specific consumable slot
    /// or hazard claiming it. Frees the server's node, which the MultiplayerSpawner mirrors
    /// as a despawn on every peer (late joiners simply never see it). No take-backs by
    /// design.
    ///
    /// <b>Releases the prop from its holder first, and that is load-bearing, not tidiness.</b>
    /// Nothing about a MultiplayerSpawner despawn tells a peer that the prop has left a hand. So
    /// consuming a prop while somebody was holding it would leave every peer's holder state
    /// naming a freed <see cref="NetworkedProp"/>, and the very next <see cref="FindHeldBy"/> —
    /// which <c>SandboxAvatar.HandleCarryIntent</c> calls on the holder's own next Interact press
    /// — would touch a disposed GodotObject. The release rides a Resting broadcast through
    /// <see cref="ApplyPropState"/>, the one funnel every peer's held-by-peer view is written
    /// from, so every peer learns before the node goes away. The registry removal still happens
    /// first: it is what gates whether this is a real prop at all, and so stops a bogus id from
    /// emitting anything.</summary>
    public bool ServerConsume(int propId)
    {
        if (!_isServer || !_registry.TryGet(propId, out PropState s) || !_registry.Remove(propId))
            return false;
        _looseSettle.Remove(propId);
        _lastStreamed.Remove(propId);
        if (s.Mode == PropMode.Held)
        {
            Transform3D at = NodeFor(propId)?.Body.GlobalTransform ?? s.Transform;
            Rpc(MethodName.ApplyPropState, propId, (int)PropMode.Resting, 0, at, (int)PropRelease.None);
        }
        NodeFor(propId)?.QueueFree();
        return true;
    }

    /// <summary>Which prop kinds the BODY poses as an armful — both arms under a bulky load —
    /// rather than as a one-handed grip on a handle.
    ///
    /// <b>This is a POSE question, not a carry rule</b> (W7-8, 2026-08-30). A 0.44 m golden cube
    /// posed <c>CarryPose.Handle</c> put the wrist ~0.22 m INSIDE the mesh with the other arm on
    /// the gait, which is exactly what Talon saw: <i>"the hands of the player character don't
    /// actually move to indicate anything is being held."</i> Nothing reads this method except the
    /// visual pose; it changes no gameplay at all.
    ///
    /// <b>The rule is "does it have a handle to grip".</b> A crate and a ball do not: both arms,
    /// under and either side. <see cref="CarryPose"/>'s own doc already said this is how it works —
    /// <i>"It names a POSE, never a rule about what may be carried"</i> — the pose layer simply
    /// had no predicate of its own to ask. Every shipped kind is an armful today; the predicate
    /// stays so a future handled kind is one line here rather than a scattered special case.</summary>
    /// <remarks>SFX-1 appended the three product kinds. None of them has a handle either — a can
    /// and an apple are small enough to palm, but the body's pose layer has exactly two poses and
    /// "both hands, under it" is the less wrong of the two for a thing with no grip. A one-handed
    /// pose is a pose-layer packet, not a sound packet.</remarks>
    public static bool IsArmfulPose(PropKind kind) =>
        kind is PropKind.Crate or PropKind.Ball or PropKind.Can or PropKind.Box or PropKind.Produce;

    /// <summary>Which prop kinds ride slightly ABOVE the carry mount so the armful hands end up
    /// under the load instead of inside it — see <c>Carryable.ArmfulLoadLiftFraction</c> for the
    /// geometry and the value. Exactly the armful-posed kinds, expressed through
    /// <see cref="IsArmfulPose"/> rather than as a second literal list so it cannot drift out of
    /// step with the pose predicate.</summary>
    public static bool TakesLoadLift(PropKind kind) => IsArmfulPose(kind);

    // --- --seed-props-drop: let the fixture FALL, once, on a clock (SFX-1, 2026-09-19) --------
    //
    // WHY THIS HAD TO EXIST. A prop from ServerSpawn is born Resting, and NetworkedProp._Ready
    // freezes a Resting prop kinematic on every peer — so a --seed-test-props fixture hangs
    // exactly where it was seeded, in mid-air, forever. That is correct for every suite before
    // this one (they all walk a bot up to a prop and grab it) and it is fatal to SFX-1's
    // voice-budget measurement, whose whole event is forty props landing at once. Measured, not
    // reasoned: the first run of tests/Run-MaterialSfxTest.ps1 seeded forty props 0.5–2.5 m up
    // and the windowed peer logged sixteen sounds, every one of them a footstep.
    //
    // ON A CLOCK rather than at spawn, because SpawnInitialProps runs before any client has
    // connected: a drop there is a drop nobody is present to hear, and by the time a peer joins
    // the late-join dump hands it forty resting props. The delay is what puts the event inside
    // the session.
    //
    // Server-only, off unless asked, and it fires EXACTLY ONCE — a re-drop loop would be a
    // permanent noise source rather than an event.
    private readonly System.Collections.Generic.List<int> _seededIds = new();
    private double _seededDropClock;
    private bool _seededDropFired;

    private void StepSeededDrop(double delta)
    {
        double at = NetworkManager.Instance?.Options.SeedPropsDropAtSec ?? -1;
        if (_seededDropFired || at < 0 || _seededIds.Count == 0)
            return;
        _seededDropClock += delta;
        if (_seededDropClock < at)
            return;
        _seededDropFired = true;
        int dropped = 0;
        foreach (int id in _seededIds)
        {
            NetworkedProp? node = NodeFor(id);
            if (node == null || !_registry.TryGet(id, out PropState s) || s.Mode != PropMode.Resting)
                continue;
            // The ordinary release path, deliberately: the same registry transition, the same
            // reliable broadcast and the same BeginLooseServer a thrown prop takes, so what the
            // budget measures is the real Loose pipeline rather than a test-only shortcut.
            Transform3D pose = node.Body.GlobalTransform;
            _registry.SetHolder(id, 1);
            // Dropped: nothing threw these, they are let go where they were seeded.
            _registry.Release(id, pose, PropRelease.Dropped);
            Rpc(MethodName.ApplyPropState, id, (int)PropMode.Loose, 0, pose,
                (int)PropRelease.Dropped);
            node.DropLooseServer();
            dropped++;
        }
        GD.Print($"[props] --seed-props-drop: released {dropped} seeded prop(s) into Loose at t={_seededDropClock:F2}s");
    }

    /// <summary>Server-only: drives every Loose prop's physics tick. Streams its live transform
    /// to every peer (unreliable — the next tick supersedes a dropped one), latches it to Resting
    /// once it has stayed slow for <see cref="SettleTicks"/> consecutive ticks, and recovers it to
    /// its spawn transform if it ever falls below the shared out-of-bounds kill-plane (thrown
    /// through a wall, off a ledge — mirrors the offline Carryable's own OOB fallback) so a
    /// networked prop can never be permanently lost either.</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (!_isServer)
            return;
        // BEFORE the loose-prop early-out below, deliberately: contacts are reported during the
        // previous step's physics flush, and the props that made them may have settled to
        // Resting since - a can that lands and stops is exactly the case whose clank must not be
        // swallowed because nothing is Loose any more on the tick we get round to sending it.
        FlushImpacts();
        StepSeededDrop(delta);
        _streamTick++;
        // Reuse a persistent scratch list instead of allocating a fresh List<PropState> every
        // physics tick (60 Hz) - this loop needs a snapshot because a Loose prop settling to
        // Resting mutates the registry mid-iteration, but the snapshot itself doesn't need to be
        // a new heap allocation every single tick (see RISK-AUDIT-2026-07-12.md 2.2). Early-out
        // entirely when nothing is Loose - the common case is zero loose props on most ticks.
        // PROBE-1 (2026-09-20): ASK THE STORE HOW MANY ARE LOOSE INSTEAD OF COUNTING THEM.
        //
        // The walk below is O(every prop in the world) and its own comment above says the common
        // case is that none of them is loose — so on an asleep room it was paying the whole cost
        // of the loop to learn there was no loop to run, sixty times a second. Worse than the
        // iteration: `_registry.All` is an IReadOnlyCollection, so `foreach` over it boxes an
        // enumerator on the heap every tick and copies each 64-byte PropState struct through the
        // interface. AllValues is the same collection typed concretely, which gets the
        // dictionary's struct enumerator and allocates nothing.
        //
        // The two Remove calls in the `else` branch are pure cleanup of two dictionaries that can
        // only ever contain ids that WERE loose, so skipping the walk when nothing is loose
        // cannot leak: the last prop to leave Loose is cleaned up by the pass that saw it leave.
        // Belt and braces, both dictionaries are cleared on the zero edge.
        if (PropCostSwitches.LooseIndex && _registry.LooseCount == 0)
        {
            if (_looseSettle.Count > 0)
                _looseSettle.Clear();
            if (_lastStreamed.Count > 0)
                _lastStreamed.Clear();
            return;
        }
        _loosePropsScratch.Clear();
        foreach (PropState p in _registry.AllValues)
        {
            if (p.Mode == PropMode.Loose)
                _loosePropsScratch.Add(p);
            else
            {
                _looseSettle.Remove(p.Id);
                _lastStreamed.Remove(p.Id);
            }
        }
        if (_loosePropsScratch.Count == 0)
            return;

        // Broadcast the transform at 30 Hz (every other 60 Hz sim tick) to match the avatar
        // snapshot cadence — the stream is unreliable latest-wins so clients converge either way,
        // and the reliable Resting transition carries the exact final pose, so throttling can
        // never leave a prop visually stuck (see RISK-AUDIT-2026-07-12.md 2.1). Physics, settle
        // detection, and kill-plane recovery below still run every tick.
        bool streamThisTick = (_streamTick & 1u) == 0u;

        foreach (PropState p in _loosePropsScratch)
        {
            NetworkedProp? node = NodeFor(p.Id);
            if (node == null)
                continue;

            if (node.Body.GlobalPosition.Y < Carryable.KillPlaneY)
            {
                // REACH-1 (§5b layer 2, the kill-plane path): recovered to the LAST GOOD
                // transform, not to HomeTransform. For a prop that never rested anywhere legal
                // the two are the same value (NetworkedProp.Init seeds last-good from the spawn
                // pose), which is why the existing throw/OOB suites are unmoved; for a prop the
                // hider carried across the room and a seeker then knocked into the void, the
                // difference is whether the game hands the object back where it was hidden or
                // back on its starting shelf.
                RestAudit.Result oob = RestAudit.AuditKillPlane(node, node.Body.GlobalTransform,
                    PlaceOverlapToleranceM);
                NoteAudit(p.Id, oob);
                Transform3D home = oob.To;
                _registry.SetResting(p.Id, home);
                node.SettleToRest(home);
                // PropRelease.None (SFX-2's table): the kill-plane recovery is a world repair,
                // not a release verb. Nobody dropped, placed or threw this prop — it fell out of
                // the world and the server put it back — so the wire carries no event and the
                // material stays silent. The audit line is the record of the move.
                Rpc(MethodName.ApplyPropState, p.Id, (int)PropMode.Resting, 0, home,
                    (int)PropRelease.None);
                _looseSettle.Remove(p.Id);
                _lastStreamed.Remove(p.Id);
                RestLatched?.Invoke(p.Id, oob);
                continue;
            }

            // PHYS-1 (P2): THE BACKSTOP, before the transform is sampled so what is streamed is
            // what the prop is actually doing. The real bound is at the impulse
            // (PropPhysics.WakeSpeed clamps every wake to the bar); this catches the energy a
            // SOLVER can invent out of a deep overlap or a wedge, which is the event a player
            // reads as "it freaked out". The counter is the evidence that it is ~0 in play.
            if (node.ServerClampMotion())
            {
                PropClampCount++;
                GD.Print($"[phys] clamp prop={p.Id} to {node.SpeedMps:F2} m/s "
                    + $"(cap {node.SpeedCapMps:F2}) total={PropClampCount}");
            }

            Transform3D t = node.Body.GlobalTransform;
            _registry.SetLooseTransform(p.Id, t);
            // 30 Hz + skip-unchanged: don't re-broadcast a transform that hasn't meaningfully
            // moved since the last sample (a near-settled prop barely drifts). The final pose
            // still arrives reliably via the Resting transition below.
            if (streamThisTick
                && (!_lastStreamed.TryGetValue(p.Id, out Transform3D last)
                    || t.Origin.DistanceSquaredTo(last.Origin) > StreamPosEpsilonSq
                    || !t.Basis.IsEqualApprox(last.Basis)))
            {
                Rpc(MethodName.StreamLoose, p.Id, t);
                _lastStreamed[p.Id] = t;
            }

            // PHYS-1 (P4): A CAN MUST NOT "SLEEP" MID-ROLL.
            //
            // The linear gate alone latches a prop that is still visibly turning: a can rolling
            // the last half-metre of its travel crosses 0.2 m/s well before it stops, and a
            // sphere or a cylinder settling into a shelf lip rocks in place at almost no linear
            // speed at all. Latching there freezes it kinematic mid-motion on every peer, which
            // is the opposite of "cans roll around" and reads as the physics giving up.
            //
            // SettleSpinSq is the same question asked of the other degree of freedom, and the
            // number is derived from the linear one through the smallest prop's radius rather
            // than picked: a 35 mm can rolling at the linear settle speed turns at 0.2/0.035 =
            // 5.7 rad/s, so anything at or above that is still a roll.
            if (node.Body.LinearVelocity.LengthSquared() < SettleSpeedSq
                && node.Body.AngularVelocity.LengthSquared() < SettleSpinSq)
            {
                int c = _looseSettle.GetValueOrDefault(p.Id) + 1;
                _looseSettle[p.Id] = c;
                if (c >= SettleTicks)
                {
                    // REACH-1 (§5b layer 2): the rest transform is AUDITED, not merely recorded.
                    // CARRY-1 landed the recording half and left the correction to this lane on
                    // purpose — a prop teleporting with no audit behind it is indistinguishable
                    // from a desync, so the two halves had to ship with the log line that
                    // explains the move. The whole latch body lives in ServerAuditRest so the
                    // self-test and the Confirm-time precondition run the IDENTICAL code rather
                    // than a second copy of it.
                    ServerAuditRest(p.Id);
                }
            }
            else
            {
                _looseSettle[p.Id] = 0;
            }
        }
    }

    /// <summary>Server: registers a prop (assigning its stable id) and spawns it through the
    /// spawner, which replicates it to every connected peer and to any that join later. This is
    /// the single netcode funnel for objects, so the server-authority guard lives here (not just
    /// in callers like <see cref="SpawnInitialProps"/>): a client-side call would otherwise
    /// increment this peer's local-only <c>_nextId</c> and spawn a phantom node that desyncs
    /// <see cref="NodeFor"/> id-resolution on that client. Returns null off-server.</summary>
    public NetworkedProp? ServerSpawn(PropKind kind, Transform3D at)
    {
        if (!_isServer)
            return null;
        int id = _registry.Register(kind, at);
        var data = new Godot.Collections.Array { id, (int)kind, at };
        return (NetworkedProp)_spawner.Spawn(data);
    }

    /// <summary>The prop node for an id, or null. Checks adopted (authored) props first, then
    /// falls back to the runtime-spawned root by node name (the id) — same contract on every
    /// peer either way, so every grab/drop/throw RPC, the held-dump, and the loose stream all
    /// work unchanged regardless of which path a prop came from.</summary>
    public NetworkedProp? NodeFor(int id) =>
        _adopted.TryGetValue(id, out NetworkedProp? adopted)
            ? adopted
            : _propsRoot.GetNodeOrNull<NetworkedProp>(id.ToString());

    /// <summary>Test-only (SandboxSelfTest): force a spawned prop into PropMode.Loose at
    /// <paramref name="at"/> without a player grab/drop round-trip, so a headless test can set up
    /// a "loose prop in the field" precondition without a live grab/drop round-trip. Runs the
    /// registry through the same Held -> Loose path a real drop takes. Server-only; no live peer
    /// required.</summary>
    internal void TestForceLoose(int propId, Transform3D at)
    {
        if (!_isServer)
            return;
        _registry.SetHolder(propId, 1);
        _registry.Release(propId, at);
    }

    /// <summary>The prop in this peer's HAND, or null. Every caller (encumbrance prediction, the
    /// carry-visual pose, HandleCarryIntent's drop/throw branch, BotHarness) means "the thing
    /// they are actually using", and that is exactly one prop.
    ///
    /// Reads the replicated held-by-peer view rather than a private cache, so it is correct on
    /// every peer (a teammate's hand included).</summary>
    public NetworkedProp? FindHeldBy(int peerId) =>
        _heldByPeer.TryGetValue(peerId, out int propId) ? NodeFor(propId) : null;

    /// <summary>Total mass this peer is carrying, for encumbrance. Server and owner-prediction
    /// both call this so they compute the same number from the same replicated state
    /// (RISK-AUDIT-2026-07-12.md 4.1: never trust a client-reported speed factor).</summary>
    public float CarriedMassKgFor(int peerId)
    {
        NetworkedProp? node = FindHeldBy(peerId);
        return node != null && GodotObject.IsInstanceValid(node.Body) ? node.Body.MassKg : 0f;
    }

    /// <summary>Every known prop on this peer — runtime-spawned (under the PropSpawner) plus
    /// adopted authored ones — for instrumentation (see BotHarness). Not used by any gameplay
    /// path; those all go through <see cref="NodeFor"/>/<see cref="FindHeldBy"/> above.</summary>
    public System.Collections.Generic.IEnumerable<NetworkedProp> AllProps()
    {
        foreach (Node child in _propsRoot.GetChildren())
            if (child is NetworkedProp p)
                yield return p;
        foreach (NetworkedProp p in _adopted.Values)
            yield return p;
    }

    /// <summary>Count of runtime-spawned props only (children of the PropSpawner's root) —
    /// excludes adopted authored props, which never live here (see AdoptAuthoredProps). Reconnect
    /// instrumentation (BotHarness): the teardown Task A1 added frees exactly this container's
    /// children, so this is what should read back to the pre-drop count after a resume.</summary>
    public int RuntimePropCount => _propsRoot.GetChildCount();

    /// <summary>Client-only: clears every peer's held-by-peer view (see <see cref="FindHeldBy"/>).
    /// Called by Gameplay's reconnect teardown right before it frees the stale prop/avatar nodes
    /// these entries refer to, so nothing here outlives the nodes it names.
    /// Harmless no-op on the server (the authoritative registry lives in <c>_registry</c> and the
    /// server's own view is rebuilt from it); the resumed session's dump
    /// (<see cref="SendDumpTo"/>) rebuilds this dictionary fresh through the normal
    /// <see cref="ApplyPropState"/> funnel, same as a late joiner. Held-prop RESTORATION across the
    /// gap (P2) is deliberately not this method's job — see Task A2.
    ///
    /// <b>Clears ALL peers' entries, not just the local one.</b> A stale entry surviving a resume
    /// would name a prop on an avatar node that no longer exists.</summary>
    public void ClientResetHeldState() => _heldByPeer.Clear();

    // --- The playthrough boundary (CORE-PROG-A2, core-spine spec §5.2 "props" row) ------------

    public string SliceId => "props";

    /// <summary>Reserved across-nights write path (spec §5.5) — no-op today; a dropped prop
    /// staying where it fell across nights IS the map remembering (canon fact 8), and within
    /// one server process that persistence is free.</summary>
    public void CaptureNightSnapshot(int round) { }

    /// <summary>The initial-dump restore. Server-only: every client's view converges through the
    /// same funnels every live change uses (prop-state broadcasts, spawner despawns/spawns) — a
    /// client-local clear here would race those in-flight messages across channels, so clients
    /// deliberately do nothing.
    ///
    /// Idempotent (spec §5.3): a second run over an already-restored world despawns the fresh
    /// dump and seeds an identical one — state-identical, though the ids advance.
    /// <c>_nextId</c> is NEVER rewound (stated for acceptance criterion 3): PropRegistry ids
    /// stay monotonic across playthrough boundaries so a straggler packet about a pre-reset
    /// prop can never alias onto a new one (PropRegistry.Remove's rule), and
    /// PropRegistry.Register already skips occupied ids so the authored id range (1000+) is
    /// safe no matter how many boundaries a process lives through.</summary>
    public void ResetForNewPlaythrough()
    {
        if (!_isServer)
            return;

        // 1. Every hand empties (spec §5.2: a new playthrough starts empty-handed) and every
        //    runtime-spawned prop despawns (the spawner replicates each removal); adopted
        //    authored props go HOME to their adoption transform instead. A held prop gets a
        //    Resting broadcast BEFORE its despawn, because nothing about a spawner despawn tells
        //    a peer the prop has left a hand — ApplyPropState is the one funnel the held-by-peer
        //    view is written from, and it updates that view even for a node that has already
        //    gone.
        var runtimeIds = new System.Collections.Generic.List<int>();
        foreach (PropState p in _registry.All)
        {
            if (_adopted.ContainsKey(p.Id))
                continue;
            runtimeIds.Add(p.Id);
            if (p.Mode == PropMode.Held)
            {
                Transform3D at = NodeFor(p.Id)?.Body.GlobalTransform ?? p.Transform;
                Rpc(MethodName.ApplyPropState, p.Id, (int)PropMode.Resting, 0, at, (int)PropRelease.None);
            }
        }
        foreach (int id in runtimeIds)
        {
            _registry.Remove(id);
            _looseSettle.Remove(id);
            _lastStreamed.Remove(id);
            NodeFor(id)?.QueueFree();
        }
        foreach (System.Collections.Generic.KeyValuePair<int, Transform3D> kv in _adoptedInitial)
        {
            _registry.SetResting(kv.Key, kv.Value);
            // THE RESET EDGE IS None, NOT Dropped (SFX-2, and the packet says so in as many
            // words). Every prop in the world going back to its authored transform between rounds
            // is a world REARRANGEMENT; announcing it as forty simultaneous drops would be the
            // loudest lie this system could tell, in a game where the other player is navigating
            // by what they can hear through a wall.
            Rpc(MethodName.ApplyPropState, kv.Key, (int)PropMode.Resting, 0, kv.Value, (int)PropRelease.None);
        }

        // 2. The initial dump, again — exactly as session start seeded it. This is the line that
        //    makes a second run identical to the first.
        if (_world.Length > 0)
            SpawnInitialProps(_world);

        GD.Print($"[worldstate] props restored: {runtimeIds.Count} despawned, " +
                 $"{_registry.Count} in the fresh dump ({_adoptedInitial.Count} adopted rehomed)");
    }

    /// <summary>Called on EVERY peer, once, after the world node has been added to the tree (so
    /// its own _Ready has already bound each authored NetworkedProp's Body — see
    /// NetworkedProp._Ready). Walks the world's node tree for authored NetworkedProp instances
    /// (never spawns anything — no MultiplayerSpawner involvement, by design) and assigns each a
    /// stable id, deterministically, by sorting on its node path: identical on every peer since
    /// every peer instanced the exact same .tscn. On the server this ALSO seeds the authoritative
    /// PropRegistry at that same id + kind + current (authored) transform, so grab/drop/throw
    /// arbitration and the late-join dump work for authored props exactly like runtime-spawned
    /// ones. A no-op for worlds with no authored props ("open"/"propsync" today).
    ///
    /// <para><b>It prints what it did, and since SHELF-1 (2026-09-19) how long it took</b> —
    /// <c>[props] adopted N authored prop(s) in T ms (ids A..B)</c>, on every peer including
    /// dedicated servers and headless bots. Two reasons, neither of them logging for its own
    /// sake. The search room went from four authored props to a hundred and thirty, and "what
    /// does adoption cost" is a question with an answer nobody had measured; and the id RANGE on
    /// the line is what <c>tests/Run-AuthoredPropTest.ps1</c> reads to prove a server and a
    /// late-joining client agree about the block before either of them has moved anything. The
    /// walk is O(nodes) and the sort O(n log n), both once per world build, so the stopwatch is
    /// not itself a cost worth gating behind a flag.</para></summary>
    public void AdoptAuthoredProps(Node worldRoot)
    {
        long startedTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        var found = new System.Collections.Generic.List<NetworkedProp>();
        CollectNetworkedProps(worldRoot, found);
        found.Sort((a, b) => string.CompareOrdinal(a.GetPath().ToString(), b.GetPath().ToString()));
        for (int i = 0; i < found.Count; i++)
        {
            NetworkedProp prop = found[i];
            int id = AuthoredIdBase + i;
            PropKind kind = AuthoredKindOf(prop.Body);
            Transform3D at = prop.GlobalTransform;
            prop.InitAuthored(id, kind, at);
            prop.IsServer = _isServer;
            _adopted[id] = prop;
            // The authored placement, remembered (CORE-PROG-A2): adopted props cannot be
            // despawned by the boundary restore, so they return HOME instead — this transform
            // is where "the initial dump" puts them.
            _adoptedInitial[id] = at;
            if (_isServer)
            {
                bool registered = _registry.RegisterAt(id, kind, at);
                System.Diagnostics.Debug.Assert(registered,
                    $"authored prop id {id} already occupied — id-space collision (see RISK-AUDIT-2026-07-12.md 5.1d)");
            }
            // ONE LINE PER ADOPTED PROP, saying which id landed on which node (BTN-1,
            // 2026-09-19). These ids are a function of EVERY authored prop in the whole world:
            // the sort above is over node paths, so adding props to one room renumbers every
            // room whose path sorts after it. That has already bitten once — BTN-1's three rack
            // objects in HoldingRoom.tscn moved SearchRoom's four crates from 1000..1003 to
            // 1003..1006 and tests/Run-PlaceTest.ps1 named them by literal — and SHELF-1's
            // hundred aisle props will do it again. A suite that reads these lines cannot be
            // broken by a level growing; one that types the numbers can.
            GD.Print($"[props] authored prop {id} <- {prop.GetPath()} ({kind})");
        }

        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - startedTicks)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        GD.Print($"{AdoptLogPrefix} adopted {found.Count} authored prop(s) in {ms:0.00} ms"
                 + (found.Count > 0
                     ? $" (ids {AuthoredIdBase}..{AuthoredIdBase + found.Count - 1})"
                     : " (no ids)"));
    }

    /// <summary>The prefix on the adoption line, so a suite greps for this rather than for a
    /// sentence somebody may reword.</summary>
    public const string AdoptLogPrefix = "[props]";

    /// <summary>An authored prop's shape, read from its physical collider rather than
    /// <see cref="Carryable.Kind"/> — confirmed by direct instrumentation that Godot never applies
    /// a nested PackedScene instance's own exported script properties on this project's Godot/Mono
    /// build (Crate.tscn/Sphere.tscn's authored <c>kind</c>/<c>tint</c> both silently read back as
    /// the C# field's default, `Shape.Crate`, for EVERY authored prop, sphere or crate alike;
    /// native engine properties like <c>mass</c>/<c>transform</c> on the same instanced nodes are
    /// unaffected). <see cref="CollisionShape3D.Shape"/> is itself a native property, so it
    /// reliably survives instancing and needs no scene-authoring workaround.</summary>
    /// <remarks>SFX-1 (2026-09-19) moved the rule itself into
    /// <see cref="Carryable.ShapeFromCollider"/> and extended it to the three product shapes,
    /// so the sound layer and the authority layer read one table instead of two. This method
    /// keeps its name, its doc above and its job — mapping the body's shape onto the wire enum —
    /// and is now one cast, because <c>Carryable.Shape</c> mirrors <see cref="PropKind"/> 1:1 by
    /// construction (the mirror is stated on both enums).</remarks>
    private static PropKind AuthoredKindOf(Carryable body) =>
        (PropKind)(int)Carryable.ShapeFromCollider(
            body.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape);

    private static void CollectNetworkedProps(Node n, System.Collections.Generic.List<NetworkedProp> found)
    {
        foreach (Node child in n.GetChildren())
        {
            if (child is NetworkedProp np)
                found.Add(np);
            CollectNetworkedProps(child, found);
        }
    }

    // --- Client entry points: request the server, never mutate locally -----------------

    /// <summary>Networked client: ask the server to grab a prop. No local effect until the
    /// server confirms with <see cref="ApplyPropState"/> — no grab prediction.</summary>
    public void ClientRequestGrab(int propId) => RpcId(1, MethodName.RequestGrab, propId);

    /// <summary>Networked client: ask the server to drop whatever this peer holds.</summary>
    public void ClientRequestDrop()
    {
        // Telemetry (inert unless this is a real client session): drop and throw both collapse to a
        // Held->Loose transition at ApplyPropState and are indistinguishable there, so they're
        // counted here at the owner-only client entry point (called from the local PredictedOwner
        // avatar's HandleCarryIntent, always while holding) — local player's own actions only.
        Telemetry.Telemetry.Instance?.NotePropDropped();
        RpcId(1, MethodName.RequestDrop);
    }

    /// <summary>Networked client: ask the server to throw whatever this peer holds. No local
    /// effect until the server confirms with <see cref="ApplyPropState"/> — throw is
    /// server-confirmed, not predicted, consistent with grab and drop.</summary>
    public void ClientRequestThrow()
    {
        Telemetry.Telemetry.Instance?.NotePropThrown();
        RpcId(1, MethodName.RequestThrow);
    }

    /// <summary>
    /// Networked client: ask the server to SET DOWN the prop this peer is holding, at
    /// <paramref name="intended"/> — the transform the holder's own spring currently has it at,
    /// rotation and all.
    ///
    /// <para><b>The transform is the message.</b> Rotate-held is deliberately a local, unreplicated
    /// thing (CARRY-1 packet item 2: no new <c>MoveIntent</c> field), so the orientation a player
    /// spent a few seconds lining up exists only on their own machine until this call carries it.
    /// Drop and throw have no such payload and therefore lose the rotation, which is the honest
    /// distinction between "put this down like this" and "get rid of this".</para>
    ///
    /// <para>No local effect until the server confirms, exactly like grab/drop/throw — the client
    /// proposes a transform and the server is the one that decides whether a prop may be
    /// there.</para>
    /// </summary>
    public void ClientRequestPlace(int propId, Transform3D intended)
    {
        // Counted as a drop: at ApplyPropState a place and a drop are the same Held->Loose
        // transition, and the telemetry column has always meant "the player put something down".
        Telemetry.Telemetry.Instance?.NotePropDropped();
        RpcId(1, MethodName.RequestPlace, propId, intended);
    }

    // --- Server: reliable grab/drop arbitration -----------------------------------------

    /// <summary>Client -> server: request to grab a prop. Validates sender, prop existence,
    /// one-item-per-peer, and proximity (authoritative avatar position) before arbitrating
    /// through the registry's first-grab-wins rule.
    ///
    /// Every rejection path reports back (INTERACTION-BIBLE 2). These used to return
    /// silently, so pressing interact one step too far from a crate produced *nothing* — no
    /// sound, no flash, no reason — which reads as an unresponsive game rather than a
    /// refused action. "Silently ignored" is a defect class, not an implementation choice.
    ///
    /// The sender-validation returns above the reason paths stay silent on purpose: there is
    /// no trustworthy peer to answer, and replying to an unidentified sender is a
    /// reflection surface.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestGrab(int propId)
    {
        if (!_isServer)
            return;
        int peer = Multiplayer.GetRemoteSenderId();
        if (peer <= 0 || ControlDenied(peer))
            return;
        if (!_registry.TryGet(propId, out PropState s))
        {
            DenyGrab(peer, GrabDenial.Gone);
            return;
        }
        // Proximity: the grabber's avatar must be near the prop (authoritative positions).
        Node3D? avatar = AvatarResolver?.Invoke(peer);
        NetworkedProp? node = NodeFor(propId);
        if (avatar == null || node == null)
        {
            DenyGrab(peer, GrabDenial.Gone);
            return;
        }
        if (avatar.GlobalPosition.DistanceSquaredTo(node.WorldPosition) > GrabRangeSq)
        {
            DenyGrab(peer, GrabDenial.OutOfRange);
            return;
        }

        // Re-grabbing something you already hold is a refusal, answered rather than silently
        // returned: see GrabDenial.AlreadyHeld. Checked before HandsFull so the message names
        // the actual situation.
        if (_heldByPeer.TryGetValue(peer, out int heldId))
        {
            DenyGrab(peer, heldId == propId ? GrabDenial.AlreadyHeld : GrabDenial.HandsFull);
            return;
        }

        // SetHolder is the atomic first-grab-wins gate, and the server processes reliable RPCs
        // one at a time, so of two peers who pressed interact on the same crate in the same tick
        // exactly one gets true here.
        if (!_registry.SetHolder(propId, peer))
        {
            DenyGrab(peer, GrabDenial.Taken); // someone else won the race
            return;
        }
        // node.BODY's transform, not the node's (FEEL-1, 2026-09-20). A NetworkedProp NODE is
        // placed once at spawn and never moves again -- the physical Carryable child is what
        // travels, which is why PropManager reads WorldPosition => Body.GlobalPosition
        // everywhere else. This argument was DISCARDED by ApplyPropState's Held arm until FEEL-1
        // made it the pose every non-holder peer carries the prop at, so the staleness was
        // invisible and is now load-bearing. Measured: a witness watching a regrab saw the ball
        // teleport to its SPAWN corner on the grab and then trail its holder by five metres for
        // the rest of the run (Run-RegrabTest, "held ball 5.52m from holder").
        Rpc(MethodName.ApplyPropState, propId, (int)PropMode.Held, peer, node.Body.GlobalTransform, (int)PropRelease.None);
    }

    /// <summary>
    /// <b>The authority half of the interaction-gates cascade row</b> (STATE-CASCADE-TABLE rows 7,
    /// 9, 14, 22). A body that is knocked out, frozen or momentarily ragdolled cannot grab, drop
    /// or throw — enforced here, on the server, in the one place every prop verb has to pass
    /// through.
    ///
    /// <para><c>SandboxAvatar</c> gates the same rule client-side so the local player's keys feel
    /// dead the same instant their steering does. That half is feel; this half is the rule. Both
    /// read the identical replicated <c>MoveState</c> field, so they cannot disagree, and a
    /// doctored client that skips its own gate simply gets ignored.</para>
    ///
    /// <para>Reads the avatar rather than taking an injected predicate deliberately: the state is
    /// already replicated onto the very node <see cref="AvatarResolver"/> hands back, so an
    /// injection would be a second source for a fact that already has one.</para>
    /// </summary>
    private bool ControlDenied(int peer)
        => AvatarResolver?.Invoke(peer) is MpFoundation.Game.Sandbox.SandboxAvatar avatar
           && avatar.ControlDeniedNow;

    /// <summary>Server -> the one requester whose grab was refused. Reliable: a dropped
    /// rejection is a silent failure again, which is the thing being fixed.</summary>
    private void DenyGrab(int peer, GrabDenial reason)
    {
        // Server-side trace for every refusal. The player already gets a perceivable cue
        // (SandboxAvatar.OnGrabDenied's bump), but that cue deliberately does not say WHY, so
        // from the outside a refused grab and a broken grab look identical — with no way to tell
        // an out-of-range bot from a broken registry. One line per refusal, and refusals are
        // rare by construction (a player has to actually miss).
        ServerLog.Info("grab denied", $"peer={peer} reason={reason}");
        if (peer == Multiplayer.GetUniqueId())
            OnGrabDenied((int)reason); // host-as-player: no round trip to itself
        else
            RpcId(peer, MethodName.OnGrabDenied, (int)reason);
    }

    /// <summary>Server -> requester: the grab was refused, and why. Raises
    /// <see cref="GrabDenied"/> for whatever the local peer wants to do about it.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void OnGrabDenied(int reason)
    {
        LastDenial = (GrabDenial)reason;
        GrabDenied?.Invoke((GrabDenial)reason);
    }

    /// <summary>Client -> server: drop what is in the sender's hand. Releases into Loose with a
    /// gentle toss off the holder's facing (the same light-lob feel as the offline
    /// Carryable.OnDropped arc) rather than snapping straight to Resting, so a dropped prop
    /// tumbles and settles like the offline path and the server's loose loop (see
    /// _PhysicsProcess) latches it to Resting once it stops moving. No-op if the hand is
    /// empty.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestDrop()
    {
        if (!_isServer)
            return;
        int peer = Multiplayer.GetRemoteSenderId();
        if (peer <= 0 || ControlDenied(peer))
            return;
        ReleaseHeldInto(peer, DropForwardSpeed, DropUpSpeed, PropRelease.Dropped);
    }

    /// <summary>
    /// Client -> server: <b>the place verb</b>. Validates, in this order: that the sender holds
    /// this prop; that the sender's body is within the same authoritative reach a grab needs;
    /// that the intended transform is within <see cref="PlaceReachM"/> of the sender's hand; that
    /// placement integrity layer 1 passes at that transform (program doc §5b); and finally that
    /// any registered <see cref="IPlacementValidator"/> allows it.
    ///
    /// <para><b>Order is the message.</b> Each test refuses with a reason the player can act on,
    /// and the cheapest, most specific one runs first — being told "you are not holding that" when
    /// you are two rooms away is more useful than being told it does not fit. The physics query is
    /// last of the server's own tests because it is the only one that costs anything.</para>
    ///
    /// <para><b>A validator that MOVES the placement gets re-checked.</b> A snap pad that put a
    /// crate inside a wall would be the §5b defect arriving through the one door that skipped the
    /// check, so integrity runs again on a moved transform and a pad that snaps somewhere illegal
    /// is refused exactly like a player who aimed there.</para>
    ///
    /// <para>Sender validation returns silently for the same reason grab's does: there is no
    /// trustworthy peer to answer, and replying to an unidentified sender is a reflection
    /// surface.</para>
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestPlace(int propId, Transform3D intended)
    {
        if (!_isServer)
            return;
        int peer = Multiplayer.GetRemoteSenderId();
        if (peer <= 0 || ControlDenied(peer))
            return;

        // A doctored client can put anything in a Transform3D. Everything below dereferences it,
        // and a NaN would poison the registry, the broadcast and every peer's body at once.
        if (!intended.Origin.IsFinite() || !intended.Basis.X.IsFinite()
            || !intended.Basis.Y.IsFinite() || !intended.Basis.Z.IsFinite())
        {
            DenyPlace(peer, PlaceDenial.TooFarToPlace);
            return;
        }

        if (!_heldByPeer.TryGetValue(peer, out int heldId) || heldId != propId)
        {
            DenyPlace(peer, PlaceDenial.NotHolding);
            return;
        }
        NetworkedProp? node = NodeFor(propId);
        Node3D? avatar = AvatarResolver?.Invoke(peer);
        if (node == null || avatar == null || !_registry.TryGet(propId, out PropState s)
            || s.Mode != PropMode.Held)
        {
            DenyPlace(peer, PlaceDenial.Gone);
            return;
        }
        if (avatar.GlobalPosition.DistanceSquaredTo(node.WorldPosition) > GrabRangeSq)
        {
            DenyPlace(peer, PlaceDenial.OutOfRange);
            return;
        }

        // THE EYE, NOT THE CHEST (FEEL-1, 2026-09-20), and the allowance is DERIVED from how far
        // the holder is allowed to hold the thing in the first place.
        //
        // The rule the verb describes has not changed -- "you may set it down where you are
        // holding it" -- but where that is has. Until FEEL-1 a held prop rode a chest-height
        // socket, so measuring 0.9 m from the carry anchor was measuring from the object. It now
        // rides the VIEW RAY at a distance the wheel sets, up to CarryHold.HoldMaxBaseM from the
        // eye, so the old anchor refuses perfectly honest placements: a player looking down to
        // set a can on the floor at their feet is ~1.1 m from their own carry anchor and was
        // denied TooFarToPlace. Measured against the eye, with the ceiling the hold itself
        // enforces, "as far as you can hold it" and "as far as you can place it" are the same
        // number by construction rather than by two constants agreeing.
        //
        // It is still a REACH and not telekinesis: the sender must already be within GrabRange of
        // the prop (checked above), the prop's own bulk is the only thing added to the ceiling
        // (the intended transform names the prop's ORIGIN, while the hold distance measures to
        // the GRABBED POINT, which can be a bounding radius away from it), and
        // GrabRangeTolerance is added for the identical reason it is added to the grab reach --
        // the client measured against its PREDICTED body and the server against the
        // authoritative one.
        Vector3 eye = avatar is SandboxAvatar sa
            ? sa.AimOriginGlobalPosition
            : avatar.GlobalPosition;
        float propRadius = node.Body.BoundingRadiusM;
        float placeReach = Sandbox.Feel.CarryHold.HoldMaxM(
            Sandbox.Feel.CarryHold.HoldMinM(
                avatar is SandboxAvatar prop ? prop.Proportions.CapsuleRadiusM : 0f, propRadius))
            + propRadius + GrabRangeTolerance;
        if (eye.DistanceSquaredTo(intended.Origin) > placeReach * placeReach)
        {
            DenyPlace(peer, PlaceDenial.TooFarToPlace);
            return;
        }

        Rid holderRid = avatar is CollisionObject3D co ? co.GetRid() : default;
        Transform3D at = intended;
        PlacementIntegrity.Verdict verdict = PlacementIntegrity.Check(
            node.Body, at, holderRid, PlaceOverlapToleranceM);
        if (!verdict.Allowed)
        {
            ServerLog.Info("place refused", $"peer={peer} prop={propId} {verdict}");
            DenyPlace(peer, DenialFor(verdict));
            return;
        }

        if (PlacementValidator is { } validator)
        {
            PlacementDecision decision = validator.Validate(node, at, peer);
            if (!decision.Allowed)
            {
                DenyPlace(peer, decision.Reason == PlaceDenial.None
                    ? PlaceDenial.NotAllowedHere
                    : decision.Reason);
                return;
            }
            if (!decision.Transform.Origin.IsEqualApprox(at.Origin)
                || !decision.Transform.Basis.IsEqualApprox(at.Basis))
            {
                at = decision.Transform;
                verdict = PlacementIntegrity.Check(node.Body, at, holderRid, PlaceOverlapToleranceM);
                if (!verdict.Allowed)
                {
                    ServerLog.Info("place refused (snapped)", $"peer={peer} prop={propId} {verdict}");
                    DenyPlace(peer, DenialFor(verdict));
                    return;
                }
            }
        }

        // Legal. Record it as this prop's last known-good pose BEFORE the release, so a settle
        // that immediately goes wrong (knocked by something already falling) still has somewhere
        // honest for REACH-1 to put it back to.
        node.NoteLastGood(at);
        // PLACED, and this is the funnel the packet names. Everything else about the release is
        // identical to a drop's; the byte is the only thing that tells the other player's client
        // to tick the can's rim down instead of whooshing it away.
        _registry.Release(propId, at, PropRelease.Placed);
        Rpc(MethodName.ApplyPropState, propId, (int)PropMode.Loose, 0, at, (int)PropRelease.Placed);
        node.PlaceLooseServer(at);
    }

    /// <summary>The player-facing ordinal for an integrity verdict. One mapping, in one place, so
    /// REACH-1's layer-2 audit reports the same reasons this verb does.</summary>
    private static PlaceDenial DenialFor(PlacementIntegrity.Verdict verdict) => verdict.Fault switch
    {
        PlacementIntegrity.PlacementFault.OutsideRoomBounds => PlaceDenial.OutsideRoom,
        PlacementIntegrity.PlacementFault.Overlapping => PlaceDenial.DoesNotFitThere,
        _ => PlaceDenial.Gone,
    };

    /// <summary>Server -> the one requester whose place was refused. Reliable, and logged, for the
    /// same reasons <see cref="DenyGrab"/> is both.</summary>
    private void DenyPlace(int peer, PlaceDenial reason)
    {
        ServerLog.Info("place denied", $"peer={peer} reason={reason}");
        if (peer == Multiplayer.GetUniqueId())
            OnPlaceDenied((int)reason); // host-as-player: no round trip to itself
        else
            RpcId(peer, MethodName.OnPlaceDenied, (int)reason);
    }

    /// <summary>Server -> requester: the place was refused, and why.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void OnPlaceDenied(int reason)
    {
        LastPlaceDenial = (PlaceDenial)reason;
        PlaceDenied?.Invoke((PlaceDenial)reason);
    }

    /// <summary>Client -> server: throw what is in the sender's hand, along that peer's
    /// authoritative facing. Same release-into-Loose path as drop, just a harder impulse.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestThrow()
    {
        if (!_isServer)
            return;
        int peer = Multiplayer.GetRemoteSenderId();
        if (peer <= 0 || ControlDenied(peer))
            return;
        float scale = ThrowScale;
        ReleaseHeldInto(peer, ThrowForwardSpeed * scale, ThrowUpSpeed * scale,
            PropRelease.Thrown, alongAim: true);
    }

    /// <summary>Headless-test staging knob (--carry-throw-scale on the SERVER, since the server —
    /// not the thrower — owns the impulse: RequestThrow is where a networked throw actually
    /// happens, and the client deliberately never predicts it). Default 1.0, so no real session
    /// changes; only a test fixture ever passes it.
    ///
    /// Exists because Run-RegrabTest has to catch the ball while it is still Loose and rolling. A
    /// full-strength throw gives the Ball ~7.5 m/s and it sheds speed so slowly that it outruns the
    /// chasing bot (~3.6 m/s) for ~8s and only gets caught as it decays — by which point it has
    /// crossed ~45m of a field whose ground slab is 64m wide. A measured run caught it at z=31.81:
    /// 19cm from the edge. Tip it over and PropManager's own kill-plane recovery below teleports it
    /// home to the opposite corner, stranding the bot ~44m away with no time left — "never
    /// completed held -> loose -> held-again". A coin flip decided by 19cm, which is why it only
    /// ever showed up under a loaded marathon. See issue #4.</summary>
    private static float ThrowScale =>
        NetworkManager.Instance?.Options is { } o ? (float)o.CarryThrowScale : 1f;

    /// <summary>Server: drop/throw arbitration. Releases whatever is in the peer's hand into
    /// Loose. No-op if there is nothing to release.</summary>
    private void ReleaseHeldInto(int peer, float forwardSpeed, float upSpeed,
        PropRelease release, bool alongAim = false)
    {
        if (!_heldByPeer.TryGetValue(peer, out int propId))
            return;
        ReleaseIntoLooseDirected(propId, peer, forwardSpeed, upSpeed, yawOffsetRad: 0f, release,
            alongAim);
    }

    /// <summary>Server: releases ONE prop into Loose at its current held transform, with an
    /// impulse along <paramref name="peer"/>'s authoritative facing, broadcasts the Held -> Loose
    /// transition (which is what empties the hand on every peer), then kicks off the server's own
    /// simulation of it.</summary>
    /// <summary>Releases one prop into Loose along the holder's facing, with the impulse rotated
    /// <paramref name="yawOffsetRad"/> off it. Factored out for <see cref="ScatterHeldBy"/>:
    /// releasing several props along the identical vector stacks them in one spot, and a scatter
    /// that leaves a neat pile is not a scatter.</summary>
    /// <param name="alongAim">
    /// <b>Throw only.</b> The impulse leaves along the replicated AIM ray
    /// (<c>MoveIntent.AimYaw</c>/<c>AimPitch</c>, rebuilt through
    /// <see cref="MpFoundation.Game.Aim.AimQuery.DirectionFromYawPitch"/>) instead of the body's
    /// facing, and keeps its pitch instead of being flattened.
    ///
    /// <para><b>Why the two verbs differ.</b> <c>MoveState.Yaw</c> tracks where the body is
    /// TRAVELLING, not where the player is looking — a distinction nobody could see in third
    /// person while walking forward, and one that becomes absurd the moment FP-1 lands: a player
    /// standing still, looking up at a shelf, would throw the crate at their own feet. A drop and
    /// a scatter keep the body facing on purpose, because both mean "this ends up around me", and
    /// aiming at the ceiling should not lob a discarded object over your shoulder.</para>
    ///
    /// <para><b>The mismatch this creates, stated rather than hidden</b> (CARRY-1 packet item 4):
    /// the holder's own screen shows the prop leave the hand carrying the SPRING's velocity —
    /// throw while sprinting and it departs faster, because that is what the feel system does with
    /// <c>Interactor.ThrowScale</c>. The server, which has no spring for a remote holder and must
    /// be able to compute the same answer for every peer, uses this ray and a FIXED speed. So the
    /// first ~100 ms of a throw is the holder's local flourish and everything after it is the
    /// server's arc, and the two differ by the holder's own motion at the moment of release. The
    /// alternative — replicating the spring's velocity — would make the length of a throw a number
    /// the client reports about itself, which is the one shape
    /// <c>RISK-AUDIT-2026-07-12.md 4.1</c> says never to trust.</para></param>
    /// <param name="release">Which verb this was, for the presentation layer on EVERY peer
    /// (SFX-2). The one place a drop, a throw and a scatter differ that a remote client can
    /// perceive — the impulse they differ by is the server's own arithmetic and never crosses
    /// the wire as anything but a transform stream.</param>
    private void ReleaseIntoLooseDirected(int propId, int peer, float forwardSpeed, float upSpeed,
        float yawOffsetRad, PropRelease release, bool alongAim = false)
    {
        NetworkedProp? node = NodeFor(propId);
        if (node == null || !_registry.TryGet(propId, out PropState p) || p.Mode != PropMode.Held)
            return;
        Node3D? avatar = AvatarResolver?.Invoke(peer);
        Vector3 fwd;
        if (alongAim && avatar is SandboxAvatar aimer)
        {
            fwd = MpFoundation.Game.Aim.AimQuery.DirectionFromYawPitch(aimer.AimYaw, aimer.AimPitch);
            if (fwd.LengthSquared() < 0.0001f || !fwd.IsFinite())
                fwd = Vector3.Forward;
        }
        else
        {
            fwd = avatar != null ? -avatar.GlobalBasis.Z : Vector3.Forward;
            fwd.Y = 0;
            fwd = fwd.LengthSquared() > 0.0001f ? fwd.Normalized() : Vector3.Forward;
        }
        if (yawOffsetRad != 0f)
            fwd = fwd.Rotated(Vector3.Up, yawOffsetRad);
        Vector3 impulse = fwd * forwardSpeed + Vector3.Up * upSpeed;
        Transform3D at = node.Body.GlobalTransform;
        _registry.Release(propId, at, release);
        ClearStuckClock(propId);
        // PHYS-1 (P2): A THROW IS THE ONE EXCEPTION TO THE BAR, and it is granted by the VERB
        // rather than inferred from the velocity. CARRY-1's throw leaves the hand at 7.5 m/s
        // forward and 3.2 m/s up, so a prop judged only by its speed would be clamped in the
        // first tick of every throw. The allowance lasts exactly as long as the launch energy
        // does (PropPhysics.ThrowEnergySpent) and covers only the prop that was thrown.
        if (release == PropRelease.Thrown)
            node.NoteThrownServer();
        Rpc(MethodName.ApplyPropState, propId, (int)PropMode.Loose, 0, at, (int)release);
        node.BeginLooseServer(impulse);
    }

    /// <summary>Server -> everyone (including itself, CallLocal): the authoritative outcome of
    /// a grab, drop, throw, or settle. Idempotent — re-applying the same state is a harmless
    /// no-op, so a duplicated reliable delivery can never corrupt a client's view.
    ///
    /// <b>The held-by-peer view is written here, first, and regardless of whether the node still
    /// exists.</b> A consumed or despawned prop's release arrives as a Resting transition that may
    /// land after the node has gone (spawner despawns and RPCs are not ordered relative to each
    /// other), and that transition must still empty the hand — otherwise <see cref="FindHeldBy"/>
    /// would keep answering with an id whose node has been freed.
    ///
    /// <para><b>No <c>TransferChannel</c>, so this rides Godot's default channel 0</b> — stated
    /// because a paragraph in <c>NetProfile.PropImpactChannel</c> used to claim it was on
    /// channel 4 (REVIEW-1 I4, 2026-09-20). Channel 0 also carries <see cref="RequestGrab"/>,
    /// <see cref="RequestDrop"/>, <see cref="RequestPlace"/>, <see cref="RequestThrow"/>, the two
    /// denial RPCs and the <c>MultiplayerSpawner</c>'s replication, and the reset edge puts one
    /// of these per adopted prop onto it in a single tick. Moving it is a WIRE CHANGE and wants a
    /// protocol note of its own, not a line slipped into a fix commit.</para></summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, CallLocal = true)]
    private void ApplyPropState(int propId, int mode, int holderPeerId, Transform3D transform,
        int release)
    {
        if ((PropMode)mode == PropMode.Held)
        {
            _heldByPeer[holderPeerId] = propId;
        }
        else
        {
            // Whoever had it no longer does. A value scan rather than a keyed lookup because a
            // release does not carry the previous holder; the table is at most a group's worth of
            // entries, so this is a handful of comparisons per transition.
            foreach (System.Collections.Generic.KeyValuePair<int, int> kv in _heldByPeer)
            {
                if (kv.Value != propId)
                    continue;
                _heldByPeer.Remove(kv.Key);
                break;
            }
        }

        NetworkedProp? node = NodeFor(propId);
        if (node == null)
            return;
        switch ((PropMode)mode)
        {
            case PropMode.Held:
                Node3D? holder = AvatarResolver?.Invoke(holderPeerId);
                if (holder == null)
                {
                    // The one transition this funnel can lose permanently instead of converging:
                    // nothing re-applies a Held bind if the holder avatar isn't in this peer's
                    // tree yet. Spawner-before-dump ordering makes that unreachable today —
                    // log loudly so a future ordering regression is a line in the log, not a
                    // silently unheld prop someone chases for a week.
                    GD.PushWarning($"[props] Held state for prop {propId} arrived before holder {holderPeerId}'s avatar; bind dropped");
                }
                if (holder != null)
                {
                    // The ONE peer whose local player is the holder carries the prop on the ray
                    // hold (NetworkedProp.BindToHolderRayHold explains why only the holder); every
                    // other peer holds it at the pose it had RELATIVE TO THE HOLDER at this
                    // instant. The host-as-player satisfies this too, which is deliberate: a
                    // host's hand is not a lesser hand.
                    //
                    // `transform` is the prop's pose at the grab and FEEL-1 is what made it load-
                    // bearing on this arm: the Held case used to ignore it entirely, so every
                    // non-holder peer had to invent a pose (the chest anchor) and disagreed with
                    // the holder about where the object was. Passing the argument that was
                    // already on the wire is the whole of that fix -- no new field, no bump.
                    node.BindToHolder(holder, transform,
                        springOnThisPeer: holderPeerId == Multiplayer.GetUniqueId());
                    // Telemetry (inert unless this is a real client session): count only THIS
                    // client's own confirmed grab, never a teammate's replicated one — this funnel
                    // runs on every peer via CallLocal, so gating on the local id is what keeps
                    // props_grabbed the local player's own actions (spec decision 3), not counted
                    // once per connected peer.
                    if (holderPeerId == Multiplayer.GetUniqueId())
                        Telemetry.Telemetry.Instance?.NotePropGrabbed();
                }
                break;
            case PropMode.Resting:
                node.Unbind(transform);
                break;
            case PropMode.Loose:
                // The reliable transition: begin following the stream. On the server this is
                // immediately superseded by BeginLooseServer unfreezing the body right after
                // this call returns.
                //
                // SFX-2: and it carries WHY. PropReleaseWire.Decode, not a raw cast — the int is
                // whatever a peer sent, and an ordinal this build has no member for must become
                // None (silence) rather than aliasing onto a verb that happens to share its bits.
                node.BeginLoose(transform, PropReleaseWire.Decode(release));
                break;
        }
    }

    // --- Loose-prop transform stream: server -> clients, unreliable, its own channel ----

    /// <summary>Server -> everyone: the current frame's transform for a Loose prop, broadcast
    /// every physics tick while the server simulates it (see the server loop in _PhysicsProcess).
    /// Unreliable and on its own channel — a dropped or reordered sample costs nothing since the
    /// next tick supersedes it, and it never queues behind movement or reliable RPCs. The server
    /// itself is the source of truth and never applies its own broadcast (CallLocal is off).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = NetCodec.PropChannel)]
    private void StreamLoose(int propId, Transform3D t) => NodeFor(propId)?.ApplyLooseStream(t);

    // --- Prop impacts: server -> everyone, unreliable, its own channel (SFX-2) -------------
    //
    // WHY THIS EXISTS AT ALL, in one line: in a two-player hide-and-seek game the seeker's only
    // remote sense of the hider is what they can hear through a wall, and until this packet a
    // can knocked off a shelf was audible to exactly one player. Godot reports a frozen
    // kinematic body no contact, and a Loose prop on a non-authority peer is exactly that, so
    // the other client could not derive the event however hard it looked. SFX-1 measured it,
    // wrote it up as the one thing it could not close, and printed it from its suite on every
    // run. The server is the only peer that sees every contact, so the server says what
    // happened and everybody - the host included - plays it from there.

    /// <summary><b>Server: this prop just took a contact worth hearing.</b> Queued, not sent:
    /// the whole tick's worth is ranked together in <see cref="FlushImpacts"/>, because "keep
    /// the loudest four" is not a decision any one contact can take on its own.
    ///
    /// <para>The intensity is quantised to its wire byte HERE rather than at send time, so the
    /// limiter ranks exactly the values the peers will hear - a float ordering that disagreed
    /// with the byte ordering would drop the wrong can by a rounding step.</para></summary>
    public void ServerNoteImpact(int propId, float intensity, Vector3 position)
    {
        if (!_isServer)
            return;
        _pendingImpacts.Add(new ImpactBudget.PendingImpact(
            propId, ImpactBudget.IntensityToByte(intensity), position));
    }

    /// <summary>Server: rank the tick's contacts, announce the loudest
    /// <see cref="ImpactBudget.MaxImpactsPerTick"/>, drop the rest, and say so in the log when
    /// it drops any. The log line is the measurement the packet asks for and it is per-tick
    /// rather than a total, because "how bad does one collapse get" is the question and a
    /// session total cannot answer it.</summary>
    private void FlushImpacts()
    {
        if (_pendingImpacts.Count == 0)
            return;
        int offered = _pendingImpacts.Count;
        if (offered > _impactsPeakOffered)
        {
            _impactsPeakOffered = offered;
            if (ActorFx.LogSfx)
            {
                GD.Print($"[sfx] impact-peak offered={offered} budget={ImpactBudget.MaxImpactsPerTick} "
                    + $"t={Time.GetTicksMsec()}");
            }
        }
        int kept = ImpactBudget.KeepLoudest(_pendingImpacts, ImpactBudget.MaxImpactsPerTick);
        _impactsOffered += offered;
        _impactsSent += kept;
        for (int i = 0; i < kept; i++)
        {
            ImpactBudget.PendingImpact e = _pendingImpacts[i];
            // SFX-2 PLANT ANCHOR - tests/Run-MaterialSfxTest.ps1 -PlantNoImpactEvent replaces
            // exactly this statement to prove the wire assertions can fail. Keep it one
            // statement on one line.
            Rpc(MethodName.PropImpact, e.PropId, (int)e.Intensity, e.Position);
        }
        _pendingImpacts.Clear();
        if (kept < offered && ActorFx.LogSfx)
        {
            GD.Print($"[sfx] impact-limit offered={offered} sent={kept} dropped={offered - kept} "
                + $"cumOffered={_impactsOffered} cumSent={_impactsSent} t={Time.GetTicksMsec()}");
        }
    }

    /// <summary>Server -> everyone (including itself, CallLocal): <b>a prop was hit this hard,
    /// here.</b> Unreliable, on <see cref="NetProfile.PropImpactChannel"/> - see that constant
    /// for why it is neither reliable nor on the prop channel.
    ///
    /// <para><b>CallLocal, so the host plays from the announcement like any other client.</b>
    /// That is the whole "one path, one sound" rule: <c>Carryable.OnBodyEntered</c> no longer
    /// plays anything for a networked prop on any peer, so the host cannot hear a hit twice and
    /// the two players hear the same thing. The cost is that a host's own impact is heard on the
    /// following physics tick (~16 ms) rather than inside the contact handler, which is under
    /// the 0.4 s per-body cooldown by a factor of twenty-five and inaudible as timing.</para>
    ///
    /// <para><b>The position is a fallback, not the anchor.</b> A peer that has this prop plays
    /// at ITS OWN copy: the loose stream has already lerped that body to within a few
    /// centimetres, and anchoring on the local node also anchors any particles on it. The sent
    /// position is what remains when the node is missing - which in practice means the prop is
    /// gone on this peer, and a peer with no node has no <c>PresentationProfile</c> either, so
    /// there is no material voice to play and this early-outs. Stated rather than hidden: the
    /// "else play at the sent position" arm the packet describes cannot resolve a sound, and
    /// inventing a generic one would be a can that sounds like a crate.</para></summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = NetProfile.PropImpactChannel, CallLocal = true)]
    private void PropImpact(int propId, int intensityByte, Vector3 position)
    {
        NetworkedProp? node = NodeFor(propId);
        if (node == null || !GodotObject.IsInstanceValid(node.Body))
        {
            if (ActorFx.LogSfx)
                GD.Print($"[sfx] impact-orphan prop={propId} at ({position.X:F2},{position.Y:F2},{position.Z:F2})");
            return;
        }
        node.PlayWireImpact(
            ImpactBudget.ByteToIntensity((byte)Mathf.Clamp(intensityByte, 0, 255)));
    }

    // --- Server: peer lifecycle -----------------------------------------------------------

    /// <summary>Server: called (by Gameplay) when a peer disconnects, BEFORE its avatar node is
    /// freed, so the drop position derives from its still-valid last authoritative transform.
    /// Every prop that peer held latches to Resting and every peer converges on the release.</summary>
    public void OnPeerLeft(int peerId)
    {
        if (!_isServer)
            return;
        ReleaseHeldBy(peerId);
        // ReleaseHeldBy's Resting broadcasts have already emptied this peer's hand on every peer
        // (the funnel drops the entry); this is belt and braces so the dictionary cannot grow
        // without bound across a long session of joins and leaves.
        _heldByPeer.Remove(peerId);
    }

    /// <summary>Server: releases whatever peerId currently holds back to Resting — at the peer's
    /// avatar transform if it can still be resolved, or the prop's own spawn transform otherwise.
    /// Factored out of <see cref="OnPeerLeft"/> (disconnect) as its own reusable release funnel.
    /// Bible §6.2: an item released this way drops in place or returns to a known spawn — never
    /// anywhere else.</summary>
    public void ReleaseHeldBy(int peerId)
    {
        if (!_isServer)
            return;
        Node3D? avatar = AvatarResolver?.Invoke(peerId);
        foreach (int id in HeldPropIdsFor(peerId))
        {
            // If the avatar can't resolve (double-disconnect race, failed spawn), fall back to
            // the prop's own spawn transform rather than DropTransformFor's Transform3D.Identity
            // (the world origin).
            Transform3D at = avatar != null
                ? DropTransformFor(avatar)
                : (NodeFor(id)?.HomeTransform ?? Transform3D.Identity);
            _registry.SetResting(id, at);
            Rpc(MethodName.ApplyPropState, id, (int)PropMode.Resting, 0, at, (int)PropRelease.None);
        }
    }

    /// <summary>
    /// Server: <b>everything <paramref name="peerId"/> is carrying scatters</b> — the carried-item
    /// cascade row (STATE-CASCADE-TABLE row 8, BEHAVIOR-BIBLE §10.2's fourth missing API) for the
    /// failure states (beta plan §10).
    ///
    /// <para><b>Loose, not Resting, and that difference is the gameplay.</b> Its sibling
    /// <see cref="ReleaseHeldBy"/> sets props Resting where the body stands, which is right for a
    /// disconnect — an absent player's stuff should be tidy and findable. Going down is the
    /// opposite: the plan says the items <i>scatter</i>, and somebody has to come and get them
    /// while whatever put you down is still out there. Each prop leaves along its own bearing,
    /// fanned by the golden angle off the holder's facing, so more than one lands as a spread
    /// rather than a stack.</para>
    ///
    /// <para>Everything lands re-pickup-able by construction: Loose is the same mode a thrown
    /// prop is in, it uses the same server-simulated settle-and-latch loop, and it honours the
    /// same out-of-bounds recovery. This adds no new prop lifecycle — it reuses the one that has
    /// been carrying thrown items all along.</para>
    /// </summary>
    public void ScatterHeldBy(int peerId)
    {
        if (!_isServer)
            return;
        int index = 0;
        foreach (int id in HeldPropIdsFor(peerId))
        {
            // 2.39996 rad — the golden angle. Deterministic (no RNG on the server's authoritative
            // path, so every peer's replay of this is identical) and non-repeating, so two props
            // never leave along the same bearing however many a future carry rule allows.
            // Dropped, not Thrown: nobody threw these. Something knocked the holder down (or
            // burst a door open) and everything in their hands left involuntarily, which is the
            // packet's "the burst's forced drop".
            ReleaseIntoLooseDirected(id, peerId, ScatterForwardSpeed, ScatterUpSpeed,
                yawOffsetRad: index * 2.39996f, release: PropRelease.Dropped);
            index++;
        }
    }

    /// <summary>
    /// Server: <b>the burst door's shove</b> (DOOR-1, program §5). Every Loose or Resting prop
    /// whose body is within <paramref name="radiusM"/> of <paramref name="originGlobal"/> takes
    /// an OUTWARD impulse whose magnitude falls off linearly to zero at the radius
    /// (<see cref="MpFoundation.Game.Round.StartleTimeline.ImpulseNsAt"/> is the arithmetic, and
    /// it is unit-tested there rather than here). Returns how many props were moved, for the
    /// server log and for the smoke.
    ///
    /// <para><b>Newton-seconds, converted through each prop's own mass.</b> The caller's number
    /// is an IMPULSE, so a heavy prop is shoved less than a light one by construction — which is
    /// the difference between a physical shove and a "set everything to 6 m/s" that makes a
    /// crate and a marble leave together.</para>
    ///
    /// <para><b>A Resting prop is WOKEN into Loose, not nudged in place.</b> Resting is a latched
    /// static fact with no stream behind it (<see cref="PropMode.Resting"/>): pushing such a body
    /// on the server would move the server's copy and nobody else's, and the prop would snap back
    /// the moment anything re-read the registry. So this takes the ordinary release path — the
    /// reliable Loose transition every peer already applies, then the server's own settle loop —
    /// which is what makes the tumble something the stream carries rather than something only the
    /// host can see. An already-Loose prop is streaming, so its impulse is simply added to what it
    /// is already doing.</para>
    ///
    /// <para><b>A HELD prop is not touched here.</b> The flinch is a separate rule with a separate
    /// knob (<c>StartleTuning.ForceDropOnBurst</c>) and goes through
    /// <see cref="ScatterHeldBy"/>; shoving a prop out of a hand as a side effect of proximity
    /// would take the object off a hider who was standing two metres from the door and leave the
    /// one standing four metres away holding theirs, which is a rule nobody could read off the
    /// screen.</para>
    ///
    /// <para><b>Outward is horizontal plus a fixed lift.</b> Purely horizontal reads as a prop
    /// sliding; purely radial from a doorway at chest height drives a floor crate INTO the floor.
    /// The lift is a fraction of the impulse rather than a second knob — see
    /// <see cref="BurstLiftFraction"/>.</para>
    /// </summary>
    public int ServerBurstImpulse(Vector3 originGlobal, float radiusM, float impulseNs)
    {
        if (!_isServer || !(radiusM > 0f) || !(impulseNs > 0f))
            return 0;

        var tuning = new MpFoundation.Game.Round.StartleTuning
        {
            BurstRadiusM = radiusM,
            BurstImpulseNs = impulseNs,
        };

        // Snapshot: waking a Resting prop mutates the registry, and _registry.All is the live
        // collection (the same reason _loosePropsScratch exists in _PhysicsProcess). A LOCAL
        // list rather than that scratch, deliberately — the scratch is owned by the 60 Hz loop
        // and this runs from a level node's own frame, so sharing it would mean one burst could
        // land inside the loop's iteration of it. Once per round is not a place to save an
        // allocation.
        var candidates = new System.Collections.Generic.List<PropState>();
        foreach (PropState p in _registry.All)
        {
            if (p.Mode is PropMode.Loose or PropMode.Resting)
                candidates.Add(p);
        }

        int moved = 0;
        foreach (PropState p in candidates)
        {
            NetworkedProp? node = NodeFor(p.Id);
            if (node == null)
                continue;
            Vector3 at = node.Body.GlobalPosition;
            float distance = at.DistanceTo(originGlobal);
            float ns = MpFoundation.Game.Round.StartleTimeline.ImpulseNsAt(distance, tuning);
            if (ns <= 0f)
                continue;

            Vector3 outward = at - originGlobal;
            outward.Y = 0f;
            // A prop sitting exactly under the doorway has no outward direction to take; push it
            // into the room rather than picking a bearing at random, because "into the room" is
            // the one direction the door itself defines and every peer replays the same number.
            outward = outward.LengthSquared() > 0.0001f
                ? outward.Normalized()
                : Vector3.Forward;
            Vector3 impulse = outward + Vector3.Up * BurstLiftFraction;
            float mass = node.Body.Mass > 0.001f ? node.Body.Mass : 1f;
            Vector3 deltaV = impulse * (ns / mass);

            if (p.Mode == PropMode.Resting)
            {
                // _registry.WAKE, not Release. Release is guarded to Held -> Loose and returns
                // false here, which is exactly the defect Run-BurstDoorTest caught on its first
                // run: the store stayed Resting, the per-tick loop above streams only Loose
                // props, and the shove moved the SERVER's rigid body while every client's copy
                // stood still. See PropRegistry.Wake's own doc comment.
                Transform3D wokeAt = node.Body.GlobalTransform;
                _registry.Wake(p.Id, wokeAt);
                // PropRelease.None (SFX-2). The burst SHOVES a resting prop; it does not release
                // one, and DOOR-1's bang already owns that instant. Announcing a shelf's worth of
                // woken props as Dropped would be forty release announcements landing on the tick
                // the round hangs on — the same lie SFX-2 refuses for the reset edge. What these
                // props are heard doing is LANDING, which PropImpact carries on its own channel.
                Rpc(MethodName.ApplyPropState, p.Id, (int)PropMode.Loose, 0, wokeAt,
                    (int)PropRelease.None);
                node.BeginLooseServer(deltaV);
            }
            else
            {
                node.Body.LinearVelocity += deltaV;
            }
            moved++;
        }

        if (moved > 0)
            ServerLog.Info("burst", $"shoved {moved} prop(s) within {radiusM:F1} m of "
                                    + $"{originGlobal} at {impulseNs:F1} Ns");
        return moved;
    }

    /// <summary>How much UP goes into the burst's outward direction, as a fraction of the
    /// horizontal component. 0.35 is a shove that lifts the near edge of a crate rather than one
    /// that launches it: enough for a stacked tower to come apart, little enough that nothing
    /// clears the 3.5 m ceiling. Not a knob, because it is the SHAPE of the push rather than its
    /// strength, and <c>StartleTuning.BurstImpulseNs</c> is the dial for the strength.</summary>
    private const float BurstLiftFraction = 0.35f;

    /// <summary>Horizontal impulse on a scattered prop. Gentler than a throw
    /// (<see cref="DropForwardSpeed"/> is the comparison) — items should end up around the body,
    /// within sight of whoever comes back for them, not flung across the map.</summary>
    private const float ScatterForwardSpeed = 1.8f;

    /// <summary>Vertical impulse on a scattered prop — enough of a tumble to read as "dropped
    /// everything" rather than "set down".</summary>
    private const float ScatterUpSpeed = 2.4f;

    /// <summary>Server: called (by Gameplay) once a newly-joined peer's avatar exists, so the
    /// late joiner's client-side dictionary of prop nodes catches every current prop state —
    /// held props attach to their holders, resting props place at their latched transform.</summary>
    public void SendDumpTo(int peerId)
    {
        if (!_isServer)
            return;
        foreach (PropState p in _registry.All)
        {
            // PropRelease.None, UNCONDITIONALLY, and not p.Release (SFX-2). A dump describes the
            // world a joiner is arriving into; the throw that started a roll happened before this
            // peer existed, and replaying it now would be a sound with no cause — a can clanking
            // in an empty aisle the moment you connect. The registry keeps the verb because the
            // state is the truth about how the prop came to be loose; the WIRE here carries an
            // event, and there is no event.
            RpcId(peerId, MethodName.ApplyPropState, p.Id, (int)p.Mode, p.HolderPeerId, p.Transform,
                (int)PropRelease.None);
        }
    }

    /// <summary>Server: ids of every prop peerId currently holds (single-slot in practice, so
    /// at most one, but returns every match rather than assuming that invariant here), snapshotted
    /// (safe to mutate the registry while iterating). Two callers: <see cref="ReleaseHeldBy"/>
    /// (the existing disconnect-release path, unchanged) and, as of Task A2 (P2), Gameplay's
    /// disconnect handler — which MUST call this BEFORE <see cref="OnPeerLeft"/>, since a release
    /// clears the held state this queries (see ReconnectRegistry.Capture's doc comment on the
    /// ordering fix). Off-server returns empty, never null.</summary>
    public int[] HeldPropIdsFor(int peerId)
    {
        if (!_isServer)
            return System.Array.Empty<int>();
        var ids = new System.Collections.Generic.List<int>();
        foreach (PropState p in _registry.All)
            if (p.Mode == PropMode.Held && p.HolderPeerId == peerId)
                ids.Add(p.Id);
        return ids.ToArray();
    }

    /// <summary>Server: re-grants a prop peerId held before a disconnect, on a successful
    /// reconnect resume (P2 — see ReconnectRegistry's captured HeldPropIds, consumed by
    /// Gameplay.OnPeerConnected). Release-then-restore by design, not a new held state (Task
    /// A2's binding plan self-review note): the prop already dropped to Resting at disconnect
    /// time (OnPeerLeft/ReleaseHeldBy, unchanged, still runs unconditionally) — this simply
    /// re-grants it through the exact same <see cref="PropRegistry.SetHolder"/> +
    /// <see cref="ApplyPropState"/>(Held) path a live grab (<see cref="RequestGrab"/>) uses, once
    /// the resumed peer's avatar exists.
    ///
    /// First-grab-wins fairness, no steal-back: a no-op if the prop is currently Held by ANYONE
    /// (including, harmlessly, a re-entrant call for the same resumed peer) — whoever grabbed it
    /// during the gap keeps it. Also a no-op if the prop no longer exists (consumed/freed during
    /// the gap), or if the resumed peer's hand is already full — a resume is a restoration, never
    /// a pickup, and a restoration that displaced something would be a silent theft. Every
    /// outcome routes through a transition that already exists; no "held by a ghost" state is
    /// introduced.</summary>
    public void TryRestoreHeldProp(int propId, int peerId)
    {
        if (!_isServer)
            return;
        // The disconnect path always leaves the prop Resting, so Resting is the precise
        // restore precondition: Held means someone re-grabbed it during the gap (no
        // steal-back), and Loose means someone grabbed AND threw it — yanking it out of
        // mid-air into the resumed player's hands would be the same steal, one state later.
        if (!_registry.TryGet(propId, out PropState s) || s.Mode != PropMode.Resting)
            return; // consumed/freed, or touched by someone else during the gap
        NetworkedProp? node = NodeFor(propId);
        if (node == null)
            return;
        if (_heldByPeer.ContainsKey(peerId))
            return;
        if (!_registry.SetHolder(propId, peerId))
            return;
        // node.Body's transform, not the node's -- see the note at the live-grab broadcast.
        Rpc(MethodName.ApplyPropState, propId, (int)PropMode.Held, peerId, node.Body.GlobalTransform,
            (int)PropRelease.None);
    }

    // Ground position a dropped/released prop rests at: in front of and at the holder's feet.
    // Height derives from the HOLDER's position, never an absolute world constant — a fixed
    // y=0.5 assumed "the world is a flat slab at y=0" and teleported a prop released on a tower
    // or upper floor through the geometry to ground level (it latches Resting and never
    // simulates again, so nothing could ever rescue it).
    private static Transform3D DropTransformFor(Node3D? avatar)
    {
        if (avatar == null)
            return Transform3D.Identity;
        Vector3 fwd = -avatar.GlobalBasis.Z;
        fwd.Y = 0;
        Vector3 dir = fwd.LengthSquared() > 0.0001f ? fwd.Normalized() : Vector3.Forward;
        Vector3 pos = avatar.GlobalPosition + dir * 0.8f;
        pos.Y = avatar.GlobalPosition.Y + 0.5f;
        return new Transform3D(Basis.Identity, pos);
    }

    // Runs on every peer (server included) when the spawner materialises a prop.
    private Node SpawnFromData(Variant data)
    {
        Godot.Collections.Array a = data.AsGodotArray();
        var prop = new NetworkedProp();
        prop.Init(a[0].AsInt32(), (PropKind)a[1].AsInt32(), a[2].AsTransform3D());
        prop.IsServer = _isServer;
        return prop;
    }

    private static Transform3D PlaceAt(Vector3 pos) => new(Basis.Identity, pos);
}
