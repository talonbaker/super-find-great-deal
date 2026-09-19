using Godot;
using MpFoundation.Game.Sandbox;

namespace Sail.Game.World.PuffinLab;

/// <remarks>PORTED from the sibling repo <c>mp-foundation</c> @ <c>8d07fed</c>
/// (<c>scripts/game/world/TinyDoor.cs</c>) by packet EGG-1, 2026-09-02. Three diffs against the
/// source, all forced by the move from a one-player offline playtest into a 2-6 peer session:
///
/// <list type="number">
/// <item><b><see cref="StartOpen"/>, and the ported scene sets it.</b> In mp-foundation this door
/// was horror-gate #2 and the only way into the black room, opened by
/// <c>EscapeHost.TryInteract</c> offline and by <c>Gameplay.RequestEscapeDoorOpen</c> — a
/// server-validated RPC — when networked. <see cref="Open"/> as written is a purely LOCAL state
/// change: a client that called it would open the door for itself and nobody else, and the blocker
/// would still be solid on the server, so that player would be walking into a wall only the server
/// can see. Making it authoritative means a new networked interactable, which is well past a port
/// and would land in files this packet does not own. So the door stands open from the first
/// frame — deterministic, identical on every peer, no RPC — and it can never be the reason the
/// return TV behind it is unreachable. Talon's brief names "puffin-sized tiny doors" as something
/// the room CONTAINS, not as a lock; whether the throwback should gate on it again is a direction
/// call, filed as an open question rather than guessed at here.</item>
/// <item><b>Materials come from <see cref="PuffinLabMaterials"/></b> — the two-material trim of
/// mp-foundation's <c>HorrorDetail</c>; that file records why the other twelve did not come.</item>
/// <item><b>The latch clack goes through <c>SfxLab.PlayStream3D</c> + <c>SfxLab.Get</c></b>, because
/// Sail has no <c>SfxLab.Play3D(node, pos, Sfx, ...)</c> overload. This is the same one-line
/// adaptation Sail's own <c>TvPortal</c> port made, for the same reason.</item>
/// </list></remarks>
/// <summary>
/// Horror-gate #2: the tiny, Puffling-sized door in the hallway's right-hand wall — the
/// only real way out, horrifying precisely because it is small, wrong, and rendered in
/// extreme detail against a plain world. Real wood grain via noise normal map, panel
/// relief modelled in actual geometry, and a scuffed shiny brass knob whose glint pulls
/// the eye right. It is placed to be FOUND by a player who looks (never force-shown).
///
/// Interact (the same MoveIntent.Interact edge as carrying) swings it open into the dark
/// beyond and drops its blocker; it never closes again. The knob shimmers softly when
/// the player is in reach — the exact affordance grammar of the carryable highlight.
/// Local axes: the door plane runs along X (hinge on -X), -Z faces the hallway, +Z is
/// the room beyond; the node sits at the floor centre of the opening.
///
/// <see cref="AuthoredVisual"/>: when true, an authored child model (an imported real
/// door GLB) provides the frame, slab, and panel-relief visuals — this script skips
/// building its own. The hinge is still a Node3D named "Leaf": if the authored scene
/// already has a child by that name, TinyDoor reuses it (and forces its hinge position
/// so the math stays correct) instead of building one — nest the door-leaf model inside
/// that authored "Leaf" node so <see cref="Open"/>'s swing tween animates the model,
/// not just an invisible hinge. The brass knob still builds by default even with an
/// authored model (<see cref="BuildKnob"/>) since it's the shimmer-material carrier and
/// tiny enough to sit on any door; <see cref="KnobPosition"/> (local to Leaf) can be
/// nudged in the editor to line it up with the model's handle.
/// </summary>
public partial class TinyDoor : StaticBody3D
{
    public const float OpeningW = 0.85f;
    public const float OpeningH = 1.05f;

    /// <summary>When true, an authored child node supplies the door's visuals; this
    /// script builds only the functional pieces (hinge, blocker, knob shimmer).</summary>
    [Export] public bool AuthoredVisual { get; set; }

    /// <summary>Keep building the small procedural brass knob even when
    /// <see cref="AuthoredVisual"/> is true — it carries the interact shimmer. Set false
    /// if the authored model already includes its own knob.</summary>
    [Export] public bool BuildKnob { get; set; } = true;

    /// <summary>Knob mesh anchor, local to the "Leaf" hinge node. Nudge in the editor to
    /// align the procedural knob with an authored door model's handle.</summary>
    [Export] public Vector3 KnobPosition { get; set; } = new(LeafW - 0.10f, KnobY, -0.03f);

    /// <summary>EGG-1. Stand open from the first frame: the leaf sits at the swung-open angle,
    /// the blocker is never solid, and no tween or latch sound runs. See this class's remarks for
    /// why the Puffin Lab port takes this path instead of the source's interact gate — the short
    /// version is that <see cref="Open"/> is client-local and would desync a six-peer session, and
    /// a door that opens on one machine only is worse than a door that was never locked.</summary>
    [Export] public bool StartOpen { get; set; }

    public bool IsOpen { get; private set; }

    /// <summary>Set by the host's proximity poll; drives the knob's interact shimmer.</summary>
    public bool Highlighted { get; set; }

    /// <summary>How close the avatar must be to the knob for Interact to open it.</summary>
    public float InteractRange => 1.7f;

    public Vector3 KnobGlobalPosition => ToGlobal(new Vector3(HalfW - 0.12f, KnobY, 0));

    private const float HalfW = OpeningW / 2f;
    private const float LeafW = OpeningW - 0.04f;
    private const float LeafH = OpeningH - 0.03f;
    private const float KnobY = 0.52f;

    private Node3D _leaf = null!;
    private CollisionShape3D _blocker = null!;
    private StandardMaterial3D? _knobMat;
    private double _pulseTime;

    public override void _Ready()
    {
        if (!AuthoredVisual)
        {
            // Frame: proud of the wall on the hallway side, so the door reads as an
            // object with depth, not a decal. Slightly darker wood than the leaf.
            var frameMat = (StandardMaterial3D)PuffinLabMaterials.WoodMat.Duplicate();
            frameMat.AlbedoColor = new Color(0.20f, 0.13f, 0.08f);
            AddBox("FrameL", new Vector3(0.07f, OpeningH + 0.07f, 0.16f), new Vector3(-HalfW - 0.035f, (OpeningH + 0.07f) / 2f, 0), frameMat);
            AddBox("FrameR", new Vector3(0.07f, OpeningH + 0.07f, 0.16f), new Vector3(HalfW + 0.035f, (OpeningH + 0.07f) / 2f, 0), frameMat);
            AddBox("FrameT", new Vector3(OpeningW + 0.14f, 0.07f, 0.16f), new Vector3(0, OpeningH + 0.035f, 0), frameMat);
            AddBox("Sill", new Vector3(OpeningW + 0.14f, 0.025f, 0.20f), new Vector3(0, 0.0125f, 0), frameMat);
        }

        // The leaf hangs from a hinge node at the -X jamb so Open() is one rotation. An
        // authored scene may already have a "Leaf" child (with the door model nested
        // inside it) — reuse that node rather than building a second one, but always
        // force its hinge position so Open()'s rotation math is correct regardless of
        // what the scene author set in the editor.
        _leaf = GetNodeOrNull<Node3D>("Leaf") ?? new Node3D { Name = "Leaf" };
        _leaf.Position = new Vector3(-HalfW, 0, 0);
        if (_leaf.GetParent() == null)
            AddChild(_leaf);

        if (!AuthoredVisual)
        {
            LeafBox("Slab", new Vector3(LeafW, LeafH, 0.055f), new Vector3(HalfW, LeafH / 2f + 0.02f, 0), PuffinLabMaterials.WoodMat);
            // Panel relief: raised border strips shaping two recessed panels — real depth
            // the normal map alone can't fake at this distance.
            BuildPanelRelief(yCenter: 0.78f, panelH: 0.30f);
            BuildPanelRelief(yCenter: 0.33f, panelH: 0.38f);
        }

        if (BuildKnob)
        {
            // The knob: rosette, shank, brass ball. Its material is a per-door clone of
            // the shared brass so the interact shimmer can animate without touching the
            // palette. Kept even in AuthoredVisual mode by default — it's the shimmer
            // carrier and small enough to sit on top of any door model.
            _knobMat = (StandardMaterial3D)PuffinLabMaterials.BrassMat.Duplicate();
            _knobMat.EmissionEnabled = true;
            _knobMat.Emission = new Color(1.0f, 0.85f, 0.5f);
            _knobMat.EmissionEnergyMultiplier = 0f;
            Vector3 knobBase = KnobPosition;
            _leaf.AddChild(new MeshInstance3D
            {
                Name = "KnobRosette",
                Mesh = new CylinderMesh { TopRadius = 0.030f, BottomRadius = 0.030f, Height = 0.012f, RadialSegments = 12 },
                MaterialOverride = _knobMat,
                Position = knobBase,
                RotationDegrees = new Vector3(90, 0, 0),
            });
            _leaf.AddChild(new MeshInstance3D
            {
                Name = "KnobShank",
                Mesh = new CylinderMesh { TopRadius = 0.010f, BottomRadius = 0.010f, Height = 0.030f, RadialSegments = 8 },
                MaterialOverride = _knobMat,
                Position = knobBase + new Vector3(0, 0, -0.018f),
                RotationDegrees = new Vector3(90, 0, 0),
            });
            _leaf.AddChild(new MeshInstance3D
            {
                Name = "Knob",
                Mesh = new SphereMesh { Radius = 0.032f, Height = 0.064f },
                MaterialOverride = _knobMat,
                Position = knobBase + new Vector3(0, 0, -0.045f),
            });
        }

        // One blocker seals the opening until Interact; the wall around it is solid.
        _blocker = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(OpeningW + 0.10f, OpeningH + 0.10f, 0.28f) },
            Position = new Vector3(0, OpeningH / 2f, 0),
            Disabled = StartOpen,
        };
        AddChild(_blocker);

        // EGG-1: settle into the open pose with no tween and no sound. Set directly rather than
        // through Open() so nothing animates or clacks on level load, and so the leaf's angle is
        // identical on every peer on frame one instead of converging over 1.3 s from whenever each
        // machine happened to enter the tree.
        if (StartOpen)
        {
            IsOpen = true;
            _leaf.Rotation = new Vector3(_leaf.Rotation.X, Mathf.DegToRad(-108f), _leaf.Rotation.Z);
        }
    }

    /// <summary>Swings the leaf inward (away from the hallway) and unseals the opening.
    /// One-way: the way out never closes behind you until you take it.</summary>
    public void Open()
    {
        if (IsOpen)
            return;
        IsOpen = true;
        Highlighted = false;
        _blocker.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        Tween tween = CreateTween();
        tween.TweenProperty(_leaf, "rotation:y", Mathf.DegToRad(-108f), 1.3f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        // A quiet latch clack — the existing bump voice, soft. No sting. Sail has no
        // SfxLab.Play3D(node, pos, Sfx, ...) overload, so the stream is fetched and played
        // through PlayStream3D — the identical adaptation Sail's TvPortal port already makes.
        SfxLab.PlayStream3D(GetParent(), KnobGlobalPosition, SfxLab.Get(Sfx.Bump),
            volumeDb: -18f, pitchJitter: 0.06f);
    }

    public override void _Process(double delta)
    {
        // Same shimmer grammar as Carryable.Highlighted: a soft pulse when in reach.
        // Null when BuildKnob is false (an authored model supplies its own knob).
        if (_knobMat == null)
            return;
        _pulseTime += delta;
        float target = Highlighted && !IsOpen ? 0.25f + 0.15f * Mathf.Sin((float)_pulseTime * 6f) : 0f;
        _knobMat.EmissionEnergyMultiplier =
            Mathf.Lerp(_knobMat.EmissionEnergyMultiplier, target, 10f * (float)delta);
    }

    private void BuildPanelRelief(float yCenter, float panelH)
    {
        // Border strips standing proud of the slab on the hallway face (-Z): the panel
        // between them reads recessed. Both faces get the strips so the leaf has a back.
        const float stripT = 0.030f, stripD = 0.016f;
        float panelW = LeafW - 0.16f;
        foreach (float zSign in new[] { -1f, 1f })
        {
            float z = zSign * (0.055f / 2f + stripD / 2f - 0.002f);
            LeafBox("PanelT", new Vector3(panelW, stripT, stripD), new Vector3(HalfW, yCenter + panelH / 2f, z), PuffinLabMaterials.WoodMat);
            LeafBox("PanelB", new Vector3(panelW, stripT, stripD), new Vector3(HalfW, yCenter - panelH / 2f, z), PuffinLabMaterials.WoodMat);
            LeafBox("PanelL", new Vector3(stripT, panelH + stripT, stripD), new Vector3(HalfW - panelW / 2f, yCenter, z), PuffinLabMaterials.WoodMat);
            LeafBox("PanelR", new Vector3(stripT, panelH + stripT, stripD), new Vector3(HalfW + panelW / 2f, yCenter, z), PuffinLabMaterials.WoodMat);
        }
    }

    private void AddBox(string name, Vector3 size, Vector3 pos, Material mat) =>
        AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = mat,
            Position = pos,
        });

    private void LeafBox(string name, Vector3 size, Vector3 pos, Material mat) =>
        _leaf.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = mat,
            Position = pos,
        });
}
