---
name: sound-anticipation-escalation
description: Use when a tension value vfx-escalation owns must become audible in Sail — a filter opening as dread rises, layers building to a crescendo, a pulse accelerating, distortion past a threshold — as real Godot 4.7 bus DSP and voice management, not a volume knob. Never computes the tension value — that stays vfx-escalation's.
---

# sound-anticipation-escalation

## Overview

**The sound of a build, once someone else has decided there is one.** Filter sweeps, layered
crescendo activation, accelerating pulse, progressive distortion — the DSP and voice work that
turns a single scalar into audible dread, plus the honest bill for it.

**It does not own the scalar.** `vfx-escalation` owns the session escalation curve, the dread
state machine, the tension budget, the earned breather, and the rule that a session-monotonic
quantity must be *derived* from `CycleDriver` rather than read off it. This skill is strictly
downstream: it consumes a `tension01` handed to it through `vfx-escalation`'s two-call
downstream contract — *"how hard should I be pushing?"* — and turns that answer into sound.

The source brief for this skill proposes a `TensionDirector : Node` that computes the value
itself. **Reject it and delegate.** Details in pattern 1; the short version is that it
duplicates a skill that already exists and reproduces the two bugs that skill was written to
correct.

Premise the brief gets right, and worth keeping: dread is anticipation of harm, not harm, and
audio is unusually good at it because a filter cutoff or a pulse interval moves continuously and
below the threshold of notice, where most visual cues are steppy. That is a real argument for
building the build in audio. It is not an argument for building the tension in audio.

## Directed, not decided — and, uniquely here, not even computed

**This skill executes a direction. It never originates one.** Two layers above it, not one:

| Layer | Owns | Skill |
|---|---|---|
| Affect | whether a build should exist, where, and what it should make people feel | `/direct`, `THRILL-BIBLE.md` |
| Conduction | the tension curve, the state machine, the budget, the breather | `vfx-escalation` |
| Timing | when a peak may land, the reinforce/withhold/decouple ratio | `vfx-audio-sync` |
| Content | the bed being swept, the layers being activated | `sound-soundscape-construction` |
| **This skill** | how a handed `tension01` becomes filter, layer, tempo and drive — and what that costs in voices and DSP | — |

**If you are invoked with no `tension01` behind you, say so and stop.** "Make it build" with no
conductor is a request for a volume automation, not dread.

Three open forks bear directly here and none of them is this skill's to close:

- **`THRILL-BIBLE.md` §9 — the tone axis is decided (2026-07-26); the *ratio* is not.** How often
  a dread build cashes out absurd versus stays sincere is explicitly open, with the ripeness
  trigger *"the first playtest in which anyone is actually frightened."* **An escalation system
  forecloses that ratio by construction**, because it commits a register for the whole build.
  Flag which position each technique takes and let Talon pick — pattern 6 has the mapping.
- **`THRILL-BIBLE.md` §6.2 — night ambient floor vs. night reversal, a live conflict.** The
  obvious hook for a house build is the night side of `CycleDriver`, and a DSP curve that assumes
  night is maximum tension has quietly taken a side in a fork nobody has resolved.
- **`THRILL-BIBLE.md` §4.4 — the one-spike-per-session ceiling is
  `[research default — pending Talon confirmation]`.** Any config field encoding "how many times
  a session may reach the top tension band" carries that marker verbatim into the code comment.

And one blank the whole `sound-*` family carries: **`LEVEL-BIBLE.md` §8.3 is `[BLANK — Talon]`**
on whether Sail commits to a dedicated diegetic audio channel that is *always* meaningful. That
bears hard here. If the answer is yes, then a furnace that only ever cycles as a dread ramp is a
system-state channel and must stay honest; if no, it is free to lie. The bible itself says this
*"constrains sound design broadly, which is why it is a call rather than a detail."*

## When to Use

- `vfx-escalation` is producing a tension value and something has to make it audible
- A build reads as "someone turned the music up" and needs to read as "more is happening"
- A filter sweep, layered crescendo, tempo ramp or drive ramp is being written
- An existing escalation sounds like it does its work early and then stalls
- The concurrent-voice or bus-DSP cost of an escalation layer needs counting before it is built

**Not for:** computing, storing, decaying or replicating the tension value itself
(`vfx-escalation` — the state machine, the budget and the earned breather all live there, and
this skill has no opinion on any of them); whether a build should exist or where (`/direct`);
when a peak is allowed to land, the reinforce/withhold/decouple ratio, or wrong silence
(`vfx-audio-sync`); the ambient bed being swept and the layers being activated
(`sound-soundscape-construction`); the `AudioEffect*` class API reference itself
(`sound-real-time-effects` — cross-referenced below, never duplicated here); anything in
`scripts/voice/`, which is shipped, tuned and out of bounds for every skill in this family.

## What exists in the repo today

Read this before proposing anything. **Every technique in this file is downstream of content
that does not exist**, and the negative findings are the useful ones.

| Thing | State on `feat/neighbourhood-exterior` |
|---|---|
| Any `tension01`, escalation curve or dread state machine | **Does not exist.** `vfx-escalation`'s own scope note records its current bite as *none*. There is nothing to consume. |
| An ambient bed to sweep | **Does not exist.** No looping or continuous audio of any kind; every playback path is one-shot. `THRILL-BIBLE.md` §9: the bed *"is on the critical path."* |
| A `"Tension"` bus | **Does not exist.** Buses are `Master`, `Sfx`, `Voice`, `VoiceCapture`, `PA` — all created in code. There is no `default_bus_layout.tres`. |
| Loopable audio content | **Does not exist.** Zero `.ogg`/`.wav`/`.mp3` in the repo. `SfxLab` synthesises 12 one-shots; `SfxLab.ToWav` sets `Format16Bits`, `MixRate`, `Stereo = false` and **no `LoopMode` at all**. There is no layer to activate. |
| Any `AudioEffect*` usage | **Exactly one site:** `VoiceManager.EnsurePaBusName()`. That is the only working in-repo reference for real DSP parameter values, and it is worth copying rather than inventing. |
| A pooled home for sustained layers | **No.** `SfxLab`'s 14-slot pool prefers an idle slot, then **steals round-robin** (`victim.Stop()`). A sustained crescendo layer parked in it gets cut mid-sustain by the next footstep. |
| A player-facing volume slider for a new bus | **No.** `scripts/ui/SettingsPanel.cs` has Master and Voice only. Any bus a player should be able to turn down needs a slider added there. |
| Audio test coverage | **None.** No `Run-SfxTest.ps1`, no `Run-AudioTest.ps1`. Nothing in the suite asserts an `AudioStream`, an `AudioServer` bus, or `SfxLab`. |

**Setting, and the brief is stale on it.** Not a forest. A 90s suburban house and its
neighbourhood ring, 2–6 kids at a sleepover, and the thing in the attic. A build here is *the
house getting louder in the wrong way* — the furnace kicking on, the fridge compressor cycling,
the water heater, joists in the ceiling, the TV in the next room, a bathroom fan, rain on a
single-pane window. Every one of those is diegetic, none is a musical stinger, and that lines up
with the brief's own tuning note that players report tension *music* as intrusive.

## Core patterns

### 1. Consume the tension, never compute it

The brief's `TensionDirector` is `vfx-escalation`'s job, and its sample reproduces the exact two
bugs that skill already corrects:

- **It decays on `_Process`.** `CycleDriver` advances from `_PhysicsProcess` — verified in source
  (`scripts/game/world/CycleDriver.cs:103`, `_PhysicsProcess(double delta)` branching
  `ServerTick`/`ClientTick`), and its own doc comment says the sim tick is chosen *"never a
  wall-clock `Timer`, which would drift from physics and desync from whatever later derives …
  on the same cadence."* A tension value decaying on the render frame drifts against the clock it
  is supposed to be derived from. `vfx-escalation` pattern 3 makes the same correction; do not
  make it a second time in a second place.
- **It derives tension from a local `bool` with no server authority.** `_threatPresent`, set by
  a local `SetThreatPresence`, gives every client a different curve. `vfx-escalation` pattern 3
  is explicit: the server decides, the outcome is replicated, because two players on different
  curves is a desync and not §5.4 information asymmetry.

There is a third, quieter problem: `RiseRatePerSecond = 0.05f` against `DecayRatePerSecond =
0.15f` is a *tension budget shape* — a slow build and a fast release — expressed as two loose
constants in an audio class. That is `vfx-escalation`'s earned breather, and encoding it here
means the breather exists in the audio and nowhere else.

**So the contract is one direction, one value:**

```csharp
/// Consumes vfx-escalation's downstream contract. This class NEVER writes _tension —
/// it has no threat model, no decay rate and no authority. If the value is wrong, the
/// bug is upstream. Smoothing below is envelope smoothing on an already-authoritative
/// target, not a second tension curve wearing a hat.
private float _tension;        // last authoritative target
private float _smoothed;       // what the DSP actually follows, per physics tick
```

Everything downstream in this file takes `tension01` as an argument. Nothing in this file
decides what it should be.

### 2. Emit on meaningful change, not every frame

The brief emits `TensionChanged` from `_Process` unconditionally: 60 emissions per second to N
subscribers, almost all of them carrying a value that has not audibly moved. At a 0.05/s rise
that is 1200 emissions across a 20-second build to express a quantity that changes by 0.0008 per
frame.

**Why a busy signal is worse than it looks.** Each emission crosses the C#↔engine marshalling
boundary once per subscriber, and every handler the brief writes does real native work inside it
— a `VolumeDb` write per layer (8 layers = 8 property sets), a filter `CutoffHz` write, a
distortion `Drive` write. Filter coefficient re-derivation on a cutoff write is
`unverified against Godot 4.7`, but the property writes themselves are not free and they are
being made to express nothing.

**The shape bug is worse than the cost.** `LayeredCrescendo.OnTensionChanged` does
`VolumeDb = Mathf.MoveToward(VolumeDb, target, 40f * GetProcessDeltaTime())` **inside the signal
handler**. That silently assumes the signal fires exactly once per frame. Quantise the emission
and the fade rate drops with it; emit twice in a frame and the fade doubles. The smoothing rate
is coupled to the emission rate, which is exactly the coupling that makes the busy signal look
mandatory.

**Decouple them.** The signal carries a *target*; every subscriber smooths toward it on its own
`_PhysicsProcess`, matching `CycleDriver`'s tick:

```csharp
// Quantisation threshold: emit only when the value moved enough to be expressible.
// 0.01 is one octave-hundredth of pattern 3's sweep (~0.033 octaves) and roughly a
// 1% tempo step in pattern 5 — both below any plausible JND, and at a 0.05/s rise it
// still yields ~100 emissions across a full build. Starting point, not a gate.
private const float TensionEpsilon = 0.01f;

private void SetTension(float tension01)
{
    if (Mathf.Abs(tension01 - _lastEmitted) < TensionEpsilon)
        return;
    _lastEmitted = tension01;
    EmitSignal(SignalName.TensionTargetChanged, tension01);
}

// Subscribers do this, on their own tick — NOT in the handler:
public override void _PhysicsProcess(double delta) =>
    _smoothed = Mathf.MoveToward(_smoothed, _target, SmoothingPerSecond * (float)delta);
```

`Mathf.MoveToward(from, to, delta)` is house idiom — `AvatarMotor.cs:94-95`,
`InteractPrompt.cs:107`, `AvatarVisual.cs:351` — and mirrors `@GlobalScope.move_toward(from, to,
delta)`, **verified against Godot 4.7** docs. `[Signal]` + `EmitSignal(SignalName.X, …)` is the
house signal form.

### 3. The filter sweep must be logarithmic — this is the correction that matters most

The brief writes `_filter.CutoffHz = Mathf.Lerp(800f, 20000f, tension01)`. **Pitch and
brightness are perceived as ratios, not differences.** A linear lerp in Hz is a linear lerp
across a perceptual axis that is logarithmic, and the arithmetic is brutal:

| `tension01` | Linear `Lerp(800, 20000, t)` | Octaves above 800 Hz | Perceptual span delivered |
|---|---|---|---|
| 0.0 | 800 Hz | 0.00 | 0% |
| 0.115 | 3 000 Hz | 1.91 | 41% |
| 0.167 | 4 000 Hz | 2.32 | 50% |
| 0.5 | **10 400 Hz** | 3.70 | **80%** |
| 1.0 | 20 000 Hz | 4.64 | 100% |

The whole span is `log2(20000/800) ≈ 4.64` octaves. **Half the perceived movement is finished by
`tension01 = 0.167`**, and the entire top half of the tension range moves the filter from
10.4 kHz to 20 kHz — 0.94 octaves, in a band where a house bed of furnace hum, compressor rumble
and ceiling creak has almost no content for a low-pass to reveal. The sweep spends itself in the
first sixth of the build and then contributes nothing for the remaining five-sixths. The build
stalls exactly where it should be tightening, and whatever discrete thing fires later (pattern 4's
top layer, pattern 6's distortion) reads as an *arrival* rather than a culmination.

**The correct form travels evenly in octaves:**

```csharp
/// Perceptual sweep. Hz is not a perceptual axis; octaves are. This walks
/// log2(Ceiling/Floor) octaves linearly across tension01, so equal tension steps
/// produce equal perceived brightness steps. Ceiling is NOT 20 kHz — see below.
private const float SweepFloorHz   = 800f;
private const float SweepCeilingHz = 8000f;   // 3.32 octaves. Starting point.

private void ApplySweep(float tension01) =>
    _filter.CutoffHz = SweepFloorHz * Mathf.Pow(SweepCeilingHz / SweepFloorHz, tension01);
```

At `tension01 = 0.5` that lands on **4 000 Hz** — the exact octave midpoint — against the linear
form's 10 400 Hz. Same endpoints, and the movement is now where the ear is.

Three things the brief omits, all verified:

- **`cutoff_hz` "Value can range from 1 to 20500"**, default 2000.0 — **Verified against Godot
  4.7** (`AudioEffectFilter`). A 20 kHz ceiling is legal and useless: a low-pass there is
  effectively bypassed. Set the ceiling near where the bed's content actually ends, which is
  unknowable until a bed exists. The repo's one real reference is `EnsurePaBusName`'s 2200 Hz,
  documented in place as *"small driver, no highs"*.
- **The slope is a bigger lever than the range, and the default is the gentlest one.**
  `AudioEffectFilter.db` is a `FilterDB` enum defaulting to `0`, which is `FILTER_6DB`
  (`FILTER_6DB=0, FILTER_12DB=1, FILTER_18DB=2, FILTER_24DB=3`) — **Verified against Godot 4.7**.
  At 6 dB/octave a cutoff at 4 kHz still passes a great deal of 8 kHz content, so the same sweep
  reads far weaker than expected. If a correctly-logarithmic sweep still feels flat, raise the
  slope before widening the range.
- **`resonance` is what makes a sweep sound like searching rather than a tone control.** Float,
  default 0.5, *"Value can range from 0 to 1"* — **Verified against Godot 4.7**. `EnsurePaBusName`
  uses 0.6.

### 4. The layered crescendo — and what a "silent" layer actually costs

The brief's premise is right and worth keeping: **never escalate by raising one master volume.**
Intensity should read as *more is happening*, not *the same thing is louder*, and threshold-based
layer activation is the standard way to get that.

Its implementation is where the bill lands. `LayeredCrescendo` holds N `AudioStreamPlayer3D`
layers and drives each toward `0f` or `-80f` dB. **A layer at -80 dB is still playing.**
`volume_db` is *"The base sound level before attenuation, in decibels"* with default 0.0 and no
documented floor — **Verified against Godot 4.7** (`AudioStreamPlayer3D`). -80 dB is a
convention, not an off switch: `db_to_linear(-80) = 1e-4`. The voice is still mixed, still
occupies a slot, and still counts.

**Do the arithmetic against the shipped budget.** `SfxLab.cs:42-44`, verbatim: *"§4 budgets ≤24
concurrent 3D players INCLUDING the 5 voice speakers and ambience; 14 one-shot slots keeps the
worst case comfortably inside."*

| | Voices |
|---|---|
| Voice speakers (5 remote peers at the 6-player ceiling) | 5 |
| `SfxLab` one-shot pool | 14 |
| Subtotal, before any atmosphere at all | **19** |
| Headroom inside the ≤24 budget | **5** |
| Brief's dread peak: "6–8 layers" | 6–8 |
| Total at **tension zero**, with fade-to-silence layers | **25–27** |

**The brief's crescendo is over budget by 1–3 voices before anything happens.** Five is the
ceiling for positional sustained layers, and that assumes every one-shot slot may be busy
simultaneously — which is precisely the moment a group panic makes likely.

Three ways out, each with a cost, and the choice is a stated decision:

1. **Non-positional layers.** A plain `AudioStreamPlayer` is outside the 3D concurrency budget
   entirely. It also makes the build identical everywhere in the house, which flattens §5.4's
   information asymmetry — the fork `vfx-audio-sync` pattern 1 already names for the bed. Whatever
   the bed chose, the crescendo should match it, or the two will disagree spatially.
2. **Stop layers instead of fading them.** Reclaims the voice, and reintroduces a start transient
   at exactly the moment the layer is supposed to appear unnoticed. Workable if the layer's own
   attack is long, which is content, not DSP.
3. **Fewer, better layers.** Three layers with distinct spectral identity read as more change
   than eight overlapping ones, for the same reason `vfx-particles` finds that fewer, larger,
   better-lit particles beat many small ones.

**Layers never live in the `SfxLab` pool.** `Rent` steals round-robin when every slot is busy, so
a sustained layer would be `Stop()`ped by the next footstep. They need their own long-lived nodes,
and those nodes are what the table above counts.

### 5. Accelerating rhythm — right instinct, three fixes

The brief's claim that *acceleration, more than loudness, is what the nervous system responds to*
is the most useful thing in it, and the pattern is worth building. Three corrections:

- **Accumulate on `_PhysicsProcess`, not `_Process`.** Same reasoning as pattern 1, same
  correction `vfx-escalation` already makes. A pulse timer on the render frame drifts against the
  clock everything else derives from, and it varies with frame rate across a 2–6 player session.
- **The exports are named backwards.** `MinIntervalSec = 1.2f` is *larger* than
  `MaxIntervalSec = 0.25f`, because "min" means minimum tension and "max" means maximum tension —
  not minimum and maximum interval. This is a naming trap that the next person to edit the file
  will invert, and the resulting bug (a pulse that *decelerates* under tension) is subtle enough
  to survive a review. Name them `SlowestIntervalSec` and `FastestIntervalSec`.
- **Tempo is perceived as a ratio too — pattern 3's correction applies again.**
  `Lerp(1.2f, 0.25f, 0.5f)` is 0.725 s; the geometric midpoint is `sqrt(1.2 × 0.25) ≈ 0.548 s`.
  The linear form front-loads the deceleration half and crowds the acceleration into the top of
  the range. Use `Slowest * Pow(Fastest/Slowest, tension01)`, the same expression as the sweep.

**On simultaneity.** A pulse derived locally from a replicated tension value drifts in phase
between clients. That is acceptable here and only here: **a pulse is a bed, not a beat.** §4.1's
simultaneity condition governs *peaks*, and peaks belong to `vfx-audio-sync` and are
server-authored and replicated there. If a build ever needs its pulse to land in the same instant
on every client, it has stopped being a pulse and become a peak — route it.

**A heartbeat specifically is both a trope and a §8.3 hazard.** A heartbeat that only ever runs
when something is near is a perfectly reliable telegraph: its *absence* becomes an all-clear, and
its presence announces that the game thinks the player should be frightened, which is §4.2's
legibility failure inverted. In a 90s house the diegetic candidates are better and cheaper: the
furnace cycle, the fridge compressor, the boiler expansion tick, a fan. They also survive §8.3 far
better, because they have a plausible reason to fire when nothing is wrong.

### 6. Distortion as wrongness — verified parameters, and the register it commits

The brief's threshold curve is correct and worth keeping: `t = Clamp((tension01 - 0.6f)/0.4f, 0,
1)` so that early tension does not sound broken. Drive rising only in the top 40% of the range is
what keeps distortion legible as *something is wrong* rather than as *the audio is clipping*.

**Verified against Godot 4.7** (`AudioEffectDistortion`): *"Adds a distortion audio effect to an
audio bus. Remaps audio samples using a nonlinear function to achieve a distorted sound."*

| Property | Type | Default | Documented range |
|---|---|---|---|
| `drive` | float | 0.0 | 0 to 1 |
| `pre_gain` | float | 0.0 | -60 to 60 dB |
| `post_gain` | float | 0.0 | -80 to 24 dB |
| `keep_hf_hz` | float | 16000.0 | 1 to 20000 Hz |
| `mode` | `Mode` | 0 | enum |

**The enum members are `MODE_CLIP=0, MODE_ATAN=1, MODE_LOFI=2, MODE_OVERDRIVE=3,
MODE_WAVESHAPE=4`** — **Verified against Godot 4.7**. The brief names these elsewhere as
`Overdrive`, `Clip`, `Tan`, `Bitcrush`, `WaveShape`. **`Tan` and `Bitcrush` do not exist**; the
real members are `Atan` and `Lofi`. In C# they are `AudioEffectDistortion.ModeEnum.Clip` /
`.Atan` / `.Lofi` / `.Overdrive` / `.Waveshape`, and `VoiceManager.EnsurePaBusName()` ships
`ModeEnum.Overdrive` as the in-repo proof of the casing.

That same method is the one working reference for real values in this repo, and its numbers are
tuned rather than guessed: `Drive = 0.28f, PostGain = -3f`. **The brief's 0.35 ceiling is in the
same neighbourhood as a value that already sounds like a speaker cone in a creepy building** —
which is a reason to start there and a reason to notice that Sail's existing distortion is
already spoken for by the PA route. Two systems using overdrive for different meanings will
collide.

**Register foreclosure — flag, do not pick.** §9's ratio is open, and these four techniques do
not sit equally on the sincere/absurd axis:

| Technique | Register it commits |
|---|---|
| Filter sweep | neutral — works under either position |
| Layered crescendo | neutral to sincere, depending entirely on layer content |
| Accelerating pulse | leans sincere; a *heartbeat* is a genre commitment |
| **Progressive distortion** | **sincere only.** There is no absurd reading of a bed going to overdrive. |

A build that has been sincerely distorting for forty seconds has already spent the moment an
absurd payoff would have needed. State that when proposing pattern 6; do not resolve it.

### 7. A build that always precedes a threat is a reliable telegraph

**`THRILL-BIBLE.md` §8.3 forbids exactly this: a cue that always fires has taught players when
they are safe.** An escalation system is the easiest place in the codebase to violate it, because
it is *designed* to correlate with danger — the correlation is the feature.

**The fix is distributional, not per-trigger, and it is not this skill's.** Every individual
sweep, layer and drive ramp can be perfectly built and the system still fails, because what
players learn is the distribution: if the furnace ramp precedes the hand every time, its silence
becomes an all-clear and the game has handed the group a safety oracle. `vfx-audio-sync` owns the
ratio that operates on the distribution rather than on any instance, and `/direct` sets it per
level and records it in `THRILL-BIBLE.md` §10.

What this skill can do about it: build the ramp so that it *can* run without a payoff — no
resolution baked into the top of the curve, no stinger wired to the peak of the sweep — and make
what actually fired measurable. `scripts/telemetry/` is a live Firebase path with zero audio
hooks today; a counter per full-tension arrival and per payoff is cheap and is the only way to
check afterwards whether the correlation is as tight as it feels. The brief's own tuning advice
(vary the rise rate per encounter, occasionally allow a false de-escalation) is the right
instinct pointed at the wrong layer — both are `vfx-escalation`'s curve, not this skill's DSP.

## The cost — "cheap, use liberally" is the wrong closing note

The brief closes with *"all of this is bus-level effects or property changes, negligible CPU —
this skill is cheap; use it liberally."* Both halves are wrong, in different ways.

**Correction: Godot does auto-bypass a silent bus, and this file previously said otherwise.**
`sound-real-time-effects` pattern 5 has the citation — the official *Audio buses* guide states
plainly, **verified against Godot 4.7**: *"There is no need to disable buses manually when not
in use. Godot detects that the bus has been silent for a few seconds and disables it (including
all effects)."* `AudioEffectInstance._ProcessSilence`'s docs corroborate: a force-always-run
opt-in (*"even if the bus has been muted or cannot otherwise be heard"*) only makes sense if
not-running is the default. So the engine already does the thing this section used to tell you
to do by hand — do not add manual `SetBusBypassEffects` gating on the strength of a cost that
does not exist.

**The one legitimate caveat survives the correction.** The auto-disable takes *"a few
seconds"* of silence to engage. A `"Tension"` bus that is regularly tickled — a sweep nudged
every couple of seconds even while dread sits near zero — never reaches the idle threshold and
may still pay its full per-block DSP cost continuously. That is a real cost this skill's design
can create; size the sweep's minimum update cadence with it in mind rather than assuming
near-silence is free just because true silence would be.

**The layers a crescendo activates are the real bill**, and pattern 4 already did that
arithmetic: 5 voice speakers + 14 `SfxLab` slots leaves **5 voices** of headroom inside the ≤24
budget, and "6–8 layers at dread peak" does not fit even at rest. That number has to be paid for
before it is designed around.

**Nothing has ever been profiled on the floor spec** (GTX 970, Forward+; integrated graphics is
never a target). Audio is CPU, and the same CPU is decoding up to five Opus voice streams at
50 packets/second. Establish a measured baseline before adopting any of the numbers here.

**Headless cannot hear.** `tests/Run-*.ps1` prove mechanism and replication; `Run-VoiceTest.ps1`
says so in its own header — *"Audio quality/feel is explicitly not provable here."* What **is**
genuinely CI-provable and should be tested: the perceptual mapping itself. `Mathf.Pow`-based
sweep and tempo curves are pure arithmetic and belong in a `public static class` in exactly the
shape of `CyclePhase.FromElapsed`, so a headless test can assert that `t = 0.5` lands on the
octave midpoint. What is not provable: whether any of it sounds like dread. This repo shipped an
island rotated 90° through a green suite; a wrong sweep will pass everything.

## Tuning guide — no numbers as law

- **800 Hz floor / 8 kHz ceiling.** The floor sits below the fundamental of most household hum;
  the ceiling should sit near where the bed's content actually stops, and that is unknowable until
  a bed exists. Both move once there is something to sweep.
- **`FILTER_12DB` or `FILTER_18DB` before a wider range.** If a correct logarithmic sweep still
  reads weak, the slope is the lever. Default `FILTER_6DB` is the gentlest option available.
- **`Resonance` 0.5–0.6.** The PA bus's 0.6 is the only tuned value in the repo. Above ~0.8 the
  filter starts to sing, which is a musical result and takes a §9 position.
- **Activation thresholds 0.2 / 0.4 / 0.6 / 0.8.** Even spacing is a placeholder, and probably
  wrong: the top of a build wants more happening per unit of tension than the bottom does, so
  expect the upper thresholds to compress. Playtest call.
- **Slowest 1.2 s / fastest 0.25 s pulse**, travelled geometrically. The *ratio* (4.8×) is what is
  being tuned, not the endpoints.
- **Distortion onset at 0.6, drive to ~0.3.** Onset is what keeps it legible as wrongness; the
  ceiling should stay near `EnsurePaBusName`'s tuned 0.28 rather than exceeding it, or the PA
  route and the dread build become the same sound.
- **Quantise the tension signal at ~0.01.** Below any plausible JND on either the octave or the
  tempo mapping, and still ~100 emissions across a 20 s build.
- **Five concurrent positional layers is the hard ceiling**, not a dial — it is arithmetic against
  `SfxLab`'s documented budget, and the only ways past it are the three in pattern 4.

## Integration points

- **`vfx-escalation`** — upstream, and the only source of `tension01`. Its two-call downstream
  contract (*"how hard should I be pushing?"* / *"should I fire this?"*) is the interface. Never
  reimplement the curve, the budget or the breather.
- **`vfx-audio-sync`** — owns the ratio, the peak, wrong silence, and the two-channel rule. **A
  dread build and an urgency cue may not share a bus, sound family, frequency band or spatial
  signature** — which directly constrains this skill, because a wide filter sweep occupies a lot
  of frequency band. Check the sweep range against whatever the urgency cue ends up occupying.
- **`sound-soundscape-construction`** — owns the bed and the layers. This skill sweeps and
  activates content it does not author. Bed first; there is no order in which that is not true.
- **`sound-real-time-effects`** — owns the `AudioEffect*` class reference. Parameter ranges are
  quoted above only where they are load-bearing for a correction; go there for the API surface.
- **`/direct`** — upstream of everything, and the write-back target for `THRILL-BIBLE.md` §10.
- **`SfxLab.EnsureBus`** — the idempotent five-line bus pattern any `"Tension"` bus copies:
  `GetBusIndex` guard, `AddBus(BusCount)`, `SetBusName`, `SetBusSend(idx, "Master")`. Effects go on
  afterwards, `EnsurePaBusName`-style.
- **`scripts/ui/SettingsPanel.cs`** — a new bus a player should be able to turn down needs a
  slider here. There is no Sfx slider today either; adding one is a separate, honest finding.
- **`CycleDriver.Instance`** — `_PhysicsProcess`, `Synced`-gated. Read it for the tick discipline;
  never bind escalation to `Phase`, which is cyclic and wraps every 120 s by default.
- **`scripts/voice/`** — out of bounds. Never add DSP to the `Voice` bus, never fork
  `VoiceConfig`. Ducking a build under speech is a **mix** decision and lives on the world side.
- **`scripts/telemetry/`** — live Firebase, zero audio hooks. Counters for full-tension arrivals
  and payoffs are buildable on what ships and are the only after-the-fact check on §8.3.

## Precedent

- **`VoiceManager.EnsurePaBusName()`** — the only `AudioEffect*` chain in the repo, and the one
  set of tuned values to reason from: `Overdrive`/`Drive 0.28`/`PostGain -3`, lowpass at 2200 Hz
  with `Resonance 0.6`, boxy reverb. Its doc comment also states the cost model this skill
  inherits: *"Bus DSP runs once per audio block regardless of listener count."*
- **`SfxLab.cs`** — the pooling contract, the ≤24 concurrent budget, and the round-robin steal
  that makes it the wrong home for sustained layers.
- **`CyclePhase.FromElapsed`** — the house precedent for pulling arithmetic out of a `Node` into a
  pure static class purely so it can be tested headless. The sweep and tempo curves belong in the
  same shape.
- **Amnesia: The Dark Descent / SOMA** — the brief's cited precedent, and it holds: continuous
  filter and layer movement tied to a hidden sanity variable rather than discrete musical stings.
  The transferable lesson is *hidden and continuous*, not the sanity meter — Sail's equivalent
  hidden variable is `vfx-escalation`'s, and it should stay as invisible as theirs.
- **`vfx-particles`** — the family's precedent for a skill that is honest about being
  infrastructure, and for the "fewer and better beats more and thinner" argument that pattern 4
  reuses.

## Troubleshooting

- **"The build sounds like someone turning a knob."** A master fader is being automated. Use
  threshold-based layer activation — the brief is right about this and it is the whole point of
  pattern 4.
- **"The sweep does its work in the first few seconds and then nothing."** A linear `Lerp` in Hz.
  Pattern 3. This is the failure most likely to be misdiagnosed as "needs more layers."
- **"The sweep is logarithmic and still feels weak."** `db` is at its `FILTER_6DB` default. Raise
  the slope before widening the range.
- **"Layers fade at different speeds depending on frame rate."** Smoothing is running inside the
  signal handler with `GetProcessDeltaTime()`. Pattern 2 — smooth on the subscriber's own tick.
- **"A layer gets cut off mid-sustain."** It is renting an `SfxLab` pool slot and got stolen
  round-robin. Sustained layers need their own nodes, counted against the ≤24 budget.
- **"Audio drops out under load / one-shots stop firing during a panic."** The concurrency ceiling
  has been reached. Do the pattern 4 arithmetic before blaming the pool.
- **"Distortion clips at low `Drive`."** The brief's answer — reduce pre-distortion bus gain — is
  correct. `pre_gain` is the documented knob, -60 to 60 dB.
- **"The pulse decelerates as tension rises."** Someone read `MaxIntervalSec` as the maximum
  interval. Pattern 5's naming trap, arriving exactly as predicted.
- **"Players call the tension audio intrusive."** The brief's fix is sound: favour filter sweeps
  and rhythm acceleration over anything that reads as music. In a house, prefer diegetic sources
  that have a reason to exist when nothing is wrong.
- **"Players know when they're safe."** §8.3, and it is not a DSP bug. Route to `vfx-audio-sync`
  and `/direct` — the ratio, not the trigger.
- **"Green suite, and it sounds wrong."** Expected. Headless proves the arithmetic, never the mix.

## Caveats

- **This skill does not own the tension value.** `vfx-escalation` owns the curve, the state
  machine, the budget and the breather; `/direct` owns whether a build should exist and where;
  `vfx-audio-sync` owns the peak and the ratio; `sound-soundscape-construction` owns the bed and
  the layers. If a proposal here starts deciding *when* tension rises, it has left this skill.
- **The brief's `TensionDirector` is rejected in full**, not adapted. Building it here would put
  a second, unreplicated, render-frame tension curve in the codebase alongside the real one.
- **No numbers as law.** 800/8000 Hz, 0.01 quantisation, 0.2/0.4/0.6/0.8, 1.2/0.25 s, drive 0.3,
  five layers — every one is a starting point with reasoning attached, and the reasoning is the
  part that transfers. The single exception worth naming as arithmetic rather than taste is the
  ≤24 concurrent-voice budget, and even that is a documented design budget, not an engine limit.
- **Verified vs unverified, stated.** **Verified against Godot 4.7:** `AudioEffectFilter.cutoff_hz`
  (1–20500, default 2000), `resonance` (0–1, default 0.5), `FilterDB` (`FILTER_6DB=0` … `=3`,
  default 0); `AudioEffectDistortion` mode enum (`MODE_CLIP/ATAN/LOFI/OVERDRIVE/WAVESHAPE`) and
  `drive`/`pre_gain`/`post_gain`/`keep_hf_hz` ranges; `AudioServer.AddBusEffect` /
  `SetBusBypassEffects` / `SetBusEffectEnabled` signatures; `AudioStreamPlayer3D.volume_db`
  (default 0.0, no documented floor); `@GlobalScope.move_toward(from, to, delta)`; the audio-buses
  hardware-limit line. **Marked `unverified against Godot 4.7`:** `Mathf.Pow` (mirrors
  `@GlobalScope.pow`, not individually re-checked), whether a filter `CutoffHz` write re-derives
  coefficients synchronously, and whether the engine internally short-circuits a silent bus.
  Verified in-repo rather than in docs: `CycleDriver._PhysicsProcess`, `Mathf.MoveToward` usage,
  `EnsurePaBusName`'s effect chain, `SfxLab`'s pool and budget, `ToWav`'s absent `LoopMode`.
- **Not a resolver.** §9's tone *ratio* is open (ripeness trigger: the first playtest in which
  anyone is actually frightened) and pattern 6 forecloses it if built silently. §6.2's night-floor
  conflict is a shipped decision in live opposition to doctrine. §4.4's spike ceiling carries
  `[research default — pending Talon confirmation]` verbatim into any config that encodes it.
  `LEVEL-BIBLE.md` §8.3 is `[BLANK — Talon]` and decides whether a diegetic build is allowed to
  lie. None of the four is settled by an audio-engineering argument.
- **Headless cannot hear.** The perceptual curves are pure arithmetic and should be tested. The
  mix cannot be, and needs a headed session with more than one human in the house.

*Scope note: written 2026-07-28 against `feat/neighbourhood-exterior` @ `0e715f3`, a branch with
no tension value, no escalation system, no ambient bed, no loopable audio content of any kind, no
bus beyond `Master`/`Sfx`/`Voice`/`VoiceCapture`/`PA`, and exactly one `AudioEffect*` call site in
the whole repo. `vfx-escalation`'s own scope note records its current bite as none, so the input
this skill consumes has no producer. **Current bite: none** — the one thing it can do today is
stop the next agent writing a second tension curve and a linear-in-Hz filter sweep. First real
test: an ambient bed running in the house for a full session, `vfx-escalation` producing a real
derived `tension01` above it, and one build driven end to end through the sweep and two layers,
in a recorded 2–6 player session. Passing looks like the audible version of `vfx-escalation`'s
own test — a session whose tension, if plotted, has visible valleys — heard rather than plotted:
players lowering their voices without being able to say what changed, and nobody afterwards
describing it as the music getting louder.*
