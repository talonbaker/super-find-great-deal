---
name: sound-real-time-effects
description: Use when Sail world audio needs a bus effect chain built or changed — muffling through a closed door, a furnace rumble, a TV from the next room, a walkie-talkie, de-essing harsh synthesis, ducking the bed under voice, or a filter parameter driven at runtime without clicking.
---

# sound-real-time-effects

## Overview

The **DSP toolbox**. Which `AudioEffect*` classes exist in Godot 4.7, what their properties are
actually called, what units they are actually in, what order to chain them in and when that
order inverts, how to drive a parameter at runtime without a click, and what any of it costs.
This is the reference layer the rest of the `sound-*` family reaches into.

**Verification basis.** Every API claim below was checked against `GodotSharp` 4.7.0 —
`~/.nuget/packages/godotsharp/4.7.0/lib/net8.0/GodotSharp.xml`, the same assembly
`Godot.NET.Sdk/4.7.0` builds this repo against — plus the official 4.x class reference for
deprecation status. Claims are marked `**Verified against Godot 4.7**` with the doc line, or
`unverified against Godot 4.7` inline.

**The source brief for this skill got four API facts wrong, and one of them ships a bug that
sounds exactly like a tuning problem.** Those corrections are the densest value in this file;
they are in *The roster, checked* below. Brief code samples are not evidence.

## Directed, not decided

**This skill is infrastructure, not affect, and it should say so plainly rather than manufacture
a doctrinal claim.** It serves no `THRILL-BIBLE.md` section directly. `vfx-particles` occupies
the same position in the `vfx-*` family and takes the same posture.

It never argues that an effect should exist. It says what an effect does, what it costs, and
what breaks if you wire it wrong. The argument for a sound existing comes from `/direct`, or it
does not come at all.

The closest thing to a doctrine link is second-order and worth stating once, not dressing up:

> A filter chain is a **tonal commitment**. `THRILL-BIBLE.md` §9's tone axis is decided
> (2026-07-26) but its **ratio** is not — how often a dread build cashes out absurd versus stays
> sincere, with the ripeness trigger *"the first playtest in which anyone is actually
> frightened."* `ModeEnum.Lofi` (bitcrush), `ModeEnum.Overdrive` and `AudioEffectChorus` are
> three different answers to that ratio, and picking one silently forecloses the fork. When a
> chain would commit, **say which position it commits to** and hand the call up.

`LEVEL-BIBLE.md` §8.3 is `[BLANK — Talon]` on whether Sail commits to a dedicated diegetic audio
channel that is *always* meaningful, and the bible notes this *"constrains sound design broadly,
which is why it is a call rather than a detail."* It bears here concretely: an always-meaningful
channel needs a reserved bus **and** a reserved frequency lane, which means an EQ carve on
everything else. That is a standing DSP cost this skill would have to pay, and it is not this
skill's call to incur.

`THRILL-BIBLE.md` §6.2's night-floor conflict is a shipped decision in live opposition to
doctrine. No skill in this family overrules it, and a DSP argument is not a way in.

## When to Use

- A bus needs an effect chain built, reordered, or corrected
- A parameter has to move at runtime — an occlusion sweep, a muffle, a filter opening up
- A chain is producing clicks, pumping, fizz, or a noise floor that was not there
- Procedural synthesis is coming out harsh and needs frequency treatment
- Someone is about to copy the shipped `PA` chain and needs to know why it is ordered the way it is
- Another `sound-*` skill needs the real property name, unit or range for something

**Not for:** what a filter sweep *means*, or when it fires — that is `sound-anticipation-escalation`,
and above it `vfx-escalation` and `/direct`. Reverb **zoning** and spatial configuration
(`sound-spatial-audio`). Frequency-lane assignment across the layers of a bed
(`sound-soundscape-construction`). Bus and voice budgets (`sound-optimization`). Whether a sound
should exist at all, or whether a device is spent (`/direct`, `vfx-audio-sync`). Anything inside
`scripts/voice/` — see *Caveats*.

**Making a chain cheap is not an argument that it should be built.** That argument lives upstream.

## What exists in the repo today

Negative findings first, because they are the load-bearing ones.

| Thing | State |
|---|---|
| `AudioEffect*` instances in the whole repo | **Exactly three.** All on the `PA` bus, all for voice, in `VoiceManager.EnsurePaBusName()`. |
| World-audio effect chain | **Does not exist.** Nothing outside `scripts/voice/` touches an `AudioEffect`. |
| `default_bus_layout.tres` | **Does not exist.** Every bus in Sail is created in code. |
| `project.godot` `[audio]` | Two lines only: `driver/enable_input=true`, `driver/mix_rate=48000`. No bus layout, no output-latency override. |
| Buses | `Master` (engine default), `Sfx`, `Voice`, `VoiceCapture`, `PA`. There is **no `Ambient` bus** — every `Ambient*` symbol in `scripts/` is lighting. |
| Audio asset files | **None.** No `.ogg`, `.wav`, `.mp3` anywhere. Everything is synthesized at runtime. |
| `SfxLab` output format | 48 kHz, 16-bit, **mono** (`Stereo = false`), rendered once and cached. |
| Mixer UI | `scripts/ui/SettingsPanel.cs` — Master and Voice sliders only. Any new bus a player should be able to turn down needs a slider added there. |
| Test coverage of any of this | **Zero.** No test loads, plays, mixes, or asserts an `AudioServer` bus. `SfxLab` has no coverage and its synthesis helpers are all `private static`. |
| Profiling | **None.** Nothing audio has ever been measured on the GTX 970 floor. |

**The shipped chain, verbatim** (`scripts/voice/VoiceManager.cs`, `EnsurePaBusName()`):

```csharp
AudioEffectDistortion    { Mode = Overdrive, Drive = 0.28f, PostGain = -3f }
AudioEffectLowPassFilter { CutoffHz = 2200f, Resonance = 0.6f }
AudioEffectReverb        { RoomSize = 0.7f, Damping = 0.4f, Wet = 0.25f, Dry = 0.9f }
```

This is the only working reference for real parameter values in the codebase. It is also
**distortion before filtering**, which inverts the brief's rule — see *Core patterns* §2. And it
contains one live curiosity: **`ModeEnum.Overdrive` documents that `Drive` has no effect in that
mode**, so `Drive = 0.28f` is doing nothing. Reported, not fixed — `scripts/voice/` is out of
bounds. It matters here only because someone copying this chain for a walkie-talkie will copy a
value that does not do what its name implies, and will then reach for the wrong knob when the
result is too clean.

## The roster, checked

**Verified against Godot 4.7** unless marked otherwise. Every `AudioEffect*` class in 4.7:

| Class | Real properties (C#) | Notes |
|---|---|---|
| `AudioEffectAmplify` | `VolumeDb` (−80…24), `VolumeLinear` | `VolumeLinear` is a convenience alias that writes `VolumeDb`. |
| `AudioEffectFilter` (base) | `CutoffHz` (1…20500), `Resonance` (0…1), `Db`, `Gain` (0…4) | `Gain` is *"only available for `AudioEffectLowShelfFilter` and `AudioEffectHighShelfFilter`"*. |
| `AudioEffectLowPassFilter` / `HighPassFilter` / `BandPassFilter` / `BandLimitFilter` / `NotchFilter` / `LowShelfFilter` / `HighShelfFilter` | inherited from `AudioEffectFilter` | **Seven concrete filters, not four.** The brief lists four. |
| `AudioEffectDistortion` | `Mode`, `Drive` (0…1), `PreGain` (−60…60), `PostGain` (−80…24), `KeepHfHz` (1…20000) | See corrections 1 and 2. |
| `AudioEffectCompressor` | `Threshold` (−60…0), `Ratio` (1…48), `Gain` (−20…20), **`AttackUs`**, **`ReleaseMs`**, `Mix` (0…1), `Sidechain` | See correction 3. |
| `AudioEffectLimiter` | `CeilingDb`, `ThresholdDb`, `SoftClipDb`, `SoftClipRatio` | **Deprecated.** See correction 4. |
| `AudioEffectHardLimiter` | `CeilingDb` (−24…0, default −0.3), `PreGainDb`, `Release` (0.01…3 **seconds**) | The replacement. |
| `AudioEffectEQ` (base) + `EQ6` / `EQ10` / `EQ21` | `SetBandGainDb(int, float)`, `GetBandGainDb(int)`, `GetBandCount()` | Method-based, no per-band property. Band centres are documented per subclass. |
| `AudioEffectReverb` | `RoomSize`, `Damping`, `Wet`, `Dry`, `Spread`, **`Hipass`**, `PredelayMsec` (20…500), `PredelayFeedback` | Property is `Hipass`; the accessors are `SetHpf`/`GetHpf`. Algorithmic, not convolution. |
| `AudioEffectChorus` | `VoiceCount` (1…4), `Wet`, `Dry` | Per-voice params are **methods only** — `SetVoiceRateHz(int, float)`, `SetVoiceDepthMs`, `SetVoiceDelayMs`, `SetVoiceLevelDb`, `SetVoicePan`, `SetVoiceCutoffHz`. They cannot go in an object initialiser. |
| `AudioEffectPitchShift` | `PitchScale` (0…16), `Oversampling`, `FftSize` | **Exists.** See correction 1. |
| `AudioEffectDelay` | two taps + feedback, all `*Ms` / `*Db` | *"a non-blurry type of echo"* versus reverb. |
| `AudioEffectPhaser`, `AudioEffectPanner`, `AudioEffectStereoEnhance` | — | `Panner` after a 3D player overrides the spatial pan. Rarely what you want. |
| `AudioEffectCapture`, `AudioEffectRecord`, `AudioEffectSpectrumAnalyzer` | — | Analysis/tap, do not alter audio. `VoiceCapture` territory. |

### Correction 1 — `AudioEffectPitchShift` exists, and the brief's central claim is wrong

The brief asserts *"Godot 4.7 does not expose a dedicated real-time pitch-shift `AudioEffect`
(pitch shifting independent of playback speed requires a phase-vocoder, which isn't built in)"*
and routes you to `AudioStreamPlayer.PitchScale`.

**Verified against Godot 4.7** — `AudioEffectPitchShift`: *"Allows modulation of pitch without
modifying speed. All frequencies can be raised or lowered with minimal effect on transients."*
Its `PitchScale` *"can range from 0 (infinitely low pitch, inaudible) to 16."* `FftSize` is
`FftSizeEnum.Size256` … `Size4096`: *"Higher values smooth out the effect over time, but have
greater latency. The effects of this higher latency are especially noticeable on audio signals
that have sudden amplitude changes."* `Oversampling`: *"Higher values result in better quality,
but are more demanding on the CPU and may cause audio cracking if the CPU can't keep up."*

The phase vocoder is built in. **These are two materially different tools and the brief collapses
them:**

- `AudioStreamPlayer3D.PitchScale` is *"the pitch **and the tempo** of the audio, as a multiplier
  of the audio sample's sample rate"* — resampling. Cheap, per-source, and it changes duration.
  Correct for the per-shot jitter `SfxLab` already does (`pitchJitter = 0.08f`).
- `AudioEffectPitchShift` changes pitch and leaves duration alone. Bus-wide, FFT-based, and the
  only one of the two that can drop a sustained sound a fourth without also slowing it down.

**What the brief's version would have produced:** anyone told "there is no pitch shifter" reaches
for `PitchScale` on a long sound and gets a tempo change they did not ask for. On a synthesized
attic thump slowed to sound bigger, the envelope stretches too — the attack softens and the hit
stops reading as an impact. That is the wrong fix arrived at confidently.

It is not free: FFT plus oversampling, per audio buffer, on a whole bus. Do not park it on
`Master`. It is the most expensive effect in the roster that is not reverb, and its latency is
`FftSize`-proportional, which matters if the shifted signal has to land in the same instant as
something visual (`vfx-audio-sync` §4.6).

### Correction 2 — the distortion modes are `Clip`, `Atan`, `Lofi`, `Overdrive`, `Waveshape`

The brief lists *"Clip, Tan, Bitcrush, WaveShape"*. Three of those four do not compile.

**Verified against Godot 4.7**, the class doc: *"The different types available are: clip, atan,
**lofi (bitcrush)**, overdrive, and waveshape."* So:

| Brief | Real | |
|---|---|---|
| `Tan` | `Atan` | *"Flattens the waveform in a smooth manner, following an arctangent curve."* |
| `Bitcrush` | **`Lofi`** | *"Decreases audio bit depth to achieve a low-resolution audio signal, going from 16-bit to 2-bit. Can be used to emulate the sound of early digital audio devices."* |
| `WaveShape` | `Waveshape` | *"…until it reaches a sharp peak at `drive = 1`, following a generic absolute sigmoid function."* |
| `Clip` | `Clip` | *"…is the only mode that clips audio signals at 0 dB."* |
| — | `Overdrive` | shipped on `PA`. *"`Drive` has no effect in this mode."* |

The bitcrush the brief wanted **is** available; it is just spelled `Lofi`. Its real doc reframes
it, though: 16-bit down to 2-bit is a *1990s digital-artefact* sound, not an alien one — which
suits a house with a cheap answering machine and a portable TV better than the brief's "reality
glitching" framing suggests. That framing is a tone commitment either way and belongs to §9's
open ratio, not here.

Two more from the same page, both worth knowing before you reach for distortion at all:

- *"An enabled distortion effect still changes the sound even when `Drive` is set to 0. This is
  not a bug. If this behavior is undesirable, consider disabling the effect using
  `AudioServer.SetBusEffectEnabled()`."* **`Drive = 0` is not a bypass.** `Enabled` is.
- `KeepHfHz` — *"Frequencies higher than this value will not be affected by the distortion."* A
  band-protect built into the effect, which sometimes removes the need to reorder the chain at
  all (see §2).

### Correction 3 — `AttackUs` is **microseconds**, and the brief is off by 1000×

The brief writes `Attack = 20f, /* ms */ Release = 250f /* ms */`.

**Verified against Godot 4.7:** the property is `AudioEffectCompressor.AttackUs` —
*"Compressor's reaction time when the audio exceeds the volume threshold level, **in
microseconds**. Value can range from 20 to 2000."* Release is `ReleaseMs` —
*"…in milliseconds. Value can range from 20 to 2000."*

Three separate problems, in ascending order of how long they take to find:

1. **`Attack` is not a property.** `new AudioEffectCompressor { Attack = 20f }` does not compile.
   Cheapest possible failure; you find it immediately.
2. **`20` in `AttackUs` is 20 microseconds — the floor of the range, the fastest attack the effect
   can do.** Someone writing "20 ms" and getting 20 µs has asked for maximum transient
   destruction. On `SfxLab`'s content — hard-enveloped synthesized thumps and bonks — that
   flattens every attack and produces exactly the pumping the brief's tuning section blames on
   tuning. **The brief diagnoses the symptom its own sample causes.**
3. **The brief's tuning advice is unachievable in Godot.** It warns that *"very fast attack
   (<5 ms) on transient-heavy sources causes audible pumping."* Godot's compressor tops out at
   **2000 µs = 2 ms**. Every legal setting is under 5 ms. If you need a slow attack that lets
   transients through, Godot's stock compressor cannot give you one — pick a different tool
   (`AudioEffectHardLimiter` for safety, `AudioEffectAmplify` plus a wider ceiling for level)
   rather than tuning toward a value that does not exist.

Also verified and useful: `Sidechain` is *"audio bus to use for the volume threshold detection"*,
and the class doc names ducking explicitly — *"This technique is common in video game mixing to
decrease the volume of music and SFX while voices are being heard."* That is the correct shape
for the one voice-adjacent thing this skill may do; see *Integration points*.

### Correction 4 — `AudioEffectLimiter` is deprecated; use `AudioEffectHardLimiter`

The brief puts `new AudioEffectLimiter { CeilingDb = -1f }` on the master bus.

**Verified against Godot 4.7.** The 4.x class reference carries *"Deprecated: Use
`AudioEffectHardLimiter` instead."* The 4.7.0 XML says it inside the class itself:
`AudioEffectLimiter.SoftClipRatio` — *"This property has no effect on the audio. Use
`AudioEffectHardLimiter` instead, as this Limiter effect is deprecated."* `AudioEffectCompressor`'s
own doc points the same way: master-bus compression, *"although an `AudioEffectHardLimiter` is
probably better."*

`AudioEffectHardLimiter.CeilingDb` is *"the waveform's maximum allowed value, in dB. This value
can range from −24 to 0. The default value of −0.3 prevents potential inter-sample peaks (ISP)
from crossing over 0 dB, which can cause slight distortion on some older hardware."* `Release` is
*"time it takes **in seconds** for the gain reduction to fully release. Value can range from 0.01
to 3"* — seconds, not milliseconds, and the one unit in this class most likely to be mis-set.

**What the deprecated version would have produced:** soft-clipping instead of peak prediction, one
dead property, and a recommendation against the engine's own guidance sitting on the master bus —
the single most consequential place in the mix. Take the default `−0.3` unless you have a measured
reason; `−1` throws away most of a dB of headroom for nothing.

### Correction 5 — the EQ band index

`AudioEffectEQ10` documents its bands as *"Band 1: 31 Hz … Band 10: 16000 Hz"* — that is prose
numbering for the human reader, not the index base. **`band_idx` is 0-based.** **Verified
2026-07-29 against the installed Godot v4.7-stable.mono, headless**: `AudioEffectEQ10.new()`
reports `GetBandCount() == 10`; `SetBandGainDb(0, …)` and `SetBandGainDb(9, …)` both round-trip
correctly (index `9` being the tenth and last band); `SetBandGainDb(10, …)` throws `ERROR: Index
p_band = 10 is out of bounds (gain.size() = 10)` from `audio_effect_eq.cpp`. So "Band 1: 31 Hz"
is index `0`, and the brief's `eq.SetBandGainDb(3, -6f)` targets the fourth band (documented as
"Band 4"), not the third.

## Core patterns

### 1. Every bus is created in code, and the creation must be idempotent

There is no bus layout resource. `SfxLab.EnsureBus()` is the pattern, and it exists in two
shipped copies (`SfxLab`, `VoiceManager.EnsureOutputBus`/`EnsurePaBusName`) that agree exactly:

```csharp
if (AudioServer.GetBusIndex(Bus) >= 0) return;   // the guard is the whole contract
int idx = AudioServer.BusCount;
AudioServer.AddBus(idx);
AudioServer.SetBusName(idx, Bus);
AudioServer.SetBusSend(idx, "Master");
```

**Make the guard load-bearing when effects are involved, because the failure mode changes.** A
non-idempotent `EnsureBus` called from two instances gives you a duplicate *bus* — visible, and
`GetBusIndex` returns one of them so half your emitters go somewhere unexpected. But an
`EnsureBus` that guards the bus and **not** the effects — or one that runs its `AddBusEffect`
calls before the early return — stacks a second full chain onto the same bus every time it is
called. Two reverbs in series is not twice the reverb; it is a reverb of a reverb, roughly twice
the CPU, and it sounds like a slightly wrong room rather than like a bug. It is silent,
cumulative, expensive, and it scales with how many times the caller was instantiated — so it
appears in a 6-player session and not in your test. `sound-spatial-audio` hits the identical
hazard from the reverb-zone side.

`AudioServer` is global and survives scene changes. A bus created once stays created for the
process. **Never call `AddBusEffect` outside the branch that just created the bus.**

**`AddBusEffect` signature, verified:** `AddBusEffect(int busIdx, AudioEffect effect, int atPosition = -1)`.
The third parameter is the insertion index. Related, all verified: `GetBusEffect(int, int)`,
`GetBusEffectCount(int)`, `RemoveBusEffect(int, int)`, `SwapBusEffects(int, int, int)`,
`SetBusEffectEnabled(int, int, bool)`, `IsBusEffectEnabled(int, int)`,
`GetBusEffectInstance(int, int, int)`.

### 2. Chain order — the rule, its reason, and when the reason inverts

The brief's order is: EQ/filter → distortion → compressor → reverb → limiter (master only).
The repo's one working chain is: **distortion → filter → reverb**. Both are right. Here is why.

**Distortion is nonlinear: it creates frequency content that was not in the input.** Everything
else in the chain only attenuates or delays what is already there. That single fact is what makes
its position matter, and it is the reason the rule has an inversion:

- **Filter → distort** shapes the *input* to the nonlinearity. The harmonics distortion generates
  land on top of the filtered signal and are themselves untouched. Correct when you are protecting
  a band you intend to keep — carve out the region proximity voice occupies first, then distort,
  and the distortion cannot fizz back into it.
- **Distort → filter** shapes the *output*, including everything the distortion invented. Correct
  when you are modelling a physical chain whose band limit sits downstream of its nonlinearity —
  which is nearly every device worth imitating in this setting. A megaphone overdrives at the amp
  and cone, then the horn band-limits; a walkie-talkie clips at the transmitter, then a tiny
  driver kills the highs; a telephone, a portable TV, a cassette answering machine, all the same
  shape. **This is why the shipped `PA` chain is ordered the way it is, and it is right.**

Note also that the brief's stated justification for its own rule — *"so you don't distort
frequencies you're about to cut"* — only holds if you cut afterwards, which in filter-first order
you do not. The rule and its reason are in tension. Use the reason, not the rule.

`KeepHfHz` is the third option and often the best one: it protects a band **inside** the
distortion, so you get band-protection without committing the whole chain to an ordering.

The rest of the order is stable and the reasons are real:

- **Compressor after distortion.** Distortion changes the dynamics it is fed; compressing first
  means compressing something the next stage is about to reshape. With Godot's ≤2 ms attack
  ceiling this is close to limiting either way (correction 3).
- **Reverb late.** Reverb models a space responding to a finished dry signal. Distorting a reverb
  tail sounds like the *room* is broken rather than the source — occasionally a deliberate effect,
  never an accident you want.
- **Limiter on `Master` only, and `AudioEffectHardLimiter`.** It is a safety net against many
  dynamic layers stacking, not a mix tool. Putting one on a sub-bus hides the level problem from
  the master limiter and from you.

### 3. Cache the effect by identity, not by index

The brief caches `(AudioEffectLowPassFilter)AudioServer.GetBusEffect(busIdx, 0)` and
`(AudioEffectDistortion)AudioServer.GetBusEffect(busIdx, 1)` in `_Ready`.

Two things about that. First, the stated reason to cache is wrong, or at least undocumented: the
brief says `GetBusEffect` *"is not free."* There is no doc line supporting that
(`unverified against Godot 4.7`) — and Godot **does** document that cost elsewhere when it exists,
e.g. `AudioServer.GetOutputLatency`: *"This can be expensive; it is not recommended to call
`GetOutputLatency()` every frame."* The absence of a matching note on `GetBusEffect` is
informative.

Second, the **real** reason to cache is much better: **index-based effect access is positional and
nothing enforces the positions.** `GetBusEffect(idx, 0)` means "whatever is first right now."
`AddBusEffect` accepts an insertion index, `RemoveBusEffect` shifts everything after it down, and
`SwapBusEffects` reorders in place. If two systems each add effects to a shared bus and their
order depends on which `_Ready` ran first, then a cast that succeeded yesterday throws
`InvalidCastException` today — or worse, succeeds against the wrong instance of the same type and
you spend an afternoon driving a filter nobody can hear.

**The safe shape: keep the reference from the moment you created it.** You already have it — you
just constructed it.

```csharp
namespace MpFoundation.Game.World;

/// <summary>
/// World audio heard through a closed door or a floor: one low-pass, created once, driven
/// from an occlusion factor. Mirrors SfxLab.EnsureBus's idempotency contract — the effect is
/// added ONLY on the branch that created the bus, and the reference is kept from construction
/// rather than fetched back by index later.
/// </summary>
public static class MuffleBus
{
    public const string Bus = "Muffle";

    private const float OpenHz = 20500f;          // filter ceiling; effectively bypassed
    private const float ShutHz = 700f;            // hollow-core interior door, by ear — a dial
    private const float MaxOctavesPerSecond = 6f; // click guard, see pattern 4

    private static AudioEffectLowPassFilter? _lowPass;
    private static float _currentHz = OpenHz;
    private static float _targetHz = OpenHz;

    public static int EnsureBus()
    {
        int idx = AudioServer.GetBusIndex(Bus);
        if (idx >= 0)
            return idx;                            // NEVER add effects past this line
        idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, Bus);
        AudioServer.SetBusSend(idx, "Master");
        var lp = new AudioEffectLowPassFilter
        {
            CutoffHz = OpenHz,
            Resonance = 0f,                        // resonance on a moving cutoff whistles
            Db = AudioEffectFilter.FilterDB.Filter12Db,
        };
        AudioServer.AddBusEffect(idx, lp);
        _lowPass = lp;                             // the only handle we will ever need
        return idx;
    }

    /// <summary>0 = open, 1 = fully shut. Interpolated in the LOG domain: pitch perception is
    /// logarithmic, so a linear Hz ramp spends most of its travel in the top octave where
    /// almost nothing audible happens, then lurches through the useful range at the end.</summary>
    public static void SetOcclusion(float t) =>
        _targetHz = OpenHz * Mathf.Pow(ShutHz / OpenHz, Mathf.Clamp(t, 0f, 1f));

    /// <summary>Drive from one _Process. Rate-limits the coefficient change so no single audio
    /// buffer sees a large jump — the size of the jump, not the write frequency, is what clicks.</summary>
    public static void Tick(double delta)
    {
        if (_lowPass == null || Mathf.IsEqualApprox(_currentHz, _targetHz))
            return;                                // don't rewrite a value that did not change
        float maxOctaves = MaxOctavesPerSecond * (float)delta;
        float wantOctaves = Mathf.Log(_targetHz / _currentHz) / Mathf.Log(2f);
        _currentHz *= Mathf.Pow(2f, Mathf.Clamp(wantOctaves, -maxOctaves, maxOctaves));
        _lowPass.CutoffHz = _currentHz;
    }
}
```

One hazard the shape does not remove: if something later calls `RemoveBusEffect` on this bus, the
cached reference stays *alive* (it is a `Resource`) but is no longer in the signal path. Writes to
it then silently do nothing. That is the same class of bug as the group-name mismatch — a feature
that looks like it does nothing. If a bus is ever going to have effects removed at runtime, hold
the index too and re-verify with `GetBusEffectCount` before writing.

### 4. Clicks, and why "only write on change" is necessary but not sufficient

**Why a parameter write clicks.** A filter is a difference equation whose coefficients are derived
from `CutoffHz` and `Resonance`. Change the property and the coefficients are recomputed, but the
filter's internal state — the delayed samples it carries between buffers — was produced by the
*old* coefficients. The first output samples under the new coefficients therefore do not continue
the previous waveform; the output steps. A step in a waveform is broadband energy, and broadband
energy in one sample is a click. The same applies to any gain: a jump from −6 dB to −24 dB between
buffers is a discontinuity regardless of how correct both values are.

**So "don't write every frame when the value is static" is real but partial.** It removes redundant
coefficient recomputation. It does nothing about the size of a single jump — one write of the wrong
size clicks just as loudly as sixty.

**What smoothing actually requires is bounding the per-step delta, not the step count.** An audio
buffer at 48 kHz is on the order of a few milliseconds to ~15 ms depending on
`audio/driver/output_latency` (`unverified against Godot 4.7` for the exact default; Sail does not
override it). That is the same order as a video frame, so a per-frame write is roughly a per-buffer
write and the frame rate does not save you. The rate limit above — octaves per second, clamped
against `delta` — is the shape that works, because it makes the maximum discontinuity a function of
wall-clock time rather than of how far the target happened to jump.

Two corollaries:

- **Large jumps must be ramped, not tweened-and-hoped.** A `Tween` is fine as the driver, but tween
  the *target* and let a rate limiter feed the effect, or tween over a duration long enough that no
  buffer sees a big step. If you tween the effect property directly, use the engine `StringName`
  the bindings already expose — `AudioEffectFilter.PropertyName.CutoffHz` — rather than a
  hand-written string, for the same reason the repo uses `SignalName.X` and `MethodName.X`.
- **Resonance on a moving cutoff is its own problem.** A resonant peak swept across the spectrum
  whistles audibly even when the sweep itself is smooth. `Resonance = 0` on anything that moves,
  unless the whistle is the point.

**The shape of a sweep — logarithmic in Hz, and how fast, and what it means — is
`sound-anticipation-escalation`'s.** This skill only guarantees that whatever shape it asks for
arrives without a click.

### 5. Cost, and the silent-bus claim the brief gets backwards

The brief asserts that *"each effect costs CPU per audio buffer whether or not sounds are playing
through it, because Godot still runs the DSP chain on the bus"* and concludes you must remove
unused buses rather than leave them muted.

**Verified against Godot 4.7** — the official *Audio buses* guide: *"There is no need to disable
buses manually when not in use. Godot detects that the bus has been silent for a few seconds and
disables it (including all effects)."* Corroborating, from the C# docs for
`AudioEffectInstance._ProcessSilence`: *"Should return `true` to force the `AudioServer` to always
call `_process`, **even if the bus has been muted or cannot otherwise be heard**."* — a
force-always-run opt-in only makes sense if not-running is the default.

So the engine already does the thing the brief tells you to do by hand. **Do not delete buses to
save CPU on the strength of that claim.** What is still true and worth keeping:

- A bus that is *audible* pays for its whole chain every buffer, and the cost is per-buffer, not
  per-voice — which is the property that makes bus DSP good value, and which `VoiceManager` already
  documents in place: *"Bus DSP runs once per audio block regardless of listener count."*
- The auto-disable takes *"a few seconds"* of silence. A bus that is nearly-always-quiet but
  regularly tickled never gets to idle.
- **Consolidate anyway, for a different reason.** One bus per creature type or per prop is bad
  architecture — bus names are global strings, the `SettingsPanel` mixer has to grow a slider per
  player-facing bus, and the effect-index fragility in pattern 3 compounds with every system that
  touches a shared bus. Consolidate because the ownership gets unmanageable, not because idle
  buses burn CPU.
- **`SetBusEffectEnabled` is the documented way to take an effect out of the path** (from the
  distortion note in correction 2). Whether it also skips the DSP work is `unverified against
  Godot 4.7` — do not assume it as a perf lever without measuring.

**Relative cost, `unverified against Godot 4.7` and unmeasured in this repo:** `AudioEffectReverb`
and `AudioEffectPitchShift` are the expensive ones by construction (a reverb network; an FFT with
oversampling). `AudioEffectChorus` runs up to four delay lines with LFOs. Filters, distortion,
compressor, amplify and EQ are cheap by comparison. The brief's "budget 4–6 active effects per bus"
has no measurement behind it in this repo either — **treat it as a smell threshold, not a budget.**
Nothing audio in Sail has ever been profiled on the GTX 970 floor, and `sound-optimization` owns
that budget when someone finally measures it.

Adjacent and verified, because it is the cheapest saving available:
`AudioStreamPlayer3D.MaxDistance` — *"This can be used to prevent the `AudioStreamPlayer3D` from
requiring audio mixing when the listener is far away, which saves CPU resources."* Culling sources
beats optimising the chain they feed.

### 6. Everything is mono, and everything is synthesized

Both facts change what the chain does.

**Mono sources.** `SfxLab` renders 16-bit mono (`Stereo = false`); `VoiceSpeaker` pushes through an
`AudioStreamGenerator`. Buses are multi-channel (`AudioServer.GetBusChannels(int)` reports the
count, **verified**), and a mono source acquires its stereo image entirely from the 3D panner. So:

- `AudioEffectReverb.Spread`, `AudioEffectChorus`'s per-voice `Pan`, and `AudioEffectStereoEnhance`
  all widen a stereo image that, for a 3D mono source, *is the positional cue*. Widening it
  smears the position. A chorus with voices panned hard L/R will partially undo the spatialisation
  that told a player which room the sound came from. Whether that trade is acceptable is
  `sound-spatial-audio`'s call, but the mechanism is this skill's to state.
- `AudioEffectPanner` after a 3D player overrides the pan the position produced. Almost never right.

**Synthesized sources, so de-essing is a live need rather than a hypothetical.** Every sound in the
game comes out of `SfxLab`'s private recipes (`EffortGrunt`, `Thump`, `Bonk`, `Warble`, `Sweep`,
`Oof`, `TwoNote`, `Squeak`) via `Render`/`Envelope`/`EaseOut`/`ToWav`. Naive synthesis at 48 kHz —
hard envelopes, non-band-limited waveforms — aliases, and the aliased content folds back down as
inharmonic high-frequency grit that no amount of level adjustment fixes. `AudioEffectEQ10` or a
`HighShelfFilter` on the bus will tame it, and that is the fast answer.

**Be honest that it is the wrong fix.** Bus EQ treats the symptom for every sound on the bus
equally, including the ones that were fine. The right fix is band-limiting in the synthesis itself
— inside `SfxLab`'s recipes, or wherever a layer's waveform is generated, which is
`sound-soundscape-construction`'s side of the line. Reach for the EQ when the synthesis is not
yours to change, and say which one you did.

## Tuning guide

Starting points with the reason attached. None of these is a gate, and none has been measured in
this repo.

- **Interior-door muffle ≈ 700 Hz low-pass, 12 dB/octave.** A hollow-core door passes low
  frequencies and speech fundamentals while eating consonants — which is why you can tell someone
  is talking through a door and not what they said. 6 dB/oct is too gentle to read as a barrier;
  24 dB/oct starts to read as underwater. `Db` is the dial, not `CutoffHz`, when the muffle is
  "wrong" but the frequency is right.
- **Walkie-talkie ≈ band-pass, not low-pass.** A small driver has no lows *or* highs.
  `AudioEffectBandPassFilter` around 1–3 kHz, then `Lofi` or `Clip` distortion for the transmitter,
  then the band-pass again if you want the artefacts contained. Order per §2; the shipped `PA`
  chain is the nearest working model and is distort-then-filter for exactly this reason.
- **Furnace / TV in the next room = the muffle chain plus distance, not a new effect.** The
  temptation is to reach for reverb. Reverb is `sound-spatial-audio`'s and a wrongly-zoned reverb
  is more noticeable than no reverb.
- **`AudioEffectHardLimiter` on `Master` at the default `−0.3 dB` ceiling.** The default exists for
  a documented reason (inter-sample peaks). Deviate only with a measurement.
- **Compressor `Ratio` 4, `Threshold` −18 dB** are reasonable transparent-glue starting points and
  the brief's figures are fine here. **`AttackUs` and `ReleaseMs` are not** — see correction 3,
  and remember every legal attack is fast.
- **`Resonance = 0` on anything that moves.** See pattern 4.
- **One bus per *mix role*, not per source type.** `Sfx`, a world bed, a muffled-interior send —
  three roles, three buses. Not one per creature, prop, or room.

## Integration points

- **`SfxLab.EnsureBus` / `VoiceManager.EnsureOutputBus`** — the idempotent creation contract. Copy
  the shape exactly, and keep `AddBusEffect` inside the creation branch.
- **`SfxLab.PlayStream3D`** is the one compliant positional playback path, and it routes to the
  `Sfx` bus. Anything that needs a different chain needs a different bus **and** a playback path
  that sets `Bus` — `PlayStream3D` does not take a bus parameter today.
- **`scripts/ui/SettingsPanel.cs`** — Master and Voice sliders only. A new player-facing bus needs
  a slider added here or players cannot turn it down. That is a real obligation, not a nicety, for
  anything continuous.
- **Ducking under voice is the one voice-adjacent thing this skill may build, and the mechanism
  matters.** `AudioEffectCompressor.Sidechain` is *"audio bus to use for the volume threshold
  detection"* — the compressor lives on the **world** bus and merely *reads* `Voice`'s level.
  Nothing is added to the `Voice` bus, so the boundary holds and `vfx-audio-sync`'s "the
  interaction belongs on the world side" is satisfied literally. Two caveats: whether the sidechain
  still reads a bus that the engine has auto-disabled for silence is `unverified against Godot 4.7`
  (it should read as silence, which is the desired behaviour, but verify); and **whether the bed
  should duck at all is a mix/affect call for `vfx-audio-sync`, not a DSP call for this skill.** In
  a 6-player proximity-voice session someone is nearly always talking, so a sidechain keyed to
  `Voice` is close to a permanent level cut.
- **`AudioStreamPlayer3D.AreaMask`** — **verified**: *"Determines which `Area3D` layers affect the
  sound for reverb and audio bus effects. Areas can be used to redirect `AudioStream`s so that they
  play in a certain audio bus."* This is the engine-native route from a room to a bus, and it is
  **`sound-spatial-audio`'s** to design. Named here only so nobody hand-rolls a per-frame bus
  reassignment.
- **`CycleDriver.Instance.Phase`** is the shipped clock and is **cyclic, not monotonic** — it wraps
  in `[0,1)` over a 120 s default period. Any chain whose parameter should escalate across a
  session must take a derived monotonic quantity from `vfx-escalation`, never `Phase` directly, and
  must gate reads on `CycleDriver.Instance.Synced`.
- **`sound-anticipation-escalation`** owns sweep shape, timing and meaning. **`sound-optimization`**
  owns the bus and voice budget. **`sound-soundscape-construction`** owns which layer gets which
  frequency lane — this skill supplies the EQ, not the lane assignment.

## Precedent

- **`scripts/voice/VoiceManager.cs` — `EnsurePaBusName()`.** The only working effect chain in the
  repo, and a genuinely good one: three cheap stock effects producing a specific, legible device,
  ordered to model a physical signal path rather than to follow a generic rule. Its own comment
  states the economics — *"Bus DSP runs once per audio block regardless of listener count."* Read
  it before building any chain. Do not modify it, and do not copy `Drive = 0.28f` without knowing
  it is inert under `Overdrive`.
- **`scripts/game/sandbox/SfxLab.cs` — `EnsureBus()`.** The idempotency contract, and the pooling
  and concurrency discipline the effects sit downstream of.
- **`scripts/game/world/CycleDriver.cs`** — the house pattern for anything that has to be the same
  on every client. A chain driven by replicated state follows it; a chain driven by a local
  `SceneTreeTimer` does not and will desync.
- **External:** the effect vocabulary and the chain ordering mirror Wwise and FMOD's RTPC-driven
  chains, and Godot's bus system is a lighter-weight functional equivalent. The `AudioEffect*`
  resource being separate from its `AudioEffectInstance` is the same author/runtime split those
  tools make. What Godot does not have is Wwise/FMOD's authoring UI, profiler, or convolution
  reverb — so the tuning that would happen in a middleware session happens here in code, which is
  the argument for keeping every constant named and commented rather than inline.

## Troubleshooting

Symptom → cause, in roughly the order they occur.

- **The chain compiles and nothing changes** — the emitter's `Bus` does not route there.
  **Verified**: `AudioStreamPlayer3D.Bus` — *"no validation is performed to see if the given name
  matches an existing bus… If this given name can't be resolved at runtime, it will fall back to
  `Master`."* A typo'd bus name is silent. Check `AudioServer.GetBusIndex(name) >= 0` at the point
  the emitter is configured, not just at creation.
- **It does not compile** — `Attack` (it is `AttackUs`), `ModeEnum.Bitcrush` (it is `Lofi`),
  `ModeEnum.Tan` (it is `Atan`), or a per-voice chorus parameter in an object initialiser (they are
  methods). Corrections 2 and 3.
- **Everything is pumping / breathing / transients are flat** — `AttackUs` is microseconds and you
  wrote a millisecond number. 20 is the fastest attack the effect has. Correction 3.
- **The mix gradually got worse across a session, or is worse with more players** — an `EnsureBus`
  that guards the bus but adds its effects unconditionally, stacking a duplicate chain per caller.
  Pattern 1. Check `GetBusEffectCount(busIdx)` against what you expect.
- **Audible clicks or zipper noise while a parameter moves** — the per-step jump is too large, not
  the write rate too high. Pattern 4. If it also whistles, `Resonance` is nonzero on a moving
  cutoff.
- **A sweep feels like it does nothing and then happens all at once** — linear interpolation in Hz.
  Interpolate in the log domain.
- **`InvalidCastException` on a cached effect, or the wrong effect responds** — index-based
  `GetBusEffect` against a chain something else reordered. Pattern 3.
- **Writes to a cached effect silently stop working** — the effect was removed from the bus; the
  reference is still alive and still accepts writes. Pattern 3's closing hazard.
- **Distortion adds a noise floor at low `Drive`** — raise `PreGain` into the nonlinearity rather
  than cranking `Drive`; the distortion curve's output depends on input amplitude, which the class
  doc states explicitly. And under `Overdrive`, `Drive` is not the knob at all.
- **Setting `Drive = 0` did not bypass the distortion** — it does not. *"An enabled distortion
  effect still changes the sound even when `Drive` is set to 0. This is not a bug."* Use
  `SetBusEffectEnabled`.
- **Sounds are grittier at high frequencies than the recipe suggests** — aliasing from the
  synthesis, not the chain. EQ treats it; band-limiting fixes it (pattern 6).
- **A positional sound stopped telling you where it was after a chain change** — a stereo-widening
  effect (chorus, stereo enhance, reverb `Spread`) or an `AudioEffectPanner` overriding the 3D pan.
  Pattern 6.
- **Green headless suite, wrong-sounding game** — expected. See *Caveats*.

## Caveats

- **No numbers as law.** 700 Hz, 12 dB/octave, 6 octaves/second, ratio 4, threshold −18 dB, "4–6
  effects per bus" — every one is a starting point with a stated reason and every one is a headed
  playtest or profiler call. Nothing here has been measured on the GTX 970 floor.
- **Verified is marked; unverified is marked.** Everything checked against `GodotSharp` 4.7.0 says
  so with the doc line. `GetBusEffect`'s cost, `SetBusEffectEnabled`'s effect on DSP work, relative
  effect costs, the EQ band-index base, the default output-latency buffer size, and the
  sidechain-versus-auto-disabled-bus interaction are all marked `unverified against Godot 4.7`.
  Brief code samples are not evidence — four of them were wrong.
- **`scripts/voice/` is out of bounds.** `VoiceConfig`'s values are marked *"Do not fork these."*
  **Never add DSP to the `Voice` bus.** The `PA` bus is voice's own routed effect chain and is
  likewise not this skill's to retune — the inert `Drive = 0.28f` is reported, not fixed.
  World-audio-versus-voice interaction is a mix question and lives entirely on the world side; the
  sidechain pattern above is what that looks like done correctly.
- **No affect.** This skill never argues that a sound should exist, never decides what a sweep
  means or when it fires, and never sets a device's cadence. Those are `/direct`, `vfx-audio-sync`
  and `vfx-escalation`. Reverb zoning is `sound-spatial-audio`; frequency lanes are
  `sound-soundscape-construction`; budgets are `sound-optimization`.
- **Not a resolver.** `THRILL-BIBLE.md` §9's tone **ratio** is open (the axis is decided as of
  2026-07-26; the ripeness trigger is the first playtest in which anyone is actually frightened) and
  `Lofi` versus `Overdrive` versus `Chorus` is a commitment against it — flag which position a chain
  takes, never pick one silently. §6.2's night-floor conflict and §4.4's spike ceiling
  (`[research default — pending Talon confirmation]`) stay open. `LEVEL-BIBLE.md` §8.3's
  `[BLANK — Talon]` on a dedicated always-meaningful diegetic channel would impose a reserved bus
  and a reserved frequency lane on everything this skill builds, and it is not this skill's to fill.
- **Headless cannot hear, but it can hear *about* wiring.** This is the one place in the family with
  real CI leverage and it should be used: after a level loads, `AudioServer.GetBusIndex(name) >= 0`
  proves the bus exists, `GetBusEffectCount(idx)` and `GetBusEffect(idx, i) is AudioEffectX` prove
  the chain is present in the stated order, and calling `EnsureBus()` twice and re-asserting
  `GetBusEffectCount` proves the idempotency contract that pattern 1 says is the expensive silent
  bug. All of that is provable with no audio device. **Whether the chain sounds right is not**, and
  no test in this repo touches an `AudioServer` bus today. `SfxLab`'s synthesis helpers are all
  `private static` and therefore unreachable from a headless self-test, so the `CyclePhase`
  precedent — pull the decision into a `public static` pure function so it can be tested — does not
  apply until someone changes that. The repo shipped an island rotated 90° through a green suite;
  audio has the same exposure with less instrumentation.

*Scope note: written 2026-07-28 against `feat/neighbourhood-exterior` @ `0e715f3`, on which exactly
three `AudioEffect` instances exist in the entire repo — `AudioEffectDistortion`,
`AudioEffectLowPassFilter` and `AudioEffectReverb`, all on the `PA` bus, all for voice, all inside
`scripts/voice/`, which this skill may not touch. There is no world-audio effect chain, no bus
layout resource, no ambient bed, no audio test of any kind, and nothing audio has ever been
profiled. **Current bite: none — this skill's entire subject matter is unbuilt outside a system it
is forbidden to modify.** First real test: the first world-audio bus with a chain on it — most
plausibly a muffled-through-a-door send for the house interior — created idempotently, driven from
replicated occlusion state, and heard in a headed session with more than one player in different
rooms. Passing looks like a player standing outside a closed bedroom door hearing that someone is
talking inside without hearing what they said, the muffle opening and closing with the door with no
click at any speed, a `GetBusEffectCount` assertion that survives the bus being ensured from every
instance in the scene, and a recorded profiler number replacing every cost claim in this file that
is currently marked unverified.*
