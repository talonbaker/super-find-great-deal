using Godot;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>What red, blue and yellow look like</b> (TASK-1, 2026-09-19) — the three sort colours, in
/// one place, because the object and the bin plate that claims it MUST be the same colour or the
/// mechanic is a lie.
///
/// <para><b>These are gameplay values, not interface tokens, and that is why they are here and
/// not in <c>UiTokens</c>.</b> A UI token is a decision about how this project's chrome looks and
/// is allowed to change with the palette or the time of day; these three are the RULE — "put the
/// red ones in the red bin" — and a red that drifted toward the yellow would not be a styling
/// regression, it would be an unwinnable round. <c>UiNoBespokeStylingTests</c> scans
/// <c>scripts/ui/**</c> plus a named list, and this file is deliberately in neither: the
/// exemption is structural rather than an allowlist entry, so nothing has to be remembered.</para>
///
/// <para><b>Chosen for separation at a glance in a lit room</b>, not for prettiness. The three
/// are far apart in hue AND in luminance (roughly 0.30 / 0.24 / 0.72 relative), so a player who
/// cannot separate the red from the yellow by hue can still separate them by how bright they are
/// — <c>ART-BIBLE</c>'s "never hue alone" rule, and it matters more here than anywhere else in
/// the game because this is the one place a colour IS a rule. The word on the plate
/// (<c>HideSeekText.BinPlateWord</c>) is the third channel.</para>
///
/// <para>Talon picked none of these numbers. They are three constants in one file; retuning them
/// is one edit and changes both the objects and the plates at once, which is the only property
/// this class exists for.</para>
/// </summary>
public static class SortPalette
{
    /// <summary>Bin 0. A warm signal red — dark enough to read against the task room's
    /// 0.54-grey walls without glowing.</summary>
    public static readonly Color Red = new(0.80f, 0.16f, 0.14f);

    /// <summary>Bin 1. A mid blue, the darkest of the three by luminance.</summary>
    public static readonly Color Blue = new(0.16f, 0.36f, 0.82f);

    /// <summary>Bin 2. A saturated yellow, and by far the brightest — which is what keeps it
    /// apart from the red for a player reading value rather than hue.</summary>
    public static readonly Color Yellow = new(0.92f, 0.78f, 0.13f);

    /// <summary>How much the objects glow. Emission is ON at a low multiplier for the same
    /// reason SFX-1's prefabs set one: <c>THRILL-BIBLE</c> Part II's 2026-08-30 ruling is that a
    /// prop may GLOW and may not LIGHT, and eighteen objects in a small room at the crate's 0.55
    /// would flatten it. 0.20 keeps the colour legible in the corner the crate sits in without
    /// the room reading as lit by its own contents.</summary>
    public const float EmissionEnergy = 0.20f;

    /// <summary>The colour for a sort colour. The one lookup; a second one somewhere else is how
    /// a bin comes to disagree with the objects it is asking for.</summary>
    public static Color Of(SortColour colour) => colour switch
    {
        SortColour.Red => Red,
        SortColour.Blue => Blue,
        _ => Yellow,
    };
}
