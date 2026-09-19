using System;
using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.World;

/// <summary>
/// The continuous day/night ambient bed THRILL-BIBLE.md §6.3 needs before "wrong silence"
/// (withdrawing a sound the player stopped noticing) can be spent at all — see the
/// 2026-07-26 horror-register decision (§9) that promoted this from nice-to-have to critical
/// path. This node BUILDS the bed and the withdrawal switch; it does not decide when the
/// switch flips — that is a future directed beat (`/direct`), not this file.
///
/// Reads <see cref="CycleDriver.Instance"/>.Phase every frame — the same read-only access
/// pattern OutdoorAtmosphere/NightDome use against the one shipped clock. Holds at the
/// zero-initialized silent default until <see cref="CycleDriver.Synced"/> is true, exactly as
/// hard (and no harder) than everything else built against this clock.
///
/// Two continuously-looping synthesized layers — day (soft, brighter wind + sparse high
/// texture) and night (thinner, darker wind + sparse low texture, quieter overall per §6.1's
/// prospect/refuge: night reads as LESS, not just different) — crossfaded by
/// <see cref="AmbientBedMath.NightWeight"/>, plus a sparse POSITIONAL event layer (occasional
/// birdsong / distant owl-or-cricket swell) fired through <see cref="SfxLab.PlayStream3D"/>'s
/// existing pool (no second pool — SfxLab already owns the shared budget, see its own doc).
/// The two loop layers route through <see cref="AudioBuses.Bed"/> and the event layer through
/// <see cref="AudioBuses.Scenery"/>; both sit under the <c>Ambient</c> group, which is where the
/// player's slider and any future whole-world trim act.
///
/// <b>What this costs the voice budget: nothing.</b> The written ceiling is ≤24 concurrent 3D
/// voices and it is already 19-deep (5 <c>VoiceSpeaker</c>s at a full six-player lobby + SfxLab's
/// 14-slot pool cap). Both loop layers here are plain <c>AudioStreamPlayer</c>s — non-positional,
/// outside that ceiling entirely — so the count after this node lands is still 19. That is not an
/// accident of implementation, it is the reason a continuous bed was allowed to exist at all;
/// see <see cref="SparseSfxEmitter"/>'s own reconciliation note. The design price is real and
/// belongs on the record: a non-positional bed sounds identical wherever a player stands, so it
/// carries none of THRILL-BIBLE §5.4's per-player sensory asymmetry. The positional layer that
/// does carry it is <see cref="SparseSfxEmitter"/>'s, and in the camp that layer is the one
/// running (see <see cref="EventLayerEnabled"/>).
///
/// <see cref="Withdraw"/>/<see cref="Restore"/> apply one master gain on top of the day/night
/// mix, never baked into it — so restoring after a withdrawal started mid-dusk lands back at
/// dusk's own mix gain, not day's or a hardcoded default. That separation (mix gain from
/// phase, master gain from withdrawal, multiplied together every frame) is what makes
/// <see cref="AmbientBedMath"/>'s functions testable in isolation — see
/// tests/Run-AmbientBedTest.ps1 and <see cref="AmbientBedSelfTest"/>.
/// </summary>
public partial class AmbientBed : Node
{
    /// <summary>The bed's own lane. Never SfxLab's "Sfx" bus — ambience and gameplay one-shots
    /// have to be mixable independently, which was this packet's original point and is now
    /// <see cref="AudioBuses"/>'s job. WP-N2 created a flat "Ambient" bus here; the sound
    /// integration packet replaced that with the shared tree, in which <c>Ambient</c> is the
    /// group handle and this is its ducked child lane. Same destination, one owner instead of
    /// five private <c>EnsureBus</c> copies.</summary>
    public const string Bus = AudioBuses.Bed;

    private const int SampleRate = 48000; // matches SfxLab's pinned mix rate.

    // --- Value picks (project CLAUDE.md ambiguity rule: pick, comment, move on) ---------
    //
    // Day/night band edges, chosen against CycleSelfTest's own breakpoints (golden hour
    // starts at phase 0.5 and has swept to blue-violet dusk by 0.5833): a dusk crossfade
    // ending at 0.65 tracks the sky's own transition, and night gets a long stable stretch
    // (0.65-0.9) before dawn starts pulling the mix back toward day at the 1.0/0.0 wrap.
    private const float DayEndPhase = 0.5f;
    private const float NightStartPhase = 0.65f;
    private const float NightEndPhase = 0.9f;

    // Night reads as LESS (§6.1), not just different: its peak layer gain is scaled well
    // below day's even at full night weight, on top of the loop's own quieter synthesis.
    private const float NightGainScale = 0.55f;

    private const float LoopSeconds = 24f;     // long enough that sparse in-loop texture
                                                // doesn't read as a metronome on repeat.
    private const float LoopOverlapSeconds = 2f; // splice-crossfade width for a seamless loop.

    private AudioStreamPlayer _dayPlayer = null!;
    private AudioStreamPlayer _nightPlayer = null!;

    private float _masterGain = 1f;
    private float _fadeFrom = 1f;
    private float _fadeTo = 1f;
    private float _fadeElapsedSec;
    private float _fadeDurationSec = 0.0001f;

    private double _eventTimerSec;
    private readonly Random _eventRng = new(2026_07_26); // deterministic sparse-event cadence.

    private static AudioStreamWav? _dayLoopCache;
    private static AudioStreamWav? _nightLoopCache;
    private static AudioStreamWav? _dayEventCache;
    private static AudioStreamWav? _nightEventCache;

    /// <summary>Current normalized night weight (0 = full day, 1 = full night) — exposed for
    /// the dev-lab on-screen readout, not used internally beyond that (the node recomputes it
    /// fresh every frame from the clock, it never reads its own cached copy back).</summary>
    public float CurrentNightWeight { get; private set; }

    /// <summary>True once a withdrawal has been started and has not yet been fully restored —
    /// the dev-lab readout's "withdrawn/restored" state.</summary>
    public bool Withdrawn { get; private set; }

    /// <summary>Where sparse positional events are centered (the lab sets this to its camera
    /// or player position each frame; defaults to the origin, which is fine for a lab with a
    /// fixed camera near the world center).</summary>
    public Vector3 EventAnchor { get; set; } = Vector3.Zero;

    /// <summary>Whether the sparse positional event layer runs. True by default (the since-removed
    /// ambient lab relied on that), and FALSE in the original level — which was
    /// the substantive reconciliation between this node (PR #73, 2026-07-26) and the sparse
    /// emitters that shipped after it (PR #154).
    ///
    /// Both build the same layer. <see cref="SparseSfxEmitter"/> does it better *in a real
    /// world*: it places birds at the generator's own trailhead markers rather than scattering
    /// them ±12 m around a single anchor, it gives each emitter its own seed so no two fall into
    /// lockstep, and it does not need something to feed it a listener position every frame.
    /// This node's version was written for a lab that had no such layer and a camera that never
    /// moves; left on in the camp it would double the birdsong and hoot from wherever
    /// <see cref="EventAnchor"/> happened to be left — the world origin, by default, which is
    /// silent from anywhere past the event clips' own 60 m cutoff.
    ///
    /// What stays this node's alone is the CONTINUOUS pair, which is what the camp had none of
    /// and what §6.3 actually needs.</summary>
    [Export] public bool EventLayerEnabled { get; set; } = true;

    /// <summary>True on a dedicated/headless peer, where nothing is listening. Rendering the two
    /// 26-second 48 kHz loops costs several million samples of noise, filtering and trig plus
    /// ~5 MB of resident PCM, and a dedicated server would pay all of it at world load for an
    /// output device that does not exist.
    ///
    /// It skips the SYNTHESIS and the playback, not the wiring: the two player nodes are still
    /// built and still named, so a headless scene self-test can prove the bed's structure even
    /// though no headless run can ever hear it. That split is deliberate — a guard that removed
    /// the nodes entirely would take the only CI-reachable evidence with it.</summary>
    private bool _silentPeer;

    public override void _Ready()
    {
        _silentPeer = NetworkManager.Instance?.IsHeadless ?? false;
        AudioBuses.EnsureLayout();

        _dayPlayer = new AudioStreamPlayer
        {
            Name = "AmbientDayLoop",
            Bus = Bus,
            Stream = _silentPeer ? null : GetDayLoop(),
            VolumeDb = -80f, // silent until the first Synced frame — see class doc.
        };
        AddChild(_dayPlayer);

        _nightPlayer = new AudioStreamPlayer
        {
            Name = "AmbientNightLoop",
            Bus = Bus,
            Stream = _silentPeer ? null : GetNightLoop(),
            VolumeDb = -80f,
        };
        AddChild(_nightPlayer);

        if (_silentPeer)
            return; // Play() on a null stream is an error, and there is nothing to play it to.

        _dayPlayer.Play();
        _nightPlayer.Play();
        _eventTimerSec = _eventRng.NextDouble() * 4.0 + 2.0; // first event a few seconds in.
    }

    public override void _Process(double delta)
    {
        if (_silentPeer)
            return;
        AdvanceFade((float)delta);

        if (CycleDriver.Instance is not { Synced: true } driver)
            return; // hold at the silent default until synced — never harder-gated than this.

        float nightWeight = AmbientBedMath.NightWeight(driver.Phase);
        CurrentNightWeight = nightWeight;

        _dayPlayer.VolumeDb = GainToDb(AmbientBedMath.DayLayerGain(nightWeight, _masterGain));
        _nightPlayer.VolumeDb = GainToDb(AmbientBedMath.NightLayerGain(nightWeight, _masterGain));

        AdvanceEventTimer(delta, nightWeight);
    }

    /// <summary>Fades the whole bed — both loop layers and the event layer's trigger cadence —
    /// to true silence over <paramref name="fadeSeconds"/>. This is the whole point of the
    /// packet: §6.3 requires an established bed to withdraw FROM, and this is the mechanism a
    /// future directed beat spends. Nobody here decides when that beat fires.</summary>
    public void Withdraw(float fadeSeconds)
    {
        StartFade(0f, fadeSeconds);
        Withdrawn = true;
    }

    /// <summary>Restores the bed to full presence over <paramref name="fadeSeconds"/>. Because
    /// the day/night mix is recomputed fresh every frame from the live phase and only
    /// multiplied by this master gain (never baked into it), restoring lands back at whatever
    /// the CURRENT phase's mix gain is — dusk's gain if the phase is at dusk, not day's and
    /// not a hardcoded default.</summary>
    public void Restore(float fadeSeconds)
    {
        StartFade(1f, fadeSeconds);
        Withdrawn = false;
    }

    private void StartFade(float target, float fadeSeconds)
    {
        _fadeFrom = _masterGain;
        _fadeTo = target;
        _fadeDurationSec = Mathf.Max(fadeSeconds, 0.0001f); // guard divide-by-zero, MECHANICS-BIBLE §6.
        _fadeElapsedSec = 0f;
    }

    private void AdvanceFade(float delta)
    {
        if (_fadeElapsedSec >= _fadeDurationSec)
        {
            _masterGain = _fadeTo;
            return;
        }
        _fadeElapsedSec += delta;
        _masterGain = AmbientBedMath.FadeValue(_fadeFrom, _fadeTo, _fadeElapsedSec, _fadeDurationSec);
    }

    // --- Sparse positional event layer (Scope 2) ----------------------------------------

    private const double DayEventMinSec = 9.0;
    private const double DayEventMaxSec = 18.0;
    private const double NightEventMinSec = 14.0;
    private const double NightEventMaxSec = 26.0; // night events sparser still — less, per §6.1.

    private void AdvanceEventTimer(double delta, float nightWeight)
    {
        if (!EventLayerEnabled)
            return; // the world owns this layer instead — see EventLayerEnabled's own doc.
        if (_masterGain <= 0.001f)
            return; // wrong-silence withdrawal also silences new events, not just the loops.

        _eventTimerSec -= delta;
        if (_eventTimerSec > 0)
            return;

        bool isNight = nightWeight >= 0.5f;
        AudioStreamWav clip = isNight ? GetNightEvent() : GetDayEvent();
        Vector3 offset = new(
            (float)(_eventRng.NextDouble() * 2 - 1) * 12f,
            (float)(_eventRng.NextDouble()) * 4f,
            (float)(_eventRng.NextDouble() * 2 - 1) * 12f);
        float eventVolumeDb = isNight ? -14f : -10f; // night events read quieter too (§6.1).
        SfxLab.PlayStream3D(this, EventAnchor + offset, clip, eventVolumeDb, pitchJitter: 0.1f,
            maxDistance: 60f, bus: AudioBuses.Scenery);

        double min = isNight ? NightEventMinSec : DayEventMinSec;
        double max = isNight ? NightEventMaxSec : DayEventMaxSec;
        _eventTimerSec = min + _eventRng.NextDouble() * (max - min);
    }

    // --- Gain plumbing ----------------------------------------------------------------------
    //
    // Bus creation used to live here as a private EnsureBus copy; AudioBuses.EnsureLayout owns
    // it now. The layer gains stay per-PLAYER rather than moving to the bus, deliberately: the
    // Ambient bus volume is the player's slider and the group handle, and a system that wrote
    // its mix into the same number the settings panel writes into would fight it every frame.

    private static float GainToDb(float linearGain) =>
        linearGain <= 0.0001f ? -80f : Mathf.LinearToDb(linearGain);

    // --- Loop-layer synthesis (rendered once, cached, looped — never re-triggered) --------

    // internal, not private, for the same reason RenderSeamlessLoop is: AmbientBedSelfTest has to
    // render the real streams and check they are not silence. A headless run cannot hear the bed,
    // but it can prove the bed has a waveform — see LoopStreamsAreRealLoopingAudio.
    internal static AudioStreamWav GetDayLoop() => _dayLoopCache ??= RenderDayLoop();
    internal static AudioStreamWav GetNightLoop() => _nightLoopCache ??= RenderNightLoop();
    private static AudioStreamWav GetDayEvent() => _dayEventCache ??= RenderBirdsong();
    private static AudioStreamWav GetNightEvent() => _nightEventCache ??= RenderOwlOrCricket();

    /// <summary>Soft wind (brighter lowpass, higher amplitude) with two-ish sparse high
    /// chirp blips baked into the loop's own texture (distinct from the positional event
    /// layer — this is the non-positional bed's own occasional detail).</summary>
    private static AudioStreamWav RenderDayLoop()
    {
        var rng = new Random(4001);
        float lp = 0f;
        float[] samples = RenderSeamlessLoop(LoopSeconds, LoopOverlapSeconds, (t, u) =>
        {
            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.06f * (white - lp); // brighter lowpass than the night layer.
            float gust = 0.75f + 0.25f * Mathf.Sin(Mathf.Tau * 0.045f * t); // slow gust swell.
            float wind = lp * 0.22f * gust;
            float chirp = SparseHighChirp(t, LoopSeconds, seedOffset: 11);
            return wind + chirp;
        });
        return ToWav(samples, loop: true);
    }

    /// <summary>Thinner, darker wind (heavier lowpass, lower amplitude — §6.1's "less, not
    /// just different") with sparse low chirp/cricket texture baked in.
    ///
    /// <b>Retuned 2026-08-09 by packet 1f, and the reason is a measurement rather than a
    /// preference.</b> This layer is what a creature has to be audible THROUGH after dark (plan
    /// §7.3), and the mix reserves 700–2200 Hz for exactly that. Measured, the
    /// original recipe put only 10.1 dB of rejection in that band — about a tenth of its power
    /// sitting in the room a growl needs — which would have been discovered as "you cannot hear
    /// the creature" in a creature packet that had done nothing wrong.
    ///
    /// Two causes, both structural rather than a level being too high. The wind was a SINGLE
    /// one-pole, which rolls off at only 6 dB/octave and therefore leaks well past a kilohertz;
    /// it is now a three-pole cascade, which is also a more honest reading of "heavier lowpass
    /// than day" than one pole with a smaller coefficient was. The cricket's trill was gated by a
    /// hard 0/1 square, and multiplying a tone by a square wave scatters sidebands upward at
    /// every odd harmonic of the gate — so a 520 Hz cricket was depositing energy across the
    /// whole reserved band. The gate is now a raised cosine, which is the same trill with no
    /// sidebands, and the fundamental moved down to 430 Hz to sit clear of the band edge.
    ///
    /// Nothing was removed. This is a reshaping of where the layer's energy sits, not a
    /// withdrawal — THRILL-BIBLE §6.3's device is untouched and remains unspent.</summary>
    private static AudioStreamWav RenderNightLoop()
    {
        var rng = new Random(4002);
        float lp0 = 0f, lp1 = 0f, lp2 = 0f;
        float[] samples = RenderSeamlessLoop(LoopSeconds, LoopOverlapSeconds, (t, u) =>
        {
            float white = (float)(rng.NextDouble() * 2 - 1);
            // Three poles at ~190 Hz: 18 dB/octave instead of 6. The cascade costs most of the
            // amplitude, which the 3.6x below gives back.
            lp0 += 0.025f * (white - lp0);
            lp1 += 0.025f * (lp0 - lp1);
            lp2 += 0.025f * (lp1 - lp2);
            float gust = 0.7f + 0.3f * Mathf.Sin(Mathf.Tau * 0.03f * t + 1.7f);
            float wind = lp2 * 3.6f * 0.12f * gust; // quieter overall than day's 0.22 peak.
            float cricket = SparseLowCricket(t, LoopSeconds, seedOffset: 23);
            return wind + cricket;
        });
        return ToWav(samples, loop: true);
    }

    private static float SparseHighChirp(float t, float loopSeconds, int seedOffset)
    {
        // Two short high chirps per loop, at fixed (seeded) offsets — "sparse", not a rhythm.
        float[] onsets = { loopSeconds * 0.31f, loopSeconds * 0.74f };
        foreach (float onset in onsets)
        {
            float dt = t - onset;
            if (dt < 0f || dt > 0.12f)
                continue;
            float u = dt / 0.12f;
            float freq = Mathf.Lerp(3400f, 4200f, u);
            return Mathf.Sin(Mathf.Tau * freq * dt) * (1f - u) * 0.05f;
        }
        return 0f;
    }

    private static float SparseLowCricket(float t, float loopSeconds, int seedOffset)
    {
        float[] onsets = { loopSeconds * 0.2f, loopSeconds * 0.55f, loopSeconds * 0.85f };
        foreach (float onset in onsets)
        {
            float dt = t - onset;
            if (dt < 0f || dt > 0.3f)
                continue;
            // A short trilling pulse train, quieter than the day chirp (§6.1).
            //
            // The gate is a RAISED COSINE, not the 0/1 square this originally used. Multiplying a
            // tone by a square wave is amplitude modulation by every odd harmonic of the gate at
            // once, which scattered sidebands from a 520 Hz cricket right across the 700-2200 Hz
            // band the mix reserves for creature voices — measured, see RenderNightLoop's note.
            // A smooth gate is the same audible trill with two sidebands instead of dozens.
            float trill = 0.5f - 0.5f * Mathf.Cos(Mathf.Tau * 32f * dt);
            float freq = 430f; // below the reserved band's lower edge with room for its sidebands
            float envelope = Mathf.Sin(Mathf.Pi * dt / 0.3f);
            return Mathf.Sin(Mathf.Tau * freq * dt) * trill * envelope * 0.045f;
        }
        return 0f;
    }

    /// <summary>One-shot day event clip: a bright two/three-note chirp, played positionally
    /// and sparsely via SfxLab's pool (Scope 2) — distinct from the loop's own baked texture.</summary>
    private static AudioStreamWav RenderBirdsong()
    {
        float[] samples = RenderOneShot(0.35f, (t, u) =>
        {
            float note = u < 0.4f ? 3200f : u < 0.7f ? 3900f : 3500f;
            return Mathf.Sin(Mathf.Tau * note * t) * Envelope(u, attack: 0.02f, curve: 1.6f) * 0.4f;
        });
        return ToWav(samples, loop: false);
    }

    /// <summary>One-shot night event clip: a low owl-ish hoot swell.</summary>
    private static AudioStreamWav RenderOwlOrCricket()
    {
        float[] samples = RenderOneShot(0.6f, (t, u) =>
        {
            float freq = Mathf.Lerp(340f, 260f, u);
            float body = Mathf.Sin(Mathf.Tau * freq * t) + 0.3f * Mathf.Sin(2f * Mathf.Tau * freq * t);
            return body * Envelope(u, attack: 0.08f, curve: 1.4f) * 0.3f;
        });
        return ToWav(samples, loop: false);
    }

    // --- Render plumbing -------------------------------------------------------------------

    private static float[] RenderOneShot(float seconds, Func<float, float, float> sample)
    {
        int count = (int)(SampleRate * seconds);
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float u = i / (float)count;
            data[i] = Mathf.Clamp(sample(t, u), -1f, 1f);
        }
        return data;
    }

    /// <summary>Renders <paramref name="seconds"/> + <paramref name="overlapSeconds"/> of
    /// audio, then equal-power crossfades the tail overlap into the head and returns exactly
    /// <paramref name="seconds"/> worth of samples — a standard loop-splice so the wrap point
    /// has no audible seam (an audible click at the loop point is a bug per the packet's own
    /// scope, not a texture). Internal (not private) so <see cref="AmbientBedSelfTest"/> can
    /// prove the splice numerically against a synthetic signal — see
    /// LoopSpliceEliminatesTheNaiveWrapDiscontinuity, the closest thing to automated evidence
    /// for a claim ("the loop point doesn't click") that is otherwise purely perceptual.</summary>
    /// <remarks>Packet 1f moved the body of this to <see cref="LoopSplice"/> so the looping
    /// emitters could share it instead of growing a second crossfade. The name is kept as a
    /// forwarder rather than replaced at the call sites because
    /// <c>LoopSpliceEliminatesTheNaiveWrapDiscontinuity</c> is the repo's only numeric proof that
    /// a loop point does not click, and a proof that quietly starts testing a different function
    /// than the one it was written against is worse than no proof.</remarks>
    internal static float[] RenderSeamlessLoop(float seconds, float overlapSeconds, Func<float, float, float> sample)
        => LoopSplice.Render(SampleRate, seconds, overlapSeconds, sample);

    private static float Envelope(float u, float attack, float curve)
    {
        float a = u < attack ? u / attack : 1f;
        return a * Mathf.Pow(1f - u, curve);
    }

    private static AudioStreamWav ToWav(float[] samples, bool loop)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(samples[i] * short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        var wav = new AudioStreamWav
        {
            Data = bytes,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
        };
        if (loop)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = samples.Length;
        }
        return wav;
    }
}

/// <summary>
/// Pure, engine-independent math pulled out of <see cref="AmbientBed"/> — the exact seam
/// <see cref="CycleDriver"/>'s own <see cref="CyclePhase"/> gives the phase clock, so this is
/// testable without a running scene tree, a display, or an audio device. See
/// <see cref="AmbientBedSelfTest"/> and tests/Run-AmbientBedTest.ps1.
/// </summary>
public static class AmbientBedMath
{
    public const float DayEndPhase = 0.5f;
    public const float NightStartPhase = 0.65f;
    public const float NightEndPhase = 0.9f;
    public const float NightGainScale = 0.55f;

    /// <summary>Normalized night weight in [0,1] for a given cycle phase in [0,1) — 0 = full
    /// day, 1 = full night, smoothstepped through the two transition bands. Continuous across
    /// the phase wrap (0.999... -&gt; 0.000) by construction: the descending dawn transition
    /// ([NightEndPhase, 1.0)) reaches exactly 0 at phase 1.0, which is the same value the flat
    /// day band already holds at phase 0.0.</summary>
    public static float NightWeight(float phase)
    {
        // Defensive wrap for out-of-[0,1) callers — CycleDriver.Phase is contractually always
        // in range, so this is a last-resort guard, not the primary validation (one clamp
        // check is enough per the packet's own scope note).
        phase = Mathf.PosMod(phase, 1f);

        if (phase < DayEndPhase)
            return 0f;
        if (phase < NightStartPhase)
            return SmoothStep((phase - DayEndPhase) / (NightStartPhase - DayEndPhase));
        if (phase < NightEndPhase)
            return 1f;
        return SmoothStep(1f - (phase - NightEndPhase) / (1f - NightEndPhase));
    }

    /// <summary>Day loop layer's linear gain: the day complement of the night weight, scaled
    /// by the withdrawal master gain. Never bakes the master gain into the mix state itself —
    /// see <see cref="AmbientBed.Restore"/>'s doc for why that separation matters.</summary>
    public static float DayLayerGain(float nightWeight, float masterGain) =>
        (1f - nightWeight) * masterGain;

    /// <summary>Night loop layer's linear gain — scaled by <see cref="NightGainScale"/> on top
    /// of the night weight itself, so night never gets as loud as day even at full weight
    /// (§6.1: night reads as less, not just different).</summary>
    public static float NightLayerGain(float nightWeight, float masterGain) =>
        nightWeight * NightGainScale * masterGain;

    /// <summary>Linear fade from <paramref name="from"/> to <paramref name="to"/> over
    /// <paramref name="durationSec"/>, reaching EXACTLY <paramref name="to"/> once
    /// <paramref name="elapsedSec"/> &gt;= <paramref name="durationSec"/> (clamped progress,
    /// not an asymptotic curve — a withdrawal must reach true silence, not approach it).</summary>
    public static float FadeValue(float from, float to, float elapsedSec, float durationSec)
    {
        if (durationSec <= 0f)
            return to; // MECHANICS-BIBLE §6: define the extreme rather than divide by zero.
        float t = Mathf.Clamp(elapsedSec / durationSec, 0f, 1f);
        return Mathf.Lerp(from, to, t);
    }

    private static float SmoothStep(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

/// <summary>
/// Headless pure-logic checks of <see cref="AmbientBedMath"/> — no scene tree, no audio
/// device, no display. The same CI-safe split every other *SelfTest in this codebase uses
/// (CycleSelfTest, ReconnectSelfTest, ...). Run via AmbientLab's own manually-parsed
/// --ambient-selftest flag (see scripts/dev/AmbientLab.cs) rather than LaunchOptions/Boot.cs —
/// this packet is designed to need zero shared-file edits, and this test needs no live node
/// tree, so it does not need the shared self-test dispatch table either.
/// </summary>
public static class AmbientBedSelfTest
{
    private static readonly System.Collections.Generic.List<string> Failures = new();

    public static int Run()
    {
        Failures.Clear();

        NightWeightIsContinuousAcrossTheWrap();
        NightWeightIsFlatInTheInteriorOfDayAndNight();
        NightWeightIsMonotonicThroughEachTransitionBand();
        NightWeightAtExactBandEdgesMatchesItsNeighbours();
        NightWeightNeverThrowsOrNaNsOutsideZeroOneRange();
        FadeValueReachesExactlyTheTargetAtDuration();
        RestoreLandsBackAtTheCurrentMixNotAHardcodedDefault();
        LoopSpliceEliminatesTheNaiveWrapDiscontinuity();
        DaytimeSceneryGateSharesTheBedsOwnBands();
        LoopStreamsAreRealLoopingAudio();
        BusLayoutHasTheStatedTopology();
        BusLayoutIsIdempotentUnderRepeatedCalls();
        TheDuckSitsOnTheBedLaneAndNowhereElse();
        TheDuckIsSwitchable();

        if (Failures.Count == 0)
        {
            GD.Print("[ambient-bed-selftest] PASS (night-weight wrap continuity, flat interiors, " +
                "monotonic transitions, band-edge agreement, out-of-range safety, exact fade " +
                "target, withdraw/restore mix-gain fidelity, loop-splice wrap-seam bound, " +
                "day-scenery gate shares the bed's bands, loop streams are real looping audio, " +
                "bus topology, EnsureLayout idempotency, duck placement, duck switchability)");
            return 0;
        }
        foreach (string failure in Failures)
            GD.PrintErr($"[ambient-bed-selftest] FAIL: {failure}");
        return 1;
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            Failures.Add(what);
    }

    private static void NightWeightIsContinuousAcrossTheWrap()
    {
        float justBefore = AmbientBedMath.NightWeight(0.9999f);
        float justAfter = AmbientBedMath.NightWeight(0.0001f);
        Check(System.Math.Abs(justBefore - justAfter) < 0.01f,
            $"wrap continuity: NightWeight(0.9999)={justBefore} vs NightWeight(0.0001)={justAfter} - should be nearly identical");
    }

    private static void NightWeightIsFlatInTheInteriorOfDayAndNight()
    {
        Check(AmbientBedMath.NightWeight(0.1f) == 0f, "day interior: NightWeight(0.1) should be exactly 0");
        Check(AmbientBedMath.NightWeight(0.25f) == 0f, "day interior: NightWeight(0.25) should be exactly 0");
        Check(AmbientBedMath.NightWeight(0.75f) == 1f, "night interior: NightWeight(0.75) should be exactly 1");
        Check(AmbientBedMath.NightWeight(0.8333f) == 1f, "night interior: NightWeight(0.8333) should be exactly 1");
    }

    private static void NightWeightIsMonotonicThroughEachTransitionBand()
    {
        float prev = -1f;
        for (float phase = AmbientBedMath.DayEndPhase; phase <= AmbientBedMath.NightStartPhase; phase += 0.01f)
        {
            float w = AmbientBedMath.NightWeight(phase);
            Check(w >= prev - 1e-5f, $"dusk transition: NightWeight went backward at phase={phase} ({w} < {prev})");
            prev = w;
        }
        prev = 2f;
        for (float phase = AmbientBedMath.NightEndPhase; phase < 1f; phase += 0.005f)
        {
            float w = AmbientBedMath.NightWeight(phase);
            Check(w <= prev + 1e-5f, $"dawn transition: NightWeight went upward at phase={phase} ({w} > {prev})");
            prev = w;
        }
    }

    private static void NightWeightAtExactBandEdgesMatchesItsNeighbours()
    {
        Check(AmbientBedMath.NightWeight(AmbientBedMath.DayEndPhase) == 0f,
            "band edge: NightWeight AT DayEndPhase should still be exactly 0 (day's flat value)");
        Check(AmbientBedMath.NightWeight(AmbientBedMath.NightStartPhase) == 1f,
            "band edge: NightWeight AT NightStartPhase should already be exactly 1 (night's flat value)");
        Check(AmbientBedMath.NightWeight(AmbientBedMath.NightEndPhase) == 1f,
            "band edge: NightWeight AT NightEndPhase should still be exactly 1 (night's flat value)");
    }

    private static void NightWeightNeverThrowsOrNaNsOutsideZeroOneRange()
    {
        float above = AmbientBedMath.NightWeight(1.2f);
        float below = AmbientBedMath.NightWeight(-0.1f);
        Check(float.IsFinite(above), $"out-of-range: NightWeight(1.2) should be finite, got {above}");
        Check(float.IsFinite(below), $"out-of-range: NightWeight(-0.1) should be finite, got {below}");
        // 1.2 wraps to 0.2 (day interior); -0.1 wraps to 0.9 (exactly NightEndPhase, still night).
        Check(above == AmbientBedMath.NightWeight(0.2f), $"out-of-range: NightWeight(1.2) should equal NightWeight(0.2) after wrap, got {above}");
        Check(below == AmbientBedMath.NightWeight(0.9f), $"out-of-range: NightWeight(-0.1) should equal NightWeight(0.9) after wrap, got {below}");
    }

    private static void FadeValueReachesExactlyTheTargetAtDuration()
    {
        float atExactDuration = AmbientBedMath.FadeValue(1f, 0f, 2f, 2f);
        Check(atExactDuration == 0f, $"fade: FadeValue at exactly the duration should be exactly 0, got {atExactDuration}");
        float pastDuration = AmbientBedMath.FadeValue(1f, 0f, 5f, 2f);
        Check(pastDuration == 0f, $"fade: FadeValue past the duration should still be exactly 0, got {pastDuration}");
        float restoreAtDuration = AmbientBedMath.FadeValue(0f, 1f, 3f, 3f);
        Check(restoreAtDuration == 1f, $"fade: restore FadeValue at exactly the duration should be exactly 1, got {restoreAtDuration}");
    }

    // The scenario named explicitly in the dispatch's own scope: withdrawing during dusk and
    // restoring should land back at DUSK's gain, not day's, and not some hardcoded default —
    // proving the master gain and the day/night mix are genuinely independent multipliers.
    private static void RestoreLandsBackAtTheCurrentMixNotAHardcodedDefault()
    {
        const float duskPhase = 0.55f; // mid dusk-transition: partial, non-trivial night weight.
        float duskNightWeight = AmbientBedMath.NightWeight(duskPhase);
        Check(duskNightWeight > 0f && duskNightWeight < 1f, $"test setup: phase {duskPhase} should be mid-transition, got weight {duskNightWeight}");

        float dayGainBefore = AmbientBedMath.DayLayerGain(duskNightWeight, masterGain: 1f);
        float nightGainBefore = AmbientBedMath.NightLayerGain(duskNightWeight, masterGain: 1f);

        // Withdraw fully (master gain -> 0): both layers must go silent regardless of mix.
        float gainAfterWithdraw = AmbientBedMath.FadeValue(1f, 0f, 2f, 2f);
        Check(AmbientBedMath.DayLayerGain(duskNightWeight, gainAfterWithdraw) == 0f, "withdraw: day layer gain should be exactly 0 once master gain reaches 0");
        Check(AmbientBedMath.NightLayerGain(duskNightWeight, gainAfterWithdraw) == 0f, "withdraw: night layer gain should be exactly 0 once master gain reaches 0");

        // Restore fully (master gain -> 1), phase unchanged (still dusk): must match the
        // ORIGINAL dusk gains exactly, not day's (1.0/0.0) and not night's (0.0/NightGainScale).
        float gainAfterRestore = AmbientBedMath.FadeValue(0f, 1f, 2f, 2f);
        float dayGainAfter = AmbientBedMath.DayLayerGain(duskNightWeight, gainAfterRestore);
        float nightGainAfter = AmbientBedMath.NightLayerGain(duskNightWeight, gainAfterRestore);
        Check(dayGainAfter == dayGainBefore, $"restore: day layer gain should return to dusk's own {dayGainBefore}, got {dayGainAfter}");
        Check(nightGainAfter == nightGainBefore, $"restore: night layer gain should return to dusk's own {nightGainBefore}, got {nightGainAfter}");
    }

    // "The loop point doesn't click" is otherwise a purely perceptual claim (see the PR body's
    // headed-audio section for the honest limit of what a dispatched agent can verify by ear).
    // This proves the splice mechanism numerically: a 137Hz test tone over a non-integer number
    // of cycles WOULD produce a large naive trim-and-loop jump (comparing sample 0 to the last
    // sample of a plain trim), but AmbientBed.RenderSeamlessLoop's actual wrap (last spliced
    // sample followed by the first) is bounded by the tone's ordinary adjacent-sample delta
    // (~2*pi*freq/sampleRate), because the splice makes spliced[0] equal the natural
    // continuation of spliced[last] rather than an arbitrary phase-mismatched restart.
    private static void LoopSpliceEliminatesTheNaiveWrapDiscontinuity()
    {
        const float seconds = 1f;
        const float overlap = 0.1f;
        const float freq = 137.3f; // deliberately non-integer cycles across `seconds` (137.3
                                    // full periods in 1s) - guarantees a real phase mismatch
                                    // at a naive trim point, unlike a whole-number frequency.
        const int sampleRate = 48000;

        float[] spliced = AmbientBed.RenderSeamlessLoop(seconds, overlap,
            (t, u) => Mathf.Sin(Mathf.Tau * freq * t));

        float naiveSeam = System.Math.Abs(
            Mathf.Sin(0f) - Mathf.Sin(Mathf.Tau * freq * seconds));
        float actualSeam = System.Math.Abs(spliced[0] - spliced[^1]);
        float ordinaryAdjacentDelta = 2f * Mathf.Pi * freq / sampleRate; // worst-case slope bound.

        Check(naiveSeam > 0.3f,
            $"test setup: chosen frequency should produce a real naive trim-and-loop seam to prove the fix against, got {naiveSeam}");
        Check(actualSeam <= ordinaryAdjacentDelta + 0.01f,
            $"loop splice: wrap-seam delta ({actualSeam}) should be bounded by the tone's own ordinary adjacent-sample delta (~{ordinaryAdjacentDelta}), not the naive trim-and-loop seam ({naiveSeam}) it replaces");
    }

    // SparseSfxEmitter.DaytimeOnly silences day scenery at NightWeight >= 0.5. That number is
    // only defensible if it lands inside the bed's own dusk crossfade — if the day scenery cut
    // out before the night loop started rising, or after it had finished, the handover would
    // read as a gap or as a pile-up. Both are audible bugs and both are pure math.
    private static void DaytimeSceneryGateSharesTheBedsOwnBands()
    {
        const float gate = 0.5f;
        Check(AmbientBedMath.NightWeight(0.25f) < gate, "day interior should be audibly DAY for the scenery gate");
        Check(AmbientBedMath.NightWeight(0.75f) >= gate, "night interior should be audibly NIGHT for the scenery gate");

        // The crossover must fall strictly INSIDE the dusk band, not at either edge.
        float crossover = -1f;
        for (float phase = AmbientBedMath.DayEndPhase; phase <= AmbientBedMath.NightStartPhase; phase += 0.001f)
        {
            if (AmbientBedMath.NightWeight(phase) >= gate)
            {
                crossover = phase;
                break;
            }
        }
        Check(crossover > AmbientBedMath.DayEndPhase && crossover < AmbientBedMath.NightStartPhase,
            $"scenery gate: the day->night crossover ({crossover}) should fall strictly inside the dusk band ({AmbientBedMath.DayEndPhase}..{AmbientBedMath.NightStartPhase}) so the layers hand over instead of leaving a gap");
    }

    // The one thing a headless run can say about the bed's actual SOUND: that there is one. A
    // synthesis recipe that silently renders zeros — an envelope that never opens, a filter
    // coefficient that eats the signal, a clamp in the wrong place — produces a bed that is
    // present in the scene tree, correctly routed, correctly crossfaded, and completely inaudible.
    // Every other check in this file would pass. So: real length, LoopMode actually set (SfxLab's
    // own ToWav never sets it, which is why a bed could not live there), and a waveform that is
    // neither silence nor clipped-to-the-rails noise.
    private static void LoopStreamsAreRealLoopingAudio()
    {
        foreach ((string name, AudioStreamWav wav) in new[]
                 { ("day", AmbientBed.GetDayLoop()), ("night", AmbientBed.GetNightLoop()) })
        {
            Check(wav.LoopMode == AudioStreamWav.LoopModeEnum.Forward,
                $"{name} loop: LoopMode is {wav.LoopMode} — a bed that does not loop plays once and " +
                "leaves silence behind it, which is a withdrawal nobody authored");
            Check(wav.MixRate == 48000, $"{name} loop: mix rate {wav.MixRate} does not match the pinned 48 kHz");

            byte[] data = wav.Data;
            Check(data.Length > 48000 * 2 * 10,
                $"{name} loop: only {data.Length / 2} samples — far short of a bed long enough not to be caught repeating");

            // 16-bit LE mono. Peak and mean magnitude together separate the three failure modes:
            // all-zero (both ~0), a lone spike (peak high, mean ~0), and rails (both maxed).
            long sum = 0;
            int peak = 0;
            int count = data.Length / 2;
            for (int i = 0; i < count; i++)
            {
                int s = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
                int mag = s < 0 ? -s : s;
                sum += mag;
                if (mag > peak)
                    peak = mag;
            }
            double mean = count > 0 ? sum / (double)count : 0;
            // Printed, not just asserted: the thresholds below only catch catastrophe, and the
            // actual figures are the closest thing to a level reading anyone gets without ears.
            GD.Print($"[ambient-bed-selftest] {name} loop: {count} samples, peak {peak}/32767, mean {mean:F1}");
            Check(peak > 300, $"{name} loop: peak magnitude {peak} of 32767 — this is silence, not a bed");
            Check(mean > 20, $"{name} loop: mean magnitude {mean:F1} — the layer is a couple of clicks in a silent buffer, not a continuous bed");
            Check(peak < 32760, $"{name} loop: peak magnitude {peak} is on the rails — the recipe is clipping");
            Check(wav.LoopEnd == count, $"{name} loop: LoopEnd {wav.LoopEnd} does not cover the whole buffer ({count} samples) — the wrap would land mid-splice");
        }
    }

    // --- Bus layout ------------------------------------------------------------------------
    //
    // These need an AudioServer but NOT an audio device: headless Godot runs a dummy driver and
    // still maintains the bus graph, so topology, chain contents and idempotency are all
    // genuinely CI-provable. What is not provable here is anything about how it SOUNDS — no
    // headless run can hear a duck, a crossfade or a loop seam.

    private static void BusLayoutHasTheStatedTopology()
    {
        AudioBuses.EnsureLayout();

        int ambient = AudioServer.GetBusIndex(AudioBuses.Ambient);
        int bed = AudioServer.GetBusIndex(AudioBuses.Bed);
        int scenery = AudioServer.GetBusIndex(AudioBuses.Scenery);
        Check(ambient >= 0, "bus layout: the Ambient group bus should exist after EnsureLayout");
        Check(bed >= 0, "bus layout: the Ambient_Bed lane should exist after EnsureLayout");
        Check(scenery >= 0, "bus layout: the Ambient_Scenery lane should exist after EnsureLayout");
        if (ambient < 0 || bed < 0 || scenery < 0)
            return;

        Check(AudioServer.GetBusSend(ambient) == "Master",
            $"bus layout: Ambient should send to Master, sends to '{AudioServer.GetBusSend(ambient)}'");
        // The lanes MUST send to the group, not straight to Master — otherwise the player's one
        // slider and any future whole-world trim reach only half the ambience.
        Check(AudioServer.GetBusSend(bed) == AudioBuses.Ambient,
            $"bus layout: {AudioBuses.Bed} should send to {AudioBuses.Ambient}, sends to '{AudioServer.GetBusSend(bed)}'");
        Check(AudioServer.GetBusSend(scenery) == AudioBuses.Ambient,
            $"bus layout: {AudioBuses.Scenery} should send to {AudioBuses.Ambient}, sends to '{AudioServer.GetBusSend(scenery)}'");
    }

    // The hazard this exists for is specific and silent: a bus builder that guards creation by
    // name but adds its effects unconditionally stacks a second compressor on the second call,
    // a third on the third, and nothing anywhere reports it — the mix just collapses.
    private static void BusLayoutIsIdempotentUnderRepeatedCalls()
    {
        AudioBuses.EnsureLayout();
        int busesBefore = AudioServer.BusCount;
        int bed = AudioServer.GetBusIndex(AudioBuses.Bed);
        if (bed < 0)
            return; // already reported by the topology check
        int effectsBefore = AudioServer.GetBusEffectCount(bed);

        for (int i = 0; i < 4; i++)
            AudioBuses.EnsureLayout();

        Check(AudioServer.BusCount == busesBefore,
            $"idempotency: 4 extra EnsureLayout calls changed the bus count ({busesBefore} -> {AudioServer.BusCount})");
        Check(AudioServer.GetBusEffectCount(bed) == effectsBefore,
            $"idempotency: 4 extra EnsureLayout calls stacked effects on {AudioBuses.Bed} ({effectsBefore} -> {AudioServer.GetBusEffectCount(bed)})");
    }

    private static void TheDuckSitsOnTheBedLaneAndNowhereElse()
    {
        AudioBuses.EnsureLayout();
        int ambient = AudioServer.GetBusIndex(AudioBuses.Ambient);
        int bed = AudioServer.GetBusIndex(AudioBuses.Bed);
        int scenery = AudioServer.GetBusIndex(AudioBuses.Scenery);
        if (ambient < 0 || bed < 0 || scenery < 0)
            return;

        Check(AudioServer.GetBusEffectCount(bed) == 1,
            $"duck: {AudioBuses.Bed} should carry exactly the one compressor, carries {AudioServer.GetBusEffectCount(bed)} effects");
        Check(AudioServer.GetBusEffect(bed, 0) is AudioEffectCompressor,
            "duck: the effect on the bed lane should be an AudioEffectCompressor");
        if (AudioServer.GetBusEffect(bed, 0) is AudioEffectCompressor comp)
        {
            // Sidechained to Voice, which is READ as a detector. If this ever became a plain
            // compressor the bed would squash itself instead of getting out of speech's way.
            Check(comp.Sidechain == AudioBuses.Voice,
                $"duck: the compressor should sidechain off '{AudioBuses.Voice}', reads '{comp.Sidechain}'");
            Check(comp.Ratio <= 3f,
                $"duck: ratio {comp.Ratio} is deep enough to turn the bed into an event — the bed ducks, it does not perform");
        }

        // The scenery lane runs clean on purpose: Godot's compressor tops out at a 2 ms attack,
        // so ducking a lane made of transients pumps instead of clearing space.
        Check(AudioServer.GetBusEffectCount(scenery) == 0,
            $"duck: {AudioBuses.Scenery} should carry no effects, carries {AudioServer.GetBusEffectCount(scenery)}");
        // And the group handle stays clean, or the split above buys nothing.
        Check(AudioServer.GetBusEffectCount(ambient) == 0,
            $"duck: the {AudioBuses.Ambient} group handle should carry no effects, carries {AudioServer.GetBusEffectCount(ambient)}");
    }

    // Whether the bed should duck at all is an open mix question (in a six-player proximity-voice
    // session somebody is nearly always talking, so a Voice-keyed sidechain is close to a
    // permanent level cut). The switch is what keeps that question answerable by standing in it
    // rather than by rebuilding the tree, so the switch itself is worth a test.
    private static void TheDuckIsSwitchable()
    {
        AudioBuses.EnsureLayout();
        Check(AudioBuses.DuckEnabled, "duck: should be enabled by default after EnsureLayout");
        AudioBuses.SetDuckEnabled(false);
        Check(!AudioBuses.DuckEnabled, "duck: SetDuckEnabled(false) should disable it");
        AudioBuses.SetDuckEnabled(true);
        Check(AudioBuses.DuckEnabled, "duck: SetDuckEnabled(true) should re-enable it");
    }
}
