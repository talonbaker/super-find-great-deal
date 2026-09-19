using Godot;
using MpFoundation.Game.World;

namespace Sail.Game.Water.Fx;

/// <summary>
/// The world heard from under the surface: a bus-level lowpass on <c>Sfx</c> and <c>Ambient</c>,
/// swept continuously with submersion.
///
/// <b>Three things this is not, each refused on the record.</b>
///
/// It is <b>not a gain</b>. Direction §10.2: under the surface the world is FILTERED, not
/// silenced — the bed keeps playing. <see cref="WaterFxTuning.FilterSubmergedHz"/> is 700 Hz, low
/// enough to take every bit of air off the top and high enough that speech stays intelligible and
/// the bed stays present.
///
/// It is <b>not <c>AmbientBed.Withdraw()</c></b>, which is one line from callable and is the
/// wrong line. THRILL-BIBLE §6.3's wrong silence needs an absence with no explanation, and going
/// underwater is the most explicable absence in the game; spending the device here would burn
/// something the ambient bed already claimed and paid for. Direction §4.5 records this as a hard
/// refusal rather than a preference, and it is repeated here because the tempting call is in
/// scope of this very file.
///
/// It is <b>not a snap</b>. Direction §8.2: a hard cut at the waterline is a startle doing a
/// build's job. The cutoff sweeps, and it sweeps exponentially in the frequency domain because a
/// linear sweep from 20 kHz spends nearly all its travel in a band the ear cannot hear moving and
/// then collapses at the end — which is a snap wearing a sweep's clothes.
///
/// <b>Voice is deliberately untouched.</b> The two buses named are <c>Sfx</c> and the
/// <c>Ambient</c> group handle; proximity voice runs on <c>Voice</c> and does not pass through
/// either. Muffling teammates while you are under would be a version of the §5.5 teammate
/// voice-cut device, which direction §4.4 puts out of this packet's reach, and it would degrade
/// the one channel the fairness contract leans on hardest.
///
/// <b>A recorded fork, not a hidden one.</b> <c>AudioBuses</c> is the declared owner of the bus
/// tree and this file adds two effects it did not declare. That is a genuine (small) fork of
/// exactly the kind <c>AudioBuses</c> was created to end, taken because W4 is scoped to
/// <c>scripts/game/water/fx/</c> and a shared-file edit costs more than it buys on a wave this
/// wide. If a consolidation pass wants it, the move is mechanical: the two constants and
/// <see cref="Ensure"/> go into <c>AudioBuses.EnsureLayout</c> and the handles come back as
/// static properties beside <c>AudioBuses.Duck</c> — which is already the precedent shape for
/// "declared centrally, swept at runtime by whoever owns the reason".
/// </summary>
public static class SubmersionFilter
{
    /// <summary>Below this the filter is switched off entirely rather than left sitting at the
    /// open end. A lowpass at 20.5 kHz is nearly transparent but it is not free, and the lake is
    /// unoccupied for the overwhelming majority of frames in a session.</summary>
    private const float EngageEpsilon = 0.004f;

    private static AudioEffectLowPassFilter? _sfx;
    private static AudioEffectLowPassFilter? _ambient;
    private static float _submersion;
    private static bool _engaged;

    /// <summary>The submersion the filter is currently applying, in [0,1].</summary>
    public static float Submersion => _submersion;

    /// <summary>Whether the two effects are switched on right now.</summary>
    public static bool Engaged => _engaged;

    /// <summary>
    /// Put the two filters on their buses if they are not there, leaving everything exactly as it
    /// was if they are. Same idempotency contract <c>AudioBuses.EnsureLayout</c> holds and for the
    /// same reason: a builder that guards bus creation but adds effects unconditionally stacks a
    /// second filter on the second call and a third on the third, and nothing anywhere reports it.
    /// Checks the slot by TYPE rather than keeping a static "already built" bool, because that
    /// shape passes an idempotency test without proving anything — the second call never reaches
    /// the code the test is trying to check.
    /// </summary>
    public static void Ensure()
    {
        AudioBuses.EnsureLayout(); // no boot-order guarantee against a lab or a settings panel

        // AudioBuses NAMES the Sfx bus but does not create it — SfxLab owns it and creates it
        // lazily, inside its own private EnsureBus, on the first sound anybody plays. That is
        // fine for SfxLab and it was a silent bug here: WaterFx._Ready runs long before the first
        // footstep, so at Ensure() time GetBusIndex("Sfx") is -1, the filter lands nowhere, and
        // going underwater muffles the ambience while every gameplay one-shot stays bright. It
        // failed exactly that way on the first run of the scene self-test. AudioBuses.EnsureBus is
        // public, idempotent, and takes the same send target SfxLab's own creator uses, so calling
        // it here creates the bus if nobody has yet and is a no-op if somebody has.
        AudioBuses.EnsureBus(AudioBuses.Sfx, AudioBuses.Master);

        _sfx = EnsureLowpass(AudioBuses.Sfx);
        // The GROUP handle, not the bed lane: a child bus's output flows through its parent, so
        // one filter here catches the continuous bed and the sparse scenery layer both. Putting
        // it on Ambient_Bed would leave every birdsong and crackle un-muffled, which is the exact
        // shape of "the world sounds underwater except for the parts that don't".
        _ambient = EnsureLowpass(AudioBuses.Ambient);
        // Whatever the tree was doing before, this class starts from open-and-off.
        SetEnabled(false);
        _submersion = 0f;
        _engaged = false;
    }

    /// <summary>
    /// Drive the filter to a submersion in [0,1]. Called every frame from
    /// <see cref="WaterFx"/>'s process with an eased value; this method only applies, it never
    /// decides how fast.
    /// </summary>
    public static void Apply(float submersion)
    {
        if (!float.IsFinite(submersion))
            submersion = 0f;
        submersion = Mathf.Clamp(submersion, 0f, 1f);
        _submersion = submersion;

        if (submersion <= EngageEpsilon)
        {
            if (!_engaged)
                return;
            // Open FIRST, then switch off. The switch therefore happens at a point where the
            // filter is doing essentially nothing, which is what makes it inaudible — flicking
            // off a filter that was still shut is the click direction §10.2 forbids.
            SetCutoff(WaterFxTuning.FilterOpenHz);
            SetEnabled(false);
            _engaged = false;
            return;
        }

        if (!_engaged)
        {
            // Symmetrically: come up at the open end, then switch on, then let the caller's ramp
            // take it down. The first engaged frame is always a near-transparent filter because
            // the caller's sweep starts from zero.
            SetCutoff(WaterFxTuning.FilterOpenHz);
            SetEnabled(true);
            _engaged = true;
        }
        SetCutoff(WaterFxTuning.FilterCutoffHz(submersion));
    }

    /// <summary>Hand the buses back exactly as they were found. Called from
    /// <see cref="WaterFx"/>'s <c>_ExitTree</c>, and it is not optional: a session torn down
    /// mid-go-under would otherwise leave the entire game — menus included — permanently
    /// muffled, with nothing on screen to explain it.</summary>
    public static void Release()
    {
        SetCutoff(WaterFxTuning.FilterOpenHz);
        SetEnabled(false);
        _submersion = 0f;
        _engaged = false;
    }

    // --- Plumbing ---------------------------------------------------------------------------

    private static AudioEffectLowPassFilter? EnsureLowpass(string bus)
    {
        int idx = AudioServer.GetBusIndex(bus);
        if (idx < 0)
            return null;
        int count = AudioServer.GetBusEffectCount(idx);
        for (int slot = 0; slot < count; slot++)
        {
            if (AudioServer.GetBusEffect(idx, slot) is AudioEffectLowPassFilter existing)
                return existing;
        }
        var filter = new AudioEffectLowPassFilter { CutoffHz = WaterFxTuning.FilterOpenHz };
        // Appended at the END of whatever chain is there. On Sfx and on the Ambient group handle
        // that chain is empty today, but appending rather than claiming slot 0 means this never
        // races the duck compressor's fixed position if the tree grows.
        AudioServer.AddBusEffect(idx, filter, count);
        AudioServer.SetBusEffectEnabled(idx, count, false);
        return filter;
    }

    private static void SetCutoff(float hz)
    {
        if (_sfx != null)
            _sfx.CutoffHz = hz;
        if (_ambient != null)
            _ambient.CutoffHz = hz;
    }

    private static void SetEnabled(bool on)
    {
        SetEnabledOn(AudioBuses.Sfx, on);
        SetEnabledOn(AudioBuses.Ambient, on);
    }

    private static void SetEnabledOn(string bus, bool on)
    {
        int idx = AudioServer.GetBusIndex(bus);
        if (idx < 0)
            return;
        int count = AudioServer.GetBusEffectCount(idx);
        for (int slot = 0; slot < count; slot++)
        {
            if (AudioServer.GetBusEffect(idx, slot) is AudioEffectLowPassFilter)
                AudioServer.SetBusEffectEnabled(idx, slot, on);
        }
    }
}
