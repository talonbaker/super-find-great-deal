using System;
using Godot;
using MpFoundation.Game.World;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// Decides whose footsteps you may hear, once per frame, for the whole lobby — and how loud.
///
/// <b>Why this exists at all.</b> Canon item 4: away from light a player is near-blind and
/// <i>sound becomes the information channel</i>. Until 2026-08-13 only your OWN footsteps ever
/// sounded (<c>ActorEvent.Step</c> had exactly two call sites, both on locally-simulated paths),
/// so the most common signal in the game was missing from the channel the darkness law depends
/// on. Making remote players audible is the other half of this change — and it is also what makes
/// this half mandatory rather than nice-to-have.
///
/// <b>The arithmetic that forces a director.</b> The gait ran at 12.5 footfalls/s at sprint when
/// this was written (a fixed <c>StepHz</c> plus a run boost). MOVE-1 derives the cadence from ground
/// speed instead, and it now tops out at <c>LocomotionProfile.CadenceMaxHz</c> = 3.9 — so the
/// pressure is about a third of what it was, and the director is still right: six sprinting players
/// is 23 footfalls/s against a 14-voice pool. The shipped presentation profile answers
/// each footfall with TWO one-shots (step_pad + step_squeak). Six players sprinting and planting
/// on the same frame is 12 shots against <see cref="SfxLab.PoolSize"/> = 14 — footsteps alone
/// would hold 86% of the pool and start stealing voices from the glow-stick crack, the match
/// strike and (plan §7.3) creature voices, which is the pool those live in. Uncapped, "you can
/// hear the others move" would be paid for with "you can no longer hear the thing that matters".
///
/// <b>The priority rule, derived from canon rather than taste.</b> In the dark a cue that tells
/// you WHERE something is outranks decoration, and nearer outranks further. That is the whole
/// rule, and it is deliberately blind to everything else: THRILL-BIBLE §10's live device row says
/// audio "may state where a thing is, and may never state what it means", so nothing here reacts
/// to threat state, to who is speaking, or to what anyone is carrying. A mix that got quieter when
/// something was near would be a telegraph (§8.3) wearing an optimisation's clothes.
///
/// <b>Two devices, for two different boundaries.</b>
/// <list type="bullet">
/// <item>The RANK boundary (you are the 5th-nearest walker) is audible, so it gets hysteresis —
/// <see cref="RankHysteresisM"/>, the same device and the same value the since-removed fire audio
/// ranker used, through the same shared <see cref="NearestKAudio"/> selection.</item>
/// <item>The DISTANCE boundary gets a taper instead: <see cref="GainFor"/> holds full level out to
/// <see cref="FullVolumeM"/> and then falls linearly IN AMPLITUDE to exactly zero at
/// <see cref="HorizonM"/>, which is where the cull happens. So the cut always lands on silence —
/// there is no threshold at which a footstep can pop in or out, because at the threshold there is
/// no footstep. Hysteresis on this boundary would be redundant for the same reason.</item>
/// </list>
/// A one-shot cannot be cut mid-flight the way a loop can (the emitter's position is fixed at play
/// time and the clip is 50 ms), so the failure this guards is not a click — it is chatter, a
/// footstep that flickers as somebody walks the threshold, which reads as a broken game rather
/// than as distance.
///
/// <b>Per-client, never replicated</b>, exactly as the fire audio director was: which
/// footsteps you can hear depends on where you are standing, and computing it locally is both
/// correct and free. Nothing about it belongs on the wire.
///
/// <b>No listener means no culling, not silence.</b> The opposite of the fire audio director,
/// and deliberately: a fire that cannot be ranked is one continuous voice held open for nobody,
/// while a footstep that cannot be ranked is a 50 ms shot into a pool that steals gracefully. With
/// no camera resolved (an offline lab scene mid-boot, a tool) the honest failure is to spend the
/// pool, not to mute the world. A dedicated server never reaches here at all — <c>ActorFx.Fire</c>
/// early-outs on headless before any of this runs.
/// </summary>
public static class FootstepAudioDirector
{
    // --- The three tuning values. One named place each, so a feel pass is one number. ---------

    /// <summary>How many players' footsteps may sound at once, listener included.
    ///
    /// <b>Four, and the number is a budget rather than a preference.</b> Each granted stepper
    /// costs <see cref="ShotsPerFootfall"/> (2, read from the profile rather than assumed) one-shot
    /// slots at the instant they plant, so the worst case is 4 x 2 = 8 of
    /// <see cref="SfxLab.PoolSize"/> = 14, leaving 6 slots that footsteps can never take. That
    /// headroom is what item one-shots and plan
    /// §7.3's creature voices are spending. Six uncapped players would have been 12 of 14.
    ///
    /// Water's four-voice self-cap cannot stack on top of it in the worst case: a player is either
    /// thrashing in the lake or running on land, never both, so six players split any way you like
    /// still land at or under 8 + 4 = 12 with 2 slots spare.
    ///
    /// Four is also enough to hear: past two or three simultaneous sets of footsteps nobody can
    /// count them, and the ones dropped are always the FARTHEST, which are the ones carrying the
    /// least positional information.</summary>
    public const int AudibleStepperCap = 4;

    /// <summary>Full level out to here. Inside 18 m a footstep is information you can act on —
    /// which way somebody went, whether they are closing. Godot's own inverse-distance attenuation
    /// is already about 5 dB down at this range; this director's taper starts where that stops
    /// being enough on its own.</summary>
    public const float FullVolumeM = 18f;

    /// <summary>The cull. Beyond 32 m a footstep is not played at all, and
    /// <see cref="GainFor"/> has already brought it to exactly zero by the time it gets here, so
    /// nothing audible is ever removed.
    ///
    /// Chosen to sit INSIDE <c>SfxLab.PlayStream3D</c>'s 40 m emitter falloff, so this horizon is
    /// always the binding one and the engine's own hard cutoff at 40 m is unreachable for a
    /// footstep. A step that stops because the engine ran out of range would stop at roughly
    /// -31 dB, i.e. while still audible; a step that stops because of this constant stops at
    /// silence.</summary>
    public const float HorizonM = 32f;

    /// <summary>How much nearer a challenger must be to take a granted stepper's slot. Six metres,
    /// the same value and the same reasoning as the fire audio ranker's hysteresis:
    /// comfortably more than a player's per-frame movement at sprint, comfortably less than the
    /// spacing at which you would think of two people as being in different places. Larger than
    /// jitter, smaller than intent.</summary>
    public const float RankHysteresisM = 6f;

    // --- State --------------------------------------------------------------------------------

    private static ulong _lastTickFrame = ulong.MaxValue;

    /// <summary>One-shots the profile fires per footfall, measured off the loaded profile rather
    /// than assumed — the number is 2 today (step_pad + step_squeak) and it is data, not code.
    /// Zero until a profile has been seen; the budget readout says so rather than guessing.</summary>
    public static int ShotsPerFootfall { get; private set; }

    /// <summary>Footsteps played and culled since the last <see cref="ResetForTest"/>.
    /// Instrumentation, in the spirit of Issue #182: a budget nobody can see is a budget nobody
    /// can defend, and these two counters are what a self-test reads to prove a remote proxy is
    /// audible at all.</summary>
    public static long StepsPlayed { get; private set; }

    public static long StepsCulled { get; private set; }

    /// <summary>How many avatars hold a step grant right now. Never exceeds
    /// <see cref="AudibleStepperCap"/>.</summary>
    public static int GrantedSteppers
    {
        get
        {
            int n = 0;
            foreach (SandboxAvatar a in SandboxAvatar.Live)
            {
                if (GodotObject.IsInstanceValid(a) && a.StepAudioGranted)
                    n++;
            }
            return n;
        }
    }

    // --- The taper, as pure arithmetic --------------------------------------------------------

    /// <summary>Amplitude 0..1 for a footstep at this distance: 1 inside <see cref="FullVolumeM"/>,
    /// falling linearly in amplitude to exactly 0 at <see cref="HorizonM"/>, and 0 beyond.
    ///
    /// Linear in AMPLITUDE, not in dB, for the reason <c>LoopingSfxEmitter.GainToDb</c> already
    /// records: a fade linear in dB spends most of its length inaudible and then arrives suddenly,
    /// which gives a taper the perceptual shape of a cut.</summary>
    public static float GainFor(float distanceM)
    {
        if (!float.IsFinite(distanceM) || distanceM < 0f)
            return 0f;
        if (distanceM <= FullVolumeM)
            return 1f;
        if (distanceM >= HorizonM)
            return 0f;
        return 1f - (distanceM - FullVolumeM) / (HorizonM - FullVolumeM);
    }

    /// <summary>The taper as a dB trim to add to the profile's own volume. Exactly
    /// <see cref="SfxLab.SilentDb"/> at and past the horizon, so the value a culled step would
    /// have carried is true silence rather than something merely small.</summary>
    public static float TrimDbFor(float distanceM)
    {
        float gain = GainFor(distanceM);
        return gain <= 0f ? SfxLab.SilentDb : 20f * MathF.Log10(gain);
    }

    /// <summary>Worst-case one-shot slots footsteps can hold at once: every granted stepper
    /// planting on the same frame. Pure, so the budget claim in this class's doc is a test rather
    /// than a sentence.</summary>
    public static int WorstCaseOneShotSlots(int steppingPlayers, int shotsPerFootfall) =>
        Math.Max(0, Math.Min(steppingPlayers, AudibleStepperCap)) * Math.Max(0, shotsPerFootfall);

    // --- The per-frame grant ------------------------------------------------------------------

    /// <summary>Called by an avatar before it asks whether it may be heard. The first call in a
    /// given engine frame does the work; the rest are a comparison. Idempotent per frame rather
    /// than per avatar for the same reason the fire audio director's was:
    /// ranking once and granting a list is what makes the cap a cap, and re-ranking mid-frame
    /// would let a later avatar see a different world than an earlier one.</summary>
    public static void EnsureTicked(Node3D context)
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame == _lastTickFrame)
            return;
        _lastTickFrame = frame;
        Tick(ResolveListener(context));
    }

    /// <summary>Ranks and grants. <paramref name="listener"/> null means "nobody can be ranked" —
    /// see the class doc for why that grants everything rather than nothing.
    ///
    /// Public so a self-test can drive it from a known listener position without a camera; a
    /// headless run has no camera by construction, so a director only reachable through one would
    /// be a director no test could exercise.</summary>
    public static void Tick(Vector3? listener)
    {
        _lastTickFrame = Engine.GetProcessFrames();

        System.Collections.Generic.List<SandboxAvatar> live = SandboxAvatar.Live;
        int n = live.Count;
        if (n == 0)
            return;

        if (listener == null)
        {
            for (int i = 0; i < n; i++)
            {
                if (GodotObject.IsInstanceValid(live[i]))
                    live[i].SetStepAudioGrant(true, 0f);
            }
            return;
        }

        // Allocated per tick rather than pooled, exactly as FireAudioDirector.Tick does: n is the
        // lobby size, this runs once per rendered frame (not once per footfall), and
        // NearestKAudio.Select allocates its own working set regardless — caching here would buy
        // nothing measurable and cost a second place for the length to go wrong.
        var d = new float[n];
        var inc = new bool[n];
        for (int i = 0; i < n; i++)
        {
            SandboxAvatar a = live[i];
            // An avatar that is not walking is not a candidate at all. Without this, three
            // teammates standing still beside you would hold every slot and the one person
            // actually moving — the only one producing information — would be the one culled.
            if (!GodotObject.IsInstanceValid(a) || !a.IsInsideTree() || !a.StepAudioCandidate)
            {
                d[i] = float.PositiveInfinity;
                inc[i] = false;
                continue;
            }
            d[i] = a.GlobalPosition.DistanceTo(listener.Value);
            inc[i] = a.StepAudioGranted;
        }

        bool[] grant = NearestKAudio.Select(d, inc, AudibleStepperCap, RankHysteresisM, HorizonM);

        for (int i = 0; i < n; i++)
        {
            SandboxAvatar a = live[i];
            if (!GodotObject.IsInstanceValid(a))
                continue;
            float gain = GainFor(d[i]);
            // Granted AND audible. A grant at zero gain would spend a slot on silence, which is
            // the exact waste this director exists to stop.
            a.SetStepAudioGrant(grant[i] && gain > 0f, TrimDbFor(d[i]));
        }
    }

    /// <summary>The avatar's question: may this footfall sound, and how much quieter than the
    /// profile says? Counts both answers so the channel is observable.</summary>
    public static bool TryTakeStep(SandboxAvatar avatar, out float trimDb)
    {
        trimDb = avatar.StepAudioTrimDb;
        if (!avatar.StepAudioGranted)
        {
            StepsCulled++;
            return false;
        }
        StepsPlayed++;
        return true;
    }

    /// <summary>Records how many one-shots this profile answers a footfall with, so the budget
    /// figure is measured off the shipped data. Called by the avatar the first time it fires a
    /// step; a third layer added to the .tres therefore shows up in the readout instead of
    /// silently costing 50% more of the pool.</summary>
    public static void NoteShotsPerFootfall(int shots)
    {
        if (shots > ShotsPerFootfall)
            ShotsPerFootfall = shots;
    }

    /// <summary>The active camera's position, or null. Godot mixes 3D audio against the current
    /// <c>Camera3D</c> unless an <c>AudioListener3D</c> is made current, and this class does not
    /// introduce one — the fire audio director made the same call, and a second listener
    /// authority is exactly the kind of thing that silently disagrees with the first.</summary>
    private static Vector3? ResolveListener(Node3D context)
    {
        if (!GodotObject.IsInstanceValid(context) || !context.IsInsideTree())
            return null;
        Camera3D? cam = context.GetViewport()?.GetCamera3D();
        return cam != null && GodotObject.IsInstanceValid(cam) ? cam.GlobalPosition : null;
    }

    /// <summary>Clears the frame guard and the counters. Test-fixture plumbing only — a director
    /// that carried a previous check's frame stamp into the next one is how a cap assertion passes
    /// for the wrong reason.</summary>
    public static void ResetForTest()
    {
        _lastTickFrame = ulong.MaxValue;
        StepsPlayed = 0;
        StepsCulled = 0;
        ShotsPerFootfall = 0;
    }
}
