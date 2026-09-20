using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Sandbox.Feel;

/// <summary>
/// THE PROPS AND THE SLOTS, built in code onto whatever world node this is handed.
///
/// Ported from shader-lab's <c>scripts/feel/FeelStage.cs</c> (2026-09-05). The props, their
/// masses, their shapes and the two-zone arrangement are unchanged; what changed is the SPREAD,
/// and only the spread. See "the one thing that was retuned" below.
///
/// -----------------------------------------------------------------------------------------------
/// NOTHING HERE IS A THING. THEY ARE MASSES.
/// -----------------------------------------------------------------------------------------------
/// Boxes, a cylinder and two spheres, named after weights rather than after objects. An interaction
/// system judged on a prop that LOOKS like a lantern gets judged partly on how lanterns ought to
/// feel, and the tester cannot tell which half of their reaction came from the physics.
///
/// The spread is the point: 0.4 kg to 22 kg against a 10 kg reference, so the lightest item sits at
/// heft 0.04 and the heaviest is pinned at 1.0. Two items an octave apart in mass are hard to tell
/// apart in the hand; two items fifty times apart are not, and a lab should make an effect obvious
/// before it makes it subtle.
///
/// -----------------------------------------------------------------------------------------------
/// THE ONE THING THAT WAS RETUNED FOR THIS REPO, AND WHY ONLY THAT
/// -----------------------------------------------------------------------------------------------
/// The original stage was built inside a 6x7 m room and everything sat within about two metres of
/// the character, because that was the whole room. Here the ground is 64x64 m and the camera is the
/// game's own, which sits farther out. So the two ZONES moved apart -- tumble to the left of the
/// spawn facing, snap to the right, three to six metres out -- and the signs grew, because a sign
/// sized for a small room is unreadable at the distance this camera stands.
///
/// PROP AND SLOT SIZES DID NOT CHANGE, deliberately. A prop is a hand-scale quantity and a slot is
/// a hand-scale quantity; neither has anything to do with how big the room is. Scaling them with
/// the room would have quietly retuned the very thing under test -- heft is mass over
/// MassReference, and a bigger prop invites a bigger mass -- and the first thing anyone would have
/// noticed is that the carry "felt different in the game", for a reason that had nothing to do with
/// the game.
///
/// The two release modes are still side by side in one view, under TUMBLE and SNAP signs, and one
/// slot still starts occupied: occupied-slot feedback that requires the tester to construct the
/// situation first is feedback that does not get tested.
///
/// The snap props deliberately start on the GROUND a few metres short of the bench rather than on
/// it. Carrying something to where it goes is the action that mode exists for, and a stage that
/// starts them already in reach of a slot tests the click and not the carry.
/// </summary>
public static class FeelStage
{
    /// <summary>
    /// Props live on physics layer 1 (so they collide with the sandbox ground and its blockwork,
    /// which are on 1) AND on layer 2, which is the layer the interactor's reach area listens to.
    /// Two bits, one object: the area then never reports a wall it would only have to discard, and
    /// the reach volume does no broadphase work for the world's static bodies.
    ///
    /// Layer 2 is free in this project -- <c>project.godot</c> names layer 1 "World" and nothing
    /// else -- so nothing shipped shares it. That is also why the sandbox's own Carryable crates
    /// and balls are NOT in this scene: they sit on layer 1 only, they set ContinuousCd and
    /// ContactMonitor in their own _Ready (which Interactable then clears), and they carry a second
    /// highlight system on the same body. Two interaction systems on one prop is not a feel test.
    /// </summary>
    private const uint PropLayer = 1 | 2;
    private const uint PropMask = 1 | 2;

    public sealed class Built
    {
        public List<Interactable> Items = new();
        public List<InteractionSlot> Slots = new();
    }

    public static Built Build(Node3D parent, Material hullMaterial)
    {
        var b = new Built();

        // ---- the bench the slots sit on --------------------------------------------------------
        // A real static body, not a decal on the floor: a snap slot above a surface you can also
        // tumble a prop onto is what makes the two modes comparable in the same place.
        Solid(parent, "Bench", new Vector3(1.8f, 0.7f, 0.7f), new Color(0.44f, 0.42f, 0.40f),
            new Vector3(2.60f, 0.35f, -5.20f));

        Sign(parent, "TUMBLE", new Vector3(-2.80f, 2.40f, -5.00f), new Color(0.92f, 0.72f, 0.46f));
        Sign(parent, "SNAP", new Vector3(2.60f, 2.40f, -5.20f), new Color(0.55f, 0.82f, 0.90f));

        // ---- slots -----------------------------------------------------------------------------
        // Spaced 0.45 m at a 0.32 m radius, so the catch zones very nearly touch without a gap. A
        // GAP between slots is a gap the player has to aim around and items fall through onto the
        // floor; heavy overlap makes the choice of slot feel arbitrary. FindBest takes the nearest
        // accepting slot, so a little overlap is safe -- a lot is indistinguishable from one big
        // slot. Unchanged from the lab: this is hand-scale, not room-scale.
        //
        // The catch volume is a vertical CYLINDER, not a sphere. See InteractionSlot.VerticalReach:
        // a sphere cannot reach from a chest-height hand down to a bench-height slot without also
        // being half a metre wide, and the whole bench then behaves as a single slot.
        var anySlot = Slot(parent, "Slot_Any", new Vector3(2.15f, 0.70f, -5.20f), 0.32f, "");
        var vesselSlot = Slot(parent, "Slot_Vessel", new Vector3(2.60f, 0.70f, -5.20f), 0.32f, "vessel");
        var orbSlot = Slot(parent, "Slot_Orb", new Vector3(3.05f, 0.70f, -5.20f), 0.32f, "orb");
        b.Slots.AddRange(new[] { anySlot, vesselSlot, orbSlot });

        // ---- tumble props, left of the spawn facing --------------------------------------------
        b.Items.Add(Prop(parent, "0.4 kg block", Box(0.20f), new Color(0.78f, 0.74f, 0.62f),
            0.4f, new Vector3(-1.80f, 0.12f, -3.60f), Interactable.ReleaseMode.Tumble, ""));

        b.Items.Add(Prop(parent, "4 kg crate", Box(0.32f), new Color(0.62f, 0.48f, 0.34f),
            4.0f, new Vector3(-2.90f, 0.18f, -4.40f), Interactable.ReleaseMode.Tumble, ""));

        // Flat and dense. The shape says "heavy" before you touch it, which is the honest way round
        // -- an item that looks light and lags like an anvil reads as a bug in the carry, not as a
        // surprise about the item.
        b.Items.Add(Prop(parent, "22 kg anvil", new BoxMesh { Size = new Vector3(0.40f, 0.20f, 0.28f) },
            new Color(0.30f, 0.31f, 0.34f), 22.0f, new Vector3(-4.10f, 0.11f, -3.70f),
            Interactable.ReleaseMode.Tumble, "",
            shape: new BoxShape3D { Size = new Vector3(0.40f, 0.20f, 0.28f) }));

        // LONG AND OFF-CENTRE. This is the prop that makes the trailing swing visible, because a
        // tilt on a cube is a tilt on a silhouette that barely changes and a tilt on a metre of
        // plank is unmissable. Pushed forward in the hand so it does not clip the character.
        b.Items.Add(Prop(parent, "6 kg plank", new BoxMesh { Size = new Vector3(0.12f, 0.12f, 1.10f) },
            new Color(0.68f, 0.58f, 0.40f), 6.0f, new Vector3(-2.40f, 0.07f, -6.10f),
            Interactable.ReleaseMode.Tumble, "",
            shape: new BoxShape3D { Size = new Vector3(0.12f, 0.12f, 1.10f) },
            holdOffset: new Vector3(0.0f, 0.0f, -0.30f)));

        // ---- snap props, right of the spawn facing, short of the bench --------------------------
        b.Items.Add(Prop(parent, "3 kg jug", new CylinderMesh
                { TopRadius = 0.12f, BottomRadius = 0.14f, Height = 0.32f, RadialSegments = 16 },
            new Color(0.52f, 0.56f, 0.62f), 3.0f, new Vector3(1.40f, 0.16f, -2.90f),
            Interactable.ReleaseMode.Snap, "vessel",
            shape: new CylinderShape3D { Radius = 0.14f, Height = 0.32f }));

        var orbB = Prop(parent, "1.6 kg orb B", new SphereMesh { Radius = 0.13f, Height = 0.26f,
                RadialSegments = 16, Rings = 8 },
            new Color(0.72f, 0.56f, 0.44f), 1.6f, new Vector3(2.30f, 0.13f, -2.60f),
            Interactable.ReleaseMode.Snap, "orb",
            shape: new SphereShape3D { Radius = 0.13f });
        b.Items.Add(orbB);

        b.Items.Add(Prop(parent, "2 kg tool", new BoxMesh { Size = new Vector3(0.09f, 0.09f, 0.40f) },
            new Color(0.46f, 0.60f, 0.50f), 2.0f, new Vector3(3.20f, 0.05f, -3.00f),
            Interactable.ReleaseMode.Snap, "",
            shape: new BoxShape3D { Size = new Vector3(0.09f, 0.09f, 0.40f) },
            holdOffset: new Vector3(0.0f, 0.0f, -0.12f)));

        // ---- the pre-occupied slot -------------------------------------------------------------
        var orbA = Prop(parent, "1.6 kg orb A", new SphereMesh { Radius = 0.13f, Height = 0.26f,
                RadialSegments = 16, Rings = 8 },
            new Color(0.74f, 0.60f, 0.30f), 1.6f, orbSlot.Position + Vector3.Up * 0.13f,
            Interactable.ReleaseMode.Snap, "orb",
            shape: new SphereShape3D { Radius = 0.13f });
        b.Items.Add(orbA);

        foreach (Interactable it in b.Items) it.BuildHull(hullMaterial);

        // Seat orb A in its slot without going through a carry. Safe to call straight through:
        // AddChild on a node already in the tree runs the child's _Ready synchronously, so every
        // Interactable above has already found its body by the time Build returns. That is also why
        // OnSnapReleased captures the body's original collision layers in _Ready rather than at
        // grab time -- a slotted item that was never carried would otherwise restore zeroes.
        orbA.OnSnapReleased(orbSlot, orbA.Body.GlobalTransform, 0.02f);

        return b;
    }

    // ------------------------------------------------------------------------------------ helpers

    private static BoxMesh Box(float s) => new() { Size = new Vector3(s, s, s) };

    /// <summary>
    /// A prop: rigid body, collision shape, mesh, and the component that makes it interactable.
    ///
    /// THIS IS ALSO THE DOCUMENTATION FOR "how do I wire a new item in". Four nodes, one of which
    /// is the component, and no registration anywhere -- the component finds its own body and puts
    /// itself in the lookup the interactor reads. If this method ever needs a fifth step, the
    /// system has grown a coupling it was built not to have.
    /// </summary>
    private static Interactable Prop(Node3D parent, string name, Mesh mesh, Color colour,
        float mass, Vector3 pos, Interactable.ReleaseMode mode, string tag,
        Shape3D? shape = null, Vector3 holdOffset = default, Vector3 holdRotationDeg = default)
    {
        var body = new RigidBody3D
        {
            Name = name.Replace(' ', '_').Replace('.', '_'),
            Mass = mass,
            Position = pos,
            CollisionLayer = PropLayer,
            CollisionMask = PropMask,
        };

        var mat = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.85f };
        body.AddChild(new MeshInstance3D { Name = "Mesh", Mesh = mesh, MaterialOverride = mat });

        if (shape == null)
        {
            Vector3 size = mesh is BoxMesh bm ? bm.Size : Vector3.One * 0.3f;
            shape = new BoxShape3D { Size = size };
        }
        body.AddChild(new CollisionShape3D { Name = "Shape", Shape = shape });

        var it = new Interactable
        {
            Name = "Interactable",
            DisplayName = name,
            Release = mode,
            Tag = tag,
            HoldOffset = holdOffset,
            HoldRotationDegrees = holdRotationDeg,
        };
        body.AddChild(it);

        parent.AddChild(body);
        return it;
    }

    private static InteractionSlot Slot(Node3D parent, string name, Vector3 pos, float radius, string tag)
    {
        var s = new InteractionSlot
        {
            Name = name,
            Position = pos,
            Radius = radius,
            AcceptTag = tag,
            // The settled pose sits above the slot node, which is on the bench surface, by about
            // half a prop. One number for every prop is a placeholder compromise and is stated as
            // one: a real project sets this per slot, or derives it from the item's own AABB.
            RestHeight = 0.15f,
        };
        parent.AddChild(s);
        return s;
    }

    private static void Solid(Node3D parent, string name, Vector3 size, Color colour, Vector3 pos)
    {
        var body = new StaticBody3D { Name = name, Position = pos };
        body.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.9f },
        });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
    }

    /// <summary>Floating text over a zone. Billboarded and depth-tested normally, so a sign behind
    /// the bench is occluded by the bench -- a label that floats through solid geometry stops
    /// reading as being attached to a place in the world. PixelSize is up from the lab's 0.0032:
    /// this camera stands farther back than the lab's did, and a sign you cannot read is a sign
    /// that is not labelling anything.</summary>
    private static void Sign(Node3D parent, string text, Vector3 pos, Color colour)
    {
        parent.AddChild(new Label3D
        {
            Name = "Sign_" + text,
            Text = text,
            Position = pos,
            PixelSize = 0.0055f,
            FontSize = 96,
            Modulate = colour,
            OutlineSize = 14,
            OutlineModulate = new Color(0, 0, 0, 0.75f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        });
    }
}
