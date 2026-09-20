using System;
using System.Collections.Generic;
using System.Linq;
// Row 31's pin calls SandboxCamera's own dip curve rather than transcribing it. The direction is
// the one AvatarMotor.cs, NetCodec.cs and StateChecksum.cs already take: MpFoundation.Net reads
// Game.Sandbox, not the reverse.
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Net;

/// <summary>
/// One row of the MOVE-4a knob table (<c>docs/design/2026-08-26-movement-tuning-surface.md</c>
/// §2.2 and §3.3): the range, the declaration it prints back into, and the test that pins it.
/// <b>Everything here is transcribed from that spec, not derived here.</b>
/// </summary>
public sealed class MotorKnob
{
    /// <summary>Knob-table row number: 1-31 from MOVE-4a §2.2, 31-52 from MOVE-5a §11.1. A stable
    /// identifier, NOT the list order — see <see cref="MotorTuningKnobs.All"/>'s remark.</summary>
    public required int Index { get; init; }

    /// <summary>The <see cref="MotorTuning"/> field name.</summary>
    public required string Name { get; init; }

    /// <summary>Ground / Gravity / Jump / Air / Skid / Landing / Camera (§2.2), plus MOVE-5's two
    /// new ones, Slide and Chain (§11.1).</summary>
    public required string Group { get; init; }

    /// <summary>The unit, for a panel label and a warning line.</summary>
    public required string Unit { get; init; }

    /// <summary>The shipped value. <b>Always equal to <c>MotorTuning.Default</c>'s field</b> —
    /// asserted by <c>MotorTuningDefaultIdentityTests</c>.</summary>
    public required float Default { get; init; }

    public required float Min { get; init; }
    public required float Max { get; init; }
    public required float Step { get; init; }

    /// <summary>Why the bound is where it is — quoted into a clamp warning so the person who hit
    /// it is told the reason, not merely the number.</summary>
    public string BoundReason { get; init; } = "";

    /// <summary>Repo-relative path of the constant this row prints back into.</summary>
    public required string DeclFile { get; init; }

    /// <summary>Line of that constant, or <c>0</c> for a constant that does not exist yet.
    /// <b>A hint that goes stale</b> (§6.4 rule 4) — <see cref="DeclFile"/> is the authority.</summary>
    public int DeclLine { get; init; }

    /// <summary>Where to add it, for a <see cref="DeclLine"/> of 0 (§6.4 rule 8).</summary>
    public string PlacementHint { get; init; } = "";

    /// <summary>The constant's own name in <see cref="DeclFile"/>, which is the field name for
    /// every row but one and <c>SkidEnterSpeedMps</c> for the reshaped one (§2.5).</summary>
    public string DeclName { get; init; } = "";

    /// <summary><c>public const float</c> for every row. Printed verbatim so a paste is a straight
    /// substitution. <b>MOVE-4c removed the last two overrides</b> — <c>TakeoffKickSec</c> and
    /// <c>TakeoffKickMinDriveFraction</c> were <c>private</c> in <c>AvatarVisual</c> and are now
    /// public (spec §2.2's note on rows 26-27), so no row needs a modifier of its own any more.
    /// The field is kept rather than deleted because §6.4 rule 2's acceptance test is "paste, save,
    /// build, no edits", and that is a property of the declaration text, not of this table.</summary>
    public string DeclModifiers { get; init; } = "public const float";

    /// <summary>Renders the right-hand side of the printed declaration. Defaults to the literal;
    /// row 19 re-emits the derived <c>MoveSpeed * 0.75f</c> form (§6.4 rule 9).</summary>
    public Func<float, string>? DeclValue { get; init; }

    public required Func<MotorTuning, float> Get { get; init; }
    public required Func<MotorTuning, float, MotorTuning> Set { get; init; }

    // --- The pin (§3.3) --------------------------------------------------------------------

    /// <summary>The test method that fails first when this row moves out of its window, or empty
    /// if the row is one of the eight that no test pins.</summary>
    public string PinTest { get; init; } = "";

    /// <summary><c>file:line</c> of that assertion.</summary>
    public string PinSource { get; init; } = "";

    /// <summary>The literal that test asserts, or a note saying there is no literal to retype
    /// because the pin is a property rather than a number (§6.4 rule 10).</summary>
    public string PinLiteral { get; init; } = "";

    /// <summary>Floor of the legal window, evaluated against the live tuning because several
    /// windows are coupled (§3.4). <c>null</c> means unbounded below.</summary>
    public Func<MotorTuning, float>? PinMin { get; init; }

    /// <summary>Ceiling of the legal window. <c>null</c> means unbounded above.</summary>
    public Func<MotorTuning, float>? PinMax { get; init; }

    /// <summary>The quantity the pin actually constrains — the knob's own value for most rows, the
    /// <i>effective</i> m/s for the two skid rows whose pins are stated in m/s (§3.3 rows 19, 22).</summary>
    public Func<MotorTuning, float>? PinQuantity { get; init; }

    /// <summary>What <see cref="PinQuantity"/> is called, when it is not the knob itself.</summary>
    public string PinQuantityLabel { get; init; } = "";

    /// <summary>The row whose movement makes this one <i>live</i>, or empty. §6.4 rule 11: a knob
    /// that is inert at its default and load-bearing the moment a companion moves must be printed
    /// even when it did not move, or the paste does not reproduce what was played.</summary>
    public string LiveWhenMoved { get; init; } = "";

    public bool IsPinned => PinTest.Length > 0;

    /// <summary>Is this row outside its window at <paramref name="t"/>? Coupled windows are
    /// evaluated live, so a combination that breaches an inequality neither knob breaches alone is
    /// caught (§3.4).</summary>
    public bool Breaches(in MotorTuning t, out float value, out float? lo, out float? hi)
    {
        value = PinQuantity is null ? Get(t) : PinQuantity(t);
        lo = PinMin?.Invoke(t);
        hi = PinMax?.Invoke(t);
        if (!IsPinned || !float.IsFinite(value))
            return false;
        return (lo is float l && value < l) || (hi is float h && value > h);
    }
}

/// <summary>
/// <b>The knob table as data</b> — MOVE-4a §2.2's thirty rows, MOVE-4f's ramp and MOVE-5a §11.1's
/// twenty-two verb rows, their ranges from §2.3 / §11.3, their pins from §3.3 / §11.4 and their
/// declarations for §6.4's printer. Validation,
/// save/load and print all read this one table, so a knob added in one place is a knob added
/// everywhere.
/// </summary>
public static class MotorTuningKnobs
{
    // Rows whose windows are coupled are evaluated against the live tuning rather than a literal;
    // see MotorTuningInvariants for the arithmetic behind the apex-hang ceiling.

    public static readonly MotorKnob MoveSpeed = new()
    {
        Index = 1, Name = "MoveSpeed", Group = "Ground", Unit = "m/s",
        Default = 3.8f, Min = 1.0f, Max = 12.0f, Step = 0.1f,
        BoundReason = "Taste, bracketing the shipped value (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 68,
        Get = t => t.MoveSpeed, Set = (t, v) => t with { MoveSpeed = v },
        // MOVE-8: 5.4 -> 3.8 with Talon's speed ruling. The pin that used to hold this exact
        // belonged to a system that is not part of this build; the row is unpinned until a live
        // consumer restates the number again.
    };

    public static readonly MotorKnob SprintMultiplier = new()
    {
        Index = 2, Name = "SprintMultiplier", Group = "Ground", Unit = "x",
        Default = 1.6f, Min = 1.00f, Max = 3.00f, Step = 0.05f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 356,
        Get = t => t.SprintMultiplier, Set = (t, v) => t with { SprintMultiplier = v },
    };

    public static readonly MotorKnob Acceleration = new()
    {
        Index = 3, Name = "Acceleration", Group = "Ground", Unit = "m/s^2",
        Default = 9f, Min = 1.0f, Max = 40.0f, Step = 0.5f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 85,
        Get = t => t.Acceleration, Set = (t, v) => t with { Acceleration = v },
        PinTest = "LocomotionTests.SprintRamp_IsLongEnoughToSee_AndBrakingIsFaster",
        PinSource = "tests/unit/LocomotionTests.cs:387",
        PinLiteral = "the sprint-ramp duration bracket, narrowed by :388",
        PinMin = _ => 7.855f, PinMax = _ => 15.75f,
    };

    public static readonly MotorKnob Deceleration = new()
    {
        Index = 4, Name = "Deceleration", Group = "Ground", Unit = "m/s^2",
        Default = 21f, Min = 2.0f, Max = 60.0f, Step = 0.5f,
        BoundReason = "Taste (spec §2.3); the shove settle floor at 18.384 is a pin, not a bound.",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 102,
        Get = t => t.Deceleration, Set = (t, v) => t with { Deceleration = v },
        PinTest = "AirborneControlTests.MaximumAirBrakeOverAFullFlight_SitsAboveTheSkidThreshold",
        PinSource = "tests/unit/AirborneControlTests.cs:217",
        PinLiteral = "Near(4.47f, ...) — the maximum the air brake can shed in one flight",
        PinMin = _ => 20.892f, PinMax = _ => 21.080f,
    };

    public static readonly MotorKnob TurnAcceleration = new()
    {
        Index = 5, Name = "TurnAcceleration", Group = "Ground", Unit = "m/s^2",
        Default = 34f, Min = 2.0f, Max = 80.0f, Step = 1.0f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 117,
        Get = t => t.TurnAcceleration, Set = (t, v) => t with { TurnAcceleration = v },
        PinTest = "LocomotionTests.Skid_DidNotRetuneOrdinarySteering",
        PinSource = "tests/unit/LocomotionTests.cs:757",
        PinLiteral = "34 +/- 0.0001",
        PinMin = _ => 33.9999f, PinMax = _ => 34.0001f,
    };

    public static readonly MotorKnob TurnLerp = new()
    {
        Index = 6, Name = "TurnLerp", Group = "Ground", Unit = "1/s",
        Default = 12f, Min = 1.0f, Max = 45.0f, Step = 0.5f,
        BoundReason = "Hard: 45 is a lerp factor of 0.75 at 60 Hz. Above 60 the factor exceeds 1 "
                    + "and the facing overshoots and oscillates (spec §2.3, §9.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 205,
        Get = t => t.TurnLerp, Set = (t, v) => t with { TurnLerp = v },
    };

    public static readonly MotorKnob AimTurnLerp = new()
    {
        Index = 7, Name = "AimTurnLerp", Group = "Ground", Unit = "1/s",
        Default = 22f, Min = 1.0f, Max = 60.0f, Step = 0.5f,
        BoundReason = "Taste (spec §2.3); the path is already clamped to a factor of 1.",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 354,
        Get = t => t.AimTurnLerp, Set = (t, v) => t with { AimTurnLerp = v },
        PinTest = "LocomotionTests.Facing_TracksTravel_UnlessAnAimIsHandedIn",
        PinSource = "tests/unit/LocomotionTests.cs:481",
        PinLiteral = "the aimed-turn floor at :481 and the ceiling at :474",
        PinMin = _ => 6.514f, PinMax = _ => 45f,
    };

    public static readonly MotorKnob BodyYawFollowsAim = new()
    {
        Index = 58, Name = "BodyYawFollowsAim", Group = "Ground", Unit = "0/1",
        Default = 1f, Min = 0f, Max = 1f, Step = 1f,
        BoundReason = "A toggle: 1 turns the body toward the replicated look yaw (first person, "
                    + "this game's default), 0 restores the foundation's travel-facing rule. Both "
                    + "behaviours run through the same AvatarMotor.ResolveYaw seam (FP-1).",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "FP-1 row: there is no `const` to point at. The paste target is "
                      + "MotorTuning.Default's initializer, and the accessor AvatarMotor exposes "
                      + "for this row sits beside AimTurnLerp.",
        Get = t => t.BodyYawFollowsAim,
        Set = (t, v) => t with { BodyYawFollowsAim = v },
        PinTest = "LocomotionTests.BodyYawFollowsAim_TurnsTheBodyTowardTheLook_AndOffRestoresTravelFacing",
        PinSource = "tests/unit/LocomotionTests.cs",
        PinLiteral = "no literal — the pin is the BEHAVIOUR of each of the two settings, not a value",
        PinMin = _ => 0f, PinMax = _ => 1f,
    };

    public static readonly MotorKnob Gravity = new()
    {
        Index = 8, Name = "Gravity", Group = "Gravity", Unit = "m/s^2",
        Default = 24f, Min = 5.0f, Max = 45.0f, Step = 0.5f,
        BoundReason = "Hard floor: at or near zero a body that leaves the ground never returns — "
                    + "an exitless state reached by a slider (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 200,
        Get = t => t.Gravity, Set = (t, v) => t with { Gravity = v },
        PinTest = "AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately",
        PinSource = "tests/unit/AirborneControlTests.cs:317-322",
        PinLiteral = "Near(1.60f, apexHeld, 0.15f) and Near(0.710f, airtimeHeld, 0.06f)",
        PinMin = _ => 21.01f, PinMax = _ => 25.31f,
    };

    public static readonly MotorKnob FallGravityMultiplier = new()
    {
        Index = 9, Name = "FallGravityMultiplier", Group = "Gravity", Unit = "x",
        Default = 1.50f, Min = 0.50f, Max = 3.00f, Step = 0.05f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 201,
        Get = t => t.FallGravityMultiplier, Set = (t, v) => t with { FallGravityMultiplier = v },
        PinTest = "AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately",
        PinSource = "tests/unit/AirborneControlTests.cs:317-322",
        PinLiteral = "Near(0.710f, airtimeHeld, 0.06f)",
        PinMin = _ => 1.076f, PinMax = _ => 2.594f,
    };

    public static readonly MotorKnob ApexHangStrength = new()
    {
        Index = 10, Name = "ApexHangStrength", Group = "Gravity", Unit = "fraction",
        Default = 0.30f, Min = 0.00f, Max = 0.90f, Step = 0.01f,
        BoundReason = "Hard ceiling: at 1.0 the effective gravity at the apex is zero and the body "
                    + "hangs forever — a state with no exit, MECHANICS §2 (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 0,
        PlacementHint = "add beside JumpReleaseGravityMultiplier",
        Get = t => t.ApexHangStrength, Set = (t, v) => t with { ApexHangStrength = v },
        PinTest = "AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately",
        PinSource = "tests/unit/AirborneControlTests.cs:317-322",
        PinLiteral = "Near(0.710f, airtimeHeld, 0.06f) — the held-airtime ceiling of 0.770 s",
        PinMax = MotorTuningInvariants.ApexHangStrengthCeiling,
    };

    public static readonly MotorKnob ApexHangWindowMps = new()
    {
        Index = 11, Name = "ApexHangWindowMps", Group = "Gravity", Unit = "m/s",
        Default = 2.00f, Min = 0.25f, Max = 6.00f, Step = 0.05f,
        BoundReason = "Hard floor: it is the divisor in |v_y| / window, so zero is a "
                    + "divide-by-zero on every airborne tick (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 0,
        PlacementHint = "add beside JumpReleaseGravityMultiplier",
        Get = t => t.ApexHangWindowMps, Set = (t, v) => t with { ApexHangWindowMps = v },
        LiveWhenMoved = "ApexHangStrength",
        PinTest = "AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately",
        PinSource = "tests/unit/AirborneControlTests.cs:317-322",
        PinLiteral = "joint with ApexHangStrength — the held-airtime ceiling of 0.770 s (spec §3.7)",
        PinMax = MotorTuningInvariants.ApexHangWindowCeiling,
    };

    public static readonly MotorKnob JumpVelocity = new()
    {
        Index = 12, Name = "JumpVelocity", Group = "Jump", Unit = "m/s",
        Default = 8.4f, Min = 2.0f, Max = 16.0f, Step = 0.1f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 202,
        Get = t => t.JumpVelocity, Set = (t, v) => t with { JumpVelocity = v },
        PinTest = "AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately",
        PinSource = "tests/unit/AirborneControlTests.cs:317-322",
        PinLiteral = "Near(1.60f, apexHeld, 0.15f)",
        PinMin = _ => 7.805f, PinMax = _ => 8.590f,
    };

    public static readonly MotorKnob JumpReleaseGravityMultiplier = new()
    {
        Index = 13, Name = "JumpReleaseGravityMultiplier", Group = "Jump", Unit = "x",
        Default = 4.00f, Min = 1.00f, Max = 8.00f, Step = 0.05f,
        BoundReason = "Hard floor: below 1.0 letting go of the jump key makes you go HIGHER, "
                    + "inverting the variable-jump model (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 316,
        Get = t => t.JumpReleaseGravityMultiplier,
        Set = (t, v) => t with { JumpReleaseGravityMultiplier = v },
        PinTest = "AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately",
        PinSource = "tests/unit/AirborneControlTests.cs:318/:320/:322",
        PinLiteral = "Near(0.67f, apexTap, 0.15f), Near(0.357f, airtimeTap, 0.06f) and apexHeld/apexTap >= 2.0f",
        PinMin = _ => 2.40f, PinMax = _ => 4.60f,
    };

    public static readonly MotorKnob CoyoteTimeSec = new()
    {
        Index = 14, Name = "CoyoteTimeSec", Group = "Jump", Unit = "s",
        Default = 0.30f, Min = 0.00f, Max = 0.40f, Step = 0.01f,
        BoundReason = "Taste (spec §2.3); zero is a legitimate and instructive setting.",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 203,
        Get = t => t.CoyoteTimeSec, Set = (t, v) => t with { CoyoteTimeSec = v },
        PinTest = "AirborneControlTests.CoyoteTime_FiresAtTheWindowEdge_AndNotOneTickLater",
        PinSource = "tests/unit/AirborneControlTests.cs:383",
        // MOVE-8: 0.12 -> 0.30, Talon's forgiveness ruling. The edge is 18 ticks now, not 7.
        PinLiteral = "the window-edge tick count, computed from 0.30",
        PinMin = _ => 0.29999f, PinMax = _ => 0.30001f,
    };

    public static readonly MotorKnob JumpBufferSec = new()
    {
        Index = 15, Name = "JumpBufferSec", Group = "Jump", Unit = "s",
        Default = 0.30f, Min = 0.00f, Max = 0.40f, Step = 0.01f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 204,
        Get = t => t.JumpBufferSec, Set = (t, v) => t with { JumpBufferSec = v },
        PinTest = "AirborneControlTests.BufferedJump_PressedBeforeLanding_FiresOnTheLandingTick",
        PinSource = "tests/unit/AirborneControlTests.cs:407-430",
        PinLiteral = "a press held for more than 6 ticks must survive to the landing tick — "
                   + "no literal to edit; the floor is the tick count the test presses at",
        PinMin = _ => 0.100f,
    };

    public static readonly MotorKnob AirControlBuild = new()
    {
        Index = 16, Name = "AirControlBuild", Group = "Air", Unit = "fraction",
        Default = 0.70f, Min = 0.00f, Max = 1.00f, Step = 0.01f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 233,
        Get = t => t.AirControlBuild, Set = (t, v) => t with { AirControlBuild = v },
        PinTest = "AirborneControlTests.AirborneRates_AreTheGroundRatesScaled",
        PinSource = "tests/unit/AirborneControlTests.cs (the AirControl [Theory] rows)",
        PinLiteral = "the [InlineData] literal 0.70f",   // MOVE-8: was 0.45f
        PinMin = _ => 0.699f, PinMax = _ => 0.701f,
    };

    public static readonly MotorKnob AirControlTurn = new()
    {
        Index = 17, Name = "AirControlTurn", Group = "Air", Unit = "fraction",
        Default = 0.60f, Min = 0.00f, Max = 1.00f, Step = 0.01f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 245,
        Get = t => t.AirControlTurn, Set = (t, v) => t with { AirControlTurn = v },
        PinTest = "AirborneControlTests.AirborneRates_AreTheGroundRatesScaled",
        PinSource = "tests/unit/AirborneControlTests.cs (the AirControl [Theory] rows)",
        PinLiteral = "the [InlineData] literal 0.60f",   // MOVE-8: was 0.35f
        PinMin = _ => 0.599f, PinMax = _ => 0.601f,
    };

    public static readonly MotorKnob AirControlBrake = new()
    {
        Index = 18, Name = "AirControlBrake", Group = "Air", Unit = "fraction",
        Default = 0.55f, Min = 0.00f, Max = 1.00f, Step = 0.01f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 268,
        Get = t => t.AirControlBrake, Set = (t, v) => t with { AirControlBrake = v },
        PinTest = "AirborneControlTests.AirborneRates_AreTheGroundRatesScaled",
        PinSource = "tests/unit/AirborneControlTests.cs (the AirControl [Theory] rows); also "
                  + "InRange(0.55, 0.70) in ThreeAirFractions_AreInsideTheDesignWindow_AndOrdered "
                  + "and the ordering Brake < Turn < Build in the same test",
        PinLiteral = "the [InlineData] literal 0.55f",   // MOVE-8: was 0.30f
        PinMin = _ => 0.549f, PinMax = _ => 0.551f,
    };

    public static readonly MotorKnob SkidEnterSpeedFraction = new()
    {
        Index = 19, Name = "SkidEnterSpeedFraction", Group = "Skid", Unit = "x MoveSpeed",
        Default = 0.75f, Min = 0.20f, Max = 1.60f, Step = 0.01f,
        BoundReason = "Taste (spec §2.3); the effective m/s is what the pins constrain.",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 150,
        DeclName = "SkidEnterSpeedMps",
        DeclValue = v => $"MoveSpeed * {MotorTuningPrint.Literal(v, 0.01f)}",
        Get = t => t.SkidEnterSpeedFraction, Set = (t, v) => t with { SkidEnterSpeedFraction = v },
        PinTest = "AirborneControlTests.MaximumAirBrakeOverAFullFlight_SitsAboveTheSkidThreshold",
        PinSource = "ceiling tests/unit/AirborneControlTests.cs:218; floor "
                  + "LocomotionTests.Skid_EntersOnlyOnACommittedReversal — tests/unit/LocomotionTests.cs:599",
        PinLiteral = "no literal — the ceiling is `shed > SkidEnterSpeedMps` and the floor is that a "
                   + "walk must not skid; both are properties, not numbers to retype",
        PinQuantity = t => t.SkidEnterSpeedMps, PinQuantityLabel = "effective skid entry, m/s",
        PinMin = _ => 2.43f, PinMax = _ => 4.473f,
    };

    public static readonly MotorKnob SkidAlignmentMax = new()
    {
        Index = 20, Name = "SkidAlignmentMax", Group = "Skid", Unit = "dot",
        Default = -0.50f, Min = -1.00f, Max = 0.00f, Step = 0.01f,
        BoundReason = "Hard ceiling: above zero a reversal test fires on an input ALIGNED with "
                    + "travel, i.e. running forward would skid (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 162,
        Get = t => t.SkidAlignmentMax, Set = (t, v) => t with { SkidAlignmentMax = v },
        PinTest = "LocomotionTests.Skid_EntersOnlyOnACommittedReversal",
        PinSource = "tests/unit/LocomotionTests.cs:608 (a 90-degree turn must not skid) and :612 "
                  + "(a WASD diagonal must)",
        PinLiteral = "no literal — the pin is the WASD geometry, dot = -0.70711 for W->A+S",
        PinMin = _ => -0.70711f, PinMax = _ => -1e-4f,
    };

    public static readonly MotorKnob SkidDeceleration = new()
    {
        Index = 21, Name = "SkidDeceleration", Group = "Skid", Unit = "m/s^2",
        Default = 13f, Min = 2.0f, Max = 40.0f, Step = 0.5f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 183,
        Get = t => t.SkidDeceleration, Set = (t, v) => t with { SkidDeceleration = v },
        PinTest = "LocomotionTests.Skid_AtASprint_SlidesFarEnoughToWatch",
        PinSource = "tests/unit/LocomotionTests.cs:569/:571; floor "
                  + "Skid_HasFourExits_AndNoneOfThemCanBeRefused — :680",
        PinLiteral = "the slide-duration and slide-distance brackets; also the literals `< 18.4f` "
                   + "at LocomotionTests.cs:654 and `> 9.30` at :575",
        PinMin = _ => 9.92f, PinMax = _ => 16.53f,
    };

    public static readonly MotorKnob SkidExitSpeedMps = new()
    {
        Index = 22, Name = "SkidExitSpeedMps", Group = "Skid", Unit = "m/s",
        Default = 1.20f, Min = 0.10f, Max = 4.00f, Step = 0.05f,
        BoundReason = "Hard: it must stay strictly below the skid entry speed — that gap IS the "
                    + "anti-chatter mechanism (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 188,
        Get = t => t.SkidExitSpeedMps, Set = (t, v) => t with { SkidExitSpeedMps = v },
        PinTest = "LocomotionTests.Skid_CannotChatter",
        PinSource = "tests/unit/LocomotionTests.cs:691",
        PinLiteral = "no literal — the assertion is `exit < enter * 0.5`, so the ceiling moves with "
                   + "MoveSpeed and SkidEnterSpeedFraction",
        PinMax = t => 0.5f * t.SkidEnterSpeedMps,
    };

    public static readonly MotorKnob SkidMaxSec = new()
    {
        Index = 23, Name = "SkidMaxSec", Group = "Skid", Unit = "s",
        Default = 0.75f, Min = 0.00f, Max = 2.00f, Step = 0.05f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclLine = 198,
        Get = t => t.SkidMaxSec, Set = (t, v) => t with { SkidMaxSec = v },
        PinTest = "LocomotionTests.Skid_HasFourExits_AndNoneOfThemCanBeRefused",
        PinSource = "tests/unit/LocomotionTests.cs:680; weaker floor via NetCodec.cs:322's clamp, "
                  + "NetCodecRoundTripTests.cs:171/:194",
        PinLiteral = "no literal — the floor is that a full-sprint skid must finish before the "
                   + "ceiling truncates it",
        PinMin = _ => 0.5833f,
    };

    public static readonly MotorKnob LandMinFallMps = new()
    {
        Index = 24, Name = "LandMinFallMps", Group = "Landing", Unit = "m/s",
        Default = 2.5f, Min = 0.5f, Max = 10.0f, Step = 0.1f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/game/sandbox/AvatarVisual.cs", DeclLine = 492,
        Get = t => t.LandMinFallMps, Set = (t, v) => t with { LandMinFallMps = v },
    };

    public static readonly MotorKnob LandFullFallMps = new()
    {
        Index = 25, Name = "LandFullFallMps", Group = "Landing", Unit = "m/s",
        Default = 14f, Min = 3.0f, Max = 30.0f, Step = 0.5f,
        BoundReason = "Hard: it is the divisor in fall / LandFullFallMps, and below LandMinFallMps "
                    + "every qualifying landing clamps to full (spec §2.3).",
        DeclFile = "scripts/game/sandbox/AvatarVisual.cs", DeclLine = 498,
        Get = t => t.LandFullFallMps, Set = (t, v) => t with { LandFullFallMps = v },
    };

    public static readonly MotorKnob TakeoffKickSec = new()
    {
        Index = 26, Name = "TakeoffKickSec", Group = "Landing", Unit = "s",
        Default = 0.16f, Min = 0.02f, Max = 0.60f, Step = 0.01f,
        BoundReason = "Hard floor: a divisor in a normalised envelope (spec §2.3).",
        // MOVE-4c widened this to `public const float` (spec §2.2's note on rows 26-27), so the
        // DeclModifiers override MOVE-4b needed is gone and the row prints the class default —
        // which is now the declaration the file actually carries.
        DeclFile = "scripts/game/sandbox/AvatarVisual.cs", DeclLine = 414,
        Get = t => t.TakeoffKickSec, Set = (t, v) => t with { TakeoffKickSec = v },
    };

    public static readonly MotorKnob TakeoffKickMinDriveFraction = new()
    {
        Index = 27, Name = "TakeoffKickMinDriveFraction", Group = "Landing", Unit = "fraction",
        Default = 0.40f, Min = 0.00f, Max = 1.00f, Step = 0.01f,
        BoundReason = "Taste (spec §2.3).",
        DeclFile = "scripts/game/sandbox/AvatarVisual.cs", DeclLine = 540,   // public since MOVE-4c
        Get = t => t.TakeoffKickMinDriveFraction,
        Set = (t, v) => t with { TakeoffKickMinDriveFraction = v },
    };

    public static readonly MotorKnob CameraDipStrengthM = new()
    {
        Index = 28, Name = "CameraDipStrengthM", Group = "Camera", Unit = "m",
        Default = 0.00f, Min = 0.00f, Max = 0.50f, Step = 0.01f,
        BoundReason = "Comfort labelling (spec §5.5).",
        DeclFile = "scripts/game/sandbox/SandboxCamera.cs", DeclLine = 155,   // built by MOVE-4c
        Get = t => t.CameraDipStrengthM, Set = (t, v) => t with { CameraDipStrengthM = v },
    };

    public static readonly MotorKnob CameraDipAttackSec = new()
    {
        Index = 29, Name = "CameraDipAttackSec", Group = "Camera", Unit = "s",
        Default = 0.05f, Min = 0.01f, Max = 0.30f, Step = 0.01f,
        BoundReason = "Hard floor: a divisor in a normalised envelope (spec §2.3).",
        DeclFile = "scripts/game/sandbox/SandboxCamera.cs", DeclLine = 159,   // built by MOVE-4c
        Get = t => t.CameraDipAttackSec, Set = (t, v) => t with { CameraDipAttackSec = v },
        LiveWhenMoved = "CameraDipStrengthM",
    };

    public static readonly MotorKnob CameraDipRecoverSec = new()
    {
        Index = 30, Name = "CameraDipRecoverSec", Group = "Camera", Unit = "s",
        Default = 0.26f, Min = 0.02f, Max = 1.20f, Step = 0.01f,
        BoundReason = "Hard floor: a divisor in a normalised envelope (spec §2.3).",
        DeclFile = "scripts/game/sandbox/SandboxCamera.cs", DeclLine = 166,   // built by MOVE-4c
        Get = t => t.CameraDipRecoverSec, Set = (t, v) => t with { CameraDipRecoverSec = v },
        LiveWhenMoved = "CameraDipStrengthM",
    };

    /// <summary>
    /// <b>Row 31 — MOVE-4f's, and the only knob in the table whose pin is about perception.</b>
    /// The exponent on the normalised landing fall speed: it is what replaced the dip's fall-speed
    /// gate when Talon took choice C on 2026-08-27, and the reason the dip can be ungated at all.
    ///
    /// <para><b>The pin is stated on the coupled quantity, not on the exponent.</b> Whether a jog
    /// tap is perceptible depends on the ramp, on <c>CameraDipStrengthM</c> and on
    /// <c>LandFullFallMps</c> together, and no bound beside this slider could say that — §3.4's
    /// argument, in its purest form. So the window is drawn on <b>the dip a jog tap would produce
    /// at the strength slider's maximum</b>, which is the worst case this tuning could ever be
    /// dragged to, against <c>SandboxCamera.PerceptibleDipM</c>. At the shipped ramp of 6 that
    /// quantity is 0.00142 m against a 0.0018 m ceiling; at 5 it is 0.00378 and the row goes red.
    /// The arithmetic is <c>SandboxCamera</c>'s own function, called rather than copied — a second
    /// transcription of the dip's curve here is exactly how the two would come to disagree.</para>
    /// </summary>
    public static readonly MotorKnob CameraDipRampPower = new()
    {
        Index = 31, Name = "CameraDipRampPower", Group = "Camera", Unit = "power",
        Default = 6f, Min = 1.0f, Max = 12.0f, Step = 0.5f,
        BoundReason = "Hard floor at 1.0: below linear the curve turns concave and a feather "
                    + "touchdown dips hardest, which is the one shape worse than the gate this "
                    + "replaced. The ceiling is taste (MOVE-4f scope item 3).",
        DeclFile = "scripts/game/sandbox/SandboxCamera.cs", DeclLine = 199,   // built by MOVE-4f
        Get = t => t.CameraDipRampPower, Set = (t, v) => t with { CameraDipRampPower = v },
        LiveWhenMoved = "CameraDipStrengthM",
        PinTest = "ApexHangAndLandingDipTests."
                + "AJogTapStaysBelowThePerceptionFloor_AtTheShippedRampAndTheStrengthKnobsMaximum",
        PinSource = "tests/unit/ApexHangAndLandingDipTests.cs:828",
        PinLiteral = "SandboxCamera.PerceptibleDipM = 0.0018f — 0.10 px at the 56 px/m MOVE-4e "
                   + "measured its horizon tracker resolving",
        PinQuantity = t => CameraDipStrengthM.Max
                         * SandboxCamera.DipIntensityFor(SandboxCamera.JogTapLandingMps,
                             t.LandFullFallMps, t.CameraDipRampPower),
        PinQuantityLabel = "jog-tap dip at max strength, m",
        PinMax = _ => SandboxCamera.PerceptibleDipM,
    };

    // =============================================================================================
    // MOVE-5 — the verb state machine (spec §11.1, rows 31-52). Twenty-two rows, transcribed.
    //
    // §11.4's standing rule is honoured literally in the pins below: a test written against these
    // rows may assert a PROPERTY, never a FEEL LITERAL. So the four coupled rows (33/34, 37/38) and
    // the two WIRE-WIDTH rows (45, 47) carry pins, and the nine rows §11.4 names as "must stay free
    // to move" — 31, 36, 39, 41, 42, 43, 48, 50, 52 — carry none, along with SlideDeceleration's
    // value inside its bounds. A pin on any of those would have decided a feel question that is
    // Talon's.
    //
    // §11.5's shape finding, recorded rather than acted on here: knobs 32, 40, 45, 46 and 47 are
    // DISCRETE and ride as floats with Step = 1 and an integer range, which is correct and needs no
    // change to MotorTuning. A discrete widget for Step == 1 rows is MOVE-5d's, not this table's.
    // =============================================================================================

    public static readonly MotorKnob JumpHoldWindowSec = new()
    {
        Index = 31, Name = "JumpHoldWindowSec", Group = "Slide", Unit = "s",
        Default = 0.20f, Min = 0.05f, Max = 0.60f, Step = 0.01f,
        BoundReason = "Taste (spec §11.1). The window is the whole input grammar of the crouch "
                    + "verbs and Talon has not felt it yet.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.JumpHoldWindowSec, Set = (t, v) => t with { JumpHoldWindowSec = v },
    };

    public static readonly MotorKnob TouchdownSlideImmediate = new()
    {
        Index = 32, Name = "TouchdownSlideImmediate", Group = "Slide", Unit = "0/1",
        Default = 0f, Min = 0f, Max = 1f, Step = 1f,
        BoundReason = "A toggle: 1 starts the verb on the touchdown tick, 0 waits out the hold "
                    + "window on the ground (spec §4.3). Both behaviours are built.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.TouchdownSlideImmediate,
        Set = (t, v) => t with { TouchdownSlideImmediate = v },
    };

    public static readonly MotorKnob SlideEnterSpeedFraction = new()
    {
        Index = 33, Name = "SlideEnterSpeedFraction", Group = "Slide", Unit = "x MoveSpeed",
        Default = 1.22f, Min = 0.50f, Max = 2.00f, Step = 0.01f,
        BoundReason = "Hard against SlideExitSpeedMps: the gap between entry and exit IS the "
                    + "anti-chatter mechanism (spec §11.3), the same law the skid pair carries.",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclName = "SlideEnterSpeedMps",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is the PRODUCT, beside the other MOVE-5 knobs.",
        Get = t => t.SlideEnterSpeedFraction,
        Set = (t, v) => t with { SlideEnterSpeedFraction = v },
        PinTest = "MovementVerbTests.TheSlidesAntiChatterGapHolds_AtEveryLegalTuning",
        PinSource = "tests/unit/MovementVerbTests.cs",
        PinLiteral = "no literal — the pin is the INEQUALITY SlideExitSpeedMps < "
                   + "SlideEnterSpeedFraction x MoveSpeed, not a value",
        PinQuantity = t => t.SlideEnterSpeedMps,
        PinQuantityLabel = "slide entry speed, m/s",
        PinMin = t => t.SlideExitSpeedMps + 0.05f,
    };

    public static readonly MotorKnob SlideExitSpeedMps = new()
    {
        Index = 34, Name = "SlideExitSpeedMps", Group = "Slide", Unit = "m/s",
        Default = 2.00f, Min = 0.10f, Max = 6.00f, Step = 0.05f,
        BoundReason = "Hard against the slide entry speed — the same anti-chatter gap read from "
                    + "the other end (spec §11.3).",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.SlideExitSpeedMps, Set = (t, v) => t with { SlideExitSpeedMps = v },
        PinTest = "MovementVerbTests.TheSlidesAntiChatterGapHolds_AtEveryLegalTuning",
        PinSource = "tests/unit/MovementVerbTests.cs",
        PinLiteral = "no literal — the pin is the same inequality row 33 carries",
        PinMax = t => t.SlideEnterSpeedMps - 0.05f,
    };

    public static readonly MotorKnob SlideDeceleration = new()
    {
        Index = 35, Name = "SlideDeceleration", Group = "Slide", Unit = "m/s^2",
        Default = 6.0f, Min = 0.5f, Max = 40.0f, Step = 0.5f,
        BoundReason = "HARD FLOOR at 0.5: at 0 the speed exit never fires and only SlideMaxSec "
                    + "ends the slide, which at that knob's own maximum is a 3-second unsteerable "
                    + "state reached by two sliders neither of which is wrong alone (spec §11.3). "
                    + "The value inside the bounds is taste and stays free (§11.4).",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.SlideDeceleration, Set = (t, v) => t with { SlideDeceleration = v },
    };

    public static readonly MotorKnob SlideTurnRateDeg = new()
    {
        Index = 36, Name = "SlideTurnRateDeg", Group = "Slide", Unit = "deg/s",
        Default = 90f, Min = 0f, Max = 360f, Step = 5f,
        BoundReason = "Taste (spec §11.1). 0 is a legal setting — it is the skid's own "
                    + "steering-suppressed behaviour, which is a comparison worth being able to "
                    + "make with a slider.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.SlideTurnRateDeg, Set = (t, v) => t with { SlideTurnRateDeg = v },
    };

    public static readonly MotorKnob SlideMinSec = new()
    {
        Index = 37, Name = "SlideMinSec", Group = "Slide", Unit = "s",
        Default = 0.12f, Min = 0.00f, Max = 0.50f, Step = 0.01f,
        BoundReason = "Hard against SlideMaxSec: the two duration exits must not overlap or "
                    + "spec §3.4's \"cannot overlap\" row becomes a lie (spec §11.3).",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.SlideMinSec, Set = (t, v) => t with { SlideMinSec = v },
        PinTest = "MovementVerbTests.TheSlidesTwoDurationExitsCannotOverlap_AtEveryLegalTuning",
        PinSource = "tests/unit/MovementVerbTests.cs",
        PinLiteral = "no literal — the pin is the inequality SlideMinSec < SlideMaxSec",
        PinMax = t => t.SlideMaxSec - 0.01f,
    };

    public static readonly MotorKnob SlideMaxSec = new()
    {
        Index = 38, Name = "SlideMaxSec", Group = "Slide", Unit = "s",
        Default = 1.40f, Min = 0.10f, Max = 3.00f, Step = 0.05f,
        BoundReason = "Hard against SlideMinSec (spec §11.3). The ceiling itself is the guard that "
                    + "keeps a low SlideDeceleration from producing a state with no exit.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.SlideMaxSec, Set = (t, v) => t with { SlideMaxSec = v },
        PinTest = "MovementVerbTests.TheSlidesTwoDurationExitsCannotOverlap_AtEveryLegalTuning",
        PinSource = "tests/unit/MovementVerbTests.cs",
        PinLiteral = "no literal — the pin is the same inequality row 37 carries",
        PinMin = t => t.SlideMinSec + 0.01f,
    };

    public static readonly MotorKnob DuckWalkSpeedFraction = new()
    {
        Index = 39, Name = "DuckWalkSpeedFraction", Group = "Slide", Unit = "x MoveSpeed",
        Default = 0.45f, Min = 0.10f, Max = 1.00f, Step = 0.01f,
        BoundReason = "The 1.00 ceiling is TASTE, not safety — above a jog the duck walk stops "
                    + "being a duck walk. The safety is structural: no tuning can create a "
                    + "DuckWalk -> Slide cycle, because spec §3.3 has no such row.",
        DeclFile = "scripts/net/AvatarMotor.cs", DeclName = "DuckWalkSpeedMps",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is the PRODUCT, beside the other MOVE-5 knobs.",
        Get = t => t.DuckWalkSpeedFraction, Set = (t, v) => t with { DuckWalkSpeedFraction = v },
    };

    public static readonly MotorKnob DuckWalkGuaranteed = new()
    {
        Index = 40, Name = "DuckWalkGuaranteed", Group = "Slide", Unit = "0/1",
        Default = 0f, Min = 0f, Max = 1f, Step = 1f,
        BoundReason = "A toggle: 1 makes every settling slide latch, 0 requires the stick inside "
                    + "the cone (spec §5.2). 0 is the brief's literal reading.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.DuckWalkGuaranteed, Set = (t, v) => t with { DuckWalkGuaranteed = v },
    };

    public static readonly MotorKnob DuckWalkEntryConeDeg = new()
    {
        Index = 41, Name = "DuckWalkEntryConeDeg", Group = "Slide", Unit = "deg",
        Default = 60f, Min = 0f, Max = 180f, Step = 5f,
        BoundReason = "Taste (spec §11.1), and inert at DuckWalkGuaranteed = 1. 180 is 'any "
                    + "direction at all' and 0 is 'exactly along travel' — both ends legal.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.DuckWalkEntryConeDeg, Set = (t, v) => t with { DuckWalkEntryConeDeg = v },
        LiveWhenMoved = "DuckWalkGuaranteed",
    };

    public static readonly MotorKnob ChainGraceSec = new()
    {
        Index = 42, Name = "ChainGraceSec", Group = "Chain", Unit = "s",
        Default = 0.35f, Min = 0.05f, Max = 2.00f, Step = 0.01f,
        BoundReason = "Taste (spec §11.1). Whether 0.35 s reads as generous is a hands question.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.ChainGraceSec, Set = (t, v) => t with { ChainGraceSec = v },
        LiveWhenMoved = "ChainBonusMps",
    };

    public static readonly MotorKnob ChainDecayIntervalSec = new()
    {
        Index = 43, Name = "ChainDecayIntervalSec", Group = "Chain", Unit = "s",
        Default = 0.50f, Min = 0.05f, Max = 3.00f, Step = 0.05f,
        BoundReason = "Taste (spec §11.1).",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.ChainDecayIntervalSec,
        Set = (t, v) => t with { ChainDecayIntervalSec = v },
        LiveWhenMoved = "ChainBonusMps",
    };

    /// <summary>
    /// <b>Row 44 — the wave's one no-op-on-arrival knob that is not a mode switch.</b> 0.00 is the
    /// exact no-op and it is what ships, for a reason the rest of the crouch family does not share
    /// (§11.2): <b>the chain is the only thing in this wave that fires with no new input at all.</b>
    /// Every ordinary hop chain in the playground's flow course would change shape the moment this
    /// leaves zero, and that course is the baseline the lab exists to compare against.
    /// </summary>
    public static readonly MotorKnob ChainBonusMps = new()
    {
        Index = 44, Name = "ChainBonusMps", Group = "Chain", Unit = "m/s",
        Default = 0.00f, Min = 0.00f, Max = 3.00f, Step = 0.05f,
        BoundReason = "0.00 is the EXACT no-op and is what ships (spec §11.2). The 3.00 ceiling is "
                    + "taste, bounded well below the point where a chained wish outruns the ground "
                    + "acceleration that has to reach it.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.ChainBonusMps, Set = (t, v) => t with { ChainBonusMps = v },
    };

    /// <summary>
    /// <b>Row 45 — a WIRE WIDTH, and that is a different kind of bound from every other row in
    /// this table.</b> <c>MoveState.ChainDepth</c> is three bits of the snapshot's <c>flags2</c>
    /// byte. A slider past 7 does not produce a bad feel; it produces a truncated field and a peer
    /// whose chain depth disagrees with the server's. <b>It must be clamped in the validator, not
    /// merely in the widget</b> (§11.3) — which is what the pin below asserts.
    /// </summary>
    public static readonly MotorKnob ChainMaxDepth = new()
    {
        Index = 45, Name = "ChainMaxDepth", Group = "Chain", Unit = "count",
        Default = 4f, Min = 0f, Max = 7f, Step = 1f,
        BoundReason = "HARD MAX 7 — it is the WIRE WIDTH of MoveState.ChainDepth (3 bits of the "
                    + "snapshot flags2 byte), not taste. Past it the field truncates and a peer's "
                    + "depth disagrees with the server's (spec §11.3).",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.ChainMaxDepth, Set = (t, v) => t with { ChainMaxDepth = v },
        PinTest = "MovementVerbTests.TheTwoWireWidthKnobsAreClampedByTheValidator_NotByAWidget",
        PinSource = "tests/unit/MovementVerbTests.cs",
        PinLiteral = "no literal — the pin is the 3-bit field width NetCodec's flags2 carries",
        PinMax = _ => 7f,
    };

    public static readonly MotorKnob AirJumpMode = new()
    {
        Index = 46, Name = "AirJumpMode", Group = "Air", Unit = "0/1/2",
        Default = 1f, Min = 0f, Max = 2f, Step = 1f,
        BoundReason = "0 = off and is the EXACT no-op; 1 = traditional; 2 = the Kick. Ships at 0 "
                    + "because a double jump changes what every gap in the playground means "
                    + "(spec §11.2).",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.AirJumpMode, Set = (t, v) => t with { AirJumpMode = v },
    };

    /// <summary><b>Row 47 — the second WIRE WIDTH.</b> <c>MoveState.AirJumpsUsed</c> is two bits.
    /// Same argument as row 45, same validator clamp, same pin.</summary>
    public static readonly MotorKnob AirJumpCountMax = new()
    {
        Index = 47, Name = "AirJumpCountMax", Group = "Air", Unit = "count",
        Default = 1f, Min = 0f, Max = 3f, Step = 1f,
        BoundReason = "HARD MAX 3 — the WIRE WIDTH of MoveState.AirJumpsUsed (2 bits of flags2), "
                    + "not taste (spec §11.3).",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.AirJumpCountMax, Set = (t, v) => t with { AirJumpCountMax = v },
        LiveWhenMoved = "AirJumpMode",
        PinTest = "MovementVerbTests.TheTwoWireWidthKnobsAreClampedByTheValidator_NotByAWidget",
        PinSource = "tests/unit/MovementVerbTests.cs",
        PinLiteral = "no literal — the pin is the 2-bit field width NetCodec's flags2 carries",
        PinMax = _ => 3f,
    };

    public static readonly MotorKnob AirJumpVelocityFraction = new()
    {
        Index = 48, Name = "AirJumpVelocityFraction", Group = "Air", Unit = "x JumpVelocity",
        Default = 0.80f, Min = 0.10f, Max = 1.50f, Step = 0.05f,
        BoundReason = "Taste (spec §11.1), and inert unless AirJumpMode = 1.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.AirJumpVelocityFraction,
        Set = (t, v) => t with { AirJumpVelocityFraction = v },
        LiveWhenMoved = "AirJumpMode",
    };

    public static readonly MotorKnob KickConversionFraction = new()
    {
        Index = 49, Name = "KickConversionFraction", Group = "Air", Unit = "fraction",
        Default = 0.35f, Min = 0.00f, Max = 1.00f, Step = 0.01f,
        BoundReason = "HARD MAX 1.00: above 1 the Kick returns more speed than the fall carried — "
                    + "energy from nothing, and precisely the floaty read the variant exists to "
                    + "avoid (spec §11.3).",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.KickConversionFraction,
        Set = (t, v) => t with { KickConversionFraction = v },
        LiveWhenMoved = "AirJumpMode",
    };

    public static readonly MotorKnob KickHorizontalGainMps = new()
    {
        Index = 50, Name = "KickHorizontalGainMps", Group = "Air", Unit = "m/s",
        Default = 1.20f, Min = 0.00f, Max = 5.00f, Step = 0.05f,
        BoundReason = "Taste (spec §11.1), and inert unless AirJumpMode = 2.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.KickHorizontalGainMps,
        Set = (t, v) => t with { KickHorizontalGainMps = v },
        LiveWhenMoved = "AirJumpMode",
    };

    public static readonly MotorKnob KickVerticalMps = new()
    {
        Index = 51, Name = "KickVerticalMps", Group = "Air", Unit = "m/s",
        Default = 1.60f, Min = 0.00f, Max = 6.00f, Step = 0.05f,
        BoundReason = "HARD MAX 6.00: at 8.4 it would equal JumpVelocity and mode 2 would become "
                    + "mode 1 wearing mode 2's name. Two modes that do the same thing is worse "
                    + "than one (spec §11.3).",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.KickVerticalMps, Set = (t, v) => t with { KickVerticalMps = v },
        LiveWhenMoved = "AirJumpMode",
    };

    public static readonly MotorKnob KickMinSpeedMps = new()
    {
        Index = 52, Name = "KickMinSpeedMps", Group = "Air", Unit = "m/s",
        Default = 4.05f, Min = 0.00f, Max = 12.00f, Step = 0.05f,
        BoundReason = "Taste (spec §11.1), and inert unless AirJumpMode = 2. 4.05 at the shipped "
                    + "tuning is SkidEnterSpeedMps — deliberately the same committed-run line.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5 row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d), and the accessor AvatarMotor exposes for this row is beside the other MOVE-5 knobs.",
        Get = t => t.KickMinSpeedMps, Set = (t, v) => t with { KickMinSpeedMps = v },
        LiveWhenMoved = "AirJumpMode",
    };

    // --- MOVE-5f, the anticipation: rows 53-57, one new group ------------------------------------
    //
    // ONE GROUP RATHER THAN TWO, and the reason is the panel rather than taste. MotorTuningSession
    // .BuildGroups derives its sections from CONSECUTIVE RUNS of the same group name, so splitting
    // these five between "Gravity" (mode 2 is genuinely a gravity term) and a new section would
    // either produce two sections called "Gravity" or bury the mode toggle three sections away from
    // the pair it switches on. They are one feature and Talon flips one switch to feel it, so they
    // are one section, appended after Chain.

    /// <summary><b>Row 53 — the toggle the brief asks for, plus off.</b> The one row of the five
    /// that is not inert: it is what makes the other four mean anything.</summary>
    public static readonly MotorKnob AnticipationMode = new()
    {
        Index = 53, Name = "AnticipationMode", Group = "Anticipation", Unit = "0/1/2",
        Default = 0f, Min = 0f, Max = 2f, Step = 1f,
        BoundReason = "0 = off and is the EXACT no-op; 1 = the real coil (POSE ONLY - the approved "
                    + "arc is unchanged to the bit at it); 2 = the coil baked into the rise curve "
                    + "(a GRAVITY term - it moves the arc by construction). Ships at 0 because 1 "
                    + "still changes the body Talon approved, and the lab exists so he picks with "
                    + "his hands rather than on paper.",
        DeclFile = "scripts/net/MotorTuning.cs",
        PlacementHint = "MOVE-5f row: there is no `const` to point at. The paste target is MotorTuning.Default's initializer (MOVE-4d); the accessors are AvatarMotor.AnticipationMode and AvatarVisual's coil block.",
        Get = t => t.AnticipationMode, Set = (t, v) => t with { AnticipationMode = v },
    };

    /// <summary><b>Row 54 — "coil depth", the brief's own words.</b> How much of
    /// <c>AnticipationCoil</c>'s authored amplitude a fully-committed hold reaches.</summary>
    public static readonly MotorKnob AnticipationDepth = new()
    {
        Index = 54, Name = "AnticipationDepth", Group = "Anticipation", Unit = "fraction",
        Default = 0.60f, Min = 0.00f, Max = 1.00f, Step = 0.05f,
        BoundReason = "HARD MAX 1.00: the amplitudes in AnticipationCoil ARE the full pose, chosen "
                    + "against MaxLimbFold and MaxBodyTilt. Past 1 the knee fold would spend its "
                    + "life pinned at MaxLimbFold, where a deeper number changes nothing visible "
                    + "and the slider lies. 0.00 makes mode 1 an exact no-op.",
        DeclFile = "scripts/game/sandbox/AnticipationCoil.cs",
        PlacementHint = "MOVE-5f row: the per-channel amplitudes are consts in AnticipationCoil; this is the single multiplier over them. Paste target is MotorTuning.Default's initializer.",
        Get = t => t.AnticipationDepth, Set = (t, v) => t with { AnticipationDepth = v },
        LiveWhenMoved = "AnticipationMode",
    };

    /// <summary><b>Row 55 — coil depth AGAINST hold duration</b>, which the brief asks for as its
    /// own knob. Seconds of rise at which the coil reaches <see cref="AnticipationDepth"/>.</summary>
    public static readonly MotorKnob AnticipationCoilSec = new()
    {
        Index = 55, Name = "AnticipationCoilSec", Group = "Anticipation", Unit = "s",
        Default = 0.22f, Min = 0.02f, Max = 1.00f, Step = 0.01f,
        BoundReason = "HARD FLOOR 0.02: it is the divisor in riseSec / coilSec, so zero is a "
                    + "divide-by-zero on every rising tick. Ceiling 1.00 s is past the longest "
                    + "ascent this motor can make (0.382 s held at the shipped gravity), where the "
                    + "coil never reaches full depth on any jump at all.",
        DeclFile = "scripts/game/sandbox/AnticipationCoil.cs",
        PlacementHint = "MOVE-5f row: AnticipationCoil.Depth's window. Paste target is MotorTuning.Default's initializer.",
        Get = t => t.AnticipationCoilSec, Set = (t, v) => t with { AnticipationCoilSec = v },
        LiveWhenMoved = "AnticipationMode",
    };

    /// <summary><b>Row 56 — approach 2's strength.</b> Pinned by the same window the apex hang is,
    /// and for the same reason: it is a gravity term, so a big enough value walks the approved arc
    /// out of <c>JumpApex_BracketsTheSpecRange</c>. The ceiling is bisected live because the
    /// constraint is coupled to <c>JumpVelocity</c>, <c>Gravity</c> and the window.</summary>
    public static readonly MotorKnob AnticipationBakedStrength = new()
    {
        Index = 56, Name = "AnticipationBakedStrength", Group = "Anticipation", Unit = "fraction",
        Default = 0.35f, Min = 0.00f, Max = 2.00f, Step = 0.05f,
        BoundReason = "HARD MAX 2.00: at 2 the opening frames of the rise run at 3x gravity, which "
                    + "is exactly JumpReleaseGravityMultiplier - the point where a held jump's "
                    + "first frames are indistinguishable from a tapped one's and the coil has "
                    + "eaten the variable jump it was supposed to decorate. 0.00 is the exact "
                    + "no-op.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5f row: AvatarMotor.LaunchCoilFactor's strength, beside JumpReleaseGravityMultiplier. Paste target is MotorTuning.Default's initializer.",
        Get = t => t.AnticipationBakedStrength,
        Set = (t, v) => t with { AnticipationBakedStrength = v },
        LiveWhenMoved = "AnticipationMode",
        PinTest = "AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately",
        PinSource = "tests/unit/AirborneControlTests.cs:336-348",
        PinLiteral = "Near(1.60f, apexHeld, 0.15f) — the held-apex FLOOR of 1.45 m, since baked "
                   + "gravity takes height away rather than adding it",
        PinMax = MotorTuningInvariants.LaunchCoilStrengthCeiling,
    };

    /// <summary><b>Row 57 — approach 2's window</b>, in the only currency <c>GravityFor</c> has:
    /// a share of the launch speed. Same joint pin as row 56, exactly as
    /// <see cref="ApexHangWindowMps"/> shares <see cref="ApexHangStrength"/>'s.</summary>
    public static readonly MotorKnob AnticipationBakedWindow = new()
    {
        Index = 57, Name = "AnticipationBakedWindow", Group = "Anticipation", Unit = "fraction",
        Default = 0.35f, Min = 0.05f, Max = 1.00f, Step = 0.05f,
        BoundReason = "HARD FLOOR 0.05: it is the divisor in (launch - v_y) / (launch x window), "
                    + "so zero is a divide-by-zero on every rising tick. Ceiling 1.00: at 1 the "
                    + "term spans the WHOLE rise and stops being 'the first few frames' the brief "
                    + "asked for - that is a gravity retune wearing the coil's name.",
        DeclFile = "scripts/net/AvatarMotor.cs",
        PlacementHint = "MOVE-5f row: AvatarMotor.LaunchCoilFactor's window, beside JumpReleaseGravityMultiplier. Paste target is MotorTuning.Default's initializer.",
        Get = t => t.AnticipationBakedWindow,
        Set = (t, v) => t with { AnticipationBakedWindow = v },
        LiveWhenMoved = "AnticipationMode",
        PinTest = "AirborneControlTests.JumpApex_BracketsTheSpecRange_HeldVersusReleasedImmediately",
        PinSource = "tests/unit/AirborneControlTests.cs:336-348",
        PinLiteral = "joint with AnticipationBakedStrength — the held-apex floor of 1.45 m",
        PinMax = MotorTuningInvariants.LaunchCoilWindowCeiling,
    };

    /// <summary>All fifty-eight rows, in knob-table order — the print order and the validation
    /// order.</summary>
    /// <remarks>
    /// <b>The order is by GROUP, not by <see cref="MotorKnob.Index"/>, and that is deliberate.</b>
    /// <c>MotorTuningSession.BuildGroups</c> derives the panel's sections from <i>consecutive runs
    /// of the same group name</i> in this list. MOVE-5 puts seven new rows (46-52) into MOVE-4's
    /// existing <b>Air</b> group per spec §11.1, so appending them at the end of the list would
    /// have produced <i>two sections both called "Air"</i> in the panel — a defect visible to
    /// Talon and to nobody's test. They are inserted after <c>AirControlBrake</c> instead, which
    /// costs nothing but a non-monotonic <c>Index</c> column inside that one section.
    ///
    /// <para><c>Index</c> stays the spec's own row number in every case, because it is a stable
    /// identifier a handoff, a pin note and a design document can all refer to; the list order is
    /// layout. The two were the same thing for thirty-one rows and stopped being the same thing the
    /// moment a wave extended an existing group rather than adding a new one.</para>
    /// </remarks>
    public static readonly IReadOnlyList<MotorKnob> All = new[]
    {
        MoveSpeed, SprintMultiplier, Acceleration, Deceleration, TurnAcceleration, TurnLerp,
        AimTurnLerp, BodyYawFollowsAim,
        Gravity, FallGravityMultiplier, ApexHangStrength, ApexHangWindowMps,
        JumpVelocity, JumpReleaseGravityMultiplier, CoyoteTimeSec, JumpBufferSec,
        AirControlBuild, AirControlTurn, AirControlBrake,
        AirJumpMode, AirJumpCountMax, AirJumpVelocityFraction,
        KickConversionFraction, KickHorizontalGainMps, KickVerticalMps, KickMinSpeedMps,
        SkidEnterSpeedFraction, SkidAlignmentMax, SkidDeceleration, SkidExitSpeedMps, SkidMaxSec,
        LandMinFallMps, LandFullFallMps, TakeoffKickSec, TakeoffKickMinDriveFraction,
        CameraDipStrengthM, CameraDipAttackSec, CameraDipRecoverSec, CameraDipRampPower,
        JumpHoldWindowSec, TouchdownSlideImmediate, SlideEnterSpeedFraction, SlideExitSpeedMps,
        SlideDeceleration, SlideTurnRateDeg, SlideMinSec, SlideMaxSec, DuckWalkSpeedFraction,
        DuckWalkGuaranteed, DuckWalkEntryConeDeg,
        ChainGraceSec, ChainDecayIntervalSec, ChainBonusMps, ChainMaxDepth,

        AnticipationMode, AnticipationDepth, AnticipationCoilSec,
        AnticipationBakedStrength, AnticipationBakedWindow,
    };

    private static readonly Dictionary<string, MotorKnob> ByName =
        All.ToDictionary(k => k.Name, StringComparer.Ordinal);

    /// <summary>Looks a row up by field name; <c>false</c> for a name the table does not carry,
    /// which is how a removed knob in a saved file is ignored rather than fatal (§6.3).</summary>
    public static bool TryByName(string name, out MotorKnob knob) => ByName.TryGetValue(name, out knob!);
}
