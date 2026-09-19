using Godot;

namespace Sail.Game.Water.Fx;

/// <summary>
/// Every number packet W4 runs on, and the pure functions over them. No node, no scene, no
/// audio device, no RNG — which is what makes the whole splash/audio budget provable by a
/// headless test rather than asserted in a PR body.
///
/// <b>The authorities.</b> <c>docs/superpowers/specs/2026-08-08-lake-water-contract-design.md</c>
/// §9 and §9.1 (the five events and the budget), and
/// <c>docs/superpowers/direction/2026-08-08-lake-direction.md</c> §5, §10.1–§10.3 (the register,
/// the routing and the parameter pairs). Where either names a value it is reproduced verbatim;
/// where one was left to this packet the value is stated in the member's own doc along with why
/// it was picked (project CLAUDE.md's ambiguity rule: pick a value, state it, move on).
///
/// <b>The one design fact that shapes this whole file.</b> There is ONE event set. Direction §5
/// is explicit: night is the identical event with the release withheld, produced by subtraction,
/// never by a second preset. So there is no <c>NightClipFor</c> anywhere below, and the only
/// things <see cref="AmbientBedMath"/>-style night weight is allowed to touch are COUNT, LIFETIME
/// and EMISSION. Size is deliberately left out of that list — see <see cref="ParticleScale"/>.
/// </summary>
public static class WaterFxTuning
{
    // --- Distance culling (spec §9.1) ---------------------------------------------------------

    /// <summary>Beyond this from the local camera, a burst is not emitted at all (spec §9.1:
    /// "a splash you cannot see is not emitted"). Measured to the event position, not to the
    /// avatar — the event is where the water breaks.</summary>
    public const float ParticleCullM = 35f;

    /// <summary>Audio's own hard cull, deliberately WIDER than the particle cull. Sound crosses
    /// open water further than a handful of white specks resolve, and a splash you can hear from
    /// the far shore but not see is honest rather than a bug. Set equal to
    /// <see cref="AudioMaxDistanceM"/> so the cut lands exactly where the attenuation has already
    /// reached silence — culling somewhere audible would be the click this avoids.</summary>
    public const float AudioCullM = 45f;

    /// <summary><see cref="AudioStreamPlayer3D.MaxDistance"/> for every water one-shot.</summary>
    public const float AudioMaxDistanceM = 45f;

    // --- Burst budget (spec §9.1) -------------------------------------------------------------

    /// <summary>Hard ceiling per burst. Spec §9.1's number, and <see cref="BurstCount"/> is
    /// clamped to it on every path including the pathological ones.</summary>
    public const int MaxParticlesPerBurst = 64;

    /// <summary>Hard ceiling on simultaneously-live bursts across ALL players. Spec §9.1's
    /// number. Enforced by <see cref="SplashParticlePool"/>'s fixed allocation, so it is a
    /// structural property rather than a check that could be forgotten at a call site.</summary>
    public const int MaxConcurrentBursts = 6;

    /// <summary>
    /// Water's self-imposed share of <c>SfxLab</c>'s 14-slot one-shot pool, in simultaneously
    /// sounding water voices.
    ///
    /// <b>This is the voice-starvation answer, and it is worth stating exactly.</b> Proximity
    /// voice does not live in that pool — <c>VoiceSpeaker</c> owns its own
    /// <c>AudioStreamPlayer3D</c> per remote peer, outside <c>SfxLab</c> entirely. So water can
    /// never literally take a speaker's node; the only thing it could do is push the *total*
    /// concurrent 3D voice count past the written ≤24 ceiling. It cannot: every water sound goes
    /// through <c>SfxLab.PlayStream3D</c>, whose pool is a fixed 14 that cannot grow, and this
    /// packet adds NO continuous positional emitter (see <see cref="LapLoopVoices"/>). The count
    /// after W4 lands is therefore still 5 speakers + 14 pool slots = 19 of 24, unchanged.
    ///
    /// Four rather than fourteen because the remaining hazard is not the ceiling but pool
    /// CONTENTION: a six-player thrash could otherwise hold all 14 slots and steal every footstep
    /// and shutter click in the camp. Four leaves ten for everything else, and a fifth
    /// simultaneous splash simply does not sound — which no ear can detect and which is far
    /// better than the alternative of it stealing something a player was listening to.
    /// </summary>
    public const int MaxConcurrentWaterVoices = 4;

    /// <summary>
    /// Continuous positional emitters this packet adds: zero, and that is a decision.
    ///
    /// Direction §10.1 permits "at most one" shoreline lap loop and flags the arithmetic that
    /// makes it dangerous — 5 free 3D slots against exactly 5 possible remote chatter voices in a
    /// six-player lobby. A lap loop would take the last one. Declining it costs the lake a
    /// texture it has never had and keeps the free five free for the chill cue's remote chatter,
    /// which is a fairness channel (LEVEL-BIBLE §8.2) and outranks ambience. Recorded here rather
    /// than merely omitted, because "we forgot the lap" and "we refused the lap" look identical
    /// in a diff.
    /// </summary>
    public const int LapLoopVoices = 0;

    // --- Per-event particle counts (day, before night subtraction) ----------------------------
    //
    // Every one of these is at or under half the 64 ceiling at its day value, so the ceiling is a
    // guard rather than the working number. Values are this packet's (the spec named the ceiling,
    // not the counts) and are shaped by what the event IS, not by how important it feels.

    /// <summary>An entry at a standing step — a foot going in. The floor of the entry ramp.</summary>
    public const int EntryMinParticles = 12;

    /// <summary>An entry at <see cref="EntrySpeedRefMps"/> or above — the running jump off the
    /// dock. The top of the entry ramp, and the biggest burst in the feature.</summary>
    public const int EntryMaxParticles = 48;

    /// <summary>Horizontal speed at which an entry burst reaches
    /// <see cref="EntryMaxParticles"/>. The avatar's sprint is well under this, so a genuine
    /// running jump (sprint plus the fall) sits at the top of the ramp and a walk-in sits near
    /// the bottom, which is exactly the read spec §9 asks for.</summary>
    public const float EntrySpeedRefMps = 6.0f;

    /// <summary>One thrash. Small on purpose: W2 fires these every 0.45 s while moving, so this
    /// is a texture that repeats, not an event. A thrash the size of an entry would make the
    /// entry mean nothing.</summary>
    public const int ThrashParticles = 20;

    /// <summary>The swallow — the collapsing ring and the bubbles. The second-biggest burst, and
    /// the only one that is big because of what it MEANS rather than because of physics.</summary>
    public const int WentUnderParticles = 36;

    /// <summary>The shed of water at the shore when the recovery lands.</summary>
    public const int SputterParticles = 18;

    /// <summary>Drips and the shake-off on the way out.</summary>
    public const int ExitParticles = 10;

    // --- Per-event lifetimes, seconds (day, before night subtraction) -------------------------

    public const float EntryLifetimeSec = 0.95f;
    public const float ThrashLifetimeSec = 0.55f;
    public const float WentUnderLifetimeSec = 1.25f;
    public const float SputterLifetimeSec = 0.80f;
    public const float ExitLifetimeSec = 0.70f;

    // --- The night subtraction (direction §10.3) ----------------------------------------------

    /// <summary>Night multiplier on particle COUNT. Direction §10.3: "A big slow splash reads
    /// comic; a small short one reads like the water took it." 0.45 rather than something
    /// gentler because the row is the whole register difference and a timid subtraction produces
    /// a night splash that reads as a day splash rendered badly.</summary>
    public const float NightCountScale = 0.45f;

    /// <summary>Night multiplier on particle LIFETIME. Slightly less severe than the count: a
    /// splash that vanishes instantly reads as a rendering fault rather than as curt.</summary>
    public const float NightLifetimeScale = 0.55f;

    /// <summary>Emission energy on the splash material by day. Splash droplets catch the sun, and
    /// a little emissive lift is what keeps white specks reading as water rather than as dust.</summary>
    public const float DayEmissionEnergy = 0.55f;

    /// <summary>
    /// The hard night emission cap (direction §10.3, "Cap it, hard").
    ///
    /// The reasoning is not aesthetic. W3 spent its entire shader budget making the night water
    /// unresolvable; splash particles are the only lit thing in that frame, and an uncapped burst
    /// is a flare that resolves the dark the shader just paid for.
    /// </summary>
    public const float NightEmissionEnergy = 0.10f;

    // --- The other half of the night cap, and it is the half that nearly got away -----------------
    //
    // The droplet material is UNSHADED, deliberately: the lake gets no light from the camp core
    // and W3's night grade is close to lightless, so a lit droplet at night would render black and
    // the splash would be missing rather than curt. But unshaded means the albedo IS the
    // brightness — the scene's lighting never touches it — so capping emission alone caps almost
    // nothing. The first headed night capture came back with a stark white cluster as the
    // brightest object in the frame by a wide margin, which is precisely the flare §10.3 forbids,
    // and every headless check was green because the emission number was correct.
    //
    // So the night dial reaches albedo too. Both numbers below are the cap; neither alone is.

    /// <summary>Droplet albedo value by day — near-white spray in sunlight.</summary>
    public const float DayAlbedoValue = 0.88f;

    /// <summary>
    /// Droplet albedo value at full night. Roughly twice the value W3's night water sits at, so
    /// the splash is a legible shape against it and nothing more.
    ///
    /// It is allowed to be hard to resolve. Direction §8.5 draws the line between *unresolvable*
    /// (sparse hard marks on a dark field — the eye keeps trying, and that is the wanted state)
    /// and *illegible noise* (dense fine sparkle — the eye gives up). A splash is sparse and hard
    /// by construction, so dimming it moves it toward the good side of that line rather than the
    /// bad one. It also costs no fairness: the fairness channels here are the rope and the three
    /// chill cues, and the splash is not one of them.
    /// </summary>
    public const float NightAlbedoValue = 0.34f;

    public const float DayAlbedoAlpha = 0.85f;

    /// <summary>Thinner at night as well as darker. Spray you can partly see the water through
    /// reads as a smaller event, which is the same subtraction count and lifetime are making.</summary>
    public const float NightAlbedoAlpha = 0.62f;

    /// <summary>Particle world size, metres. <b>Deliberately not night-scaled.</b> Direction
    /// §10.3 names this as the trap: "Size is silhouette; count and lifetime are duration.
    /// Changing the wrong one makes the night splash look cheap rather than curt." The function
    /// below takes no night weight at all, which is how that instruction is enforced rather than
    /// merely remembered — see <c>NightNeverTouchesParticleSize</c>.</summary>
    public const float ParticleScale = 0.075f;

    // --- Audio levels, dB ----------------------------------------------------------------------
    //
    // Nothing in this repo's audio has ever been mixed by ear on the floor spec, and these
    // inherit that honestly. They are ordered by how much each event should matter, which is the
    // part that is a design call rather than a mix call.

    public const float EntryQuietDb = -16f;
    public const float EntryLoudDb = -6f;
    public const float ThrashDb = -14f;
    public const float WentUnderDb = -8f;
    public const float SputterDb = -9f;
    public const float ExitDb = -15f;

    /// <summary>Per-shot pitch variation. Matched to <c>SparseSfxEmitter</c>'s 0.22 rather than
    /// <c>SfxLab</c>'s 0.08 default for the same reason that emitter states: the thrash stream
    /// repeats every 0.45 s, and the same splash at the same pitch twice running is precisely
    /// what gives a synthesised layer away.</summary>
    public const float PitchJitter = 0.22f;

    // --- The submersion filter (direction §10.2) -----------------------------------------------

    /// <summary>Filter cutoff when nothing is submerged — effectively transparent. Sitting at the
    /// open end rather than bypassing lets the effect be enabled and disabled at a point where it
    /// is doing nothing, which is what makes the switch inaudible.</summary>
    public const float FilterOpenHz = 20500f;

    /// <summary>Cutoff at full submersion. Muffled, never silenced (direction §10.2: "Under the
    /// surface the world is filtered, not silenced. The bed keeps playing. Silence is
    /// reserved."). 700 Hz keeps speech intelligible and the bed present while removing every
    /// bit of air off the top, which is what being underwater actually does.</summary>
    public const float FilterSubmergedHz = 700f;

    /// <summary>Seconds for the filter to close as the body goes under. Matched to
    /// <see cref="WaterGeometry.GoUnderSec"/> so the sweep and the sinking are the same
    /// event — direction §10.2: sweep, never snap.</summary>
    public const float FilterCloseSec = WaterGeometry.GoUnderSec;

    /// <summary>Seconds to open back up once the recovery lands. Faster than the close, because
    /// the return of the world is the release and a slow one reads as a lingering penalty —
    /// direction §5's day exemption is a COMPLETE all-clear, and a filter still half shut two
    /// seconds later is residue.</summary>
    public const float FilterOpenSec = 0.45f;

    // --- Pure functions -----------------------------------------------------------------------

    /// <summary>
    /// How many particles a burst emits. Total by construction: a hostile or garbage speed off
    /// the wire yields a legal small burst, never a NaN emit count or a request for four billion
    /// particles.
    /// </summary>
    /// <param name="kind">Which of the five events.</param>
    /// <param name="speed">Horizontal speed at the event, m/s. Only <c>Entered</c> reads it.</param>
    /// <param name="nightWeight">0 = full day, 1 = full night. From
    /// <c>AmbientBedMath.NightWeight</c> — the shipped bands, never a second set.</param>
    public static int BurstCount(WaterEventKind kind, float speed, float nightWeight)
    {
        int day = kind switch
        {
            WaterEventKind.Entered => EntryRamp(speed),
            WaterEventKind.Splash => ThrashParticles,
            WaterEventKind.WentUnder => WentUnderParticles,
            WaterEventKind.Sputtered => SputterParticles,
            WaterEventKind.Exited => ExitParticles,
            _ => ExitParticles, // unreachable through the enum, reachable through a corrupt byte
        };
        float scale = Mathf.Lerp(1f, NightCountScale, Clamp01(nightWeight));
        // Ceil, not round: the night subtraction must never take a burst all the way to zero.
        // An event that emits nothing is indistinguishable from an event that never fired, and
        // the go-under in particular has to remain a visible thing that happened.
        int scaled = Mathf.CeilToInt(day * scale);
        return Mathf.Clamp(scaled, 1, MaxParticlesPerBurst);
    }

    /// <summary>The entry ramp: a step throws little, a running jump throws a lot (spec §9).
    /// Linear between a standstill and <see cref="EntrySpeedRefMps"/>, flat above it — a faster
    /// entry than a sprinting jump is not physically reachable and letting the ramp keep climbing
    /// would put the ceiling clamp on the live path instead of the guard path.</summary>
    private static int EntryRamp(float speed)
    {
        if (!float.IsFinite(speed) || speed <= 0f)
            return EntryMinParticles;
        float t = Mathf.Clamp(speed / EntrySpeedRefMps, 0f, 1f);
        return Mathf.RoundToInt(Mathf.Lerp(EntryMinParticles, EntryMaxParticles, t));
    }

    /// <summary>Particle lifetime in seconds. Night shortens it (direction §10.3), floored so a
    /// burst is always long enough to be seen at all.</summary>
    public static float BurstLifetimeSec(WaterEventKind kind, float nightWeight)
    {
        float day = kind switch
        {
            WaterEventKind.Entered => EntryLifetimeSec,
            WaterEventKind.Splash => ThrashLifetimeSec,
            WaterEventKind.WentUnder => WentUnderLifetimeSec,
            WaterEventKind.Sputtered => SputterLifetimeSec,
            WaterEventKind.Exited => ExitLifetimeSec,
            _ => ExitLifetimeSec,
        };
        float scale = Mathf.Lerp(1f, NightLifetimeScale, Clamp01(nightWeight));
        return Mathf.Max(0.15f, day * scale);
    }

    /// <summary>
    /// Splash material emission energy. Takes ONLY the night weight — half of direction §10.3's
    /// hard cap. The other half is <see cref="DropletTint"/>, and on an unshaded material that
    /// half is the larger one.
    /// </summary>
    public static float EmissionEnergy(float nightWeight) =>
        Mathf.Lerp(DayEmissionEnergy, NightEmissionEnergy, Clamp01(nightWeight));

    /// <summary>
    /// The droplet's albedo at a given night weight — the other half of the cap. Carries a faint
    /// cool cast (a hair of blue over white) so spray reads as water rather than as the dust motes
    /// the atmosphere layer already puts in the air. ART-BIBLE §4.4 is still blank, so this is a
    /// stated pick and not a claim to satisfy a material law.
    /// </summary>
    public static Color DropletTint(float nightWeight)
    {
        float w = Clamp01(nightWeight);
        float v = Mathf.Lerp(DayAlbedoValue, NightAlbedoValue, w);
        float a = Mathf.Lerp(DayAlbedoAlpha, NightAlbedoAlpha, w);
        return new Color(v, Mathf.Min(1f, v * 1.07f), Mathf.Min(1f, v * 1.14f), a);
    }

    /// <summary>
    /// Particle world size. Takes no night weight, on purpose and by contract — direction §10.3
    /// forbids shrinking size to compensate for the count/lifetime cut. A function with no night
    /// parameter cannot be made to violate that by a later edit that only touches a constant.
    /// </summary>
    public static float ParticleSizeM(WaterEventKind kind) => kind switch
    {
        // The go-under's ring and bubbles read at a slightly larger grain than spray does.
        WaterEventKind.WentUnder => ParticleScale * 1.4f,
        // Drips are droplets, not spray.
        WaterEventKind.Exited => ParticleScale * 0.8f,
        _ => ParticleScale,
    };

    /// <summary>Initial particle speed, m/s — how hard the water is thrown. This is the other
    /// half of "a running jump throws more than a step": the entry burst is not only bigger, it
    /// goes further. Night does not touch it; velocity is silhouette in motion, and cutting it
    /// would be the same mistake as shrinking size.</summary>
    public static float BurstVelocityMps(WaterEventKind kind, float speed)
    {
        float baseline = kind switch
        {
            WaterEventKind.Entered => 2.2f,
            WaterEventKind.Splash => 1.5f,
            WaterEventKind.WentUnder => 1.1f, // a collapse inward, not a throw outward
            WaterEventKind.Sputtered => 1.3f,
            WaterEventKind.Exited => 0.9f,
            _ => 1.0f,
        };
        if (kind != WaterEventKind.Entered)
            return baseline;
        if (!float.IsFinite(speed) || speed <= 0f)
            return baseline;
        return baseline * Mathf.Lerp(0.7f, 1.9f, Mathf.Clamp(speed / EntrySpeedRefMps, 0f, 1f));
    }

    /// <summary>One-shot volume in dB. Only the entry scales with speed; the rest are fixed,
    /// because a thrash that got louder the faster you flailed would turn the throttled stream
    /// into a slider the player can hold down.</summary>
    public static float VolumeDb(WaterEventKind kind, float speed)
    {
        if (kind != WaterEventKind.Entered)
        {
            return kind switch
            {
                WaterEventKind.Splash => ThrashDb,
                WaterEventKind.WentUnder => WentUnderDb,
                WaterEventKind.Sputtered => SputterDb,
                WaterEventKind.Exited => ExitDb,
                _ => ThrashDb,
            };
        }
        if (!float.IsFinite(speed) || speed <= 0f)
            return EntryQuietDb;
        return Mathf.Lerp(EntryQuietDb, EntryLoudDb,
            Mathf.Clamp(speed / EntrySpeedRefMps, 0f, 1f));
    }

    /// <summary>Whether a burst at this squared distance from the local camera is emitted at
    /// all. Squared throughout — a per-event square root for a threshold test is the kind of
    /// cost that only looks free until six players are thrashing.</summary>
    public static bool ParticlesVisibleAt(float distanceSq) =>
        float.IsFinite(distanceSq) && distanceSq <= ParticleCullM * ParticleCullM;

    /// <summary>Whether a one-shot at this squared distance is worth a pool slot.</summary>
    public static bool AudibleAt(float distanceSq) =>
        float.IsFinite(distanceSq) && distanceSq <= AudioCullM * AudioCullM;

    /// <summary>
    /// The submersion filter's cutoff at a given submersion amount in [0,1]. Exponential in the
    /// frequency domain rather than linear, because hearing is: a linear sweep from 20 kHz spends
    /// almost all its travel in a band the ear reads as "unchanged" and then collapses at the
    /// very end, which is the snap direction §10.2 forbids wearing a sweep's clothes.
    /// </summary>
    public static float FilterCutoffHz(float submersion)
    {
        float s = Clamp01(submersion);
        float logOpen = Mathf.Log(FilterOpenHz);
        float logShut = Mathf.Log(FilterSubmergedHz);
        return Mathf.Exp(Mathf.Lerp(logOpen, logShut, s));
    }

    private static float Clamp01(float v) =>
        float.IsFinite(v) ? Mathf.Clamp(v, 0f, 1f) : 0f;
}
