using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>The one shared tally, and the only thing allowed to move it.</b> A world adds one of these
/// (<see cref="NodeName"/>), calls <see cref="AdoptAuthoredBubbles"/> once, and every bubble in
/// the level is thereafter server-adjudicated, cross-peer synced, late-join correct and
/// resettable.
///
/// <para><b>The idiom is <c>WalletManager.BroadcastWalletDelta</c>'s</b>, deliberately: the
/// server computes, then one reliable <c>CallLocal</c> RPC applies the result identically
/// wherever it runs, so the server is not a special case in the handler. The deleted
/// <c>SharedPickupCounter</c> (removed in <c>d81c3ba6</c>) had the same shape with one field;
/// this is that shape with a bitset, because "how many" is not enough — a late joiner needs to
/// know WHICH bubbles are gone or it renders a hundred spheres that are not there.</para>
///
/// <para><b>Late join rides <see cref="MultiplayerApi.PeerConnected"/> directly</b> rather than
/// being called from <c>Gameplay.OnPeerConnected</c> the way <c>CycleDriver.SendPhaseTo</c> and
/// <c>PropManager.SendDumpTo</c> are. Same moment, same reliability, one fewer edit for the
/// world packet: BT-8 adds this node and calls <see cref="AdoptAuthoredBubbles"/>, and the
/// late-join contract comes with it instead of being a second thing that can be forgotten. The
/// cost is that this node subscribes and unsubscribes a static-ish signal, which
/// <see cref="_ExitTree"/> handles.</para>
///
/// <para><b>Recorded decision — the collision mask (packet stop condition).</b> This project
/// declares exactly one 3D physics layer, "World" (<c>project.godot [layer_names]</c>), and
/// <c>SandboxAvatar</c>, every prop and every piece of static ground all sit on it. There is
/// therefore NO mask that fires for avatars only. The narrowest correct answer is: mask = layer
/// 1 (the only layer that can produce an avatar overlap at all) plus a
/// <c>body is SandboxAvatar</c> type test in <c>Bubble.OnServerBodyEntered</c>, which is where
/// the actual filtering happens. Widening the mask would add nothing; narrowing it to zero would
/// make the bubble unpoppable. If a future packet splits the layer table, that type test is the
/// one line to revisit.</para>
/// </summary>
public partial class BubbleCounter : Node
{
    /// <summary>The node name a world must use, so every peer resolves the same NodePath for the
    /// RPCs below.</summary>
    public const string NodeName = "BubbleCounter";

    /// <summary>Single instance per running game — the same static-plus-injected convention
    /// <c>PropManager</c>, <c>CycleDriver</c> and <c>WalletManager</c> already use, so BT-8's HUD
    /// can read the tally unwired.</summary>
    public static BubbleCounter? Instance { get; private set; }

    /// <summary>Fires on every peer, after the state has been applied, whenever the tally
    /// changes: <c>(id, count, byPeer)</c> for a pop, <c>(-1, 0, 0)</c> for a reset. BT-8's HUD
    /// and pedestal hang off this rather than polling.</summary>
    public event System.Action<int, int, int>? Changed;

    /// <summary>Fires on every peer, exactly once per completion, when the server says the last
    /// bubble is gone (CELEBRATE-1). Not derived from <see cref="Changed"/> and not derivable from
    /// it: a late joiner's first sync also reaches a full tally, and a player who joins a finished
    /// level must not be greeted by a triumph they did not earn. See
    /// <see cref="TryAnnounceCompletion"/>.</summary>
    public event System.Action? Completed;

    private readonly BubbleCounterState _state = new();
    private readonly List<Bubble> _bubbles = new();
    private bool _isServer;
    private bool _subscribed;

    /// <summary><b>The completion latch — server-side, and the whole of CELEBRATE-1's
    /// idempotency</b> (MECHANICS-BIBLE §4). Set when the announcement goes out, cleared by
    /// <see cref="ServerReset"/> and by nothing else. It lives on the SERVER rather than on each
    /// receiver on purpose: the alternative — every peer deciding for itself whether the count it
    /// is looking at is "new" — is a decision four machines can take four different answers to,
    /// and a late joiner's very first sync would take the wrong one every time. With the latch
    /// here there is exactly one announcement per completion in the whole session, and a peer that
    /// was not there for it correctly never hears it.</summary>
    private bool _announcedCompletion;
    private Material? _sharedFilm;
    private float _lastEnergy = -1f;

    /// <summary>How many bubbles this peer adopted. Zero in a world with none.</summary>
    public int BubbleCount => _bubbles.Count;

    /// <summary>The shared tally. Server-authoritative; on a client this is whatever the last
    /// broadcast or sync said.</summary>
    public int Count => _state.Count;

    /// <summary>True once this peer's state came from the server — either a targeted late-join
    /// <see cref="SyncState"/> or a broadcast. Always true on the server itself. The self-test
    /// asserts a late joiner is synced in its FIRST sample; a HUD should say "—" until it is,
    /// rather than confidently rendering a zero it made up.</summary>
    public bool Synced { get; private set; }

    /// <summary>Has this bubble been popped, as this peer understands it.</summary>
    public bool IsPopped(int id) => _state.IsPopped(id);

    /// <summary>Every popped id, ascending — instrumentation for the sync test.</summary>
    public List<int> PoppedIds() => _state.PoppedIds();

    /// <summary>
    /// The clock every peer's idle bob reads, in seconds. Derived from the replicated cycle
    /// (<c>CycleDriver</c>) rather than from local ticks, so the server's collider and each
    /// client's sphere are in the same place: <c>(cyclesElapsed + phase) * period</c> is
    /// monotone, server-authoritative, and already extrapolated between the driver's 2 Hz
    /// samples.
    ///
    /// <para>Falls back to local engine time when there is no driver (a lab scene, a bubble
    /// opened on its own). In that case the bob is still correct-looking and still bounded; it is
    /// simply no longer cross-peer identical, which is exactly the situation where there are no
    /// other peers. The period is read from this peer's own launch options because
    /// <c>CycleDriver</c> keeps its period private; every peer in a session is launched with the
    /// same one, and a mismatch would cost at most the 0.15 m the offset is clamped to.</para>
    /// </summary>
    public double SharedTimeSec
    {
        get
        {
            CycleDriver? cycle = CycleDriver.Instance;
            if (cycle == null)
                return Time.GetTicksMsec() / 1000.0;
            double period = MpFoundation.NetworkManager.Instance?.Options.CyclePeriodSec ?? 120.0;
            if (period <= 0)
                period = 120.0;
            return (cycle.CyclesElapsed + cycle.Phase) * period;
        }
    }

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        if (_subscribed)
        {
            Multiplayer.PeerConnected -= OnPeerConnected;
            _subscribed = false;
        }
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Server or client. Call before <see cref="AdoptAuthoredBubbles"/> — the adoption
    /// pass is what wires the server-only <c>BodyEntered</c> subscriptions, so it has to already
    /// know which side it is on.</summary>
    public void Setup(bool isServer)
    {
        _isServer = isServer;
        Synced = isServer;
        _announcedCompletion = false;
        BubbleCelebration.ResetCounters();
        if (!isServer || _subscribed)
            return;
        _subscribed = true;
        Multiplayer.PeerConnected += OnPeerConnected;
    }

    /// <summary>
    /// Walks <paramref name="worldRoot"/> once for <see cref="Bubble"/> nodes, sorts them by
    /// node path, and hands out ids 0..N−1 — the <c>PropManager.AdoptAuthoredProps</c> idiom, and
    /// for the same reason: every peer instanced the same scene file, so an ordinal sort on the
    /// path is a stable identity that costs no traffic and needs no authoring discipline.
    ///
    /// <para>Ids start at 0 rather than at an offset because they index a bitset; there is no
    /// second id space to avoid colliding with.</para>
    ///
    /// <para><b>N &gt; <see cref="BubbleCounterState.Capacity"/> is refused, loudly.</b> The
    /// late-join sync is one fixed-size bitset; a level that quietly exceeded it would sync the
    /// first 512 bubbles and silently drop the rest — a bug that only shows up as "some bubbles
    /// came back for the new guy". Excess bubbles are left un-adopted (id −1, inert), which is
    /// visible in the editor and in the log line below.</para>
    /// </summary>
    public void AdoptAuthoredBubbles(Node worldRoot)
    {
        _bubbles.Clear();
        Collect(worldRoot, _bubbles);
        _bubbles.Sort((a, b) => string.CompareOrdinal(a.GetPath().ToString(), b.GetPath().ToString()));
        if (_bubbles.Count > BubbleCounterState.Capacity)
        {
            GD.PushError($"[bubble] {_bubbles.Count} bubbles exceeds the {BubbleCounterState.Capacity} " +
                         "the shared bitset can carry; the surplus is left un-adopted and un-poppable.");
            _bubbles.RemoveRange(BubbleCounterState.Capacity, _bubbles.Count - BubbleCounterState.Capacity);
        }
        for (int i = 0; i < _bubbles.Count; i++)
            _bubbles[i].InitAuthored(i, _isServer, this);
        AdoptSharedFilm();
        GD.Print($"[bubble] adopted {_bubbles.Count} bubble(s) (server={_isServer})");
    }

    /// <summary>
    /// <b>Server only.</b> A player's body entered bubble <paramref name="id"/>. Flips the bit,
    /// increments, broadcasts. A second entrant on the same tick — or any repeat at all — takes
    /// the <c>TryPop</c> false branch and produces no second broadcast, which is the whole of
    /// acceptance criterion 4.
    /// </summary>
    public void ServerPop(int id, int byPeer)
    {
        if (!_isServer || !_state.TryPop(id))
            return;
        // Telemetry: scripts/telemetry/Telemetry.cs exposes NO fire-and-forget event API — it has
        // BeginClientSession plus four aggregate counters (NoteVoiceUsed, NotePropGrabbed,
        // NotePropThrown, NotePropDropped) read once at quit, and it is inert on every headless,
        // bot and server launch by construction. Per packet scope item 8 this therefore logs
        // nothing to Telemetry. The server log line below is the pop record the playtest actually
        // gets; program §6.6's richer event stream needs a Telemetry API that does not exist yet.
        float phase = CycleDriver.Instance?.Phase ?? -1f;
        GD.Print($"[bubbletest] pop id={id} byPeer={byPeer} count={_state.Count} phase={phase:F4}");
        Rpc(MethodName.PopBroadcast, id, _state.Count, byPeer);
        TryAnnounceCompletion();
    }

    /// <summary>The log line the completion prints on the SERVER, once per completion. Named so a
    /// harness greps a constant rather than a retyped string — <c>tests/Run-CelebrateTest.ps1</c>
    /// counts these to prove the announcement is idempotent.</summary>
    public const string CompletionLogPrefix = "[bubbletest] all bubbles popped:";

    /// <summary>
    /// <b>Server only: has the last bubble just gone, and if so, say so — once.</b>
    ///
    /// <para><b>It rides ServerPop's own channel and is sent AFTER the pop broadcast</b>, which is
    /// the ordering property the whole feature rests on. Godot's reliable RPCs are ordered per
    /// transfer channel, so sending the announcement on the same (default) channel as
    /// <see cref="PopBroadcast"/> guarantees every receiver applies the final pop before it hears
    /// the triumph. A channel of its own — <c>NetProfile.HonkChannel</c>'s shape, which is the
    /// obvious precedent for a payload-free event — would have bought head-of-line isolation and
    /// paid for it with a race in which a client celebrates at 99 of 100. Ordering beats
    /// isolation here; the message is one per completion, so there is no head of line to block.
    /// <b>No protocol bump:</b> nothing about the wire format, the handshake or the snapshot
    /// codec moves, so <c>NetProfile.ProtocolVersion</c> stays 14.</para>
    ///
    /// <para><b>The predicate is <c>BubblholicRule.AllPopped</c>, deliberately reused rather than
    /// respelled.</b> "Every bubble is gone" already had exactly one definition in this
    /// repository, it already takes these two values, and it already guards the case that matters
    /// — <c>total == 0</c> is false, so the CI scaffolding worlds that carry no bubbles at all
    /// ("open" without the fixture, "propsync") do not announce a completed level on their first
    /// frame, which <c>0 &gt;= 0</c> alone would. A second copy of the rule here would be one that
    /// could disagree with the achievement about what completing the level means, and a player
    /// hearing the triumph without earning the unlock is the exact shape of that
    /// disagreement.</para>
    /// </summary>
    private void TryAnnounceCompletion()
    {
        if (!_isServer || _announcedCompletion
            || !Achievements.BubblholicRule.AllPopped(_state.Count, _bubbles.Count))
            return;
        _announcedCompletion = true;
        GD.Print($"{CompletionLogPrefix} count={_state.Count}/{_bubbles.Count}");
        Rpc(MethodName.CelebrateBroadcast);
    }

    /// <summary>
    /// <b>Server only, TEST ONLY: pop every bubble that is still up.</b> CELEBRATE-1's harness
    /// (<c>--bubble-pop-all-at</c>, driven by <see cref="BubbleCompletionSchedule"/>).
    ///
    /// <para><b>Why it lives HERE rather than in the schedule that calls it.</b>
    /// <c>BubbleCounterStateTests.NoClientPathCanPopABubble</c> sweeps <c>scripts/**</c> and fails
    /// on any file outside <c>BubbleCounter.cs</c> / <c>Bubble.cs</c> that calls
    /// <see cref="ServerPop"/> — an audit that exists because a pop is the one thing in this
    /// system a client must never be able to drive. A test harness looping <c>ServerPop</c> from
    /// its own file trips it, correctly. The answer is not to widen the audit for a harness: "pop
    /// everything" is a COUNTER operation, so it belongs on the counter, where the server check
    /// and the per-bubble adjudication it delegates to are the same ones a real collider gets. The
    /// schedule above it is then a clock and nothing else, and the audit stays exactly as strict
    /// as it was. <c>BubbleCounterStateTests</c> pins this method's caller set so the new surface
    /// is swept rather than merely permitted.</para>
    ///
    /// <para>Each bubble goes through <see cref="ServerPop"/> individually, so the bits, the
    /// broadcasts and the completion announcement all happen exactly as they do when a player
    /// walks into the last one. Ids already popped take <c>TryPop</c>'s false branch and cost
    /// nothing, which is what makes this safe to call on a partially-collected board.</para>
    /// </summary>
    public void ServerPopAllForTest()
    {
        if (!_isServer)
            return;
        for (int id = 0; id < _bubbles.Count; id++)
            ServerPop(id, 0);
    }

    /// <summary>
    /// <b>Server only.</b> Every bubble back, tally to zero (program D9's reset lever, wired by
    /// BT-8; also the second-run path so a session does not need a relaunch). Idempotent.
    /// </summary>
    public void ServerReset()
    {
        if (!_isServer)
            return;
        _state.Reset();
        // CELEBRATE-1: the lever puts every bubble back for everyone, so the level can be
        // completed again — and completing it again should sound again. Cleared HERE, with the
        // state it belongs to, rather than anywhere a second copy of "the board is fresh" could
        // drift away from it.
        _announcedCompletion = false;
        GD.Print("[bubbletest] reset");
        Rpc(MethodName.ResetBroadcast);
    }

    /// <summary>Server only: push the whole popped set to one peer. Called on connect; public so
    /// a future reconnect path can re-push without a second message type.</summary>
    public void SendStateTo(int peerId)
    {
        if (!_isServer)
            return;
        RpcId(peerId, MethodName.SyncState, _state.Encode(), _state.Count);
    }

    private void OnPeerConnected(long id) => SendStateTo((int)id);

    /// <summary>Server -&gt; everyone, including itself (<c>CallLocal</c>): one bubble is gone.
    /// The count is hard-set from the server's value rather than incremented locally, the same
    /// way <c>WalletManager.BroadcastWalletDelta</c> hard-sets balances — a peer that had somehow
    /// diverged is corrected by the next broadcast instead of staying wrong forever.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, CallLocal = true)]
    private void PopBroadcast(int id, int count, int byPeer)
    {
        _state.TryPop(id);
        CrossCheckCount(count);
        Synced = true;
        Bubble? bubble = BubbleFor(id);
        if (bubble != null)
        {
            Vector3 at = bubble.GlobalPosition;
            bubble.ApplyPopped();
            PlayPopFeedback(at);
        }
        Changed?.Invoke(id, count, byPeer);
    }

    /// <summary>
    /// <b>Server -&gt; everyone, including itself: the last bubble is gone</b> (CELEBRATE-1).
    ///
    /// <para><b>Payload-free.</b> There is nothing to say — the receiver's own tally is already
    /// correct, because the pop that completed the level went out on this same channel one message
    /// earlier (see <see cref="TryAnnounceCompletion"/>). <c>HonkManager.PlayHonk</c>'s shape: an
    /// event, not a state message, and the smallest one that can be sent.</para>
    ///
    /// <para><b>Defence in depth behind <c>RpcMode.Authority</c></b>, exactly as
    /// <c>HonkManager.PlayHonk</c> does it: <c>SceneMultiplayer</c> relays client→client RPCs by
    /// default, so a hostile client could aim this at another client. The attribute refuses it
    /// first and this line refuses it second. Forging it would cost an attacker one spurious
    /// sound; it is refused anyway, because a security property nothing ever checks is a
    /// comment.</para>
    ///
    /// <para><c>CallLocal</c> so a listening host runs the same receive path as every client
    /// rather than a second copy of it — the idiom every RPC in this class already uses.</para>
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, CallLocal = true)]
    private void CelebrateBroadcast()
    {
        if (!Multiplayer.IsServer() && Multiplayer.GetRemoteSenderId() != 1)
            return;
        Completed?.Invoke();
        BubbleCelebration.Play(this);
    }

    /// <summary>Server -&gt; everyone: every bubble is back.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, CallLocal = true)]
    private void ResetBroadcast()
    {
        _state.Reset();
        Synced = true;
        foreach (Bubble bubble in _bubbles)
            bubble.ApplyRestored();
        Changed?.Invoke(-1, 0, 0);
    }

    /// <summary>Server -&gt; one joining peer: the whole popped set, before that peer has
    /// rendered a frame with bubbles in it. The <c>CycleDriver.SyncPhaseTo</c> pattern.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncState(byte[] bitset, int count)
    {
        if (!_state.Decode(bitset, count))
        {
            // Refused, not adopted: a payload whose bits and count disagree would leave this peer
            // permanently unable to explain its own HUD. Loud, because it can only mean a version
            // skew or a corrupted message, and both are worth a line in the log.
            GD.PushError($"[bubble] rejected a late-join sync ({bitset?.Length ?? -1} bytes, count {count})");
            return;
        }
        Synced = true;
        foreach (Bubble bubble in _bubbles)
        {
            if (_state.IsPopped(bubble.Id))
                bubble.ApplyPopped();
            else
                bubble.ApplyRestored();
        }
        Changed?.Invoke(-1, _state.Count, 0);
    }

    public override void _Process(double delta)
    {
        if (_revealRemainingSec > 0.0)
            _revealRemainingSec -= delta;

        if (_sharedFilm == null)
            return;
        CycleDriver? cycle = CycleDriver.Instance;
        float energy = cycle == null
            ? 0f
            : BubbleOscillation.EmissionEnergy(cycle.Phase, cycle.CyclesElapsed);
        // EGG-2: the secret bubble's reveal, ADDED to the night curve rather than replacing it.
        // Additive is what makes one number work at noon (where the curve is 0) and at midnight
        // (where it is not) without a second curve to keep in step, and it means the reveal ending
        // hands the film straight back to the curve with nothing to restore.
        if (_revealRemainingSec > 0.0)
            energy += _revealBoost;

        // One write for every bubble in the level, and only when it actually moved: the film is a
        // single shared material (see AdoptSharedFilm), so the night glow costs one property set
        // per frame at 100 bubbles rather than 100.
        if (Mathf.IsEqualApprox(energy, _lastEnergy))
            return;
        _lastEnergy = energy;
        ApplyGlow(_sharedFilm, energy);
    }

    /// <summary>The emission energy currently written to the film. Read by the self-test
    /// (acceptance criterion 7).</summary>
    public float CurrentEmissionEnergy => _lastEnergy < 0f ? 0f : _lastEnergy;

    // --- The secret bubble's reveal (EGG-2) --------------------------------------------------

    private double _revealRemainingSec;
    private float _revealBoost;

    /// <summary>True while <see cref="SecretBubble"/>'s reveal is lighting the level up. Read by
    /// captures and by the self-test; nothing gameplay-facing keys off it.</summary>
    public bool Revealing => _revealRemainingSec > 0.0;

    /// <summary>Seconds of reveal left, 0 when it is not running.</summary>
    public float RevealRemainingSec => _revealRemainingSec > 0.0 ? (float)_revealRemainingSec : 0f;

    /// <summary>
    /// <b>Light every un-popped bubble in the level for <paramref name="seconds"/>.</b>
    /// <see cref="SecretBubble"/>'s effect, and the reason it lives here rather than there: the
    /// film is ONE shared material this class owns and re-writes every frame, so a boost written
    /// from outside would be silently overwritten on the next tick the night curve moved — the
    /// exact "poked value, republished away" trap <c>NightAidDriver</c>'s own header records for
    /// <c>bt_darkness</c>.
    ///
    /// <para>Called on EVERY peer, from the secret bubble's own reliable broadcast — this is
    /// presentation, applied identically wherever it runs, and it touches no bit of
    /// <see cref="BubbleCounterState"/>. <b>The tally is unchanged by a reveal</b>, which is the
    /// property that keeps the secret a secret rather than a bug report.</para>
    ///
    /// <para>Re-entrant: a second call restarts the window rather than stacking the boost, so the
    /// reveal can never brighten without bound.</para></summary>
    public void BeginReveal(float seconds, float boost)
    {
        if (seconds <= 0f || boost <= 0f)
            return;
        _revealRemainingSec = seconds;
        _revealBoost = boost;
        // Forces the next _Process write even if the night curve has not moved a hair, which at
        // noon (curve pinned at 0) is every frame.
        _lastEnergy = -1f;
    }

    // --- internals -----------------------------------------------------------------------

    /// <summary>The bubble adopted under <paramref name="id"/>. Ids are the adoption index by
    /// construction, so this is a direct lookup; the identity check is a cheap guard against a
    /// future change to <see cref="AdoptAuthoredBubbles"/> silently desynchronising the two.</summary>
    private Bubble? BubbleFor(int id)
    {
        if (id < 0 || id >= _bubbles.Count)
            return null;
        Bubble candidate = _bubbles[id];
        return candidate.Id == id ? candidate : null;
    }

    /// <summary>
    /// The server's count against the one this peer's own bitset produces. <b>It is a check, not
    /// a setter</b> — <see cref="BubbleCounterState"/> derives Count from its bits precisely so
    /// there is one authority for the fact, and a client that overwrote the number without the
    /// matching bit would render a HUD its own popped set cannot explain.
    ///
    /// <para>Over a reliable, ordered channel the two cannot diverge in a live session; the one
    /// window is a peer that joined mid-flight, which <see cref="SyncState"/> closes by replacing
    /// both together. So a mismatch here means something structural, and it gets a log line
    /// rather than a silent repair that would hide it.</para>
    /// </summary>
    private void CrossCheckCount(int count)
    {
        if (count == _state.Count)
            return;
        GD.PushWarning($"[bubble] count divergence: server says {count}, local bitset says {_state.Count}");
    }

    /// <summary>Every peer: the pop's two halves (packet scope item 3) — a pooled positional
    /// one-shot through the §4-compliant path, and a small cosmetic burst. Both are presentation;
    /// a headless peer that has no audio device simply never hears it, and neither is on any
    /// gameplay path.</summary>
    private void PlayPopFeedback(Vector3 at)
    {
        SfxLab.PlayStream3D(this, at, SfxLab.Get(Sfx.Pop), volumeDb: -8f, pitchJitter: 0.12f, maxDistance: 28f);
        JuiceFx.Puff(this, at, count: 10, color: PuffColor, size: 0.05f, speed: 1.6f, lifetime: 0.45f);
    }

    /// <summary>Soap-film white with a faint cool cast — a stand-in that reads on both the pale
    /// section grounds and against night. BT-9 owns the real look.</summary>
    private static readonly Color PuffColor = new(0.86f, 0.93f, 0.98f, 0.8f);

    /// <summary>
    /// Gives every adopted bubble ONE material instance, duplicated once from whatever the scene
    /// authored, so the per-frame glow write above is a single property set. A hundred bubbles
    /// each carrying their own duplicate would be a hundred unique materials and a hundred draw
    /// calls for what is visually one thing — the GTX 970 Forward+ floor is the budget this level
    /// has to hold with five sections and ~100 of these in it.
    /// </summary>
    private void AdoptSharedFilm()
    {
        _sharedFilm = null;
        foreach (Bubble bubble in _bubbles)
        {
            MeshInstance3D? visual = bubble.Visual;
            if (visual == null)
                continue;
            Material? authored = visual.MaterialOverride ?? visual.Mesh?.SurfaceGetMaterial(0);
            if (authored == null)
                continue;
            _sharedFilm ??= (Material)authored.Duplicate();
            visual.MaterialOverride = _sharedFilm;
        }
        _lastEnergy = -1f;
    }

    /// <summary>Writes the night glow onto whichever material the level ships. A
    /// <c>StandardMaterial3D</c> takes it as emission; a <c>ShaderMaterial</c> takes it as the
    /// <see cref="FilmEmissionUniform"/> uniform.
    ///
    /// <para><b>THE NAME IS LOAD-BEARING AND IT WAS WRONG FROM BT-6 UNTIL DARK-1 (2026-08-28).</b>
    /// This method used to write <c>"night_glow"</c> — a name BT-6 invented, guarded by a comment
    /// saying a <c>ShaderMaterial</c> takes it "if it declares one". BT-9 then added the uniform
    /// to <c>bubble_film.gdshader</c> and called it <c>emission_energy</c>, and neither side
    /// reconciled. <c>night_glow</c> appears nowhere in the repository. <b>Godot silently ignores
    /// a write to a uniform that does not exist</b> — no error, no warning, no failed test — so
    /// the driver above computed the correct curve every frame, wrote it every frame, and the
    /// bubbles never glowed once. The `if it declares one` hedge is what made that survivable
    /// for three packets: it converted a name mismatch into a documented no-op.</para></summary>
    private static void ApplyGlow(Material material, float energy)
    {
        switch (material)
        {
            case StandardMaterial3D std:
                std.EmissionEnabled = energy > 0f;
                std.EmissionEnergyMultiplier = energy;
                break;
            case ShaderMaterial shader:
                shader.SetShaderParameter(FilmEmissionUniform, energy);
                break;
        }
    }

    /// <summary>The uniform <c>bubble_film.gdshader</c> actually declares, and the same name
    /// <c>bubble_film.tres</c> and the BT-9 docs already use. Named once, here, so the C# side
    /// and the shader side can never drift apart silently again.</summary>
    private const string FilmEmissionUniform = "emission_energy";

    private static void Collect(Node n, List<Bubble> found)
    {
        foreach (Node child in n.GetChildren())
        {
            if (child is Bubble bubble)
                found.Add(bubble);
            Collect(child, found);
        }
    }
}
