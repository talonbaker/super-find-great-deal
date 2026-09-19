using Godot;
using MpFoundation;
using MpFoundation.Game.Sandbox;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>The secret bubble.</b> Talon's easter-egg addendum §3: <i>"A secret bubble, a different
/// colour from the normal collectibles, hidden somewhere clever. Popping it triggers something
/// special (exact effect TBD/open to the implementing agent)."</i>
///
/// <para><b>It is deliberately NOT a <see cref="Bubble"/>, and that is the whole of acceptance
/// criterion 5.</b> <c>BubbleCounter.AdoptAuthoredBubbles</c> collects by TYPE
/// (<c>child is Bubble</c>) and assigns ids by sorted node path, so a subclass — or an instance of
/// <c>Bubble.tscn</c> with a different material — would have been adopted as the 113th bubble,
/// moved every id after it, and turned <see cref="BubbleCounter.BubbleCount"/> into 113 against a
/// <see cref="World.BubbleTest.BubbleTestLayout.BubbleTarget"/> of 112. A secret that corrupts the
/// tally reads as a bug rather than as a secret. Sharing no base class means the count is
/// unchanged <i>by construction</i> rather than by a guard somebody has to remember; there is no
/// code path through which this node could be counted.</para>
///
/// <para><b>Server-adjudicated, exactly like <see cref="Bubble"/>.</b> Only the server subscribes
/// <see cref="Area3D.BodyEntered"/>; the pop is one reliable <c>CallLocal</c> RPC, so the server
/// is not a special case in the handler and two players touching it on the same tick is a
/// non-event rather than a race. Clients never flip their own bit.</para>
///
/// <para><b>The effect is a REVEAL, and it is visible to every peer.</b> For
/// <see cref="RevealSeconds"/> seconds after the pop, every bubble in the level that is still
/// un-popped glows hard — one property write on the single shared film material
/// (<c>BubbleCounter.AdoptSharedFilm</c>), so 112 bubbles cost the same as one.
///
/// <para><b>Why every peer and not just the popper</b>, since the packet requires that choice to
/// be made rather than defaulted: the television's flash is deliberately private (THRILL §12 —
/// one player vanishing must not become an announced event, because the watchers' half of that
/// beat is not being told). This is the opposite beat. It is a co-op level whose 112 collectibles
/// are hidden on purpose, and the thing worth having is not a private trinket but a moment where
/// one player's curiosity pays the whole group at once — the level lighting up for everybody, and
/// four people asking who did that. A private reveal would be a strictly worse version of walking
/// around looking.</para></para>
///
/// <para><b>It restores with the lever.</b> <c>BubbleResetLever</c> exists so a session can run
/// the level twice without a relaunch (program D9); a secret that could only be found once per
/// process would quietly make the second run a smaller level. It rides
/// <see cref="BubbleCounter.Changed"/>'s reset signal rather than adding a second RPC.</para>
/// </summary>
public partial class SecretBubble : Area3D
{
    /// <summary>The node name the world adds it under, so every peer resolves the same NodePath
    /// for the RPC below. One spelling, in one place.</summary>
    public const string NodeName = "SecretBubble";

    /// <summary>Scene-tree group, for runtime queries by the self-test and by captures.</summary>
    public const string Group = "secret_bubble";

    /// <summary>Visual radius, metres — deliberately larger than <see cref="Bubble.VisualRadiusM"/>
    /// (0.45). "A different colour from the normal collectibles" is the brief; a different SIZE as
    /// well is what makes it read as different at the distance you first see it from, through
    /// murky water, rather than only once you are on top of it.</summary>
    public const float VisualRadiusM = 0.62f;

    /// <summary>Collider radius. Larger than the visual would be a lie, smaller is the shipped
    /// discipline (<see cref="Bubble"/>'s class doc: the visible part is the promise, the collider
    /// is the part that has to be honestly reachable). Same 0.78 ratio the collectibles use.
    /// </summary>
    public const float ColliderRadiusM = 0.48f;

    /// <summary>How long the reveal lasts. Long enough to turn and look at a section you are not
    /// standing in, short enough that it is a moment rather than a mode.</summary>
    public const float RevealSeconds = 12f;

    /// <summary>How much emission the reveal adds on top of whatever the night curve is already
    /// writing. Additive rather than absolute, so the beat reads at noon (when the night glow is
    /// 0) and at midnight (when it is not) without a second curve to keep in step.</summary>
    public const float RevealBoostEnergy = 3.2f;

    /// <summary>How fast the hue travels, full turns per second. Slow: the point is that the
    /// colour is visibly WRONG for a collectible, not that it strobes.</summary>
    private const float HueTurnsPerSec = 0.22f;

    /// <summary>True on this peer once the broadcast said it is gone. Presentation state; the
    /// authority is the server's own <see cref="_popped"/>.</summary>
    public bool Popped { get; private set; }

    /// <summary>How many times it has been popped this session. Instrumentation, for the same
    /// reason <c>BubbleResetGate.Accepted</c> is counted: an event nobody observed is
    /// indistinguishable from an event that never happened.</summary>
    public int Pops { get; private set; }

    /// <summary>The mesh this bubble renders. Built here rather than authored in a section file,
    /// which is legal for exactly the reason the entrance televisions are:
    /// <c>BubbleTestSelfTest.CheckBake</c> walks the seven SECTION scenes and this node is a child
    /// of the world.</summary>
    public MeshInstance3D? Visual { get; private set; }

    private StandardMaterial3D? _mat;
    private bool _isServer;
    private bool _subscribed;
    private bool _popped;
    private double _time;

    public override void _Ready()
    {
        AddToGroup(Group);
        Monitoring = true;
        Monitorable = false;
        CollisionMask = 1;   // the project declares exactly one 3D layer; the type test is the mask.

        AddChild(new CollisionShape3D
        {
            Name = "Shape",
            Shape = new SphereShape3D { Radius = ColliderRadiusM },
        });

        _mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.95f, 1f, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = Colors.White,
            EmissionEnergyMultiplier = 2.4f,
            Roughness = 0.1f,
            Metallic = 0.2f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        Visual = new MeshInstance3D
        {
            Name = "Visual",
            Mesh = new SphereMesh { Radius = VisualRadiusM, Height = VisualRadiusM * 2f },
            MaterialOverride = _mat,
        };
        AddChild(Visual);

        TrySubscribe();
    }

    public override void _ExitTree()
    {
        if (_subscribed && BubbleCounter.Instance is { } counter)
            counter.Changed -= OnCounterChanged;
        _subscribed = false;
    }

    /// <summary>Attach to the counter's reset signal the moment there is a counter to attach to.
    /// Retried from <c>_Process</c> rather than assumed available at <c>_Ready</c>, because the
    /// two worlds that build this level do not agree on the order: the played level creates the
    /// counter before this node, and <c>--bubble-selftest</c> brings its own AFTER the world (see
    /// <c>BubbleTestWorld.SetUpBubbleCounter</c>'s <c>fixtureOwnsTheCounter</c> branch). One bool
    /// check a frame is cheaper than a rule nobody can see is being relied on.</summary>
    private void TrySubscribe()
    {
        if (_subscribed || BubbleCounter.Instance is not { } counter)
            return;
        counter.Changed += OnCounterChanged;
        _subscribed = true;
    }

    /// <summary>Server or client. Called by the world before the node is in the tree does not
    /// work — <c>_Ready</c> builds the visual — so this is called after <c>AddChild</c> and is the
    /// ONLY place the server-side trigger is wired. A client that listened would be one refactor
    /// away from acting on its own overlap, and the cheapest way to keep a rule is to leave no
    /// wire in place that could carry the violation (<see cref="Bubble.InitAuthored"/>'s own
    /// argument).</summary>
    public void Setup(bool isServer)
    {
        _isServer = isServer;
        if (!isServer)
            return;
        BodyEntered += OnServerBodyEntered;
    }

    private void OnServerBodyEntered(Node3D body)
    {
        if (_popped || !_isServer)
            return;
        if (body is not SandboxAvatar avatar)
            return;   // a prop or a creature touching it is not a finder.
        _popped = true;
        int peerId = int.TryParse(avatar.Name.ToString(), out int parsed) ? parsed : 0;
        Rpc(MethodName.ApplyPopped, peerId);
        ApplyPopped(peerId);   // CallLocal is off; the server applies its own, like BubbleCounter.
    }

    /// <summary>Every peer: the secret was found. Hiding AND un-monitoring, for
    /// <see cref="Bubble.ApplyPopped"/>'s reason — a hidden area that still reports overlaps keeps
    /// firing pop-shaped events forever.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ApplyPopped(int byPeer)
    {
        if (Popped)
            return;
        Popped = true;
        Pops++;
        Visible = false;
        Monitoring = false;
        SetProcess(false);
        // Loud, and on every peer: this is the one event in the level with no HUD line and no
        // counter tick, so the log is the only place a headless run can see it happened at all.
        GD.Print($"[bubbletest.secret] the secret bubble was popped by peer={byPeer} — revealing "
                 + $"every un-popped bubble for {RevealSeconds:F0} s (+{RevealBoostEnergy:F1} "
                 + "emission)");
        BubbleCounter.Instance?.BeginReveal(RevealSeconds, RevealBoostEnergy);
    }

    /// <summary>Every peer: the reset lever put everything back, so the secret is findable again.
    /// Idempotent.</summary>
    public void ApplyRestored()
    {
        _popped = false;
        Popped = false;
        Visible = true;
        Monitoring = true;
        SetProcess(true);
    }

    private void OnCounterChanged(int id, int count, int byPeer)
    {
        // BubbleCounter broadcasts (-1, 0, 0) for a reset and (id, count, peer) for a pop.
        if (id == -1)
            ApplyRestored();
    }

    public override void _Process(double delta)
    {
        TrySubscribe();
        if (Popped || _mat is null)
            return;
        _time += delta;
        // The one thing that makes it read as "not one of the 112": a colour no collectible has.
        // Hue only — value and saturation are held, so it never goes dark or turns white and
        // starts to look like the film material it is standing next to.
        var hue = (float)Mathf.PosMod(_time * HueTurnsPerSec, 1.0);
        Color c = Color.FromHsv(hue, 0.85f, 1.0f);
        _mat.Emission = c;
        _mat.AlbedoColor = new Color(c.R, c.G, c.B, 0.55f);
    }
}
