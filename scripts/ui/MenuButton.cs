using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Ui;

/// <summary>
/// A text-driven menu button with an underline that grows on hover. The underline colour follows
/// the button's own themed hover colour, so a danger verb reveals in danger ink with no
/// per-button configuration. Focus is left to the theme's focus box, so keyboard nav reads as a
/// box and pointer hover as an underline. Remains a <see cref="Button"/> so existing wiring is
/// unchanged.
///
/// <para><b>It now checks whether it is wanted.</b> This script is attached to nearly every
/// button in the menu scenes, including the filled primary verbs — and a growing underline drawn
/// *inside* a solid ember pill is not an affordance, it is a stray line. Underlining is what a
/// button with no chrome of its own does to answer the pointer; a button that already answers
/// with a fill, a border and a lift does not need a second answer. So the underline is built only
/// for recipes that draw nothing at rest, and every other caller keeps the theme's hover
/// untouched.</para>
/// </summary>
public partial class MenuButton : Button
{
    private ColorRect? _underline;

    public override void _Ready()
    {
        if (!WantsUnderline())
            return;

        _underline = new ColorRect
        {
            Color = GetThemeColor("font_hover_color"),
            MouseFilter = MouseFilterEnum.Ignore,
            Size = new Vector2(0, UiScale.BorderHair),
        };
        AddChild(_underline);

        MouseEntered += () => Reveal(true);
        MouseExited += () => Reveal(false);

        // The hover colour is a token, so it moves at dusk like everything else.
        UiThemeService.Bind(this, _ =>
        {
            if (_underline != null)
                _underline.Color = GetThemeColor("font_hover_color");
        });
    }

    /// <summary>True only for a button whose resting style paints nothing — the text and ghost
    /// verbs. Asked of the resolved stylebox rather than of the variation name, so a recipe
    /// change that gives text buttons a fill silently retires the underline instead of leaving
    /// it drawing over the new one.</summary>
    private bool WantsUnderline() =>
        GetThemeStylebox("normal") is not StyleBoxFlat { DrawCenter: true };

    /// <summary>Grows (or retracts) the underline. Public so capture tooling can pose it.</summary>
    public void Reveal(bool on)
    {
        if (_underline == null)
            return;

        float inset = GetThemeStylebox("normal")?.ContentMarginLeft ?? UiScale.SpaceSnug;
        float full = Mathf.Max(0f, Size.X - 2f * inset);
        _underline.Position = new Vector2(inset, Size.Y - UiScale.SpaceTight - 1f);
        CreateTween()
            .TweenProperty(_underline, "size:x", on ? full : 0f, UiScale.MotionQuick)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }
}
