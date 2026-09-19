using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Ui;

/// <summary>
/// <b>A control, drawn as the thing you press.</b> Talon, 2026-08-29 (note 2): <i>"Needs icons
/// for right mouse button, left mouse buttons, shift, etc. these should have keys, icons which
/// go with the keys."</i>
///
/// <para><b>Drawn, never sourced.</b> A keycap is a rounded rect with a lip and a label; a mouse
/// is a capsule with one region filled. Both are cheaper as six primitives than as a .png with
/// an .import sidecar, a second resolution and a re-tint every time the palette moves — and
/// <see cref="Hud.MouseGlyph"/> already proved the shape works at HUD scale. This is the same
/// idea rebuilt on <see cref="UiTokens"/> rather than <see cref="Hud.HudTheme"/>, because the
/// How To Play screen is menu chrome and has to match the title screen (note 5), not the HUD.</para>
///
/// <para><b>They scale with the type, because that is the other half of the note.</b> Every
/// dimension here is derived from the resolved font size of the type rank the icon was asked for
/// — <c>Body</c> unless the caller says otherwise — so when <see cref="UiScale.SizeBody"/> moved
/// 15 -> 17 the caps grew with it and will keep doing so, and a cap dropped into a caption line
/// comes out caption-sized rather than towering over it. Nothing here hardcodes a pixel height.</para>
///
/// <para><b>What is drawn is what is bound.</b> Neither class chooses a label: both are handed a
/// <see cref="ControlGlyphs.Binding"/> resolved live from the <see cref="InputMap"/>. A legend
/// that says Shift while the game reads something else is worse than no legend, so the only way
/// to get an icon on this screen is to have a binding produce one.</para>
/// </summary>
public static class ControlIcon
{
    /// <summary>The one place a binding becomes a widget. Keyboard bindings get a keycap; mouse
    /// bindings get a mouse with the pressed part lit; an unbound action gets a keycap reading
    /// "?" rather than nothing, because "bound to nothing" is information and a blank cell is
    /// not.</summary>
    public static Control For(ControlGlyphs.Binding binding, string rank = DefaultRank) => binding.Kind switch
    {
        ControlGlyphs.GlyphKind.MouseLeft => new MouseIcon(MouseIcon.Part.Left, rank),
        ControlGlyphs.GlyphKind.MouseRight => new MouseIcon(MouseIcon.Part.Right, rank),
        ControlGlyphs.GlyphKind.MouseMiddle => new MouseIcon(MouseIcon.Part.Wheel, rank),
        ControlGlyphs.GlyphKind.MouseWheel => new MouseIcon(MouseIcon.Part.Wheel, rank),
        _ => new KeycapIcon(binding.Label, rank),
    };

    /// <summary>The type rank a cap is sized against when the caller does not say. A cap in a
    /// sentence of body text is a body-sized cap.</summary>
    public const string DefaultRank = "Body";

    /// <summary>Cap height: the text of the given rank plus one snug step of padding above and
    /// below, so a cap reads as the same weight as the sentence beside it. Resolved from the
    /// control's own theme rather than from <see cref="UiScale"/> directly, so a variation
    /// override still tracks — and so a cap dropped into a Caption line comes out caption-sized
    /// instead of towering over it.</summary>
    internal static float CapHeight(Control host, string rank) =>
        host.GetThemeFontSize("font_size", rank) + (UiScale.SpaceSnug * 2);
}

/// <summary>One key, drawn as a keycap: a base plate with a lit top face sitting a couple of
/// pixels proud of it. The lip is what separates a key from a chip — without it the same
/// rounded rect reads as a tag.</summary>
public partial class KeycapIcon : Control
{
    /// <summary>How far the top face sits above the base. One tight step: enough to read as a
    /// physical edge at 1080p, small enough not to look like two stacked boxes at 720p.</summary>
    private const int LipSteps = 1;

    private readonly string _label;
    private readonly string _rank;
    private Font _font = null!;
    private int _fontSize;

    public KeycapIcon(string label, string rank = ControlIcon.DefaultRank)
    {
        _label = label;
        _rank = rank;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        _font = GetThemeFont("font", _rank);
        _fontSize = GetThemeFontSize("font_size", _rank);

        float height = ControlIcon.CapHeight(this, _rank);
        float textWidth = _font.GetStringSize(_label, HorizontalAlignment.Left, -1f, _fontSize).X;
        // Square unless the legend is wider than that: "W" and "1" stay square keys, "Space" and
        // "Shift" grow sideways the way real caps do. The floor is the height so a row of mixed
        // caps still reads as one keyboard.
        float width = Mathf.Max(height, textWidth + (UiScale.SpaceSnug * 2));
        CustomMinimumSize = new Vector2(Mathf.Ceil(width), Mathf.Ceil(height));

        UiThemeService.Bind(this, _ =>
        {
            _font = GetThemeFont("font", _rank);
            _fontSize = GetThemeFontSize("font_size", _rank);
            QueueRedraw();
        });
    }

    public override void _Draw()
    {
        UiTokens t = UiThemeService.Tokens;
        float lip = UiScale.SpaceTight * LipSteps;

        // Base plate: the shadowed side of the cap, and the only part that touches the row below.
        var basePlate = new StyleBoxFlat { BgColor = t.SurfaceChip, BorderColor = t.HairlineStrong };
        basePlate.SetCornerRadiusAll(UiScale.RadiusTight);
        basePlate.SetBorderWidthAll(UiScale.BorderHair);
        DrawStyleBox(basePlate, new Rect2(Vector2.Zero, Size));

        // Top face: inset on the sides, raised off the bottom. The gap at the bottom IS the lip.
        var face = new StyleBoxFlat { BgColor = t.SurfaceRaised, BorderColor = t.Hairline };
        face.SetCornerRadiusAll(UiScale.RadiusTight);
        face.SetBorderWidthAll(UiScale.BorderHair);
        DrawStyleBox(face, new Rect2(
            UiScale.BorderHair,
            UiScale.BorderHair,
            Size.X - (UiScale.BorderHair * 2),
            Size.Y - (UiScale.BorderHair * 2) - lip));

        // Legend, centred on the top face rather than on the whole cap — otherwise it sits low
        // and the lip reads as a shadow under the text instead of under the key.
        Vector2 text = _font.GetStringSize(_label, HorizontalAlignment.Left, -1f, _fontSize);
        var origin = new Vector2(
            (Size.X - text.X) * 0.5f,
            ((Size.Y - lip) * 0.5f) + (_font.GetAscent(_fontSize) - (text.Y * 0.5f)));
        DrawString(_font, origin, _label, HorizontalAlignment.Left, -1f, _fontSize, t.InkRank1);
    }
}

/// <summary>A mouse with one part lit. Fill versus outline is the whole signal — the lit part
/// goes to the brightest ink rather than to a tint, so the difference survives ART-BIBLE §3's
/// "separate in value, not hue" and does not spend the screen's one accent instance.</summary>
public partial class MouseIcon : Control
{
    public enum Part
    {
        Left,
        Right,
        Wheel,
    }

    /// <summary>A mouse is taller than it is wide. Width is a fraction of the shared cap height
    /// so an icon row keeps one baseline whatever the type scale is doing.</summary>
    private const float WidthRatio = 0.68f;

    /// <summary>Where the buttons stop and the body starts, as a fraction of total height.</summary>
    private const float ButtonBandRatio = 0.40f;

    private readonly Part _lit;
    private readonly string _rank;

    public MouseIcon(Part lit, string rank = ControlIcon.DefaultRank)
    {
        _lit = lit;
        _rank = rank;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        float height = ControlIcon.CapHeight(this, _rank);
        CustomMinimumSize = new Vector2(Mathf.Ceil(height * WidthRatio), Mathf.Ceil(height));
        UiThemeService.BindRedraw(this);
    }

    public override void _Draw()
    {
        UiTokens t = UiThemeService.Tokens;
        float w = Size.X;
        float h = Size.Y;
        float band = h * ButtonBandRatio;
        float mid = w * 0.5f;
        float wheelW = Mathf.Max(UiScale.BorderWeight, w * 0.16f);

        // Body: a capsule, drawn as the outline the buttons sit inside. Radius is half the width
        // so it stays a mouse silhouette at any type scale rather than a rounded rectangle.
        var body = new StyleBoxFlat { BgColor = t.SurfaceChip, BorderColor = t.HairlineStrong };
        body.SetCornerRadiusAll(Mathf.RoundToInt(w * 0.5f));
        body.SetBorderWidthAll(UiScale.BorderHair);
        DrawStyleBox(body, new Rect2(Vector2.Zero, Size));

        Color lit = t.InkRank1;
        float inset = UiScale.BorderHair;

        if (_lit == Part.Left)
            DrawRect(new Rect2(inset, inset, mid - inset, band - inset), lit);
        else if (_lit == Part.Right)
            DrawRect(new Rect2(mid, inset, mid - inset, band - inset), lit);

        // Divider between the buttons and the line closing them off from the body: the two marks
        // that make the capsule read as a mouse rather than a pill.
        DrawLine(new Vector2(mid, inset), new Vector2(mid, band), t.HairlineStrong, UiScale.BorderHair);
        DrawLine(new Vector2(inset, band), new Vector2(w - inset, band), t.HairlineStrong, UiScale.BorderHair);

        // The wheel straddles the divider. Lit for a scroll binding, outlined otherwise — it is
        // always drawn, because a mouse with no wheel reads as a two-button diagram and the
        // scroll rows need somewhere for the eye to land.
        var wheel = new Rect2(mid - (wheelW * 0.5f), band * 0.25f, wheelW, band * 0.9f);
        DrawRect(wheel, _lit == Part.Wheel ? lit : t.SurfaceRaised);
        DrawRect(wheel, t.HairlineStrong, filled: false, width: UiScale.BorderHair);
    }
}
