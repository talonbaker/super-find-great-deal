using Godot;
using MpFoundation.Game.Sandbox;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>One bubble.</b> An <see cref="Area3D"/> a level author drops anywhere: it bobs in place,
/// glows as night comes on, and pops when a player touches it — sound, a puff, and one tick on
/// the shared tally, on every peer, exactly once.
///
/// <para><b>Nothing here decides anything.</b> The pop is adjudicated on the server by the
/// server's own physics (program D6): only the server subscribes <see cref="Area3D.BodyEntered"/>,
/// and all it does is tell <see cref="BubbleCounter"/>. Clients never request a pop and never flip
/// their own bit — they hide this node when the broadcast tells them to. That is what makes
/// "two players touch the same bubble on the same tick" a non-event rather than a race, and it is
/// why acceptance criterion 5 (a client-forced pop changes nobody else's count) is a property of
/// the shape rather than of a guard somewhere.</para>
///
/// <para><b>The collider is deliberately smaller than the visual</b> (0.35 m against a 0.45 m
/// sphere, program §6.11). BT-11 hides bubbles by tucking them behind geometry; if the two were
/// the same size, a bubble whose visual just peeks past a corner would be pop-able through the
/// wall it was hidden behind. The visible part is the promise; the collider is the part that has
/// to be honestly reachable.</para>
///
/// <para><b>Ownership.</b> Placement is BT-11's, the film material is BT-9's, and the HUD and
/// reset lever are BT-8's. This class owns the entity and nothing else.</para>
/// </summary>
public partial class Bubble : Area3D
{
    /// <summary>Scene-tree group every bubble joins at ready. Used for runtime queries (the
    /// self-test's hidden-set readout); the id assignment itself walks by TYPE, because a group
    /// authored in a .tscn is a known silent-failure mode in this repo.</summary>
    public const string Group = "bubbles";

    /// <summary>Visual radius, metres — the sphere a player sees.</summary>
    public const float VisualRadiusM = 0.45f;

    /// <summary>Collider radius, metres. Smaller than <see cref="VisualRadiusM"/> on purpose —
    /// see the class doc.</summary>
    public const float ColliderRadiusM = 0.35f;

    /// <summary>
    /// This bubble's identity in the shared bitset. <b>−1 until <see cref="BubbleCounter"/>
    /// assigns one</b>, which is also the state a bubble is in when it is opened on its own in
    /// the editor or dropped into a world that has no counter — it just sits there, inert and
    /// un-poppable, rather than claiming id 0 and stealing another bubble's tick.
    ///
    /// <para>Exported so the assignment is visible in the inspector when debugging a level, NOT
    /// so an author sets it: ids are derived from authored order (see
    /// <see cref="BubbleCounter.AdoptAuthoredBubbles"/>) precisely so nobody has to keep a
    /// hundred hand-typed numbers unique across five section files.</para>
    /// </summary>
    [Export]
    public int Id { get; set; } = -1;

    /// <summary>True once this peer has been told the bubble is gone. Presentation state only —
    /// the authority is <see cref="BubbleCounter"/>'s bitset.</summary>
    public bool Popped { get; private set; }

    /// <summary>The mesh this bubble renders, if the scene has one. Public so
    /// <see cref="BubbleCounter"/> can hand every bubble the one shared film material rather
    /// than leaving a hundred duplicates in the scene.</summary>
    public MeshInstance3D? Visual { get; private set; }

    private Vector3 _authoredPosition;
    private BubbleCounter? _counter;
    private bool _serverBound;

    /// <summary>Where the level author put it, in the parent's space. Every idle offset is
    /// applied relative to this, never accumulated onto the live position — a bob that
    /// integrated would drift, and a drifting collider is acceptance criterion 6's failure
    /// mode.</summary>
    public Vector3 AuthoredPosition => _authoredPosition;

    /// <summary>How far the collider currently sits from where it was authored, metres. Read by
    /// the self-test (acceptance criterion 6); nothing gameplay-facing keys off it.</summary>
    public float OffsetFromAuthoredM => Position.DistanceTo(_authoredPosition);

    public override void _Ready()
    {
        _authoredPosition = Position;
        AddToGroup(Group);
        Visual = FindVisual(this);
        // Enforced here as well as authored in Bubble.tscn: a bubble instanced from code (the
        // BT-6 fixture) and a bubble instanced from a section scene must behave identically, and
        // "the .tscn said so" is not a guarantee a code-built instance inherits.
        Monitoring = true;
        Monitorable = false;
    }

    /// <summary>
    /// Server + client: adopt this bubble into the counter under <paramref name="id"/>. Called
    /// once, by <see cref="BubbleCounter.AdoptAuthoredBubbles"/>, on every peer, with the same
    /// id everywhere.
    ///
    /// <para>The <see cref="Area3D.BodyEntered"/> subscription happens HERE and only when
    /// <paramref name="isServer"/> — not in <c>_Ready</c>. A client that listened would be one
    /// refactor away from acting on its own overlap, and the cheapest way to keep a rule is to
    /// leave no wire in place that could carry the violation.</para>
    /// </summary>
    public void InitAuthored(int id, bool isServer, BubbleCounter counter)
    {
        Id = id;
        _counter = counter;
        if (!isServer || _serverBound)
            return;
        _serverBound = true;
        BodyEntered += OnServerBodyEntered;
    }

    private void OnServerBodyEntered(Node3D body)
    {
        if (Popped || _counter == null)
            return;
        // Layer discipline: this project has exactly ONE physics layer ("World",
        // project.godot [layer_names]), so no collision mask can single out avatars — see
        // BubbleCounter's class doc for the recorded decision. The type test is the mask.
        if (body is not SandboxAvatar avatar)
            return;
        // The avatar node's NAME is its peer id — the same lookup Gameplay's own AvatarResolver
        // uses (`_players.GetNodeOrNull<SandboxAvatar>(id.ToString())`), read in reverse.
        int peerId = int.TryParse(avatar.Name.ToString(), out int parsed) ? parsed : 0;
        _counter.ServerPop(Id, peerId);
    }

    /// <summary>Every peer: this bubble is gone. Hiding AND un-monitoring, because a hidden area
    /// that still reports overlaps would keep firing pop-shaped events at a counter that
    /// correctly ignores them — free to prevent, and exactly the kind of permanent no-op traffic
    /// that makes a later profile unreadable.</summary>
    public void ApplyPopped()
    {
        Popped = true;
        Visible = false;
        Monitoring = false;
        SetProcess(false);
    }

    /// <summary>Every peer: the reset lever put it back (program D9). Idempotent — a bubble that
    /// was never popped is unchanged.</summary>
    public void ApplyRestored()
    {
        Popped = false;
        Visible = true;
        Monitoring = true;
        Position = _authoredPosition;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (Popped || _counter == null)
            return;
        // One shared clock for every peer (see BubbleCounter.SharedTimeSec), so the server's
        // collider and every client's sphere are in the same place. The wander hook is added
        // before the clamp so a future Path3D drift inherits the 0.15 m bound rather than
        // quietly escaping it.
        double t = _counter.SharedTimeSec;
        Vector3 offset = BubbleOscillation.OffsetAt(Id, t) + BubbleOscillation.WanderOffsetAt(Id, t);
        float len = offset.Length();
        if (len > BubbleOscillation.MaxOffsetM)
            offset *= BubbleOscillation.MaxOffsetM / len;
        Position = _authoredPosition + offset;
    }

    private static MeshInstance3D? FindVisual(Node n)
    {
        foreach (Node child in n.GetChildren())
        {
            if (child is MeshInstance3D mesh)
                return mesh;
            MeshInstance3D? deeper = FindVisual(child);
            if (deeper != null)
                return deeper;
        }
        return null;
    }
}
