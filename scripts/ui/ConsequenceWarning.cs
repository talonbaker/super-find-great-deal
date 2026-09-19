using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Ui;

/// <summary>
/// <b>A screen-space plate that says what a destructive world control is about to cost, while
/// the player is still standing in front of it.</b>
///
/// <para><b>Why this exists rather than a bigger sign</b> (Talon, 2026-08-29 second-pass playtest,
/// note 6): <i>"please include a non-diegetic 'pop up' ... to warn the player that if they toggle
/// the switch in the main level it will reset their bubbles. Please make sure they know this
/// because the warning is good but it's too small for them to read and usually the player
/// character is blocking the text anyway."</i></para>
///
/// <para><b>Read what defeats the diegetic sign, because it decides what this class is.</b> The
/// sign's <i>wording</i> is fine — he said so. Two things beat it and both are consequences of it
/// being IN THE WORLD: it renders at world scale, so it shrinks with distance and with field of
/// view; and the third-person avatar stands between the camera and it, so at the one distance you
/// can actually press the lever, your own body is on top of the plate. <b>Neither is fixable by
/// making the sign bigger</b> — a bigger sign is a bigger thing behind the same body. A
/// <c>CanvasLayer</c> composites over the 3D viewport by construction, so it cannot be occluded by
/// anything in the world and does not scale with distance. That property, not the size, is the
/// fix.</para>
///
/// <para><b>It does not replace the sign and must not.</b> The sign marks <i>where</i> the lever is
/// from across the hub and carries the live tally on a surface anyone in the session can see; this
/// carries the <i>consequence</i>, at reading size, to the one player who is close enough to cause
/// it. Different jobs, different ranges, both kept.</para>
///
/// <para><b>It is a warning, not a confirmation.</b> No input, no mouse capture, no focus, no state
/// to dismiss: <see cref="Control.MouseFilterEnum.Ignore"/> on every node it builds, and the only
/// thing that raises or lowers it is where the player is standing. A player who walks away has
/// already dismissed it. That is also what keeps it inside
/// <see cref="UiCoverageLaw"/>: this plays while the player still has the camera and the stick, so
/// it hangs from <see cref="UiColumns.LowerBandTopAnchor"/> — below the aim point at every frame
/// height — and paints a few percent of the frame rather than a scrim.</para>
///
/// <para><b>Generic on purpose, and empty of copy.</b> The words belong next to the mechanism that
/// can be wrong about them (see <c>BubbleResetLever</c>'s copy constants, authored beside its sign
/// text for the same reason). This class owns the plate, the placement, the fade and the
/// suppression, and knows nothing about bubbles.</para>
/// </summary>
public partial class ConsequenceWarning : CanvasLayer
{
    private const double FadeInSec = UiScale.MotionQuick;
    private const double FadeOutSec = UiScale.MotionSettle;

    private Control _root = null!;
    private Label _heading = null!;
    private Label _body = null!;
    private Label _foot = null!;
    private bool _urgent;
    private bool _wanted;
    private float _alpha;

    /// <summary>The plate that actually paints — exposed so a coverage measurement reads the
    /// drawn rectangle rather than the layer's notional extent, the same seam
    /// <c>NightfallOverlay.Plate</c> opened for <c>--hudlayout-selftest</c>.</summary>
    public Control Plate { get; private set; } = null!;

    /// <summary>True while the plate is up or fading in. The capture harness and the suites read
    /// this rather than inferring visibility from a modulate.</summary>
    public bool Showing => _wanted;

    /// <summary>
    /// Builds one for <paramref name="owner"/>, or returns null on a peer that renders nothing.
    /// The caller owns it and must <see cref="Node.QueueFree"/> it when the thing it warns about
    /// leaves — it is furniture belonging to one interactable and must not outlive it holding a
    /// stale sentence about a lever that is no longer in the level.
    ///
    /// <para><b>It is parented to the scene root rather than to the owner</b>, which is where every
    /// CanvasLayer in this project that draws already lives — <c>InteractPrompt.Attach</c> and
    /// <c>GameHud.Attach</c> both take a scene root and add themselves to it. The cost is that the owner has to free it, which is why <see cref="Attach"/> hands the
    /// instance back rather than installing a singleton.
    ///
    /// <para><b>An honest note, because a wrong one nearly shipped here.</b> Four headed captures
    /// during this packet came back with no plate in them while the layer reported
    /// <c>alpha=1.00 layerVisible=True rect=(430, 403.2), (420, 190)</c> — and the tempting
    /// conclusion, that a CanvasLayer under a <see cref="Node3D"/> lays out but does not composite,
    /// is NOT what those frames showed. A full-screen debug rect proved the layer composites fine
    /// either way; the empty frames were the pop-up correctly hiding, because the bot's body was
    /// flickering across the lever's press radius and the geometry line above is printed once per
    /// raise rather than at the shutter. The parenting is an idiom choice. It is not a fix.</para>
    /// </summary>
    public static ConsequenceWarning? Attach(Node owner)
    {
        if (NetworkManager.Instance is { IsHeadless: true })
            return null;
        SceneTree? tree = owner.GetTree();
        Node? host = tree?.CurrentScene ?? tree?.Root;
        if (host == null)
            return null;
        var warning = new ConsequenceWarning { Name = nameof(ConsequenceWarning) };
        host.AddChild(warning);
        return warning;
    }

    public override void _Ready()
    {
        Layer = UiLayers.ConsequenceWarning;

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.Modulate = new Color(1, 1, 1, 0);
        _root.Visible = false;
        AddChild(_root);

        // Centred horizontally, hanging from the lower-band anchor and grown from its own content
        // — NightfallOverlay's placement, for the identical reason: the thing it has to miss is
        // the aim point, and the aim point is a FRACTION of the frame, so a fixed pixel lift
        // clears it at one window size and lands on it at another.
        PanelContainer plate = UiKit.Panel(out VBoxContainer column);
        plate.MouseFilter = Control.MouseFilterEnum.Ignore;
        column.MouseFilter = Control.MouseFilterEnum.Ignore;
        column.AddThemeConstantOverride("separation", UiScale.SpaceSnug);
        plate.AnchorLeft = 0.5f;
        plate.AnchorRight = 0.5f;
        plate.AnchorTop = UiColumns.LowerBandTopAnchor;
        plate.AnchorBottom = UiColumns.LowerBandTopAnchor;
        plate.OffsetLeft = 0f;
        plate.OffsetRight = 0f;
        plate.OffsetTop = 0f;
        plate.OffsetBottom = 0f;
        plate.GrowHorizontal = Control.GrowDirection.Both;
        plate.GrowVertical = Control.GrowDirection.End;
        _root.AddChild(plate);
        Plate = plate;

        _heading = Centred(UiKit.Heading(string.Empty));
        _heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_heading);

        // The plate's single accent instance — the once-per-surface rule. It is deliberately NOT
        // the urgent channel: danger separates from the accent in VALUE, and a second accent-
        // coloured thing would cost the first one its meaning.
        ColorRect keyline = UiKit.Keyline();
        keyline.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        column.AddChild(keyline);

        _body = Centred(UiKit.Body(string.Empty));
        _body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_body);

        _foot = Centred(UiKit.Caption(string.Empty));
        _foot.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_foot);

        // Re-inked on every palette move — and Bind applies once on the way in, so there is no
        // second call here to fall out of step with it.
        UiThemeService.Bind(this, _ => ApplyInk());

        // Nothing to do between visits. The fade re-enables it (see Set/Clear).
        SetProcess(false);
    }

    /// <summary>
    /// Raise the plate, or rewrite the one already up.
    ///
    /// <para><paramref name="urgent"/> is the escalation channel and it is redundant by
    /// construction (INTERACTION-BIBLE §8.2): the WORDS change, and the ink moves to the danger
    /// token. A player reading no colour still gets a different sentence, and a player reading no
    /// text still gets a different plate.</para>
    /// </summary>
    public void Set(string heading, string body, string foot, bool urgent)
    {
        _heading.Text = heading;
        _body.Text = body;
        _foot.Text = foot;
        if (_urgent != urgent)
        {
            _urgent = urgent;
            ApplyInk();
        }
        if (_wanted)
            return;
        _wanted = true;
        SetProcess(true);
    }

    /// <summary>Lower the plate. Idempotent — the highlighter clears on every walk-away whether or
    /// not anything was up.</summary>
    public void Clear()
    {
        if (!_wanted)
            return;
        _wanted = false;
        _reported = false;      // the next raise is a new layout and gets measured again
        SetProcess(true);
    }

    /// <summary>
    /// The fade, and the one suppression check.
    ///
    /// <para>It rides <see cref="WorldUi.Suppressed"/> rather than inventing a second flag. That
    /// flag's own doc calls itself the switch for world-<i>anchored</i> UI and this plate is
    /// screen-space, so the reading is a small stretch — but it is the correct one: the flag means
    /// "a menu or a state screen owns the frame right now", <c>FlowScreens</c> is what raises it,
    /// and a player who cannot press the lever must not be told what pressing it would cost. A
    /// competing overlay flag is exactly the drift <see cref="WorldUi"/> was created to end.</para>
    /// </summary>
    public override void _Process(double delta)
    {
        bool up = _wanted && !WorldUi.Suppressed;
        float target = up ? 1f : 0f;
        float rate = (float)(1.0 / (up ? FadeInSec : FadeOutSec));
        _alpha = Mathf.MoveToward(_alpha, target, (float)delta * rate);

        // ONE WRITER FOR VISIBILITY, AND IT IS THE FADE. This used to be raised in Set() and
        // lowered here, and that cost four headed captures: a warning raised while
        // WorldUi.Suppressed was still true faded to zero and hid itself, and when the suppression
        // lifted, Set() had already latched _wanted and early-returned, so nothing ever set
        // Visible back. The plate then sat at alpha 1, "wanted", laid out at the right rect,
        // reporting layerVisible=True — and invisible, which is the worst shape a defect can take
        // because every instrument says it is fine. Derived state belongs to the thing that
        // derives it.
        _root.Visible = _alpha > 0f;
        _root.Modulate = new Color(1, 1, 1, _alpha);

        if (!Mathf.IsEqualApprox(_alpha, target))
            return;
        if (_alpha > 0f)
            ReportGeometryOnce();
        // Settled. Keep ticking for as long as the plate is wanted — suppression can arrive at any
        // moment, WorldUi has no signal to subscribe to, and a widget that stopped looking would
        // draw straight over the pause menu. The cost is one comparison per frame while a player
        // is standing at a lever, which is not a budget anybody has to defend.
        SetProcess(_wanted);
    }

    /// <summary>
    /// Once per raise, print where the plate actually landed and how much of the frame it took.
    ///
    /// <para>This is not a trace, it is the control the capture harness gates on and the answer to
    /// the two questions a screenshot cannot settle by itself: <b>is it on screen at all</b> (a
    /// plate that fails to lay out is invisible and prints nothing anyone would notice), and
    /// <b>does it stay under the aim point</b> — <see cref="UiCoverageLaw"/>'s hard veto, measured
    /// against the real rect rather than argued from the anchor it was authored with.</para>
    /// </summary>
    private void ReportGeometryOnce()
    {
        if (_reported)
            return;
        _reported = true;
        Vector2 vp = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        Rect2 rect = Plate.GetGlobalRect();
        if (vp.X <= 0f || vp.Y <= 0f)
            return;
        (float covered, bool coversAim) = UiCoverageLaw.Measure(vp, new[] { rect });
        GD.Print($"[ui] consequence warning up: rect={rect} viewport={vp} "
                 + $"covered={covered * 100f:F1}% coversAim={coversAim} "
                 + $"layer={Layer} drawn={Plate.IsVisibleInTree()} alpha={_root.Modulate.A:F2}");
        if (coversAim || covered > UiCoverageLaw.MaxCoveredFraction)
        {
            GD.PushWarning("[ui] the consequence warning is breaking UiCoverageLaw — it plays while "
                           + "the player still has the camera and the stick, so it may neither cover "
                           + "the aim point nor paint more than a quarter of the frame.");
        }
    }

    private bool _reported;

    private void ApplyInk()
    {
        UiTokens t = UiThemeService.Tokens;
        _heading.AddThemeColorOverride("font_color", _urgent ? t.InkDanger : t.InkRank1);
        _body.AddThemeColorOverride("font_color", _urgent ? t.InkDanger : t.InkRank2);
        _foot.AddThemeColorOverride("font_color", t.InkRank3);
    }

    /// <summary>Centring is the label's own alignment rather than a CenterContainer per line — one
    /// node fewer each, and the plate keeps sizing to its longest line.</summary>
    private static Label Centred(Label label)
    {
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }
}
