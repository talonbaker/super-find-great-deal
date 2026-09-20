using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Ui.Menu;

/// <summary>
/// <b>A secondary destination, at text level.</b> SETTINGS and QUIT beside the primary action; HOST,
/// JOIN and HOW TO PLAY once the player is past the title.
///
/// <para><b>Why these are not buttons.</b> One screen may have exactly one primary action, or it
/// has none — five equally-weighted plates is the shape the old menu had and it is the reason
/// nothing on it looked like the thing to press. Rank here is carried by weight and colour, not by
/// a second chrome treatment, which is the same order the rest of this interface uses.</para>
///
/// <para><b>They are still controls, and they are held to a control's standards.</b> All five
/// states exist; the hit target is a full <see cref="UiScale.TargetHeight"/> tall even though the
/// ink is 19 px, because a stick cannot reliably land on a line of text; hover is signalled by a
/// rule under the word as well as by colour, so it does not depend on colour perception alone; and
/// focus gets the same two-tone ring the primary action gets, because a focus indicator that
/// changes appearance between controls is a focus indicator the player has to learn twice.</para>
///
/// <para><b>Their ink is at the TOP of the ramp, not a muted grey-blue, and that is a measured
/// change from the previz — twice, in opposite directions.</b> MENU-1 measured the light previz's
/// muted tone at <b>2.27:1</b> on the field these items actually sit on and took the ink to the
/// bottom of the ramp. MENU-2 measured the NIGHT previz's muted tone — (150,156,170) — on the same
/// field, now dark, at <b>2.94:1</b>, and took the ink to the top. Same failure, same fix, opposite
/// end of the ramp, because this row runs out to x 700 and the scrim's reach ends at 0.52 of the
/// width, so these items sit on unscrimmed blurred blocks either way. Rank is carried here by size
/// and weight instead: 19 px at weight 600 against the wordmark's 78 px at weight 800, over a mark
/// area small enough that it cannot compete with the bubble.</para>
/// </summary>
public partial class MenuTextLink : BaseButton
{
    private string _label = "";
    private FontVariation _face = null!;
    private float _pad;

    /// <summary>Set to render one state regardless of input, for the capture sheet.</summary>
    public UiState? ForcedState { get; set; }

    public MenuTextLink() { }

    public MenuTextLink(string label)
    {
        _label = label;
    }

    public override void _Ready()
    {
        _face = MenuType.Face(UiScale.WeightMedium, MenuLook.SecondaryTracking);
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;

        _pad = MenuLook.SpaceS;
        float width = _face.GetStringSize(_label, HorizontalAlignment.Left, -1f, MenuLook.SecondarySize).X;
        CustomMinimumSize = new Vector2(width + _pad * 2f, UiScale.TargetHeight);
        Size = CustomMinimumSize;

        MouseEntered += QueueRedraw;
        MouseExited += QueueRedraw;
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
        ButtonDown += QueueRedraw;
        ButtonUp += QueueRedraw;
    }

    public string Label => _label;

    public UiState State
    {
        get
        {
            if (ForcedState.HasValue)
                return ForcedState.Value;
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

    public override void _Draw()
    {
        UiState state = State;
        float w = Size.X;
        float h = Size.Y;

        if (state == UiState.Focus)
            DrawFocusRing(w, h);

        // ONE ink for every live state, and the states are told apart by SHAPE. That is deliberate:
        // these items sit past the scrim's horizontal reach, on blurred blocks, and any ink dim
        // enough to leave headroom for a "brighter on hover" step fails AA at rest. So the ink sits
        // at the top of the ramp where it measures 7.7:1 or better everywhere along the row (read
        // off the rendered frame, MENU-2), and hover, press and focus each get their own
        // unmistakable non-colour marker.
        Color ink = state == UiState.Disabled ? MenuLook.InkDisabled : MenuLook.InkStrong;
        float offset = state == UiState.Pressed ? 2f : 0f;

        int size = MenuLook.SecondarySize;
        float baseline = offset + (h + _face.GetAscent(size) - _face.GetDescent(size)) * 0.5f;
        DrawString(_face, new Vector2(_pad, baseline), _label, HorizontalAlignment.Left, -1f, size, ink);

        // The hover/press marker: a rule under the word. This is the ONLY channel separating them
        // from rest, so it is 3 px rather than a hairline — and it is a shape change, which is what
        // a text link's hover is everywhere and what a player who does not separate two blues can
        // still see.
        if (state is UiState.Hover or UiState.Pressed)
        {
            float textWidth = _face.GetStringSize(_label, HorizontalAlignment.Left, -1f, size).X;
            DrawRect(new Rect2(_pad, baseline + offset + 5f, textWidth, 3f), ink);
        }
    }

    /// <summary>The SAME two-tone ring the primary action gets, drawn by the same code.
    ///
    /// <para>Deliberately not a lighter-weight variant. A focus indicator that changes appearance
    /// between controls on one screen is an indicator the player has to learn twice, and the
    /// controller-first case is exactly the one that cannot afford that. The only difference is that
    /// a text link has no fill of its own, so the light inner tone lands on the picture — which is
    /// why the DARK tone stays outermost here too.</para></summary>
    private void DrawFocusRing(float w, float h) => MenuActionButton.DrawRingPair(
        this, w, h, MenuLook.FocusRingOffset - 3, MenuLook.FocusRingInnerSplit - 3, MenuLook.ButtonRadius);
}
