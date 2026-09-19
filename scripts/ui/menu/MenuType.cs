using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Ui.Menu;

/// <summary>
/// The title screen's typefaces, cut once and shared.
///
/// <para><b>The face comes from the token layer, the sizes do not.</b> <see cref="UiTypeface"/> is
/// where the game's typeface lives and this screen reads it rather than naming a font, so the day
/// the real display face lands the title re-sets itself with everything else. What this screen does
/// NOT take from <see cref="UiScale"/> is the size ladder: that ladder tops out at 40 px for a HUD
/// heading, and a title wordmark is display type at roughly twice it. Borrowing the name and
/// stretching the value would have made the ladder a lie for every other screen.</para>
///
/// <para><b>Letter-spacing is not decoration here.</b> The wordmark came down from 96 px to 78 to
/// stop it outweighing the bubble, and tracking is what buys the presence back — a wide, quiet line
/// reads as a title where a big, loud one reads as a shout. Godot expresses it as a glyph spacing
/// on a <see cref="FontVariation"/>, which is why every face on this screen is a variation rather
/// than a bare <see cref="Font"/>.</para>
/// </summary>
public static class MenuType
{
    /// <summary>One cut of the sans face at a given weight and tracking. Cheap enough to call per
    /// control — a <see cref="FontVariation"/> is a thin wrapper over the shared base font, not a
    /// second copy of it.</summary>
    public static FontVariation Face(int weight, int tracking)
    {
        var face = new FontVariation
        {
            VariationOpentype = new Godot.Collections.Dictionary { { "wght", weight } },
        };
        if (ResourceLoader.Exists(UiTypeface.SansPath))
            face.BaseFont = GD.Load<Font>(UiTypeface.SansPath);
        if (tracking != 0)
            face.SetSpacing(TextServer.SpacingType.Glyph, tracking);
        return face;
    }

    /// <summary>The wordmark's cut, already fitted to its column.
    ///
    /// <para><b>Fitted, and only ever downward.</b> The agreed size is 78 px in the 1600-wide design
    /// frame, but the shipped typeface is not the condensed face the previz stood in with, so the
    /// same nominal size can run wider. The approved frame's wordmark occupies 615 of 1600 px, and a
    /// display line that overruns its column — colliding with the bubble that is supposed to be the
    /// subject — is a worse error than one a couple of points under its nominal size. It is never
    /// scaled UP: 78 is a ceiling that came down from 96 for a reason.</para></summary>
    public static (FontVariation Face, int Size) Wordmark(string text)
    {
        int size = MenuLook.WordmarkSize;
        FontVariation face = Face(UiScale.WeightDisplay, MenuLook.WordmarkTracking);
        float target = MenuLook.DesignWidth * MenuLook.WordmarkTargetWidth;

        float width = face.GetStringSize(text, HorizontalAlignment.Left, -1f, size).X;
        if (width > target && width > 1f)
            size = Mathf.Max(24, Mathf.FloorToInt(size * target / width));

        return (face, size);
    }
}
