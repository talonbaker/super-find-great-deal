using Godot;

namespace MpFoundation.Ui;

/// <summary>
/// The prototype's pill toggle switch: a 38x22 rounded track (frost when off,
/// periwinkle accent when on) with a 16px dark knob that slides 3&#8596;19px. Built as
/// a <see cref="Button"/> in toggle mode so any existing CheckButton wiring
/// (<c>ButtonPressed</c> / <c>Toggled</c>) transfers unchanged. Colours are read
/// from the theme's Accent slots — one source (UiThemeFactory).
/// Interaction states: hover brightens the track, keyboard focus draws a 2px
/// accent ring, the knob's slide answers every press.
/// </summary>
public partial class PillToggle : Button
{
    private const float TrackW = 38f, TrackH = 22f, KnobSize = 16f;
    private const float KnobOff = 3f, KnobOn = 19f;

    private float _knobX = KnobOff;
    private Tween? _slide;

    public override void _Ready()
    {
        ToggleMode = true;
        CustomMinimumSize = new Vector2(TrackW, TrackH);
        // The track/knob IS the widget — suppress the themed text-button chrome.
        var empty = new StyleBoxEmpty();
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            AddThemeStyleboxOverride(state, empty);

        _knobX = ButtonPressed ? KnobOn : KnobOff;
        Toggled += OnToggledVisual;
        MouseEntered += QueueRedraw;
        MouseExited += QueueRedraw;
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
        Design.UiThemeService.BindRedraw(this);
    }

    private void OnToggledVisual(bool on)
    {
        _slide?.Kill();
        _slide = CreateTween();
        _slide.TweenMethod(Callable.From((float x) => { _knobX = x; QueueRedraw(); }),
                _knobX, on ? KnobOn : KnobOff, 0.15)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    public override void _Draw()
    {
        var track = new Rect2((Size.X - TrackW) / 2f, (Size.Y - TrackH) / 2f, TrackW, TrackH);
        Design.UiTokens tokens = Design.UiThemeService.Tokens;
        bool hot = IsHovered();

        // Disabled was the missing fifth state (token audit, finding 6): a disabled toggle drew
        // exactly like an enabled one, so a settings row that could not be changed looked like a
        // settings row that simply had not been. The inert fill and the inert knob are the same
        // tokens every other disabled control in the game uses.
        var box = new StyleBoxFlat
        {
            BgColor = Disabled
                ? tokens.DisabledFill
                : ButtonPressed
                    ? (hot ? tokens.AccentHi : tokens.Accent)
                    : new Color(tokens.InkRank1, hot ? 0.18f : 0.10f),
        };
        box.SetCornerRadiusAll((int)(TrackH / 2f));
        if (HasFocus() && !Disabled)
        {
            box.BorderColor = tokens.Focus;
            box.SetBorderWidthAll(Design.UiScale.BorderFocus);
        }
        else if (Disabled)
        {
            box.BorderColor = tokens.Hairline;
            box.SetBorderWidthAll(Design.UiScale.BorderHair);
        }
        DrawStyleBox(box, track);

        Color knob = Disabled ? tokens.InkDisabled : tokens.PageSheet;
        float r = KnobSize / 2f;
        DrawCircle(new Vector2(track.Position.X + _knobX + r, track.Position.Y + TrackH / 2f), r, knob);
    }
}
