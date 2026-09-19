using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MpFoundation.Game.World;

namespace Sail.Game.Water.Fx;

/// <summary>
/// Headless self-test of packet W4 <b>where it actually lives</b> — through the real
/// <see cref="WaterFx.Dispatch"/>, into the real <see cref="SplashParticlePool"/>, against a real
/// <see cref="AudioServer"/> bus tree.
///
/// <b>Why this exists alongside <c>WaterFxTuningTests</c>.</b> The xUnit tier proves the budget
/// arithmetic far more thoroughly than a scene test could. What it cannot prove is that any of it
/// is <i>reachable</i>: that the pool really stops at six nodes instead of allocating one per
/// splash, that <c>Amount</c> really stays pinned so the buffer is not reallocated every burst,
/// that the filter really lands on <c>Sfx</c> and <c>Ambient</c> and really does not land on
/// <c>Voice</c>, and that the night weight really reaches the emitter rather than being computed
/// and dropped. The dominant failure mode on this project is a system built and wired to nothing,
/// and green unit tests are not evidence against it. Everything below is measured off the live
/// nodes and the live bus graph.
///
/// <b>What no headless run can say.</b> Nothing here hears a splash or sees one. Whether the
/// day burst reads comic and the night burst reads curt is judged from the locked-camera capture
/// sequences under <c>docs/superpowers/status/2026-08-08-lake-w4/</c>, and from nowhere else.
///
/// Follows <c>WaterSelfTest</c>'s shape exactly: a <c>Node3D</c> instanced from a <c>.tscn</c>,
/// one printed line per check, and <c>WATERFX-TEST OVERALL: PASS|FAIL</c> with exit 0 iff green.
/// Run: <c>Godot --headless --path . res://tests/scenes/WaterFxSelfTest.tscn</c>.
/// </summary>
public partial class WaterFxSelfTest : Node3D
{
    private readonly List<(string Name, bool Ok)> _results = new();
    private bool _finished;
    private WaterFx _fx = null!;

    private static readonly WaterEventKind[] AllKinds =
    {
        WaterEventKind.Entered, WaterEventKind.Exited, WaterEventKind.Splash,
        WaterEventKind.WentUnder, WaterEventKind.Sputtered,
    };

    public override void _Ready()
    {
        GetTree().CreateTimer(60.0).Timeout += () =>
        {
            if (_finished)
                return;
            Check("watchdog_no_hang", false, "the run never reached Finish");
            Finish();
        };

        // A camera, because every distance cull in this repo resolves the local viewpoint through
        // GetViewport().GetCamera3D() and a test without one would exercise the null branch only.
        AddChild(new Camera3D { Name = "Camera", Current = true, Position = Vector3.Zero });

        _fx = new WaterFx { Name = "WaterFx" };
        AddChild(_fx);

        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        await Frame();

        ClipsAreRealAudioAndNeverLoop();
        ClipsAreCachedAndIdenticalPerKind();
        FilterLandsOnSfxAndAmbientAndNotOnVoice();
        FilterEnsureIsIdempotentUnderRepeatedCalls();
        FilterEngagesOnlyWhileSubmergedAndOpensAtTheSwitch();
        FilterReleaseHandsTheBusesBack();

        await AllFiveKindsReachThePool();
        await PoolNeverAllocatesMoreThanSixEmitters();
        await PoolPinsAmountAndScalesThroughAmountRatio();
        await DistanceCullRefusesBeyondThirtyFiveMetres();
        await NightSubtractionReachesTheLiveEmitter();
        await NightEmissionCapReachesTheLiveMaterial();
        await FirstSplashOfTheNightIsNotDayBright();
        await VoiceBudgetCapsWaterWithoutStealing();
        ChillChatterIsNonPositionalAndCostsNo3DVoice();

        Finish();
    }

    // --- The clips -------------------------------------------------------------------------------

    /// <summary>
    /// The one thing a headless run can say about a synthesized sound: that there is one. A recipe
    /// that silently renders zeros — an envelope that never opens, a filter coefficient that eats
    /// the signal, a clamp in the wrong place — produces a clip that is correctly cached,
    /// correctly routed, correctly pitched and completely inaudible, and every other check in this
    /// file would pass. Peak and mean together separate the three failure modes: all-zero (both
    /// near zero), a lone spike (peak high, mean near zero), and rails (both maxed).
    ///
    /// The loop check is not decoration. <c>SfxLab.Rent</c> selects on <c>!Playing</c>, so a
    /// looping clip would hold its pool slot open forever and permanently shrink the shared 14 —
    /// the exact failure <c>SparseSfxEmitter</c>'s class doc warns about.
    /// </summary>
    private void ClipsAreRealAudioAndNeverLoop()
    {
        foreach (WaterEventKind kind in AllKinds)
        {
            AudioStreamWav wav = WaterSfx.For(kind);
            byte[] data = wav.Data;
            int count = data.Length / 2;

            long sum = 0;
            int peak = 0;
            for (int i = 0; i < count; i++)
            {
                int s = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
                int mag = s < 0 ? -s : s;
                sum += mag;
                if (mag > peak)
                    peak = mag;
            }
            double mean = count > 0 ? sum / (double)count : 0;
            GD.Print($"[waterfx-selftest] {kind}: {count} samples, peak {peak}/32767, mean {mean:F1}");

            Check($"clip_{kind}_has_length", count > 48000 / 8,
                $"only {count} samples — under an eighth of a second is not a splash");
            Check($"clip_{kind}_is_not_silence", peak > 300, $"peak {peak} of 32767 is silence");
            Check($"clip_{kind}_is_not_a_lone_click", mean > 20,
                $"mean {mean:F1} — a couple of clicks in a silent buffer, not a sound");
            Check($"clip_{kind}_is_not_clipping", peak < 32760, $"peak {peak} is on the rails");
            Check($"clip_{kind}_never_loops",
                wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled,
                $"LoopMode is {wav.LoopMode} — a looping one-shot never returns its pool slot");
            Check($"clip_{kind}_is_48k", wav.MixRate == 48000,
                $"mix rate {wav.MixRate} does not match the pinned 48 kHz");
        }
    }

    /// <summary>One set, cached. Reference identity across repeated calls proves both that the
    /// cache works (five clips are synthesized once per process, not once per splash) and that
    /// nothing in the runtime path can hand back a different clip for the same event — which is
    /// direction §5's "no night variant" observed from the outside.</summary>
    private void ClipsAreCachedAndIdenticalPerKind()
    {
        foreach (WaterEventKind kind in AllKinds)
        {
            AudioStreamWav a = WaterSfx.For(kind);
            AudioStreamWav b = WaterSfx.For(kind);
            Check($"clip_{kind}_is_cached", ReferenceEquals(a, b),
                "two calls returned different stream objects — the clip is being re-synthesized");
        }
        // Positive control: the identity test CAN distinguish two clips, so "identical" above is a
        // measurement rather than an artefact of comparing everything to itself.
        Check("clip_identity_can_tell_two_clips_apart",
            !ReferenceEquals(WaterSfx.For(WaterEventKind.Entered),
                WaterSfx.For(WaterEventKind.WentUnder)),
            "two different events returned the same clip object");
    }

    // --- The filter ---------------------------------------------------------------------------

    private static bool HasLowpass(string bus)
    {
        int idx = AudioServer.GetBusIndex(bus);
        if (idx < 0)
            return false;
        for (int slot = 0; slot < AudioServer.GetBusEffectCount(idx); slot++)
            if (AudioServer.GetBusEffect(idx, slot) is AudioEffectLowPassFilter)
                return true;
        return false;
    }

    /// <summary>
    /// Where the filter is, and — as much to the point — where it is not.
    ///
    /// Voice is deliberately untouched: muffling teammates while you are under would be a version
    /// of the §5.5 teammate voice-cut device that direction §4.4 puts out of this packet's reach,
    /// and it would degrade the channel the fairness contract leans on hardest. That is an
    /// "absence" claim, so it carries a positive control — the same finder must be shown returning
    /// true on a bus where the filter IS present, or "no filter on Voice" could just mean the
    /// finder never works.
    /// </summary>
    private void FilterLandsOnSfxAndAmbientAndNotOnVoice()
    {
        SubmersionFilter.Ensure();

        Check("filter_on_sfx", HasLowpass(AudioBuses.Sfx),
            "no lowpass on Sfx — gameplay one-shots would not muffle");
        Check("filter_on_ambient_group", HasLowpass(AudioBuses.Ambient),
            "no lowpass on the Ambient group handle — the bed and the scenery would not muffle");

        // Positive control for the two absence claims below.
        Check("filter_finder_positive_control", HasLowpass(AudioBuses.Sfx),
            "the finder cannot see a filter that is definitely there, so its negatives mean nothing");

        Check("no_filter_on_voice", !HasLowpass(AudioBuses.Voice),
            "a lowpass landed on Voice — proximity voice is out of this packet's bounds");
        Check("no_filter_on_bed_lane", !HasLowpass(AudioBuses.Bed),
            "a lowpass landed on the bed lane; the group handle already covers it and a second " +
            "one would double the muffle on the bed relative to the scenery");

        // The bed's duck must still be exactly where AudioBuses put it and nowhere else.
        int bed = AudioServer.GetBusIndex(AudioBuses.Bed);
        Check("duck_untouched", bed >= 0 && AudioServer.GetBusEffectCount(bed) == 1
                                         && AudioServer.GetBusEffect(bed, 0) is AudioEffectCompressor,
            "the bed lane's compressor was disturbed");
    }

    /// <summary>The hazard is specific and silent: a builder that guards by name but adds its
    /// effect unconditionally stacks a second filter on the second call, a third on the third, and
    /// nothing anywhere reports it — the mix just collapses. Same check <c>AudioBuses</c>' own
    /// self-test runs, for the same reason.</summary>
    private void FilterEnsureIsIdempotentUnderRepeatedCalls()
    {
        int sfx = AudioServer.GetBusIndex(AudioBuses.Sfx);
        int ambient = AudioServer.GetBusIndex(AudioBuses.Ambient);
        int busesBefore = AudioServer.BusCount;
        int sfxBefore = sfx >= 0 ? AudioServer.GetBusEffectCount(sfx) : -1;
        int ambientBefore = ambient >= 0 ? AudioServer.GetBusEffectCount(ambient) : -1;

        for (int i = 0; i < 4; i++)
            SubmersionFilter.Ensure();

        Check("filter_ensure_adds_no_buses", AudioServer.BusCount == busesBefore,
            $"bus count moved {busesBefore} -> {AudioServer.BusCount}");
        Check("filter_ensure_stacks_nothing_on_sfx",
            sfx < 0 || AudioServer.GetBusEffectCount(sfx) == sfxBefore,
            $"Sfx effects {sfxBefore} -> {AudioServer.GetBusEffectCount(sfx)}");
        Check("filter_ensure_stacks_nothing_on_ambient",
            ambient < 0 || AudioServer.GetBusEffectCount(ambient) == ambientBefore,
            $"Ambient effects {ambientBefore} -> {AudioServer.GetBusEffectCount(ambient)}");
    }

    /// <summary>
    /// The switch happens at the open end in both directions, which is what makes it inaudible —
    /// flicking a still-shut filter off is the click direction §10.2's "sweep, never snap"
    /// forbids. Also proves the filter is genuinely off when nobody is under, so the lake costs no
    /// DSP for the overwhelming majority of a session.
    /// </summary>
    private void FilterEngagesOnlyWhileSubmergedAndOpensAtTheSwitch()
    {
        SubmersionFilter.Apply(0f);
        Check("filter_disengaged_when_dry", !SubmersionFilter.Engaged,
            "the filter is switched on with nobody underwater");

        SubmersionFilter.Apply(0.5f);
        Check("filter_engages_when_submerged", SubmersionFilter.Engaged,
            "the filter did not switch on at half submersion");
        float mid = CurrentCutoff(AudioBuses.Sfx);
        Check("filter_midsweep_is_between_the_ends",
            mid < WaterFxTuning.FilterOpenHz - 1f && mid > WaterFxTuning.FilterSubmergedHz + 1f,
            $"mid-sweep cutoff {mid} Hz is not strictly between " +
            $"{WaterFxTuning.FilterSubmergedHz} and {WaterFxTuning.FilterOpenHz}");

        SubmersionFilter.Apply(1f);
        Check("filter_bottoms_out_muffled_not_silent",
            Mathf.Abs(CurrentCutoff(AudioBuses.Sfx) - WaterFxTuning.FilterSubmergedHz) < 1f,
            "full submersion did not reach the stated cutoff");

        SubmersionFilter.Apply(0f);
        Check("filter_disengages_again", !SubmersionFilter.Engaged, "the filter stayed on");
        Check("filter_switched_off_at_the_open_end",
            Mathf.Abs(CurrentCutoff(AudioBuses.Sfx) - WaterFxTuning.FilterOpenHz) < 1f,
            $"the filter was switched off at {CurrentCutoff(AudioBuses.Sfx)} Hz rather than wide " +
            "open — that transition is an audible click");
    }

    /// <summary>A session torn down mid-go-under must not leave the whole game — menus
    /// included — permanently muffled with nothing on screen to explain it.</summary>
    private void FilterReleaseHandsTheBusesBack()
    {
        SubmersionFilter.Apply(1f);
        Check("release_precondition_engaged", SubmersionFilter.Engaged,
            "test setup: the filter should be engaged before Release is exercised");

        SubmersionFilter.Release();

        Check("release_disengages", !SubmersionFilter.Engaged, "Release left the filter on");
        Check("release_opens_sfx",
            Mathf.Abs(CurrentCutoff(AudioBuses.Sfx) - WaterFxTuning.FilterOpenHz) < 1f,
            "Release left Sfx filtered");
        Check("release_opens_ambient",
            Mathf.Abs(CurrentCutoff(AudioBuses.Ambient) - WaterFxTuning.FilterOpenHz) < 1f,
            "Release left Ambient filtered");
        Check("release_zeroes_submersion", SubmersionFilter.Submersion == 0f,
            $"Release left submersion at {SubmersionFilter.Submersion}");
    }

    private static float CurrentCutoff(string bus)
    {
        int idx = AudioServer.GetBusIndex(bus);
        if (idx < 0)
            return float.NaN;
        for (int slot = 0; slot < AudioServer.GetBusEffectCount(idx); slot++)
            if (AudioServer.GetBusEffect(idx, slot) is AudioEffectLowPassFilter lp)
                return lp.CutoffHz;
        return float.NaN;
    }

    // --- The pool ---------------------------------------------------------------------------------

    private List<GpuParticles3D> PooledEmitters()
    {
        var found = new List<GpuParticles3D>();
        Node anchor = GetTree().CurrentScene ?? GetTree().Root;
        foreach (Node child in anchor.GetChildren())
            if (child is GpuParticles3D p && p.Name.ToString().StartsWith("SplashPool"))
                found.Add(p);
        return found;
    }

    private async Task ResetPool()
    {
        SplashParticlePool.Reset();
        await Frame();
        await Frame(); // QueueFree lands at the end of a frame, so the count is stale until then
    }

    private async Task AllFiveKindsReachThePool()
    {
        await ResetPool();
        int before = _fx.BurstsEmitted;
        foreach (WaterEventKind kind in AllKinds)
        {
            _fx.Dispatch(kind, new WaterEvent(1, new Vector3(2f, 0f, 0f),
                WaterState.Dry, WaterState.Wading, 3f));
        }
        Check("all_five_kinds_emit", _fx.BurstsEmitted - before == AllKinds.Length,
            $"{_fx.BurstsEmitted - before} of {AllKinds.Length} kinds produced a burst — one is " +
            "falling through the dispatcher silently");
    }

    /// <summary>
    /// Spec §9.1's "allocate the pool once, never instantiate per splash", measured as node count.
    /// Thirty bursts fired back to back with no frames in between, so nothing has had a chance to
    /// free: a per-event implementation would leave thirty nodes behind.
    /// </summary>
    private async Task PoolNeverAllocatesMoreThanSixEmitters()
    {
        await ResetPool();
        Check("pool_starts_empty", PooledEmitters().Count == 0,
            $"{PooledEmitters().Count} emitters survived the reset");

        for (int i = 0; i < WaterFxTuning.MaxConcurrentBursts * 5; i++)
        {
            _fx.Dispatch(WaterEventKind.Splash, new WaterEvent(1, new Vector3(1f, 0f, 0f),
                WaterState.Wading, WaterState.Wading, 2f));
        }
        int nodes = PooledEmitters().Count;
        GD.Print($"[waterfx-selftest] 30 bursts -> {nodes} emitter nodes");

        Check("pool_allocated_something", nodes > 0,
            "no emitters at all — the pool is not reachable from the dispatcher");
        Check("pool_capped_at_six", nodes <= WaterFxTuning.MaxConcurrentBursts,
            $"{nodes} emitters for 30 bursts — the pool is allocating per event");
        Check("pool_concurrency_capped", SplashParticlePool.LiveCount <= WaterFxTuning.MaxConcurrentBursts,
            $"{SplashParticlePool.LiveCount} concurrent bursts exceeds the stated ceiling");
    }

    /// <summary>
    /// The <c>Amount</c> trap. Writing it reallocates the particle buffer and restarts the
    /// system, so a pool that scaled bursts that way would look like a pool and behave like a
    /// per-event allocation. Amount must stay pinned at the ceiling on every slot forever, and
    /// <c>AmountRatio</c> must be the thing that moved — asserted with a positive control, because
    /// "Amount never changed" would also be true of a pool that never scaled anything at all.
    /// </summary>
    private async Task PoolPinsAmountAndScalesThroughAmountRatio()
    {
        await ResetPool();

        // A tiny burst and a big one, deliberately far apart in count.
        _fx.TestSetNightWeight(1f);
        _fx.Dispatch(WaterEventKind.Exited, new WaterEvent(1, new Vector3(1f, 0f, 0f),
            WaterState.Wading, WaterState.Dry, 0f));
        _fx.TestSetNightWeight(0f);
        _fx.Dispatch(WaterEventKind.Entered, new WaterEvent(1, new Vector3(1f, 0f, 0f),
            WaterState.Dry, WaterState.Swimming, 12f));

        List<GpuParticles3D> emitters = PooledEmitters();
        Check("amount_pin_precondition", emitters.Count >= 2,
            $"expected at least two emitters for the comparison, got {emitters.Count}");

        bool allPinned = true;
        float minRatio = 2f, maxRatio = -1f;
        foreach (GpuParticles3D p in emitters)
        {
            if (p.Amount != WaterFxTuning.MaxParticlesPerBurst)
                allPinned = false;
            minRatio = Mathf.Min(minRatio, p.AmountRatio);
            maxRatio = Mathf.Max(maxRatio, p.AmountRatio);
        }
        GD.Print($"[waterfx-selftest] amount pinned={allPinned}, ratio span {minRatio:F3}..{maxRatio:F3}");

        Check("amount_stays_pinned_at_the_ceiling", allPinned,
            $"an emitter's Amount is not {WaterFxTuning.MaxParticlesPerBurst} — writing it " +
            "reallocates the buffer and restarts the system on every burst");
        // Positive control: the scaling really happened, so the check above is not passing merely
        // because nothing ever scales.
        Check("amount_ratio_is_what_scales", maxRatio - minRatio > 0.1f,
            $"AmountRatio span {minRatio:F3}..{maxRatio:F3} — a tiny burst and a huge one came " +
            "out the same size, so per-burst scaling is not wired");
    }

    // --- Culling, night, and the voice budget ---------------------------------------------------

    private async Task DistanceCullRefusesBeyondThirtyFiveMetres()
    {
        await ResetPool();

        // Positive control FIRST: prove the harness can observe a burst being emitted at all.
        int emitBefore = _fx.BurstsEmitted;
        int cullBefore = _fx.BurstsCulled;
        _fx.Dispatch(WaterEventKind.Entered, new WaterEvent(1, new Vector3(5f, 0f, 0f),
            WaterState.Dry, WaterState.Wading, 3f));
        Check("cull_control_near_splash_emits", _fx.BurstsEmitted == emitBefore + 1,
            "a splash 5 m from the camera did not emit, so the cull test below proves nothing");
        Check("cull_control_near_splash_not_culled", _fx.BurstsCulled == cullBefore,
            "a splash 5 m from the camera was culled");

        emitBefore = _fx.BurstsEmitted;
        cullBefore = _fx.BurstsCulled;
        _fx.Dispatch(WaterEventKind.Entered, new WaterEvent(1, new Vector3(100f, 0f, 0f),
            WaterState.Dry, WaterState.Wading, 3f));
        Check("cull_far_splash_is_not_emitted", _fx.BurstsEmitted == emitBefore,
            "a splash 100 m from the camera still emitted particles");
        Check("cull_far_splash_is_counted", _fx.BurstsCulled == cullBefore + 1,
            "the far splash was neither emitted nor counted as culled — it vanished");
    }

    /// <summary>
    /// Direction §10.3, checked in the live path rather than in the pure function. The pure test
    /// proves the arithmetic; this proves the number actually arrives at the emitter. A night
    /// weight computed every frame and then dropped on the floor is exactly the "built and wired
    /// to nothing" failure this repo keeps shipping.
    /// </summary>
    private async Task NightSubtractionReachesTheLiveEmitter()
    {
        await ResetPool();

        _fx.TestSetNightWeight(0f);
        _fx.Dispatch(WaterEventKind.Entered, new WaterEvent(1, new Vector3(1f, 0f, 0f),
            WaterState.Dry, WaterState.Swimming, 6f));
        _fx.TestSetNightWeight(1f);
        _fx.Dispatch(WaterEventKind.Entered, new WaterEvent(1, new Vector3(1f, 0f, 0f),
            WaterState.Dry, WaterState.Swimming, 6f));

        List<GpuParticles3D> e = PooledEmitters();
        if (e.Count < 2)
        {
            Check("night_reaches_emitter_precondition", false,
                $"expected two emitters, got {e.Count}");
            return;
        }
        GpuParticles3D day = e[0], night = e[1];
        GD.Print($"[waterfx-selftest] day ratio {day.AmountRatio:F3} life {day.Lifetime:F3} | " +
                 $"night ratio {night.AmountRatio:F3} life {night.Lifetime:F3}");

        Check("night_cuts_count_at_the_emitter", night.AmountRatio < day.AmountRatio - 0.05f,
            $"night AmountRatio {night.AmountRatio:F3} is not below day's {day.AmountRatio:F3}");
        Check("night_cuts_lifetime_at_the_emitter", night.Lifetime < day.Lifetime - 0.05f,
            $"night lifetime {night.Lifetime:F3} is not below day's {day.Lifetime:F3}");

        // And the rule direction §10.3 named as the trap: SIZE is silhouette and must not move.
        var dayProc = day.ProcessMaterial as ParticleProcessMaterial;
        var nightProc = night.ProcessMaterial as ParticleProcessMaterial;
        Check("night_does_not_shrink_the_droplet",
            dayProc != null && nightProc != null
                            && Mathf.Abs(dayProc.ScaleMax - nightProc.ScaleMax) < 1e-4f,
            $"droplet size moved between day ({dayProc?.ScaleMax}) and night ({nightProc?.ScaleMax}) " +
            "— size is silhouette, count and lifetime are duration");
    }

    /// <summary>The emission cap is the one number standing between a night splash and a flare
    /// that resolves the dark W3 spent its whole shader budget making unresolvable. Read straight
    /// off the shared draw material.</summary>
    private async Task NightEmissionCapReachesTheLiveMaterial()
    {
        await ResetPool();
        _fx.Dispatch(WaterEventKind.Splash, new WaterEvent(1, Vector3.Zero,
            WaterState.Wading, WaterState.Wading, 2f));

        List<GpuParticles3D> e = PooledEmitters();
        StandardMaterial3D? mat = e.Count > 0 && e[0].DrawPass1 is QuadMesh q
            ? q.Material as StandardMaterial3D
            : null;
        if (mat == null)
        {
            Check("emission_cap_precondition", false, "could not reach the droplet draw material");
            return;
        }

        SplashParticlePool.SetNightLevel(0f);
        float dayEnergy = mat.EmissionEnergyMultiplier;
        float dayValue = mat.AlbedoColor.R;
        float dayAlpha = mat.AlbedoColor.A;
        SplashParticlePool.SetNightLevel(1f);
        float nightEnergy = mat.EmissionEnergyMultiplier;
        float nightValue = mat.AlbedoColor.R;
        float nightAlpha = mat.AlbedoColor.A;
        GD.Print($"[waterfx-selftest] emission {dayEnergy:F3} -> {nightEnergy:F3}, " +
                 $"albedo {dayValue:F3}a{dayAlpha:F2} -> {nightValue:F3}a{nightAlpha:F2}");

        Check("emission_day_reaches_the_material",
            Mathf.Abs(dayEnergy - WaterFxTuning.DayEmissionEnergy) < 1e-3f,
            $"day emission at the material is {dayEnergy}, expected {WaterFxTuning.DayEmissionEnergy}");
        Check("emission_night_is_capped_at_the_material",
            Mathf.Abs(nightEnergy - WaterFxTuning.NightEmissionEnergy) < 1e-3f,
            $"night emission at the material is {nightEnergy}, expected {WaterFxTuning.NightEmissionEnergy}");
        Check("emission_night_is_far_below_day", nightEnergy < dayEnergy * 0.3f,
            "the night cap is not a cap");

        // The half that nearly got away. The material is Unshaded, so nothing in the scene lights
        // it and the albedo IS the brightness — a cap that reaches only the emission caps almost
        // nothing, which is how the first headed night capture came back with a white flare on a
        // perfectly green emission number.
        Check("albedo_is_also_capped_at_night", nightValue < dayValue * 0.5f,
            $"night albedo value {nightValue:F3} against day's {dayValue:F3} — on an unshaded " +
            "material this is the number that decides how bright the splash actually renders");
        Check("albedo_alpha_thins_at_night", nightAlpha < dayAlpha,
            $"night alpha {nightAlpha:F2} is not below day's {dayAlpha:F2}");
    }

    /// <summary>
    /// The ordering a real session actually has, which is the opposite of the capture lab's.
    ///
    /// <c>WaterFx._Process</c> pushes the night level every frame from the moment the session
    /// starts — long before anybody gets in the water. So by the time the first splash allocates
    /// the shared draw material, the level has ALREADY been "set". A pool that tracked one number
    /// for both "asked for" and "written" would short-circuit that write, leave the material at
    /// the day values it was constructed with, and — because the night weight is exactly 1.0 and
    /// perfectly flat through the night band — never write it again. The first splash of the
    /// night, and every one after it, at full day brightness.
    ///
    /// The capture lab could not have caught this: it fires a warm-up burst inside its
    /// synchronous <c>_Ready</c> prologue, so the material exists before the first <c>_Process</c>
    /// and gets written correctly. This check reproduces the player's order instead.
    /// </summary>
    private async Task FirstSplashOfTheNightIsNotDayBright()
    {
        await ResetPool();

        // Night is asked for BEFORE any material exists — the live order.
        SplashParticlePool.SetNightLevel(1f);
        _fx.TestSetNightWeight(1f);
        _fx.Dispatch(WaterEventKind.Entered, new WaterEvent(1, Vector3.Zero,
            WaterState.Dry, WaterState.Swimming, 6f));

        List<GpuParticles3D> e = PooledEmitters();
        StandardMaterial3D? mat = e.Count > 0 && e[0].DrawPass1 is QuadMesh q
            ? q.Material as StandardMaterial3D
            : null;
        if (mat == null)
        {
            Check("first_night_splash_precondition", false, "could not reach the draw material");
            return;
        }
        GD.Print($"[waterfx-selftest] first-splash-of-the-night albedo {mat.AlbedoColor.R:F3}, " +
                 $"emission {mat.EmissionEnergyMultiplier:F3}");

        Check("first_splash_of_the_night_is_night_dark",
            Mathf.Abs(mat.AlbedoColor.R - WaterFxTuning.NightAlbedoValue) < 1e-3f,
            $"the first splash after a fresh material allocation rendered at albedo " +
            $"{mat.AlbedoColor.R:F3}, not night's {WaterFxTuning.NightAlbedoValue} — the night " +
            "level was requested before the material existed and the write was suppressed");
        Check("first_splash_of_the_night_is_night_emission",
            Mathf.Abs(mat.EmissionEnergyMultiplier - WaterFxTuning.NightEmissionEnergy) < 1e-3f,
            $"emission is {mat.EmissionEnergyMultiplier:F3}, not night's {WaterFxTuning.NightEmissionEnergy}");
    }

    /// <summary>
    /// The starvation answer, measured. Twelve splashes in one instant must produce at most four
    /// water voices and must refuse the rest outright rather than stealing — because what a fifth
    /// simultaneous splash would steal from <c>SfxLab</c>'s shared 14 is a footstep or a shutter
    /// click somebody was listening to, and what refusing loses is one splash out of twelve that
    /// were all happening at once.
    /// </summary>
    private async Task VoiceBudgetCapsWaterWithoutStealing()
    {
        // Wait out every voice the checks above took. The governor holds a slot for the clip's own
        // length and the longest clip is the 1.15 s swallow, so without this the four slots are
        // still busy when the burst below arrives and all twelve are denied — which would pass
        // the cap check for entirely the wrong reason. Found by running it.
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        Check("voice_budget_starts_from_an_empty_governor", _fx.WaterVoicesLive == 0,
            $"{_fx.WaterVoicesLive} water voices were still sounding when the burst test began");

        int playedBefore = _fx.VoicesPlayed;
        int deniedBefore = _fx.VoicesDeniedByBudget;

        for (int i = 0; i < 12; i++)
        {
            _fx.Dispatch(WaterEventKind.Splash, new WaterEvent(1, new Vector3(1f, 0f, 0f),
                WaterState.Wading, WaterState.Wading, 2f));
        }
        int played = _fx.VoicesPlayed - playedBefore;
        int denied = _fx.VoicesDeniedByBudget - deniedBefore;
        GD.Print($"[waterfx-selftest] 12 simultaneous splashes -> {played} played, {denied} denied");

        Check("voice_budget_played_something", played > 0,
            "no water voice played at all, so the cap below proves nothing");
        Check("voice_budget_caps_at_four", played <= WaterFxTuning.MaxConcurrentWaterVoices,
            $"{played} simultaneous water voices exceeds the self-imposed cap of " +
            $"{WaterFxTuning.MaxConcurrentWaterVoices}, so water can crowd the shared pool");
        Check("voice_budget_refuses_the_rest", denied == 12 - played,
            $"{played} played and {denied} denied of 12 — some events neither sounded nor were " +
            "accounted for");
        Check("voice_budget_live_count_matches", _fx.WaterVoicesLive <= WaterFxTuning.MaxConcurrentWaterVoices,
            $"{_fx.WaterVoicesLive} live water voices exceeds the cap");
    }

    /// <summary>
    /// The cold's third channel, and the load-bearing claim about it: that it is
    /// <see cref="AudioStreamPlayer"/> and not <see cref="AudioStreamPlayer3D"/>.
    ///
    /// That type is the whole budget answer. Direction §10.1 flagged 5 free 3D voices against 5
    /// possible remote chatter voices in a six-player lobby; a non-positional owner-only channel
    /// spends none of them and never rents an <c>SfxLab</c> slot either. If a later edit made this
    /// positional "for the asymmetry", the collision would be back and nothing else in this file
    /// would notice — so the type itself gets a check.
    /// </summary>
    private void ChillChatterIsNonPositionalAndCostsNo3DVoice()
    {
        ChillChatter? chatter = _fx.Chatter;
        Check("chatter_exists", chatter != null,
            "WaterFx did not build the cold's audio channel");
        if (chatter == null)
            return;

        // Measured, not asserted from the declaration: count the positional players this listener
        // owns anywhere in its subtree. Zero is the budget claim — direction §10.1's five free 3D
        // voices are still five after this packet. (The C# compiler already refuses to even
        // COMPILE `chatter.Player is AudioStreamPlayer3D`, because the two types are siblings
        // under Node rather than parent and child, which is a stronger guarantee than this check
        // and the reason this one had to be written as a subtree count instead.)
        int positional = CountPositionalPlayers(_fx);
        Check("chatter_costs_no_3d_voice", positional == 0,
            $"WaterFx owns {positional} AudioStreamPlayer3D nodes — each one spends a voice from " +
            "the five direction §10.1 reserved for proximity speech");

        // Positive control: the counter CAN see a 3D player, so the zero above is a measurement.
        var probe = new AudioStreamPlayer3D { Name = "SelfTestProbe" };
        _fx.AddChild(probe);
        Check("positional_counter_positive_control", CountPositionalPlayers(_fx) == 1,
            "the 3D-player counter cannot see a 3D player that is definitely there");
        _fx.RemoveChild(probe);
        probe.QueueFree();
        Check("chatter_routes_to_sfx", chatter.Player.Bus == AudioBuses.Sfx,
            $"chatter is on '{chatter.Player.Bus}' — your own body reacting is gameplay feedback, " +
            "not scenery, and the scenery lane hides it behind the wrong slider");

        // Silent with nobody cold. The cue is a build; a shiver with zero chill is a broken clock.
        Check("chatter_silent_when_warm", chatter.ShotsFired == 0,
            $"{chatter.ShotsFired} shots fired with no chill anywhere");

        foreach ((string name, AudioStreamWav wav) in new[]
                 { ("shiver", ChillChatter.Shiver()), ("teeth", ChillChatter.Teeth()) })
        {
            byte[] data = wav.Data;
            int count = data.Length / 2;
            long sum = 0;
            int peak = 0;
            for (int i = 0; i < count; i++)
            {
                int s = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
                int mag = s < 0 ? -s : s;
                sum += mag;
                if (mag > peak)
                    peak = mag;
            }
            double mean = count > 0 ? sum / (double)count : 0;
            GD.Print($"[waterfx-selftest] chatter/{name}: {count} samples, peak {peak}, mean {mean:F1}");
            Check($"chatter_{name}_is_not_silence", peak > 300, $"peak {peak} of 32767 is silence");
            Check($"chatter_{name}_is_not_a_lone_click", mean > 15, $"mean {mean:F1} is a click");
            Check($"chatter_{name}_is_not_clipping", peak < 32760, $"peak {peak} is on the rails");
            Check($"chatter_{name}_never_loops",
                wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled, $"{name} loops");
        }

        // Quickening, and in step with the rumble it shares a contract with.
        float slow = ChillChatter.PeriodFor(0.05f, 0f);
        float fast = ChillChatter.PeriodFor(1f, 0f);
        Check("chatter_quickens_with_the_cold", fast < slow * 0.5f,
            $"period at full chill ({fast:F2}s) is not much shorter than at onset ({slow:F2}s)");
        Check("chatter_interval_is_irregular",
            Mathf.Abs(ChillChatter.PeriodFor(0.5f, 0f) - ChillChatter.PeriodFor(0.5f, 1.9f)) > 0.05f,
            "consecutive intervals at the same intensity are identical — a metronome, not a body");
        Check("chatter_gets_louder_with_the_cold",
            ChillChatter.VolumeDbFor(1f) > ChillChatter.VolumeDbFor(0f),
            "the chatter does not rise with the cue");
        Check("chatter_period_never_degenerates",
            ChillChatter.PeriodFor(float.NaN, float.NaN) > 0f
            && ChillChatter.PeriodFor(2f, 1e9f) > 0f,
            "a hostile intensity produced a non-positive period — that is a per-frame shot loop");
    }

    /// <summary>Every <see cref="AudioStreamPlayer3D"/> in a subtree — one of these is one of the
    /// ≤24 concurrent 3D voices the mix budget counts.</summary>
    private static int CountPositionalPlayers(Node root)
    {
        int n = root is AudioStreamPlayer3D ? 1 : 0;
        foreach (Node child in root.GetChildren())
            n += CountPositionalPlayers(child);
        return n;
    }

    // --- Plumbing ----------------------------------------------------------------------------------

    private async Task Frame() =>
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(string name, bool ok, string why = "")
    {
        _results.Add((name, ok));
        if (ok)
            GD.Print($"[waterfx-selftest] ok   {name}");
        else
            GD.PrintErr($"[waterfx-selftest] FAIL {name}{(why.Length > 0 ? $": {why}" : "")}");
    }

    private void Finish()
    {
        if (_finished)
            return;
        _finished = true;

        int failed = 0;
        foreach ((string _, bool ok) in _results)
            if (!ok)
                failed++;

        GD.Print(failed == 0
            ? $"WATERFX-TEST OVERALL: PASS ({_results.Count} checks)"
            : $"WATERFX-TEST OVERALL: FAIL ({failed} of {_results.Count} checks)");
        GetTree().Quit(failed == 0 ? 0 : 1);
    }
}
