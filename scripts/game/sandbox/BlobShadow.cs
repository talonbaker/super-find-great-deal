using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// The Bible §3 shadow: a soft dark quad that hugs the ground under a mover. One shared
/// mesh + one shared material + one generated radial gradient for EVERY blob in the
/// scene (adding a mover costs one node and zero materials), against real-time shadow
/// maps' per-light scene re-renders. A shadow only has to say "this thing is grounded,
/// and it is THIS far above the floor" — a blob says exactly that.
///
/// Each physics tick it raycasts straight down from its parent (world geometry only,
/// parent excluded), sits just above the hit, and shrinks/fades with height so a jump
/// reads. TopLevel: the parent's waddle/tilt/scale never skews the quad. Skipped
/// entirely on headless peers (dedicated servers and bots render nothing).
/// </summary>
public partial class BlobShadow : Node3D
{
    private const float GroundOffset = 0.03f;  // above the floor, below z-fight range
    private const float MaxDrop = 8f;          // beyond this fall there is no shadow
    private const float BaseAlpha = 0.42f;

    /// <summary>Shadow radius in metres at ground contact. Settable after attachment, not
    /// <c>init</c>-only: an avatar re-measures its body whenever its appearance rebuilds (the
    /// avatar-key synchronizer catching up to the authority's pick), and a shadow left at the
    /// previous character's width is one of the four things that went stale when the player
    /// stopped being the original squat body. Read fresh every physics tick, so a write lands immediately.</summary>
    public float Radius { get; set; } = 0.42f;

    private static QuadMesh? _sharedMesh;
    private static StandardMaterial3D? _sharedMat;

    /// <summary>The IntersectRay result key, built into a Variant once instead of marshalling
    /// the managed string into a Godot String on every physics tick (perf audit 2026-08-07).
    /// A String-typed Variant, exactly what <c>hit["position"]</c> produced before — the
    /// dictionary lookup is byte-for-byte the same operation, only the key stops being rebuilt.</summary>
    private static readonly Variant PositionKey = "position";

    private MeshInstance3D _quad = null!;
    private PhysicsBody3D? _owner;

    // Hoisted out of _PhysicsProcess (perf audit 2026-08-07). Both were rebuilt every tick, per
    // blob, and a blob exists per avatar AND per carryable — so this was the project's largest
    // per-frame allocation source, scaling with BOTH player count and prop count. The query
    // object's other fields (CollisionMask, CollideWithBodies/Areas) keep the same defaults
    // PhysicsRayQueryParameters3D.Create used; only From/To are rewritten per tick. The owner's
    // physics RID is fixed for the body's lifetime, so the exclude set never needs rebuilding.
    private PhysicsRayQueryParameters3D _rayQuery = null!;

    /// <summary>Attaches a blob shadow under <paramref name="owner"/> unless this peer is
    /// headless (no renderer — a dedicated server or bot has no use for shadows). Returns the
    /// shadow, or null on a headless peer, so a caller that re-sizes its mover later has
    /// something to write <see cref="Radius"/> on without going back through the node tree.</summary>
    public static BlobShadow? Attach(PhysicsBody3D owner, float radius = 0.42f)
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsHeadless)
            return null;
        var shadow = new BlobShadow { Name = "BlobShadow", Radius = radius };
        owner.AddChild(shadow);
        return shadow;
    }

    public override void _Ready()
    {
        TopLevel = true;
        _owner = GetParent() as PhysicsBody3D;

        _sharedMesh ??= new QuadMesh { Size = Vector2.One, Orientation = PlaneMesh.OrientationEnum.Y };
        _sharedMat ??= new StandardMaterial3D
        {
            AlbedoTexture = BuildRadialTexture(),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = false,
            // Never writes depth and draws after opaque — a decal in spirit.
            RenderPriority = -1,
        };
        _quad = new MeshInstance3D
        {
            Name = "Quad",
            Mesh = _sharedMesh,
            MaterialOverride = _sharedMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_quad);

        _rayQuery = new PhysicsRayQueryParameters3D();
        if (_owner != null)
            _rayQuery.Exclude = new Godot.Collections.Array<Rid> { _owner.GetRid() };
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_owner == null || !IsInstanceValid(_owner))
            return;
        // Probe under where the owner RENDERS: an avatar hiding a reconciliation
        // correction draws its mesh offset from the raw body, and the grounding cue
        // must hug the feet the player sees, not the invisible simulated body.
        Vector3 basePos = _owner is SandboxAvatar avatar
            ? avatar.RenderGlobalPosition
            : _owner.GlobalPosition;
        Vector3 from = basePos + Vector3.Up * 0.1f;
        _rayQuery.From = from;
        _rayQuery.To = from + Vector3.Down * (MaxDrop + 0.1f);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(_rayQuery);
        if (hit.Count == 0)
        {
            _quad.Visible = false;
            return;
        }

        Vector3 point = (Vector3)hit[PositionKey];
        float drop = Mathf.Max(0f, from.Y - 0.1f - point.Y);
        float t = 1f - Mathf.Clamp(drop / MaxDrop, 0f, 1f);
        // Height response: smaller and fainter the higher the mover is — enough change
        // that a jump visibly "lifts off" the shadow.
        float scale = Radius * 2f * Mathf.Lerp(0.55f, 1f, t);
        _quad.Visible = true;
        GlobalPosition = point + Vector3.Up * GroundOffset;
        GlobalRotation = Vector3.Zero;
        _quad.Scale = new Vector3(scale, 1f, scale);
        _quad.Transparency = 1f - BaseAlpha * Mathf.Lerp(0.35f, 1f, t * t);
    }

    /// <summary>Test hook: is the shadow quad currently drawn? (Purely the logical visibility flag —
    /// meaningful headless, where nothing actually rasterizes.)</summary>
    internal bool IsRendering => _quad.Visible;

    /// <summary>A soft radial falloff: opaque dark centre easing to transparent at the rim.
    /// Generated once (64px is plenty for a soft blob) and shared by every instance.</summary>
    private static GradientTexture2D BuildRadialTexture()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0f, 0f, 0f, 1f));
        gradient.SetColor(1, new Color(0f, 0f, 0f, 0f));
        gradient.AddPoint(0.55f, new Color(0f, 0f, 0f, 0.8f));
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 64,
            Height = 64,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.5f, 0.0f),
        };
    }
}
