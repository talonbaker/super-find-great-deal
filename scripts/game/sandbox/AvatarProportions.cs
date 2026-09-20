using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// Every dimension the rest of the game needs from the player's body, <b>derived from the
/// character that is actually on screen</b> instead of typed in once per system.
///
/// <para>WHY THIS TYPE EXISTS. Until 2026-08-08 the player was always one squat blob body,
/// and eight independent numbers were tuned by eye against it: a 0.9 m collision
/// capsule, a 0.78 m aim anchor, a +1.15 m nameplate, a 0.46 m blob shadow, two carry
/// anchors, a 0.7 m camera focus and a 1.6 m voice emitter. Making a taller whole figure the
/// player character did not update any of them, because none of them knew where they came from.
/// The roster came to hold two families whose crowns ranged 0.93–1.27 m and
/// whose widths ranged 0.40–1.01 m, so "one number per system" is no longer expressible: the
/// numbers have to be a function of the model, and this struct is that function.</para>
///
/// <para>THE CALIBRATION. Every fraction below is the shipped, playtested value for the
/// reference body divided by that body's OWN measured dimension. That is deliberate and it is
/// the whole reason this change is safe to make in one pass: feed the reference measurements
/// back in and each fraction reproduces the value that was already shipping (to within a
/// millimetre or two of float rounding), so the reference body does not move, while a
/// taller, much narrower figure gets numbers that follow its own body. A fraction is a
/// stated relationship between a body and a system; a magic number is neither.</para>
///
/// <para>DETERMINISM. Both inputs are measured off the same imported <c>.glb</c> by the same
/// code on every peer, so a predicted owner, the server authority and a remote proxy all
/// derive a byte-identical collision capsule. That matters: the capsule participates in
/// <c>AvatarMotor.Step</c>, which prediction, authority and reconciliation replay all share
/// (see <c>MoveState</c>'s contract). A per-peer capsule would be a silent desync source.</para>
///
/// <para>WHAT IS NOT DERIVED HERE. Feel constants that are about the ANIMATION rather than
/// the body — squash amplitudes, waddle roll, the body-tilt cap — are untouched. They are
/// tuned by eye against a moving character and a headed pass, not by measuring a mesh, and
/// silently rescaling them would be a feel change wearing a bug fix's clothes.</para>
/// </summary>
public readonly struct AvatarProportions
{
    // --- The reference character ---------------------------------------------------------
    // The original harvested creature (since removed), measured across ALL 17 primitives (glTF ships
    // one primitive per material, so primitives[0] is one material's chunk, not the mesh):
    //   body crown  0.9301 m  (Torso top; the springy sprout above it is excluded — see
    //                          AvatarVisual.BodyBounds for why a cosmetic must not collide)
    //   half-width  0.5043 m  (mean of the X and Z half-extents, 0.5050 and 0.5037)
    // These two appear below ONLY as the divisor that turned each shipped value into a
    // fraction, and as the fallback body if a model ever fails to load. Nothing at runtime
    // reads the reference: a live character is always measured.

    /// <summary>Reference body crown — see the note above. Fallback only.</summary>
    public const float FallbackCrownM = 0.9301f;

    /// <summary>Reference half-width — see the note above. Fallback only.</summary>
    public const float FallbackHalfWidthM = 0.5043f;

    // --- Calibrated fractions -------------------------------------------------------------

    /// <summary>Collision radius as a fraction of half-width: 0.36 / 0.5043. The capsule has
    /// always been narrower than the silhouette on purpose — the reference body's arms and tail stick
    /// out past a body a player expects to fit through a gap, and a collider drawn round the
    /// widest cosmetic makes a character feel fat. Keeping the RATIO keeps that intent.</summary>
    public const float CapsuleRadiusFraction = 0.7138f;

    /// <summary>Blob-shadow radius as a fraction of half-width: 0.46 / 0.5043. Wider than the
    /// capsule (the shadow reads the whole silhouette, not the collider) and still inside the
    /// model's own footprint.</summary>
    public const float ShadowRadiusFraction = 0.9121f;

    /// <summary>Carry-anchor height as a fraction of crown: 0.62 / 0.9301. Held items ride at
    /// roughly two-thirds of the character's height — chest level on a figure with a chest.</summary>
    public const float CarryHeightFraction = 0.6666f;

    /// <summary>How far in FRONT the carry anchor sits, as a multiple of half-width:
    /// 0.62 / 0.5043. Scaling reach with WIDTH rather than height is the point — a whole figure is
    /// taller than the reference blob but much slimmer, and an item held at the blob's reach would
    /// float a hand's width off the figure's chest.</summary>
    public const float CarryReachFraction = 1.2294f;

    /// <summary>Stow anchor, off to one side: 0.20 / 0.5043 of half-width.</summary>
    public const float StowSideFraction = 0.3966f;

    /// <summary>Stow anchor height: 0.46 / 0.9301 of crown.</summary>
    public const float StowHeightFraction = 0.4946f;

    /// <summary>Stow anchor, behind the back: 0.30 / 0.5043 of half-width.</summary>
    public const float StowBackFraction = 0.5949f;

    /// <summary>Camera focus height as a fraction of crown: 0.70 / 0.9301. The third-person
    /// rig frames a point three-quarters up the body; on a 1.24 m figure the old absolute
    /// 0.70 m framed the navel.</summary>
    public const float CameraFocusFraction = 0.7526f;

    /// <summary>Eye height when a model has no eye geometry to measure: 0.78 / 0.9301 of
    /// crown — i.e. exactly the aim anchor that shipped, expressed as a ratio. Every model on
    /// the roster today HAS eyes, so this is the degraded path, not the normal one.</summary>
    public const float EyeHeightFallbackFraction = 0.8386f;

    /// <summary>Gap between the crown and the nameplate, in metres — deliberately ABSOLUTE
    /// rather than a fraction. A nameplate gap is a legibility distance (the plate must clear
    /// the head without floating a body-length above it), and legibility does not scale with
    /// the character. 0.22 m is what the shipped +1.15 m plate was above the reference body's
    /// 0.9301 m crown, so that plate does not move.</summary>
    public const float NameplateGapM = 0.22f;

    // --- Sanity floors ---------------------------------------------------------------------
    // A measurement can only fail one way that matters: a model that did not load leaves an
    // empty AABB and would silently produce a zero-size collider — a player who falls through
    // the world. These floors turn that into a visibly wrong avatar instead.
    private const float MinCrownM = 0.25f;
    private const float MinHalfWidthM = 0.10f;

    /// <summary>Ground-to-crown of the body, metres, measured off the model in its rest pose.
    /// The avatar's origin is the ground it stands on, so this doubles as "how tall is the
    /// collider allowed to be".</summary>
    public float CrownM { get; }

    /// <summary>Mean of the X and Z half-extents of the body, metres. One scalar rather than
    /// two because every consumer below wants a ROUND quantity — a capsule radius, a circular
    /// shadow, a reach — and averaging the two axes is the honest way to collapse a box into a
    /// circle. Taking the max instead would size everything to a tail.</summary>
    public float HalfWidthM { get; }

    /// <summary>Eye height, metres — measured off the model's own eye geometry when it has
    /// any, otherwise <see cref="EyeHeightFallbackFraction"/> of the crown.</summary>
    public float EyeHeightM { get; }

    /// <summary>True when <see cref="EyeHeightM"/> came from real eye geometry rather than the
    /// fallback ratio. Test surface: "this character's eyeline was measured, not guessed" is a
    /// different claim from "this character has a plausible eyeline", and a suite that cannot
    /// tell them apart would pass just as happily on a model whose eyes never loaded.</summary>
    public bool EyesMeasured { get; }

    private AvatarProportions(float crownM, float halfWidthM, float? eyeHeightM)
    {
        CrownM = Mathf.Max(crownM, MinCrownM);
        HalfWidthM = Mathf.Max(halfWidthM, MinHalfWidthM);
        EyesMeasured = eyeHeightM is float eye && eye > 0f && eye <= CrownM;
        EyeHeightM = EyesMeasured ? eyeHeightM!.Value : CrownM * EyeHeightFallbackFraction;
    }

    /// <summary>The reference body's numbers, for an avatar whose model failed to measure. Never the
    /// normal path — <see cref="For"/> only falls back on a degenerate AABB.</summary>
    public static AvatarProportions Fallback { get; } =
        new(FallbackCrownM, FallbackHalfWidthM, null);

    // --- The player's own body, for code that cannot wait for a live avatar ------------------
    //
    // A live avatar always measures its own model (see For) and nothing on a live path should
    // read the two constants below. They exist for `static readonly` DATA that is constructed at
    // type-load, long before any scene tree — a since-removed creature roster was the case they
    // were added for (CATCH-1, 2026-08-16).
    //
    // Measured off the whole-figure model AvatarVisual.PreferredAvatarKey resolved to at the
    // time — i.e. what a player WAS in that build. Read
    // from the shipped glTF, not from a doc. Only the two HEIGHTS are stated: the half-width was
    // not measured in that pass and inventing one would be exactly the kind of plausible number
    // this whole type exists to abolish.
    //
    // WHY THEY EARN THEIR PLACE. Hovering creatures were authored to hover at "near head
    // height on a child" — 1.20 m and 1.35 m — against a child who did not exist in that build.
    // The real head was at 1.241 m and the real eyeline at 0.995 m, so both floated roughly 35 cm
    // too high, and the net's hoop could only ever clip the top rim of its target. A
    // literal at the call site goes stale silently; expressed against these, it goes stale
    // LOUDLY, in one place, on the day a model lands at a different scale.

    /// <summary>Measured ground-to-crown of the reference whole figure, metres. See the note above.</summary>
    public const float PlayerCrownM = 1.241f;

    /// <summary>Measured eye-centre height of the reference whole figure, metres — the height the aim ray
    /// leaves from (<see cref="AimAnchorLocal"/>) and therefore the height a level swing sweeps.
    /// See the note above.</summary>
    public const float PlayerEyeHeightM = 0.995f;

    /// <summary>Derives a character's dimensions from its measured rest-pose body bounds
    /// (<see cref="AvatarVisual.BodyBounds"/>) and, when the model has eyes, their centre
    /// height (<see cref="AvatarVisual.EyeCentre"/>).</summary>
    public static AvatarProportions For(Aabb bodyBounds, float? eyeHeightM)
    {
        // A zero-height or zero-footprint body is a model that did not load, not a very small
        // character: degrade to the reference creature rather than to a pinpoint collider.
        if (bodyBounds.Size.Y <= MinCrownM || bodyBounds.Size.X + bodyBounds.Size.Z <= 0f)
            return Fallback;
        return new AvatarProportions(
            bodyBounds.End.Y,
            (bodyBounds.Size.X + bodyBounds.Size.Z) * 0.25f,
            eyeHeightM);
    }

    /// <summary>Radius of the collision capsule, metres.</summary>
    public float CapsuleRadiusM => HalfWidthM * CapsuleRadiusFraction;

    /// <summary>TOTAL height of the collision capsule including both hemispherical caps
    /// (Godot's <c>CapsuleShape3D.Height</c> convention), metres. Floored at a sphere so a very
    /// wide, very short creature can never be given an impossible capsule.</summary>
    public float CapsuleHeightM => Mathf.Max(CrownM, CapsuleRadiusM * 2f);

    /// <summary>Local position of the capsule's centre. The capsule spans y = 0 (the ground the
    /// avatar's origin sits on, which every spawn, teleport and floor check in the codebase
    /// assumes) up to the crown, so the whole visible body is inside the collider — which is
    /// the defect this type was written for: a 1.24 m figure wearing a 0.9 m capsule has a head
    /// that passes through ceilings and takes no part in collision at all.</summary>
    public Vector3 CapsuleCentreLocal => new(0f, CapsuleHeightM * 0.5f, 0f);

    /// <summary>Blob-shadow radius at ground contact, metres.</summary>
    public float ShadowRadiusM => HalfWidthM * ShadowRadiusFraction;

    /// <summary>Rest offset of the carry anchor, avatar-local. Negative Z is forward in Godot's
    /// convention, so this sits in front of the chest.</summary>
    public Vector3 CarryAnchorRestLocal =>
        new(0f, CrownM * CarryHeightFraction, -HalfWidthM * CarryReachFraction);

    /// <summary>Rest offset of the stow anchor, avatar-local: behind and below the hand.</summary>
    public Vector3 StowAnchorRestLocal => new(
        HalfWidthM * StowSideFraction,
        CrownM * StowHeightFraction,
        HalfWidthM * StowBackFraction);

    /// <summary>Local position of the aim-ray origin: the character's actual eyeline.</summary>
    public Vector3 AimAnchorLocal => new(0f, EyeHeightM, 0f);

    /// <summary>Height above the avatar's origin at which the nameplate is projected.</summary>
    public float NameplateHeightM => CrownM + NameplateGapM;

    /// <summary>Height above the avatar's origin the follow camera frames.</summary>
    public float CameraFocusHeightM => CrownM * CameraFocusFraction;
}
