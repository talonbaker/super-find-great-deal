using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.Presentation;

/// <summary>THE shared call site between gameplay events and cosmetic response. A
/// controller reports WHAT HAPPENED (<see cref="ActorEvent"/> + position + intensity);
/// the actor's <see cref="PresentationProfile"/> decides what plays; the existing pooled
/// primitives (<see cref="SfxLab"/>, <see cref="JuiceFx"/>) do the
/// playing. Controllers never call SfxLab/JuiceFx directly for actor events again —
/// that coupling is what this class exists to remove.
///
/// Performance contract (spec §3): zero allocation steady-state — the profile's index
/// is prebuilt, responses iterate in place, no LINQ/closures — and a headless instance
/// (dedicated server / CI) early-outs before touching the audio pool.</summary>
public static class ActorFx
{
    private static bool _warnedNullProfile;

    /// <summary><paramref name="context"/> anchors playback in the scene tree — pass a
    /// world-static node (callers use GetParent()), NOT the moving actor itself, or
    /// particle bursts would ride along with it. <paramref name="intensity"/> is the
    /// 0..1 "how hard" (run-vs-walk step, fall severity); it gates responses below
    /// their MinIntensity and scales volume by IntensityVolumeBoostDb.
    ///
    /// <paramref name="volumeTrimDb"/> is a caller-owned attenuation added on top of whatever the
    /// profile says, and it is deliberately NOT the same dial as intensity: intensity is a
    /// property of the EVENT (how hard the foot landed) and the profile is entitled to interpret
    /// it per response, while the trim is a property of the LISTENER (how far away they are) and
    /// applies uniformly to every response the event fires. Conflating them would have let a
    /// distant footstep still trip a MinIntensity gate and spawn its dust.
    /// <c>FootstepAudioDirector</c> is the first caller; it passes its distance taper here.</summary>
    public static void Fire(Node context, PresentationProfile? profile,
        ActorEvent evt, Vector3 position, float intensity = 0f, float volumeTrimDb = 0f)
    {
        // Null-profile check comes first: it's a free reference compare, and warning
        // here means an unassigned profile gets diagnosed even on a headless server
        // (the guard below used to skip this entirely on servers).
        if (profile == null)
        {
            if (!_warnedNullProfile)
            {
                _warnedNullProfile = true;
                GD.PushWarning($"ActorFx.Fire: null profile (first offender: {evt} from '{context.Name}') — actor plays nothing until one is assigned");
            }
            return;
        }
        // Still load-bearing in production: a dedicated server must not pay for pool
        // players or particle nodes it can never render/hear. This runs before any
        // pool/particle work, same as before the null-profile check moved above it.
        if (NetworkManager.Instance == null || NetworkManager.Instance.IsHeadless)
            return;

        FireCore(context, profile, evt, position, intensity, volumeTrimDb);
    }

    /// <summary>The actual response loop — MinIntensity gate, CustomSound-over-Sound
    /// pick, the intensity volume formula, and puff spawning. Same body <see cref="Fire"/>
    /// ran inline before this extraction; behavior is unchanged.
    ///
    /// internal, not private: Fire's headless early-out above is real production
    /// behavior (a dedicated server must not touch the audio pool or spawn particles),
    /// but it also means Fire is a no-op under the headless test harness every
    /// self-test runs in — there is no way to observe this loop's actual behavior by
    /// calling Fire headless. PresentationSelfTest.RunFireCore calls this directly
    /// (same assembly, no reflection needed) to exercise the real response logic.</summary>
    internal static void FireCore(Node context, PresentationProfile profile,
        ActorEvent evt, Vector3 position, float intensity, float volumeTrimDb = 0f)
    {
        EventResponse[] responses = profile.ResponsesFor(evt);
        for (int i = 0; i < responses.Length; i++)
        {
            EventResponse r = responses[i];
            if (intensity < r.MinIntensity)
                continue;

            AudioStream? stream = r.CustomSound ?? (r.Sound != Sfx.None ? SfxLab.Get(r.Sound) : null);
            if (stream != null)
            {
                SfxLab.PlayStream3D(context, position,
                    stream,
                    VolumeDbFor(r.VolumeDb, r.IntensityVolumeBoostDb, intensity, volumeTrimDb),
                    r.PitchJitter,
                    pitchScale: PitchScaleFor(r.IntensityPitchRange, intensity));
                Fires++;
                SfxLab.SamplePeak();
                if (LogSfx)
                {
                    GD.Print($"[sfx] sfx {r.Sound} event={evt} intensity={intensity:F3} "
                        + $"at ({position.X:F2},{position.Y:F2},{position.Z:F2}) src={context.Name}");
                }
            }

            if (r.PuffCount > 0)
                JuiceFx.Puff(context, position, r.PuffCount, r.PuffColor,
                    r.PuffSize, r.PuffSpeed, r.PuffLifetime);
        }
    }

    // --- The intensity mapping, as arithmetic a Godot-free test can reach (SFX-1) -------------
    //
    // Both of these were expressions inlined in FireCore. They are named functions now for one
    // reason: FireCore needs a live AudioStream and a Node in a tree, so nothing in tests/unit can
    // call it, and "intensity is monotonic and clamped" is a gate SFX-1 owes. Free statics over
    // primitives rather than methods on EventResponse, because EventResponse is a Godot Resource
    // and constructing one needs the native engine the unit suite does not have.

    /// <summary>The final volume in dB: the response's own level, plus its intensity boost scaled
    /// by a CLAMPED intensity, plus the caller's listener trim. The clamp is what makes this
    /// monotonic-and-bounded rather than merely monotonic — an intensity of 4 must not be four
    /// times as loud as an intensity of 1, and a negative one must not invert the boost.</summary>
    public static float VolumeDbFor(float volumeDb, float intensityBoostDb, float intensity, float trimDb = 0f)
        => volumeDb + intensityBoostDb * Mathf.Clamp(intensity, 0f, 1f) + trimDb;

    /// <summary>The deliberate pitch multiplier for a hit of this intensity:
    /// <c>1 + range × clamp(i)</c>, floored at a hard 0.25 so a pathological authored range can
    /// never produce a zero or negative pitch scale (which is silence or a reversed stream, not a
    /// quiet sound).</summary>
    public static float PitchScaleFor(float intensityPitchRange, float intensity)
        => Mathf.Max(0.25f, 1f + intensityPitchRange * Mathf.Clamp(intensity, 0f, 1f));

    // --- --log-sfx instrumentation (SFX-1) ----------------------------------------------------

    /// <summary><c>--log-sfx</c>: print one line per sound actually played. Set once at boot from
    /// <c>LaunchOptions</c> rather than read per fire, because this sits inside the per-footstep
    /// hot path that <see cref="FireCore"/>'s doc promises is allocation-free, and a
    /// <c>NetworkManager.Instance?.Options</c> walk per sound is a property chain in that path for
    /// a flag no shipped launch ever sets.</summary>
    public static bool LogSfx { get; set; }

    /// <summary>How many sounds this class has actually played since the process started — the
    /// denominator for <c>SfxLab.OneShotSteals</c>. Counts sounds, not events: an event whose
    /// response is particle-only, or which every response's MinIntensity gated away, adds
    /// nothing.</summary>
    public static long Fires { get; private set; }

    /// <summary>The one line a <c>--log-sfx</c> run prints at exit. Assembled here rather than at
    /// the call site so the harness does not have to know which counters exist.</summary>
    public static string BudgetSummaryLine()
        => $"[sfx] SUMMARY fires={Fires} peakLive3DVoices={SfxLab.PeakLive3DVoices} "
            + $"oneShotSteals={SfxLab.OneShotSteals} poolSize={SfxLab.PoolSize} "
            + $"ceiling={AudioVoiceBudget.Ceiling}";
}
