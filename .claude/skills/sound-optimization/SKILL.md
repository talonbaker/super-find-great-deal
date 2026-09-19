---
name: sound-optimization
description: Use when Sail audio must survive a real 2-6 player session — the concurrent-voice budget, routing positional one-shots through SfxLab's pool, voice-steal policy, cutting distant emitters to cached samples, CPU cost scaling with player count. How many sounds at once, not how far any one carries — that is sound-spatial-audio.
---

# sound-optimization

## Overview

The **performance substrate** for Sail's audio, and the enabling constraint under every other
`sound-*` skill. It answers *how many of these can run at once on the floor spec, what each one
costs, and what happens when the answer is "more than fit"* — and it hands every question about
what a sound should mean back to `/direct` and `vfx-audio-sync`.

Its single most important job is arithmetic. `SfxLab` ships a written concurrency budget; every
other skill in this family cites it; nobody has checked whether the things those skills propose
fit inside it. **They do not, at the numbers the source brief assumes.** *The budget* below does
that arithmetic out loud and rebuilds it honestly.

**Perf floor: GTX 970, Forward+.** `vfx-particles` owns that constraint for the whole family and
this skill inherits rather than restates it. The audio-specific half is the part `vfx-particles`
does not cover: audio is **CPU and mix-thread cost, not GPU**, so it competes with voice decode,
physics and networking rather than with the renderer, and it worsens with player count instead of
with view distance.

## Infrastructure, not affect — stated plainly

`/direct` and `docs/THRILL-BIBLE.md` decide **which** feeling and **why there**. `vfx-audio-sync`
decides which mode a moment wants and how often a device is spent. `vfx-escalation` decides how
hard everything is pushing. This skill decides **what it costs and whether it fits**.

It is the `sound-*` family's `vfx-particles`: **it serves no doctrine section directly and should
be honest about that.** The nearest doctrinal link is second-order and worth stating plainly
rather than dressing up:

> A voice stolen at the wrong moment, or a mix-thread stall during a group panic, destroys the
> exact instant §4.1's **simultaneity** condition depends on. Two players reacting to the same
> sound in the same second is what makes a beat a beat; a shot that got stolen on one client
> means they did not react to anything.

That is the whole claim — do not manufacture more. **And it is not a licence to add a sound.**
Twenty-four concurrent voices that all fit is still a worse soundscape than six that were chosen.
That argument comes from `/direct` or it does not come at all.

## When to Use

- A new audio system needs slots and someone has to say how many are left
- A positional one-shot is being played and the call site is not `SfxLab.PlayStream3D`, or someone
  proposes building a second pool
- Pool exhaustion, audible cutoffs, or a steal policy is being chosen or defended
- A distant emitter is doing full-detail work nobody can distinguish
- CPU cost is observed to scale with player count
- The ≤24 budget is about to be spent, raised, or quietly exceeded
- Audio needs instrumenting so a guessed number can become a measured one

**Not for:** whether a sound should exist or what it should make players feel (`/direct`,
`vfx-audio-sync`); how a bed is layered, separated and choreographed
(`sound-soundscape-construction`); attenuation model, `UnitSize`, `MaxDistance` and reverb zoning
chosen *for localisation* (`sound-spatial-audio` — this skill only cares that `MaxDistance` is set
at all, because that is a CPU decision); the DSP classes themselves (`sound-real-time-effects`);
how a build or crescendo is expressed (`sound-anticipation-escalation`); a threat's voice
(`sound-threat-audio`); what dispatches a cue and how it replicates (`sound-event-wiring`); the
mechanics of a withdrawal's cut and return (`sound-silence-negative-space`); the frame budget for
particles and rendering (`vfx-particles`, which owns
the GTX 970 Forward+ floor for the whole family); the proximity voice pipeline in `scripts/voice/`,
which is shipped, tuned, and out of bounds.

## What exists in the repo today

The negative findings are the valuable ones.

| Thing | State |
|---|---|
| `scripts/game/sandbox/SfxLab.cs` | **The pool.** Fixed `PoolSize = 14` of `AudioStreamPlayer3D` on a dedicated `Sfx` bus, prefer-idle then steal-round-robin, lazy re-anchor, invalid-entry prune. It is the house contract. |
| A second audio pool | **Does not exist and must not be built.** `docs/ATMOSPHERIC-VFX-INTEGRATION.md:204` names spawn-per-sound as anti-pattern #19: *"`PlayUnknownOriginSound` spawning a node per sound … Use `SfxLab.PlayStream3D`."* |
| Audio asset files | **Zero.** No `.ogg`, `.wav`, `.mp3` anywhere in the repo. Every sound is synthesised at runtime. There is no `default_bus_layout.tres`; every bus is created in code. |
| Ambient bed | **Does not exist.** No looping or continuous audio of any kind. Every playback path is one-shot. Specced in `docs/superpowers/2026-07-26-wp-ambient-bed-dispatch.md`, on a branch, not here. |
| Buses | `Master`, `Sfx`, `Voice`, `VoiceCapture`, `PA`. No `Ambient` bus. Every `Ambient*` hit in `scripts/` is lighting. |
| Bus effects | **Three, all on the `PA` bus** (`VoiceManager.EnsurePaBusName`): distortion, lowpass, reverb. That is every `AudioEffect*` use in the repo. |
| `AudioStreamGenerator` users | **One: `VoiceSpeaker`**, one per remote speaker, up to 5 at 6 players, `MixRate = 48000`, `GeneratorBufferSec = 0.2f`. |
| Audio tests | **None.** No `Run-SfxTest.ps1`, no `Run-AudioTest.ps1`, no `Run-AmbientBedTest.ps1`. `SfxLab` has zero coverage. |
| Audio telemetry | **None.** `scripts/telemetry/` is a live Firebase path with zero audio hooks. |
| Profiling | **Nothing in Sail has ever been profiled**, audio or otherwise, on the floor spec or anywhere else. |

Two shipped details that constrain advice this skill would otherwise give freely:

- **`SfxLab`'s synthesis helpers are all `private static`** (`EffortGrunt`, `Thump`, `Bonk`,
  `Warble`, `Sweep`, `Oof`, `TwoNote`, `Squeak`, `Render`, `Envelope`, `EaseOut`, `ToWav`), and so
  are `Rent` and `Pool`. "Extract the pure math and test it, the way `CyclePhase.FromElapsed` was
  extracted" is blocked by **accessibility, not by difficulty** — it is a change to shipped code.
- **`SfxLab.cs:77` references `LabAmbience`, which does not exist anywhere in the repo.** A stale
  comment, not a dependency.

## The budget

**This is the section other skills cite. It has to be arithmetically true.**

### The shipped ceiling, verbatim

`SfxLab.cs:42-44`:

> `/// <summary>Pool size. §4 budgets ≤24 concurrent 3D players INCLUDING the 5 voice`
> `/// speakers and ambience; 14 one-shot slots keeps the worst case comfortably inside.</summary>`
> `private const int PoolSize = 14;`

Restated at `docs/ATMOSPHERIC-VFX-INTEGRATION.md:111` and carried by `vfx-audio-sync`,
`sound-soundscape-construction` and `sound-spatial-audio`. It is the only written audio budget in
the project.

### The source brief's table does not close against it

The brief proposes, for a 5-10 player session: 8-14 ambient layers per region, 10-15 concurrent
creature vocalizations, 1-2 player-feedback one-shots per player, 2-3 full `AudioStreamGenerator`
vocalizations (a subset of the creature figure), 3-4 reverb buses, 15-25 total bus effects.

Do the arithmetic. Take the **floor** of every range at the **bottom** of its player count —
5 players, so 4 remote voice speakers:

```
  8 ambient  +  10 creature  +  (1 × 5 players)  +  4 voice  =  27 concurrent 3D voices
```

**27 exceeds 24 before a single range reaches its midpoint.** At every midpoint with 6 players it
is roughly `11 + 12 + 9 + 5 = 37`, over 1.5× the ceiling; at the top of every range at 10 players
it is past 50. This is not a tight fit that needs care — it is a budget for a different, larger
game, written against a 5-10 player dark forest that Sail is not. **The setting is a 90s suburban
house and neighbourhood ring, 2-6 players.**

The bus-effect figure is wrong differently: it counts a resource Sail spends almost none of (three
effects exist, all on `PA`) and says nothing about the one Sail is short of, which is 3D voices.

### The honest table, rebuilt

Worst case is **6 players**, where the voice speakers peak. Everything below is per-client, since
every client mixes its own.

| Consumer | 3D voices at 6 players | 3D voices at 2 players | Why |
|---|---|---|---|
| `VoiceSpeaker` (`scripts/voice/`) | **5** | **1** | One `AudioStreamPlayer3D` per remote peer. Not negotiable and not this family's to touch. |
| `SfxLab` one-shot pool | **14 (cap)** | **14 (cap)** | Hard-capped by `PoolSize`. Cannot exceed it by construction — `Rent` grows to the cap and then steals. |
| **Left inside ≤24** | **5** | **9** | Everything else. The bed, positional creature layers, anything else positional. |

**Five slots.** That is the entire positional-ambience allocation at full lobby, and nobody has
allocated them. `sound-soundscape-construction` and `vfx-audio-sync` both plan work that lands
here; both must count against these five, not beside them.

Two honest softenings, neither of which changes what to budget against:

- The pool is a **ceiling, not an occupancy**. `Rent` grows lazily, so steady state is usually well
  below 14 — but the worst case *is* the group-panic moment, six players running with every
  footstep firing, which is exactly when the budget matters. Budget the cap.
- `AudioStreamPlayer3D.MaxDistance` removes far voices from mixing entirely. **Verified against
  Godot 4.7:** *"The distance past which the sound can no longer be heard at all … This can be
  used to prevent the `AudioStreamPlayer3D` from requiring audio mixing when the listener is far
  away, **which saves CPU resources**."* (`SfxLab.PlayStream3D` already defaults to 40 m.) That is
  headroom in the **CPU** budget only, and only for sounds actually far away — never in the node
  budget.

### The levers, in the order to reach for them

1. **Make a layer non-positional.** An `AudioStreamPlayer` is not an `AudioStreamPlayer3D` and
   consumes no 3D slot. It still costs a mixed voice and CPU, but it is outside the ≤24 budget.
   **This has a design price:** `vfx-audio-sync` §1 flags positional-vs-non-positional as a real
   fork — a non-positional bed is identical everywhere and flattens §5.4's information asymmetry.
   Take it deliberately and let `sound-soundscape-construction` own the consequence.
2. **Collapse co-located sounds onto one node.** **Verified against Godot 4.7:**
   `AudioStreamPolyphonic` *"lets the user play custom streams at any time from code,
   simultaneously using a single player"*; `AudioStreamPlaybackPolyphonic.PlayStream` returns an ID
   and *"returns `InvalidId` if the amount of streams currently playing equals
   `AudioStreamPolyphonic.Polyphony`"*. One node, N streams, one slot. **The constraint is that
   every stream shares that node's single position** — good for one prop, one creature's stacked
   layers, one room; never for a spread-out bed.
3. **Shrink the one-shot pool.** 14 → 10 frees four slots, at the cost of more steals during a
   panic, which is when steals are most audible. A real trade; make it with a reason.
4. **Raise the ceiling — with a profile behind it.** ≤24 is itself unprofiled; nothing in the repo
   shows it was measured. Raising it is legitimate provided the new number is measured on the floor
   spec **and written back into `SfxLab`'s doc comment**, because that comment is what every other
   skill cites. A budget that drifts in one file and not the other is worse than no budget.

## Core patterns

### 1. There is already a pool. Do not build a second one.

`SfxLab.PlayStream3D` is the one compliant positional path in Sail. It takes a caller-supplied
`AudioStream`, so it already serves synthesised sounds it has never heard of — that is what
`ActorFx` does today (`AudioStream? stream = r.CustomSound ?? …; SfxLab.PlayStream3D(...)`).

What the shipped `Rent` does, and why each part is right:

| Step | Code | Reason |
|---|---|---|
| Validity gate | `if (!IsInstanceValid(context) \|\| !context.IsInsideTree()) return null` | Headless teardown races. `PlayStream3D` then **no-ops silently** — a dropped sound is preferable to a crash, and this is deliberate. |
| Prune | `Pool.RemoveAll(p => !GodotObject.IsInstanceValid(p))` | Nodes die with their scene. Without this the pool fills with corpses. |
| Prefer idle | `foreach … if (!p.Playing) return Reanchor(p, anchor)` | Free before forced. Linear over ≤14 — cheaper than any structure that would replace it. |
| Grow | `if (Pool.Count < PoolSize)` | Lazy. A two-player round never allocates fourteen nodes. |
| Steal | `Pool[_next]; _next = (_next + 1) % Pool.Count; victim.Stop()` | O(1), zero allocation, at peak load. See pattern 3. |
| Re-anchor | under `GetTree().CurrentScene ?? GetTree().Root` | Scene changes cannot orphan-leak the pool. |

**Note what it does *not* do: it never subscribes to `Finished` on a pooled node.** It polls
`p.Playing` at rent time instead. That is not laziness — it sidesteps an entire class of
signal-lifetime bug, which pattern 2 is about. (`PlayUi` does use `Finished += QueueFree`, but that
node is single-use and freed, so nothing can accumulate on it.)

If a new system needs positional one-shots it calls `SfxLab.PlayStream3D`. If it needs something
`SfxLab` cannot do, the change goes **into `SfxLab`** — one pool, one budget, one place the cap is
enforced. Two pools means two caps and no budget.

### 2. The brief's pool has two real bugs. Both are silent.

**Bug A — `Finished` fires on every playback, not once.** The sample wires
`player.Finished += () => ReturnToPool(player);` a single time in `_Ready`. **Verified against
Godot 4.7:** `AudioStreamPlayer3D.Finished` is *"Emitted when the audio stops playing"* — every
time, not once per subscription.

So: rent → play → finish → enqueued. Rent again → play → finish → **enqueued again**. `Queue<T>`
has no set semantics, so the same node now sits in `_available` twice. Eventually `Acquire()` hands
the identical node to two callers in the same frame; both assign `Stream`, `VolumeDb` and
`GlobalPosition`; one wins. A sound vanishes, at the wrong position, under load,
non-deterministically — gradual, silent, load-dependent pool corruption, the hardest bug shape
there is.

(Whether `Stop()` also emits `Finished` is `unverified against Godot 4.7` — the doc line is
ambiguous, and that ambiguity is itself an argument for `SfxLab`'s design, which does not depend
on the signal at all.)

**Bug B — `_all.OrderBy(p => p.VolumeDb).First()` sorts the pool at peak load.** LINQ `OrderBy`
allocates an ordered enumerable, buffers it and sorts — O(n log n) plus allocations — to read one
element. It runs *only* in the exhausted branch: the moment every slot is busy, the frame is under
the most pressure, and a GC pause is least affordable. `SfxLab`'s `Pool[_next]` is O(1) and
allocation-free, and that is not a micro-optimisation — allocation in an every-shot path is how a
busy second becomes a collection.

### 3. Steal the oldest, not the quietest — and treat a steal as truncation, not a fault

The brief steals the quietest voice. That is wrong twice.

**It does not measure what it claims to.** `VolumeDb` on an `AudioStreamPlayer3D` is the *authored*
volume. Distance attenuation happens in the mixer from `UnitSize`, `MaxDistance` and listener
distance — **verified against Godot 4.7:** `UnitSize` is *"the factor for the attenuation effect"*
and `AttenuationModel` *"decides if audio should get quieter with distance"* — and none of it is
written back to `VolumeDb`. Sorting by `VolumeDb` sorts by authorial intent, not by what anyone is
hearing.

**And even if it did, quiet means distant** — and distance is precisely what a threat cue is trying
to communicate. A quietest-first policy systematically deletes the far half of the soundscape: the
footstep from across the house goes before the one at your feet. It optimises for the sounds the
player already knows about.

`SfxLab` steals the **oldest**, and the reasoning is information rather than loudness: a shot
running longest has already delivered most of what it had. Onsets carry the information; tails do
not. Truncating a tail costs a decay envelope; truncating an onset costs the event.

**Frame a forced steal as a deliberate, visible truncation rather than a correctness bug** — the
same framing `vfx-particles` uses for its pool. A bounded pool is *supposed* to steal when it is
full. What is worth knowing is *how often*, which is what to instrument.

The brief gets one thing right here and it should be kept: **steal rather than skip.** A skipped
sound is an event that did not happen; a stolen one happened and got cut short. Only
`PlayStream3D`'s null-`Rent` teardown path skips, and only because there is no tree to play into.

### 4. There is nothing to compress — the real costs are synthesis and generators

The brief's compression section (Ogg Vorbis default, WAV for short retriggered sounds, avoid MP3,
set it per-asset in the import dock) is **moot here, and transcribing it would be misinformation.**
There are zero audio files and no import-dock decision because there is nothing to import. Revisit
only if Sail ever ships an audio asset. What actually costs something:

**Cached `AudioStreamWav` memory is negligible; the first-use spike is not.** `SfxLab.ToWav`
produces 48 kHz, 16-bit, mono — **96 kB per second of audio**. All twelve recipes total about
1.55 s, so the whole palette is **under 150 kB** resident. Memory is not the problem. `Get`
synthesising *on the frame the sound is first requested* is: `Render` evaluates a per-sample lambda
`SampleRate × seconds` times on the calling thread — `Warble` at 0.38 s is 18,240 evaluations of a
`Mathf.Sin` chain, landing on the game thread at exactly the moment the sound was wanted.

**The fix is a warm pass at load** — call `SfxLab.Get` for every `Sfx` value during loading, where
the spike is masked. The engine documents the same reasoning for its own equivalent: **verified
against Godot 4.7**, `AudioServer.RegisterStreamAsSample` warns *"Lag spikes may occur when calling
this method … It is suggested to call this method while loading assets, where the lag spike could
be masked, instead of registering the sample right before it needs to be played."*

**And the scaling breaks at bed length.** A 60-second synthesised ambient loop through the same
path is 5.76 MB and ~2.9 million sample evaluations in one call. That is where
synthesise-to-`AudioStreamWav` stops being free, and the fork is short loop / live generator /
offline bake — `sound-procedural-reactive` and `sound-soundscape-construction` make that call;
this skill supplies the cost model.

**`AudioStreamGenerator` is the expensive one, and Sail already runs five.** `VoiceSpeaker` runs
one per remote speaker. **Verified against Godot 4.7:** `BufferLength` — *"Lower values result in
less latency, but require the script to generate audio data faster, resulting in increased CPU
usage and more risk for audio cracking if the CPU can't keep up."* `VoiceSpeaker` sits at 0.2 s.
Every generator a new system adds competes with voice decode for the same headroom, and **voice
losing is worse than any ambience winning** — proximity voice is §5.1's highest-leverage
instrument and it is shipped and tuned. Budget generators against that, not against silence.

(`AudioServer.PlaybackType.Sample` is not a lever here. **Verified against Godot 4.7:** *"Only
currently supported on the web platform"*, and *"`AudioEffect`s are not supported when playback is
considered as a sample."*)

### 5. LOD is structural, and the engine already does the easy half

**The easy half is free and already correct.** `MaxDistance` culls from the mix, per listener, on
the mixer thread, with no `_Process` involved — verified doc line in *The budget* above. The first
LOD move is always *set `MaxDistance` to something honest*, not write a manager.
`sound-spatial-audio` owns what the value should be; this skill only insists it is not left at the
default that disables it (it *"only has an effect if set to a value greater than `0.0`"*).

**The hard half — whether to run a live `AudioStreamGenerator` at all — is the brief's one
genuinely good LOD idea.** Full-detail range: live synthesis. Beyond it: a cached `AudioStreamWav`
loop. Distant listeners cannot discern the real-time variation, and the CPU difference is a
generator versus a buffer read. With no audio assets in the repo the "pre-rendered fallback"
**must be a cached synthesis pass** — which `SfxLab.Get`'s cache-or-synthesise shape already models
exactly. **Crossfade the swap** (~0.5-1 s is the brief's figure and a reasonable start): an instant
source change pops, and a pop at range reads as a bug rather than as distance.

**Two things the brief lists as LOD dials that are not:**

- *"Reduce polyphony at range."* **Verified against Godot 4.7:** `MaxPolyphony` is *"the maximum
  number of sounds this node can play at the same time. Playing additional sounds after this value
  is reached will cut off the oldest sounds."* Lowering it does not reduce the cost of the sounds
  that do play — it steals earlier. A concurrency cap, not a cost dial. (Note it is also the engine
  implementing `SfxLab`'s steal-oldest policy at the node level — independent endorsement of
  pattern 3.)
- *"Thin the effect chain at range."* **Bus effects are per-bus, not per-voice** —
  `AudioServer.AddBusEffect(busIdx, effect, atPosition)` attaches to a bus. There is no per-emitter
  chain to thin. The only real version is routing far emitters to a *different*, cheaper bus, which
  is a zoning decision (`sound-spatial-audio`, `sound-real-time-effects`) and costs a bus rather
  than saving one.

### 6. `_Process` LOD across N emitters is itself the cost you came to remove

A distance check per emitter per frame is exactly the pattern that shows up as *"CPU scales with
player count"*. Three cheaper shapes, and one hard rule:

- **Stagger.** Evaluate 1/N emitters per frame. **Verified against Godot 4.7:**
  `Engine.GetProcessFrames()` is documented for exactly this — *"This method can be used to run
  expensive logic less often without relying on a `Timer`."*
- **Hysteresis.** Separate enter and exit radii. A single threshold makes an emitter standing on
  the line swap source every evaluation, which is both the most expensive thing it can do and
  audibly the worst.
- **Event-driven beats polled** wherever a movement or state change can push the update instead.

**The hard rule, taken directly from `vfx-particles`: distance is measured to the nearest player,
not the local camera.** In a 2-6 player session the local camera is frequently not the nearest
observer, and culling against it makes a sound vanish for the person standing next to it.

The distinction that is easy to get wrong: **attenuation** is per-client and correctly measured
from the local listener — that is what the engine does and it is right. **Existence is not.**
Anything deciding whether a source runs at all must be decided against the nearest player, or two
clients disagree about whether a thing is making a sound.

```csharp
/// Distance LOD for a synthesised emitter: live generator up close, cached sample at range.
/// Staggered, hysteretic, and evaluated against the NEAREST PLAYER — never the local camera,
/// which in a 2-6 player session is frequently not the nearest observer.
public sealed partial class SynthEmitterLod : Node3D
{
    /// Enter live synthesis inside this radius, leave it past the exit radius. The GAP is the
    /// point: one threshold makes an emitter sitting on the line swap every evaluation.
    private const float LiveEnterM = 15f;
    private const float LiveExitM = 20f;

    /// 1/N emitters evaluated per frame. A distance check is cheap; N of them every frame at
    /// six players is the cost this pattern exists to avoid.
    private const int StaggerFrames = 8;

    private bool _live;
    private int _slot = -1;

    public override void _Process(double delta)
    {
        if (_slot < 0)
            _slot = (int)(GetInstanceId() % StaggerFrames); // spread the load, don't sync it
        if (Engine.GetProcessFrames() % StaggerFrames != (ulong)_slot)
            return;

        float d = NearestPlayerDistanceM(GlobalPosition);
        bool want = _live ? d < LiveExitM : d < LiveEnterM;
        if (want == _live)
            return;

        _live = want;
        CrossfadeSourceTo(_live ? Source.LiveGenerator : Source.CachedSample, seconds: 0.6f);
    }
}
```

Every number in it is a starting point with a reason, and the reasons live in the comments because
the reasons are the part that survives tuning.

`sound-event-wiring` owns the dispatch side of anything event-driven here; this skill only owns
what the polling costs.

### 7. Perceptual masking: probably not worth building, and the brief's version is a no-op

The brief's check is `intendedVolumeDb > peak - MaskingThresholdDb || intendedVolumeDb > -20f`,
with `MaskingThresholdDb = -6f`, reading `AudioServer.GetBusPeakVolumeLeftDb(busIdx, 0)`.

**The signature is right.** **Verified against Godot 4.7:**
`GetBusPeakVolumeLeftDb(int busIdx, int channel)` — *"Returns the peak volume of the left speaker
at bus index `busIdx` and channel index `channel`"*, with `GetBusChannels(busIdx)` giving the valid
channel range (`0` on stereo). **There is no bus-monitoring toggle to enable** — the 4.7
`AudioServer` C# surface has no `SetBusMonitoring`/`IsBusMonitoring`, so the Godot-3-era caveat
does not apply. Four reasons it still should not be built:

1. **The second clause makes the first dead code.** `SfxLab`'s default is `volumeDb = -6f` and
   every shipped call site sits at or near it. `-6 > -20` is unconditionally true, so `ShouldPlay`
   returns before the peak is ever consulted. Against this repo's real call sites it is a no-op
   with a comment claiming otherwise — worse than no check.
2. **It is not frequency masking.** A bus peak is one wideband level with no spectral content, and
   real masking needs masker and maskee overlapping in a critical band. It will cull a high squeak
   behind a low thump that does not mask it at all.
3. **It reads a stale value.** Peak volume comes off the mixer thread on its own cadence —
   `GetTimeSinceLastMix` / `GetTimeToNextMix` exist because that cadence is not the frame cadence.
   A peak read in `_Process` describes the last mixed block, not the one the sound would land in.
4. **The cost it saves is not the cost Sail has.** Fourteen slots on one bus at six players is not
   a dense mix. The pool cap already bounds concurrency and `MaxDistance` already removes distant
   voices from mixing — both cheaper, both shipped. `vfx-particles`' discipline applies directly:
   **profile before optimising, because the candidate causes have opposite fixes and guessing wrong
   makes it worse.** Nothing here has been profiled.

**And a live design dependency could make it unsafe outright.** `LEVEL-BIBLE.md` §8.3 is
`[BLANK — Talon]` on whether Sail commits to a dedicated diegetic audio channel that is *always*
meaningful — the bible's own note is that this *"constrains sound design broadly, which is why it
is a call rather than a detail."* **If that commitment is made there is no such thing as a
low-priority ambient one-shot**, and any policy ranking some sounds droppable becomes unsafe by
construction. A genuine dependency, not a formality, and not this skill's to resolve.

Regardless of the verdict: **never gate a gameplay-critical or threat cue on any culling
heuristic.** A missed threat cue is a design failure, not an optimisation win. `LEVEL-BIBLE.md`
§8.1 bears on it from the other side — *"Audio alone is never sufficient, because the tether severs
it on purpose"* — so an urgency cue always has a second channel, and its audio half must not be
what a heuristic decides to drop.

## Measuring it — and the gap

**Godot exposes no concurrent-voice count.** Confirmed by enumerating the whole 4.7 `AudioServer`
C# surface: `GetBusPeakVolumeLeftDb` / `RightDb` are the only per-bus runtime readouts and neither
counts anything. The profiler surfaces mixing-thread CPU (the monitor's name is `unverified against
Godot 4.7` — check it, do not cite it from here). The brief's conclusion is right:
**instrument your own pool.** `SfxLab` has no "checked out" concept — `Rent` returns and forgets —
so three things are countable:

| Metric | Where | Why it is the one worth having |
|---|---|---|
| **Steal count** | the `all busy` branch of `Rent` | A nonzero steal rate *is* the budget reporting that it is being exceeded. This is the single most informative audio number in the project. |
| Peak concurrent `Playing` | sampled, or high-water on rent | Tells you how much of the 14 is actually used, which is how you learn whether 14 is right. |
| `Pool.Count` high-water | after growth | Distinguishes "the pool grew" from "the pool thrashed". |

`scripts/telemetry/` makes this cheap and buildable rather than hypothetical — a live Firebase path
with **zero audio hooks today**, and the house idiom is already there: private int fields on the
`Telemetry` autoload, accumulated during play, read once at quit in `BuildUsageReport`, inert when
the session is not active.

```csharp
public void NoteSfxSteal() => _sfxSteals++;                 // same shape as NotePropGrabbed()
public void NoteSfxConcurrency(int playing) =>              // same shape as _peakPlayers
    _peakSfxVoices = System.Math.Max(_peakSfxVoices, playing);
```

**The honest gap.** Nothing in Sail has ever been profiled — audio or otherwise, on the GTX 970
floor or anywhere. Every number in this file, including the ≤24 ceiling it defends, is a
considered guess until someone runs a six-player session with the profiler open. This skill's real
first deliverable is not an optimisation. It is a measurement.

## Testability, honestly

**Genuinely provable headless**, because it is pure bookkeeping:

- The pool never exceeds `PoolSize`.
- `Rent` prefers a non-`Playing` slot when one exists.
- The steal cursor advances and wraps — steal order is round-robin and deterministic.
- `Rent` never hands the same node to two live callers.
- Returning a slot never duplicates it (the failure pattern 2 describes).
- `RemoveAll` prunes invalidated entries and growth resumes correctly afterwards.

**Blocked — and the block is accessibility, not difficulty.** `Rent`, `Pool` and `_next` are
`private static`; `PlayStream3D` is the only reachable path and it needs a live `Node` in a real
tree. `SfxLab` has zero coverage today, and there is no `Run-SfxTest.ps1` or `Run-AudioTest.ps1`.
The house precedents are real — `MuteRegistry` is a pure Godot-free set, `CyclePhase.FromElapsed`
is a `public static class` pulled out of a `Node` purely so a headless self-test could reach it —
but applying them here means **changing shipped code**. Several skills in this family will want to
recommend it as though it were free; it is a small refactor with a real diff. Say so.

Run the **full** suite before any commit, never a filtered subset.

**And no test can hear a steal.** **Verified against Godot 4.7:** `AudioServer.GetDriverName` —
*"`--headless` also automatically sets the audio driver to `Dummy`."* Under CI, `Play()` mixes into
nothing, peak-volume reads are meaningless, and every audible consequence of every decision in this
file is invisible. `Run-VoiceTest.ps1:30` states it outright: *"Audio quality/feel is explicitly
not provable here — that is the weekend manual [check]."* The repo shipped an island rotated 90°
through a green suite; the audio equivalent will be quieter and take longer to notice. **A headed
multi-human session is required**, and the observation recorded — not "sounds fine."

## Tuning guide

Starting points with reasons. None of these is a gate.

- **`PoolSize = 14`** exists to keep the worst case inside ≤24 alongside 5 voice speakers. If the
  bed needs more than the 5 remaining slots, shrinking the pool is a legitimate trade against more
  audible steals during panics — decide it, do not drift into it.
- **`maxDistance = 40f`** is `SfxLab.PlayStream3D`'s default and sits comfortably outside
  `VoiceConfig.ProximityMaxDistance = 24.0f` — past the range at which a group is talking to each
  other, so a culled shot is one nobody was co-experiencing. That is a reason, not a rule, and the
  *localisation* consequences belong to `sound-spatial-audio`.
- **15 m / 20 m LOD radii** in pattern 6: the gap matters more than either number. Any two values
  with hysteresis beat one perfect value without it.
- **8-frame stagger**: at 60 fps that is a ~133 ms worst-case reaction to a distance change, which
  is imperceptible for a source swap and unacceptable for anything a player is aiming at. Scale it
  to what is being decided.
- **~0.6 s crossfade** on a source swap: long enough that the swap is not an edge, short enough
  that a player walking briskly is not inside the transition for long.
- **A 60 s bed loop is the synthesis cliff** (~5.76 MB, ~2.9M sample evaluations). Not a limit —
  the point at which the approach needs a decision rather than a default.
- **`AudioStreamPolyphonic.Polyphony`**: `PlayStream` returning `InvalidId` at the cap is a silent
  drop, so a cap set too low skips a sound rather than truncating one — the failure mode pattern 3
  argues against. Set it to what the source genuinely needs.

## Integration points

- **`sound-integration-guide`** — the family map, build order, and where the shared blockers live.
  Read it if it is not obvious which `sound-*` skill owns the question.
- **`SfxLab.PlayStream3D`** — the one positional path, and the place the budget is enforced.
  Anything that needs more goes into `SfxLab`, not beside it.
- **`ActorFx` + `EventResponse`** (`scripts/game/presentation/`) — the shipped data-driven dispatch
  layer, `[Export]`-configured, already decoupling controllers from `SfxLab`. It is the nearest
  thing Sail has to an event bus, and it works. **`AtmosphereEventBus` is deliberately not built**
  (`docs/ATMOSPHERIC-VFX-INTEGRATION.md` §3.4: *"Build the bus when a second listener actually
  exists"*). Do not add one to solve an audio-cost problem — and the dispatch architecture itself
  is `sound-event-wiring`'s, not this skill's.
- **`scripts/voice/`** — out of bounds, and the largest fixed line item in the budget: 5
  `AudioStreamPlayer3D` and 5 `AudioStreamGenerator` at 6 players. Never fork `VoiceConfig`'s
  values, never add DSP to the `Voice` bus. Where world audio and voice contend, it is a **mix**
  question and it lives on the world side (`sound-soundscape-construction`).
- **`scripts/telemetry/`** — the live Firebase path, `Telemetry.Instance`, the `Note*` counter
  idiom. Zero audio hooks today.
- **`scripts/ui/SettingsPanel.cs`** — the only mixer UI: Master and Voice sliders only. **Any new
  bus a player should be able to turn down needs a slider added here**, and a bus with no slider is
  a bus a player cannot escape.
- **`vfx-particles`** — owns the frame budget and the GTX 970 Forward+ floor for the whole family.
  Cite it; do not restate it. Audio and particles contend for the same CPU, and the pool shape in
  both files is deliberately the same object.

## Precedent

- **`scripts/game/sandbox/SfxLab.cs`** — the house pooling contract and the only written audio
  budget in the project. Prefer-idle, steal-oldest, bounded, lazily grown, lazily re-anchored, on a
  dedicated bus. Everything in this file either defends it or measures it.
- **`docs/ATMOSPHERIC-VFX-INTEGRATION.md:204`** — spawn-per-sound recorded as anti-pattern #19,
  with the fix named: use `SfxLab.PlayStream3D`.
- **`scripts/voice/VoiceSpeaker.cs`** — proof Sail can run real-time generators with 3D attenuation
  at five concurrent instances, and the CPU baseline any new generator competes with.
- **`scripts/telemetry/Telemetry.cs`** — the counter idiom (`NoteVoiceUsed`, `NotePropGrabbed`,
  `_peakPlayers`) that audio instrumentation should copy exactly.
- **`MuteRegistry` / `CyclePhase.FromElapsed`** — the house precedent for pulling pure logic out of
  a `Node` so a headless self-test can reach it. What this skill wants and cannot currently use.
- **`vfx-particles`** — the sibling substrate: same pool shape, same "cheap does not mean
  warranted", same profile-first discipline, same honesty about serving no doctrine section.
- External: **Lethal Company** and **R.E.P.O.** pool per-player footstep and interaction audio and
  LOD distant sources — the brief's cited precedent, and the right comparison for the failure mode
  (cost scaling with player count) even at different player counts.

## Troubleshooting

In the order these actually occur.

- **CPU cost rises with player count** — per-player feedback creating unpooled `AudioStreamPlayer3D`
  instances. The most common multiplayer audio scaling bug, and the brief is right about it. Find
  the call site that is not `SfxLab.PlayStream3D`.
- **Sounds intermittently do not play, at the wrong position, under load** — a pool handing the same
  node to two callers. Pattern 2, bug A. Check whether anything subscribes `Finished` once and
  returns to a queue.
- **Audible cutoffs during busy moments** — the pool is doing its job. Confirm with a steal counter
  before raising `PoolSize`; if steals are rare, the cutoff is something else.
- **Distant sounds get cut before near ones** — a quietest-first steal policy, or `VolumeDb` being
  used as a proxy for perceived loudness. Pattern 3.
- **A frame hitch the first time a given sound plays** — first-use synthesis on the game thread.
  Warm `SfxLab.Get` at load.
- **A pop when an emitter changes source** — the LOD swap is instant. Crossfade it.
- **An emitter rapidly switching source while a player walks past** — one threshold instead of two.
  Add hysteresis; do not widen the single radius.
- **Distant audio noticeably worse after LOD** — the cached fallback is too thin or the reduced-detail
  range is too near. Both are tuning; check variety in the fallback before moving the radius, since
  a repeating fallback is what actually gets noticed.
- **A sound vanishes for the player standing next to it** — culled against the local camera instead
  of the nearest player. Pattern 6's hard rule.
- **A masking or priority check appears to do nothing** — it probably does nothing. Pattern 7.
- **Green headless suite, audibly broken game** — expected. `--headless` sets the audio driver to
  `Dummy`. Run it headed, with humans.

## Caveats

- **No numbers as law.** 14 slots, ≤24 concurrent, 40 m, 15/20 m, 8 frames, 0.6 s, 96 kB/s — each
  is a starting point with a stated reason, and the reason is the part that survives. **The ≤24
  ceiling included:** it is the shipped number and this skill defends it, but nobody measured it,
  and defending an unprofiled number is not the same as proving it.
- **Unverified is stated, not implied.** `MaxDistance`, `UnitSize`, `AttenuationModel`,
  `MaxPolyphony`, `Finished`, `AudioStreamPolyphonic`, `AudioStreamPlaybackPolyphonic.PlayStream`,
  `AudioStreamGenerator.BufferLength`, `GetBusPeakVolumeLeftDb`, `GetBusChannels`,
  `RegisterStreamAsSample`, `PlaybackType.Sample`, `GetDriverName` and `Engine.GetProcessFrames`
  were checked against GodotSharp 4.7.0's XML docs. Whether `Stop()` emits `Finished`, and the
  profiler's audio-monitor name, are marked `unverified against Godot 4.7` inline. **The brief's
  code samples are not evidence** — two of them ship bugs.
- **No affect, and cheap does not mean warranted.** This skill never argues a sound should exist,
  only what it costs. The fastest way to wreck a soundscape is to add every layer that fit.
- **Not a resolver.** `LEVEL-BIBLE.md` §8.3's `[BLANK — Talon]` on the diegetic channel decides
  whether any culling policy is safe at all, and stays blank. `THRILL-BIBLE.md` §9's tone **ratio**
  is open (axis decided 2026-07-26; the ratio's ripeness trigger is the first playtest in which
  anyone is actually frightened) — it costs no CPU, but a sincere-dread mix and an absurd-payoff
  mix have different concurrency profiles, so the budget's *shape* is downstream of it. §6.2's
  night ambient floor is a shipped decision in live conflict with doctrine. §4.4's spike ceiling
  carries `[research default — pending Talon confirmation]` verbatim. A performance argument is the
  most tempting way to settle any of these, and settling them that way would be wrong.
- **Headless cannot hear.** Pool bookkeeping is CI-provable and should be tested. Nothing audible
  is; the driver under `--headless` is `Dummy`.

*Scope note: written 2026-07-28 against `feat/neighbourhood-exterior`, where the entire audio
system is `SfxLab` — 12 synthesised one-shots, a 14-slot pool, one `Sfx` bus — plus the shipped
proximity voice pipeline. No ambient bed, no audio asset files, no `Ambient` bus, no audio test,
no audio telemetry, and nothing profiled on the GTX 970 floor or anywhere else. **Current bite:
one — routing any new positional one-shot through `SfxLab.PlayStream3D` instead of a second pool,
which is enforceable today. Everything else in this file is a budget waiting for a measurement.**
First real test: a six-player session with a steal counter and a peak-concurrency counter wired
into `Telemetry`, played headed on the floor spec with the profiler open. Passing looks like a
recorded steal rate, a recorded peak voice count, and the ≤24 ceiling in `SfxLab`'s doc comment
replaced by a number somebody measured — or confirmed, with the measurement attached.*
