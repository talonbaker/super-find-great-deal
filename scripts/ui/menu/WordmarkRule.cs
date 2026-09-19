using Godot;

namespace MpFoundation.Ui.Menu;

/// <summary>
/// <b>The iridescent thread's first authorised placement, finally built.</b> A 3 px rule under the
/// wordmark carrying a narrow, travelling slice of the thin-film spectrum.
///
/// <para>Talon, 2026-08-28: <i>"I would like iridencense in the menu as the 'accent' and you know
/// that needs to shimmer"</i>, then: <i>"make the rainbow effect underline more subtle ... use a
/// zoomed in piece of the spectrum and then use something to make that move along with the
/// bubble."</i></para>
///
/// <para><b>This is BT-9's material, not a second one.</b>
/// <c>resources/materials/ui/iridescent_thread.tres</c> shipped on 2026-08-27 naming two authorised
/// placements: the HUD's bubble icon fill, which was built, and this underline, which was aimed at
/// a <c>scenes/ui/TitleScreen.tscn</c> that does not exist anywhere in the repo — so the thread has
/// been half-landed for a day. The control duplicates the shared material and sets the underline's
/// own parameters on its copy, exactly as the HUD icon does; nothing is overridden in the
/// <c>.tres</c>, so neither placement can quietly restyle the other.</para>
///
/// <para><b>Why the thread may be here at all.</b> BT-9's rule is that it touches non-interactive
/// identity elements only — never a control, a state, or a focus indicator — and the reasoning is
/// engineering rather than taste: a drifting hue cannot hold a fixed contrast ratio, and something
/// that animates forever must be stoppable by a reduced-motion setting. A wordmark underline has
/// none of those obligations. It is decorative by definition, carries no state, is not a target,
/// appears exactly once, and sits on the identity element — which is precisely where a house thread
/// belongs.</para>
///
/// <para><b>"Move along with the bubble", literally.</b> <see cref="SetPhase"/> is fed from the same
/// number <see cref="MenuBackdrop"/> gives its bubbles, so the window of colour and its travelling
/// highlight turn over on the bubble's own clock rather than on a second one that happens to run at
/// a similar speed. Stopping that number stops both, which is the whole of this screen's
/// reduced-motion behaviour.</para>
/// </summary>
public partial class WordmarkRule : ColorRect
{
    private const string ThreadMaterialPath = "res://resources/materials/ui/iridescent_thread.tres";

    private ShaderMaterial? _thread;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Color = Colors.White;

        var shared = GD.Load<ShaderMaterial>(ThreadMaterialPath);
        if (shared == null)
        {
            // The correct degradation for decoration: the rule simply is not there. A missing
            // accent must never take the wordmark's layout with it.
            Visible = false;
            return;
        }

        _thread = (ShaderMaterial)shared.Duplicate();

        // A 58 nm window instead of the whole first-order band. The drift path the HUD icon uses
        // spreads a fixed FRACTION of the film thickness across the element, so a wide rule always
        // shows a wide sweep of hues and reads as a pride bar; naming an absolute window in
        // nanometres and walking it along the spectrum is what "a zoomed in piece" means.
        _thread.SetShaderParameter("span_nm", MenuLook.RuleSpanNm);

        // Base 0.30, rising to the agreed 0.64 where the travelling highlight is.
        _thread.SetShaderParameter("alpha", MenuLook.RuleBaseAlpha);
        _thread.SetShaderParameter("highlight_gain", MenuLook.RuleHighlightGain);

        // Pull toward WHITE rather than toward the band's own luminance. BT-9's default is correct
        // for the dark HUD it was written against — a grey point at the film's own brightness is
        // what stops low saturation looking washed out there. On a high-key frame the same choice
        // lands a mid-grey line; pulled toward white the rule stays pale and reads as a tint.
        _thread.SetShaderParameter("grey_point", 1.0f);
        _thread.SetShaderParameter("saturation", 0.62f);
        _thread.SetShaderParameter("value", 1.0f);

        // The same optical path the menu's bubbles use, so the rule is that film rather than
        // another colourful thing.
        _thread.SetShaderParameter("opd_scale", MenuLook.RuleOpdScale);

        Material = _thread;
        SetPhase(MenuLook.RestPhase);
    }

    /// <summary>Hands the rule the menu's shared phase. Holding it still is the reduced-motion
    /// state and the reproducible-capture state, and it is the same state — which is the point.</summary>
    public void SetPhase(float phase)
    {
        _thread?.SetShaderParameter("phase", phase);
    }
}
