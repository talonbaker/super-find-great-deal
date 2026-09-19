using Godot;

namespace MpFoundation.Game.Light;

/// <summary>
/// <b>The player's pocket light, as pure numbers.</b> Everything the flashlight is, expressed as
/// compile-time constants so the values that decide how it reads are provable by
/// <c>dotnet test</c> with no engine present.
///
/// <para><b>PRESENTATION ONLY, AND THAT IS A DECISION RATHER THAN AN OVERSIGHT (NIGHT-2,
/// 2026-08-29).</b> This light illuminates. It does not extend how far anybody can see. Canon's
/// parity law says gameplay visibility is server data identical on every client and that rendered
/// light is presentation; <c>PlayerSightCurve</c> is the sole producer of the former and nothing
/// here reaches it. Concretely: <b>there is no <c>AppendLitLightSamples</c> on
/// <see cref="FlashlightManager"/></b> and no <c>Flashlights</c> property on
/// <c>PlayerSightService</c>, so a later packet cannot wire this into the sight union by
/// forgetting a rule — it would have to write the method first. Making the rule impossible to
/// break beats writing it down for somebody to honour. <c>FlashlightParityTests</c> scans this
/// directory for the sight symbols with a positive control, so the guard fails a test rather than
/// living in this paragraph.</para>
///
/// <para><b>Why presentation-only is the FIX and not a hedge, measured.</b> The world Talon plays
/// (<c>bubbletest</c>) overrides the sight seam outright —
/// <c>BubbleTestWorld.NightSightRangeM</c> pins night fog at a 28 m range regardless of what any
/// light does — so a sight contribution there would change nothing at all. What is missing after
/// dusk in that world is not <i>range</i>, it is <i>illumination</i>: ambient sits at
/// <c>OutdoorAtmosphere.NightAmbientFloor</c> and the level has no light source of its own. A real
/// <c>OmniLight3D</c> on the body is therefore the whole of the fix there. A world that instead
/// falls to <c>PlayerSightCurve.DarkFloorM</c> (3 m) would have its fog swallow this glow at about
/// four metres. That is a finding for Talon, recorded in the NIGHT-2 report, not a value to change
/// here: whether a personal light grants <i>sight</i> is adjacent to THRILL-BIBLE §13's open "does
/// a small light ward at all?" fork and is a canon call.</para>
///
/// <para><b>The numbers sit under canon fact 3's backstops, deliberately.</b> The central fire is the
/// emotional centrepiece and every other light is personal-scale: a personal light may reach at
/// most <see cref="MaxPersonalLitRadiusM"/> and burn at most <see cref="MaxPersonalIntensity01"/>,
/// and this light is checked against both rather than merely intended to respect them.</para>
/// </summary>
public static class FlashlightProfile
{
    /// <summary>Canon fact 3's backstop on any personal light's reach, metres: the central fire is
    /// the centrepiece and nothing personal-scale may approach it. Asserted by the tests rather
    /// than merely intended.</summary>
    public const float MaxPersonalLitRadiusM = 6.0f;

    /// <summary>Canon fact 3's backstop on any personal light's brightness, on the normalised
    /// scale where the central fire's reserved value is 1.0.</summary>
    public const float MaxPersonalIntensity01 = 0.5f;

    /// <summary><b>Radius of ground the flashlight lights, metres.</b> Five, and the neighbours
    /// that fixed it were the other personal lights of the original design, which lit 4 m; Issue
    /// #184's ratified design is that a battery flashlight <i>beats a handheld flame at lighting the
    /// ground</i> — that is the one job it is allowed to win. One metre is the smallest step that
    /// reads as "better" without approaching <see cref="MaxPersonalLitRadiusM"/> (6 m), which
    /// canon fact 3 makes the ceiling for anything that is not the fire.
    ///
    /// <para>Talon asked for a "simple glow", not a beam. Five metres of ground around you is a
    /// pool you stand in; it shows the step you are about to take and a teammate beside you, and
    /// it shows nothing about the tree line. That is the shape of the ask and it is also what
    /// keeps the dark load-bearing — the dark is canon's pressure, and a glow you carry inside it
    /// does not repeal it.</para></summary>
    public const float LitRadiusM = 5.0f;

    /// <summary>Brightness, normalised against the central fire's reserved 1.0. 0.45 is above the
    /// original design's other personal light (0.35, same reasoning as the radius) and below
    /// <see cref="MaxPersonalIntensity01"/> (0.5), which canon fact 3 makes an absolute backstop
    /// rather than a target to approach.</summary>
    public const float Intensity01 = 0.45f;

    /// <summary><see cref="OmniLight3D.LightEnergy"/> per unit of normalised intensity — the one
    /// scale that turns a personal light's <see cref="Intensity01"/> into rendered energy.
    /// Deliberately modest: <see cref="MaxPersonalIntensity01"/> (0.5) is canon fact 3's backstop
    /// and 1.0 is reserved for the central fire, so a personal light at full burn lands well under a
    /// third of the fire's eventual energy.</summary>
    public const float LightEnergyPerIntensity = 2.2f;

    /// <summary>The rendered energy, derived rather than restated, so the two numbers cannot
    /// drift apart.</summary>
    public static float LightEnergy => Intensity01 * LightEnergyPerIntensity;

    /// <summary><b>Cool white, and the direction of the shift is the point.</b> THRILL-BIBLE §6.6
    /// makes colour temperature a channel: warm reads as life and intimacy, cool as distance and
    /// unfamiliarity. The central fire owns warm (canon fact 3) and the trail light of the original
    /// design owned acid yellow so it could not be mistaken for firelight; a battery light that read
    /// warm would be competing with the fire for the one thing the fire is for. Slightly blue-white
    /// is what a cheap battery lamp actually looks like and it is the only free slot left.</summary>
    public static readonly Color LightColor = new(0.80f, 0.86f, 1.00f);

    /// <summary>Height above the avatar's origin the light hangs at, metres. Chest height on a
    /// ~1.4 m player: high enough that the falloff sphere is not half buried in the ground (which
    /// wastes most of the radius), low enough that the pool reads as coming from the player rather
    /// than from overhead.</summary>
    public const float MountHeightM = 1.0f;

    /// <summary>Node name of the per-avatar light, so the manager, the tests and any future probe
    /// agree on one spelling. Two spellings of one node is the "second copy" trap
    /// <c>BubbleTestWorld.HubTvNodeName</c> already paid for once.</summary>
    public const string LightNodeName = "FlashlightGlow";

    /// <summary><b>Shadows are off and must stay off.</b> The performance floor is a GTX 970 on
    /// Forward+ and this is a light <i>per player</i>, up to the supported group size — a
    /// shadow-casting omni is six cube renders a frame for a glow whose whole job is a pool of
    /// ground. Stated as a constant so flipping it is an edit somebody has to justify rather than
    /// a property poke in a later pass.</summary>
    public const bool ShadowsEnabled = false;
}
