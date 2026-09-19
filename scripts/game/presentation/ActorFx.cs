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
                SfxLab.PlayStream3D(context, position, stream,
                    r.VolumeDb + r.IntensityVolumeBoostDb * intensity + volumeTrimDb, r.PitchJitter);

            if (r.PuffCount > 0)
                JuiceFx.Puff(context, position, r.PuffCount, r.PuffColor,
                    r.PuffSize, r.PuffSpeed, r.PuffLifetime);
        }
    }
}
