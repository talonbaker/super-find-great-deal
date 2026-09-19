---
name: sound-procedural-reactive
description: Use when Sail audio must produce variety and continuous state-response from an asset budget of zero — a seeded generative event scheduler, a mix answering several live gameplay dimensions at once, organic non-uniform event timing, or synthesizing live vs once into a cached AudioStreamWav.
---

# sound-procedural-reactive

## Overview

**Variety and continuous state-response, from nothing.** Sail ships zero audio asset files — no
`.ogg`, no `.wav`, no `.mp3` anywhere in the tree. Every sound in the game is synthesized at
runtime by `scripts/game/sandbox/SfxLab.cs`. That is not a gap this skill routes around; it is
the condition this skill is written for, and it changes almost every technique the usual
generative-ambience playbook recommends.

This skill owns four things: **seeded generative scheduling**, **multi-dimensional state-driven
mixing**, **organic (non-uniform) event timing**, and **the synthesis-versus-cached-sample
decision**. It is the systemic counterpart to `sound-threat-audio`, which owns per-emitter
synthesis for one voice.

**It does not own:** what the world should be responding to emotionally (`/direct`); the
escalation curve or the session-monotonic derivation (`vfx-escalation`); the bed's layer
construction, lanes and ducking (`sound-soundscape-construction`); spatialisation and falloff
(`sound-spatial-audio`); a specific threat's voice (`sound-threat-audio`); the withdrawal
(`sound-silence-negative-space`, `vfx-audio-sync`); pooling policy and the concurrency budget
(`sound-optimization` — cite its table, never invent a competing one).

## Directed, not decided

`/direct` and `docs/THRILL-BIBLE.md` decide **which** feeling and **why there**. `vfx-audio-sync`
decides **which mode** a moment wants and **how often** a device is spent. This skill decides
**how the variation is generated, seeded, timed, replicated and paid for.**

That boundary is easy to lose here, because a system that continuously maps game state onto a
mix looks like it is making design decisions. It is not, and it must not: **the state dimensions
and their meanings come from `/direct`; this skill owns the plumbing that reads them.** If a
proposal starts arguing that the house *should* go quiet when the kids scatter, it has left this
skill. Route it.

Two forks bear on this skill hard enough that they get real treatment rather than a footnote —
see *The §8.3 dependency* below, and *Caveats*.

## When to Use

- A house or neighbourhood ambience needs to vary across sessions without an author placing every
  event
- The same handful of synthesis recipes has to stop sounding like the same handful
- The mix needs to answer to several live gameplay values at once, not a calm/danger binary
- Event timing reads as a metronome, or as noise, rather than as a place
- Something wants a live `AudioStreamGenerator` and the question is whether it has earned one
- A generative system has to be *stoppable* because a withdrawal is about to fire

**Not for:** whether a sound should exist or what it means (`/direct`); the session escalation
curve, the dread state machine, or deriving a monotonic session quantity from `CycleDriver`
(`vfx-escalation`); how the bed's layers are separated, laned and ducked
(`sound-soundscape-construction`); bus effect chains and their property surface
(`sound-real-time-effects`); attenuation, `UnitSize`, reverb zoning (`sound-spatial-audio`);
one threat's growl (`sound-threat-audio`); the withdrawal schedule (`sound-silence-negative-space`);
the concurrency budget and pool policy (`sound-optimization`); anything inside `scripts/voice/`,
which is shipped, tuned and out of bounds.

Generating variation cheaply is **not an argument that the variation should exist.** A generative
scheduler that can fire a hundred distinct events is a hundred opportunities to violate
`LEVEL-BIBLE.md` §8.3, not a feature.

## What exists in the repo today

| Thing | State |
|---|---|
| Audio asset files | **Zero.** No `.ogg` / `.wav` / `.mp3` in the tree. `BirdCallPool[]` and `InsectPool[]` have nothing to hold. |
| `scripts/game/sandbox/SfxLab.cs` | **The shipped procedural-audio system.** 12 cartoon one-shots synthesized to 48 kHz `AudioStreamWav` at first use, cached in a `Dictionary<Sfx, AudioStreamWav>`, played through a 14-slot `AudioStreamPlayer3D` pool on a lazily-created `Sfx` bus. |
| The eight synthesis recipes | `EffortGrunt`, `Thump`, `Bonk`, `Warble`, `Sweep`, `Oof`, `TwoNote`, `Squeak`. Plumbing: `Render`, `Envelope`, `EaseOut`, `ToWav`. **All `private static`.** |
| Generative scheduler | **Does not exist.** |
| Ambient bed | **Does not exist.** No looping or continuous audio anywhere; every playback path is one-shot. |
| Ecosystem / disturbance state | **Does not exist**, and its premise is dead — see *Retargeting the state*. |
| A session-monotonic quantity | **Does not exist.** `CycleDriver.Phase` is cyclic. Owned by `vfx-escalation`. |
| Live `AudioStreamGenerator` | Exactly one user: `VoiceSpeaker` (`scripts/voice/`), up to 5 concurrent. Out of bounds. |
| Audio test coverage | **Zero.** No test loads, plays, mixes or asserts an `AudioStream`, an `AudioServer` bus, or `SfxLab`. |
| RNG precedents | `GD.Randf` / `GD.RandRange` (`Carryable`, `BodyLanguageEvaluator`, `AvatarVisual`), Godot `RandomNumberGenerator` (`IntentSources`, `NetSim`), and — a name-collision trap — `RoomCode.cs:24` uses **`System.Security.Cryptography.RandomNumberGenerator`**, a different class entirely. |

Two stale things in `SfxLab` worth knowing before you edit it: `SfxLab.cs:77` names `LabAmbience`
in a doc comment, and **`LabAmbience` does not exist anywhere in the repo** — the "callers with
their own streams" that `PlayStream3D` was built for were never written. And the `Sfx` enum's
`None = 0` is load-bearing (`EventResponse.Sound` defaults to it), so never reorder it.

## Core patterns

### 1. Synthesize once and cache — the rule, and what the cache costs

`SfxLab` is the precedent and it argues against the live-generator architecture. `Get(Sfx kind)`
renders a `float[]` through `Render`, converts it in `ToWav`, stores the `AudioStreamWav` in
`Cache`, and every subsequent play is an ordinary stream playback at **zero synthesis cost**. A
live `AudioStreamGenerator` pays its cost on every frame, forever.

**The decision rule: synthesize-and-cache unless a parameter must vary continuously *during* the
sound.** Not "during the session" — during the individual sound. Almost nothing qualifies. A wind
layer whose brightness follows a state float does not need a generator; it needs a cached loop
and an `AudioEffectLowPassFilter` on its bus, or a crossfade between two cached renders. A
threat's vocalisation that has to bend pitch as it moves might qualify, and that is
`sound-threat-audio`'s call, not this skill's.

**In Sail the live-synthesis budget is already spent, by voice.** `VoiceSpeaker` runs up to five
`AudioStreamGenerator`s decoding Opus in real time, and voice has to work. A world-audio generator
competes with it directly, on the same CPU, in the same 2–6 player session. The brief's
`SynthesisBudgetManager` with `MaxActiveSynthesizers = 3` is a budget for a resource Sail should
be spending zero of — and its "fall back to a pre-rendered sample" branch is incoherent here,
because **the cache is the pre-rendered sample.** There is nothing else to fall back to.

**Verified against Godot 4.7** (GodotSharp 4.7.0 XML docs): `AudioStreamGenerator` "does not play
back sounds on its own; instead, it expects a script to generate audio data for it", and
"Due to performance constraints, this class is best used from C# or from a compiled language via
GDExtension." That note is about *language*, not about the class being cheap. `AudioStreamWav`
"can also be used to store dynamically-generated PCM audio data" — which is exactly what `ToWav`
does.

**What the cache actually costs, and the brief never gets near this.** `SfxLab`'s recipes are
50–380 ms, so a cached render is a few KB and first-use synthesis is imperceptible. A *sustained*
layer is a different object: 30 s mono 16-bit at 48 kHz is **2.88 MB resident**, plus a transient
5.76 MB `float[]` during `Render`. Thirty-six parameter variants of that is ~100 MB and a visible
hitch on whichever frame first asks for one. So:

- **Warm the cache at load**, not on first use, for anything above roughly a second.
- **Budget cached loops as memory**, and hand the concurrency half of the budget to
  `sound-optimization` rather than restating it here.
- `SfxLab.Get` renders on the calling thread. That is fine for a `Squeak` and wrong for a bed.

**One more thing `ToWav` cannot do today:** it sets `Data`, `Format`, `MixRate` and `Stereo` and
nothing else, so `LoopMode` stays at its default. **Verified against Godot 4.7:**
`AudioStreamWav.LoopModeEnum` is `Disabled` / `Forward` / `Pingpong` / `Backward`, with
`LoopBegin` and `LoopEnd` "in number of samples, relative to the beginning of the stream". A
loopable synthesized layer needs `LoopMode = Forward` and sample-accurate loop points, and
`Envelope` — `a * Mathf.Pow(1f - u, curve)` — forces every recipe to decay to silence at `u = 1`.
**The palette encodes "one-shot" at the level of its plumbing.** A bed layer is a new plumbing
function, and the shape of it belongs to `sound-soundscape-construction`.

### 2. Recipe parameterisation is the only variance axis Sail has

The brief's tuning advice for a still-repetitive soundscape is "expand the source sample pool."
**That advice is unavailable here.** There is no pool. The "samples" are synthesis recipes, and a
recipe is not a fixed waveform — it is a function with arguments.

Look at what `Get` actually does with them:

```
Sfx.Land       => Thump(0.12f, 85f,  noise: 0.5f),
Sfx.Thunk      => Thump(0.15f, 110f, noise: 0.3f),
Sfx.Step       => Thump(0.05f, 150f, noise: 0.6f),
```

Three different-sounding cues from **one** recipe and three argument tuples. That is the lever,
and it is a genuinely different technique from jittering the playback rate of a recording:

| | Playback pitch jitter | Recipe re-render |
|---|---|---|
| What moves | the whole spectrum, including formants and noise character — the chipmunk axis | only what the argument controls |
| What stays | the exact same waveform, stretched | the envelope shape, the harmonic structure, the character |
| Cost | free | one render, then cached forever |
| Ceiling | ~±8% before it reads as a pitch effect | the whole parameter space |

**Cache on a quantized parameter tuple, not on a continuous one.** A continuous key means every
event renders a new WAV — unbounded memory and a synthesis hitch per event, which is the failure
mode this pattern most easily creates. Quantize deliberately: 3 durations × 4 fundamentals ×
3 noise levels is 36 distinct, permanently-cached "samples" from one recipe, warmed at load, with
an asset budget of zero. Pick the buckets so that adjacent buckets are audibly different; two
buckets nobody can tell apart are one bucket and a wasted 2.88 MB.

**And there is a free axis sitting in plain sight.** Every noisy recipe hard-codes its noise
seed — `new Random(1234)` in `Thump`, `new Random(777)` in `Oof`, `new Random(9001)` in
`EffortGrunt`. Deliberate, because it makes the cached render deterministic. But it also means
**every landing in the game has bit-identical noise.** Promoting that seed to a parameter costs
one argument and yields as many distinct-but-same-character variants as you want, with no change
to the recipe's pitch, envelope or perceived identity. That is the cheapest variance in the
codebase and nobody has spent it.

**The constraint on all of this:** `Render`, `Envelope`, `EaseOut`, `ToWav` and all eight recipes
are `private static`. You cannot compose against them from a new file — extending the palette
means **editing `SfxLab.cs`**. It is also why the file has zero test coverage: there is no
reachable seam. If you build a parameterised layer, put the *selection* logic in a `public static`
class so it is testable (pattern 6) and leave the synthesis private, mirroring the
`CyclePhase` / `CycleDriver` split.

### 3. Seeding in a 2–6 player session — texture may diverge, events may not

**This is the central design decision in the whole skill, and the brief does not notice it is
one.** `_rng.Seed = (ulong)Time.GetTicksUsec()` runs independently on every client. In a six-kid
sleepover that is six different soundscapes, and nobody in the room is hearing the same house.

**Draw the line explicitly:**

> **Texture may diverge. Events may not.**

Ambient variation nobody will ever compare — which fridge hum variant a client rendered, where
inside a 5 m radius a floorboard settled — is fine to roll locally, and rolling it locally is
strictly cheaper than replicating it. **Anything a player might say out loud** — "did you hear
that?" — must be **server-rolled and replicated**, because `THRILL-BIBLE.md` §4.1's
**simultaneity** condition is what makes a reaction contagious rather than individual. Two players
describing different events is not information asymmetry; it is a desync wearing its costume.

The same correction has now been made twice elsewhere in this codebase's skill set —
`vfx-escalation` about client-side `GD.Randf` rolls in `ShouldFireEvent`, `vfx-audio-sync` about
client-side silence timers. **It is the single most repeated error in this material.**

The shipped pattern is `scripts/game/world/CycleDriver.cs` and it is worth copying whole: the
server owns the input and advances it on the **sim tick** (never a wall-clock `Timer`, which
drifts from physics); it broadcasts with
`[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = …)]`
— **Reliable for a one-shot event**, because unlike a phase sample a dropped event has no
successor; it carries a sequence number with a staleness guard
(`if (!isSync && Synced && seq <= _lastAppliedSeq)`); and it gives every joining or resuming peer
a **targeted reliable sync** from `Gameplay.OnPeerConnected` that always wins. Channels 2–5 are
taken (voice, move, prop, cycle); confirm the next free index against `NetCodec` at build time.

**A shared seed is a third option and it deserves naming.** Replicate the seed once at session
start and let every client derive the identical sequence locally — no per-event traffic at all.
It costs one thing, and that thing is fragile: **every client must consume the stream in exact
lockstep.** One client skipping an event because a slot was busy, or taking a different branch
because its local state differed, and the streams diverge permanently and silently. Pattern 4's
timing generator makes this worse than it looks, and pattern 4 says why.

**API corrections, verified against Godot 4.7:**

- `RandomNumberGenerator.Randomize()` "sets up a time-based seed for this instance" — this is the
  house expression of "randomize per session" and `NetSim` already uses it. Prefer it to
  hand-rolling `Time.GetTicksUsec()`.
- `Seed`'s doc carries a warning the brief's construction walks straight into: **"The RNG does
  not have an avalanche effect, and can output similar random streams given similar seeds.
  Consider using a hash function to improve your seed quality if they're sourced externally."**
  `Time.GetTicksUsec()` is "time passed in microseconds **since the engine started**" — so clients
  launched together produce near-identical seeds, and a region index used directly as a seed
  produces near-identical streams per region. Hash it.
- `Randf()` returns a float "between 0.0 and 1.0 **(inclusive)**" — so `pool[(int)(Randf() * n)]`
  can index `n` and throw. Use `RandiRange(from, to)`, which is inclusive on both ends, with
  `to = n - 1`.
- `[Export] ulong Seed` — `ulong` is a built-in value type and is not on Godot's excluded list
  (`decimal`, `nint`, `nuint`), so it marshals; but Variant stores integers as **signed** 64-bit,
  so seeds above `long.MaxValue` will not round-trip through the inspector or a saved scene.
  `[Export] long` with an explicit cast is the honest declaration. Marked
  `unverified against Godot 4.7` for the specific question of whether the inspector clamps or
  wraps — check before relying on a large literal seed.
- "`0` = randomize" is a workable convention but note that `0` is a perfectly valid seed you are
  giving up, and that `new RandomNumberGenerator()` is already pseudo-randomly seeded by default.

### 4. Organic timing — one draw, one anchor, one stop condition

Layered rather than uniform is the right instinct: uniform intervals read either as a metronome
or as noise, and neither reads as a place. `vfx-particles` and `vfx-audio-sync` both make the
related point that **interval irregularity beats interval length**, because a regular cadence gets
learned — `THRILL-BIBLE.md` §8.3's territory.

Three things wrong with the brief's version, in ascending order of severity.

**The comment describes a distribution the code does not produce.** The draws are sequential, so
it is 15% lull, then 25% *of the remaining 85%* — about 21% flurry and 64% typical, not 60/25/15.
Arithmetically minor. Structurally not: two sequential `Randf()` calls mean the RNG advances by
**one or two** steps depending on the branch taken, which is precisely what breaks a shared-seed
lockstep (pattern 3). One draw with cascaded thresholds fixes both.

**`slot.GlobalPosition += …` accumulates.** It is `+=`, not `=`, on a persistent emitter, so every
event random-walks the emitter further from where it started. Over a session the "ambient bird"
drifts over the horizon and the ambience goes quiet for no reason anybody can name. Offset from a
**captured anchor**, never from the current position. Better still: in Sail this bug has no place
to live, because `SfxLab.PlayStream3D` rents a pooled player per shot and sets its position
absolutely — there *is* no persistent mutable position to accumulate into. (Persistent emitters
are still correct for sustained bed layers, and the anchor rule applies there in full; that is
`sound-soundscape-construction`'s object.)

**`async void` recursive scheduling has no lifetime and no cancellation.** `ScheduleNextEvent`
awaits a `SceneTreeTimer` and calls itself forever; the continuation resumes on a node that may
have been freed, and there is no way to stop it. That second half matters more than it sounds:
**`sound-silence-negative-space` needs exactly that stop.** A generative scheduler still firing
floorboard creaks through a withdrawal has defeated the withdrawal, and the withdrawal is the
highest-value unbuilt device in the game.

Tick the sim step instead. It fixes the lifetime, matches `CycleDriver`'s discipline, and hands
the stop condition over for free:

```csharp
/// Generative house ambience. Rolls texture locally; anything remarkable is server-rolled and
/// replicated — see pattern 3. Ticks the sim step so it stays in step with CycleDriver and so
/// sound-silence-negative-space can stop it.
public partial class HouseAmbienceDirector : Node
{
    [Export] public long Seed;                                    // 0 = randomize per session
    [Export] public Godot.Collections.Array<Node3D> Anchors = new();
    [Export] public float Spread = 1.5f;                          // metres; a room, not a field
    [Export] public float BaseMin = 6f, BaseMax = 18f;            // starting points, not gates

    private readonly RandomNumberGenerator _rng = new();
    private double _untilNext;
    private bool _running = true;

    public override void _Ready()
    {
        if (Seed == 0) _rng.Randomize();       // NetSim's precedent for "time-based seed"
        else _rng.Seed = (ulong)Seed;
        _untilNext = NextInterval();
    }

    /// The seam sound-silence-negative-space needs. Suspend() must survive a withdrawal that
    /// outlives any single tween, so it is state, not a cancelled task.
    public void Suspend() => _running = false;
    public void Resume() { _running = true; _untilNext = NextInterval(); }

    public override void _PhysicsProcess(double delta)
    {
        if (!_running || Anchors.Count == 0) return;
        _untilNext -= delta;
        if (_untilNext > 0) return;
        _untilNext = NextInterval();

        Node3D anchor = Anchors[_rng.RandiRange(0, Anchors.Count - 1)];   // inclusive both ends
        Vector3 pos = anchor.GlobalPosition + new Vector3(                // read fresh, never write back
            _rng.RandfRange(-Spread, Spread), 0f, _rng.RandfRange(-Spread, Spread));
        SfxLab.PlayStream3D(this, pos, PickVariant(), volumeDb: -16f);
    }

    /// ONE draw, so the stream advances by exactly one step per event and the stated
    /// distribution is the distribution. Lull / flurry / typical — starting points.
    private double NextInterval()
    {
        float r = _rng.Randf();
        if (r < 0.15f) return _rng.RandfRange(BaseMax * 1.5f, BaseMax * 3f);
        if (r < 0.40f) return _rng.RandfRange(BaseMin * 0.3f, BaseMin * 0.7f);
        return _rng.RandfRange(BaseMin, BaseMax);
    }
}
```

`PickVariant()` is pattern 2's quantized-tuple lookup. `SfxLab.PlayStream3D` is the **only**
compliant positional path in the repo — it pools, prefers an idle slot, steals round-robin when
full, re-anchors across scene changes, and returns silently on a headless teardown race.

### 5. Retargeting the state — and the axes that are not floats

**Everything the brief's state model describes is dead.** Harvesting is gone
(`ROADMAP.md:245`, *"harvest = nothing"*), there is no ecosystem, no extraction and no wildlife.
The premise is a 90s sleepover: 2–6 kids in a house, and a thing in the attic kept asleep by a
nightly tribute.

The *instinct* translates beautifully. "Wildlife quiets as disturbance rises" becomes **a house
that notices, and stops** — the fridge hum, the clock, the radiator tick, the TV two rooms away,
thinning as attention rises. That is the same mechanism doing better work.

Orthogonal dimensions that actually exist or are designed:

| Dimension | Shape | Notes |
|---|---|---|
| How far through the night the group is | monotonic float | **Not yours to derive.** See pattern 7. |
| Nearest player's proximity to the attic hatch | float | Server knows it; per-client derivation is `vfx-proximity`'s rule, not a broadcast intensity |
| Tribute state | **enum** — prepared / due / overdue | *Not a float* |
| Group dispersion — five kids in one room vs five rooms | float | The genuinely new axis, and the one a sleepover makes legible |
| Why the house is quiet | **categorical** — everyone asleep vs everyone stopped | *Not a float* |

**The finding that matters: orthogonality is not the same as continuity.** The brief's instinct is
to make every dimension a 0..1 float and `Lerp` between them. Two of the five above are
categorical, and flattening them destroys exactly the "which kind of disturbance is this"
legibility the multi-dimensional mix existed to provide. A mix driven by a float that blends
*prepared* and *overdue* communicates a state that never occurs. Route categorical dimensions to
discrete mixes — a different layer set, not a different gain on the same one.

**And name the collision:** a mix that quiets on rising state is a *continuous* version of the
withdrawal, and it can pre-spend it. If the house always goes quiet when someone approaches the
hatch, silence has acquired a legible cause and `THRILL-BIBLE.md` §6.3's wrong silence has nothing
left to be wrong about. That interaction is `vfx-audio-sync`'s and `sound-silence-negative-space`'s
to arbitrate. **Report it; do not tune your way around it.**

### 6. What is safe to modulate on a sustained layer — and pitch is not

**Verified against Godot 4.7:** `AudioStreamPlayer3D.PitchScale` is "the pitch **and the tempo** of
the audio, as a multiplier of the audio sample's sample rate" (the `AudioStreamPlayer` doc is
blunter: "A value of `2.0` doubles the audio's pitch, while a value of `0.5` halves the pitch").
It is resampling. On a looping `AudioStreamWav` that means the loop points — which are fixed in
**samples** — arrive at a different wall-clock time, so layers that were phase-aligned drift
apart, and a sustained tonal layer at 1.05× is audibly detuned against anything it sits next to.

So `PitchScale = Mathf.Lerp(1f, 1.05f, PlayerDensity01)` on a bed layer is not the free knob it
looks like.

- **Safe on short one-shots.** `SfxLab.PlayStream3D` jitters ±0.08 by default for exactly this
  reason, and its own doc comment says why: so repeated hops and bumps "never sound machine-gun
  identical."
- **Not safe on anything sustained or looping**, and not safe at all on two layers meant to sit in
  a fixed relationship.

Reach for these instead, in order of how well they survive a session: **gain** (`Mathf.LinearToDb`
— **verified**, "can be used to implement volume sliders that behave as expected since volume
isn't linear"); **layer count** (activate and deactivate whole layers rather than pushing one
harder); **filter cutoff** on the layer's bus; **crossfade between differently-parameterised
renders** from pattern 2, which is the one that changes character rather than level.

Note the DSP precedent before adding an effect: the **only three `AudioEffect*` uses in the entire
repo** are on `VoiceManager`'s PA bus (`AudioEffectDistortion`, `AudioEffectLowPassFilter`,
`AudioEffectReverb`). Bus DSP for a build belongs to `sound-anticipation-escalation`; bus
structure belongs to `sound-soundscape-construction`. Copy `SfxLab.EnsureBus()`'s lazy
create-and-route-to-Master shape for any new bus, and remember that
`scripts/ui/SettingsPanel.cs` has sliders for Master and Voice only — a new bus a player should be
able to turn down needs one adding there.

### 7. `DaylightRemaining01` is the trap `vfx-escalation` exists to prevent

**Do not solve this.** `CycleDriver.Phase` is **cyclic** — normalized in `[0,1)` over a 120 s
default period, with `CyclesElapsed` counting the wraps. It is not a one-way session ramp. A
"daylight remaining" input bound to `Phase` resets **every two minutes**, and a soundscape that
gets tenser and then cheerfully resets seven times an hour is the feature inverted.

`vfx-escalation` owns the derivation and **deliberately leaves the choice open** — from
`CyclesElapsed` normalised against the period, from a separate monotonic driver, or from the
pressure mechanic — because it is the same question as `THRILL-BIBLE.md` §13's "one clock or two".
Consume its `SessionProgress` seam. Do not build a second one.

Two caller-side rules that are yours regardless:

- **Gate on `CycleDriver.Instance.Synced`.** A client's `Phase` and `CyclesElapsed` sit at
  zero-initialised defaults until the first authoritative update lands; treating that as "the
  night just started" is the same class of bug as rendering t=0 first. `DayNightSky` shows the
  guard: `if (CycleDriver.Instance is not { Synced: true } driver) return;`
- **Never let the derived value go backwards.** A reconnect delivers a targeted reliable
  `SyncPhaseTo` that can legitimately move `Phase` back across a wrap. A returning player must not
  hear the house get calmer.

Hand the fork upward. Say in your output which derivation you consumed and that you did not choose
it.

### The §8.3 dependency — and why it is not a footnote

`LEVEL-BIBLE.md` §8.3 is **`[BLANK — Talon]`**:

> "**[BLANK — Talon]** whether Sail commits to a dedicated diegetic audio channel that is *always*
> meaningful — the tide's sound, the hive's hum. Deep Rock Galactic's version of this is giving
> dwarves no idle chatter, so that any bark at all carries system state. **Note this constrains
> sound design broadly, which is why it is a call rather than a detail.**"

**This skill is the one that collides with it head-on.** A generative scheduler exists to fire
texture — events with no system state behind them, whose entire job is to make a place feel
inhabited. If §8.3 lands on *yes, every sound carries state*, then a director firing meaningless
floorboard creaks is not merely undesirable, it is **incompatible by construction**, and the
system has to be deleted or re-founded rather than tuned down. If it lands on *no*, texture is
free and this skill is straightforwardly correct. There is no middle position that both answers
support.

**The honest engineering response to an undecided architectural dependency is to build for the
reversible case.** Give every scheduled event a declared meaning at the point it is scheduled —
an explicit field, with `None` a legal and common value — so that a *yes* on §8.3 is one pass
deleting every `None` event, rather than an archaeology project through a design where texture and
signal were never distinguished. Costs one enum. Buys the ability to answer §8.3 either way.

Do not resolve it, do not infer it from what the reference games did, and do not let "we already
built it" become the argument.

## Tuning guide

Starting points with reasons attached. None of these is a gate.

- **15 / 25 / 60 lull-flurry-typical** — the *shape* is the point, not the split. Lulls create the
  interval variance §8.3 wants; flurries stop a lull reading as a bug. If the flurries read as a
  malfunction, they are too tight or too loud, not too frequent.
- **6–18 s base interval** for a house interior, roughly a third of the 40–110 s `vfx-audio-sync`
  suggests between *silences* — a creak is not a device spend. Both are playtest calls.
- **±1.5 m position spread**, not ±5 m. The brief's figure is sized for an open field; inside a
  bedroom, 5 m puts the sound through a wall. Spread should be smaller than the room.
- **±0.08 pitch jitter** on one-shots, matching `SfxLab.PlayStream3D`'s shipped default. Above
  roughly ±0.10 it stops reading as variation and starts reading as an effect.
- **36-ish quantized recipe variants** per recipe is a plausible starting fan-out — enough that a
  player cannot learn the set, few enough to warm at load. Pick buckets that are audibly distinct;
  two indistinguishable buckets are one bucket and wasted memory.
- **Ambient one-shots well below the bed and well below voice.** A texture event that makes a
  teammate at 10 m harder to understand has cost more than it bought (`vfx-audio-sync`'s rule,
  restated because this skill fires the most events).
- **Zero live synthesizers** is the correct starting budget, not three. Raise it only with a
  profiled before/after on the GTX 970 floor, with voice active.

## Integration points

- **`SfxLab.PlayStream3D`** — the only compliant positional one-shot path. Pooled, prefer-idle
  then steal-oldest, 14 slots inside the documented ≤24 concurrent-3D-player budget that already
  counts the five voice speakers. Never spawn a player per sound.
- **`SfxLab.Get` / the recipes** — pattern 2's parameterisation means **editing `SfxLab.cs`**.
  There is no other seam; the helpers are `private static`.
- **`vfx-escalation`** — owns `SessionProgress` and the monotonic derivation. Consume, never
  rebuild.
- **`vfx-audio-sync`** — owns which mode a moment wants and the sync ratio. A generative scheduler
  fires *under* that ratio, and every event it fires counts against the distribution the ratio
  governs.
- **`sound-silence-negative-space`** — needs `Suspend()`/`Resume()` on every generator. Build the
  seam even before the withdrawal exists; it is two lines and its absence is a silent defeat.
- **`sound-soundscape-construction`** — owns bed layers, lanes, buses and ducking. Sustained loops
  are its object; this skill's cached-loop memory finding is input to it.
- **`sound-optimization`** — owns pooling policy and the concurrency budget. Cite its table.
- **`CycleDriver` / `NetCodec` / `Gameplay.OnPeerConnected`** — the replication pattern, the
  transfer channel (2–5 taken; confirm the next free index at build time), and the late-join
  funnel.
- **`scripts/telemetry/`** — live Firebase, zero audio hooks today. Counting which variants fired
  and in what proportion is genuinely buildable on what ships, and it is the only way to find out
  afterwards whether the generator produced variation or just noise.
- **There is no event bus, deliberately.** `docs/ATMOSPHERIC-VFX-INTEGRATION.md` §3.4 is titled
  *"`AtmosphereEventBus` — do not build it yet"*: *"Build the bus when a second listener actually
  exists."* The nearest shipped dispatch layer is `ActorFx` + `EventResponse` — data-driven,
  `[Export]`-configured, already decoupling controllers from `SfxLab`. Copy that shape rather than
  building a bus to have somewhere to send events.

## Precedent

- **`scripts/game/sandbox/SfxLab.cs`** — the primary precedent, and the brief does not know it
  exists. Synthesize-once-and-cache, a parameterised recipe palette, a bounded pool, a dedicated
  bus, per-shot pitch jitter with the reason written in the comment. Almost everything this skill
  recommends is already half-built there.
- **`scripts/game/world/CycleDriver.cs`** — server owns the input, sim-tick advance,
  authority-mode RPC, staleness guard, targeted late-join sync, `Synced` gate. Plus `CyclePhase`,
  the house precedent for pulling a decision into a pure static function so it can be tested
  headless.
- **`scripts/net/NetSim.cs`** — `_rng.Randomize()` as the house expression of "seed per session".
  **`scripts/game/sandbox/IntentSources.cs`** — a per-instance `RandomNumberGenerator` driving
  organic wander pauses, which is the closest shipped thing to generative timing.
- **`scripts/game/presentation/ActorFx.cs` + `EventResponse.cs`** — data-driven audio dispatch
  without a bus.
- External: **No Man's Sky**'s procedural planet ambience and **RimWorld**'s mood-driven ambient
  selection are the cited references for "small pool + multi-dimensional state-driven selection".
  Both have real sample libraries underneath. **Sail has none**, which is why pattern 2 exists
  and why the reference's tuning advice does not transfer.

## Troubleshooting

In roughly the order these actually occur.

- **Ambience gradually goes quiet over a long session for no reason anybody can name.** The
  emitter position is being accumulated with `+=` instead of offset from a captured anchor. Slow
  onset, silent, and invisible in a five-minute test — the worst combination there is. Pattern 4,
  and it is directly catchable by the bounded-radius assertion in the determinism test below.
- **Two players describe different events.** The roll is running per-client. Texture may diverge;
  events may not. Pattern 3.
- **A player who joined mid-session is out of step with everyone else.** No targeted late-join
  sync. `CycleDriver.SendPhaseTo`'s treatment, applied to the generator's state.
- **Still loop-y despite randomization.** Check *which* axes are actually varying. Randomizing
  sample choice alone is the most common cause, and in Sail there is only one "sample" per recipe
  anyway — the fix is recipe parameterisation, not more pitch. Pattern 2.
- **Every event sounds like the same event with a different pitch.** The recipes' noise seeds are
  hard-coded (`1234`, `777`, `9001`). Promote them to parameters.
- **A synthesis hitch on the frame an event first fires.** The parameter cache key is continuous,
  so nothing is ever a cache hit. Quantize, and warm at load.
- **Memory climbing over a session.** Same cause, one step later — an unbounded variant cache of
  sustained loops at 2.88 MB per 30 s.
- **A synthesized ambient layer clicks at the loop point, or does not loop at all.** `ToWav` never
  sets `LoopMode`, and `Envelope` decays every recipe to zero. Pattern 1.
- **The ecosystem response feels disconnected from play.** Verify the state floats are driven by
  real gameplay events rather than left at placeholders — and separately, check that a categorical
  dimension has not been flattened into a float, which produces a mix for a state that never
  occurs. Pattern 5.
- **The soundscape resets every couple of minutes.** Something is bound to `CycleDriver.Phase`
  instead of a derived monotonic value. Pattern 7 — and this is the failure `vfx-escalation`
  exists to prevent.
- **A sustained layer sounds detuned, or two layers drift apart.** `PitchScale` used as a density
  knob. Pattern 6.
- **Events keep firing during a withdrawal.** No stop condition on the scheduler. Pattern 4.
- **A green headless suite and a soundscape nobody can stand.** Expected. See the next section.

## Caveats

- **No numbers as law.** 15/25/60, 6–18 s, ±1.5 m, ±0.08, 36 variants, zero live synthesizers —
  every one is a starting point with its reasoning attached, and the reasoning is the part that
  transfers. If any of them reads as a gate in the code you write, it is written wrong.
- **Verified vs unverified, stated not implied.** Checked against GodotSharp 4.7.0 XML docs:
  `RandomNumberGenerator.Seed` (including the no-avalanche-effect warning), `Randf` (inclusive of
  1.0), `RandfRange`, `RandiRange`, `Randomize`, `Time.GetTicksUsec`, `AudioStreamPlayer3D` /
  `AudioStreamPlayer.PitchScale`, `AudioStreamWav.LoopModeEnum` / `LoopBegin` / `LoopEnd`,
  `AudioStreamGenerator` and its performance note, `AudioStreamPlayer3D.MaxDistance` / `UnitSize`,
  `Mathf.LinearToDb`, `SceneTree.CreateTimer`. Marked `unverified against Godot 4.7`: whether
  `[Export] ulong` clamps or wraps in the inspector above `long.MaxValue`, and the per-frame cost
  of `AudioStreamGenerator` on the floor spec — nobody has profiled it.
- **`AudioStreamRandomizer` exists and is worth knowing about.** **Verified against Godot 4.7:**
  it "picks a random AudioStream from the pool, depending on the playback mode, and applies random
  pitch shifting and volume shifting during playback", with `PlaybackModeEnum.RandomNoRepeats`
  ("avoid playing the same stream twice in a row whenever possible"), `RandomPitch` (range is
  `1.0 / random_pitch` to `random_pitch`) and `RandomVolumeOffsetDb`. It is the engine's version
  of half the brief's hand-rolled loop. **It is also nearly useless in Sail today**, because it
  selects from a pool and there is no pool — and it will not run its rolls server-side, so it
  belongs to texture, never to events.
- **Not a resolver.** `LEVEL-BIBLE.md` §8.3's `[BLANK — Talon]` is an architectural dependency of
  this skill, not a footnote, and the reversible-build response above is a way to carry it, not a
  way to answer it. `THRILL-BIBLE.md` §9's tone **axis** was decided 2026-07-26 but its **ratio**
  is still open, with the ripeness trigger "the first playtest in which anyone is actually
  frightened" — a generative palette that leans comic or leans sincere forecloses it quietly, so
  say which way a variant set leans. §6.2's night ambient floor is a live conflict between a
  shipped decision (`DayNightSky.MinAmbientEnergy = 0.30f`, asserted by `CycleSelfTest`) and
  doctrine; no skill overrules it. §4.4's spike ceiling carries
  `[research default — pending Talon confirmation]` verbatim wherever it reaches code.
- **Never originates a feeling.** State dimensions and their meanings come from `/direct`. This
  skill owns the plumbing that reads them, the seeding, the timing and the bill.
- **Determinism is genuinely CI-testable, and that is unusual in this family.** A seeded generator
  is a pure-function seam — same seed in, same event sequence out — testable headless with no
  scene tree, the way `CyclePhase.FromElapsed` is tested by `CycleSelfTest` under
  `tests/Run-CycleTest.ps1`. A real test would assert: the same seed produces a byte-identical
  event sequence; two different seeds do not; the interval distribution over ~10,000 draws lands
  in the stated lull/flurry/typical bands; the RNG advances by exactly one step per event
  (the shared-seed lockstep precondition); no event's position exceeds `Spread` from its anchor
  (**this is the `+=` accumulation bug, caught as an assertion**); and — with a replicated
  generator — that every peer applied the same sequence. Put the selection logic in a `public
  static` class so all of that is reachable; the synthesis itself is not, and that is why
  `SfxLab` has zero coverage.
- **Headless cannot hear.** Everything above proves the *mechanism*. It cannot tell you whether a
  house sounds inhabited, whether the flurries read as alive or as broken, or whether the variant
  set is large enough that nobody learns it. `Run-VoiceTest.ps1:30` already states the limit
  outright — audio quality and feel are "explicitly not provable here". The repo shipped an entire
  island rotated 90° through a green suite. That precedent applies to sound too, and it is worse
  here, because a wrong soundscape does not even look wrong in a screenshot.

*Scope note: written 2026-07-28 against `feat/neighbourhood-exterior` @ `0e715f3`, where the only
procedural audio is `SfxLab`'s twelve cached one-shots, there are no audio asset files at all, no
ambient bed, no generative scheduler, no ecosystem or house state model, no session-monotonic
quantity, no `Ambient` bus, no event bus, and zero tests touching audio. The newest design doc for
this branch names **audio** explicitly as out of scope. Current bite: **none** — there is nothing
to schedule and no state to react to. The one thing this skill can do today is stop the next agent
hand-rolling a client-seeded `async void` scheduler that random-walks its emitters off the map.
First real test: one generative texture layer in the house, running a full sleepover session with
2+ humans, with the determinism test green under `Run-AllTests.ps1` and the recipe variant set
warmed at load. Passing looks like nobody able to describe the ambience afterwards, nobody having
noticed a repeat, two players still hearing the same house at minute forty, and — the part
headless will never tell you — somebody asking "was that us?" about a sound that was texture.*
