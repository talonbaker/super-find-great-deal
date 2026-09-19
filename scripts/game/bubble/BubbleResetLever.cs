using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Ui;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>The lever beside the pedestal that puts every bubble back.</b>
///
/// <para><b>Why it exists</b> (program D9): a one-hour session with 100 bubbles finishes early,
/// and a second run should not need a relaunch. So the reset is diegetic — something in the world
/// any player can walk to and pull — rather than a console command or a host-only menu.</para>
///
/// <para><b>The client asks; the server decides.</b> <see cref="Press"/> runs on the pressing
/// peer and does exactly one thing: send <see cref="RequestReset"/>. The server re-checks the
/// cooldown itself (<see cref="BubbleResetGate"/>) and, if it honours the press, calls
/// <c>BubbleCounter.ServerReset()</c>, whose own reliable <c>CallLocal</c> broadcast is what
/// every peer — including the presser — actually reacts to. No peer, presser included, animates
/// the lever off its own press: the swing rides the broadcast, so what everyone sees is the
/// reset, not six independent guesses about it (MECHANICS-BIBLE — one authority, one message,
/// applied identically wherever it runs).</para>
///
/// <para><b>Two presses inside 1.2 s produce one reset</b>, whether they came from one player
/// double-tapping or from six players who all saw the counter fill at once. The rule lives in
/// <see cref="BubbleResetGate"/> and is exhaustively unit-tested there; this class owns only the
/// clock it is measured against and the RPC it is reached by.</para>
///
/// <para><b>Feedback, per INTERACTION-BIBLE.</b> Three channels and none of them is the reset
/// itself: it READS as interactable before it is touched (the shimmer, via
/// <see cref="IHighlightable"/>, and <c>InteractPrompt</c>'s floating key chip, both driven by
/// <c>InteractHighlighter</c> now that <see cref="IPressable"/> is one of its candidates); it
/// ANSWERS a press that is honoured (the swing, plus <c>Sfx.Thunk</c>, on every peer); and it
/// answers a press it refuses (a short shake and a duller click on the presser only — the world
/// did not change, so nothing about it should travel).</para>
///
/// <para><b>LEVER-1, 2026-08-29 — it says what it does, and it asks first.</b> Talon, from his own
/// playtest: <i>"the reset lever next to the bubble counter. There needs to be a sign at least
/// that it will reset the bubble counter and a warning ... because I would be upset if I spent
/// hours collecting all the bubbles and then someone reset my progress."</i> Two things came out
/// of that, and they are separate:</para>
/// <list type="bullet">
///   <item><b>The sign.</b> Four <c>Label3D</c> plates on the pedestal's four faces, authored in
///   the scene — non-billboarding, matching the hub's own diegetic plinth signs rather than
///   inventing a widget, and deliberately NOT the camera-facing kind UI-1 had just finished
///   removing from this level. Idle they say what the lever does; armed they become the warning
///   and carry the live tally, so the number at risk is on the sign the moment it is at risk.
///   They are the same nodes in both states because a warning shown somewhere else would leave
///   the calm text still readable beside it.</item>
///   <item><b>The warning.</b> A deliberate two-stage press held in
///   <see cref="BubbleResetConfirm"/> — server-side, per peer, cancellable, and kept strictly
///   apart from <see cref="BubbleResetGate"/>'s debounce because they answer different questions.
///   <see cref="BubbleResetAdjudicator"/> composes the two in the order that matters. See those
///   classes for the whole argument.</item>
/// </list>
///
/// <para><b>Why two presses rather than a hold.</b> <see cref="IPressable"/> carries a single
/// discrete <see cref="Press"/> and no hold channel, so hold-to-confirm would mean opening one
/// through <c>InteractHighlighter</c> and <c>SandboxAvatar</c> — shared interaction core, edited
/// for one level's prop, which is the coupling <see cref="IPressable"/> exists to avoid. Worse, a
/// hold is measured on the pressing client and arrives as "I held it long enough", which hands
/// the client the very decision this packet exists to take away from it. Two presses need no new
/// input, and both halves are the server's own clock against its own state.</para>
///
/// <para><b>LEVER-2, 2026-08-29 — and the sign still could not be read.</b> Talon, next playtest:
/// <i>"please include a non-diegetic 'pop up' ... to warn the player that if they toggle the switch
/// in the main level it will reset their bubbles. Please make sure they know this because the
/// warning is good but it's too small for them to read and usually the player character is
/// blocking the text anyway."</i> The wording was not the defect. Two properties of a world-space
/// sign were: it renders at world scale, and at the one distance you can press the lever your own
/// third-person body is between the camera and the plate. <b>A bigger sign is a bigger thing behind
/// the same body</b>, so the answer is a surface the world cannot occlude —
/// <see cref="ConsequenceWarning"/>, raised while <see cref="Highlighted"/> and lowered the instant
/// it clears. <b>The sign stays.</b> It marks where the lever is from across the hub and shows the
/// other five players what is pending; the pop-up carries the consequence, at reading size, to the
/// one player who can cause it. Neither does the other's job.</para>
///
/// <para><b>And the pop-up says whose bubbles.</b> <see cref="BubbleCounter"/> holds ONE
/// <see cref="BubbleCounterState"/> — a single server-authoritative bitset with no per-peer
/// partition anywhere in it — and <c>ServerReset</c> clears that bitset and broadcasts
/// <c>ResetBroadcast</c> to every peer, restoring every bubble everywhere. There is no such thing
/// as "your" bubbles in this level. So the copy below says <i>shared</i> and says <i>everyone</i>,
/// because "this will reset your bubbles" would be a strictly smaller warning than the truth, and
/// the thing Talon said he would be upset about is losing someone else's hours.</para>
///
/// <para><b>The armed warning is broadcast to everyone, not shown to the presser alone</b>
/// (INTERACTION-BIBLE §8.1: this is an <c>all_players</c> consequence — the run changes for
/// everyone). That is the co-op half of Talon's sentence: a teammate at the board can see the
/// lever go red and say something inside the four seconds before it lands. The channels are
/// redundant per §8.2 — the sign's text changes, the sign's colour changes, and a flat positional
/// click plays — so the warning survives both a player whose audio is severed and a player who is
/// not reading.</para>
/// </summary>
public partial class BubbleResetLever : Node3D, IPressable
{
    /// <summary>The node name a world must use. Every peer instantiates the same scene into the
    /// same parent, so the RPC's NodePath resolves identically without any spawn traffic — the
    /// idiom <c>BubbleCounter</c> already relies on.</summary>
    public const string NodeName = "BubbleResetLever";

    /// <summary>The authored scene.</summary>
    public const string ScenePath = "res://scenes/game/props/BubbleResetLever.tscn";

    /// <summary>How far east of the pedestal the lever stands, in metres (packet scope item 4).
    /// Comfortably outside <see cref="PressRadius"/> of the plinth itself, so walking up to read
    /// the board never arms the lever.</summary>
    public const float OffsetFromPedestalM = 1.5f;

    /// <summary>Reach. Slightly under the door's 1.8 m: the lever sits on a plinth in the open and
    /// nothing else in the hub competes for the press, so the radius only has to be comfortable
    /// rather than generous.</summary>
    public const float PressRadius = 1.6f;

    /// <summary>How far the handle swings, in radians. A visible throw at 20 m, and it returns —
    /// the lever is a momentary control, not a switch with two states, because "every bubble is
    /// back" is an event and a latched handle would claim it is a mode.</summary>
    private const float SwingRad = 0.9f;

    private const double SwingOutSec = 0.12;
    private const double SwingBackSec = 0.30;

    /// <summary>The node that holds the four sign plates. One parent so the whole set is found,
    /// written and coloured in one pass and cannot drift apart face to face.</summary>
    public const string SignsNodeName = "Signs";

    /// <summary>What the sign says when nothing is pending. Three lines, and the third is the one
    /// that matters: naming the loss ("count back to zero") is what a player walking up to an
    /// unlabelled lever did not have. Authored here rather than in the scene so the idle and the
    /// armed text sit next to each other and cannot be edited apart.</summary>
    public const string SignIdleText = "RESET\nALL BUBBLES RETURN\nCOUNT BACK TO ZERO";

    /// <summary>The armed text, formatted with the live tally. <c>{0}</c> is the count at risk —
    /// the number is the warning, not decoration: "wipe 87 bubbles" is a sentence a player can
    /// refuse, and "reset the counter" is not.</summary>
    public const string SignArmedFormat = "WARNING\nPULL AGAIN TO WIPE\n{0} BUBBLES";

    // --- the pop-up's copy (LEVER-2) ---------------------------------------------------------
    //
    // Authored here beside the sign text for the reason the sign text is authored here: three
    // wordings of one warning that must never be edited apart. Every line below is checked against
    // BubbleCounter's actual behaviour — one shared bitset, no per-peer state — in
    // BubbleResetConfirmTests.

    /// <summary>Heading while a player is in reach and nothing is pending.</summary>
    public const string PopupIdleHeading = "RESET LEVER";

    /// <summary>The idle warning. <c>{0}</c> is the live tally. It leads with SHARED because that
    /// is the fact a player cannot recover from finding out afterwards.</summary>
    public const string PopupIdleBodyFormat =
        "The count is SHARED — pulling this puts all {0} bubbles back for EVERYONE, not just "
        + "for you.";

    /// <summary>The same warning with an empty board. <b>Caught by the LEVER-2 capture, not by
    /// reasoning:</b> the shipped frame read <i>"puts all 0 bubbles back"</i>, which is a sentence
    /// that reads as a bug and costs the whole plate its credibility on the one screen where the
    /// player has to believe it. A tally is the warning when there is one; when there is not, the
    /// rule is still worth teaching and the number is not.</summary>
    public const string PopupIdleBodyEmpty =
        "The count is SHARED — a pull puts EVERYONE's bubbles back, not just yours. Nothing is "
        + "popped right now.";

    /// <summary>The idle footnote: which key does it, that one press is not enough, and what the
    /// second one costs. <c>{0}</c> is the LIVE interact binding — see
    /// <see cref="InteractKeyLabel"/>; never a hardcoded letter.
    ///
    /// <para>Talon, 2026-08-30 playtest note 3: <i>"when this pops up, please include in the text
    /// 'press E to reset' and then when the warning is shown again, again include 'press E to
    /// reset' to make it clear to the users what to do."</i> The plate said what a pull WOULD cost
    /// and never said what to press — a warning that is only a warning, on a fixture whose whole
    /// job is to be operated deliberately.</para>
    ///
    /// <para>The window is deliberately not quoted in seconds — the number lives in
    /// <see cref="BubbleResetConfirm.ConfirmWindowSec"/> and a copy of it here would be the
    /// second place it could be wrong.</para></summary>
    public const string PopupIdleFootFormat =
        "Press {0} to pull the lever. One pull only arms it; press {0} again to reset, and the "
        + "count returns to zero.";

    /// <summary>Armed, by the player reading it. <c>{0}</c> is the tally the server warned
    /// about — the same number the sign is showing, so the two surfaces can never disagree in
    /// front of the same player.</summary>
    public const string PopupArmedSelfHeadingFormat = "PULL AGAIN AND {0} BUBBLES ARE GONE";

    /// <summary>Armed by you — the body.</summary>
    public const string PopupArmedSelfBody =
        "That is the whole group's progress, not only yours.";

    /// <summary>Armed by you — the key and the way out, in that order because the reader who got
    /// here pressed it on purpose. <c>{0}</c> is the LIVE interact binding. True only for the peer
    /// that armed it, which is why this string is not shared with the case below (the
    /// highlighter's walk-away cancel is sent by the arming peer alone; see
    /// RequestCancelIfMine).</summary>
    public const string PopupArmedSelfFootFormat =
        "Press {0} to reset. Step away from the lever to cancel.";

    /// <summary>Armed by somebody else, read by a player standing at the lever. <c>{0}</c> is the
    /// tally at risk.</summary>
    public const string PopupArmedOtherHeadingFormat = "RESET ARMED — {0} BUBBLES AT RISK";

    /// <summary>Armed by somebody else — the body. It names the reader's own stake, because the
    /// shared count is exactly what makes another player's press the reader's problem.</summary>
    public const string PopupArmedOtherBody =
        "One more pull clears the shared count. Everyone loses them, you included.";

    /// <summary>Armed by somebody else — the footnote. Says the honest thing: this reader cannot
    /// cancel another peer's warning, only outlast it. <c>{0}</c> is the LIVE interact binding.
    ///
    /// <para><b>The lapse is stated first and the key second, and that order is the whole
    /// content of the sentence.</b> The reader of THIS wording is a bystander to somebody else's
    /// arm, so the action they most likely want is to do nothing — but the body directly above
    /// already tells them "one more pull clears the shared count", and naming the consequence
    /// while hiding the key is the worse of the two failures. It grants nothing: any peer's second
    /// press confirms, with or without this line.</para></summary>
    public const string PopupArmedOtherFootFormat =
        "It lapses on its own in a few seconds if nobody pulls again — or press {0} to reset it "
        + "now.";

    /// <summary>The armed heading with an empty board, shared by both armers — "0 BUBBLES AT RISK"
    /// says the opposite of what it means. The bodies still differ, because what the reader can DO
    /// about it still differs.</summary>
    public const string PopupArmedEmptyHeading = "RESET ARMED — NOTHING POPPED YET";

    /// <summary>Idle sign colour — the same warm the handle already shimmers, so the plate reads
    /// as part of this fixture and not as a second one.</summary>
    private static readonly Color SignIdleColour = new(0.92f, 0.88f, 0.80f);

    /// <summary>Armed sign colour. Red because the whole point is that it stops looking like the
    /// thing you were just reading; flat, not pulsing (see the /direct entry — this beat is
    /// administrative on purpose and a throb would make a chore feel like a set piece).</summary>
    private static readonly Color SignArmedColour = new(1.0f, 0.32f, 0.22f);

    private readonly BubbleResetAdjudicator _decision = new();
    private readonly System.Collections.Generic.List<Label3D> _signs = new();
    private Node3D? _handle;
    private StandardMaterial3D? _handleMaterial;
    private BubbleCounter? _counter;
    private bool _isServer;
    private bool _highlighted;
    private double _clock;

    /// <summary>Which peer's warning is standing on THIS peer's copy of the lever, or 0 for none.
    /// Server-authored and broadcast; every peer including the server reads only this, so the six
    /// signs in a session can never disagree about what the lever is doing.</summary>
    private int _warningByPeer;

    /// <summary>The tally the standing warning was raised about — the server's number, carried on
    /// the broadcast, so the pop-up and the sign quote the same figure even if a teammate pops
    /// another bubble during the four-second window.</summary>
    private int _warningCount;

    /// <summary>The screen-space plate (LEVER-2). Null on any peer that renders nothing, and every
    /// call below is written to work without it.</summary>
    private ConsequenceWarning? _popup;

    /// <summary>What the pop-up last said, so the log line the capture harness gates on is emitted
    /// once per real change rather than once per frame.</summary>
    private string _popupState = "none";

    /// <summary>Set before the node enters the tree. Only the server runs the gate.</summary>
    public bool IsServer { get => _isServer; set => _isServer = value; }

    /// <summary>Accepted presses this session, server-side — read by the suites, which assert
    /// "exactly one reset from two presses" rather than "a reset happened".</summary>
    public int AcceptedPresses => _decision.Accepted;

    /// <summary>Refused presses this session, server-side.</summary>
    public int RefusedPresses => _decision.Refused;

    /// <summary>Warnings raised this session, server-side — a first press that armed rather than
    /// reset. Read by the suites.</summary>
    public int ArmedPresses => _decision.Armed;

    /// <summary>Confirmations this session, server-side. Note this counts presses the CONFIRM
    /// stage honoured; the debounce may still have refused the reset behind it, which is exactly
    /// the "two peers confirm at once" case the suites assert on.</summary>
    public int ConfirmedPresses => _decision.Confirmed;

    public float PressRadiusM => PressRadius;

    /// <summary>
    /// The shimmer flag <c>InteractHighlighter</c> drives — and, since LEVER-1, the cancel.
    ///
    /// <para><b>Walking away from an armed lever withdraws the warning</b>, which is what makes
    /// the warning a warning rather than a countdown (INTERACTION-BIBLE §7: an interruption
    /// mid-interaction must not leave the player or the interactable in a state neither can
    /// leave). The highlighter clears this the moment the avatar leaves
    /// <see cref="PressRadius"/> or another candidate wins, so "step back from the lever" is
    /// already a gesture the game measures — no second key, no new input path. The peer that
    /// armed asks the server to drop it; the server, as ever, decides.</para>
    /// </summary>
    public bool Highlighted
    {
        get => _highlighted;
        set
        {
            if (_highlighted == value)
                return;
            _highlighted = value;
            if (!value)
                RequestCancelIfMine();
            ApplyHighlight();
            // LEVER-2: the pop-up's whole trigger. Proximity is already measured, already
            // throttled and already the definition of "near enough to pull it", so the warning
            // rides it rather than opening a second poll that could disagree with the shimmer.
            RefreshPopup();
        }
    }

    public override void _Ready()
    {
        AddToGroup(IPressable.Group);
        CollectSigns();
        // Parented to the lever, so it cannot outlive the thing it is warning about. Null on a
        // dedicated server; see ConsequenceWarning.Attach.
        _popup = ConsequenceWarning.Attach(this);
        _handle = GetNodeOrNull<Node3D>("Handle");
        if (_handle == null)
        {
            GD.PushWarning("[bubbletest] reset lever has no Handle node — it will work and show "
                           + "nothing, which is the failure mode INTERACTION-BIBLE 1 is about.");
            return;
        }

        // ONE duplicate shared by every mesh in the handle, rather than one each: the shimmer is a
        // property of the lever, so a single material means a single write per state change and
        // the shaft and knob can never disagree about whether they are highlighted. Duplicated
        // (not written in place) because the authored material is a scene sub-resource and the
        // shimmer must not reach anything else that ever comes to share it.
        foreach (Node child in _handle.GetChildren())
        {
            if (child is not MeshInstance3D mesh)
                continue;
            _handleMaterial ??= mesh.GetActiveMaterial(0)?.Duplicate() as StandardMaterial3D;
            if (_handleMaterial != null)
                mesh.MaterialOverride = _handleMaterial;
        }
        ApplyHighlight();
    }

    public override void _ExitTree()
    {
        if (_counter != null && GodotObject.IsInstanceValid(_counter))
            _counter.Changed -= OnCounterChanged;
        _counter = null;
        // The pop-up lives on the scene root rather than under this node — the idiom every
        // drawing CanvasLayer here already follows (see ConsequenceWarning.Attach) — so its
        // lifetime is this class's to end. A lever that leaves the level must not leave a plate
        // behind describing it.
        if (_popup != null && GodotObject.IsInstanceValid(_popup))
            _popup.QueueFree();
        _popup = null;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        // A lapsed warning has to become a quiet sign on every peer, and nothing else will notice
        // it lapse: expiry is the passive half of "cancellable" and there is no press behind it.
        // ONE reconcile point for the sign, shared with every press and cancel below, so the
        // broadcast can never be right on one path and forgotten on another.
        if (_isServer)
            SyncWarning();
        if (_counter != null && GodotObject.IsInstanceValid(_counter))
            return;
        // The counter is added by the world in the same _Ready pass as this lever, and in a
        // fixture it can arrive later still, so it is resolved lazily rather than captured.
        BubbleCounter? found = BubbleCounter.Instance;
        if (found == null)
            return;
        _counter = found;
        _counter.Changed += OnCounterChanged;
    }

    /// <summary>
    /// <see cref="IPressable.Press"/> — the local player pressed Interact with this lever as the
    /// winning candidate. A client asks; never acts.
    ///
    /// <para><b>The host takes the short path deliberately, and it is not an optimisation.</b> A
    /// listen-host and an offline launch are both "server", and an offline launch has no
    /// multiplayer peer at all — <c>RpcId(1, …)</c> there is a call into nothing. Routing the
    /// server's own press straight to <see cref="ServerPress"/> means the lever works in a
    /// single-player launch, and it changes no behaviour on a real host: the gate, the log line
    /// and the broadcast are identical either way, because they all live on the far side of this
    /// call rather than on this side of it.</para>
    /// </summary>
    public void Press()
    {
        // LD-2: the pull is an act of the local body whether or not the reset is granted — the
        // gate is the server's, the reach for the lever is the player's.
        Sail.Game.World.CadenceTracker.Instance?.Clock.NoteAct("lever");
        if (_isServer)
        {
            int self = Multiplayer?.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
            ServerPress(self == 0 ? 1 : self);
            return;
        }
        RpcId(1, MethodName.RequestReset);
    }

    /// <summary>
    /// Client -&gt; server: "I pulled the lever." <c>AnyPeer</c> because any of the six may, and
    /// unreliable would be wrong for a request that has no repeat.
    ///
    /// <para><b>Nothing is trusted from the payload because there is no payload.</b> The sender
    /// id comes from the multiplayer layer, and the only decision — may this press count — is the
    /// server's own clock against its own gate.</para>
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestReset()
    {
        int byPeer = Multiplayer.GetRemoteSenderId();
        ServerPress(byPeer == 0 ? 1 : byPeer);
    }

    /// <summary>
    /// <b>Server only.</b> The one entry point a press reaches, whichever way it arrived — an
    /// RPC from a client, the host's own <see cref="Press"/>, or the fixture's scripted pull.
    /// Returns true when the press was honoured, i.e. when a reset actually landed.
    ///
    /// <para><b>The decision itself is not here.</b> It is
    /// <see cref="BubbleResetAdjudicator"/> — the warning and the debounce composed in the order
    /// that matters, with no Godot in it, so the case Talon named can be proved headless instead
    /// of only in a three-peer session. This method owns what the verdict then costs: a log line,
    /// the sign, the feedback RPC, and the counter.</para>
    /// </summary>
    public bool ServerPress(int byPeer)
    {
        if (!_isServer)
            return false;

        ResetVerdict verdict = _decision.Press(byPeer, _clock);
        // The sign follows EVERY verdict, before anything else happens, because the arm and the
        // reset are both changes to what the plate should say and only one code path may decide.
        SyncWarning();

        if (verdict == ResetVerdict.Armed)
        {
            GD.Print($"[bubbletest] reset ARMED byPeer={byPeer} — "
                     + $"{BubbleResetConfirm.ConfirmWindowSec:F1}s to confirm, "
                     + $"{CountAtRisk()} bubbles at risk");
            return false;
        }
        // Ignored is a bounced finger inside MinDwellSec. The warning it would have armed is
        // already standing and already saying the only thing there is to say, so this is the one
        // verdict with no feedback of its own — anything else would be the lever answering a
        // press the player does not believe they made.
        if (verdict == ResetVerdict.Ignored)
            return false;

        if (verdict == ResetVerdict.Debounced)
        {
            GD.Print("[bubbletest] reset refused (cooldown "
                     + $"{_decision.CooldownRemainingSec(_clock):F2}s left) byPeer={byPeer}");
            // Called, not RpcId'd, when the presser IS this peer: an offline launch has no
            // multiplayer peer to send to, and on a listen-host an RpcId to self is a round trip
            // for something already in hand. And RpcId'd only to a peer the layer still lists:
            // a player can leave between the press and the refusal, and a dedicated server's
            // fixture pulls the lever as ids that were never connections at all.
            if (Multiplayer?.MultiplayerPeer == null || byPeer == Multiplayer.GetUniqueId())
                RefusedFeedback();
            else if (System.Array.IndexOf(Multiplayer.GetPeers(), byPeer) >= 0)
                RpcId(byPeer, MethodName.RefusedFeedback);
            return false;
        }
        // Program §6 item 6 asked for a Telemetry event here. There is no fire-and-forget event
        // API on scripts/telemetry/Telemetry.cs — it carries BeginClientSession plus four
        // aggregate counters read once at quit — and BT-6 recorded the same finding for the pop
        // event. So the record the playtest actually gets is this line, same shape as BT-6's, and
        // the missing API is reported rather than invented.
        GD.Print($"[bubbletest] reset lever pulled byPeer={byPeer}");
        _counter ??= BubbleCounter.Instance;
        if (_counter == null)
        {
            GD.PushWarning("[bubbletest] reset lever pulled with no BubbleCounter in the world.");
            return false;
        }
        _counter.ServerReset();
        return true;
    }

    /// <summary>
    /// <b>Server only.</b> Withdraw <paramref name="byPeer"/>'s warning. Returns true when there
    /// was one to withdraw — a cancel from a peer with no arm is a no-op, not an error, because
    /// the highlighter clears on every walk-away whether or not anything was pending.
    /// </summary>
    public bool ServerCancel(int byPeer)
    {
        if (!_isServer)
            return false;
        bool had = _decision.Cancel(byPeer);
        if (had)
            GD.Print($"[bubbletest] reset warning withdrawn byPeer={byPeer} (stepped away)");
        SyncWarning();
        return had;
    }

    /// <summary>Client -&gt; server: "I have stepped away from the lever." Sent only by a peer
    /// that believes it is the one being warned about, so an idle session sends nothing.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestCancel()
    {
        int byPeer = Multiplayer.GetRemoteSenderId();
        ServerCancel(byPeer == 0 ? 1 : byPeer);
    }

    /// <summary>The walk-away half of the cancel, from the <see cref="Highlighted"/> setter. Only
    /// the peer the sign is currently naming asks — everyone else losing the highlight is just a
    /// player walking past a lever somebody else armed.</summary>
    private void RequestCancelIfMine()
    {
        if (_warningByPeer == 0)
            return;
        int self = Multiplayer?.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
        if (self == 0)
            self = 1;
        if (_warningByPeer != self)
            return;
        if (_isServer)
            ServerCancel(self);
        else
            RpcId(1, MethodName.RequestCancel);
    }

    /// <summary>
    /// <b>Server only.</b> Push the sign's state out if — and only if — it changed. Called after
    /// every press and cancel and once per server frame, so a lapsed window reaches the six signs
    /// on the same terms a press does and there is exactly one place that decides what the plate
    /// says.
    /// </summary>
    private void SyncWarning()
    {
        int want = _decision.WarningPeer(_clock);
        if (want == _warningByPeer)
            return;
        BroadcastWarning(want, want == 0 ? 0 : CountAtRisk());
    }

    /// <summary>How many bubbles a confirm would erase right now. Read off the counter rather
    /// than remembered, because the tally moves while somebody stands at the lever.</summary>
    private int CountAtRisk()
    {
        _counter ??= BubbleCounter.Instance;
        return _counter != null && GodotObject.IsInstanceValid(_counter) ? _counter.Count : 0;
    }

    private void BroadcastWarning(int byPeer, int count)
    {
        if (Multiplayer?.MultiplayerPeer == null)
            ShowWarning(byPeer, count);   // offline launch: no peer to send to, same effect.
        else
            Rpc(MethodName.ShowWarning, byPeer, count);
    }

    /// <summary>
    /// Server -&gt; every peer, presser included (<c>CallLocal</c>): the sign is now warning about
    /// <paramref name="byPeer"/>, or quiet when it is 0.
    ///
    /// <para><b>Broadcast, not sent to the presser alone</b>, per the class doc: the whole point
    /// of a warning on a shared board is that the other five can see it coming. Reliable, because
    /// a dropped "quiet again" would leave a lever permanently claiming a wipe was pending.</para>
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowWarning(int byPeer, int count)
    {
        _warningByPeer = byPeer;
        _warningCount = count;
        ApplySigns(count);
        ApplyHighlight();
        // The pop-up escalates and de-escalates off the same broadcast the sign does — including
        // the lapse, which arrives as byPeer 0 and has no press behind it. One message, one
        // reconcile, so a plate and a sign in front of the same player cannot disagree.
        RefreshPopup();
        // Printed on EVERY peer, and it is a control rather than a trace. The first headed
        // capture of this feature photographed an idle sign while the server's own log said
        // "armed" — the server had armed before the bot connected and the window had lapsed by
        // the time the shutter fired. Only an eye on the PNG caught it. A line the receiving
        // peer prints makes "the client actually showed the warning" machine-checkable, which
        // is what the capture harness now asserts.
        GD.Print(byPeer == 0
            ? "[bubbletest] lever sign: quiet"
            : $"[bubbletest] lever sign: WARNING byPeer={byPeer} count={count}");
        if (byPeer == 0)
            return;
        // INTERACTION-BIBLE §2 — feedback at the exact moment, and §8.2's redundant channel: the
        // text and the colour are the two visual channels, this is the third. Positional and
        // quiet: an acknowledgement, not a stinger (see the /direct entry).
        SfxLab.PlayStream3D(this, GlobalPosition, SfxLab.Get(Sfx.Chirp), volumeDb: -10f,
            pitchJitter: 0.03f, maxDistance: 20f);
    }

    /// <summary>Find the authored sign plates once. A lever with none still works and says so —
    /// the same failure mode, and the same warning, the missing Handle already gets.</summary>
    private void CollectSigns()
    {
        _signs.Clear();
        Node? parent = GetNodeOrNull(SignsNodeName);
        if (parent == null)
        {
            GD.PushWarning($"[bubbletest] reset lever has no {SignsNodeName} node — it will reset "
                           + "the board with nothing on it saying so, which is the defect LEVER-1 "
                           + "exists to close.");
            return;
        }
        foreach (Node child in parent.GetChildren())
        {
            if (child is Label3D label)
                _signs.Add(label);
        }
        ApplySigns(0);
    }

    /// <summary>Write the four plates. One loop, so the faces cannot disagree about what the
    /// lever is about to do.</summary>
    private void ApplySigns(int countAtRisk)
    {
        bool armed = _warningByPeer != 0;
        string text = armed
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, SignArmedFormat,
                countAtRisk)
            : SignIdleText;
        Color colour = armed ? SignArmedColour : SignIdleColour;
        foreach (Label3D label in _signs)
        {
            if (!GodotObject.IsInstanceValid(label))
                continue;
            label.Text = text;
            label.Modulate = colour;
        }
    }

    /// <summary>Server -&gt; the presser only. The world did not change, so nothing about this
    /// travels to anyone else.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RefusedFeedback()
    {
        if (_handle == null)
            return;
        SfxLab.PlayStream3D(this, GlobalPosition, SfxLab.Get(Sfx.Bump), volumeDb: -14f,
            pitchJitter: 0.05f, maxDistance: 14f);
        Tween tween = _handle.CreateTween();
        float rest = _handle.Rotation.X;
        tween.TweenProperty(_handle, "rotation:x", rest - SwingRad * 0.12f, 0.06);
        tween.TweenProperty(_handle, "rotation:x", rest, 0.10);
    }

    /// <summary>The reset broadcast, on every peer — see the class doc for why the swing rides
    /// this rather than the local press. <c>id == -1 &amp;&amp; count == 0</c> is BT-6's reset
    /// signature; a late-join sync also arrives with id −1 but carries the server's real count,
    /// so a joiner walking into an empty level does not see a lever swing at nobody.</summary>
    private void OnCounterChanged(int id, int count, int byPeer)
    {
        // The idle pop-up quotes the LIVE tally, and a player standing at the lever while a
        // teammate pops one has to see the number move — a warning naming a stale count is a
        // warning the player can catch out, and then stops reading.
        RefreshPopup();
        if (id >= 0 || count != 0)
            return;
        Swing();
    }

    /// <summary>
    /// <b>LEVER-2 — the screen-space plate, rewritten from whatever is true right now.</b> Called
    /// from every place that can change what it should say: the proximity flag, the warning
    /// broadcast (arm, cancel and lapse alike) and the tally. One writer, so there is no path on
    /// which the plate is right and another path forgets it.
    ///
    /// <para><b>Three wordings, not two.</b> Whether the standing warning is the reader's own or a
    /// teammate's changes what the reader can DO about it — walking away cancels your own arm and
    /// nobody else's (see <see cref="RequestCancelIfMine"/>) — so a single armed string would have
    /// to be either wrong or useless for one of the two cases.</para>
    /// </summary>
    private void RefreshPopup()
    {
        if (_popup == null || !GodotObject.IsInstanceValid(_popup))
            return;
        if (!_highlighted)
        {
            _popup.Clear();
            LogPopupState("none", 0);
            return;
        }

        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        // Resolved per refresh rather than cached: the plate is rebuilt on every state change
        // anyway, and a cached label would be the one thing on the screen that still named the old
        // key after a rebind.
        string key = InteractKeyLabel();
        if (_warningByPeer == 0)
        {
            int live = CountAtRisk();
            _popup.Set(PopupIdleHeading,
                live == 0 ? PopupIdleBodyEmpty : string.Format(inv, PopupIdleBodyFormat, live),
                string.Format(inv, PopupIdleFootFormat, key), urgent: false);
            LogPopupState("near", live);
            return;
        }

        int self = Multiplayer?.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
        if (self == 0)
            self = 1;
        if (_warningByPeer == self)
        {
            _popup.Set(
                _warningCount == 0
                    ? PopupArmedEmptyHeading
                    : string.Format(inv, PopupArmedSelfHeadingFormat, _warningCount),
                PopupArmedSelfBody, string.Format(inv, PopupArmedSelfFootFormat, key), urgent: true);
            LogPopupState("armed-self", _warningCount);
            return;
        }
        _popup.Set(
            _warningCount == 0
                ? PopupArmedEmptyHeading
                : string.Format(inv, PopupArmedOtherHeadingFormat, _warningCount),
            PopupArmedOtherBody, string.Format(inv, PopupArmedOtherFootFormat, key), urgent: true);
        LogPopupState("armed-other", _warningCount);
    }

    /// <summary>Whatever key the InputMap actually binds to <c>interact</c> right now.
    ///
    /// <para><b>Talon's note says "press E"; this prints what is really bound.</b> E is the
    /// shipped binding and is what a player will see — but a hardcoded letter is a bug that has
    /// not happened yet, on a plate whose entire job is to be believed. It also resolves through
    /// the active keyboard layout, so an AZERTY player is told their own key. Routed through
    /// <see cref="ControlGlyphs.BindingFor"/> — the repo's one binding-to-label resolver, which
    /// HOW TO PLAY and the world-space interact chip already read — rather than a fourth private
    /// copy of the same loop.</para></summary>
    private static string InteractKeyLabel()
    {
        const string Action = "interact";
        if (!InputMap.HasAction(Action))
            return "E"; // headless/self-test trees that never loaded the input map
        ControlGlyphs.Binding binding = ControlGlyphs.BindingFor(Action);
        return binding.Kind == ControlGlyphs.GlyphKind.Unbound ? "E" : binding.Label;
    }

    /// <summary>
    /// The pop-up's own witness line, printed by the peer that DREW it.
    ///
    /// <para>LEVER-1 learned this the expensive way: its first headed capture photographed an idle
    /// sign while the server's log said "armed", because the server had armed before the bot
    /// connected. A line the server prints is evidence about the server. So the capture harness
    /// gates on this one, which is emitted where the pixels are.</para>
    /// </summary>
    private void LogPopupState(string state, int count)
    {
        if (_popupState == state)
            return;
        _popupState = state;
        GD.Print($"[bubbletest] reset popup: {state} count={count}");
    }

    private void Swing()
    {
        if (_handle == null)
            return;
        SfxLab.PlayStream3D(this, GlobalPosition, SfxLab.Get(Sfx.Thunk), volumeDb: -6f,
            pitchJitter: 0.08f, maxDistance: 30f);
        float rest = 0f;
        Tween tween = _handle.CreateTween();
        tween.TweenProperty(_handle, "rotation:x", rest + SwingRad, SwingOutSec)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_handle, "rotation:x", rest, SwingBackSec)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>The shimmer half of "it reads as interactable before you touch it". Emission
    /// rather than a colour swap, so it also reads at night, when this hub's only other light is
    /// the pedestal beside it.
    ///
    /// <para><b>An armed lever takes the warning colour whether or not anyone is standing at
    /// it</b>, and it is the channel that carries furthest: MECHANICS-BIBLE §2.5 asks that a
    /// state a player can sit in for more than a moment be distinguishable from the other one
    /// inside a second and without reading text, and four seconds of red handle across a dark hub
    /// is that, at ranges where the sign's own letters are already too small.</para></summary>
    private void ApplyHighlight()
    {
        if (_handleMaterial == null)
            return;
        bool armed = _warningByPeer != 0;
        _handleMaterial.EmissionEnabled = true;
        _handleMaterial.Emission = armed ? SignArmedColour : HighlightColour;
        // COLOUR carries the armed state, not extra energy. An armed lever at 2.2 was measured in
        // a headed day capture as a blown orange streak in the glow post chain — the exact
        // complaint Talon filed against the bubble board on the same day (playtest note 1), and
        // it would have been this packet adding a second one to the same hub. Warm-to-red at the
        // energy the highlight already uses reads at a glance and blooms no harder than the
        // handle already did.
        _handleMaterial.EmissionEnergyMultiplier = armed || _highlighted ? 1.6f : 0.25f;
    }

    /// <summary>Warm, and deliberately not the pedestal's cool glyph colour: the two lit things
    /// in the hub have different jobs and should not read as one fixture.</summary>
    private static readonly Color HighlightColour = new(1.0f, 0.78f, 0.42f);
}
