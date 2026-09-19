using System.IO;
using System.Linq;
using System.Reflection;
using Sail.Game.Water;
using Sail.Game.Water.Fx;

namespace SailNet.Tests;

/// <summary>
/// Packet W4's budget and register rules, proved without an engine.
///
/// <b>What this tier can and cannot say.</b> It cannot say a splash looks right, and it does not
/// try — that is what the locked-camera capture sequence under
/// <c>docs/superpowers/status/2026-08-08-lake-w4/</c> is for. What it can say is that the numbers
/// spec §9.1 fixed are actually enforced on every path including the hostile ones, and that the
/// three hard calls direction §5 and §10.3 made are structurally impossible to violate rather
/// than merely remembered in a comment.
///
/// <b>Every "X is absent" check carries a positive control.</b> A diagnostic that reports
/// "absent" is worthless until it has been shown able to report "present" — this repo has shipped
/// source-scanning checks that silently never matched anything. So the reflection assertions
/// below each prove the same technique finds a known-present thing, and the source scan proves it
/// finds a known-present call before it claims anything about W4's.
/// </summary>
public class WaterFxTuningTests
{
    private static readonly WaterEventKind[] AllKinds =
    {
        WaterEventKind.Entered,
        WaterEventKind.Exited,
        WaterEventKind.Splash,
        WaterEventKind.WentUnder,
        WaterEventKind.Sputtered,
    };

    /// <summary>Speeds spanning the reachable range plus every hostile value a corrupt wire byte
    /// can produce. W2 sanitises to <c>Max(0, speed)</c> before raising, so the negatives and
    /// non-finites here are belt-and-braces — but a total function is cheaper than a proof that
    /// nothing upstream will ever change.</summary>
    private static readonly float[] Speeds =
    {
        0f, 0.5f, 1f, 3f, 6f, 12f, 1e6f,
        -1f, -1e9f, float.NaN, float.PositiveInfinity, float.NegativeInfinity,
    };

    private static readonly float[] NightWeights =
    {
        0f, 0.1f, 0.25f, 0.5f, 0.75f, 0.9f, 1f,
        -0.5f, 1.5f, float.NaN, float.PositiveInfinity,
    };

    // --- Spec §9.1: the hard ceilings ------------------------------------------------------------

    [Fact]
    public void BurstCount_NeverExceedsTheSixtyFourCeiling_ForAnyInput()
    {
        foreach (WaterEventKind kind in AllKinds)
        foreach (float speed in Speeds)
        foreach (float night in NightWeights)
        {
            int n = WaterFxTuning.BurstCount(kind, speed, night);
            Assert.InRange(n, 1, WaterFxTuning.MaxParticlesPerBurst);
        }
    }

    /// <summary>The other end of the clamp, and it matters as much. An event that emits zero
    /// particles is indistinguishable from an event that never fired, and the night subtraction
    /// is aggressive enough (0.45) that a floor-to-zero on the smallest burst was reachable.</summary>
    [Fact]
    public void BurstCount_IsNeverZero_EvenAtFullNightOnTheSmallestEvent()
    {
        int smallest = WaterFxTuning.BurstCount(WaterEventKind.Exited, 0f, 1f);
        Assert.True(smallest >= 1, $"the smallest possible burst emitted {smallest} particles");
    }

    /// <summary>Every day value sits at or under half the ceiling, so the clamp above is a guard
    /// rather than the working number. If a future tuning pass pushes a count into the clamp, the
    /// ceiling silently becomes the value and the tuning stops meaning anything.</summary>
    [Fact]
    public void EveryDayBurst_SitsWellInsideTheCeilingRatherThanAgainstIt()
    {
        foreach (WaterEventKind kind in AllKinds)
        {
            int n = WaterFxTuning.BurstCount(kind, 1e6f, 0f);
            Assert.True(n <= WaterFxTuning.MaxParticlesPerBurst * 3 / 4,
                $"{kind} at max speed emits {n}, which is close enough to the {WaterFxTuning.MaxParticlesPerBurst} " +
                "ceiling that the clamp is doing the tuning");
        }
    }

    [Fact]
    public void ConcurrencyCeilings_MatchTheSpecAndThePoolIsSizedToThem()
    {
        Assert.Equal(64, WaterFxTuning.MaxParticlesPerBurst);
        Assert.Equal(6, WaterFxTuning.MaxConcurrentBursts);
        Assert.Equal(WaterFxTuning.MaxConcurrentBursts, SplashParticlePool.PoolSize);
        Assert.Equal(35f, WaterFxTuning.ParticleCullM);
    }

    // --- Spec §9: a running jump throws more than a step ------------------------------------------

    [Fact]
    public void EntryBurst_GrowsWithSpeed_AndIsMonotonic()
    {
        int step = WaterFxTuning.BurstCount(WaterEventKind.Entered, 0.5f, 0f);
        int run = WaterFxTuning.BurstCount(WaterEventKind.Entered, WaterFxTuning.EntrySpeedRefMps, 0f);
        Assert.True(run > step * 2,
            $"a running entry ({run}) should throw substantially more than a step ({step})");

        int previous = -1;
        for (float s = 0f; s <= 10f; s += 0.1f)
        {
            int n = WaterFxTuning.BurstCount(WaterEventKind.Entered, s, 0f);
            Assert.True(n >= previous, $"entry burst went backwards at speed {s}: {n} < {previous}");
            previous = n;
        }
    }

    /// <summary>Only the entry reads speed. A thrash that got bigger the faster you flailed would
    /// turn W2's throttled stream into a dial the player holds down.</summary>
    [Fact]
    public void OnlyTheEntryReadsSpeed()
    {
        foreach (WaterEventKind kind in AllKinds.Where(k => k != WaterEventKind.Entered))
        {
            Assert.Equal(
                WaterFxTuning.BurstCount(kind, 0f, 0f),
                WaterFxTuning.BurstCount(kind, 20f, 0f));
            Assert.Equal(
                WaterFxTuning.VolumeDb(kind, 0f),
                WaterFxTuning.VolumeDb(kind, 20f));
        }
        // Positive control: the technique above CAN see a difference where one exists.
        Assert.NotEqual(
            WaterFxTuning.BurstCount(WaterEventKind.Entered, 0f, 0f),
            WaterFxTuning.BurstCount(WaterEventKind.Entered, 20f, 0f));
        Assert.NotEqual(
            WaterFxTuning.VolumeDb(WaterEventKind.Entered, 0f),
            WaterFxTuning.VolumeDb(WaterEventKind.Entered, 20f));
    }

    // --- Direction §10.3: what the night may and may not touch -------------------------------------

    [Fact]
    public void Night_ReducesCountAndLifetime_ForEveryEvent()
    {
        foreach (WaterEventKind kind in AllKinds)
        {
            int day = WaterFxTuning.BurstCount(kind, 3f, 0f);
            int night = WaterFxTuning.BurstCount(kind, 3f, 1f);
            Assert.True(night < day, $"{kind}: night count {night} did not fall below day's {day}");

            float dayLife = WaterFxTuning.BurstLifetimeSec(kind, 0f);
            float nightLife = WaterFxTuning.BurstLifetimeSec(kind, 1f);
            Assert.True(nightLife < dayLife,
                $"{kind}: night lifetime {nightLife} did not fall below day's {dayLife}");
        }
    }

    /// <summary>
    /// Direction §10.3's named trap: "Do not shrink it to compensate. Size is silhouette; count
    /// and lifetime are duration."
    ///
    /// Asserted structurally rather than by value, because a value check would pass a version of
    /// the function that took a night weight and happened to ignore it today. A method with no
    /// such parameter cannot be made to violate the rule by an edit that only moves a constant.
    /// </summary>
    [Fact]
    public void NightNeverTouchesParticleSize_BecauseTheFunctionCannotSeeIt()
    {
        MethodInfo? size = typeof(WaterFxTuning).GetMethod(nameof(WaterFxTuning.ParticleSizeM));
        Assert.NotNull(size);
        Assert.Single(size!.GetParameters());
        Assert.Equal(typeof(WaterEventKind), size.GetParameters()[0].ParameterType);

        // Positive control: the same reflection technique DOES see a night parameter where one
        // is supposed to be, so "no night parameter" above is a finding and not a dead check.
        MethodInfo? emission = typeof(WaterFxTuning).GetMethod(nameof(WaterFxTuning.EmissionEnergy));
        Assert.NotNull(emission);
        Assert.Single(emission!.GetParameters());
        Assert.Equal("nightWeight", emission.GetParameters()[0].Name);
    }

    /// <summary>Velocity is silhouette in motion. Cutting it at night would be the same mistake as
    /// shrinking size, so it gets the same structural guard.</summary>
    [Fact]
    public void NightNeverTouchesBurstVelocity_BecauseTheFunctionCannotSeeIt()
    {
        MethodInfo? vel = typeof(WaterFxTuning).GetMethod(nameof(WaterFxTuning.BurstVelocityMps));
        Assert.NotNull(vel);
        Assert.Equal(2, vel!.GetParameters().Length);
        Assert.DoesNotContain(vel.GetParameters(), p => p.Name == "nightWeight");
    }

    [Fact]
    public void NightEmission_IsCappedHard_AndFarBelowDay()
    {
        Assert.Equal(WaterFxTuning.DayEmissionEnergy, WaterFxTuning.EmissionEnergy(0f), 1e-4f);
        Assert.Equal(WaterFxTuning.NightEmissionEnergy, WaterFxTuning.EmissionEnergy(1f), 1e-4f);
        Assert.True(WaterFxTuning.NightEmissionEnergy < WaterFxTuning.DayEmissionEnergy * 0.25f,
            "the night cap is not a cap if it is within a factor of four of day");

        // Monotone and never NaN across the whole range including hostile input.
        float previous = float.MaxValue;
        for (float w = 0f; w <= 1f; w += 0.01f)
        {
            float e = WaterFxTuning.EmissionEnergy(w);
            Assert.True(float.IsFinite(e));
            Assert.True(e <= previous + 1e-5f, $"emission rose at night weight {w}");
            previous = e;
        }
        foreach (float hostile in NightWeights)
            Assert.True(float.IsFinite(WaterFxTuning.EmissionEnergy(hostile)));
    }

    /// <summary>
    /// The other half of the cap, and the half a headless check nearly let through. The droplet
    /// material is Unshaded — a deliberate call, because the lake gets no light and a lit droplet
    /// at night would render black — which means the ALBEDO is the brightness and capping emission
    /// alone caps almost nothing. The first headed night capture came back with a stark white
    /// cluster as the brightest thing in the frame while the emission number was exactly right.
    /// </summary>
    [Fact]
    public void NightAlbedo_IsCappedTooBecauseTheMaterialIsUnshaded()
    {
        var day = WaterFxTuning.DropletTint(0f);
        var night = WaterFxTuning.DropletTint(1f);

        Assert.True(night.R < day.R * 0.5f,
            $"night albedo value {night.R} against day's {day.R} — on an unshaded material this " +
            "is what decides whether the splash is a shape or a flare");
        Assert.True(night.A < day.A, "night spray should be thinner as well as darker");

        // The cool cast is the one deliberate colour call: spray must not read as the dust motes
        // the atmosphere layer already puts in the air.
        Assert.True(day.B > day.R && night.B > night.R, "the droplet lost its cool cast");

        // Total and monotone across the range, hostile input included.
        float previous = float.MaxValue;
        for (float w = 0f; w <= 1f; w += 0.01f)
        {
            var c = WaterFxTuning.DropletTint(w);
            Assert.True(float.IsFinite(c.R) && c.R is >= 0f and <= 1f);
            Assert.True(float.IsFinite(c.A) && c.A is >= 0f and <= 1f);
            Assert.True(c.R <= previous + 1e-5f, $"albedo brightened toward night at {w}");
            previous = c.R;
        }
        foreach (float hostile in NightWeights)
            Assert.True(float.IsFinite(WaterFxTuning.DropletTint(hostile).R));
    }

    // --- Direction §5: one event set, no night variant --------------------------------------------

    /// <summary>
    /// The hard call this packet was told not to relitigate, asserted the only way it can be:
    /// there is exactly one public entry point into the clip set and it takes only the event kind.
    /// A <c>For(kind, nightWeight)</c> overload, or a second <c>NightFor</c>, would be the night
    /// variant arriving through the back door.
    /// </summary>
    [Fact]
    public void ThereIsExactlyOneClipSet_AndNoWayToAskForANightOne()
    {
        MethodInfo[] api = typeof(WaterSfx)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .ToArray();

        Assert.Single(api);
        Assert.Equal(nameof(WaterSfx.For), api[0].Name);
        Assert.Single(api[0].GetParameters());
        Assert.Equal(typeof(WaterEventKind), api[0].GetParameters()[0].ParameterType);

        // Positive control: this reflection query is capable of returning more than one method,
        // so "exactly one" above is a measurement rather than an artefact of the filter.
        Assert.True(
            typeof(WaterFxTuning)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Length > 1);
    }

    // --- Spec §9.1: distance culling ---------------------------------------------------------------

    [Fact]
    public void DistanceCulls_HoldAtTheStatedRadii_AndAudioReachesFurtherThanSight()
    {
        const float eps = 0.01f;
        float pc = WaterFxTuning.ParticleCullM;
        float ac = WaterFxTuning.AudioCullM;

        Assert.True(WaterFxTuning.ParticlesVisibleAt((pc - eps) * (pc - eps)));
        Assert.False(WaterFxTuning.ParticlesVisibleAt((pc + eps) * (pc + eps)));
        Assert.True(WaterFxTuning.AudibleAt((ac - eps) * (ac - eps)));
        Assert.False(WaterFxTuning.AudibleAt((ac + eps) * (ac + eps)));

        Assert.True(ac > pc, "a splash should be hearable from further than it is resolvable");
        // The audio cull must land exactly where attenuation has already reached silence, or the
        // cut is audible — which is the click the whole sweep discipline exists to avoid.
        Assert.Equal(WaterFxTuning.AudioMaxDistanceM, WaterFxTuning.AudioCullM);

        // Hostile input is culled, never emitted.
        Assert.False(WaterFxTuning.ParticlesVisibleAt(float.NaN));
        Assert.False(WaterFxTuning.AudibleAt(float.NaN));
        Assert.False(WaterFxTuning.ParticlesVisibleAt(float.PositiveInfinity));
    }

    // --- Direction §10.2: the submersion filter -----------------------------------------------------

    [Fact]
    public void FilterCutoff_SweepsMonotonicallyAndHitsBothEndsExactly()
    {
        Assert.Equal(WaterFxTuning.FilterOpenHz, WaterFxTuning.FilterCutoffHz(0f), 1f);
        Assert.Equal(WaterFxTuning.FilterSubmergedHz, WaterFxTuning.FilterCutoffHz(1f), 1f);

        float previous = float.MaxValue;
        for (float s = 0f; s <= 1f; s += 0.005f)
        {
            float hz = WaterFxTuning.FilterCutoffHz(s);
            Assert.True(float.IsFinite(hz) && hz > 0f, $"cutoff at {s} was {hz}");
            Assert.True(hz <= previous + 1e-3f, $"cutoff rose while submerging, at {s}");
            previous = hz;
        }
        foreach (float hostile in NightWeights)
            Assert.True(float.IsFinite(WaterFxTuning.FilterCutoffHz(hostile)));
    }

    /// <summary>
    /// It is a filter, never a gain, and never a silence. 700 Hz keeps speech intelligible and the
    /// bed present — direction §10.2 is explicit that under the surface the world is filtered and
    /// that silence is reserved for THRILL §6.3's device, which this packet is forbidden to spend.
    /// </summary>
    [Fact]
    public void SubmergedCutoff_MufflesWithoutSilencing()
    {
        Assert.InRange(WaterFxTuning.FilterSubmergedHz, 300f, 1500f);
    }

    /// <summary>The sweep is a build, so it takes as long as the descent it belongs to; the
    /// re-open is a release, so it is faster. Direction §8.2 and §5 respectively.</summary>
    [Fact]
    public void FilterTimings_MatchTheEventTheyBelongTo()
    {
        Assert.Equal(WaterGeometry.GoUnderSec, WaterFxTuning.FilterCloseSec);
        Assert.True(WaterFxTuning.FilterOpenSec < WaterFxTuning.FilterCloseSec,
            "the world coming back is the release; it should not take as long as the descent");
    }

    // --- Direction §10.1: the voice arithmetic --------------------------------------------------------

    /// <summary>
    /// The flagged collision, answered as arithmetic. The written ceiling is 24 concurrent 3D
    /// voices; the camp spends 19 (5 <c>VoiceSpeaker</c>s at a full lobby + SfxLab's 14-slot cap),
    /// leaving 5 against 5 possible remote chatter voices. This packet must add zero to that
    /// count, and it does — every water sound rents from the existing 14 and there is no
    /// continuous emitter.
    /// </summary>
    [Fact]
    public void WaterAddsNoConcurrent3DVoices_SoProximityVoiceCannotBeStarved()
    {
        Assert.Equal(0, WaterFxTuning.LapLoopVoices);
        Assert.True(WaterFxTuning.MaxConcurrentWaterVoices < 14,
            "water must leave slots in the shared pool for footsteps, the shutter and the match");
        Assert.True(WaterFxTuning.MaxConcurrentWaterVoices >= 3,
            "a cap this low would drop audible splashes in an ordinary two-player swim");

        const int voiceSpeakers = 5;   // a full six-player lobby: five remote peers
        const int sfxPoolSlots = 14;   // SfxLab.PoolSize, a hard cap that cannot grow
        const int writtenCeiling = 24;
        Assert.True(voiceSpeakers + sfxPoolSlots + WaterFxTuning.LapLoopVoices <= writtenCeiling);
    }

    /// <summary>
    /// The cold's third channel resolves direction §10.1's flagged collision by not spending it.
    ///
    /// The collision was real: 5 free 3D voices against exactly 5 possible remote chatter voices
    /// in a six-player lobby, before anything else asks. The resolution is that the channel is
    /// owner-only and non-positional, which costs zero 3D voices — the same arithmetic that let
    /// <c>AmbientBed</c>'s continuous layers exist. That claim lives in the TYPE of the player
    /// node, which the scene self-test checks; what is checkable here is the timing contract it
    /// shares with the rumble.
    /// </summary>
    [Fact]
    public void ChillChatter_QuickensWithTheColdAndIsNeverAMetronome()
    {
        float onset = ChillChatter.PeriodFor(0.05f, 0f);
        float full = ChillChatter.PeriodFor(1f, 0f);
        Assert.True(full < onset * 0.5f,
            $"period at full chill ({full}) should be far shorter than at onset ({onset})");

        // Monotone: the cue never slows down as the cold gets worse. It is a fairness contract
        // (LEVEL-BIBLE §8.2) and a cue that eased off would be lying about the clock.
        float previous = float.MaxValue;
        for (float i = 0f; i <= 1f; i += 0.02f)
        {
            float p = ChillChatter.PeriodFor(i, 0f);
            Assert.True(p > 0f && float.IsFinite(p), $"period at intensity {i} was {p}");
            Assert.True(p <= previous + 1e-4f, $"the cue slowed down at intensity {i}");
            previous = p;
        }

        // Irregular: two shots at the same intensity, at different moments, are different lengths
        // apart. A random interval reads as a fault; a shifting one reads as a body.
        Assert.True(System.Math.Abs(ChillChatter.PeriodFor(0.5f, 0f)
            - ChillChatter.PeriodFor(0.5f, 1.9f)) > 0.05f,
            "consecutive intervals at the same intensity are identical — a metronome, not a body");

        // ...but deterministic, so it is not RNG wearing a shape.
        Assert.Equal(ChillChatter.PeriodFor(0.5f, 1.9f), ChillChatter.PeriodFor(0.5f, 1.9f), 1e-6f);

        // A non-positive period would be a shot every frame.
        Assert.True(ChillChatter.PeriodFor(float.NaN, float.NaN) > 0f);
        Assert.True(ChillChatter.PeriodFor(2f, 1e9f) > 0f);
        Assert.True(ChillChatter.PeriodFor(-5f, -1e9f) > 0f);

        Assert.True(ChillChatter.VolumeDbFor(1f) > ChillChatter.VolumeDbFor(0f),
            "the chatter should rise with the cue, not sit at one level");
        Assert.True(ChillChatter.VolumeDbFor(1f) < 0f, "the chatter should never be at unity");
    }

    // --- The wiring assertion -----------------------------------------------------------------------

    /// <summary>
    /// <b>The question this repo fails hardest.</b> Everything above proves the numbers are right;
    /// none of it proves a player ever reaches them. <c>WaterFx</c> has exactly one construction
    /// site in the whole codebase — <c>Gameplay._Ready</c> — and if that line is deleted the
    /// feature is silently gone in the real game while every test here still passes.
    ///
    /// So the line itself is the assertion. Guarded by two controls, because a source scan that
    /// silently never matches is exactly the failure this technique has already shipped once: it
    /// must find a call that is definitely there (<c>ChillCueOverlay.Attach</c>, wired in the same
    /// block), and it must NOT find one that definitely is not.
    /// </summary>
    [Fact]
    public void WaterFx_IsConstructedInTheLiveGamePath_NotOnlyInALab()
    {
        string gameplay = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "game", "Gameplay.cs"));

        Assert.Contains("ChillCueOverlay.Attach(this)", gameplay);          // control: finds present
        Assert.DoesNotContain("ThisCallDoesNotExist.Attach(this)", gameplay); // control: not blind

        Assert.Contains("Fx.WaterFx.Attach(this)", gameplay);
    }

    /// <summary>The listener must attach AFTER the service it subscribes to is constructed, or its
    /// <c>_Ready</c> finds a null <c>Instance</c> and silently subscribes to nothing — a failure
    /// with no error, no log line and no visible symptom except that the lake is mute.</summary>
    [Fact]
    public void WaterFx_IsAttachedAfterTheServiceItSubscribesTo()
    {
        string gameplay = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "game", "Gameplay.cs"));
        int service = gameplay.IndexOf("_waterService.Setup(", System.StringComparison.Ordinal);
        int listener = gameplay.IndexOf("Fx.WaterFx.Attach(this)", System.StringComparison.Ordinal);

        Assert.True(service >= 0, "could not find WaterService.Setup in Gameplay.cs");
        Assert.True(listener >= 0, "could not find WaterFx.Attach in Gameplay.cs");
        Assert.True(listener > service,
            "WaterFx.Attach runs before WaterService.Setup, so its _Ready subscribes to nothing");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
