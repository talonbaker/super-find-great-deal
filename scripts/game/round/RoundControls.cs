using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The three buttons, the rack and the drop-off bin, joined to the round.</b> One node under
/// <c>Gameplay</c>, present on every peer with a fixed name so its RPCs route, exactly like
/// <c>PropManager</c> and <c>HideSeekDriver</c> beside it.
///
/// <para><b>What it is for.</b> A <c>RoundButton</c> in a room scene cannot own an RPC funnel
/// (three of them would be three funnels), cannot adjudicate (the round does that), and must not
/// know which prop is the target (the rack does). So the scene nodes are presentation and
/// sensors, and this is the one server-side owner: it takes the press, gates it, latches the
/// fact, reads the loop's answer one instant later, and broadcasts it.</para>
///
/// <para><b>The whole press path, in order.</b></para>
/// <list type="number">
/// <item>client: <c>RoundButton.Press</c> → <see cref="ClientRequestPress"/> →
/// <c>RpcId(1, RequestPress)</c>. The client decides nothing.</item>
/// <item>server: <see cref="RequestPress"/> re-checks reach against AUTHORITATIVE positions and
/// asks <see cref="RoundButtonRules.Gate"/> whether this is even a question for the round. A no
/// is answered immediately, with a reason.</item>
/// <item>server: a yes latches the fact on <see cref="RoundControlFacts"/>.</item>
/// <item>server, same tick or the next: <c>HideSeekDriver</c> folds the facts, steps the loop,
/// and calls <see cref="IRoundFactSource.AfterStep"/> — which is where
/// <see cref="OnAdjudicate"/> reads whether the phase moved or a refusal was named.</item>
/// <item>server → everyone (<c>CallLocal</c>): <see cref="ApplyPressResult"/>. Every peer
/// animates the cap; the presser hears it, feels it and reads why.</item>
/// </list>
///
/// <para><b>It rides <see cref="NetProfile.RoundChannel"/> rather than claiming a new one.</b>
/// These messages ARE round state — three per round, reliable, ordered — and ordering them
/// behind the phase broadcast they cause is the behaviour you want: the cap goes down, then the
/// strip changes. The channel ladder's argument for separate channels is about UNRELATED streams
/// head-of-line-blocking each other, and two lanes of the same system are not that. It also
/// leaves 18/19/20/22 free for DOOR-1 and VOICE-1, who are picking numbers in parallel with this
/// branch and cannot see it.</para>
/// </summary>
public partial class RoundControls : Node
{
    /// <summary>Fixed, because an RPC routes by node path and every peer must have the same one.</summary>
    public const string NodeName = "RoundControls";

    /// <summary>The prefix every line this lane prints carries, so a suite can grep for it.</summary>
    public const string LogPrefix = "[buttons]";

    /// <summary>How much further than the client's own reach the server will accept a press.
    /// The same number and the same argument as <c>PropManager.GrabRangeTolerance</c>: the client
    /// tests reach against its PREDICTED body and the server against the authoritative one, and
    /// those differ by up to a round trip of movement. Reusing the constant rather than writing a
    /// second one is the point — two independently authored reaches is the divergence
    /// INTERACTION-BIBLE §4 names.</summary>
    public const float PressReachToleranceM = PropManager.GrabRangeTolerance;

    /// <summary>Single instance per running game, same convention as
    /// <c>HideSeekDriver.Instance</c>.</summary>
    public static RoundControls? Instance { get; private set; }

    private bool _isServer;
    private PropManager? _props;
    private Node3D? _players;
    private HideSeekDriver? _driver;
    private ReachabilityFactSource? _reach;

    private readonly RoundControlFacts _facts = new();
    private bool _targetPinned;
    private readonly List<RoundButton> _buttons = new();
    private ObjectRack? _rack;
    private DropOffBin? _bin;

    /// <summary>The prop the round is hiding this round, or -1. OWNED HERE, on the server, and
    /// mirrored onto <c>ReachabilityFactSource</c> (which needs it to aim its physics audit) and
    /// into <c>DropOffBin</c> (which needs it to tell a delivery from a decoy). One owner, two
    /// readers — the alternative is three systems each deciding what "the target" means.</summary>
    public int TargetPropId { get; private set; } = -1;

    /// <summary>Counts every press the SERVER answered, accepted or refused. A suite reads it to
    /// tell "the press never arrived" from "the press arrived and was refused" — which look
    /// identical from the outside and have completely different fixes (the lesson HONK-1 paid
    /// for: assert the ATTEMPT count before believing any absence).</summary>
    public int PressesAnswered { get; private set; }

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Wired by <c>Gameplay</c> on every peer, after the world node is in the tree and after
    /// <c>PropManager.AdoptAuthoredProps</c> has run — the rack's three objects have to already
    /// carry their ids by the time anything asks the rack what they are.
    /// </summary>
    /// <param name="isServer">Only the server gates, latches, adjudicates and broadcasts.</param>
    /// <param name="props">The prop manager, for "who is holding what" (replicated, so this is
    /// answerable on every peer — which is what lets the lamp be derived rather than pushed).</param>
    /// <param name="players">The avatars root, for the server's authoritative reach re-check.</param>
    /// <param name="driver">The round. Registered with on the server only.</param>
    /// <param name="worldNode">Where to look for the buttons, the rack and the bin. A world with
    /// none of them (every CI slab world) leaves this node inert rather than special-cased.</param>
    /// <param name="devTargetPropId">REACH-1's <c>--reach-target</c>, or −1. When a dev flag
    /// names the target, it OWNS it for the whole session and the rack never touches it — see
    /// <see cref="ServerTrackTarget"/>.</param>
    public void Setup(bool isServer, PropManager props, Node3D players, HideSeekDriver driver,
        Node worldNode, int devTargetPropId = -1)
    {
        _isServer = isServer;
        _props = props;
        _players = players;
        _driver = driver;

        _buttons.Clear();
        Collect(worldNode);

        foreach (RoundButton b in _buttons)
            // THE FACING IS PRINTED, not assumed. A .tscn's twelve Transform3D floats are basis
            // ROWS (.claude/rules/godot-scenes.md), so a rotation written column-wise comes out
            // inverted and only a render shows it — except that a button whose +Z points INTO
            // the wall presses its cap outwards, which is a defect with a silent symptom. This
            // line is what makes a headless run able to say the three buttons face into their
            // rooms.
            GD.Print($"{LogPrefix} found {b.Kind} button at {b.GlobalPosition} "
                     + $"facing {b.GlobalTransform.Basis.Z} (reach {RoundButton.PressRadius:0.0} m)");
        if (_rack != null)
            GD.Print($"{LogPrefix} rack holds prop ids [{string.Join(", ", _rack.PropIds)}]");
        if (_bin != null)
            GD.Print($"{LogPrefix} drop-off bin at {_bin.GlobalPosition}");

        if (!isServer)
            return;

        // THE DEV FLAG WINS, FOR THE WHOLE SESSION. --reach-target names a prop in a world where
        // nobody is standing at the rack, so a rack that "tracked" the hider's empty hands would
        // quietly set the target back to none on the first tick and every suite built on that
        // flag would stop testing what it says it tests. Pinning also means the two owners can
        // never disagree: either the flag owns the target or the rack does.
        if (devTargetPropId >= 0)
        {
            _targetPinned = true;
            TargetPropId = devTargetPropId;
            GD.Print($"{LogPrefix} target prop PINNED to {devTargetPropId} by --reach-target; "
                     + "the rack will not change it this session");
        }

        _bin?.SetupServer(() => TargetPropId, () => _driver?.ServerState.Phase ?? HideSeekPhase.Holding,
            () => _driver?.ServerState.SeekerPeerId ?? 0);

        // The probes are delegates rather than cached booleans on purpose: they are evaluated at
        // the instant HideSeekLoop folds its facts, so this node's own tick order relative to the
        // driver's cannot make the round read a fact from the previous frame.
        _facts.RackHoldProbe = () => _rack != null && _rack.IsRackProp(HeldIdOf(HiderPeerId));
        _facts.TargetHoldProbe = () => TargetPropId >= 0 && HeldIdOf(HiderPeerId) == TargetPropId;
        _facts.BinProbe = () => _bin != null && _bin.TargetDelivered;
        _facts.Adjudicate = OnAdjudicate;
        _facts.Stepped = ServerTrackTarget;

        driver.Register(_facts);
        GD.Print($"{LogPrefix} registered with the round (server) — "
                 + $"{_buttons.Count} button(s), rack={_rack != null}, bin={_bin != null}");
    }

    /// <summary>REACH-1's fact source, handed over by <c>Gameplay</c> so the rack can aim it.
    /// Null on a client and in any world without one; setting it pushes the current target
    /// immediately so a mid-session wiring order cannot leave the two disagreeing.</summary>
    public ReachabilityFactSource? Reach
    {
        get => _reach;
        set
        {
            _reach = value;
            if (_reach != null)
                _reach.TargetPropId = TargetPropId;
        }
    }

    private void Collect(Node from)
    {
        foreach (Node child in from.GetChildren())
        {
            switch (child)
            {
                case RoundButton b: _buttons.Add(b); break;
                case ObjectRack r: _rack ??= r; break;
                case DropOffBin d: _bin ??= d; break;
            }
            Collect(child);
        }
    }

    private int HiderPeerId => _driver?.ServerState.HiderPeerId ?? 0;

    /// <summary>The id of the prop in a peer's hand, or -1. Reads the replicated held-by-peer
    /// view, so it is the same answer on the server and on every client.</summary>
    private int HeldIdOf(int peerId) =>
        peerId != 0 && _props?.FindHeldBy(peerId) is { } held ? held.PropId : -1;

    // ------------------------------------------------------------------------------------
    // The lamp's inputs, for every peer
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Everything a button needs to light its lamp, gathered from replicated state.</b> Static
    /// and null-safe so <c>RoundButton</c> never has to ask whether the session is wired yet —
    /// an unwired peer gets <c>Synced = false</c>, which the rule reads as Dark.
    ///
    /// <para><b>Every input here crosses the wire already.</b> The phase, the roles and the
    /// roster come from <c>HideSeekDriver.View</c> (one absolute message); who is holding what
    /// comes from <c>PropManager</c>'s replicated holder view; which props are the rack's comes
    /// from the rack node, which every peer instanced from the same scene with the same adopted
    /// ids. So no new field rides the wire for the affordance — which is the difference between
    /// deriving a lamp and pushing one.</para>
    /// </summary>
    public static RoundButtonRules.LampFacts LampFactsFor(int selfPeerId)
    {
        HideSeekDriver? driver = HideSeekDriver.Instance;
        if (driver is not { Synced: true })
            return new RoundButtonRules.LampFacts(false, HideSeekPhase.Holding, selfPeerId,
                0, 0, false, false, false);

        HideSeekView view = driver.View;
        RoundControls? self = Instance;
        int heldByHider = self?.HeldIdOf(view.HiderPeerId) ?? -1;
        bool rackProp = self?._rack != null && self._rack.IsRackProp(heldByHider);
        // On a CLIENT the target id is not replicated (it never needs to be — see the class doc
        // and the handoff). During Hiding the target IS the rack object in the hider's hands, so
        // "holding a rack prop" is the same predicate there, which is the only phase the Confirm
        // lamp reads it in.
        bool holdsTarget = self is { _isServer: true }
            ? self.TargetPropId >= 0 && heldByHider == self.TargetPropId
            : rackProp;

        return new RoundButtonRules.LampFacts(
            Synced: true,
            Phase: view.Phase,
            SelfPeerId: selfPeerId,
            HiderPeerId: view.HiderPeerId,
            SeekerPeerId: view.SeekerPeerId,
            // SOLO-1: "is this peer one the ROUND knows about at all". Every human on the wire
            // owns a score row from their first tick; the dedicated server, which builds the
            // world and therefore owns a copy of every button, does not -- and it is nobody
            // rather than a player who is waiting. See RoundButtonRules.InThisMatch.
            SelfIsOnTheRoster: selfPeerId != 0 && (view.Scores?.ContainsKey(selfPeerId) ?? false),
            HiderHoldsRackProp: rackProp,
            HiderHoldsTarget: holdsTarget);
    }

    // ------------------------------------------------------------------------------------
    // The press
    // ------------------------------------------------------------------------------------

    /// <summary>Client → server: "I pressed this button." The client's whole contribution.
    ///
    /// <para><b>And the hand moves</b> (HANDS-1, 2026-09-20). This is the ONE client-side point
    /// every press goes through — a human's click on a <c>RoundButton</c> and a suite's
    /// <c>--press</c> alike — so hooking the poke here is what makes it impossible to press a
    /// button without the hand reaching for it. <c>FirstPersonHands.Local</c> is null on every
    /// peer that built no first-person lens (a server, a headless bot), so this costs those
    /// nothing. The hand resolves WHICH button from the avatar's own aim-aware pick, and a press
    /// fired from across the room moves nothing, which is correct.</para></summary>
    public void ClientRequestPress(RoundButtonKind kind)
    {
        Sandbox.Hands.FirstPersonHands.Local?.Poke();
        RpcId(1, MethodName.RequestPress, (byte)kind);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.RoundChannel)]
    private void RequestPress(byte kindByte)
    {
        if (!_isServer || _driver is null)
            return;
        int peer = Multiplayer.GetRemoteSenderId();
        if (peer <= 0)
            return;   // 0 is a locally-invoked RPC; this server never has a local player.
        if (kindByte > (byte)RoundButtonKind.End)
        {
            GD.PushWarning($"{LogPrefix} peer {peer} asked for button kind {kindByte}, "
                           + "which does not exist — ignored.");
            return;
        }
        var kind = (RoundButtonKind)kindByte;
        HideSeekState s = _driver.ServerState;

        // 1. REACH, against authoritative positions. A refused press still gets an answer: this
        //    is a refusal, not a dropped packet, and "nothing happened" is the defect.
        if (!WithinReach(peer, kind))
        {
            Answer(kind, peer, accepted: false, PressRefusal.TooFarAway);
            return;
        }

        // 2. IS THIS A QUESTION THE ROUND CAN ANSWER? Phase and role only; everything the loop
        //    itself refuses on is forwarded so the loop's own sentence comes back.
        PressRefusal gate = RoundButtonRules.Gate(kind, s.Phase, peer, s.HiderPeerId, s.SeekerPeerId);
        if (gate != PressRefusal.None)
        {
            Answer(kind, peer, accepted: false, gate);
            return;
        }

        // 3. Latch. The loop answers on the next step, in OnAdjudicate below.
        _facts.Press(kind, peer, s.Phase);
        GD.Print($"{LogPrefix} press {kind} peer={peer} forwarded (phase {s.Phase})");
    }

    /// <summary>The server's own reach check. Missing avatar ⇒ out of reach rather than allowed:
    /// a press from a peer whose body the server cannot find is not a press it should act on.</summary>
    private bool WithinReach(int peerId, RoundButtonKind kind)
    {
        RoundButton? button = ButtonOf(kind);
        if (button is null)
            return false;
        Node3D? avatar = _players?.GetNodeOrNull<Node3D>(peerId.ToString());
        if (avatar is null)
            return false;
        float bar = RoundButton.PressRadius + PressReachToleranceM;
        return avatar.GlobalPosition.DistanceSquaredTo(button.GlobalPosition) <= bar * bar;
    }

    private RoundButton? ButtonOf(RoundButtonKind kind)
    {
        foreach (RoundButton b in _buttons)
            if (b.Kind == kind)
                return b;
        return null;
    }

    /// <summary>
    /// <b>The loop has just stepped; what did it do with the presses?</b> Called from
    /// <see cref="RoundControlFacts.AfterStep"/>, which <c>HideSeekDriver</c> calls in the same
    /// method and on the same tick as <c>HideSeekLoop.Step</c> — so <c>ServerState</c> here is
    /// the post-step state and the refusal (which lives for exactly one tick) is still on it.
    /// </summary>
    private void OnAdjudicate(IReadOnlyList<RoundControlFacts.PendingPress> pending)
    {
        HideSeekState s = _driver?.ServerState ?? default;
        foreach (RoundControlFacts.PendingPress p in pending)
        {
            (bool accepted, PressRefusal reason) =
                RoundButtonRules.ReadResult(p.PhaseAtPress, s.Phase, s.Refusal);
            if (!accepted && reason == PressRefusal.NotNow)
            {
                // ROUND-1's stated invariant is that no branch of the loop reads a press and then
                // neither acts nor names a refusal. If this ever fires, that invariant has broken
                // and the player is getting a vaguer sentence than they deserve — so it is loud.
                GD.PushWarning($"{LogPrefix} {p.Kind} from peer {p.PeerId} was forwarded in "
                               + $"{p.PhaseAtPress} and the loop neither moved nor refused "
                               + "(HideSeekLoop's no-silent-no-op invariant).");
            }
            Answer(p.Kind, p.PeerId, accepted, reason);
        }
    }

    private void Answer(RoundButtonKind kind, int peerId, bool accepted, PressRefusal reason)
    {
        PressesAnswered++;
        GD.Print($"{LogPrefix} press {kind} peer={peerId} "
                 + $"{(accepted ? "accepted" : "refused")} reason={reason} "
                 + $"text=\"{RoundButtonText.Sentence(reason, kind)}\"");
        Rpc(MethodName.ApplyPressResult, (byte)kind, peerId, accepted, (byte)reason);
    }

    /// <summary>Server → everyone, <c>CallLocal</c>. One code path for the presser, the other
    /// player and the server's own log, so nothing downstream can depend on which it is.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = NetProfile.RoundChannel, CallLocal = true)]
    private void ApplyPressResult(byte kindByte, int peerId, bool accepted, byte reasonByte)
    {
        if (kindByte > (byte)RoundButtonKind.End)
            return;
        ButtonOf((RoundButtonKind)kindByte)?.PlayPressResult(
            accepted, (PressRefusal)reasonByte, peerId == (int)Multiplayer.GetUniqueId());
    }

    // ------------------------------------------------------------------------------------
    // The target's identity
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Server: the target follows the hider's hands while the round is in Holding, and is
    /// frozen the moment the round leaves it.</b> Driven from the round's own tick (the driver
    /// calls <see cref="IRoundFactSource.AfterStep"/> every step) rather than from a
    /// <c>_PhysicsProcess</c> of this node's own, so there is one clock and no ordering to get
    /// wrong.
    ///
    /// <para><b>Set on the pickup, not on the transition</b> (REACH-1's handoff asks for exactly
    /// this): layer 3's audit fires when the target latches Resting, and a target id that only
    /// appeared at the Holding → Hiding edge would miss every settle the hider makes while
    /// choosing. Following the hands in Holding gives the same value at the edge — they are
    /// holding it when they press Start — and gives it earlier.</para>
    ///
    /// <para><b>−1 on the reset edge</b>, also REACH-1's ask: a fact about last round's crate is
    /// not a fact about this round's.</para>
    /// </summary>
    internal void ServerTrackTarget()
    {
        if (!_isServer || _driver is null)
            return;
        HideSeekState s = _driver.ServerState;

        if (s.ResetRequested)
        {
            _bin?.ClearForNewRound();
            _facts.ClearForNewRound();
        }

        int want = TargetPropId;
        if (_targetPinned)
            return;
        if (s.ResetRequested)
        {
            want = -1;
        }
        else if (s.Phase == HideSeekPhase.Holding)
        {
            int held = HeldIdOf(s.HiderPeerId);
            want = _rack != null && _rack.IsRackProp(held) ? held : -1;
        }

        if (want == TargetPropId)
            return;
        TargetPropId = want;
        if (_reach != null)
            _reach.TargetPropId = want;
        GD.Print($"{LogPrefix} target prop is now "
                 + $"{(want < 0 ? "none" : want.ToString())} (phase {s.Phase})");
    }
}
