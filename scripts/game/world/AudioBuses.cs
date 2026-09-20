using Godot;
using MpFoundation.Voice;

namespace MpFoundation.Game.World;

/// <summary>
/// The one place the game's audio bus tree is declared. Before this existed the routing was
/// genuinely ad hoc — four separate classes each grew their own private <c>EnsureBus</c>
/// (<see cref="Sandbox.SfxLab"/> "Sfx", <see cref="VoiceManager"/> "Voice" and "PA",
/// <see cref="VoiceCapture"/> "VoiceCapture", <see cref="AmbientBed"/> "Ambient"), there is no
/// <c>default_bus_layout.tres</c> anywhere in the project, and nothing could say what the mix
/// looked like without reading five files. Nothing was *wrong*; there was simply no layout, and
/// a mix with no layout cannot duck one group under another.
///
/// <b>The tree.</b> Only the ambience side is owned here. The voice and one-shot buses keep
/// their existing owners — this class names them so the tree reads in one place, and calls
/// their owners' own creators rather than racing them:
///
/// <code>
/// Master
/// ├── Sfx              (SfxLab's pooled one-shots: gameplay feedback — footsteps, shutter, bonk)
/// ├── Voice            (VoiceSpeaker's proximity output; read here as a duck DETECTOR only)
/// ├── PA               (VoiceManager's intercom chain — untouched)
/// ├── VoiceCapture     (mic capture; never routed to output)
/// └── Ambient          (the group handle: one player slider, one place to trim the world)
///     ├── Ambient_Bed      (AudioEffectCompressor, sidechained to Voice) — the continuous bed
///     └── Ambient_Scenery  (no effects) — sparse positional scenery one-shots
/// </code>
///
/// <b>Why the bed and the scenery are separate lanes.</b> They want opposite treatment under a
/// duck. The bed is continuous and low, so pulling it down under a speaking teammate is free —
/// nobody can name a level change in a sound they had stopped hearing. The scenery layer is
/// transients (a crackle, a bird), and Godot's compressor tops out at a 2000 µs attack, so a
/// sidechain across the scenery lane would clamp every crackle onto every syllable and read as
/// pumping. So the compressor sits on the bed lane alone and the scenery lane runs clean. Both
/// still sit under <c>Ambient</c>, which is what makes them one slider and one group.
///
/// <b>What this class does NOT decide.</b> Whether the bed should duck at all is a mix/affect
/// call that belongs to <c>vfx-audio-sync</c>, not to bus plumbing — and there is a real
/// argument against it, recorded here rather than resolved: in a six-player proximity-voice
/// session somebody is nearly always talking, so a sidechain keyed to Voice is close to a
/// permanent level cut rather than an event. The duck is therefore built SHALLOW (see
/// <see cref="DuckThresholdDb"/>) and switchable in one call
/// (<see cref="SetDuckEnabled"/>) so the question can be answered by standing in it.
///
/// <b>Idempotency is the whole contract.</b> A non-idempotent bus builder stacks duplicate
/// effects silently and cumulatively — the compressor gets added twice, then four times, and
/// the mix quietly collapses. <see cref="EnsureLayout"/> is safe to call any number of times
/// from any number of callers: bus creation checks by name, and every effect is placed through
/// <see cref="EnsureEffect{T}"/>, which checks the slot before adding. Deliberately NOT a
/// process-static "already built" bool — that shape passes an idempotency test without proving
/// anything, because the second call never reaches the code the test is trying to check.
/// </summary>
public static class AudioBuses
{
    /// <summary>Godot's own bus 0. Named, never created.</summary>
    public const string Master = "Master";

    /// <summary>Created and owned by <see cref="Sandbox.SfxLab"/>; named here for the tree.</summary>
    public const string Sfx = Sandbox.SfxLab.Bus;

    /// <summary>Created and owned by <see cref="VoiceManager"/>; read here as a duck detector
    /// only. Nothing in this class adds an effect to it or forks a <see cref="VoiceConfig"/>
    /// value — the proximity-voice pipeline is shipped and tuned and stays out of bounds.</summary>
    public const string Voice = VoiceConfig.OutputBus;

    /// <summary>The ambience group handle: the player's Ambient slider, and the single point a
    /// future withdrawal or a whole-world trim would act on. Carries no effects itself.</summary>
    public const string Ambient = "Ambient";

    /// <summary>The continuous bed's lane. Carries the duck compressor.</summary>
    public const string Bed = "Ambient_Bed";

    /// <summary>Sparse positional scenery one-shots (fire crackle, birdsong, insects, and
    /// the bed's own event layer where it is enabled). Clean — see the class doc for why these
    /// deliberately do not pass through the duck.</summary>
    public const string Scenery = "Ambient_Scenery";

    // --- Duck settings ------------------------------------------------------------------
    //
    // Starting points with reasons, not law — nothing in this repo's audio has ever been
    // profiled or mixed by ear on the floor spec, and these numbers inherit that. All four are
    // inside Godot 4.7's real parameter ranges, which are narrower than they look:
    // AttackUs is MICROseconds (20-2000, i.e. 0.02-2 ms) and ReleaseMs is milliseconds
    // (20-2000). Writing "20" meaning 20 ms would ask for the fastest attack the effect can do.

    /// <summary>Sidechain threshold, dB. Deliberately higher (less sensitive) than the -24 dB a
    /// generic music duck would use: a quiet or distant teammate should not move the bed at all,
    /// because a duck that fires constantly stops being a duck and becomes a level setting.</summary>
    public const float DuckThresholdDb = -18f;

    /// <summary>2:1, not 4:1. At a close teammate peaking around -6 dB this is roughly 6 dB of
    /// reduction rather than 13 — enough that speech sits clearly on top, shallow enough that
    /// the bed never reads as an event. Depth deep enough to notice belongs to
    /// <c>vfx-audio-sync</c>, not to a routing decision.</summary>
    public const float DuckRatio = 2f;

    /// <summary>The slowest attack Godot's compressor can do (2000 µs = 2 ms). Faster only
    /// destroys the bed's own transients for no intelligibility gain.</summary>
    public const float DuckAttackUs = 2000f;

    /// <summary>400 ms. Long enough that the bed does not pump between words in a sentence,
    /// short enough that it is back up before the next thing happens.</summary>
    public const float DuckReleaseMs = 400f;

    private static AudioEffectCompressor? _duck;

    /// <summary>Creates the whole tree if it is not there and leaves it exactly as it was if it
    /// is. Safe to call from every consumer's <c>_Ready</c>, and every consumer should — there
    /// is no boot-order guarantee between a world, a settings panel and a lab scene.</summary>
    public static void EnsureLayout()
    {
        // The voice buses keep their own owners. Calling them here rather than re-creating them
        // means there is still exactly one creator per bus, and it removes the ordering trap
        // where the compressor below names a Sidechain bus that does not exist yet.
        VoiceManager.EnsureOutputBus();

        int ambient = EnsureBus(Ambient, Master);
        int bed = EnsureBus(Bed, Ambient);
        EnsureBus(Scenery, Ambient);

        // Group handle carries no DSP on purpose: an effect here would apply to the scenery lane
        // too, which is the thing the two-lane split exists to prevent.
        _ = ambient;

        _duck = EnsureEffect(bed, 0, () => new AudioEffectCompressor
        {
            Sidechain = Voice, // read-only detector; no DSP is added to the Voice bus
            Threshold = DuckThresholdDb,
            Ratio = DuckRatio,
            AttackUs = DuckAttackUs,
            ReleaseMs = DuckReleaseMs,
        });
    }

    /// <summary>Turns the bed's duck on or off without rebuilding anything — the switch behind
    /// the open "should it duck at all" question in the class doc. Safe before
    /// <see cref="EnsureLayout"/> (no-op).</summary>
    public static void SetDuckEnabled(bool enabled)
    {
        int bed = AudioServer.GetBusIndex(Bed);
        if (bed < 0 || AudioServer.GetBusEffectCount(bed) == 0)
            return;
        AudioServer.SetBusEffectEnabled(bed, 0, enabled);
    }

    /// <summary>Whether the duck compressor is currently active. False if the layout has not
    /// been built.</summary>
    public static bool DuckEnabled
    {
        get
        {
            int bed = AudioServer.GetBusIndex(Bed);
            return bed >= 0 && AudioServer.GetBusEffectCount(bed) > 0
                && AudioServer.IsBusEffectEnabled(bed, 0);
        }
    }

    /// <summary>Lazy, idempotent, and returns the index either way. Same shape as
    /// <see cref="Sandbox.SfxLab"/>'s own <c>EnsureBus</c> with the send target as a parameter,
    /// because this tree has a middle layer and <c>SetBusSend</c> names a bus that must already
    /// exist — the ordering in <see cref="EnsureLayout"/> is load-bearing for that reason.</summary>
    public static int EnsureBus(string name, string send)
    {
        int existing = AudioServer.GetBusIndex(name);
        if (existing >= 0)
            return existing;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, name);
        AudioServer.SetBusSend(idx, send);
        return idx;
    }

    /// <summary>Places <paramref name="make"/>'s effect at <paramref name="slot"/> on
    /// <paramref name="busIdx"/> if something of that type is not already there, and returns
    /// whatever ends up occupying the slot. This is the part that makes repeated
    /// <see cref="EnsureLayout"/> calls genuinely harmless rather than merely short-circuited —
    /// see the class doc on why a "_built" bool is the wrong shape.</summary>
    private static T EnsureEffect<T>(int busIdx, int slot, System.Func<T> make) where T : AudioEffect
    {
        if (AudioServer.GetBusEffectCount(busIdx) > slot
            && AudioServer.GetBusEffect(busIdx, slot) is T existing)
        {
            return existing;
        }
        T effect = make();
        AudioServer.AddBusEffect(busIdx, effect, slot);
        return effect;
    }

    /// <summary>The live duck compressor, or null before <see cref="EnsureLayout"/>. Held from
    /// construction rather than fetched by index at every use, which is the pattern that stops a
    /// runtime parameter sweep depending on an effect's position in a chain somebody later
    /// reorders.</summary>
    public static AudioEffectCompressor? Duck => _duck;
}
