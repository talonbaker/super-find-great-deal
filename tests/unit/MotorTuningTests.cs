using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The tuning seam</b> — MOVE-4b against <c>docs/design/2026-08-26-movement-tuning-surface.md</c>
/// §6 (the object and its file), §7 (the prediction-parity guard) and §9 (the const-to-property
/// conversion).
///
/// <para><b>Nothing here leaves <see cref="MotorTuning.Current"/> at a non-default value.</b> The
/// movement suite reads <c>AvatarMotor</c>'s properties live and xUnit runs test classes in
/// parallel, so a test that parked a tuned value would show up as an intermittent red somewhere
/// else entirely. The one test that must prove the writer actually writes moves
/// <c>CameraDipStrengthM</c> and restores it in a <c>finally</c>.</para>
///
/// <para><b>MOVE-4c gave <c>CameraDipStrengthM</c> a reader — <c>SandboxCamera</c> — and it is
/// still the right knob to move here.</b> The property that matters is not "nothing reads it", it
/// is "nothing ANOTHER xUnit CLASS reads can observe the brief window", and <c>SandboxCamera</c> is
/// a Godot Node that no test in this suite instantiates or ticks. The five knobs to avoid are the
/// ones <c>AvatarMotor</c>'s pure functions read, because seven test classes exercise those live.
/// <b>MOVE-4c added no test that parks anything</b>: its two new shapes are pure functions of their
/// arguments, so every non-default value it exercises is passed in rather than applied.</para>
/// </summary>
[Collection(MotorTuningStaticsCollection.Name)]
public class MotorTuningTests
{
    private static readonly DateTimeOffset Stamp =
        new(2026, 8, 26, 17, 42, 3, TimeSpan.Zero);

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "sail-motor-tuning-" + Guid.NewGuid().ToString("N"),
            "movement-tuning.json");

    private static void Cleanup(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* a temp directory that will not delete is not a test failure */ }
    }

    /// <summary>A tuning in which every one of the fifty-eight rows differs from its default, built
    /// by stepping each knob three steps off its shipped value and clamping into range.
    ///
    /// <para><b>MOVE-5 added a case three steps cannot reach:</b> five of its rows are DISCRETE
    /// toggles whose whole range is one or two steps wide (<c>TouchdownSlideImmediate</c> and
    /// <c>DuckWalkGuaranteed</c> are 0/1, <c>AirJumpMode</c> is 0/1/2). Three steps in either
    /// direction clamps them straight back onto their default, and the helper would then silently
    /// stop moving the row it claims to move. The single-step fallback is what keeps this helper's
    /// promise — the property it exists for is "every row moved", not "moved by three".</para></summary>
    private static MotorTuning EverythingMoved()
    {
        MotorTuning t = MotorTuning.Default;
        foreach (MotorKnob knob in MotorTuningKnobs.All)
        {
            float up = knob.Default + knob.Step * 3f;
            float value = up <= knob.Max ? up : knob.Default - knob.Step * 3f;
            if (value < knob.Min || value > knob.Max || value == knob.Default)
                value = knob.Default + knob.Step <= knob.Max
                    ? knob.Default + knob.Step
                    : knob.Default - knob.Step;
            t = knob.Set(t, Math.Clamp(value, knob.Min, knob.Max));
        }
        return MotorTuning.Validate(t, out _);
    }

    // =============================================================================================
    // 1. The const-to-property seam (§9.1, acceptance criterion 2).
    // =============================================================================================

    /// <summary>
    /// <b>A mixed presence/absence check, with its positive control built in.</b> Every feel
    /// constant the knob table names must now be a <i>property</i> on <c>AvatarMotor</c> and must
    /// no longer be a field; <c>TickRate</c> and <c>TickDelta</c> must still be <c>const</c>
    /// (§2.4 — they alias <c>NetProfile</c> and they are the simulation rate, not feel).
    ///
    /// <para><b>The absence half is worthless on its own</b>, so the presence half runs first and
    /// over the same mechanism: if reflection could not tell a converted constant from an
    /// unconverted one, the twenty-one assertions above the simulation-rate ones would fail. That
    /// is the positive control — "TickDelta is still const" is only meaningful because this method
    /// has already demonstrated it can see the difference.</para>
    /// </summary>
    [Fact]
    public void EveryFeelConstantIsNowAProperty_AndTheSimulationRateIsStillConst()
    {
        Type motor = typeof(AvatarMotor);

        // Positive control + the presence half, in one pass.
        MotorKnob[] owned = MotorTuningKnobs.All
            .Where(k => k.DeclFile == "scripts/net/AvatarMotor.cs" && k.DeclLine > 0)
            .ToArray();
        Assert.Equal(21, owned.Length);

        foreach (MotorKnob knob in owned)
        {
            string name = knob.DeclName.Length > 0 ? knob.DeclName : knob.Name;
            Assert.True(motor.GetProperty(name, BindingFlags.Public | BindingFlags.Static) is not null,
                $"AvatarMotor.{name} is not a static property — the seam did not convert it");
            Assert.True(motor.GetField(name, BindingFlags.Public | BindingFlags.Static) is null,
                $"AvatarMotor.{name} is still a field — the seam did not convert it");
        }

        // The absence half, now that the mechanism above has proved it can tell them apart.
        foreach (string name in new[] { "TickRate", "TickDelta" })
        {
            FieldInfo? field = motor.GetField(name, BindingFlags.Public | BindingFlags.Static);
            Assert.True(field is not null, $"AvatarMotor.{name} disappeared");
            Assert.True(field!.IsLiteral, $"AvatarMotor.{name} is no longer a const — §2.4 says it "
                                        + "must stay one: it is the simulation rate, not feel");
        }

        // §2.4's two "lying knobs" and the anti-cheat clamp are excluded from the table AND stay
        // constants. A slider on any of them would move nothing, or would move an attack surface.
        foreach (string name in new[] { "LandingSpeedMultiplier", "MinSpeedFactor" })
        {
            FieldInfo? field = motor.GetField(name, BindingFlags.Public | BindingFlags.Static);
            Assert.True(field is not null && field.IsLiteral,
                $"AvatarMotor.{name} must stay a const — spec §2.4");
            Assert.DoesNotContain(MotorTuningKnobs.All, k => k.Name == name);
        }
    }

    /// <summary>
    /// <b>The mirror's positive control.</b> <c>MotorTuningInvariants.GravityFor</c> re-implements
    /// <c>AvatarMotor.GravityFor</c> so an unapplied tuning can be measured; a mirror that drifts
    /// silently reports the wrong invariants forever. This sweeps both over every branch and both
    /// sides of the boundary at exactly zero.
    ///
    /// <para><b>MOVE-4c: when the apex-hang term lands, add it to both.</b> This test is what tells
    /// you which one you forgot.</para>
    /// </summary>
    [Fact]
    public void TheInvariantMirrorAgreesWithTheMotor_AcrossEveryBranch()
    {
        foreach (float vy in new[] { -9f, -1f, -1e-6f, 0f, 1e-6f, 1f, 8.4f })
        foreach (bool held in new[] { false, true })
        foreach (bool locked in new[] { false, true })
        {
            Assert.Equal(
                AvatarMotor.GravityFor(vy, held, locked),
                MotorTuningInvariants.GravityFor(MotorTuning.Default, vy, held, locked));
        }
    }

    /// <summary>
    /// <b>§9.3's live defect, fixed.</b> <c>ResolveYaw</c>'s aimed path clamped its lerp factor and
    /// the travel path did not, so any <c>TurnLerp</c> above 60 at 60 Hz produced a factor above 1
    /// and the facing overshot and then oscillated — frame-rate dependently, inside a function the
    /// netcode requires to be deterministic. It could not fire while <c>TurnLerp</c> was hard-coded
    /// at 12; giving it a slider is what would have armed it.
    ///
    /// <para>The assertion is the property, not the constant: with a lerp factor of exactly 1 the
    /// body lands <i>on</i> its target, and with anything larger an unclamped lerp would sail past
    /// it. Driving the factor to 3 with a large <c>dt</c> is how this test reaches the case without
    /// touching <see cref="MotorTuning.Current"/>.</para>
    /// </summary>
    [Fact]
    public void ResolveYaw_NeverOvershootsItsTarget_HoweverLargeTheLerpFactor()
    {
        var wish = new Godot.Vector3(0f, 0f, -1f);       // face +Z... atan2(-0, 1) = 0 rad
        float target = MathF.Atan2(-wish.X, -wish.Z);
        float start = target - 1.0f;                      // a radian away

        // TurnLerp is 12 at the shipped tuning, so a dt of 0.25 s gives a factor of exactly 3.
        float yaw = AvatarMotor.ResolveYaw(start, wish, faceYaw: null, dt: 0.25f);

        Assert.Equal(target, yaw, 1e-4f);
        Assert.True(MathF.Abs(Godot.Mathf.AngleDifference(yaw, target)) <= 1e-4f,
            $"the facing overshot its target by {Godot.Mathf.AngleDifference(yaw, target):F4} rad — "
            + "the travel path's lerp factor is unclamped again (spec §9.3)");
    }

    // =============================================================================================
    // 2. The prediction-parity guard (§7, acceptance criterion 8).
    // =============================================================================================

    /// <summary><b>The guard refuses, and refuses without a throw</b> — the caller is a slider
    /// callback, and a throw there kills the panel mid-drag and leaves the tuning half-applied
    /// across fields, which is a worse state than the one being prevented.</summary>
    [Fact]
    public void WhileASessionIsLive_TryApplyRefuses_AndCurrentIsUntouched()
    {
        try
        {
            MotorTuning.SetSessionProbeForTests(() => true);
            MotorTuning before = MotorTuning.Current;

            bool applied = MotorTuning.TryApply(
                MotorTuning.Default with { CameraDipStrengthM = 0.12f }, out string refusal);

            Assert.False(applied);
            Assert.NotEqual("", refusal);
            Assert.Contains("network session", refusal, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, MotorTuning.Current);
        }
        finally
        {
            MotorTuning.ResetForTests();
        }
    }

    /// <summary><b>And it applies when there is no session.</b> The moved knob is
    /// <c>CameraDipStrengthM</c> deliberately: its only reader is <c>SandboxCamera</c>, a Godot Node
    /// no test in this suite instantiates, so the brief window in which <c>Current</c> is not the
    /// default cannot be observed by another test. See the class doc.</summary>
    [Fact]
    public void WithNoSession_TryApplyWrites_AndTheOtherTwentyNineRowsAreUntouched()
    {
        try
        {
            MotorTuning.SetSessionProbeForTests(() => false);

            bool applied = MotorTuning.TryApply(
                MotorTuning.Default with { CameraDipStrengthM = 0.12f }, out string refusal);

            Assert.True(applied);
            Assert.Equal("", refusal);
            Assert.Equal(0.12f, MotorTuning.Current.CameraDipStrengthM);
            Assert.Equal(MotorTuning.Default,
                MotorTuning.Current with { CameraDipStrengthM = 0.00f });
        }
        finally
        {
            MotorTuning.ResetForTests();
        }
    }

    /// <summary><b>The writer validates.</b> A NaN that reached the motor would propagate through
    /// <c>MoveToward</c> into position and the body would be gone for good, so
    /// <c>TryApply</c> never trusts its argument — even one handed straight from a slider.</summary>
    [Fact]
    public void TryApply_ValidatesBeforeWriting_SoANaNCanNeverReachTheMotor()
    {
        try
        {
            MotorTuning.SetSessionProbeForTests(() => false);

            Assert.True(MotorTuning.TryApply(
                MotorTuning.Default with { Deceleration = float.NaN }, out _));

            Assert.Equal(21f, MotorTuning.Current.Deceleration);
            Assert.Equal(MotorTuning.Default, MotorTuning.Current);
        }
        finally
        {
            MotorTuning.ResetForTests();
        }
    }

    // =============================================================================================
    // 3. Validation (§6.3, §2.3).
    // =============================================================================================

    [Fact]
    public void ANonFiniteValue_IsReplacedByItsShippedDefault_AndSaidSo()
    {
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            MotorTuning t = MotorTuning.Validate(
                MotorTuning.Default with { Gravity = bad }, out var warnings);
            Assert.Equal(24f, t.Gravity);   // MOVE-8: the ruled gravity
            Assert.Contains(warnings, w => w.Contains("Gravity"));
        }
    }

    [Fact]
    public void AnOutOfRangeValue_IsClampedRatherThanDefaulted_BecauseTheIntentWasExplicit()
    {
        MotorTuning t = MotorTuning.Validate(
            MotorTuning.Default with { Deceleration = 500f }, out var warnings);
        Assert.Equal(MotorTuningKnobs.Deceleration.Max, t.Deceleration);
        Assert.NotEqual(MotorTuningKnobs.Deceleration.Default, t.Deceleration);
        Assert.Contains(warnings, w => w.Contains("Deceleration"));
    }

    /// <summary><b>The exitless states §2.3 names, each reached by a slider and each refused.</b>
    /// An apex hang at 1.0 leaves zero gravity at the apex and the body never comes down; a gravity
    /// of zero means a body that leaves the ground never returns; a zero apex-hang window is a
    /// divide-by-zero on every airborne tick.</summary>
    [Theory]
    [InlineData("ApexHangStrength", 1.0f)]
    [InlineData("ApexHangWindowMps", 0f)]
    [InlineData("Gravity", 0f)]
    [InlineData("JumpReleaseGravityMultiplier", 0.5f)]
    [InlineData("TakeoffKickSec", 0f)]
    [InlineData("CameraDipAttackSec", 0f)]
    [InlineData("CameraDipRecoverSec", 0f)]
    public void AStructuralBound_CannotBeCrossedByAHandEditedFile(string knobName, float attempt)
    {
        Assert.True(MotorTuningKnobs.TryByName(knobName, out MotorKnob knob));
        MotorTuning t = MotorTuning.Validate(knob.Set(MotorTuning.Default, attempt), out var warnings);
        Assert.InRange(knob.Get(t), knob.Min, knob.Max);
        Assert.NotEqual(attempt, knob.Get(t));
        Assert.Contains(warnings, w => w.Contains(knobName));
    }

    /// <summary>
    /// <b>The skid's anti-chatter gap is an ordering constraint, so no per-field bound can express
    /// it</b>: exit must stay strictly below entry or the skid re-enters the tick it exits —
    /// MECHANICS §2's flicker case, produced by a slider.
    ///
    /// <para>And it is genuinely coupled, which is the point: neither knob here is out of its own
    /// range. <c>SkidExitSpeedMps</c> stays at its shipped 1.2 and <c>MoveSpeed</c> is dragged to
    /// its legal minimum, which drops the entry speed to 0.75 m/s and puts <i>exit above entry</i>
    /// with both sliders inside their bounds.</para>
    /// </summary>
    [Fact]
    public void TheSkidExitSpeed_IsForcedBelowTheEntrySpeed_EvenWhenNeitherKnobIsOutOfRange()
    {
        MotorTuning candidate = MotorTuning.Default with { MoveSpeed = 1.0f };
        Assert.True(candidate.SkidExitSpeedMps > candidate.SkidEnterSpeedMps,
            "the test no longer reaches the case it is about");

        MotorTuning t = MotorTuning.Validate(candidate, out var warnings);

        Assert.True(t.SkidExitSpeedMps < t.SkidEnterSpeedMps,
            $"exit {t.SkidExitSpeedMps} is not below entry {t.SkidEnterSpeedMps}");
        Assert.InRange(t.SkidExitSpeedMps, MotorTuningKnobs.SkidExitSpeedMps.Min,
            MotorTuningKnobs.SkidExitSpeedMps.Max);
        Assert.Contains(warnings, w => w.Contains("SkidExitSpeedMps"));
    }

    /// <summary>The landing gate must stay two constants: below <c>LandMinFallMps</c> every
    /// qualifying landing clamps to full intensity and the gate collapses into one.</summary>
    [Fact]
    public void TheLandingGate_CannotCollapseIntoASingleConstant()
    {
        MotorTuning t = MotorTuning.Validate(
            MotorTuning.Default with { LandMinFallMps = 9f, LandFullFallMps = 4f }, out var warnings);
        Assert.True(t.LandFullFallMps >= t.LandMinFallMps + 0.5f);
        Assert.Contains(warnings, w => w.Contains("LandFullFallMps"));
    }

    /// <summary><b>MECHANICS §4.</b> Validation is a projection: whatever a hand-edited file or a
    /// slider hands it, running it twice lands in the same place as running it once. A validator
    /// that moved a value on the second pass would mean the legal set is not closed, and a lab that
    /// loaded a file, saved it, and loaded it again would drift.</summary>
    [Fact]
    public void ValidationIsIdempotent_SoTheLegalSetIsClosed()
    {
        MotorTuning wild = MotorTuning.Default with
        {
            MoveSpeed = 1.0f,               // drags the skid entry below the exit speed
            Gravity = float.NaN,
            Deceleration = 500f,
            LandMinFallMps = 9f,
            LandFullFallMps = 4f,
            ApexHangStrength = 1.0f,
        };

        MotorTuning once = MotorTuning.Validate(wild, out _);
        MotorTuning twice = MotorTuning.Validate(once, out var secondWarnings);

        Assert.Equal(once, twice);
        Assert.Empty(secondWarnings);
    }

    // =============================================================================================
    // 4. The file (§6.2, §6.3, acceptance criterion 6).
    // =============================================================================================

    /// <summary>§6.2's location, asserted so a refactor cannot quietly move Talon's settings.</summary>
    [Fact]
    public void TheFileLivesInTheUserDirectory_OutsideTheRepo()
    {
        Assert.Equal("user://movement-tuning.json", MotorTuningFile.UserPath);
        Assert.Equal("user://movement-tuning-prints", MotorTuningFile.PrintDirUserPath);
    }

    /// <summary><b>Demonstrated, not asserted (AC 6, case 1):</b> a save produces a real file, and
    /// it is the sparse document §6.3 specifies — only what moved.</summary>
    [Fact]
    public void Saving_WritesASparseJsonDocument_CarryingOnlyWhatMoved()
    {
        string path = TempFile();
        try
        {
            MotorTuningFile.SaveTo(path, MotorTuning.Default with { Acceleration = 12.5f }, Stamp);

            Assert.True(File.Exists(path));
            string json = File.ReadAllText(path);
            Assert.Contains("\"version\": 1", json);
            Assert.Contains("\"Acceleration\"", json);
            Assert.DoesNotContain("\"Gravity\"", json);      // unchanged: not written
            Assert.DoesNotContain("\"MoveSpeed\"", json);
        }
        finally { Cleanup(path); }
    }

    /// <summary><b>Demonstrated (AC 6, case 2):</b> a round trip through the file restores every one
    /// of the thirty-one fields, at a tuning where every one of them moved.</summary>
    [Fact]
    public void ARoundTripThroughTheFile_RestoresEveryField()
    {
        string path = TempFile();
        try
        {
            MotorTuning saved = EverythingMoved();
            Assert.All(MotorTuningKnobs.All, k => Assert.NotEqual(k.Default, k.Get(saved)));

            MotorTuningFile.SaveTo(path, saved, Stamp);
            MotorTuningLoad loaded = MotorTuningFile.LoadFrom(path);

            Assert.True(loaded.FileExisted);
            Assert.False(loaded.Discarded);
            Assert.Empty(loaded.Warnings);
            Assert.Equal(saved, loaded.Tuning);
        }
        finally { Cleanup(path); }
    }

    /// <summary><b>Demonstrated (AC 6, case 3):</b> a field missing from <c>values</c> loads at its
    /// shipped default. This is the normal case, because the file is sparse by design.</summary>
    [Fact]
    public void AFieldMissingFromTheFile_LoadsAtItsShippedDefault()
    {
        string path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                "{ \"version\": 1, \"savedUtc\": \"2026-08-26T17:42:03Z\", "
                + "\"values\": { \"Acceleration\": 12.5 } }");

            MotorTuningLoad loaded = MotorTuningFile.LoadFrom(path);

            Assert.Equal(12.5f, loaded.Tuning.Acceleration);
            Assert.Equal(MotorTuning.Default.Gravity, loaded.Tuning.Gravity);
            Assert.Equal(MotorTuning.Default.MoveSpeed, loaded.Tuning.MoveSpeed);
            Assert.False(loaded.Discarded);
            Assert.Empty(loaded.Warnings);
        }
        finally { Cleanup(path); }
    }

    /// <summary><b>Demonstrated (AC 6, case 4):</b> a malformed file is discarded <i>whole</i> —
    /// never partially applied, because half a corrupt file is a tuning nobody authored — and it
    /// says so in a warning the readout shows.</summary>
    [Fact]
    public void AMalformedFile_IsDiscardedWhole_WithAWarningAndNoPartialApplication()
    {
        string path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ \"version\": 1, \"values\": { \"Acceleration\": 12.5,");

            MotorTuningLoad loaded = MotorTuningFile.LoadFrom(path);

            Assert.True(loaded.Discarded);
            Assert.Equal(MotorTuning.Default, loaded.Tuning);
            Assert.NotEmpty(loaded.Warnings);
        }
        finally { Cleanup(path); }
    }

    /// <summary>An absent file is the first run: defaults, and <b>silent</b>. A warning here would
    /// teach Talon to ignore warnings.</summary>
    [Fact]
    public void AnAbsentFile_IsTheFirstRun_AndIsNotAProblem()
    {
        MotorTuningLoad loaded = MotorTuningFile.LoadFrom(TempFile());
        Assert.False(loaded.FileExisted);
        Assert.Equal(MotorTuning.Default, loaded.Tuning);
        Assert.Empty(loaded.Warnings);
    }

    [Fact]
    public void AnUnknownVersion_IsDiscardedRatherThanGuessedAt()
    {
        MotorTuningLoad loaded = MotorTuningFile.FromJson(
            "{ \"version\": 99, \"values\": { \"Acceleration\": 12.5 } }");
        Assert.True(loaded.Discarded);
        Assert.Equal(MotorTuning.Default, loaded.Tuning);
        Assert.Contains(loaded.Warnings, w => w.Contains("99"));
    }

    [Fact]
    public void AKnobThatNoLongerExists_IsIgnored_AndDoesNotStopTheFileLoading()
    {
        MotorTuningLoad loaded = MotorTuningFile.FromJson(
            "{ \"version\": 1, \"values\": { \"WallRunGravity\": 3.0, \"Acceleration\": 12.5 } }");
        Assert.False(loaded.Discarded);
        Assert.Equal(12.5f, loaded.Tuning.Acceleration);
        Assert.Contains(loaded.Warnings, w => w.Contains("WallRunGravity"));
    }

    [Fact]
    public void AHandEditedOutOfRangeValue_IsClampedOnLoad_AndTheClampIsReported()
    {
        MotorTuningLoad loaded = MotorTuningFile.FromJson(
            "{ \"version\": 1, \"values\": { \"Deceleration\": 500.0 } }");
        Assert.Equal(MotorTuningKnobs.Deceleration.Max, loaded.Tuning.Deceleration);
        Assert.Contains(loaded.Warnings, w => w.Contains("Deceleration"));
    }

    // =============================================================================================
    // 5. Print-as-C# (§6.4, acceptance criterion 7).
    // =============================================================================================

    /// <summary><b>MOVE-4d retargeted the printer at <c>MotorTuning.Default</c>'s initializer</b>,
    /// so a printed line is an initializer element at eight spaces rather than a <c>const</c>
    /// declaration at four. See <c>MotorTuningPrint</c>'s class comment for the measurement behind
    /// that: the old shape stopped compiling the moment MOVE-4b turned the constants into
    /// properties.</summary>
    private static readonly Regex DeclarationLine =
        new(@"^        [A-Za-z]+ = -?[0-9.]+f,\s+// was ", RegexOptions.Compiled);

    /// <summary><b>The acceptance test for the format, stated as the workflow: paste, save, build,
    /// no edits.</b> Every emitted line is a complete initializer element at
    /// <c>MotorTuning.Default</c>'s own eight-space indentation, every float literal carries the
    /// <c>f</c> suffix a <c>float</c> field needs, and every line carries the <c>// was</c> that
    /// makes the paste reviewable in a diff.</summary>
    [Fact]
    public void EveryPrintedLine_IsACompilableDeclarationWithItsOldValue()
    {
        string block = MotorTuningPrint.Render(EverythingMoved(), Stamp, all: true);

        List<string> declarations = block.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.StartsWith("        ", StringComparison.Ordinal) && l.Contains(" = "))
            .ToList();

        Assert.Equal(59, declarations.Count);   // FP-1: BodyYawFollowsAim is the fifty-ninth row
        foreach (string line in declarations)
        {
            Assert.Matches(DeclarationLine, line);
            string rhs = line[(line.IndexOf(" = ", StringComparison.Ordinal) + 3)..];
            rhs = rhs[..rhs.IndexOf(',')];
            Assert.EndsWith("f", rhs);                       // never a double literal
            Assert.Contains(".", rhs);                       // never a bare integer
        }

        // Every field of MotorTuning.Default is named exactly once, so the block is a complete
        // replacement for that initializer rather than a partial one somebody has to reconcile.
        foreach (MotorKnob knob in MotorTuningKnobs.All)
            Assert.Single(declarations.Where(
                l => l.TrimStart().StartsWith(knob.Name + " = ", StringComparison.Ordinal)));
    }

    /// <summary><b>The paste target is named, once, and it is the initializer</b> — not the
    /// <c>AvatarMotor</c> / <c>AvatarVisual</c> constants, which stopped being constants when
    /// MOVE-4b turned them into properties. A block that still pointed at them would emit lines
    /// that either do not compile (CS0133 on the derived skid row) or compile and switch the
    /// slider that produced them back off.</summary>
    [Fact]
    public void ThePasteTarget_IsTheDefaultInitializer_NotTheOldConstants()
    {
        string block = MotorTuningPrint.Render(
            MotorTuning.Default with { Acceleration = 12.5f }, Stamp);

        Assert.Contains("PASTE TARGET: scripts/net/MotorTuning.cs", block);
        Assert.Contains("MotorTuning.Default's initializer", block);
        Assert.DoesNotContain("public const float", block);
        Assert.DoesNotContain("private const float", block);
        Assert.DoesNotContain("MoveSpeed * ", block);

        // And it says out loud that promoting a tuning is never a one-file edit.
        Assert.Contains("TESTS THAT WILL FAIL ON PASTE", block);
        Assert.Contains(MotorTuningPrint.IdentityTest, block);
    }

    /// <summary>§6.4 rule 1 and the trailer: only what moved is printed, and the block says how
    /// many rows it left out.</summary>
    [Fact]
    public void OnlyWhatMovedIsPrinted_AndTheRestIsCounted()
    {
        string block = MotorTuningPrint.Render(MotorTuning.Default with { Acceleration = 12.5f }, Stamp);

        Assert.Contains("1 of 59 knobs differ", block);   // FP-1: 58 -> 59 rows
        Assert.Contains("        Acceleration = 12.5f,", block);
        Assert.Contains("// was 9", block);
        Assert.DoesNotContain("        Gravity = ", block);
        Assert.Contains("unchanged (58)", block);
    }

    /// <summary>
    /// <b>§6.4 rule 9, satisfied structurally instead of textually.</b> The rule existed so the
    /// skid threshold could not be stranded at an absolute number when <c>MoveSpeed</c> moved, and
    /// the old target needed the derived <c>MoveSpeed * 0.75f</c> re-emitted to get that. The field
    /// being a <i>fraction</i> is now the guarantee itself, and re-emitting the derived form is
    /// what made the paste fail to compile (CS0133 — <c>MoveSpeed</c> is a property).
    ///
    /// <para>So the row prints as the fraction, and the effective m/s — the number a reader
    /// actually wants — rides in the comment beside it.</para>
    /// </summary>
    [Fact]
    public void TheReshapedSkidRow_PrintsTheFraction_AndTheEffectiveSpeedBesideIt()
    {
        string block = MotorTuningPrint.Render(
            MotorTuning.Default with { SkidEnterSpeedFraction = 0.80f }, Stamp);

        Assert.Contains("        SkidEnterSpeedFraction = 0.8f,", block);
        Assert.Contains("effective skid entry, m/s 3.04", block);   // MOVE-8: 0.80 x 3.8
        Assert.DoesNotContain("MoveSpeed * 0.8f", block);
    }

    /// <summary>§6.4 rule 11: a knob that is inert at its default and load-bearing the moment a
    /// companion moves is printed anyway, saying why. A paste that moved the apex hang and silently
    /// omitted its window would not reproduce what was played.</summary>
    [Fact]
    public void AKnobThatBecameLive_IsPrintedEvenThoughItDidNotMove()
    {
        string block = MotorTuningPrint.Render(
            MotorTuning.Default with { ApexHangStrength = 0.35f }, Stamp);

        Assert.Contains("        ApexHangStrength = 0.35f,", block);
        Assert.Contains("        ApexHangWindowMps = 2.0f,", block);
        Assert.Contains("(unchanged, printed because ApexHangStrength makes it live)", block);

        // §6.4 rule 8 — "new constants print with NEW and a placement hint" — dissolved with the
        // retarget: all thirty-one fields already exist in MotorTuning.Default, so nothing is new and
        // there is nowhere to place anything. A stray NEW would mean the printer had gone back to
        // pointing at constants that no longer exist.
        Assert.DoesNotContain(":NEW", block);
    }

    /// <summary>
    /// <b>§6.4 rule 6 and rule 10 — the paste carries its own warning.</b> Twenty-three of the
    /// thirty-one knobs are pinned by a test, so a paste is almost never a one-file edit and this block
    /// is the only place the full edit set can be known. Spec §6.4's worked example is
    /// <c>Deceleration 17.50</c>, whose air-brake shed falls below the skid entry speed.
    /// </summary>
    [Fact]
    public void ABreachedPin_GetsTheBanner_ThePerLineTag_AndTheTestThatWillFail()
    {
        string block = MotorTuningPrint.Render(
            MotorTuning.Default with { Deceleration = 17.5f }, Stamp);

        Assert.Contains("OUT OF BOUNDS. THE SUITE WILL GO RED IF YOU PASTE THIS.", block);
        Assert.Contains("[PINNED [20.892, 21.08] — BREACHED]", block);
        Assert.Contains("TESTS THAT WILL FAIL ON PASTE", block);
        Assert.Contains("MaximumAirBrakeOverAFullFlight_SitsAboveTheSkidThreshold", block);
        Assert.Contains("tests/unit/AirborneControlTests.cs:217", block);
    }

    /// <summary>§6.4 rule 7: the invariant readout is printed breached or not, so a pasted block
    /// records what the state of the world was when the tuning was chosen. At the shipped tuning
    /// every one of them holds — which is the same claim as "MOVE-4b shipped no new feel".</summary>
    [Fact]
    public void TheInvariantReadout_IsAlwaysPrinted_AndHoldsAtTheShippedTuning()
    {
        string block = MotorTuningPrint.Render(MotorTuning.Default, Stamp, all: true);

        Assert.Contains("live invariant readout at print time:", block);
        Assert.Contains("air-brake vs skid entry", block);
        Assert.Contains("JumpApex window, apex", block);
        Assert.Contains("gear ordering", block);
        Assert.DoesNotContain("BREACHED", block);
    }

    /// <summary>A breach of a coupled inequality that no single slider crosses — the case §3.4 says
    /// no per-slider number can express. The chosen <c>Deceleration</c> is inside its own window;
    /// what stops fitting is the eel's launch-to-rest against the 1.2 s ragdoll, and the air brake
    /// against the skid entry.
    ///
    /// <para><b>MOVE-8 had to move the worked example, and WHY it moved is the interesting part.</b>
    /// §6.4's example was <c>Deceleration = 17.5</c>, which used to breach both inequalities at
    /// once. Talon's ruling broke the second half of it in both directions: the skid entry is a
    /// FRACTION of <c>MoveSpeed</c> so it fell 4.05 → 2.85, and <c>AirControlBrake</c> rose
    /// 0.30 → 0.55, so the air brake now sheds far more against a far lower bar and 17.5 clears it
    /// easily. The pair now separates at <b>7.6</b> rather than at <b>15.6</b>, and 7.0 is the value
    /// that reproduces §6.4's original point: two inequalities breached by one legal slider.</para>
    /// </summary>
    [Fact]
    public void ACoupledInvariant_IsEvaluatedLive_AndReportsItsOwnBreach()
    {
        IReadOnlyList<MotorInvariant> shipped =
            MotorTuningInvariants.Evaluate(MotorTuning.Default);
        IReadOnlyList<MotorInvariant> lowered =
            MotorTuningInvariants.Evaluate(MotorTuning.Default with { Deceleration = 7.0f });

        Assert.InRange(7.0f, MotorTuningKnobs.Deceleration.Min, MotorTuningKnobs.Deceleration.Max);

        Assert.True(shipped.Single(i => i.Name == "air-brake vs skid entry").Holds);
        Assert.False(lowered.Single(i => i.Name == "air-brake vs skid entry").Holds);
    }

    /// <summary>§3.7's ceiling, which is a curve rather than a number: the apex hang is pinned by a
    /// test written before the term existed, and the ceiling moves with the window and with gravity
    /// itself. §4.4's recommended first experiment — 0.35 at a 2.00 m/s window — must sit just
    /// inside it.
    /// <para><b>MOVE-4c narrowed the windows and the reason is a correction, not a retune.</b>
    /// MOVE-4b transcribed §3.7's closed form, which is continuous-math applied to a quantity the
    /// pinning test measures in whole 60 Hz ticks and overstates the ceiling. The ceiling is
    /// bisected on that simulation instead. <c>ApexHangAndLandingDipTests</c> measures both numbers
    /// and demonstrates that the closed form's answer is one the pinning test rejects.</para>
    ///
    /// <para><b>MOVE-8: the ceiling MOVED UP, from 0.350 to 0.600 at a 2.00 m/s window.</b> The
    /// binding assertion is the held airtime and Talon's ruled gravity rows shortened the baseline
    /// flight, so there is more room under the same tolerance. §4.4's 0.35 experiment therefore has
    /// real margin now instead of one thousandth — and the SHIPPED strength is 0.30, half the
    /// ceiling.</para></summary>
    [Fact]
    public void TheApexHangCeiling_IsACurve_AndTheRecommendedExperimentSitsInsideIt()
    {
        float at2 = MotorTuningInvariants.ApexHangStrengthCeiling(MotorTuning.Default);
        Assert.InRange(at2, 0.599f, 0.602f);
        Assert.True(0.35f < at2, "spec §4.4's recommended experiment no longer fits under the pin");
        Assert.True(MotorTuning.Default.ApexHangStrength < at2,
            "the SHIPPED strength must sit under its own live ceiling");

        float at4 = MotorTuningInvariants.ApexHangStrengthCeiling(
            MotorTuning.Default with { ApexHangWindowMps = 4.00f });
        Assert.InRange(at4, 0.39f, 0.41f);
        Assert.True(at4 < at2, "the ceiling must fall as the window widens");
    }

    /// <summary>Literal rendering, §6.4 rule 5: the minimum decimals that round-trip the step,
    /// always an <c>f</c>, never a bare integer that would not compile as a float const.</summary>
    [Theory]
    [InlineData(12.5f, 0.5f, "12.5f")]
    [InlineData(9f, 0.5f, "9.0f")]
    [InlineData(0.35f, 0.01f, "0.35f")]
    [InlineData(2.0f, 0.05f, "2.0f")]
    [InlineData(-0.5f, 0.01f, "-0.5f")]
    public void FloatLiterals_AreRenderedShortAndCompilable(float value, float step, string expected)
        => Assert.Equal(expected, MotorTuningPrint.Literal(value, step));
}
