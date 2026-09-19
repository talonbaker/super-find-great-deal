using Godot;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// <b>The bubble glyph, and the ripple that runs through it when the group total moves.</b>
///
/// <para><b>The artwork is supplied, not drawn.</b> <c>assets/ui/hud/bubble_icon.png</c> is
/// Talon's own 256 × 256 painting of a soap bubble, imported with mipmaps on because it is drawn
/// here at 40 px and a 6.4× minification without them aliases into a sparkling mess. Nothing in
/// this file draws a circle or a highlight; it draws that texture.</para>
///
/// <para><b>Why a custom draw and not a <c>TextureRect</c>.</b> Talon, ruling on the pop
/// reaction: <i>"a rotating specular highlight reads as a turning sticker — drift the iridescent
/// bands and hue, keep the highlight anchored upper-left. The wobble is a low-frequency UV
/// domain-warp (a true ripple), not a scale pulse."</i> A scale pulse is what a <c>TextureRect</c>
/// can do, and it is the thing that was refused: scaling a HUD element also re-flows the row it
/// sits in, and at 40 px it reads as the icon jumping rather than as the surface moving. A domain
/// warp needs per-vertex UVs, so this control emits its own triangulated quad grid and moves the
/// UVs while the vertices stay exactly where the layout put them. <b>The control's footprint
/// never changes</b>, so nothing beside it moves and the readout cannot bounce.</para>
///
/// <para><b>The highlight stays put</b> because it is painted into the texture and the warp is a
/// small, radially symmetric, decaying displacement that returns to identity — at rest the UVs
/// are the identity map and the icon is pixel-for-pixel the artwork.</para>
///
/// <para><b>The resting shimmer is BT-9's.</b> <c>resources/materials/ui/iridescent_thread.tres</c>
/// carries the house iridescence — the same three-cosine thin-film maths as the in-world bubble
/// film, so the HUD glyph and the things it counts are visibly the same material. That material's
/// header names this control as one of its two authorised placements and tells it to set
/// <c>band_axis</c> to a diagonal for a small round glyph. Two further uniforms are set here and
/// the reasons are in <see cref="BuildMaterial"/>. If the material is missing the icon draws
/// plain, which is the correct degradation for decoration.</para>
/// </summary>
public partial class BubbleIcon : Control
{
    private const string IconPath = "res://assets/ui/hud/bubble_icon.png";
    private const string ThreadMaterialPath = "res://resources/materials/ui/iridescent_thread.tres";

    /// <summary>Quads per side. Four is the smallest grid that can carry a wave with a visible
    /// crest and trough across 40 px; more subdivisions buy detail the glyph is too small to
    /// show and cost draw calls on a per-frame redraw.</summary>
    private const int Grid = 4;

    /// <summary>Peak UV displacement, in texture units. 0.045 of a 256 px texture is ~11 px of
    /// source moving under ~9 px of screen — enough that the surface visibly deforms, small
    /// enough that the highlight does not detach from its corner.</summary>
    private const float WarpUv = 0.045f;

    /// <summary>Crests across the radius. 1.6 puts a single trough inside the glyph with the
    /// crest arriving at the rim, which is what reads as one ripple rather than as boiling.</summary>
    private const float Waves = 1.6f;

    /// <summary>How far the wave travels outward over the envelope, in wave cycles. Below ~1 the
    /// ripple looks like a wobble in place; above ~2 it reads as vibration.</summary>
    private const float TravelCycles = 1.35f;

    private Texture2D? _texture;
    private float _amp;
    private double _elapsed;
    private bool _rippling;

    /// <summary>True while a ripple is in flight. The caller coalesces on this: six pops in one
    /// tenth of a second nudge the icon once.</summary>
    public bool Rippling => _rippling;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _texture = GD.Load<Texture2D>(IconPath);
        if (_texture == null)
            GD.PushWarning($"[hud] bubble icon missing at {IconPath} — the tally has no glyph.");
        Material = BuildMaterial();
        QueueRedraw();
    }

    /// <summary>
    /// Start one ripple, or do nothing if one is already running.
    ///
    /// <para><b>The coalescing rule, and it is a design decision rather than a guard.</b> Talon:
    /// <i>"Six players popping at once nudges the icon once; it never queues or jackhammers."</i>
    /// Restarting a live ripple would produce exactly the jackhammer — the envelope would keep
    /// being kicked back to full while the wave never finishes crossing the glyph. Dropping the
    /// extra triggers is right because the icon is not counting: the NUMBER carries how many, and
    /// it counts every one of them.</para>
    /// </summary>
    public void Pop()
    {
        if (_rippling)
            return;
        _rippling = true;
        _elapsed = 0;
        HudMotion.Wobble(this, amp =>
        {
            _amp = amp;
            if (amp <= 0f)
                _rippling = false;
            // The envelope drives the phase too, so a frozen tween (reduced motion) leaves the
            // wave at its rest position instead of at whatever phase it stopped on.
            _elapsed = (1f - amp) * HudMotion.WobbleDurationSec;
            QueueRedraw();
        });
        if (!_rippling)
            QueueRedraw(); // reduced motion: one redraw of the undeformed glyph, then nothing.
    }

    public override void _Draw()
    {
        if (_texture == null)
            return;

        Vector2 size = Size;
        var colours = new[] { Colors.White, Colors.White, Colors.White, Colors.White };
        float phase = (float)(_elapsed / HudMotion.WobbleDurationSec) * TravelCycles;

        // One DrawPolygon per cell rather than one call for the whole grid: DrawPolygon
        // triangulates a CONVEX outline, so a grid handed to it as a single polygon would be
        // triangulated across its own interior and the UVs would shear. Sixteen quads is sixteen
        // commands on a control that only redraws while a ripple is live.
        for (int y = 0; y < Grid; y++)
        {
            for (int x = 0; x < Grid; x++)
            {
                var uv = new Vector2[4];
                var pts = new Vector2[4];
                for (int c = 0; c < 4; c++)
                {
                    int cx = x + (c == 1 || c == 2 ? 1 : 0);
                    int cy = y + (c >= 2 ? 1 : 0);
                    var flat = new Vector2((float)cx / Grid, (float)cy / Grid);
                    pts[c] = flat * size;          // the layout's geometry, never touched
                    uv[c] = Warp(flat, phase);     // the domain warp
                }
                DrawPolygon(pts, colours, uv, _texture);
            }
        }
    }

    /// <summary>Displaces a sample point radially by a decaying travelling wave. Returns the
    /// identity map at amplitude 0, which is what makes "reduced motion" and "at rest" the same
    /// code path rather than two.</summary>
    private Vector2 Warp(Vector2 uv, float phase)
    {
        if (_amp <= 0f)
            return uv;
        Vector2 d = uv - new Vector2(0.5f, 0.5f);
        float r = d.Length();
        if (r < 0.0001f)
            return uv;
        float w = _amp * WarpUv * Mathf.Sin(Mathf.Tau * (r * Waves - phase));
        return uv + (d / r) * w;
    }

    /// <summary>
    /// A private duplicate of BT-9's thread material, so the two uniforms this placement needs
    /// are set on this icon and not on the shared resource every other user would inherit.
    ///
    /// <para><b><c>band_axis</c> is BT-9's own instruction</b> for this placement: a diagonal
    /// band reads better than either axis inside a small round glyph.</para>
    ///
    /// <para><b><c>alpha</c> goes to 1.0, and that is a deliberate departure</b> from the
    /// material's "no overrides" note. Its 0.55 was tuned for a 2–3 px decorative rule under a
    /// title-screen wordmark, where being half-there is the point. Here the same number would
    /// make the icon the faintest thing in a readout whose entire job is legibility, over a live
    /// 3D world rather than a dark title plate. The icon keeps its own painted alpha instead, and
    /// the thread stays a sheen because <c>saturation</c> — the knob that actually decides sheen
    /// versus rainbow — is left exactly where BT-9 tuned it.</para>
    ///
    /// <para><b><c>value</c> goes to 1.0</b> for the same reason: the shader multiplies into the
    /// sampled texel, so BT-9's 0.85 would darken supplied artwork by 15% on top of tinting it.
    /// </para>
    ///
    /// <para><b>Reduced motion freezes the drift</b> to <c>drift_speed = 0</c> — the material's
    /// own documented accessible state, and the one that also makes a capture reproducible. Read
    /// once at build: a preference change takes effect on the next HUD, which is the same
    /// contract <see cref="HudSettings.SetReducedMotion"/> already documents.</para>
    /// </summary>
    private static ShaderMaterial? BuildMaterial()
    {
        var shared = GD.Load<ShaderMaterial>(ThreadMaterialPath);
        if (shared == null)
        {
            GD.PushWarning($"[hud] {ThreadMaterialPath} missing — the bubble icon draws plain.");
            return null;
        }
        var mine = (ShaderMaterial)shared.Duplicate();
        mine.SetShaderParameter("band_axis", new Vector2(0.7f, 0.7f));
        mine.SetShaderParameter("alpha", 1.0f);
        mine.SetShaderParameter("value", 1.0f);
        if (HudSettings.ReducedMotion)
            mine.SetShaderParameter("drift_speed", 0.0f);
        return mine;
    }
}
