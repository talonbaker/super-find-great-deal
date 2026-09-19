---
name: sound-soundscape-construction
description: Use when a Sail ambient bed must be layered, frequency-separated, placed in depth, choreographed over time, or mixed against proximity voice without turning to mud — each layer a lane and a bus, positional vs not, entries and exits that never loop audibly, ducking under a speaking teammate.
---

# sound-soundscape-construction

## Overview

The **construction substrate** for Sail's ambient bed. `vfx-audio-sync` establishes that the bed
must be continuous, unremarkable, layered so it can thin, mixed under voice, and on its own bus.
This skill is the layer beneath that sentence: **how those properties are achieved, and what they
cost.** Four axes, and the craft is in keeping them independent:

1. **Frequency separation** — each layer gets a lane, enforced at the bus.
2. **Depth** — in Sail that means which side of a wall you are on, not how far away a tree is.
3. **Temporal choreography** — entries and exits that never resolve into a loop or a pattern.
4. **Level and ducking** — the bed sits under proximity voice, always.

A bed that fails is almost never a bed with the wrong sounds in it. It is a bed whose layers all
live in the same 500–4000 Hz band, all play at once forever, all sit at the same depth, and all
compete with the one channel that carries information. Mud is a **structural** failure, which is
why this skill is structural.

## Directed, not decided

`/direct` and `docs/THRILL-BIBLE.md` decide whether a place should have a bed and what it should
make people feel. `vfx-audio-sync` decides which mode a moment wants, when the bed is withdrawn,
and the sync ratio. `vfx-escalation` owns the session curve any density signal derives from.
**This skill decides only how the thing is built and mixed.** It is closer to `vfx-particles` than
to the affect skills — infrastructure, and honest about it. Its one second-order doctrinal claim:

> §6.3's device works by withdrawing a sound the player stopped noticing. **Whether the player
> stops noticing is decided entirely by construction** — lane separation, choreography that stays
> below attention, a loop the ear never catches. A bed the player is still hearing at minute ten
> cannot be spent. That is a build failure, not a direction failure.

If a proposal here starts arguing about *when* a layer should thin, it has left this skill.

## When to Use

- An ambient bed is being built and its layers need lanes, buses and levels assigned
- An existing bed is muddy, or makes a teammate at conversational distance harder to understand
- The bed needs to read differently indoors and outdoors, or across a doorway
- Layers need entry/exit scheduling that does not become a learnable pattern
- Ducking under proximity voice needs building on the world side of the voice boundary
- The bed's node and voice cost needs reconciling against `SfxLab`'s concurrency budget

**Not for:** whether a bed should exist or what a place should feel like (`/direct`); when the bed
is withdrawn, how often, and the sync ratio (`vfx-audio-sync`, which also owns the two-channel rule
separating dread cues from urgency cues); the cut-and-return envelope of a withdrawal and
partial-lane withdrawal *as a device* (`sound-silence-negative-space`); attenuation model, falloff
substrate and room reverb character (`sound-spatial-audio`); the escalation curve
(`vfx-escalation`); a rising filter sweep as an anticipation device (`sound-anticipation-escalation`);
trigger dispatch and the event bus (`sound-event-wiring`); the concurrency budget itself, voice-steal
policy and audio LOD (`sound-optimization`); the `AudioEffect` toolbox, its ranges and chain ordering
(`sound-real-time-effects`); seeding and generative event scheduling (`sound-procedural-reactive`);
the proximity voice pipeline (`scripts/voice/` — shipped, tuned, out of bounds); the urgency cue
(`/spec-urgency-cue`); frame cost (`vfx-particles`); UI sound (`SfxLab.PlayUi`).

## What exists in the repo today

The negative findings are the ones that matter.

| Thing | State on `feat/neighbourhood-exterior` |
|---|---|
| Audio asset files | **Zero.** No `.ogg`, `.wav`, `.mp3` anywhere in the tree. |
| Ambient bed | **Does not exist.** No continuous or looping audio of any kind; every playback path is one-shot. |
| `Ambient` bus | **Does not exist.** Buses are `Master`, `Sfx`, `Voice`, `VoiceCapture`, `PA`. |
| `default_bus_layout.tres` | **Does not exist.** Every bus is created in code, via `SfxLab.EnsureBus`'s five-line shape. |
| `scripts/game/sandbox/SfxLab.cs` | Shipped. 12 synthesised one-shots, pooled `AudioStreamPlayer3D`, `Sfx` bus, the concurrency budget. |
| `scripts/voice/` | Shipped, tuned. `ProximityUnitSize = 6.0f`, `ProximityMaxDistance = 24.0f`. `VoiceManager.EnsurePaBusName`'s distortion + lowpass + reverb chain is the repo's only `AudioEffect*` use. |
| `scripts/ui/SettingsPanel.cs` | Master slider, Voice slider, mic dropdown. **No Sfx slider, no Ambient slider.** |
| Audio tests | **None.** No test asserts anything about an `AudioStream`, an `AudioServer` bus, or `SfxLab`. |
| The bed spec | `docs/superpowers/2026-07-26-wp-ambient-bed-dispatch.md` specs `AmbientBed.cs`, an `"Ambient"` bus, `NightWeight(phase)`, `Withdraw`/`Restore`; a `feat/ambient-bed` branch exists. **None of it is on this branch, and `scripts/dev/` does not exist here.** |

**Do not contradict the bed spec.** It picks synthesis over assets, one `"Ambient"` bus routed to
Master, `SfxLab.PlayStream3D` reuse for the sparse positional event layer, and `NightWeight` as a
pure function extracted for headless testing. This skill builds on those choices.

## There are no loops to choose

The brief's core performance advice is *"prefer fewer well-chosen loop layers over many one-shot
random triggers."* **Sail has no loops to choose.** Every sound is synthesised in code: `SfxLab`
renders float arrays through `Render`/`Envelope`, converts to 16-bit mono via `ToWav`, and caches
the `AudioStreamWav` at first use — *"No asset files, no licensing, and the whole palette is tunable
from one place."*

- **None of the 12 recipes is usable as a bed layer.** `Envelope` returns
  `a * Mathf.Pow(1f - u, curve)` — it reaches zero at `u = 1` by construction, so every recipe is a
  decaying one-shot. A bed needs *sustained* material: filtered noise for wind and room tone, a low
  oscillator bank for hum, sparse grains for insects. New synthesis family, not a parameter change.
- **`Render`, `Envelope`, `ToWav` and every recipe are `private static`** — unreachable from a
  headless self-test and from a sibling file. A bed synthesiser either duplicates the plumbing or
  makes a narrow append-only addition to `SfxLab`. Duplicating is the lower-risk call on a shared
  file; say which was chosen.

**The alternative is an asset-import decision nobody has made** — the first `.ogg` in the repo, an
import pipeline, a licence audit, a repo-size call. Surface it; do not write a bed that assumes a
sample library exists, and do not quietly create one.

**Looping a synthesised buffer.** **Verified against Godot 4.7**: `AudioStreamWav.loop_mode`
`LOOP_FORWARD = 1` is *"Audio loops the data between loop_begin and loop_end, playing forward
only,"* the points being *"in number of samples."* `ToWav` never sets them, so it emits
`LOOP_DISABLED`; a bed writer has to. 16-bit mono at 48 kHz costs 96 kB/s, so a 30 s wind layer is
~2.9 MB — cheap; a five-minute layer rendered *to avoid having a loop point at all* is ~29 MB and
is the wrong trade, because pattern 3's coprime answer costs kilobytes. `ToWav` also hardcodes
`Stereo = false`, right for a positional layer and wrong for a non-positional one (mono reads as
dead-centre and in-your-head) — a stereo air layer needs its own writer.

## The concurrency arithmetic does not close

**This is the most important finding in this file.** `SfxLab.cs:42-44`, verbatim:

> `/// <summary>Pool size. §4 budgets ≤24 concurrent 3D players INCLUDING the 5 voice`
> `/// speakers and ambience; 14 one-shot slots keeps the worst case comfortably inside.</summary>`

```
  24   concurrent 3D players, total budget
 − 5   voice speakers (6 players → 5 remote)
 −14   SfxLab one-shot pool at full stretch
 = 5   3D players left for the entire ambient bed
```

The brief asks for **8–14 always-playing ambient emitters per region**. `14 + 14 + 5 = 33`. Even
its floor of 8 gives 27. **It is over by 3 to 9 voices in the exact moment the budget was written
for** — six players' footsteps during a group panic, which is also the moment atmosphere matters
most. **What actually fits is five.** Everything else is a trade someone has to choose (the budget
itself, and any argument for moving it, is `sound-optimization`'s — this is the bed's share of it):

| Option | Buys | Costs |
|---|---|---|
| Five positional layers, no more | Fits today, no argument | Thin across a whole neighbourhood ring, with nothing left for spatial variety |
| Non-positional layers on `AudioStreamPlayer` | Free against a budget written in *3D* players; trivially withdrawn as a group | Identical everywhere — no audio topology, no zone distinguishability. Still costs a mixer voice; only the spatial slot is saved |
| `AudioStreamPolyphonic` on one 3D player | Many layers, one node — **the biggest single lever here** | All sub-streams share one position and one attenuation curve: buys layer *count*, not spatial *spread* |
| Shrink the `SfxLab` pool to 10 | Frees 4 slots immediately | 14 was chosen to stay "comfortably inside"; at 10 the round-robin steal becomes audible with six players walking |
| Raise the ≤24 budget | Everything | **Nothing here has ever been profiled.** The 24 is a bible figure, not a measurement. Legitimate only with a profiled number on the GTX 970 floor attached — a decision to surface, not to take |

**The polyphonic lever, verified.** `AudioStreamPolyphonic` is *"AudioStream that lets the user play
custom streams at any time from code, simultaneously using a single player"* — **Verified against
Godot 4.7**; `polyphony` defaults to 32. Get `AudioStreamPlaybackPolyphonic` from
`GetStreamPlayback()` after assigning the stream; `play_stream(stream, from_offset, volume_db,
pitch_scale, playback_type, bus)` returns an `int` id (`INVALID_ID = -1` on failure), with
`set_stream_volume(id, db)` and `stop_stream(id)` — a per-layer volume handle *and* a per-layer
lifetime handle on one node.

Marked honestly: whether one polyphonic 3D player costs the mixer one voice or N is **unverified
against Godot 4.7** — the doc's claim is about the node and the API. And
`AudioStreamPlayer3D.max_polyphony` defaults to `1` (**verified**: *"The maximum number of sounds
this node can play at the same time"*); its interaction with the stream's own `polyphony` is
**unverified**. Check both on the first headed run before betting a layer count on them.

**Rule out `AudioStreamSynchronized`.** **Verified against Godot 4.7:** *"The streams begin at
exactly the same time when play is pressed, and will end when the last of them ends"*
(`MAX_STREAMS = 32`). Built for stems that start together, which locks you out of axis 3.

### A silent layer still costs a voice

The brief's scheduler starts **every** layer at `VolumeDb = -80f` and calls `Play()`. Nothing in the
4.7 docs guarantees the mixer skips an inaudible player, and the budget is written in *concurrent
players*, not audible ones. A 14-layer bed parked at -80 dB costs 14 voices from the first frame and
produces no sound for them.

**The verifiable consequence is worse than the budget.** `SfxLab.Rent` selects a slot by
`if (!p.Playing)`. A continuous layer rented from that pool is `Playing` forever and never returns,
permanently shrinking the one-shot pool — or, once the pool is full, gets stolen mid-bed by the next
footstep (`victim.Stop()`). **Never rent an `SfxLab` slot for a continuous layer.** The bed spec's
`PlayStream3D` reuse is correct *only* for the sparse positional event layer, which is one-shots by
construction. Allocate on demand instead: `play_stream` on entry with a randomised `from_offset`
(which also solves pattern 3's loop-identity problem), `stop_stream` when the fade reaches zero. A
layer that is not in the mix costs nothing.

## Core patterns

### 1. Lanes, enforced at the bus — and the slope that makes them real

Assign each layer one frequency lane and keep it there. Enforce at the mix bus, never per emitter:
one filter instance per lane serves every layer routed to it, and lane character stays editable in
one place.

**Verified against Godot 4.7.** `AudioEffectLowPassFilter` and `AudioEffectHighPassFilter` **do**
exist as separate classes, both inheriting `AudioEffectFilter` — *"A 'filter' controls the gain of
frequencies, using cutoff_hz as a frequency threshold"* — alongside `AudioEffectBandLimitFilter`,
`AudioEffectBandPassFilter`, `AudioEffectNotchFilter` and two shelf filters. `cutoff_hz` defaults to
`2000.0`, *"Value can range from 1 to 20500."* So the brief's
`new AudioEffectLowPassFilter { CutoffHz = 500f }` is valid C#, and `VoiceManager.EnsurePaBusName`
already does exactly this. `AudioEffectEQ` is a **base class** (**verified**: inherited by
`AudioEffectEQ6/10/21`), which the brief instantiates directly; `set_band_gain_db` /
`get_band_gain_db` / `get_band_count` exist as documented. Prefer paired filters for lane
definition — an EQ is for carving a notch out of a layer already in its lane.

**What the brief omits is the slope, and without it a lane is not a lane.** `AudioEffectFilter.db`
defaults to `0` = `FILTER_6DB` — *"Steepness of the cutoff curve in dB per octave... Higher orders
have a more aggressive cutoff"*, with `FILTER_6DB/12DB/18DB/24DB` (**verified**). A 6 dB/octave
low-pass at 500 Hz still passes 2 kHz only 12 dB down. That is a **tilt**, and eight layers tilted
at each other is precisely the mud the axis exists to prevent. Reach for `FILTER_18DB` or
`FILTER_24DB` where a boundary is load-bearing, accepting that steeper filters ring — a starting
point checked by ear, not a rule.

The lane table, **retargeted to a 90s house and its neighbourhood ring**. The brief's forest
vocabulary survives only where the exterior genuinely justifies it.

| Lane | Hz | Inside the house | Outside on the ring | Voice risk |
|---|---|---|---|---|
| sub / rumble | 20–120 | furnace cycling two floors down; a weight shifting in the attic above the ceiling | a truck on the far road; distant thunder | none — below speech |
| low-mid | 120–500 | fridge compressor; forced-air ducting; wind pressing a window | a car two streets over; wind in the birches | low — masks vowel body if loud |
| mid | 500–2000 | a TV through a wall; the house settling; a screen door | a dog three lots over; a sprinkler head | **highest** |
| high-mid | 2000–6000 | the fridge's whine; a wall clock; a smoke-detector chirp | cicadas at a window; leaf rustle | **highest** |
| air | 6000+ | room tone | the ring's general air | moderate — sibilance |

**The finding that falls out of this table is the whole mix rule.** Speech intelligibility lives
roughly 500 Hz–4 kHz, with consonants — the part carrying meaning — concentrated at 2–4 kHz. The two
lanes the brief fills most densely, mid and high-mid, are exactly the two that eat intelligibility.
**In a 90s neighbourhood at night, a continuous cicada bed is the single most dangerous layer you
can write**, because it sits on consonants and never stops. Weight the bed toward sub, low-mid and
air; use mid and high-mid **sparsely and transiently** — one distant dog, one cricket, a clock tick
— routed through the positional event layer rather than as continuous lanes.

### 2. Depth is inside-versus-outside, not near-versus-far

The brief's `DepthLayeredEmitter` derives everything from `[Export] float DistanceHint`, then
hard-sets `AttenuationFilterCutoffHz = Lerp(20000f, 3000f, reverbSend)`. **That fights the engine.**
**Verified against Godot 4.7**, `AudioStreamPlayer3D`'s class description: *"Positional effects
include distance attenuation, directionality, and the Doppler effect. **For greater realism, a
low-pass filter is applied to distant sounds.**"* `attenuation_filter_cutoff_hz` defaults to `5000.0`
(*"A sound above this frequency is attenuated more than a sound below this frequency"*) with a
companion `attenuation_filter_db` (default `-24.0`), and is disabled by setting the cutoff to 20500.

So it is a **per-emitter character ceiling the engine already modulates by distance**, not a depth
dial to write each frame. Setting 20000 for a "near" emitter switches Godot's own rolloff off;
hard-setting 3000 for a "far" one leaves it muffled when a player walks up to it. Set it once and
let the engine do the distance part. `unit_size` (default `10.0`) and `max_distance` (default `0.0`,
effective only above zero) exist as the brief writes them — **verified** — and lerping `unit_size`
is defensible, because it is a distance factor and not a frequency.

**Sail's real depth axis is a wall.** *"The inside is where it's scary."* A house bed and a street
bed are two different beds, and the doorway between them is the most audible transition in the mix.
The brief's hand-rolled `reverbSend` float is unnecessary — **verified against Godot 4.7**,
`Area3D.audio_bus_override` / `audio_bus_name` (*"the area's audio bus overrides the default audio
bus"*) routes every emitter inside an interior volume to `Ambient_Interior`, `reverb_bus_enabled` /
`reverb_bus_name` / `reverb_bus_amount` / `reverb_bus_uniformity` give a real per-region reverb
send, and `AudioStreamPlayer3D.area_mask` *"determines which Area3D layers affect the sound for
reverb and audio bus effects."* Reverb *character* is `sound-spatial-audio`'s.

**The honest limit, which the brief never reaches:** Area3D audio routing is **emitter-side**. It
gives the fridge a kitchen reverb and leaves the cicada dry. It does **not** muffle the outdoor bed
when the *listener* steps inside. That is listener-side, and needs either a bed duplicated per region
and crossfaded at the threshold (more voices — see the arithmetic) or a low-pass cutoff on the
`Ambient` bus driven by the local player's region (one filter, no extra voices, but the whole bed
moves as one). **State which was chosen.** Either way crossfade across the doorway's depth rather
than swapping buses on a trigger — a hard swap is audible as a jump, and a jump in the bed is the
one thing `vfx-audio-sync`'s device must own exclusively.

### 3. Choreography that stays below attention

- **Coprime loop lengths.** Two layers at 17 s and 23 s realign as a pair every 391 s. Two at 20 s
  and 30 s realign every 60 s and will be caught. *Coprime*, not merely different, is the
  load-bearing word. Randomise each layer's `from_offset` on entry so a re-entry does not start
  where the last one did.
- **Staggered entries and exits.** A layer holds, fades out over several seconds, stays out, returns.
  The brief's 2–20 s delay / 3–8 s in / 30–90 s held / 5–12 s out is a plausible starting shape; the
  irregularity is what matters, not the averages.

**The rule this skill adds:** the bed's own choreography must stay *below the threshold at which a
player would consciously notice a change*, so that the one change they do notice is the directed
one. `vfx-audio-sync` owns a device whose entire power is an abrupt, noticeable loss; a bed visibly
breathing on its own has spent that contrast before the director arrives. Thin slowly, thin
partially, and never take the lane currently carrying the bed's identity.

**Replication.** The texture schedule may run per-client — texture has no shared stake and
simultaneity is not a property of it. **The moment a layer entry means something it stops being
texture**, and belongs on `vfx-audio-sync`'s server-authored, `Reliable`, late-join-synced path, the
treatment `CycleDriver.SendPhaseTo` gives a joining peer. That is the test: if you would care that
two players heard it in the same second, it is not this skill's to schedule.

### 4. The scheduler shape — the brief's is broken four ways

The brief's `AmbientLayerScheduler` uses `async void ScheduleFadeIn(...)`, awaits a `SceneTreeTimer`,
tweens volume, then *immediately* calls `ScheduleNextCycle(layer)`. In order of severity:

1. **`ScheduleNextCycle` fires after the fade-in tween is *started*, not after it finishes.** The
   30–90 s hold begins during the 3–8 s fade, so the hold is silently short by the fade duration —
   and when a hold expires while a fade still runs, two live tweens write the same `VolumeDb` on one
   frame and the layer stair-steps. Across a session the layers drift out of their tuned relationship.
2. **`async void` is unrooted and unobservable.** No `Task`, no cancellation, and an exception has
   nowhere to go. There is no way to stop the schedule except freeing the node.
3. **Awaiting a `SceneTreeTimer` across 90 s on a node that may be freed** resumes the continuation
   against a disposed `GodotObject`. A scene change inside a 90 s await is not an edge case in a game
   with a bedtime ritual in it.
4. **Tweens die differently from awaits.** **Verified against Godot 4.7**: a bound Tween *"will halt
   the animation when the object is not inside tree"* and *"will be automatically killed when the
   bound object is freed"*, and `finished` is *"Never emitted when the Tween is set to infinite
   looping."* Half the schedule silently stops while half keeps running — the hardest class of audio
   bug to reproduce.

**The correct shape is boring on purpose:** one node, one `_Process`, an array of layer structs
holding `{ int StreamId, State, float Remaining, float Gain }`, advanced by `delta`. No `async`, no
`SceneTreeTimer`, no per-layer Tween. It cannot outlive its node, it is cancellable by not
processing, and it collapses the brief's own "CPU spikes when >15 emitters each run a tween" entry
into a non-problem. Extract the envelope as a pure `public static` function so a headless test can
assert it without an audio device — the seam `CyclePhase.FromElapsed` gives the clock, and the one
the bed spec asks for around `NightWeight`. That is the only part of a bed CI can prove.

### 5. Ducking under proximity voice

**This is the mix rule that matters most**, and `vfx-audio-sync` sets the standard: a bed that makes
a teammate at 10 m harder to understand has cost more than it bought. Set the bed's level against a
teammate speaking at conversational distance, never in isolation.

**Godot has a real sidechain and the brief does not use it.** `AudioEffectCompressor.sidechain` —
**Verified against Godot 4.7**: *"Audio bus to use for the volume threshold detection."* Put the
compressor on the **`Ambient`** bus with `Sidechain = "Voice"`. The Voice bus is only *read* as a
detector; **no DSP is added to it and no `VoiceConfig` value is forked**, so this sits cleanly on the
world side of the boundary. It also ducks *proportionally* — the Voice bus carries the 3D-attenuated
output of `VoiceSpeaker`, so a teammate at 20 m ducks less than one at 3 m.

Its expressible range is narrower than the brief assumes: `attack_us` is **20–2000 microseconds**
(0.02–2 ms), `release_ms` 20–2000, `threshold` -60–0 dB, `ratio` 1–48, `gain` -20–20 (**all
verified**). So `DuckAttackSec = 0.05f` — 50 ms — **is not expressible with the compressor at all**,
and `DuckAmountDb = -10f` has no direct parameter; depth is a consequence of threshold and ratio.
`DuckReleaseSec = 0.6f` maps fine to `release_ms = 600`. The manual bus-volume tween is therefore a
legitimate *second* mechanism, not a substitute: sidechain when the duck should track how loud and
how near the speaker is, tween when a slow, deliberately unnatural duck is wanted for a beat. If
tweening, randomise attack and release ±20% per trigger so it does not read as machinery.

**Ducking is not a substitute for lane discipline.** A bed needing 10 dB of duck to make voice
intelligible was written into the wrong lanes. Fix pattern 1 first.

## The bus tree

Buses are created in code — there is no bus layout resource. A per-lane scheme means N new buses,
each one `EnsureBus`-shaped code somebody maintains. **Lanes send to a parent `Ambient`, and only
`Ambient` sends to `Master`** — otherwise the bed cannot be ducked, withdrawn or volume-controlled
as one group, which is exactly what `vfx-audio-sync` needs it to be. The *topology* is this skill's;
the effect classes hung off it, their ranges and chain ordering are `sound-real-time-effects`'.

```csharp
// Mirrors SfxLab.EnsureBus: lazy, idempotent. Order is load-bearing — SetBusSend names a
// bus, so the parent must exist first.
private static int EnsureBus(string name, string send)
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

private static void EnsureAmbientTree()
{
    int ambient = EnsureBus("Ambient", "Master");        // group handle: duck, withdraw, slider
    AudioServer.AddBusEffect(ambient, new AudioEffectCompressor
    {
        Sidechain = VoiceConfig.OutputBus,               // "Voice" read as a detector only
        Threshold = -24f, Ratio = 4f, ReleaseMs = 600f,  // starting points, tuned by ear at 10 m
    });

    int lowMid = EnsureBus("Ambient_LowMid", "Ambient");
    AudioServer.AddBusEffect(lowMid, new AudioEffectLowPassFilter
    {
        CutoffHz = 500f, Db = AudioEffectFilter.FilterDB.Filter18Db, // 6 dB/oct is a tilt
    });
    // ...one bus per lane the bed actually uses. Never create a lane nothing routes to.
}
```

`AddBusEffect(bus_idx, effect, at_position = -1)`, `SetBusSend`, `GetBusIndex` returning `-1` when
absent, `SetBusVolumeDb`, `SetBusVolumeLinear` and `SetBusEffectEnabled` all exist with those
signatures — **verified against Godot 4.7**. `linear_to_db` / `db_to_linear` are `Mathf.LinearToDb` /
`Mathf.DbToLinear` in C# — **verified** (*"Math global functions... are located under `Mathf`"*).
Convert at the edge and hold layer gain as linear 0–1 internally; a crossfade in dB is not the curve
anyone expects.

**`SettingsPanel` has no Ambient slider.** A bed the player cannot turn down is a support ticket. Add
exactly one, on `Ambient` — not five on the lanes, which just lets a player carve the mix into mud.

## Frequency is logarithmic — a lerp in Hz sweeps wrong

`Mathf.Lerp(20000f, 3000f, t)` reaches 11500 Hz at `t = 0.5`. That is under one octave below 20 kHz,
while 3000 Hz is nearly two octaves below *that* — half the parameter's travel covers a small
fraction of the perceived change, and the audible move bunches at the end. Every cutoff interpolation
in the brief has this bug.

```csharp
static float LerpHz(float from, float to, float t) => from * Mathf.Pow(to / from, t); // both > 0
```

A lane crossfade, a region filter following a player through a doorway, and any cutoff that moves at
all are subject to this. **A rising sweep used as an anticipation device belongs to
`sound-anticipation-escalation`** — flagged here only because lane boundaries are this skill's
subject.

## Tuning guide

Starting points with reasons. None is a gate.

- **Five positional bed voices** is what the budget leaves after voice and the one-shot pool.
  Arithmetic, not taste — and the number to argue *with*, by profiling, not around. The brief's 8–14
  is a dense-forest figure for a game that is a house and a street with 2–6 players.
- **`FILTER_18DB` where a lane boundary is load-bearing**, `FILTER_6DB` where a gentle tilt is all
  that is wanted. Steeper filters ring; check by ear.
- **Coprime loop lengths, both under ~30 s.** Long enough not to be caught, short enough that memory
  stays trivial. 17 s and 23 s realign every 391 s.
- **Bed level set at 10 m against a speaking teammate**, because that sits inside
  `ProximityMaxDistance = 24.0f` and is roughly where a group actually talks. If a word has to be
  repeated, the bed is too loud regardless of how it sounds alone.
- **Sub and air lanes carry the bed's weight; mid and high-mid stay sparse.** Intelligibility is the
  reason, and it is the least negotiable line in this file.
- **Duck depth is whatever makes speech clear and nothing more.** A duck deep enough to notice has
  turned the bed into an event, and events belong to `vfx-audio-sync`.

## Integration points

- **`SfxLab`** — `EnsureBus`'s shape, the pooling doctrine, and the ≤24 budget the bed fits *inside*
  rather than beside. `PlayStream3D` for the sparse positional event layer only, never continuous.
- **`scripts/voice/`** — read-only. `VoiceConfig.OutputBus` as a sidechain *detector*; its attenuation
  values as the yardstick the bed's level is set against. Nothing else.
  **`scripts/ui/SettingsPanel.cs`** — one slider on `Ambient`, following the Master/Voice pattern.
- **`CycleDriver.Instance.Phase`** — the shipped clock, cyclic in `[0,1)` over 120 s, not monotonic.
  Gate reads on `Synced`. `NightWeight(phase)` consumes it and must stay continuous across the wrap.
  Any session-monotonic density signal is derived, and that derivation is `vfx-escalation`'s.
- **`vfx-audio-sync`** owns `Withdraw`/`Restore` and when they fire; the cut-and-return envelope is
  `sound-silence-negative-space`'s. This skill hands both a bed with one group handle (`Ambient`) and
  per-layer gain handles, and stops.
- **`ActorFx` + `EventResponse`** — the shipped data-driven dispatch precedent, and
  `sound-event-wiring`'s territory if the bed ever needs external triggers. `AtmosphereEventBus` is
  **deliberately not built** (`docs/ATMOSPHERIC-VFX-INTEGRATION.md` §3.4). `scripts/telemetry/` is
  live with zero audio hooks; counting layer entries and duck activations is buildable on what ships.

## Precedent

- **`SfxLab.cs`** — the whole house contract: lazy `EnsureBus`, render-once-and-cache, a bounded pool
  with prefer-idle-then-steal-oldest, lazy re-anchoring across scene changes, and the only written
  concurrency budget in the repo.
- **`VoiceManager.EnsurePaBusName`** — proof the repo already builds a bus with a full effect chain in
  code (`AudioEffectDistortion` → `AudioEffectLowPassFilter` → `AudioEffectReverb`). The bus tree
  above is the same object at larger scale.
- **`CycleDriver.cs`** for replication discipline, and **`CyclePhase.FromElapsed`** as the house
  precedent for pulling a decision out of a `Node` into a pure static function purely so it can be
  tested headless. The envelope and `NightWeight` both want that treatment.
- External: the brief claims **Subnautica** and **The Forest** — exterior natural beds whose lane
  vocabulary transfers to the ring and not to the house. Sail's interior half (a domestic room tone
  with an appliance carrying the low end and almost nothing in the mids) has different reference
  points, *Resident Evil 7* and *P.T.*, named as reference points rather than measured techniques.
  **Phasmophobia** is `vfx-audio-sync`'s precedent for the withdrawal and is not re-litigated here.

## Troubleshooting

- **The bed is mud** — check lane *overlap* before adding EQ, and the filter *slope* before believing
  the lanes exist. A 6 dB/octave filter at 500 Hz still passes 2 kHz.
- **Teammates are harder to understand once the bed is in** — mid and high-mid lanes. Fix the lanes;
  ducking a bed that sits on consonants only makes the duck deeper.
- **The bed sounds empty and volume does not help** — a temporal problem, not a level problem. Add
  variation and staggering before gain.
- **One-shots start stealing from each other as soon as the bed comes up** — a continuous layer was
  rented from the `SfxLab` pool. `Rent` tests `!p.Playing`, so it never comes back.
- **An audible loop point** — coprime lengths, randomised `from_offset`. If it is a click rather than
  a repeat, `loop_begin`/`loop_end` are not on a zero crossing.
- **A lane's bus effect is inaudible** — the emitter's `Bus` names a different bus, or the lane bus
  was created before its parent existed and `SetBusSend` had nothing to connect to.
- **A distant-tagged emitter sounds muffled up close** — `AttenuationFilterCutoffHz` written from a
  distance hint. It is a per-emitter ceiling; the engine already applies the distance part.
- **A filter sweep spends most of its time doing nothing audible** — linear Hz.
- **Layers drift, or a fade stair-steps** — the `ScheduleNextCycle`-before-`Finished` defect, or two
  live tweens on one `VolumeDb`.
- **CPU spikes when the bed fades** — a tween per layer. One `_Process` over an array.
- **Green headless suite, no audible bed** — expected. **Headless cannot hear.** The repo has shipped
  an island rotated 90° through a green suite; audio has the same exposure and less instrumentation.

## Caveats

- **No numbers as law.** 24, 14, 5, 500 Hz, 17/23 s, `FILTER_18DB`, -24 dB threshold, 600 ms release,
  10 m — every one is a starting point with a stated reason, and the reason is what transfers. The one
  figure behaving like a constraint is the ≤24 budget, and even that is a bible number nobody profiled.
- **Unverified is stated, not implied.** Verified against Godot 4.7: `AudioEffectFilter` and its
  low-/high-pass subclasses, `FilterDB`, `AudioEffectEQ` as an abstract base with `SetBandGainDb`,
  `AudioStreamPlayer3D.AttenuationFilterCutoffHz` / `UnitSize` / `MaxDistance` / `MaxPolyphony` /
  `AreaMask` and the automatic distance low-pass, `Area3D`'s audio and reverb bus properties,
  `AudioEffectReverb` and `AudioEffectCompressor` defaults and ranges, `AudioStreamPolyphonic` and
  `AudioStreamPlaybackPolyphonic`, `AudioStreamSynchronized`, `AudioStreamWav.LoopMode`,
  `AudioServer`'s bus methods, `Tween` binding and `Finished`, `Mathf.LinearToDb`. **Unverified:**
  whether one polyphonic 3D player costs one mixer voice or many; how `MaxPolyphony` interacts with a
  polyphonic stream's own `polyphony`; whether Godot skips a player at -80 dB. All three bear on the
  budget and need a profiled headed run before anything is bet on them.
- **Not the voice pipeline.** Reading the `Voice` bus as a sidechain detector is world-side and
  legitimate. Adding an effect *to* `Voice`, or forking `ProximityUnitSize` / `ProximityMaxDistance`,
  is not, in any circumstance.
- **Not a resolver.** `LEVEL-BIBLE.md` §8.3 is `[BLANK — Talon]` on whether Sail commits to a dedicated
  diegetic audio channel that is *always* meaningful — the bible's own note is that it *"constrains
  sound design broadly, which is why it is a call rather than a detail."* **It bears directly on this
  skill's premise:** every technique here assumes the bed is texture that falls below attention. If
  Sail commits to the Deep Rock model, a bed whose layers mean nothing may be the wrong bed entirely,
  and axes 1 and 3 would need rewriting around layers that each carry system state. Flag it; build
  toward neither answer. `THRILL-BIBLE.md` §6.2's night-floor conflict is a shipped decision
  (`DayNightSky`'s `MinAmbientEnergy = 0.30f`) in live opposition to doctrine, and the night bed is
  its audio analogue — a night layer that reads as *less* versus a night that stays navigable.
  Ripening, not ripe. §9's tone **axis** is decided (2026-07-26, dread-forward, atmosphere named first
  by Talon); the **ratio** is not, and it bears on the register of the texture itself — a sincere
  fridge hum and a comically wet sound from above the ceiling are both defensible under different
  ratios. Ripeness trigger unchanged: the first playtest in which anyone is actually frightened.
  §4.4's spike ceiling stays `[research default — pending Talon confirmation]` and is not this
  skill's.
- **A bed that fits the budget is not thereby warranted.** Making layers affordable is not an argument
  that a place should have them. That argument comes from `/direct` or it does not come.
- **Headless cannot hear.** `tests/Run-*.ps1` can prove `NightWeight` is continuous across the phase
  wrap, that an envelope reaches zero at its stated fade time and restores to the gain it left rather
  than a default, and that a bus tree was built with the right sends — all pure functions and
  `AudioServer` state, all worth testing. It cannot prove separation, depth, loop audibility or
  intelligibility. Each of those needs a headed run with more than one human talking.

*Scope note: written 2026-07-28 against `feat/neighbourhood-exterior` @ `0e715f3` — a branch with no
ambient bed, no `Ambient` bus, no `scripts/dev/`, zero audio asset files anywhere in the tree, and no
test that touches an `AudioStream` or an `AudioServer` bus. The bed spec
(`docs/superpowers/2026-07-26-wp-ambient-bed-dispatch.md`) and a `feat/ambient-bed` branch exist
elsewhere and are not here. Nothing has been profiled on the GTX 970 floor. **Current bite:
essentially none — there is no bed to layer, and the first thing this skill does when invoked is
report the concurrency arithmetic rather than build.** First real test: a synthesised bed running for
a full session across the neighbourhood ring and the house interior, on a headed multiplayer run with
4–6 humans, crossing the doorway in both directions, voice live throughout. Passing looks like two
players holding a conversation at ~10 m without anyone asking for a word to be repeated, the doorway
crossing heard as a place changing rather than a bus switching, and nobody afterwards able to name a
single layer they were listening to.*
