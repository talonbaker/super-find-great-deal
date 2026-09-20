using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Ui.Menu;

/// <summary>
/// <b>The title screen's primary action.</b> Talon, 2026-08-28: "the start here that little UI
/// button is also terrible so please change this using best practices for UX/UI design."
///
/// <para><b>The fault was not aesthetic, and this is what was actually wrong.</b></para>
/// <list type="number">
/// <item><b>It wore a permanent ember ring at rest.</b> So the resting state already looked
/// focused, and there was nothing left to express real focus with. In a controller-first game the
/// focus ring IS the cursor — spending it on decoration leaves keyboard and pad players with no
/// cursor at all.</item>
/// <item><b>The accent was on decoration instead of on the action.</b> Here the accent is the
/// FILL and the ring is reserved for focus alone. <b>MENU-2, 2026-08-29: that fill is no longer
/// ember.</b> Talon: <i>"I don't like the orange bar that is the button for the host game simply
/// please remove this orange it clashes with the game aesthetic overall."</i> It is now
/// <see cref="MenuLook.Accent"/> — moonlight, from the frame's own sky family — with a deep night
/// label at a measured <b>8.21:1</b>, against the ember pair's 5.67:1. Removing the colour he
/// objected to cost the button nothing in legibility.</item>
/// <item><b>A translucent white fill made the label's contrast depend on the backdrop.</b> A
/// legibility figure that changes with whatever drifts behind it is not a figure. The fill is
/// opaque, so the ratio is a property of the button.</item>
/// <item><b>"PRESS START" is an instruction, not a label</b> — and a meaningless one on mouse.
/// The label is PLAY.</item>
/// <item><b>Exactly one state existed.</b> All five are here, and they are visually distinct:
/// rest, hover, pressed, focus, disabled.</item>
/// </list>
///
/// <para><b>Why a <see cref="BaseButton"/> drawn by hand rather than a themed <c>Button</c>.</b>
/// The focus ring has to sit 8 px OUTSIDE the target and be two-toned — on this night frame, a
/// LIGHT ring outside a dark one — because a single-colour ring can hold 3:1 against the accent
/// plate or against the night behind it but not against both. Godot's theme gives one style box
/// per state, which cannot express two concentric rings outside the control's own rect. Everything
/// that matters is still Godot's: <see cref="BaseButton"/> owns hover, press, focus and disabled,
/// so mouse, keyboard and pad cannot disagree about which of them is true.</para>
///
/// <para><b>The iridescent thread deliberately does not come near this control.</b> BT-9's rule:
/// the thread touches non-interactive identity elements only — never a control, never a state,
/// never a focus indicator. A drifting hue cannot hold a fixed contrast ratio, and a focus ring
/// cannot be turned off for reduced motion.</para>
/// </summary>
public partial class MenuActionButton : BaseButton
{
    private string _label = "PLAY";
    private FontVariation _face = null!;

    /// <summary>Where the fill currently is, as opposed to where the state says it should be. The
    /// gap between them is the 120 ms the house uses for a state answering the pointer.</summary>
    private Color _fill;
    private Color _targetFill;
    private float _offset;
    private float _targetOffset;

    private UiState? _forcedState;

    /// <summary>Set to render one state regardless of input, for the capture sheet. A button with
    /// one state is not a built button, and the only honest way to show five is to shoot five.
    /// Null means "whatever is actually true", which is what a player ever sees.
    ///
    /// <para>The setter re-resolves. Writing this as an auto-property is a real defect and it was
    /// caught by measuring the capture sheet rather than by looking at it: every forced state
    /// rendered the REST fill, because the fill is resolved in <c>Restate</c> and nothing called it.
    /// Four of the five rows of the state sheet were the same picture and the eye did not notice —
    /// the pixel measurement did.</para></summary>
    public UiState? ForcedState
    {
        get => _forcedState;
        set
        {
            _forcedState = value;
            Restate();
        }
    }

    /// <summary>Whether state changes cross-fade or snap. Reduced motion snaps — the END state
    /// arrives instantly and identically, which is the contract <c>HudSettings.SetReducedMotion</c>
    /// already documents: the information never changes, only the movement is dropped.</summary>
    public bool ReducedMotion { get; set; }

    public MenuActionButton() { }

    public MenuActionButton(string label)
    {
        _label = label;
    }

    public string Label
    {
        get => _label;
        set { _label = value; QueueRedraw(); }
    }

    public override void _Ready()
    {
        _face = MenuType.Face(UiScale.WeightStrong, MenuLook.ButtonLabelTracking);
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(MenuLook.ButtonWidth, MenuLook.ButtonHeight);
        Size = CustomMinimumSize;

        _fill = _targetFill = MenuLook.Accent;

        MouseEntered += Restate;
        MouseExited += Restate;
        FocusEntered += Restate;
        FocusExited += Restate;
        ButtonDown += Restate;
        ButtonUp += Restate;
        Restate();
    }

    /// <summary>The five states, resolved in one place so none of them can be the one nobody
    /// wrote. Order matters: disabled outranks everything, and a held press outranks focus.</summary>
    public UiState State
    {
        get
        {
            if (_forcedState.HasValue)
                return _forcedState.Value;
            if (Disabled)
                return UiState.Disabled;
            if (IsPressed())
                return UiState.Pressed;
            if (HasFocus())
                return UiState.Focus;
            if (IsHovered())
                return UiState.Hover;
            return UiState.Normal;
        }
    }

    private void Restate()
    {
        UiState state = State;
        _targetFill = state switch
        {
            UiState.Hover => MenuLook.AccentHover,
            UiState.Pressed => MenuLook.AccentPressed,
            UiState.Disabled => MenuLook.DisabledFill,
            // FOCUS DOES NOT CHANGE THE FILL. The ring is the whole focus signal, and moving the
            // fill as well would make focus and hover argue about which one the player is seeing.
            _ => MenuLook.Accent,
        };
        _targetOffset = state == UiState.Pressed ? 2f : 0f;

        if (ReducedMotion)
        {
            _fill = _targetFill;
            _offset = _targetOffset;
        }
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // A framerate-independent exponential approach to the target, settling inside the house's
        // 120 ms "instant".
        float k = 1f - Mathf.Exp(-(float)delta / (float)(UiScale.MotionInstant / 3.0));
        var next = new Color(
            Mathf.Lerp(_fill.R, _targetFill.R, k),
            Mathf.Lerp(_fill.G, _targetFill.G, k),
            Mathf.Lerp(_fill.B, _targetFill.B, k));
        float nextOffset = Mathf.Lerp(_offset, _targetOffset, k);

        bool settled = next.IsEqualApprox(_targetFill) && Mathf.IsEqualApprox(nextOffset, _targetOffset, 0.01f);
        _fill = settled ? _targetFill : next;
        _offset = settled ? _targetOffset : nextOffset;
        QueueRedraw();
        if (settled)
            SetProcess(false);
    }

    public override void _Draw()
    {
        UiState state = State;
        float w = Size.X;
        float h = Size.Y;

        if (state == UiState.Focus)
            DrawFocusRing(w, h);

        var body = new StyleBoxFlat { BgColor = _fill };
        body.SetCornerRadiusAll(MenuLook.ButtonRadius);
        DrawStyleBox(body, new Rect2(0f, _offset, w, h));

        Color ink = state == UiState.Disabled ? MenuLook.DisabledInk : MenuLook.OnAccent;
        int size = MenuLook.ButtonLabelSize;
        float baseline = _offset + (h + _face.GetAscent(size) - _face.GetDescent(size)) * 0.5f;
        DrawString(_face, new Vector2(0f, baseline), _label, HorizontalAlignment.Center, w, size, ink);
    }

    /// <summary>Two concentric rounded rings, 8 px outside the target.
    ///
    /// <para><b>Two tones, and the ORDER is the whole point.</b> A focus indicator owes 3:1 against
    /// the colours it actually touches — which here are two very different things: the night frame
    /// on the outside and a pale accent plate on the inside. One colour cannot hold both. So on
    /// this frame the LIGHT tone goes outermost, where its neighbour is the dark picture, and the
    /// DARK tone goes inside, where its neighbour is the moonlit plate. Measured: <b>12.31:1</b>
    /// outward, <b>8.31:1</b> inward.</para>
    ///
    /// <para><b>MENU-1 shipped this pair the other way round, and it was right to.</b> On the light
    /// frame the reflex order — a white halo outside a dark ring — measured 1.67:1 and 2.42:1, a
    /// double failure that looked completely fine, and the swapped order took it to 7.9:1 and
    /// 5.45:1. That note ended with "the reflex is correct only on a DARK interface", and MENU-2
    /// made this a dark interface, so the tones swapped back. <b>The lesson that survives both
    /// directions: this pair is a function of the frame's value, and it does not survive a palette
    /// change unmeasured.</b> The code never changed — only two constants did, which is what
    /// putting the order in <see cref="MenuLook"/> bought.</para>
    ///
    /// <para>It is deliberately thicker than any border elsewhere in the interface — focus has to
    /// survive squint distance on a television.</para></summary>
    internal static void DrawRingPair(Control on, float w, float h, float outer, float split, int radius)
    {
        // Outermost: on this night frame, the LIGHT tone — its neighbour is the dark picture.
        var outerRing = new StyleBoxFlat
        {
            BgColor = Colors.Transparent,
            DrawCenter = false,
            BorderColor = MenuLook.FocusRingOuter,
        };
        outerRing.SetBorderWidthAll(Mathf.RoundToInt(outer - split));
        outerRing.SetCornerRadiusAll(radius + 4);
        on.DrawStyleBox(outerRing, new Rect2(-outer, -outer, w + outer * 2f, h + outer * 2f));

        // Inside it: the DARK tone, against the control's own pale fill.
        var innerRing = new StyleBoxFlat
        {
            BgColor = Colors.Transparent,
            DrawCenter = false,
            BorderColor = MenuLook.FocusRingInner,
        };
        innerRing.SetBorderWidthAll(Mathf.RoundToInt(split));
        innerRing.SetCornerRadiusAll(radius + 2);
        on.DrawStyleBox(innerRing, new Rect2(-split, -split, w + split * 2f, h + split * 2f));
    }

    private void DrawFocusRing(float w, float h) => DrawRingPair(
        this, w, h, MenuLook.FocusRingOffset, MenuLook.FocusRingInnerSplit, MenuLook.ButtonRadius);
}
