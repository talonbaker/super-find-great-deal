using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MpFoundation.Dev;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>MOVE-4d — the knob panel's call sites.</b> MOVE-4b built <c>MotorTuningFile</c> and
/// <c>MotorTuningPrint</c> and deliberately called neither; this packet is those call sites, and
/// these are the tests that hold them.
///
/// <para><b>Not one test here mutates <c>MotorTuning.Current</c>, and not one touches the session
/// probe.</b> That is MOVE-4b's standing rule taken one step further rather than merely obeyed:
/// xUnit runs classes in parallel and seven classes now read <c>AvatarMotor</c>'s properties live,
/// so a parked value — even one restored in a <c>finally</c> — is an intermittent red in a file
/// nobody changed. <see cref="MotorTuningSession"/> takes its writer as a delegate whose production
/// default is <see cref="MotorTuning.TryApply"/>; every test below hands it a fake writer holding
/// its own tuning, so the global is never written at all.</para>
/// </summary>
[Collection(MotorTuningStaticsCollection.Name)]
public class MotorTuningSessionTests
{
    /// <summary>A stand-in for <c>MotorTuning.TryApply</c> that keeps its own tuning instead of the
    /// process-global one. It validates exactly as the real writer does, so what the tests observe
    /// is the real clamping behaviour and not a simplified one.</summary>
    private sealed class FakeWriter
    {
        public MotorTuning Value = MotorTuning.Default;
        public bool Refuse;
        public string Refusal = "a network session is live (test)";
        public int Writes;

        public bool Write(in MotorTuning next, out string refusal)
        {
            if (Refuse)
            {
                refusal = Refusal;
                return false;
            }
            refusal = "";
            Value = MotorTuning.Validate(next, out _);
            Writes++;
            return true;
        }

        public MotorTuningSession Session(string path) => new(path, Write, () => Value);
    }

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "sail-move4d-" + Guid.NewGuid().ToString("N") + ".json");

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is not a test failure.
        }
    }

    // =============================================================================================
    // 1. The panel is built from the knob table (acceptance criteria 1 and 2).
    // =============================================================================================

    /// <summary><b>Every field has a row, and the rows are the table.</b> Acceptance criterion 1's
    /// absence half — that the panel does not carry a second hand-typed list — is a grep; this is
    /// the positive half, and it is the one that would go red if a knob were added to
    /// <c>MotorTuning</c> and forgotten here.</summary>
    [Fact]
    public void EveryKnobTableRow_IsAPanelRow_InTableOrder()
    {
        Assert.Equal(MotorTuningKnobs.All.Count, MotorTuningSession.Rows.Count);
        Assert.Equal(
            MotorTuningKnobs.All.Select(k => k.Name).ToArray(),
            MotorTuningSession.Rows.Select(k => k.Name).ToArray());

        // And every one of them is a real MotorTuning field, reachable both ways.
        foreach (MotorKnob knob in MotorTuningSession.Rows)
        {
            MotorTuning moved = knob.Set(MotorTuning.Default, knob.Default + knob.Step);
            Assert.Equal(knob.Default + knob.Step, knob.Get(moved));
        }
    }

    /// <summary><b>The groups are derived, not typed.</b> Spec §2.2's seven groups, in table order,
    /// with every row in exactly one of them — so a knob added to the table appears in the panel
    /// under the right heading with no edit to the panel.</summary>
    [Fact]
    public void TheGroups_AreDerivedFromTheTable_AndCoverEveryRowExactlyOnce()
    {
        // MOVE-5 extends MOVE-4's seven with Slide and Chain, and puts its seven air-jump rows into
        // the EXISTING Air group — which is why MotorTuningKnobs.All is ordered by group rather
        // than by Index. Appending them would have produced two sections both called "Air", a
        // defect visible to Talon and to no test; this assertion is the one that would have caught
        // it, so it is stated as an exact ordered sequence rather than a set.
        // MOVE-5f appends a TENTH, "Anticipation", holding all five of its rows including the two
        // that are genuinely gravity terms. Splitting them into Gravity would have produced two
        // sections called "Gravity" — the same defect this assertion exists to catch — or buried the
        // toggle three sections from the pair it switches on.
        string[] expected =
        {
            "Ground", "Gravity", "Jump", "Air", "Skid", "Landing", "Camera", "Slide", "Chain",
            "Anticipation",
        };
        Assert.Equal(expected, MotorTuningSession.Groups.Select(g => g.Name).ToArray());

        List<MotorKnob> flattened = MotorTuningSession.Groups.SelectMany(g => g.Knobs).ToList();
        Assert.Equal(MotorTuningKnobs.All.Count, flattened.Count);
        Assert.Equal(MotorTuningKnobs.All.Select(k => k.Name), flattened.Select(k => k.Name));

        foreach (MotorTuningSession.KnobGroup group in MotorTuningSession.Groups)
            Assert.All(group.Knobs, k => Assert.Equal(group.Name, k.Group));
    }

    /// <summary><b>Acceptance criterion 2, transcribed independently.</b> The panel configures each
    /// slider straight off its knob row, so what this actually guards is that the knob row still
    /// says what MOVE-4a §2.2 says — a second, deliberately hand-typed copy of the table, which is
    /// the only kind of duplication that catches a transcription slip.</summary>
    [Theory]
    // name, min, max, step
    [InlineData("MoveSpeed", 1.0f, 12.0f, 0.1f)]
    [InlineData("SprintMultiplier", 1.00f, 3.00f, 0.05f)]
    [InlineData("Acceleration", 1.0f, 40.0f, 0.5f)]
    [InlineData("Deceleration", 2.0f, 60.0f, 0.5f)]
    [InlineData("TurnAcceleration", 2.0f, 80.0f, 1.0f)]
    [InlineData("TurnLerp", 1.0f, 45.0f, 0.5f)]
    [InlineData("AimTurnLerp", 1.0f, 60.0f, 0.5f)]
    [InlineData("Gravity", 5.0f, 45.0f, 0.5f)]
    [InlineData("FallGravityMultiplier", 0.50f, 3.00f, 0.05f)]
    [InlineData("ApexHangStrength", 0.00f, 0.90f, 0.01f)]
    [InlineData("ApexHangWindowMps", 0.25f, 6.00f, 0.05f)]
    [InlineData("JumpVelocity", 2.0f, 16.0f, 0.1f)]
    [InlineData("JumpReleaseGravityMultiplier", 1.00f, 8.00f, 0.05f)]
    [InlineData("CoyoteTimeSec", 0.00f, 0.40f, 0.01f)]
    [InlineData("JumpBufferSec", 0.00f, 0.40f, 0.01f)]
    [InlineData("AirControlBuild", 0.00f, 1.00f, 0.01f)]
    [InlineData("AirControlTurn", 0.00f, 1.00f, 0.01f)]
    [InlineData("AirControlBrake", 0.00f, 1.00f, 0.01f)]
    [InlineData("SkidEnterSpeedFraction", 0.20f, 1.60f, 0.01f)]
    [InlineData("SkidAlignmentMax", -1.00f, 0.00f, 0.01f)]
    [InlineData("SkidDeceleration", 2.0f, 40.0f, 0.5f)]
    [InlineData("SkidExitSpeedMps", 0.10f, 4.00f, 0.05f)]
    [InlineData("SkidMaxSec", 0.00f, 2.00f, 0.05f)]
    [InlineData("LandMinFallMps", 0.5f, 10.0f, 0.1f)]
    [InlineData("LandFullFallMps", 3.0f, 30.0f, 0.5f)]
    [InlineData("TakeoffKickSec", 0.02f, 0.60f, 0.01f)]
    [InlineData("TakeoffKickMinDriveFraction", 0.00f, 1.00f, 0.01f)]
    [InlineData("CameraDipStrengthM", 0.00f, 0.50f, 0.01f)]
    [InlineData("CameraDipAttackSec", 0.01f, 0.30f, 0.01f)]
    [InlineData("CameraDipRecoverSec", 0.02f, 1.20f, 0.01f)]
    // MOVE-5f's five, hand-typed here for the same reason every row above is: a second copy is the
    // only kind of duplication that catches a transcription slip.
    [InlineData("AnticipationMode", 0f, 2f, 1f)]
    [InlineData("AnticipationDepth", 0.00f, 1.00f, 0.05f)]
    [InlineData("AnticipationCoilSec", 0.02f, 1.00f, 0.01f)]
    [InlineData("AnticipationBakedStrength", 0.00f, 2.00f, 0.05f)]
    [InlineData("AnticipationBakedWindow", 0.05f, 1.00f, 0.05f)]
    public void EverySliderRange_MatchesTheSpecTable(string name, float min, float max, float step)
    {
        Assert.True(MotorTuningKnobs.TryByName(name, out MotorKnob knob), $"no knob row named {name}");
        Assert.Equal(min, knob.Min);
        Assert.Equal(max, knob.Max);
        Assert.Equal(step, knob.Step);
    }

    /// <summary>
    /// <b>Twenty-three of thirty-one are pinned, and every pinned row can name its test.</b> The
    /// panel prints <c>PinTest</c> and <c>PinSource</c> onto the row itself — acceptance criterion
    /// 3 — so an empty one would be a badge that says "PINNED" and nothing else.
    ///
    /// <para><b>MOVE-4d finding, still standing: it was 22 / 8, not the 23 / 7 the packet and spec
    /// §3.2b both state.</b> Spec §3.3's own verdict table marks exactly eight rows "not pinned" —
    /// 6, 24, 25, 26, 27, 28, 29, 30 — and its prose then calls that list "the seven clean knobs",
    /// which is where the arithmetic slipped. <c>MotorTuningKnobs</c> transcribes the table
    /// correctly; the prose is what is wrong.</para>
    ///
    /// <para><b>MOVE-4f added row 31, <c>CameraDipRampPower</c>, and it is pinned</b> — so the
    /// count is now 23 / 8 of thirty-one, and the eight clean rows are the same eight. Its pin is
    /// the only one in the table whose window is drawn on a perceptual quantity rather than on a
    /// test's hand-typed literal, which is why it is asserted by name below.</para>
    ///
    /// <para><b>TWENTY-NINE on this repo, not Sail's thirty-one (MOVE-1, 2026-09-04), and the two
    /// that moved are a cut-system consequence rather than a decision.</b> <c>MoveSpeed</c> was
    /// pinned by <c>NightPressureTests</c> and <c>SprintMultiplier</c> by <c>ToolStanceTests</c>;
    /// the MVP extraction cut both systems and their suites with them, and
    /// <c>MotorTuningKnobs</c> already records on each row that the pin "belonged to a system that
    /// is not part of this build". A pin whose test does not exist is a badge that says PINNED and
    /// nothing else — exactly what this test exists to forbid — so the two rows are counted as
    /// clean and named in the list below. Measured on this tree, not carried over: 29 pinned,
    /// 29 clean, 58 rows.</para>
    ///
    /// <para><b>THIRTY on this repo since FP-1 (2026-09-19).</b> <c>BodyYawFollowsAim</c> — the
    /// first-person facing mode — was added pinned, on a test that exercises both of its settings.
    /// 30 pinned, 29 clean, 59 rows.</para>
    /// </summary>
    [Fact]
    public void ThirtyRowsArePinned_AndEachOneNamesTheTestAndTheSourceThatPinsIt()
    {
        List<MotorKnob> pinned = MotorTuningSession.Rows.Where(k => k.IsPinned).ToList();
        // FP-1 (2026-09-19): 29 -> 30 pinned, 58 -> 59 rows. BodyYawFollowsAim is pinned on
        // LocomotionTests.BodyYawFollowsAim_TurnsTheBodyTowardTheLook_AndOffRestoresTravelFacing,
        // which exercises BOTH of its settings — a 0/1 row whose pin only ever ran one of them
        // would be a badge, which is exactly what this test forbids.
        Assert.Equal(30, pinned.Count);
        Assert.Equal(29, MotorTuningSession.Rows.Count - pinned.Count);
        Assert.Contains(pinned, k => k.Name == "BodyYawFollowsAim");
        Assert.Contains(pinned, k => k.Name == "CameraDipRampPower");
        // MOVE-5f: approach 2 is a GRAVITY term, so its two rows are pinned on the same window the
        // apex hang is — read from the other end, because a baked coil takes height rather than
        // adding it. Approach 1's two rows are not pinned and cannot be: a pose has no arc to move.
        Assert.Contains(pinned, k => k.Name == "AnticipationBakedStrength");
        Assert.Contains(pinned, k => k.Name == "AnticipationBakedWindow");

        foreach (MotorKnob knob in pinned)
        {
            Assert.False(string.IsNullOrWhiteSpace(knob.PinTest), knob.Name);
            Assert.False(string.IsNullOrWhiteSpace(knob.PinSource), knob.Name);
            Assert.Contains(".", knob.PinTest, StringComparison.Ordinal);
            // At least one side of the window, or the badge has nothing to draw.
            Assert.True(knob.PinMin is not null || knob.PinMax is not null, knob.Name);
        }

        // The clean rows, named rather than merely counted — spec §3.3's verdict table.
        Assert.Equal(
            new[]
            {
                // MOVE-1: clean HERE and pinned in Sail. Their pinning suites (NightPressureTests,
                // ToolStanceTests) belonged to systems the MVP extraction cut. Re-pinning either
                // one needs a live consumer in THIS repo to restate the number first.
                "MoveSpeed", "SprintMultiplier",
                "TurnLerp",
                // MOVE-5's air-jump rows: every one of them is inert unless AirJumpMode leaves 0,
                // and spec §11.4 names none of them as pinnable. The two that ARE pinned in this
                // block (AirJumpCountMax) and in Chain (ChainMaxDepth) are pinned on their WIRE
                // WIDTHS, which is not a feel question at all.
                "AirJumpMode", "AirJumpVelocityFraction", "KickConversionFraction",
                "KickHorizontalGainMps", "KickVerticalMps", "KickMinSpeedMps",
                "LandMinFallMps", "LandFullFallMps", "TakeoffKickSec",
                "TakeoffKickMinDriveFraction", "CameraDipStrengthM", "CameraDipAttackSec",
                "CameraDipRecoverSec",
                // MOVE-5 §11.4's "must stay free to move" list, transcribed: a test asserting a
                // literal on any of these would have decided a feel question that is Talon's.
                "JumpHoldWindowSec", "TouchdownSlideImmediate", "SlideDeceleration",
                "SlideTurnRateDeg", "DuckWalkSpeedFraction", "DuckWalkGuaranteed",
                "DuckWalkEntryConeDeg",
                "ChainGraceSec", "ChainDecayIntervalSec", "ChainBonusMps",
                // MOVE-5f's approach-1 rows and the toggle itself. A pose cannot move an arc, so
                // there is no arc test to pin them against; pinning them anyway would be a badge
                // that says PINNED and nothing else.
                "AnticipationMode", "AnticipationDepth", "AnticipationCoilSec",
            },
            MotorTuningSession.Rows.Where(k => !k.IsPinned).Select(k => k.Name).ToArray());
    }

    /// <summary>
    /// <b>The four Landing rows are live knobs, not lying ones.</b> MOVE-4b carried
    /// <c>MotorTuning</c> fields for <c>AvatarVisual</c>'s landing gate and take-off kick but left
    /// the constants alone, because that file was outside its packet. The result would have been
    /// four sliders that changed the world not at all while the printed block reported that they
    /// had moved — worse than four absent sliders, because an absent one reads as "not a thing" and
    /// a dead one reads as "this works". MOVE-4d converted them.
    ///
    /// <para><b>The positive control is built in:</b> the same reflection call is made against
    /// <c>AvatarMotor.TickDelta</c>, which is deliberately still a <c>const</c> (§2.4 — it is the
    /// simulation rate, not feel). If <c>GetField</c> could not see a constant, the four
    /// <c>Assert.Null</c>s above it would pass for the wrong reason.</para>
    /// </summary>
    [Fact]
    public void TheFourLandingRows_AreLiveProperties_NotConstantsWithASliderOverThem()
    {
        const BindingFlags anyStatic =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        foreach (string name in new[]
                 {
                     "LandMinFallMps", "LandFullFallMps", "TakeoffKickSec",
                     "TakeoffKickMinDriveFraction",
                 })
        {
            Assert.Null(typeof(AvatarVisual).GetField(name, anyStatic));

            PropertyInfo? property = typeof(AvatarVisual).GetProperty(
                name, BindingFlags.Public | BindingFlags.Static);
            Assert.True(property is not null, $"{name} is not a public static property");
            Assert.Equal(typeof(float), property!.PropertyType);

            // And the knob table points at the same name, so the panel's row and the property the
            // motor reads cannot drift apart.
            Assert.True(MotorTuningKnobs.TryByName(name, out _), $"{name} has no knob row");
        }

        // The positive control: a constant that is meant to stay a constant is still found as one.
        FieldInfo? tickDelta = typeof(AvatarMotor).GetField("TickDelta", anyStatic);
        Assert.True(tickDelta is not null, "reflection cannot see a const — the nulls above are meaningless");
        Assert.True(tickDelta!.IsLiteral);
    }

    /// <summary><b>No knob is locked</b> (acceptance criterion 3, second half). The session's
    /// setter moves a frozen knob off its frozen value exactly as it moves an unpinned one — the
    /// panel warns, and warning is the whole argument of spec §3.4.</summary>
    [Fact]
    public void APinnedKnobStillMoves_BecauseTheDecisionWasWarnNotLock()
    {
        var fake = new FakeWriter();
        MotorTuningSession session = fake.Session("");

        // MOVE-1: in Sail this used MoveSpeed, frozen to *exactly* 3.8 by NightPressureTests'
        // untoleranced Assert.Equal. The extraction cut that suite, so MoveSpeed is no longer
        // pinned here and the last line below would assert a breach of a window that does not
        // exist. Acceleration is the nearest surviving equivalent: a REAL pin on this tree, held
        // by LocomotionTests' sprint-ramp bracket [7.855, 15.75], and 40.0 is outside it. What the
        // test proves is unchanged — a pinned row still moves, and the panel warns rather than
        // locking. The positive control is the Breaches assertion itself: it would be false if the
        // row this now names had quietly lost its pin too.
        Assert.True(MotorTuningKnobs.Acceleration.IsPinned, "the row this proves warn-not-lock on "
            + "must actually be pinned, or the last assertion below passes for the wrong reason");
        Assert.True(session.SetKnob(MotorTuningKnobs.Acceleration, 40.0f));
        Assert.Equal(40.0f, fake.Value.Acceleration);
        Assert.True(MotorTuningKnobs.Acceleration.Breaches(fake.Value, out _, out _, out _));
    }

    // =============================================================================================
    // 2. The live coupled-constraint readout (acceptance criterion 4).
    // =============================================================================================

    /// <summary><b>The readout moves when a slider does</b>, and it catches the case no per-slider
    /// bound can: a product walked out of bounds by two knobs neither of which left its own
    /// window.</summary>
    [Fact]
    public void TheInvariantReadout_HoldsAtTheShippedTuning_AndBreachesWhenASliderMoves()
    {
        Assert.Equal(0, MotorTuningSession.BreachedInvariantCount(MotorTuning.Default));
        Assert.Equal(0, MotorTuningSession.BreachedKnobCount(MotorTuning.Default));

        var fake = new FakeWriter();
        MotorTuningSession session = fake.Session("");

        // Spec §6.4's own worked example, re-derived at MOVE-8's ruled tuning: one legal
        // Deceleration that breaches the air-brake-vs-skid-entry invariant. In Sail it breached
        // TWO, the other being "EelProfile invariant 1" — the MVP extraction cut the eel (blast)
        // system and MotorTuningInvariants no longer evaluates it, so on this tree the same knob
        // move breaches ONE. MOVE-1, 2026-09-04; the surviving assertion is unchanged.
        // 17.5 was that value until Talon's ruling; it no longer is, because the skid entry is a
        // FRACTION of MoveSpeed (4.05 -> 2.85) while AirControlBrake rose 0.30 -> 0.55, so the air
        // brake now clears a much lower bar. The pair separates at 7.6 instead of 15.6.
        // MotorTuningTests.ACoupledInvariant_IsEvaluatedLive_AndReportsItsOwnBreach carries the
        // same correction and the arithmetic.
        Assert.True(session.SetKnob(MotorTuningKnobs.Deceleration, 7.0f));

        IReadOnlyList<MotorInvariant> live = MotorTuningInvariants.Evaluate(fake.Value);
        Assert.Equal(1, live.Count(i => !i.Holds));
        Assert.DoesNotContain(live, i => i.Name == "EelProfile invariant 1");
        Assert.Contains(live, i => i.Name == "air-brake vs skid entry" && !i.Holds);
        Assert.Equal(1, MotorTuningSession.BreachedInvariantCount(fake.Value));
    }

    /// <summary><b>A coupled breach neither knob causes alone.</b> <c>SkidExitSpeedMps</c> is pinned
    /// below half the skid entry speed, and the entry speed is itself a product of
    /// <c>MoveSpeed</c> and <c>SkidEnterSpeedFraction</c> — so lowering <c>MoveSpeed</c> can breach
    /// a knob nobody touched. This is the case §3.4 says a per-slider number cannot state.</summary>
    [Fact]
    public void LoweringOneKnob_CanBreachAPinOnAnother_WhichIsWhyTheReadoutIsCoupled()
    {
        var fake = new FakeWriter();
        MotorTuningSession session = fake.Session("");

        Assert.False(MotorTuningKnobs.SkidExitSpeedMps.Breaches(fake.Value, out _, out _, out _));

        // Nothing is done to SkidExitSpeedMps at all.
        Assert.True(session.SetKnob(MotorTuningKnobs.MoveSpeed, 2.5f));

        Assert.Equal(1.20f, fake.Value.SkidExitSpeedMps);
        Assert.True(MotorTuningKnobs.SkidExitSpeedMps.Breaches(fake.Value, out _, out _, out float? hi));
        Assert.Equal(0.5f * fake.Value.SkidEnterSpeedMps, hi!.Value, 1e-4f);
    }

    // =============================================================================================
    // 3. Save, load, reset, round trip (acceptance criteria 5 and 7).
    // =============================================================================================

    /// <summary><b>Save writes the file, and load restores every field.</b> Demonstrated over a real
    /// file on disk, not asserted.</summary>
    [Fact]
    public void SaveWritesTheFile_AndLoadRestoresEveryField()
    {
        string path = TempFile();
        try
        {
            var writing = new FakeWriter();
            MotorTuningSession save = writing.Session(path);

            Assert.True(save.SetKnob(MotorTuningKnobs.ApexHangStrength, 0.35f));
            Assert.True(save.SetKnob(MotorTuningKnobs.CameraDipStrengthM, 0.12f));
            Assert.True(save.SetKnob(MotorTuningKnobs.Acceleration, 12.5f));
            Assert.True(File.Exists(path));
            Assert.True(save.SaveCount >= 3, $"expected a write per apply, got {save.SaveCount}");

            var reading = new FakeWriter();
            MotorTuningSession load = reading.Session(path);
            Assert.True(load.LoadFromFile());

            Assert.Equal(writing.Value, reading.Value);
            Assert.Equal(0.35f, reading.Value.ApexHangStrength);
            Assert.Equal(0.12f, reading.Value.CameraDipStrengthM);
            Assert.Equal(12.5f, reading.Value.Acceleration);
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary><b>A field missing from the file loads at its shipped default</b> — the normal case,
    /// because the file is sparse by design and carries only what moved.</summary>
    [Fact]
    public void AFieldMissingFromTheFile_LoadsAtItsShippedDefault()
    {
        string path = TempFile();
        try
        {
            File.WriteAllText(path,
                "{\"version\":1,\"savedUtc\":\"2026-08-27T00:00:00Z\","
              + "\"values\":{\"CameraDipStrengthM\":0.12}}");

            var fake = new FakeWriter();
            MotorTuningSession session = fake.Session(path);
            Assert.True(session.LoadFromFile());

            Assert.Equal(0.12f, fake.Value.CameraDipStrengthM);
            Assert.Equal(MotorTuning.Default, fake.Value with { CameraDipStrengthM = 0.00f });
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary><b>A malformed file is discarded whole, with a warning on the readout</b> — never a
    /// partial application, because half a corrupt file is a tuning nobody authored (§6.3).</summary>
    [Fact]
    public void AMalformedFile_IsDiscardedWhole_AndTheReadoutSaysSo()
    {
        string path = TempFile();
        try
        {
            File.WriteAllText(path, "{\"version\":1,\"values\":{\"Acceleration\":");

            var fake = new FakeWriter();
            MotorTuningSession session = fake.Session(path);
            Assert.True(session.LoadFromFile());

            Assert.Equal(MotorTuning.Default, fake.Value);
            Assert.NotEmpty(session.Notices);
            Assert.Contains(session.Notices, n => n.Contains("discard", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary><b>An absent file is the first run, and is not a problem</b> — but the apply still
    /// happens, so a guard that would refuse says so at startup rather than at the first drag.</summary>
    [Fact]
    public void AnAbsentFile_IsSilentAndLeavesEveryKnobAtItsShippedDefault()
    {
        string path = TempFile();
        var fake = new FakeWriter();
        MotorTuningSession session = fake.Session(path);

        Assert.True(session.LoadFromFile());
        Assert.Equal(MotorTuning.Default, fake.Value);
        Assert.Empty(session.Notices);
        Assert.False(File.Exists(path));   // a first run does not create the file
    }

    /// <summary><b>A hand-edited out-of-range value is clamped on load and the clamp is reported to
    /// the readout</b>, because a value silently moved is a lab lying to the person using it.</summary>
    [Fact]
    public void AHandEditedOutOfRangeValue_IsClampedOnLoad_AndTheClampReachesTheReadout()
    {
        string path = TempFile();
        try
        {
            File.WriteAllText(path,
                "{\"version\":1,\"values\":{\"ApexHangStrength\":4.0}}");

            var fake = new FakeWriter();
            MotorTuningSession session = fake.Session(path);
            Assert.True(session.LoadFromFile());

            Assert.Equal(0.90f, fake.Value.ApexHangStrength);
            Assert.Contains(session.Notices,
                n => n.Contains("ApexHangStrength", StringComparison.Ordinal));
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary><b>Reset restores <c>MotorTuning.Default</c> exactly</b>, and — acceptance criterion
    /// 7 — a save → reset → load round trip returns the saved values. That only works because reset
    /// deliberately does not write the file: it is the A/B key, and a reset that destroyed the
    /// session's work would be the most expensive keystroke in the panel.</summary>
    [Fact]
    public void SaveThenResetThenLoad_ReturnsTheSavedValues_BecauseResetDoesNotTouchTheFile()
    {
        string path = TempFile();
        try
        {
            var fake = new FakeWriter();
            MotorTuningSession session = fake.Session(path);

            Assert.True(session.SetKnob(MotorTuningKnobs.Gravity, 26.0f));
            Assert.True(session.SetKnob(MotorTuningKnobs.JumpVelocity, 9.2f));
            MotorTuning saved = fake.Value;
            string onDisk = File.ReadAllText(path);
            int writes = session.SaveCount;

            Assert.True(session.ResetAll());
            Assert.Equal(MotorTuning.Default, fake.Value);
            // The file is byte-identical: reset did not write it, which is what makes the round
            // trip below possible at all.
            Assert.Equal(onDisk, File.ReadAllText(path));
            Assert.Equal(writes, session.SaveCount);

            Assert.True(session.ReloadFromFile());
            Assert.Equal(saved, fake.Value);
            Assert.Equal(26.0f, fake.Value.Gravity);
            Assert.Equal(9.2f, fake.Value.JumpVelocity);
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary>Resetting a single knob is an edit like any other, so it <i>is</i> written — an edit
    /// that is not written is an edit a crash takes with it.</summary>
    [Fact]
    public void ResettingOneKnob_IsWritten_UnlikeResettingEverything()
    {
        string path = TempFile();
        try
        {
            var fake = new FakeWriter();
            MotorTuningSession session = fake.Session(path);

            Assert.True(session.SetKnob(MotorTuningKnobs.Gravity, 26.0f));
            Assert.True(session.SetKnob(MotorTuningKnobs.JumpVelocity, 9.2f));
            int writes = session.SaveCount;

            Assert.True(session.ResetKnob(MotorTuningKnobs.Gravity));
            Assert.Equal(writes + 1, session.SaveCount);
            Assert.Equal(MotorTuning.Default.Gravity, fake.Value.Gravity);

            var reading = new FakeWriter();
            Assert.True(reading.Session(path).LoadFromFile());
            Assert.Equal(MotorTuning.Default.Gravity, reading.Value.Gravity);
            Assert.Equal(9.2f, reading.Value.JumpVelocity);
        }
        finally
        {
            Delete(path);
        }
    }

    // =============================================================================================
    // 4. The single writer, and the guard (acceptance criterion 8).
    // =============================================================================================

    /// <summary><b>The production session writes through <c>MotorTuning.TryApply</c> and through
    /// nothing else.</b> The absence half of acceptance criterion 8 is a grep over
    /// <c>scripts/</c> for an assignment to <c>MotorTuning.Current</c>, whose positive control is
    /// that it finds the one legitimate writer inside <c>MotorTuning.TryApply</c> itself; this is
    /// the half a test can hold — a session built the way the panel builds it reads and writes the
    /// process-global tuning rather than a copy of its own.</summary>
    [Fact]
    public void TheDefaultWriter_IsTheSingleWriter_AndTheDefaultReaderIsTheLiveTuning()
    {
        var production = new MotorTuningSession("");
        Assert.Equal(MotorTuning.Current, production.Live);
        Assert.Equal(MotorTuning.Default, production.Live);   // nothing in this suite parks a tuning
    }

    /// <summary><b>A refusal is displayed, never worked around.</b> The guard is a hard runtime
    /// refusal in every build; if it says no, the tuning does not move and the reason is on the
    /// readout. The lab being a special case is exactly what the guard is written to allow.</summary>
    [Fact]
    public void WhenTheGuardRefuses_NothingMoves_AndTheReasonReachesTheReadout()
    {
        string path = TempFile();
        try
        {
            var fake = new FakeWriter { Refuse = true };
            MotorTuningSession session = fake.Session(path);

            Assert.False(session.SetKnob(MotorTuningKnobs.Gravity, 26.0f));
            Assert.Equal(MotorTuning.Default, fake.Value);
            Assert.Equal(fake.Refusal, session.LastRefusal);
            Assert.Contains(session.Notices, n => n.StartsWith("REFUSED", StringComparison.Ordinal));
            Assert.Equal(0, session.SaveCount);
            Assert.False(File.Exists(path), "a refused write must not write the file either");

            // And the refusal is not sticky once the guard clears.
            fake.Refuse = false;
            Assert.True(session.SetKnob(MotorTuningKnobs.Gravity, 26.0f));
            Assert.Equal("", session.LastRefusal);
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary>File I/O off — the scripted capture mode — loads nothing and writes nothing, so a
    /// measurement run reads the shipped defaults and can never overwrite a human's tuning.</summary>
    [Fact]
    public void WithFileIoOff_NothingIsReadAndNothingIsWritten()
    {
        var fake = new FakeWriter();
        var session = new MotorTuningSession("", fake.Write, () => fake.Value);

        Assert.False(session.FileIoEnabled);
        Assert.True(session.LoadFromFile());
        Assert.Equal(MotorTuning.Default, fake.Value);
        Assert.Equal(0, session.LoadCount);

        Assert.True(session.SetKnob(MotorTuningKnobs.CameraDipStrengthM, 0.12f));
        Assert.Equal(0.12f, fake.Value.CameraDipStrengthM);
        Assert.Equal(0, session.SaveCount);
    }

    // =============================================================================================
    // 5. The step grid (§6.4 rule 5 — a nudged value must stay printable).
    // =============================================================================================

    /// <summary><b>All thirty-one shipped defaults sit exactly on their own step grid.</b> This is what
    /// makes snapping safe: it can never move a knob off its default, and a knob nudged away and
    /// back lands on the same float.</summary>
    [Fact]
    public void EveryShippedDefault_SitsOnItsOwnStepGrid()
    {
        foreach (MotorKnob knob in MotorTuningSession.Rows)
        {
            Assert.True(MotorTuningSession.IsOnStepGrid(knob, knob.Default),
                $"{knob.Name}: default {knob.Default} is not on a {knob.Step} grid from {knob.Min}");
            Assert.Equal(knob.Default, knob.Get(MotorTuning.Default));
        }
    }

    /// <summary><b>Forty nudges up and forty back land on the value it started at</b>, for every
    /// knob. Naive <c>value += step</c> does not: <c>0.75f</c> plus forty <c>0.01f</c> is not
    /// <c>1.15f</c>, and a knob that has drifted to 1.1500001 prints as a literal nobody typed.</summary>
    [Fact]
    public void NudgingUpAndBackAgain_LandsOnExactlyTheValueItStartedAt()
    {
        var fake = new FakeWriter();
        MotorTuningSession session = fake.Session("");

        foreach (MotorKnob knob in MotorTuningSession.Rows)
        {
            float start = knob.Get(fake.Value);
            int room = (int)MathF.Floor((knob.Max - start) / knob.Step);
            int steps = Math.Min(40, room);
            if (steps <= 0)
                continue;

            // MOVE-8: the up-leg counts the nudges that ACTUALLY moved the value, rather than
            // assuming every one of them can. A knob's ceiling is not always its own Max —
            // Validate enforces the coupled ones — and Talon's speed ruling made that live: the
            // skid entry is a fraction of MoveSpeed, so it fell 4.05 -> 2.85, and SkidExitSpeedMps
            // (which Validate holds under the entry) now runs out of room after 32 nudges instead
            // of 40. Counting the real steps keeps the round-trip claim EXACT — which is this
            // test's whole point — instead of comparing against a top the knob was never allowed
            // to reach.
            int taken = 0;
            for (int i = 0; i < steps; i++)
            {
                float before = knob.Get(fake.Value);
                session.Nudge(knob, +1);
                if (knob.Get(fake.Value) == before)
                    break;
                taken++;
            }
            float top = knob.Get(fake.Value);

            // Still on its own step grid: a naive `value += step` drifts to 1.1500001 and prints as
            // a literal nobody typed, and Snap-of-itself is what catches that.
            Assert.Equal(MotorTuningSession.Snap(knob, top), top);
            if (taken == steps)
                Assert.Equal(MotorTuningSession.Snap(knob, start + steps * knob.Step), top);

            for (int i = 0; i < taken; i++)
                session.Nudge(knob, -1);
            Assert.Equal(start, knob.Get(fake.Value));
        }

        Assert.Equal(MotorTuning.Default, fake.Value);
    }

    /// <summary>A nudge past either end stops at the end rather than wrapping or overshooting, and
    /// stops spending file writes once it is there.</summary>
    [Fact]
    public void ANudgePastTheEnd_StopsAtTheEnd_AndCostsNoFurtherWrites()
    {
        string path = TempFile();
        try
        {
            var fake = new FakeWriter();
            MotorTuningSession session = fake.Session(path);
            MotorKnob knob = MotorTuningKnobs.ApexHangStrength;

            for (int i = 0; i < 500; i++)
                session.Nudge(knob, +1);
            Assert.Equal(knob.Max, knob.Get(fake.Value));

            int writes = session.SaveCount;
            session.Nudge(knob, +1);
            Assert.Equal(writes, session.SaveCount);

            for (int i = 0; i < 500; i++)
                session.Nudge(knob, -1);
            Assert.Equal(knob.Min, knob.Get(fake.Value));
        }
        finally
        {
            Delete(path);
        }
    }

    [Theory]
    [InlineData(1.0f, 0)]
    [InlineData(0.5f, 1)]
    [InlineData(0.1f, 1)]
    [InlineData(0.05f, 2)]
    [InlineData(0.01f, 2)]
    public void DecimalsFor_MatchesTheStep(float step, int decimals) =>
        Assert.Equal(decimals, MotorTuningSession.DecimalsFor(step));

    /// <summary>A non-finite value handed to a slider callback resolves to the shipped default
    /// rather than reaching the writer — a NaN in the motor propagates into position and the body
    /// is gone for good.</summary>
    [Fact]
    public void ANonFiniteSliderValue_ResolvesToTheShippedDefault()
    {
        Assert.Equal(MotorTuningKnobs.Gravity.Default,
            MotorTuningSession.Snap(MotorTuningKnobs.Gravity, float.NaN));
        Assert.Equal(MotorTuningKnobs.Gravity.Default,
            MotorTuningSession.Snap(MotorTuningKnobs.Gravity, float.PositiveInfinity));
    }

    // =============================================================================================
    // 6. Print-as-C# — the call site (acceptance criterion 6).
    // =============================================================================================

    /// <summary><b>The session renders §6.4's block for whatever is live</b>, which is the half of
    /// the print the panel does not need an engine for. The clipboard and the archive
    /// (<c>MotorTuningPrint.Publish</c>) are engine-only and are exercised headed by MOVE-4e.</summary>
    [Fact]
    public void ThePrintCallSite_RendersTheLiveTuning_AndOnlyWhatMoved()
    {
        var fake = new FakeWriter();
        MotorTuningSession session = fake.Session("");
        Assert.True(session.SetKnob(MotorTuningKnobs.Acceleration, 12.5f));

        string block = session.Render(all: false, new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero));

        Assert.Contains("1 of 59 knobs differ", block, StringComparison.Ordinal);   // FP-1
        Assert.Contains("        Acceleration = 12.5f,", block, StringComparison.Ordinal);
        Assert.Contains("// was 9", block, StringComparison.Ordinal);
        Assert.DoesNotContain("        Deceleration = ", block, StringComparison.Ordinal);

        // MOVE-4d: the paste target is MotorTuning.Default's initializer, so every row prints under
        // its own field name — see MotorTuningPrint's class comment for why it is no longer the
        // AvatarMotor constants.
        Assert.Contains("PASTE TARGET: " + MotorTuningPrint.TargetFile, block, StringComparison.Ordinal);

        string all = session.Render(all: true, new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero));
        foreach (MotorKnob knob in MotorTuningSession.Rows)
            Assert.Contains("        " + knob.Name + " = ", all, StringComparison.Ordinal);
    }

    // =============================================================================================
    // 7. The readout's own honesty.
    // =============================================================================================

    /// <summary><b>The status line is computed from the live tuning every time it is asked</b> —
    /// there is no cached count anywhere in the session, because a panel that shows a cached number
    /// can lie about what the motor is doing.</summary>
    [Fact]
    public void TheStatusLine_TracksTheLiveTuning_AndNamesABreachWhenThereIsOne()
    {
        var fake = new FakeWriter();
        MotorTuningSession session = fake.Session("");

        Assert.Contains("0 of 59 moved", session.StatusLine(), StringComparison.Ordinal);   // FP-1
        Assert.Contains("all pins hold", session.StatusLine(), StringComparison.Ordinal);

        Assert.True(session.SetKnob(MotorTuningKnobs.Deceleration, 17.5f));
        Assert.Contains("1 of 59 moved", session.StatusLine(), StringComparison.Ordinal);   // FP-1
        Assert.Contains("BREACHED", session.StatusLine(), StringComparison.Ordinal);
    }

    /// <summary>The notice list is bounded: a lab session reports every clamp, and a readout that
    /// grows without bound stops being readable long before it stops being correct.</summary>
    [Fact]
    public void TheNoticeList_IsBounded_AndKeepsTheNewest()
    {
        var session = new MotorTuningSession("", (in MotorTuning _, out string r) => { r = ""; return true; },
            () => MotorTuning.Default);

        // The sink is what the panel points at the Godot console, so a warning that scrolled off
        // the visible lines is still findable — it must see every notice, not only the kept ones.
        var mirrored = new List<string>();
        session.NoticeSink = mirrored.Add;

        for (int i = 0; i < MotorTuningSession.MaxNotices * 3; i++)
            session.Note($"notice {i}");

        Assert.Equal(MotorTuningSession.MaxNotices, session.Notices.Count);
        Assert.Equal($"notice {MotorTuningSession.MaxNotices * 3 - 1}", session.Notices[^1]);
        Assert.Equal(MotorTuningSession.MaxNotices * 3, mirrored.Count);

        session.Note("   ");   // blank notices are not notices
        Assert.Equal(MotorTuningSession.MaxNotices, session.Notices.Count);
        Assert.Equal(MotorTuningSession.MaxNotices * 3, mirrored.Count);

        session.ClearNotices();
        Assert.Empty(session.Notices);
    }

    // =============================================================================================
    // MOVE-5d. Twenty-two rows arrived in the table and none of them were typed into the panel;
    // these are the assertions that say so, and that say the rows carry what §11.1 wrote down.
    // =============================================================================================

    /// <summary>
    /// <b>Acceptance criterion 2, transcribed independently for MOVE-5 §11.1's twenty-two rows.</b>
    /// A second, deliberately hand-typed copy of the spec's table — the only kind of duplication
    /// that catches a transcription slip, and the same shape MOVE-4d gave rows 1-30.
    ///
    /// <para><b>The default is asserted here too, and that closes a real gap.</b>
    /// <c>MotorTuningDefaultIdentityTests</c> transcribes literals up to row 31 and stops;
    /// MOVE-5's twenty-two are covered there only by
    /// <c>TheKnobTablesDefault_IsTheTuningsDefault_ForEveryRow</c>, which proves the table and the
    /// struct AGREE but not that either is what §11.1 says. Two of these rows are exact no-ops that
    /// the whole wave's "the approved body did not move" claim rests on — <c>ChainBonusMps</c> at
    /// 0.00 and <c>AirJumpMode</c> at 0 — and a silent drift in either would change the shipped feel
    /// with no slider touched.</para>
    ///
    /// <para><c>CameraDipRampPower</c> is in this block rather than MOVE-4d's because MOVE-4f added
    /// it after that <c>[Theory]</c> was written and it was never appended — found by MOVE-5d, fixed
    /// here rather than reported, because the fix is one line.</para>
    /// </summary>
    [Theory]
    // name, default, min, max, step — spec §11.1, rows 31-52, in the table's own order
    [InlineData("JumpHoldWindowSec", 0.20f, 0.05f, 0.60f, 0.01f)]
    [InlineData("TouchdownSlideImmediate", 0f, 0f, 1f, 1f)]
    [InlineData("SlideEnterSpeedFraction", 1.22f, 0.50f, 2.00f, 0.01f)]
    [InlineData("SlideExitSpeedMps", 2.00f, 0.10f, 6.00f, 0.05f)]
    [InlineData("SlideDeceleration", 6.0f, 0.5f, 40.0f, 0.5f)]
    [InlineData("SlideTurnRateDeg", 90f, 0f, 360f, 5f)]
    [InlineData("SlideMinSec", 0.12f, 0.00f, 0.50f, 0.01f)]
    [InlineData("SlideMaxSec", 1.40f, 0.10f, 3.00f, 0.05f)]
    [InlineData("DuckWalkSpeedFraction", 0.45f, 0.10f, 1.00f, 0.01f)]
    [InlineData("DuckWalkGuaranteed", 0f, 0f, 1f, 1f)]
    [InlineData("DuckWalkEntryConeDeg", 60f, 0f, 180f, 5f)]
    [InlineData("ChainGraceSec", 0.35f, 0.05f, 2.00f, 0.01f)]
    [InlineData("ChainDecayIntervalSec", 0.50f, 0.05f, 3.00f, 0.05f)]
    [InlineData("ChainBonusMps", 0.00f, 0.00f, 3.00f, 0.05f)]
    [InlineData("ChainMaxDepth", 4f, 0f, 7f, 1f)]
    // MOVE-8: Talon ruled the traditional double jump on 2026-08-28, so this row LEFT its exact
    // no-op. It is the only one of the twenty-two that moved, and the paragraph above — "a silent
    // drift in either would change the shipped feel with no slider touched" — is why it is
    // re-pinned here in the same session rather than left to be discovered.
    [InlineData("AirJumpMode", 1f, 0f, 2f, 1f)]
    [InlineData("AirJumpCountMax", 1f, 0f, 3f, 1f)]
    [InlineData("AirJumpVelocityFraction", 0.80f, 0.10f, 1.50f, 0.05f)]
    [InlineData("KickConversionFraction", 0.35f, 0.00f, 1.00f, 0.01f)]
    [InlineData("KickHorizontalGainMps", 1.20f, 0.00f, 5.00f, 0.05f)]
    [InlineData("KickVerticalMps", 1.60f, 0.00f, 6.00f, 0.05f)]
    [InlineData("KickMinSpeedMps", 4.05f, 0.00f, 12.00f, 0.05f)]
    // MOVE-4f's row 31, missing from the MOVE-4 block above.
    [InlineData("CameraDipRampPower", 6f, 1.0f, 12.0f, 0.5f)]
    public void EveryMoveFiveSliderRange_MatchesTheSpecTable(string name, float shipped, float min,
        float max, float step)
    {
        Assert.True(MotorTuningKnobs.TryByName(name, out MotorKnob knob), $"no knob row named {name}");
        Assert.Equal(shipped, knob.Default);
        Assert.Equal(min, knob.Min);
        Assert.Equal(max, knob.Max);
        Assert.Equal(step, knob.Step);
        Assert.Equal(shipped, knob.Get(MotorTuning.Default));
    }

    /// <summary>
    /// <b>All twenty-two are on the panel, in §11.1's groups, and the two new groups arrived
    /// whole.</b> Acceptance criterion 1's positive half at the wave's own granularity: the general
    /// assertions above would still pass if a MOVE-5 row had been dropped from
    /// <c>MotorTuningKnobs.All</c>, because they compare the table with itself.
    /// </summary>
    [Fact]
    public void AllTwentyTwoMoveFiveRows_AreOnThePanel_InTheGroupsTheSpecAssignsThem()
    {
        string[] slide =
        {
            "JumpHoldWindowSec", "TouchdownSlideImmediate", "SlideEnterSpeedFraction",
            "SlideExitSpeedMps", "SlideDeceleration", "SlideTurnRateDeg", "SlideMinSec",
            "SlideMaxSec", "DuckWalkSpeedFraction", "DuckWalkGuaranteed", "DuckWalkEntryConeDeg",
        };
        string[] chain =
        {
            "ChainGraceSec", "ChainDecayIntervalSec", "ChainBonusMps", "ChainMaxDepth",
        };
        string[] air =
        {
            "AirJumpMode", "AirJumpCountMax", "AirJumpVelocityFraction", "KickConversionFraction",
            "KickHorizontalGainMps", "KickVerticalMps", "KickMinSpeedMps",
        };
        Assert.Equal(22, slide.Length + chain.Length + air.Length);

        MotorTuningSession.KnobGroup Group(string name) =>
            Assert.Single(MotorTuningSession.Groups.Where(g => g.Name == name));

        // Whole groups, in order: the Slide and Chain sections ARE these rows and nothing else.
        Assert.Equal(slide, Group("Slide").Knobs.Select(k => k.Name).ToArray());
        Assert.Equal(chain, Group("Chain").Knobs.Select(k => k.Name).ToArray());

        // Air is MOVE-4's existing group EXTENDED, which is why there must still be exactly one of
        // it — Assert.Single above is the assertion that would have caught two sections both
        // called "Air", the defect MOVE-5b's handoff calls out as visible to Talon and to no test.
        string[] airRows = Group("Air").Knobs.Select(k => k.Name).ToArray();
        Assert.Equal(new[] { "AirControlBuild", "AirControlTurn", "AirControlBrake" }.Concat(air),
            airRows);

        // And every one of them is reachable by the keyboard walk, which is the flat view.
        foreach (string name in slide.Concat(chain).Concat(air))
            Assert.Contains(MotorTuningSession.Rows, k => k.Name == name);
    }

    /// <summary>
    /// <b>Spec §11.5's shape finding, acted on.</b> Six rows are discrete — the two toggles, the two
    /// mode switches and the two wire widths — and a continuous slider with two or three stops reads
    /// as broken, so <see cref="MotorKnobRow.DiscreteStops"/> paints a tick per legal value.
    ///
    /// <para><b>Derived, never named.</b> The rule is "whole step, whole bounds, few enough stops to
    /// be information"; no knob is listed in the widget. This test names them only to assert the
    /// rule picks out exactly the six — and, in the same breath, that it does NOT pick out
    /// <c>TurnAcceleration</c>, whose step is also 1 over a span of 78.</para>
    ///
    /// <para><b>MOVE-5f added the sixth, <c>AnticipationMode</c></b> — the same shape
    /// <c>AirJumpMode</c> is, three positions with off first — and the derived rule picked it out
    /// with no edit to the widget, which is exactly what the rule exists for.</para>
    /// </summary>
    [Fact]
    public void TheSevenDiscreteRows_PaintTheirStops_AndNoContinuousRowDoes()
    {
        var discrete = MotorTuningSession.Rows
            .Where(k => MotorKnobRow.DiscreteStops(k) > 0)
            .ToDictionary(k => k.Name, MotorKnobRow.DiscreteStops, StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                "AirJumpCountMax", "AirJumpMode", "AnticipationMode", "BodyYawFollowsAim",
                "ChainMaxDepth", "DuckWalkGuaranteed", "TouchdownSlideImmediate",
            },
            discrete.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray());

        Assert.Equal(3, discrete["AirJumpMode"]);          // 0, 1, 2
        Assert.Equal(3, discrete["AnticipationMode"]);     // off, the real coil, the baked coil
        Assert.Equal(4, discrete["AirJumpCountMax"]);      // 0-3, a two-bit wire width
        Assert.Equal(8, discrete["ChainMaxDepth"]);        // 0-7, a three-bit wire width
        Assert.Equal(2, discrete["TouchdownSlideImmediate"]);
        Assert.Equal(2, discrete["DuckWalkGuaranteed"]);
        Assert.Equal(2, discrete["BodyYawFollowsAim"]);   // FP-1: travel facing, or the look

        // The negative case, named: a whole step over a wide range is a continuous knob.
        Assert.True(MotorTuningKnobs.TryByName("TurnAcceleration", out MotorKnob turn));
        Assert.Equal(1f, turn.Step);
        Assert.Equal(0, MotorKnobRow.DiscreteStops(turn));

        // Every stop a discrete row paints is a value the step grid can actually reach, or the
        // ticks would be drawn where the grabber can never rest.
        foreach (MotorKnob knob in MotorTuningSession.Rows.Where(k => MotorKnobRow.DiscreteStops(k) > 0))
            for (int i = 0; i < MotorKnobRow.DiscreteStops(knob); i++)
                Assert.True(MotorTuningSession.IsOnStepGrid(knob, knob.Min + i * knob.Step),
                    $"{knob.Name} stop {i}");
    }

    /// <summary>
    /// <b>No count is typed into the panel's own text.</b> MOVE-4d's key help and print notice both
    /// said "all 30", which was a hand-maintained total of a generated list — a second list wearing
    /// a disguise — and it was wrong the moment MOVE-4f added row 31.
    ///
    /// <para><b>Positive control included:</b> the assertion is that the help quotes the table's
    /// live count, so it is checked against <c>MotorTuningKnobs.All.Count</c> and then checked
    /// again against the stale literals, which must be gone.</para>
    /// </summary>
    [Fact]
    public void ThePanelsKeyHelp_QuotesTheTablesOwnCount_NotATypedTotal()
    {
        string help = MotorTuningPanel.KeyHelp;
        Assert.Contains($"all {MotorTuningKnobs.All.Count}", help, StringComparison.Ordinal);
        Assert.DoesNotContain("all 30", help, StringComparison.Ordinal);
        Assert.DoesNotContain("all 31", help, StringComparison.Ordinal);
    }
}
