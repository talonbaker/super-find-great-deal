using Godot;
using MpFoundation.Game.Round;
using MpFoundation.Ui.Design;

namespace MpFoundation.Game.World;

/// <summary>
/// <b>One of the task room's three sorting bins</b> (TASK-1, 2026-09-19): an open box on a
/// plinth with a label plate on its front, which says a different word on alternating rounds.
///
/// <para><b>The bin never changes and its MEANING does.</b> Bin 0 asks for RED on an odd round
/// and for CUBE on an even one; the boxes stay exactly where they are. That is the mechanic
/// Talon asked for — the player learns three positions once and then has to re-learn what they
/// mean, which is the difference between concentrating and running on habit. Re-arranging the
/// room instead would have made the second round a fresh search rather than a harder version of
/// the same job.</para>
///
/// <para><b>Which bin this is comes from the NODE NAME</b> (<c>SortBin_0</c>), not from an
/// <c>[Export]</c>. <c>TaskRoom.tscn</c> is instanced into <c>Supermarket.tscn</c> and a nested
/// instance's exported C# script properties are silently dropped on this build — CARRY-1 §6 and
/// CLOCK-1 §3.1 both measured it, and <c>.claude/rules/godot-scenes.md</c> states the rule this
/// follows. A node name is native.</para>
///
/// <para><b>It has no <c>_Process</c> and no RPC.</b> <c>SortRoom</c> owns the one poll for all
/// three bins (<c>RoundAudio</c>'s pattern for the clocks, and the same reason: three scene-tree
/// callbacks a frame to read one enum), and nothing about a bin is ever sent — the rule is
/// derived from the round index on every peer by <see cref="SortRule.For"/>.</para>
///
/// <para><b>Three channels, not one.</b> The plate carries the WORD, the plate's own colour
/// carries the tint on a colour round, and a small unshaded icon carries the shape on a shape
/// round. <c>INTERACTION-BIBLE.md</c> §8.2 is about a consequence delivered on one channel only;
/// here the channel a player cannot use (a red/yellow confusion, a glance too quick to read) is
/// always covered by one they can.</para>
///
/// <para><b>Nothing here builds a node.</b> Every part is authored in <c>SortBin.tscn</c> and
/// merely found; that file is in <c>SupermarketWorldSelfTest.SectionScenes</c>, so a child added
/// in <c>_Ready</c> turns the level suite red (CLOCK-1 §3.2 is why that list has to name every
/// LEVEL prefab).</para>
/// </summary>
public partial class SortBin : Node3D
{
    /// <summary>The node-name prefix every bin carries; the digits after it are the slot.</summary>
    public const string NamePrefix = "SortBin_";

    /// <summary>The authored <c>Area3D</c> whose box IS the bin's mouth. Monitoring is off: this
    /// is a SHAPE AND A TRANSFORM the sort reads directly, exactly like CARRY-1's
    /// <c>RoomBounds</c>, rather than a pair of signals. Pairing eighteen rigid bodies with three
    /// areas every frame would be broadphase work for an answer that is one point-in-box
    /// test.</summary>
    public const string VolumeName = "Volume";

    /// <inheritdoc cref="VolumeName"/>
    public const string VolumeShapeName = "Collision";

    /// <summary>The front plate: the thing that is tinted on a colour round.</summary>
    public const string PlateName = "Plate";

    /// <summary>The word on the plate.</summary>
    public const string PlateLabelName = "PlateLabel";

    /// <summary>The parent of the three shape icons. All three are authored; the one matching
    /// this bin's slot is shown on a shape round and all three are hidden on a colour round —
    /// one prefab serves all three bins, which is why the icons are a visibility choice rather
    /// than three prefabs.</summary>
    public const string PlateIconName = "PlateIcon";

    /// <inheritdoc cref="PlateIconName"/>
    public static readonly string[] IconNames = { "IconCube", "IconBall", "IconCan" };

    /// <summary>How long the plate stays lit after a correct sort, in seconds. Short: it is a
    /// receipt, not a celebration, and the player is already turning round for the next
    /// object.</summary>
    public const float FlashSec = 0.35f;

    /// <summary>How much brighter the plate's emission goes at the peak of a flash. A
    /// MULTIPLIER on the resting energy rather than a colour, so a flash cannot make a bin look
    /// like a different bin for a third of a second — which is exactly the confusion this
    /// mechanic must not manufacture.</summary>
    public const float FlashEnergyMultiplier = 6f;

    /// <summary>Resting emission of the plate. Low, for the reason <see cref="SortPalette"/>
    /// gives: a prop (or a plate) may glow and may not light.</summary>
    private const float RestEnergy = 0.25f;

    /// <summary>Which bin this is, 0..<see cref="SortRule.BinCount"/>-1, from the node name.
    /// −1 when the name did not carry one, which is a level defect and is reported.</summary>
    public int Slot { get; private set; } = -1;

    private Area3D? _volume;
    private BoxShape3D? _box;
    private Transform3D _volumeInverse = Transform3D.Identity;
    private MeshInstance3D? _plate;
    private StandardMaterial3D? _plateMaterial;
    private Label3D? _label;
    private Node3D? _iconRoot;
    private readonly MeshInstance3D?[] _icons = new MeshInstance3D?[SortRule.BinCount];

    private UiTokens _tokens;
    private bool _haveTokens;
    private SortBy _rule = SortBy.Colour;
    private bool _labelled;
    private float _flashLeft;

    public override void _Ready()
    {
        Slot = SlotFromName(Name.ToString());
        if (Slot < 0)
        {
            GD.PushError($"[sort] bin '{Name}' does not end in a slot index — a bin is named "
                         + $"{NamePrefix}0..{NamePrefix}{SortRule.BinCount - 1}, and the slot is "
                         + "read from the name because a nested instance's [Export] is dropped "
                         + "on this build.");
        }

        _volume = GetNodeOrNull<Area3D>(VolumeName);
        _box = _volume?.GetNodeOrNull<CollisionShape3D>(VolumeShapeName)?.Shape as BoxShape3D;
        _plate = GetNodeOrNull<MeshInstance3D>(PlateName);
        _label = GetNodeOrNull<Label3D>(PlateLabelName);
        _iconRoot = GetNodeOrNull<Node3D>(PlateIconName);
        for (int i = 0; i < IconNames.Length; i++)
            _icons[i] = _iconRoot?.GetNodeOrNull<MeshInstance3D>(IconNames[i]);

        if (_volume == null || _box == null || _plate == null || _label == null)
        {
            // Loud, then inert. A bin with no volume takes nothing and looks identical to a bin
            // the player simply has not filled, and those have completely different fixes.
            GD.PushError($"[sort] bin '{Name}' is missing volume={_volume != null} "
                         + $"box={_box != null} plate={_plate != null} label={_label != null} "
                         + "— check SortBin.tscn.");
            return;
        }

        // The plate's material is resource_local_to_scene in SortBin.tscn, so three bins are
        // three materials. Duplicated defensively for RoundClock's reason: a shared one would
        // let the last bin to _Ready decide the colour of all three.
        _plateMaterial = _plate.GetActiveMaterial(0) as StandardMaterial3D;
        if (_plateMaterial != null && !_plateMaterial.ResourceLocalToScene)
        {
            _plateMaterial = (StandardMaterial3D)_plateMaterial.Duplicate();
            _plate.SetSurfaceOverrideMaterial(0, _plateMaterial);
        }

        UiThemeService.Bind(this, ApplyTokens);
    }

    /// <summary>The digits after <see cref="NamePrefix"/>, or −1. Pure and static so the naming
    /// convention is a function rather than a habit.</summary>
    public static int SlotFromName(string nodeName)
    {
        if (string.IsNullOrEmpty(nodeName) || !nodeName.StartsWith(NamePrefix, System.StringComparison.Ordinal))
            return -1;
        return int.TryParse(nodeName[NamePrefix.Length..], out int slot)
               && slot >= 0 && slot < SortRule.BinCount
            ? slot
            : -1;
    }

    /// <summary>
    /// Is this global point inside the bin's mouth? A point-in-box test in the volume's own
    /// space, which is the cheapest exact answer for an authored box and needs no physics query
    /// at all.
    /// </summary>
    /// <remarks>The inverse transform is refreshed once per <see cref="Tick"/> rather than per
    /// call: bins never move, and eighteen objects against three bins is 54 calls a tick.</remarks>
    public bool Contains(Vector3 globalPoint)
    {
        if (_box == null)
            return false;
        Vector3 local = _volumeInverse * globalPoint;
        Vector3 half = _box.Size * 0.5f;
        return Mathf.Abs(local.X) <= half.X
               && Mathf.Abs(local.Y) <= half.Y
               && Mathf.Abs(local.Z) <= half.Z;
    }

    /// <summary>How far in front of the plate a body stands to work at this bin, metres.
    /// Measured along the bin's own -Z, so it follows the bin if a level turns one. Inside
    /// <c>PropManager.PlaceReachM + GrabRangeTolerance</c> (1.65 m) from the hand with room to
    /// spare, which is what makes a scripted delivery land rather than be refused for
    /// distance.</summary>
    public const float ApproachOffsetM = 1.1f;

    /// <summary>Height above the bin's origin at which an object is SET DOWN, metres: 0.08 m
    /// above the tray's top face, so the three authored sortables (half-heights 0.070-0.080 m)
    /// land within a centimetre of resting rather than being dropped from the rim.</summary>
    public const float DropHeightM = 0.74f;

    /// <summary>How far from the bin's centre line each drop spot sits. Two spots 0.26 m apart
    /// clear the widest sortable (the 0.16 m ball) with 0.10 m to spare, which matters because
    /// <c>PlacementIntegrity</c> refuses a placement that penetrates a prop already there — a
    /// fixture that always aimed at the centre would have its second delivery refused.</summary>
    private const float DropSpreadM = 0.13f;

    /// <summary>Where a body stands to work at this bin, in world space.</summary>
    public Vector3 ApproachPoint => GlobalTransform * new Vector3(0f, 0f, -ApproachOffsetM);

    /// <summary>Where the <paramref name="nth"/> object set down in this bin goes, in world
    /// space. Four spots, cycled, for the reason <see cref="DropSpreadM"/> gives.</summary>
    public Transform3D DropPoint(int nth)
    {
        int i = ((nth % 4) + 4) % 4;
        float dx = (i & 1) == 0 ? -DropSpreadM : DropSpreadM;
        float dz = (i & 2) == 0 ? -DropSpreadM : DropSpreadM;
        return new Transform3D(Basis.Identity,
            GlobalTransform * new Vector3(dx, DropHeightM, dz));
    }

    /// <summary>
    /// The bin with this slot, anywhere under <paramref name="from"/>'s tree.
    ///
    /// <para>For a fixture, not for the game: <see cref="SortRoom"/> finds its own bins by
    /// walking its parent. A scripted bot has no handle on the task room, and hard-coding the
    /// bins' coordinates into a suite is how a fixture comes to be testing a level that has
    /// moved. Reading them out of the bot's OWN copy of the world is also the honest thing —
    /// every peer has the same authored room, which is the whole premise TASK-1 rests on.</para>
    /// </summary>
    public static SortBin? Find(Node from, int slot)
    {
        Node? root = from?.GetTree()?.Root;
        return root == null ? null : Search(root, slot);
    }

    private static SortBin? Search(Node from, int slot)
    {
        if (from is SortBin bin && bin.Slot == slot)
            return bin;
        foreach (Node child in from.GetChildren())
        {
            SortBin? hit = Search(child, slot);
            if (hit != null)
                return hit;
        }
        return null;
    }

    /// <summary>Driven by <see cref="SortRoom"/>'s one poll: refreshes the cached volume
    /// transform and decays the flash. There is deliberately no <c>_Process</c> here.</summary>
    public void Tick(double delta)
    {
        if (_volume != null)
            _volumeInverse = _volume.GlobalTransform.AffineInverse();

        if (_flashLeft <= 0f)
            return;
        _flashLeft -= (float)delta;
        PaintPlate();
    }

    /// <summary>
    /// Re-label for <paramref name="rule"/>: the word, the tint and the icon.
    ///
    /// <para>Called on the reset edge and on any poll where the derived rule has changed, which
    /// is the same thing said two ways — a late joiner gets it on its first poll and needs no
    /// catch-up path, because the rule is a pure function of a round index it already
    /// has.</para>
    /// </summary>
    public void Relabel(SortBy rule)
    {
        if (_labelled && rule == _rule)
            return;
        _rule = rule;
        _labelled = true;

        if (_label != null)
            _label.Text = HideSeekText.BinPlateWord(rule, Slot);

        for (int i = 0; i < _icons.Length; i++)
        {
            if (_icons[i] != null)
                _icons[i]!.Visible = rule == SortBy.Shape && i == Slot;
        }

        PaintPlate();
    }

    /// <summary>The receipt for a correct sort. Idempotent and re-armable — two sorts into one
    /// bin inside a third of a second restart the flash rather than stacking.</summary>
    public void Flash() => _flashLeft = FlashSec;

    private void ApplyTokens(UiTokens tokens)
    {
        _tokens = tokens;
        _haveTokens = true;
        if (_label != null)
            _label.Modulate = tokens.InkRank1;
        for (int i = 0; i < _icons.Length; i++)
        {
            if (_icons[i]?.GetActiveMaterial(0) is StandardMaterial3D m)
                m.AlbedoColor = tokens.InkRank1;
        }
        PaintPlate();
    }

    /// <summary>
    /// The plate's own colour: the sort colour on a colour round, the neutral sunken surface on
    /// a shape round.
    ///
    /// <para><b>The colour comes from <see cref="SortPalette"/> and the neutral from
    /// <see cref="UiTokens"/>, and that split is the point.</b> The tint on a colour round is the
    /// RULE and must be the same value the objects are painted with; the neutral is chrome and
    /// belongs to the palette, exactly as <c>RoundClock</c>'s panel does. A plate that lost its
    /// token binding renders white and screams rather than quietly reverting.</para>
    /// </summary>
    private void PaintPlate()
    {
        if (_plateMaterial == null)
            return;
        Color rest = _rule == SortBy.Colour && Slot >= 0
            ? SortPalette.Of((SortColour)Slot)
            : _haveTokens ? _tokens.SurfaceSunken : Colors.White;

        _plateMaterial.AlbedoColor = rest;
        _plateMaterial.EmissionEnabled = true;
        _plateMaterial.Emission = rest;
        float lit = _flashLeft > 0f ? Mathf.Clamp(_flashLeft / FlashSec, 0f, 1f) : 0f;
        _plateMaterial.EmissionEnergyMultiplier =
            RestEnergy * (1f + lit * (FlashEnergyMultiplier - 1f));
    }
}
