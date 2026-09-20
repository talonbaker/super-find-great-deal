using System;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Game.World;

/// <summary>
/// The ear's speedometer, as arithmetic a test can check: horizontal speed in → (body gain dB,
/// low-pass cutoff Hz, hiss-layer gain dB) out. LD-4 (2026-09-02), research D4-R1.
///
/// <b>What it encodes.</b> Talon's 2026-09-01 brief: <i>stopping should feel bad</i>. The checker
/// floor is the eye's speedometer (texture flowing past); this is the ear's. Reality gives a
/// monotonic law (Seidman 2017, ≈0.44 dB per km/h at a cyclist's ear); the game exaggerates the
/// slope, and it keeps the shape: silent at or below walk, a whisper between walk and jog, open
/// from jog to sprint, and a high hiss that only joins past sprint — which on foot only happens
/// downhill or on carried momentum, so the hiss reads as "faster than your legs".
///
/// <b>The four knees are the whole tuning surface.</b> They are read off
/// <see cref="MotorTuning.Default"/> rather than copied, so the ladder moving (MOVE-8 moved it)
/// moves this curve with it. The dB and Hz values at each knee are the value forks the packet
/// hands this role; they are stated here once and nowhere else.
///
/// <b>Direction (/direct, 2026-09-02, THRILL-BIBLE §12).</b> This is a movement-feel system at
/// system altitude, not a dread device. Three constraints it carries from the ledger: the wind is
/// the mover's own and never the world's — a pure function of speed, no gusts, no events, no
/// direction; it sits on the honest lane (§8.3) and may never be modulated by threat, darkness or
/// anything but speed; and its cut is the most <i>caused</i> absence in the game and is therefore
/// NOT §6.3's wrong silence — never log it as one.
///
/// Engine-free by construction (System.MathF; <see cref="Vector3"/> is GodotSharp's plain struct)
/// so <c>tests/unit</c> reaches every number without an engine — the same split as
/// <see cref="FireAudioRank"/> and <see cref="LoopSplice"/>.
/// </summary>
public static class SpeedWindCurve
{
    // --- The speed ladder, derived, never copied --------------------------------------------

    /// <summary>Walk gear (Left Ctrl): 0.45 × MoveSpeed = 1.71 m/s at the shipped ladder.</summary>
    public static float WalkMps => MotorTuning.Default.DuckWalkSpeedMps;

    /// <summary>Jog, the default gear: 3.80 m/s shipped.</summary>
    public static float JogMps => MotorTuning.Default.MoveSpeed;

    /// <summary>Sprint: 1.6 × jog = 6.08 m/s shipped. The fastest the legs go on the flat.</summary>
    public static float SprintMps => MotorTuning.Default.MoveSpeed * MotorTuning.Default.SprintMultiplier;

    /// <summary>Where the body layer reaches its ceiling: 1.2 × sprint (7.30 m/s shipped). Past
    /// sprint the only sources of speed are slopes, carried momentum and — later — the bike, and
    /// the curve keeps rising through that band so those read as MORE, not as "sprint again".</summary>
    public static float FullMps => SprintMps * 1.2f;

    /// <summary>Where the hiss reaches its own ceiling: 1.25 × sprint (7.60 m/s shipped).</summary>
    public static float HissFullMps => SprintMps * 1.25f;

    // --- Body gain (dB, absolute VolumeDb on the wind bus) -----------------------------------

    /// <summary>Exactly silent. <see cref="SfxLab.SilentDb"/>, not a small number, so a stopped
    /// body lands ON silence rather than approaching it.</summary>
    public const float SilentDb = SfxLab.SilentDb;

    /// <summary>Just above walk: a whisper you can hear only because it was not there a moment
    /// ago. −34 dB is chosen so that walk → jog is a 20 dB climb and reads as opening.</summary>
    public const float WhisperDb = -34f;

    /// <summary>At jog. −16 dB: present, sits under a speaking teammate, under the fire's body
    /// (−13 dB at the pit) — moving at the default gear must not bury the beacon.</summary>
    public const float JogDb = -16f;

    /// <summary>At sprint. −6 dB: the wind owns the mix above the bed. The 10 dB jog → sprint
    /// step is 4.4 dB per m/s ≈ 1.2 dB per km/h — about 2.8× Seidman's real-world slope, which is
    /// the exaggeration the research asks for.</summary>
    public const float SprintDb = -6f;

    /// <summary>At and past 1.2 × sprint. Unity on the bus; the bus itself is under the Ambient
    /// group so the player's World Ambience slider trims it.</summary>
    public const float FullDb = 0f;

    // --- Low-pass cutoff (Hz) --------------------------------------------------------------

    /// <summary>The filter's rest position while silent, and the whisper's colour just above
    /// walk: a dull rumble with no air on it.</summary>
    public const float WalkCutoffHz = 250f;

    public const float JogCutoffHz = 700f;

    public const float SprintCutoffHz = 3500f;

    /// <summary>Open. 9 kHz rather than the filter's 20.5 kHz ceiling: the body layer is meant
    /// to keep some roundness even flat out, and the top octave is the hiss layer's job.</summary>
    public const float FullCutoffHz = 9000f;

    // --- Hiss gain (dB) ---------------------------------------------------------------------

    /// <summary>Just above sprint. −26 dB: barely there, and it is the being-there that matters.</summary>
    public const float HissEntryDb = -26f;

    /// <summary>At and past 1.25 × sprint.</summary>
    public const float HissFullDb = -10f;

    /// <summary>Body-layer gain at a horizontal speed. Monotonic non-decreasing; exactly
    /// <see cref="SilentDb"/> at and below walk.</summary>
    public static float BodyGainDb(float speedMps)
    {
        if (!float.IsFinite(speedMps) || speedMps <= WalkMps)
            return SilentDb;
        if (speedMps <= JogMps)
            return Lerp(WhisperDb, JogDb, Frac(speedMps, WalkMps, JogMps));
        if (speedMps <= SprintMps)
            return Lerp(JogDb, SprintDb, Frac(speedMps, JogMps, SprintMps));
        if (speedMps <= FullMps)
            return Lerp(SprintDb, FullDb, Frac(speedMps, SprintMps, FullMps));
        return FullDb;
    }

    /// <summary>Low-pass cutoff at a horizontal speed. Interpolated in the LOG domain between the
    /// knees — pitch perception is logarithmic, and a linear-Hz ramp spends most of its travel in
    /// the top octave where nothing audible happens and then lurches (sound-real-time-effects §4).
    /// Monotonic non-decreasing.</summary>
    public static float CutoffHz(float speedMps)
    {
        if (!float.IsFinite(speedMps) || speedMps <= WalkMps)
            return WalkCutoffHz;
        if (speedMps <= JogMps)
            return LogLerp(WalkCutoffHz, JogCutoffHz, Frac(speedMps, WalkMps, JogMps));
        if (speedMps <= SprintMps)
            return LogLerp(JogCutoffHz, SprintCutoffHz, Frac(speedMps, JogMps, SprintMps));
        if (speedMps <= FullMps)
            return LogLerp(SprintCutoffHz, FullCutoffHz, Frac(speedMps, SprintMps, FullMps));
        return FullCutoffHz;
    }

    /// <summary>Hiss-layer gain at a horizontal speed. Exactly <see cref="SilentDb"/> at and
    /// below sprint; monotonic non-decreasing above it.</summary>
    public static float HissGainDb(float speedMps)
    {
        if (!float.IsFinite(speedMps) || speedMps <= SprintMps)
            return SilentDb;
        if (speedMps <= HissFullMps)
            return Lerp(HissEntryDb, HissFullDb, Frac(speedMps, SprintMps, HissFullMps));
        return HissFullDb;
    }

    /// <summary>The speed the curve reads: the horizontal magnitude only. Vertical velocity is
    /// deliberately not an input, and neither is grounded-ness — the motor keeps horizontal speed
    /// through a landing, so the wind keeps it too (research D4 item 5: landing costs nothing; a
    /// ducked landing would SOUND like a speed loss the body did not take).</summary>
    public static float HorizontalSpeed(Vector3 velocity) =>
        MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);

    // --- dB helpers, engine-free ------------------------------------------------------------

    /// <summary>dB → linear amplitude, with <see cref="SilentDb"/> (and anything below) mapping
    /// to exactly zero so the stream's silence is a true zero rather than 1e-4.</summary>
    public static float DbToAmp(float db) =>
        db <= SilentDb ? 0f : MathF.Pow(10f, db / 20f);

    /// <summary>Linear amplitude → dB, with zero mapping to exactly <see cref="SilentDb"/>.</summary>
    public static float AmpToDb(float amp) =>
        amp <= 0f ? SilentDb : MathF.Max(SilentDb, 20f * MathF.Log10(amp));

    private static float Frac(float x, float a, float b) => Math.Clamp((x - a) / (b - a), 0f, 1f);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float LogLerp(float a, float b, float t) => a * MathF.Pow(b / a, t);
}

/// <summary>
/// The rate shaping: the stateful half of the speedometer, stepped once per physics tick from
/// the local avatar's velocity. <see cref="SpeedWindCurve"/> says where the wind should BE for a
/// speed; this says how it gets there, and the two directions are deliberately not symmetric.
///
/// <b>The wind never lags a loss and always lags a gain.</b> Research D4: "what matters most is
/// the rate of change: a skid must sound like the wind being CUT, not faded." So a decrease is
/// followed at a fixed linear-in-amplitude rate that reaches silence from full in
/// <see cref="CutSeconds"/> (−12 dB relative in three quarters of that), while an increase is a
/// one-pole rise with time constant <see cref="RiseTauSeconds"/> — a wind that builds. Linear in
/// AMPLITUDE for the cut, for the reason <c>LoopingSfxEmitter.GainToDb</c> gives: linear-in-dB
/// spends most of its duration inaudible, which would give a cut the perceptual shape of a fade.
///
/// The cutoff follows the curve with a log-domain rate limit (<see cref="MaxOctavesPerSecond"/>)
/// so no single audio buffer sees a coefficient jump large enough to click
/// (sound-real-time-effects §4).
///
/// <b>Suppression</b> (menu open, the mute toggle) is a separate multiplier with its own slew, so
/// a menu opening fades the wind out over <see cref="SuppressSeconds"/> rather than cutting it —
/// a cut is the skid's word and the menu must not borrow it.
/// </summary>
public sealed class SpeedWindStream
{
    /// <summary>Full → silence on a decrease, seconds. 0.10 s: −12 dB relative at 0.075 s from
    /// full, and a step from the sprint level (−6 dB, amplitude 0.5) is −12 dB relative in
    /// 0.0375 s. A cut, and it is stated in the test names.</summary>
    public const float CutSeconds = 0.10f;

    /// <summary>One-pole time constant of a rise, seconds. 0.25 s puts the −3 dB point of a step
    /// from silence at τ·ln(1/(1−10^(−3/20))) = 0.307 s — "the wind takes 0.3 s to arrive".</summary>
    public const float RiseTauSeconds = 0.25f;

    /// <summary>The rise time the tests name: from silence, the body gain reaches within 3 dB of
    /// a stepped target no sooner than this.</summary>
    public const float RiseToMinus3DbSeconds = 0.30f;

    /// <summary>Rate limit on the cutoff, octaves per second. 12: the whole walk → full sweep
    /// (250 → 9000 Hz, 5.2 octaves) takes 0.43 s if asked for at once, which is slower than the
    /// motor's own 0.68 s stop-to-sprint anyway, so the limit only ever binds on a cut.</summary>
    public const float MaxOctavesPerSecond = 12f;

    /// <summary>Full → silent on suppression, seconds.</summary>
    public const float SuppressSeconds = 0.25f;

    private float _bodyAmp;      // 0..1, the follower's own state (linear amplitude of the body gain)
    private float _hissAmp;      // 0..1
    private float _cutoffHz = SpeedWindCurve.WalkCutoffHz;
    private float _open = 1f;    // 1 = not suppressed, 0 = fully suppressed
    private bool _suppressed;

    /// <summary>The speed the last step read, m/s horizontal.</summary>
    public float SpeedMps { get; private set; }

    /// <summary>Body-layer VolumeDb right now.</summary>
    public float BodyDb => SpeedWindCurve.AmpToDb(_bodyAmp);

    /// <summary>Hiss-layer VolumeDb right now.</summary>
    public float HissDb => SpeedWindCurve.AmpToDb(_hissAmp);

    /// <summary>Low-pass cutoff right now, Hz.</summary>
    public float CutoffHz => _cutoffHz;

    /// <summary>Whether the stream is being driven to silence regardless of speed.</summary>
    public bool Suppressed => _suppressed;

    /// <summary>Menu open, mute toggle, no avatar: drive to silence over
    /// <see cref="SuppressSeconds"/>. Releasing lets the wind build back at the ordinary rise.</summary>
    public void SetSuppressed(bool suppressed) => _suppressed = suppressed;

    /// <summary>One physics tick. Takes the body's whole velocity and reads only its horizontal
    /// part — see <see cref="SpeedWindCurve.HorizontalSpeed"/> for why the vertical component and
    /// grounded-ness are not inputs.</summary>
    public void Step(Vector3 velocity, float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
            return;

        SpeedMps = SpeedWindCurve.HorizontalSpeed(velocity);

        float openTarget = _suppressed ? 0f : 1f;
        _open = Slew(_open, openTarget, dt / SuppressSeconds);

        float bodyTarget = SpeedWindCurve.DbToAmp(SpeedWindCurve.BodyGainDb(SpeedMps)) * _open;
        float hissTarget = SpeedWindCurve.DbToAmp(SpeedWindCurve.HissGainDb(SpeedMps)) * _open;

        _bodyAmp = Follow(_bodyAmp, bodyTarget, dt);
        _hissAmp = Follow(_hissAmp, hissTarget, dt);

        float cutoffTarget = SpeedWindCurve.CutoffHz(SpeedMps);
        float maxOct = MaxOctavesPerSecond * dt;
        float wantOct = MathF.Log2(cutoffTarget / _cutoffHz);
        _cutoffHz *= MathF.Pow(2f, Math.Clamp(wantOct, -maxOct, maxOct));
    }

    /// <summary>The asymmetric follower. Down: linear slew, full → silent in
    /// <see cref="CutSeconds"/>. Up: one-pole toward the target with <see cref="RiseTauSeconds"/>.
    /// Exposed so the tests can name the shape rather than infer it.</summary>
    public static float Follow(float current, float target, float dt)
    {
        if (target < current)
            return MathF.Max(target, current - dt / CutSeconds);
        if (target > current)
        {
            float k = 1f - MathF.Exp(-dt / RiseTauSeconds);
            return current + (target - current) * k;
        }
        return current;
    }

    private static float Slew(float current, float target, float maxStep)
    {
        if (target > current)
            return MathF.Min(target, current + maxStep);
        if (target < current)
            return MathF.Max(target, current - maxStep);
        return current;
    }
}

/// <summary>
/// The two noise beds the layer plays, rendered once at load. Engine-free (returns samples);
/// <c>SpeedWindLayer</c> wraps them into looping <c>AudioStreamWav</c>s through
/// <see cref="SfxLab.ToWavLooping"/>.
///
/// Both are seeded and eventless: no gusts, no swells, no grain — the direction is that this is
/// the mover's own wind and a pure function of speed, so every bit of character has to come from
/// the curve, not the source. A source with its own motion would read as weather, and weather is
/// the world's (THRILL-BIBLE §10, the unreadable dark's weather/events carve-out).
/// </summary>
public static class SpeedWindSynth
{
    /// <summary>Matches the project's pinned mix rate and <see cref="SfxLab"/>'s render rate.</summary>
    public const int SampleRate = 48000;

    /// <summary>Body loop length. 4 s of pink noise is long enough that the loop is not learnable
    /// under the filter; 0.25 s of equal-power splice hides the wrap. 4 s × 48 kHz × 2 bytes =
    /// 384 kB resident, rendered in a few milliseconds.</summary>
    public const float BodySeconds = 4f;

    public const float HissSeconds = 3f;

    public const float SpliceSeconds = 0.25f;

    /// <summary>Peak amplitude the body is normalised to. Leaves headroom on the bus for the
    /// hiss to sit on top at <see cref="SpeedWindCurve.FullDb"/>.</summary>
    public const float BodyPeak = 0.7f;

    public const float HissPeak = 0.5f;

    /// <summary>Pink-ish noise (Kellet's three-pole economy approximation of 1/f) — broadband so
    /// the bus low-pass has something to open onto, tilted so an open filter is a roar rather
    /// than a scream.</summary>
    public static float[] RenderBody(int seed = 20260902)
    {
        var rng = new Random(seed);
        float b0 = 0f, b1 = 0f, b2 = 0f;
        float[] raw = LoopSplice.Render(SampleRate, BodySeconds, SpliceSeconds, (_, _) =>
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            b0 = 0.99765f * b0 + white * 0.0990460f;
            b1 = 0.96300f * b1 + white * 0.2965164f;
            b2 = 0.57000f * b2 + white * 1.0526913f;
            return (b0 + b1 + b2 + white * 0.1848f) * 0.25f;
        });
        Normalise(raw, BodyPeak);
        return raw;
    }

    /// <summary>White noise through two one-pole high-passes (~3 kHz): the air on top of the
    /// body, the layer that only joins past sprint.</summary>
    public static float[] RenderHiss(int seed = 20260903)
    {
        var rng = new Random(seed);
        const float cutoffHz = 3000f;
        float alpha = MathF.Exp(-2f * MathF.PI * cutoffHz / SampleRate);
        float x1 = 0f, y1 = 0f, y2 = 0f, z1 = 0f;
        float[] raw = LoopSplice.Render(SampleRate, HissSeconds, SpliceSeconds, (_, _) =>
        {
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            float y = alpha * (y1 + x - x1); // one-pole HP
            x1 = x;
            y1 = y;
            float z = alpha * (z1 + y - y2); // and again, 12 dB/oct
            y2 = y;
            z1 = z;
            return z;
        });
        Normalise(raw, HissPeak);
        return raw;
    }

    private static void Normalise(float[] samples, float peak)
    {
        float max = 0f;
        foreach (float s in samples)
            max = MathF.Max(max, MathF.Abs(s));
        if (max <= 0f)
            return;
        float g = peak / max;
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= g;
    }
}
