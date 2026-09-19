using Godot;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// A small drawn mouse with one button lit — the icon half of Talon's "a mouse icon to make it
/// clear that left click is to perform the action and right click is to ready the action".
///
/// <b>Why a picture and not just "LMB".</b> "LMB" is a piece of jargon a player has to already
/// know; a lit button on a mouse outline is the same information with nothing to learn, and it
/// survives being read at a glance from across the desk in the middle of a fight. A text label
/// always rides next to it — the icon is the redundant channel, not a replacement, which is the
/// same never-one-channel rule the rest of the HUD follows.
///
/// <b>Drawn, not an asset.</b> Six primitives; a sprite would mean a .png, an .import, and a
/// second thing to re-tint whenever the palette moves. It also scales to any HUD size without a
/// second resolution.
/// </summary>
public partial class MouseGlyph : Control
{
    public enum Button
    {
        Left,
        Right,
    }

    /// <summary>Icon footprint. Sized to sit on the same baseline as
    /// <see cref="HudTheme.SizeHint"/> text without pushing the line taller than the text alone
    /// would.</summary>
    public const float GlyphWidth = 13f;
    public const float GlyphHeight = 18f;

    private readonly Button _lit;

    public MouseGlyph(Button lit)
    {
        _lit = lit;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(GlyphWidth, GlyphHeight);
    }

    /// <summary>The glyph paints itself once and then only when something asks. Dusk is
    /// something asking: without this the mouse chrome would still be lit at noon after
    /// nightfall.</summary>
    public override void _Ready() => Design.UiThemeService.BindRedraw(this);

    public override void _Draw()
    {
        float w = GlyphWidth;
        float h = GlyphHeight;
        var body = new Color(HudTheme.TextMuted, 0.85f);
        // The lit button is full-contrast text colour against a dim body — fill versus empty is
        // the whole signal, so the lit half goes to the brightest tier rather than a tint.
        Color lit = HudTheme.TextPrimary;

        // Body: a rounded capsule. Drawn as the outline the two buttons sit inside. The scrim
        // interior is what keeps the glyph readable where it overlaps bright ground — the same
        // job the scrim does for the panels beside it.
        var outline = new StyleBoxFlat
        {
            BgColor = HudTheme.Scrim,
            BorderColor = body,
        };
        outline.SetCornerRadiusAll(6);
        outline.SetBorderWidthAll(1);
        DrawStyleBox(outline, new Rect2(0f, 0f, w, h));

        // The two buttons: the top third, split by the centre divider. Only the lit one is
        // filled — an unlit button stays as bare outline, so the difference is a fill and not
        // merely a brighter shade of the same shape (ART-BIBLE §3: separate in value, not hue).
        float buttonH = h * 0.38f;
        float mid = w * 0.5f;
        if (_lit == Button.Left)
            DrawRect(new Rect2(1f, 1f, mid - 1f, buttonH), lit);
        else
            DrawRect(new Rect2(mid, 1f, mid - 1f, buttonH), lit);

        // Divider between the buttons, and the line closing them off from the body — the two
        // marks that make the capsule read as a mouse rather than a pill.
        DrawLine(new Vector2(mid, 1f), new Vector2(mid, buttonH + 1f), body, 1f);
        DrawLine(new Vector2(1f, buttonH + 1f), new Vector2(w - 1f, buttonH + 1f), body, 1f);
    }
}
