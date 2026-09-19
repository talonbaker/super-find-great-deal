using Godot;
using MpFoundation;
using MpFoundation.Ui.Hud;

namespace Sail.Game.Water;

/// <summary>
/// The cold's third channel, and the one this packet adds — 2026-08-08 playtest fallout, P2.
///
/// <b>Why a fourth file when the spec named three.</b> Spec §5.1 shipped Audio, Visual (frost)
/// and Haptic (rumble) as the redundant trio. In the field all three existed and Talon still
/// "did not realize that anything was happening... with or without audio." The haptic channel is
/// the tell: it is silent by construction on any session with no controller plugged in
/// (<c>Input.GetConnectedJoypads().Count == 0</c> in <see cref="ChillCueOverlay.UpdateRumble"/>),
/// so on keyboard-and-mouse it was never really a third channel — the redundant pair was audio
/// and frost, and both apparently failed to register at the onset threshold that shipped (see
/// <see cref="ChillClock.CueOnsetChill"/>'s own doc). The dispatch decision default for this
/// packet is explicit and supersedes §5.1's channel list: "a screen-space visual..., a
/// HUD/diegetic readout, and audio" — swapping the hardware-gated channel for one that is always
/// on screen. Rumble is not deleted; it still rides along in <see cref="ChillCueOverlay"/> as a
/// bonus fourth signal for players who do have a controller.
///
/// <b>Still "no HUD bar."</b> Spec §5.1's ban is on a <i>meter</i> — "the register does not carry
/// meters" — not on diegetic text. This is three worded stages, never a number, so a chill of
/// 0.41 and a chill of 0.49 render identically; nothing here would let a player min-max the
/// exact instant to get out.
///
/// <b>Client-local presentation, same shape as <see cref="ChillCueOverlay"/>.</b> Reads only
/// <see cref="WaterService"/>'s public surface, writes nothing, replicates nothing, no-ops
/// headless.
/// </summary>
public partial class ChillReadout : CanvasLayer
{
    /// <summary>Above <see cref="GameHud"/>-equivalent chrome (80) so it is never hidden behind a
    /// HUD panel, below <see cref="ChillCueOverlay"/>'s frost (94) and <c>BedSleepFade</c> (95) so
    /// both can still cover it.</summary>
    private const int LayerIndex = MpFoundation.Ui.Design.UiLayers.ChillReadout;

    private const float MidStage = 0.5f;
    private const float HighStage = 0.85f;

    private const float FadeInSec = 0.30f;
    private const float FadeOutSec = 0.70f;

    private static ChillReadout? _instance;

    private PanelContainer _panel = null!;
    private Label _label = null!;
    private float _alpha;
    private int _lastStage = -1;

    /// <summary>Attach to a scene root. No-op headless and no-op if one already exists — the same
    /// guard <see cref="ChillCueOverlay.Attach"/> uses.</summary>
    public static void Attach(Node sceneRoot)
    {
        if (NetworkManager.Instance is { IsHeadless: true })
            return;
        if (_instance != null && IsInstanceValid(_instance))
            return;
        sceneRoot.AddChild(new ChillReadout { Name = "ChillReadout" });
    }

    public override void _Ready()
    {
        _instance = this;
        Layer = LayerIndex;

        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", HudTheme.PanelStyle());
        _panel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Begin;
        _panel.Modulate = new Color(1, 1, 1, 0);
        AddChild(_panel);

        _label = HudTheme.MakeLabel("", HudTheme.RoleBody, HorizontalAlignment.Center);
        _panel.AddChild(_label);

        _panel.Resized += () => CentreBottom(_panel);
        CentreBottom(_panel);
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!float.IsFinite(dt) || dt <= 0f)
            return;

        float intensity = 0f;
        if (WaterService.Instance is { Synced: true } water)
        {
            int me = Multiplayer?.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
            intensity = ChillClock.CueIntensity(water.ChillOf(me));
        }

        int stage = StageOf(intensity);
        if (stage != _lastStage)
        {
            _label.Text = TextFor(stage);
            _panel.AddThemeStyleboxOverride("panel", HudTheme.PanelStyle(accented: stage == 3));
            CentreBottom(_panel);
            // Only flash on an ESCALATION (including the initial appearance at stage 0->1+), not
            // on the fade back to zero — a flash on the way out would read as a new event rather
            // than as relief.
            if (stage > _lastStage)
                HudMotion.FlashText(_label, HudTheme.AccentPrimary);
            _lastStage = stage;
        }

        float targetAlpha = stage > 0 ? 1f : 0f;
        float followRate = 1f / (targetAlpha > _alpha ? FadeInSec : FadeOutSec);
        _alpha = Mathf.MoveToward(_alpha, targetAlpha, dt * followRate);
        _panel.Modulate = new Color(1, 1, 1, _alpha);
    }

    /// <summary>Which of the three worded stages <paramref name="intensity"/> falls in, or 0 for
    /// hidden. Pure and static so <c>ChillReadoutTests</c> can assert the boundaries without a
    /// scene tree — the same seam <see cref="PhaseToastText"/> uses.</summary>
    public static int StageOf(float intensity)
    {
        if (!float.IsFinite(intensity) || intensity <= 0f)
            return 0;
        if (intensity < MidStage)
            return 1;
        if (intensity < HighStage)
            return 2;
        return 3;
    }

    /// <summary>Stage -&gt; the line on screen. Escalates in WORDING, never in a number — see the
    /// class doc's reconciliation with spec §5.1's "no meters" ban.</summary>
    public static string TextFor(int stage) => stage switch
    {
        1 => "The cold is creeping in.",
        2 => "You're getting seriously cold.",
        3 => "GET OUT OF THE WATER.",
        _ => "",
    };

    private static void CentreBottom(Control control)
    {
        float half = control.Size.X * 0.5f;
        control.OffsetLeft = -half;
        control.OffsetRight = half;
        control.OffsetBottom = -HudTheme.ScreenMargin * 3f;
        control.OffsetTop = control.OffsetBottom - control.Size.Y;
    }
}
