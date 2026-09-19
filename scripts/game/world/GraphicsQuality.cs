using Godot;

namespace MpFoundation.World;

/// <summary>
/// One place that decides how expensive the world is allowed to be.
///
/// <para><b>Why this exists at all.</b> The target is a locked 60 fps at 1080p on a
/// GTX 1650-laptop / GTX 970-desktop class card, Forward+, with 2-6 players. That card has
/// roughly an eighth of the shader throughput of the machine this system was developed on, so
/// "it runs fine here" is not evidence about it. The tiers below are therefore expressed as
/// ratios of work — instance counts, view distances, shader branches — not as a fps number,
/// because ratios are the part that transfers between cards and a fps number is not.</para>
///
/// <para><b>The rule this type enforces:</b> a tier must make the scene genuinely cheaper, not
/// merely smaller. Every dial here removes work — vertex invocations, texture fetches, or
/// shaded pixels. A dial that only changed a number without removing work would belong in a
/// settings menu, not in here.</para>
///
/// <para><b>The default is resolved by <see cref="MpFoundation.GraphicsSettings"/>, deliberately
/// (Issue #201):</b> a windowed player boots into the persisted tier or the ship default (High —
/// the comps set the shipped-quality floor; Medium remains the tier sized to hold 60 fps on the
/// floor card, one persisted settings click away), a headless process boots into Medium (the
/// baseline every historical headless measurement ran), and <c>--graphics</c> overrides both for
/// parity tests. Medium is sized for the floor card; High is the intended look; Low exists for
/// laptops below the floor rather than as the expected experience. Auto-detection is NOT
/// attempted — GPU capability cannot be inferred reliably from a device string, and guessing
/// wrong in the pessimistic direction makes a good machine look bad while guessing
/// optimistically makes a bad one unplayable.</para>
/// </summary>
public static class GraphicsQuality
{
    public enum Tier
    {
        Low,
        Medium,
        High,
    }

    /// <summary>The active tier. Set before the world scene is instantiated — GrassField reads
    /// it in <c>_Ready</c> and does not rebuild afterwards.
    ///
    /// <para>The initializer below is a last-resort fallback for code paths that never pass
    /// through <c>Boot._Ready</c> (dev labs instantiated directly); it is NOT the shipped
    /// default. This property had NO writer at all until Story #180 added <c>--graphics</c> and
    /// Issue #201 added the real one — every session before that silently ran this initializer,
    /// which is why tests/Run-GraphicsTierTest.ps1 now fails if boot stops assigning it
    /// deliberately. Do not "simplify" the boot path by deleting the assignment because the
    /// value happens to match.</para></summary>
    public static Tier Current { get; set; } = Tier.Medium;

    /// <summary>Grass field geometry for one tier. See GrassField's class doc for what the ring
    /// scheme is and why the cost is proportional to clump count rather than to triangles.</summary>
    public readonly struct GrassTier
    {
        public GrassTier(float spacing, float innerHalfExtent, int ringCount,
            float fadeStart, float fadeEnd)
        {
            Spacing = spacing;
            InnerHalfExtent = innerHalfExtent;
            RingCount = ringCount;
            FadeStart = fadeStart;
            FadeEnd = fadeEnd;
        }

        /// <summary>Metres between clumps in the innermost ring.</summary>
        public float Spacing { get; }

        /// <summary>Half-extent of the innermost ring in metres.</summary>
        public float InnerHalfExtent { get; }

        /// <summary>Nested rings. Reach = InnerHalfExtent * 2^(RingCount-1).</summary>
        public int RingCount { get; }

        /// <summary>Metres at which blades begin shrinking.</summary>
        public float FadeStart { get; }

        /// <summary>Metres at which blades are fully gone. Must be under the field's reach.</summary>
        public float FadeEnd { get; }
    }

    /// <summary>
    /// Grass geometry per tier. Clump counts, computed from the ring scheme:
    ///
    /// <code>
    ///   Low     2 rings, 0.34 m, inner 6 m    ~1,850 clumps    5,550 blades    22 k tris
    ///   Medium  3 rings, 0.28 m, inner 7 m    ~4,600 clumps   13,800 blades    55 k tris
    ///   High    4 rings, 0.24 m, inner 7 m    ~8,700 clumps   26,100 blades   104 k tris
    /// </code>
    ///
    /// <para>Against the 70,756 clumps / 212,268 blades / 849,072 triangles the uniform grid
    /// submitted unconditionally, Medium is a 15x cut in the quantity that was measured to cost
    /// the frame (clumps, hence vertex invocations, hence splat fetches).</para>
    ///
    /// <para>Fade distances are set INSIDE each tier's reach, not at it — a fade that runs to
    /// the field edge puts the pop it was supposed to hide right at the boundary.</para>
    /// </summary>
    /// <para>Each tier's fade ends WELL INSIDE its outermost ring rather than at the field edge.
    /// That outermost ring is the sparse one — spacing doubles per step — and a blade still at
    /// full height out there reads as an isolated spike rather than as grass. Headed capture
    /// showed exactly that at Medium's original 26 m fade against a 28 m reach; pulling the fade
    /// in to 20 m puts the sparse ring under the height and colour fade where it belongs, and
    /// hands the read to the ground shader's own grass layer, which is what it is for.</para>
    public static GrassTier Grass(Tier tier) => tier switch
    {
        Tier.Low => new GrassTier(0.34f, 6f, 2, 7f, 11f),
        Tier.High => new GrassTier(0.24f, 8f, 4, 18f, 34f),
        _ => new GrassTier(0.28f, 7f, 3, 11f, 20f),
    };

    /// <summary>
    /// Ground shader dials per tier. Both remove real per-fragment work:
    /// <list type="bullet">
    /// <item><c>detail_fade_start/end</c> — the distance past which detail normals and the
    /// second anti-tiling sample stop being taken. Pulling it in removes fetches AND removes
    /// shimmer, the rare case where cheap and clean want the same thing.</item>
    /// <item><c>macro_tile_blend</c> — at 0 the anti-tiling second sample compiles out
    /// entirely, halving the albedo fetches. Low pays visible tiling for that; it is the
    /// correct trade on a card that cannot afford the fetches.</item>
    /// </list>
    /// </summary>
    public static (float FadeStart, float FadeEnd, float MacroTileBlend) Ground(Tier tier) => tier switch
    {
        Tier.Low => (8f, 22f, 0f),
        Tier.High => (34f, 90f, 0.34f),
        _ => (20f, 55f, 0.26f),
    };

    /// <summary>
    /// Applies the tier's ground dials to the terrain material. Called by a world once the
    /// world is built; safe to call again if the tier changes at runtime, since these are plain
    /// shader parameters with no rebuild behind them.
    /// </summary>
    public static void ApplyGround(ShaderMaterial? groundMaterial, Tier tier)
    {
        if (groundMaterial is null)
        {
            return;
        }
        (float fadeStart, float fadeEnd, float macroTileBlend) = Ground(tier);
        groundMaterial.SetShaderParameter("detail_fade_start", fadeStart);
        groundMaterial.SetShaderParameter("detail_fade_end", fadeEnd);
        groundMaterial.SetShaderParameter("macro_tile_blend", macroTileBlend);
    }

    /// <summary>
    /// Applies the tier's grass dials to the grass material. The geometry half of the tier is
    /// read by GrassField itself via <c>GrassField.Tier</c> — it cannot be applied here
    /// because it changes what gets built, not what gets uniform-set.
    /// </summary>
    public static void ApplyGrass(ShaderMaterial? grassMaterial, Tier tier)
    {
        if (grassMaterial is null)
        {
            return;
        }
        GrassTier g = Grass(tier);
        grassMaterial.SetShaderParameter("fade_start", g.FadeStart);
        grassMaterial.SetShaderParameter("fade_end", g.FadeEnd);
    }
}
