using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>One sortable object's two facts</b> (TASK-1, 2026-09-19): the colour it is and the shape it
/// is. A plain <c>Node</c> hanging off each <c>NetworkedProp</c> in the task room's supply crate,
/// authored in <c>TaskRoom.tscn</c> beside the prop it describes.
///
/// <para><b>NEITHER FACT IS AN <c>[Export]</c>, AND THAT IS THE FOURTH TIME THIS REPO HAS PAID
/// FOR ONE TRAP.</b> Godot does not apply a nested PackedScene instance's exported C# script
/// properties on this project's Godot/Mono build, and <c>TaskRoom.tscn</c> is itself instanced
/// into <c>Supermarket.tscn</c> — CARRY-1 §6 measured exactly that shape and BurstDoor's
/// <c>_Ready</c> already avoids an <c>[Export] NodePath</c> because of it. Before that:
/// <c>PropManager.AuthoredKindOf</c> (<c>Crate.tscn</c>'s <c>kind</c>/<c>tint</c>),
/// <c>Carryable.LoadLiftM</c>, and <c>RoundClock.Room</c>. So both facts come from native
/// properties, which provably survive instancing:</para>
///
/// <list type="bullet">
/// <item><b>Colour, from the PARENT'S NODE NAME</b> — <c>Sort_000_Red</c>. A node name is native.
/// This is the rule <c>.claude/rules/godot-scenes.md</c> states after CLOCK-1 ("derive identity
/// from the node name"), and it has the side benefit that <c>TaskRoom.tscn</c> reads as what it
/// is: the line says <c>Sort_007_Blue</c> and its child instances <c>SortBall.tscn</c>, so a
/// level author reads "blue ball" off the file without loading it.</item>
/// <item><b>Shape, from the COLLIDER</b> — <c>Carryable.ShapeFromCollider</c>, which is SFX-1's
/// table and is already how this repo decides a prop's wire kind and its sound profile. A
/// fourth reader of the same native fact cannot disagree with the other three.</item>
/// </list>
///
/// <para><b>It also tints the object</b>, from <see cref="SortPalette"/>, rather than the three
/// prefabs shipping in nine colour variants. Three prefabs and a runtime tint means the colour a
/// player sees and the colour the rule tests are literally the same value: there is no authored
/// albedo anywhere that could drift from the enum. Same argument <c>RoundClock</c> makes for
/// taking its panel colour from the tokens instead of the scene file.</para>
///
/// <para><b>It builds nothing.</b> No child, no mesh, no collider —
/// <c>SupermarketWorldSelfTest</c> counts <c>TaskRoom.tscn</c>'s packed nodes against its live
/// ones and this node is authored on both sides.</para>
/// </summary>
public partial class SortItem : Node
{
    /// <summary>The node name this component always has inside a sortable. Spelled once.</summary>
    public const string NodeName = "SortItem";

    /// <summary>The prefix every sortable's node name starts with, which is also what
    /// <c>SortRoom</c> is looking for and what fixes the adoption order (ids are assigned by
    /// sorted node path — <c>PropManager.AdoptAuthoredProps</c>).</summary>
    public const string NamePrefix = "Sort_";

    /// <summary>This object's colour, from its parent's node name. <see cref="Resolved"/> says
    /// whether the name actually carried one.</summary>
    public SortColour Colour { get; private set; }

    /// <summary>This object's shape, from its own body's collider.</summary>
    public SortShape Shape { get; private set; }

    /// <summary>Both facts, for <see cref="SortRule"/>.</summary>
    public SortItemFacts Facts => new(Colour, Shape);

    /// <summary>False when the parent's name carried no recognisable colour. A false here is a
    /// LEVEL defect, reported loudly at <c>_Ready</c> and counted by <c>SortRoom</c>'s census
    /// line, because the alternative is eighteen objects that all silently claim to be red.
    /// </summary>
    public bool Resolved { get; private set; }

    /// <summary>The prop this component describes — its parent, which is the adopted
    /// <c>NetworkedProp</c>. Null only if a level authors this node somewhere it does not
    /// belong.</summary>
    public NetworkedProp? Prop { get; private set; }

    /// <summary>The prop's id, or 0 before adoption. Read every poll rather than cached:
    /// <c>PropManager.AdoptAuthoredProps</c> runs after the world is in the tree, so the id is
    /// not yet assigned when this node's <c>_Ready</c> fires.</summary>
    public int PropId => Prop?.PropId ?? 0;

    public override void _Ready()
    {
        Prop = GetParent() as NetworkedProp;
        if (Prop == null)
        {
            GD.PushError($"[sort] {NodeName} under '{GetParent()?.Name}' has no NetworkedProp "
                         + "parent — a sortable is a NetworkedProp with this node and a Body "
                         + "child. See TaskRoom.tscn.");
            return;
        }

        Resolved = TryColourFromName(Prop.Name.ToString(), out SortColour colour);
        Colour = colour;
        if (!Resolved)
        {
            GD.PushError($"[sort] '{Prop.Name}' does not end in a sort colour "
                         + "(Red/Blue/Yellow), so this object has no colour. Node names carry "
                         + "the colour on this build because a nested instance's [Export] is "
                         + "silently dropped — see this class's doc.");
        }

        Carryable? body = Prop.GetNodeOrNull<Carryable>("Body");
        Shape = ShapeOf(body);
        Tint(body);
    }

    /// <summary>
    /// The colour at the end of a sortable's node name: <c>Sort_000_Red</c> → red. Pure and
    /// static so the naming convention is one function rather than a habit.
    ///
    /// <para>Case-insensitive on the colour word and tolerant of the index having any width, so
    /// a level author renumbering the crate cannot break the colour by accident. It does NOT
    /// default to red on a miss — see <see cref="Resolved"/>.</para>
    /// </summary>
    public static bool TryColourFromName(string nodeName, out SortColour colour)
    {
        colour = SortColour.Red;
        if (string.IsNullOrEmpty(nodeName))
            return false;
        int cut = nodeName.LastIndexOf('_');
        if (cut < 0 || cut == nodeName.Length - 1)
            return false;
        string word = nodeName[(cut + 1)..];
        if (word.Equals("Red", System.StringComparison.OrdinalIgnoreCase))
            colour = SortColour.Red;
        else if (word.Equals("Blue", System.StringComparison.OrdinalIgnoreCase))
            colour = SortColour.Blue;
        else if (word.Equals("Yellow", System.StringComparison.OrdinalIgnoreCase))
            colour = SortColour.Yellow;
        else
            return false;
        return true;
    }

    /// <summary>
    /// The shape, off the collider, through SFX-1's one table.
    ///
    /// <para><c>Carryable.Shape</c> has five members and this enum has three, because the sort's
    /// three are the three the task room authors. The mapping is stated here rather than inside
    /// <c>ShapeFromCollider</c> so that table stays the prop layer's and gains nothing about
    /// sorting: a crate-shaped collider is <c>Cube</c>, a small sphere is <c>Ball</c>, a cylinder
    /// is <c>Can</c>, and anything else is <c>Cube</c> with the census line to say so.</para>
    /// </summary>
    public static SortShape ShapeOf(Carryable? body)
    {
        Carryable.Shape shape = Carryable.ShapeFromCollider(
            body?.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape);
        return shape switch
        {
            Carryable.Shape.Can => SortShape.Can,
            Carryable.Shape.Ball or Carryable.Shape.Produce => SortShape.Ball,
            _ => SortShape.Cube,
        };
    }

    /// <summary>Paints the body from <see cref="SortPalette"/>. The prefabs' materials are
    /// <c>resource_local_to_scene</c>, so three red cubes are three materials; duplicated
    /// defensively anyway, for the reason <c>RoundClock</c> gives — a shared material here would
    /// make the last object to <c>_Ready</c> decide the colour of all of them, and that is
    /// invisible until somebody changes the palette.</summary>
    private void Tint(Carryable? body)
    {
        var mesh = body?.GetNodeOrNull<MeshInstance3D>("Visual/MeshInstance3D");
        if (mesh?.GetActiveMaterial(0) is not StandardMaterial3D material)
            return;
        if (!material.ResourceLocalToScene)
        {
            material = (StandardMaterial3D)material.Duplicate();
            mesh.MaterialOverride = material;
        }

        Color c = SortPalette.Of(Colour);
        material.AlbedoColor = c;
        material.EmissionEnabled = true;
        material.Emission = c;
        material.EmissionEnergyMultiplier = SortPalette.EmissionEnergy;
    }
}
