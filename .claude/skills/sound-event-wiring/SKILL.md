---
name: sound-event-wiring
description: Use when a Sail audio cue needs its trigger wired — what dispatches it, aligning it to a procedural animation beat or a visual it must land with, firing exactly once per event, and every player hearing it in the same instant rather than merely the same frame locally.
---

# sound-event-wiring

## Overview

**How an audio cue gets triggered.** The dispatch architecture, the alignment of a sound to an
animation beat and to a visual, and the difference between two responders firing on the same
frame of one client and 2–6 players perceiving one event in the same instant.

**This is not `vfx-audio-sync`.** The two are easy to confuse — this file was called
"audio-visual sync" in its source brief — so the split is stated once, plainly:

| | `vfx-audio-sync` | `sound-event-wiring` (this file) |
|---|---|---|
| Question | *should* this sound and this visual land together — reinforce, withhold, decouple? | *how* does the trigger get from that decision to `Play()` |
| Owns | the sync ratio `/direct` sets per level, the bed, wrong silence, the voice boundary | the call site, the fan-out, the replication, the fire-once guarantee |
| Fails by | teaching players when they are safe | firing twice, firing on one client, firing where nobody was looking |

Reach for `vfx-audio-sync` first; reach for this file once the answer is "yes, both, together"
and the question becomes what emits it. **Two things in the brief are wrong and they are this
file's headline.** It recommends building
a `GameEvents` autoload *before writing any actual sound* — the repo has a standing written
decision not to build that bus yet. And it claims two responders on one signal means "no risk of
drift," conflating same-frame-locally with same-instant-between-players. The second is the more
expensive error because it looks solved.

## Directed, not decided

**This is infrastructure, not affect, and it should say so plainly.** It serves no
`THRILL-BIBLE.md` section directly. It exists so that when a directed beat fires, it fires once,
everywhere, when it was supposed to. The only second-order doctrine link worth claiming: §4.1's
**simultaneity** condition is a *networking* property, and a wiring mistake destroys it outright.
Everything about what a cue should *mean* goes upward — see *Not for:* below.

**Forks carried, unresolved.** §9's tone **axis** was decided 2026-07-26; the **ratio** is not,
and its ripeness trigger is *"the first playtest in which anyone is actually frightened"* — a
dispatch layer is where that ratio will eventually be *measured*, which is not the same as being
allowed to pick it. §6.2's night ambient floor versus night reversal is a live conflict between a
shipped decision and doctrine. §4.4's one-spike-per-session ceiling is `[research default —
pending Talon confirmation]`; carry that string **verbatim** into any code comment or config
enforcing a spike budget. And `LEVEL-BIBLE.md` §8.3 is `[BLANK — Talon]` on whether Sail commits
to a diegetic audio channel that is *always* meaningful — *"this constrains sound design broadly,
which is why it is a call rather than a detail."* It bears here more directly than anywhere else
in the family: `ActorEvent.Bark` exists at ordinal 11 and **no shipped profile maps it**. If §8.3
resolves toward the Deep Rock position — no idle chatter, so any bark carries system state — the
dispatch layer is carrying *meaning*, not just timing.

## When to Use

- A new gameplay event needs a sound and the question is what fires it
- An existing cue fires twice, fires late, or fires for one player and not the others
- A sound has to land on a specific beat of a procedural animation
- A cue must be perceived simultaneously by every player in the session
- Someone is about to build an event bus, a singleton, or a fourth autoload for audio
- A late joiner or a reconnecting peer is in the wrong audio state
- Dispatch wiring needs a headless test

**Not for:** whether the sound should exist or what it means (`/direct`); whether it shares a
moment with a visual, and the ratio governing how often (`vfx-audio-sync`); the escalation state
being dispatched on (`vfx-escalation`); pooled playback, bus creation and the ≤24
concurrent-3D-player budget (`sound-optimization`, `SfxLab`); particle systems (`vfx-particles`);
the proximity voice pipeline in `scripts/voice/`, which is shipped, tuned and out of bounds for
every skill in this family.

## What exists in the repo today

The negative findings are the load-bearing ones.

| Thing | State |
|---|---|
| `scripts/game/presentation/ActorFx.cs` | **Ships and works.** `static class`, `Fire` → `FireCore`. The single call site between a gameplay event and its cosmetic response. |
| `EventResponse.cs` / `PresentationProfile.cs` | **Ship.** `[Export]`-configured `Resource`s authored as `.tres` — `Sound`, `CustomSound`, `VolumeDb`, `PitchJitter`, `IntensityVolumeBoostDb`, `MinIntensity`, `Puff*`. Lazily-built `ActorEvent → EventResponse[]` index. |
| `ActorEvent.cs` | 10 live ordinals; 4 and 5 retired and reserved. `Bark` and `ScrapSorted` are mapped by nothing. |
| `GameEvents` / `AtmosphereEventBus` / `EventBus` / `SignalBus` | **Does not exist, deliberately.** Zero hits in `scripts/`. Three docs record the deferral. |
| `AnimationPlayer` / `AnimationTree` / `AnimationMixer` | **Zero occurrences in the entire tree** — no `.cs`, no `.tscn`, no `.tres`. Only design docs mention them. All character animation is procedural. |
| Animation→audio alignment | `AvatarVisual._stepPhase` crossing index → `_footPlanted` latch → `ConsumeFootPlant()` → `ActorFx.Fire(…, ActorEvent.Step, …)`. Mirrored in `BodyLanguageEvaluator`. |
| Autoloads | Exactly **three**: `NetworkManager`, `VoiceManager`, `Telemetry`. A fourth is a decision, not a free move. |
| C# signal idiom | `[Signal] public delegate void XEventHandler(…)` + `EmitSignal(SignalName.X, …)` — `CrashReportDialog`, `FeedbackPanel`, `HowToPlayPanel`, `SettingsPanel`, `UsageConsentDialog`, `PlaytestForewordPanel`. **All UI. No gameplay system emits a custom signal.** |
| Unsubscribe precedent | `Gameplay.cs:240-247` — `-=` for every multiplayer handler in teardown. `VoiceManager._Ready` subscribes with a written reason for why it never needs to. |
| Cross-client replication | `scripts/game/world/CycleDriver.cs`. Not an autoload — reached via `.Instance`. |
| `NetCodec` channels | 2 voice, 3 move, 4 prop, 5 cycle. **Next free is 6** — confirm at build time, don't trust this line. |
| Audio assets | **Zero.** Every sound is synthesised at runtime by `SfxLab`. `EventResponse.CustomSound` is the escape hatch to a real file and nothing uses it. |
| `tests/Run-PresentationSelfTest.ps1` | **Ships.** Already tests `ActorEvent` resolution, `.tres` contents, `Fire`'s guards and `FireCore`'s response logic. The existing home for dispatch tests. |

## Core patterns

### 1. `ActorFx` + `EventResponse` is the shipped answer — extend it before inventing anything

The brief does not know this exists. It is data-driven audio *and* VFX co-dispatch from one
resource, already solving the brief's stated problem for actor-scale events, and it is
`.tres`-authorable rather than code. `ActorFx`'s own class doc states the intent:

> *"Controllers never call SfxLab/JuiceFx directly for actor events again — that coupling is
> what this class exists to remove."*

The whole dispatch is four lines (`ActorFx.cs:69-76`):

```csharp
AudioStream? stream = r.CustomSound ?? (r.Sound != Sfx.None ? SfxLab.Get(r.Sound) : null);
if (stream != null)
    SfxLab.PlayStream3D(context, position, stream,
        r.VolumeDb + r.IntensityVolumeBoostDb * intensity, r.PitchJitter);

if (r.PuffCount > 0)
    JuiceFx.Puff(context, position, r.PuffCount, r.PuffColor, …);
```

**Already covered:** the controller reports *what happened*, never *what plays*; one event fans
out to N responses; audio and its particle companion resolve from the same row of the same
resource on the same call, so they cannot drift; a `MinIntensity` gate; a headless early-out so a
dedicated server never touches the audio pool; warn-once on an unassigned profile; a prebuilt
index with a zero-allocation steady state.

**Not covered, and do not stretch it:** world/atmosphere events with no actor (the bed, wrong
silence, a dome beat — `ActorFx` takes a `PresentationProfile` by construction); replication
(`Fire` is purely local presentation of a cause replicated upstream, and must not grow a wire —
see pattern 3); responders not known at author time, which is the bus argument and has not
happened yet.

**Default recommendation for a new actor-scale cue: append an `ActorEvent` ordinal and add a
`.tres` row. Zero code.** Ordinals are a serialization contract — *"append new events, never
reorder"* — and a property-name typo in a hand-authored `.tres` is dropped **silently** by Godot
on load, which is why `PresentationSelfTest.RunProfileAssets` pins those files value-by-value.
Extend that test in the same commit.

### 2. The event bus is deliberately not built, and the trigger to reverse that is specific

`docs/ATMOSPHERIC-VFX-INTEGRATION.md` §3.4 is titled, verbatim, *"`AtmosphereEventBus` — do not
build it yet."* Its two load-bearing lines:

> :128 — *"It has one listener. `vfx-audio-sync` is the only system that would subscribe today"*
>
> :145 — ***"Build the bus when a second listener actually exists."***

`.claude/skills/README.md:208` records the same (*"deliberately not built"*), as does the
2026-07-25 reconciliation dispatch. The repo has an established YAGNI posture on exactly this
shape (decision D2, 2026-07-21). **A bus with one subscriber is a direct call with ceremony.**

The brief's build order opens with *"set up the `GameEvents` autoload before writing any actual
sound."* That is backwards in the expensive direction: it front-loads an untested indirection
whose first real exercise would be the moment it matters.

1. **The reversing trigger is a genuine second listener** — two independent systems that must
   react to one cause and whose relationship is not already two rows of one `EventResponse`
   table. Audio + particles from one `ActorFx` call is *one* listener wearing two hats; the
   fan-out is already had, with no autoload and no subscription to leak.
2. **When it is built, it is built once.** `vfx-particles` and `vfx-audio-sync` both record it as
   a shared dependency, *"by whichever lands first, not twice."*
3. **What is needed now is narrower and specified** — `vfx-escalation`'s two downstream calls, so
   one tension budget governs all channels:

```csharp
if (_escalation.ShouldFireEvent(baseChance))   // gated by the shared tension budget
{
    _escalation.RegisterEventSpend(cost);      // so the pullback is felt system-wide
    // the firing system then does its own audio/visual work, directly
}
```

Invoked to "set up the event bus," the correct output is this finding and a direct call.

### 3. Same frame locally is not the same instant between players

The claim under scrutiny: *"because both responders subscribe to the same signal, audio and VFX
fire on the same frame the signal is emitted — no manual timing coordination required, and no
risk of drift."*

**Half of that is true and it is the smaller half.** Two C# handlers on one `EmitSignal` do run
synchronously inside that call — on *one client*. Nothing about a local signal bus puts player A
and player B in the same moment, and §4.1's simultaneity condition is entirely about the second
thing.

| Property | A local bus gives you | Needs the wire |
|---|---|---|
| Two systems on this client react to one cause | ✓ | |
| Every player experiences the event at all | | ✓ |
| Every player experiences it at the same moment | | ✓ |
| A mid-round joiner is in the right state | | ✓ |
| The event happened *once*, not once per client | | ✓ |

**The wire is `CycleDriver`'s pattern:**

- **The server owns the decision.** Every `GD.Randf()` roll that gates a perceptible event is the
  server's. A per-client roll is not a timing bug — it is a *different game per player*.
  `vfx-escalation` makes this correction about escalation rolls and `vfx-audio-sync` about
  wrong-silence timers; **this is the general statement of that error**, and it recurs because
  client-side rolls are easier to write and test green.
- **Sim-tick advance, never a wall-clock `Timer`**, which drifts from physics.
- **The house RPC form** (`CycleDriver.cs:154, 163`):

```csharp
[Rpc(MultiplayerApi.RpcMode.Authority,
     TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
     TransferChannel = NetCodec.AtmosphereChannel)]   // next free index is 6 — confirm in NetCodec
private void FireCue(int cueId, Vector3 position, uint seq) => ApplyCue(cueId, position, seq);
```

  The brief's `[Rpc(CallLocal = false)]` is not this repo's form; `Authority` makes
  "server → everyone" a checked property rather than a convention.
- **Transfer mode is per-event.** `CycleDriver` broadcasts phase `Unreliable` because a dropped
  sample is superseded in 0.5 s. **A one-shot cue has no successor** — `Reliable`, and do not
  inherit the 0.5 s cadence either; a discrete cue is one message, not a stream.
- **Latest-wins staleness guard**, including the `!isSync` term (`CycleDriver.cs:194`:
  `if (!isSync && Synced && seq <= _lastAppliedSeq)`). After a reconnect `_lastAppliedSeq` is
  stale history, not an ordering claim, and must not block the reliable targeted sync.
- **Late join is first-class.** `Gameplay.OnPeerConnected` already fires
  `CycleDriver.SendPhaseTo` and `PropManager.SendDumpTo`; persistent audio state joins that
  funnel, carrying *what state* and *how much remains*.
- **Clients apply, never decide.**

**The honest ceiling.** Even a perfectly replicated cue lands at each client's own latency plus
its own audio buffer. §4.6's "same instant" is a *perceptual* claim — the window in which two
humans on proximity voice react as one — not a sample-locked one. Building toward sample-lock is
wasted work; building toward "no client is systematically half a second behind" is not.

### 4. If a singleton is ever built: the two costs the brief treats as footnotes

**Autoload order.** The brief's `public override void _Ready() => Instance = this;` publishes at
`_Ready`, so any subscriber whose `_Ready` runs earlier dereferences null. Two house shapes:

- `VoiceManager.cs:25, 58` — `public static VoiceManager Instance { get; private set; } = null!;`
  published in **`_EnterTree`**, which runs strictly before any `_Ready` in the subtree; the
  non-nullable declaration asserts that ordering holds.
- `CycleDriver.cs:35, 77-82` — nullable `Instance`, published in `_Ready`, cleared in `_ExitTree`
  under `if (Instance == this)`, **plus a separate `Synced` flag** so consumers can distinguish
  "exists" from "has real data." The consumer shape is
  `if (CycleDriver.Instance is not { Synced: true } driver) return;` (`DayNightSky.cs:360`,
  `SessionHud.cs:71`) — *hold rather than guess*. `CycleDriver` names rendering the
  zero-initialised default as the single most likely bug in that feature.

Publish in `_EnterTree`, clear in `_ExitTree` guarded by identity, and give consumers a readiness
flag whenever the object can exist before its data does.

**The `+=` lifecycle is a real leak in Godot C#, and it is the architectural cost of the whole
pattern.** **Verified against Godot 4.7** (the C# Signals documentation):

> *"Normally, when any `GodotObject` is freed (such as any `Node`), Godot automatically
> disconnects all connections associated with that object."*

Automatic disconnection does **not** happen in two cases — *"The signal is connected to a lambda
expression that captures a variable"* and ***"The signal is a custom signal."*** For custom
signals the documented instruction is to *"use `-=` at an appropriate time"*, or to use
`Connect()` instead, which *"does disconnect automatically with custom signals."*

A `[Signal]`-declared bus signal is exactly the case the engine will not clean up. A responder
that subscribes in `_Ready` and is freed mid-session leaves a dangling delegate on an autoload
that outlives the level, and the next emit reaches a disposed object at the moment a cue was
supposed to fire. A node that re-enters the tree double-subscribes and **every cue fires twice** —
a doubled one-shot a few milliseconds apart is a phasing artifact, not an obvious duplicate,
which is the hardest class of audio bug to diagnose.

House precedent runs both ways and the reconciling rule is **symmetric lifetime or symmetric
code**: `Gameplay.cs:240-247` unsubscribes because `Gameplay` dies and `Multiplayer` does not;
`VoiceManager._Ready` never unsubscribes and writes down why (*"Autoload lifetime: subscribe
once, never torn down"*). If a subscriber can die before its emitter, `-=` in `_ExitTree`, and
keep a field reference if the handler is a lambda.

**This is the cost `ActorFx` avoids entirely** — a static call against a data-driven response
table has nothing to leak, double-register, or disconnect. An argument for pattern 1, not merely
a defence of it.

### 5. Frame alignment when the animation is procedural — the latch, not the call-method track

The brief recommends `AnimationPlayer` call-method tracks over `_Process` polling. **There is no
`AnimationPlayer` anywhere in Sail** — zero occurrences across `.cs`, `.tscn` and `.tres`. Every
character animation is procedural. The advice is not wrong in general; it has nothing to attach
to here.

What the repo does instead is better for this case, and the reason generalises: a footfall is
detected as an **integer index change**, not a proximity test against a threshold
(`AvatarVisual.cs:367-377`):

```csharp
_stepPhase += delta * StepHz * Mathf.Tau * (0.4f + 0.6f * speedFrac) * (1f + RunStepFreqBoost * _runBlend);
int plantIndex = (int)(_stepPhase / Mathf.Pi);
if (plantIndex != _lastPlantIndex)
{
    _lastPlantIndex = plantIndex;
    _footPlanted = true;      // latched; consumed once by the SFX layer
}
```

A crossing cannot be missed at any frame rate, because the test is on the *index*: a 200 ms hitch
that skips three plants still changes it, and the flag latches. `SandboxAvatar.PlayStepCosmetics`
consumes it exactly once (`ConsumeFootPlant()` reads and clears), gates on grounded-and-moving,
and fires `ActorEvent.Step` with a running/walking intensity.

**The precise cost, stated rather than hidden:** `PlayStepCosmetics` runs *before* `AnimateVisual`
in the same tick (`SandboxAvatar.cs:576/579`), so the flag consumed was latched on the previous
tick. The footstep trails the visual plant by up to one physics tick (~16 ms at 60 Hz),
**deterministically and never missed** — comfortably inside the audio buffer's own latency
(pattern 6), so reordering the calls buys nothing audible. The method's comment carries the other
half of the contract: *"consumed once per first-time-simulated tick; never on replay."* A
prediction loop re-runs the same tick, and a cue fired from a replayed tick fires twice.

**If a real `AnimationPlayer` ever ships** — an authored death clip, an attic-hatch mechanism —
call-method tracks are correct, with three caveats the brief omits. **Verified against Godot 4.7:**

- `AnimationMixer.callback_mode_method` is *"The call mode used for 'Call Method' tracks."*
  `ANIMATION_CALLBACK_MODE_METHOD_DEFERRED` *"Batch[es] method calls during the animation
  process, then do[es] the calls after events are processed"*; `..._IMMEDIATE` *"Make[s] method
  calls immediately when reached in the animation."* Deferred is safer and lands **after** the
  frame's event processing — still not "on the exact sample."
- `AnimationPlayer.seek(seconds, update, update_only)`: *"Events between the current frame and
  `seconds` are skipped"*, and *"If `update_only` is `true`, the method / audio / animation
  playback tracks will not be processed."* **A seek or scrub silently drops the method track with
  no error.** Anything that rewinds, snaps, or previews loses its sound.
- Method keys at or near the end of a clip have a long history of firing unreliably upstream
  (godotengine/godot#75479 and relatives) — `unverified against Godot 4.7` for this build, but do
  not put a load-bearing cue on a clip's last key regardless.

### 6. "Frame-accurate" is a promise the audio thread does not make

Godot mixes on its own thread, in buffers. **Verified against Godot 4.7** (*Sync the gameplay
with audio and music*): *"Audio is mixed in chunks (not continuously), depending on the size of
audio buffers used"* and *"Mixed chunks of audio are not played immediately."*
`AudioServer.GetOutputLatency()` *"Returns the audio driver's effective output latency. This is
based on `ProjectSettings.audio/driver/output_latency`, but the exact returned value will differ
depending on the operating system and audio driver"* — and *"This can be expensive; it is not
recommended to call `get_output_latency()` every frame."* The setting itself: *"Output latency in
milliseconds for audio. Lower values will result in lower audio latency at the cost of increased
CPU usage. Low values may result in audible cracking on slower hardware."*

Sail's `project.godot` `[audio]` block sets **only** `driver/enable_input=true` and
`driver/mix_rate=48000` — it does not set `output_latency`, so the engine default applies. That
default figure is `unverified against Godot 4.7`; read it off `AudioServer.GetOutputLatency()`
once on a real machine rather than assuming it.

So `Play()` on frame N is heard when the mixer next fills, plus driver latency. **What
frame-accuracy can mean here: the trigger is deterministic, cannot be missed, and cannot
double-fire. What it cannot mean: the waveform starts on a chosen video frame.**

**This reconciles with `vfx-audio-sync` §6 rather than contradicting it.** That skill says to
fire both halves of a synced peak from one call site and gate the audio half on the visual half's
*actual presentation*. This is why that is right: one call site eliminates the only skew you
control — dispatch — leaving the mixer's, which is uniform across both halves and every cue. Two
systems listening to one signal add a second, variable source of skew for no benefit a direct
call does not already give. The brief's one genuinely good tuning claim survives intact: **never
delay the audio to wait for a visual; pre-warm the visual instead.**

`AudioServer.GetTimeToNextMix()` and `GetTimeSinceLastMix()` exist (**verified against Godot
4.7**) for true sample alignment against a music bed. Nothing in Sail needs them — there is no
music and there are no audio files — and reaching for them first is premature.

### 7. Retarget the vocabulary, and fire on the outcome exactly once

The brief's event names are from a superseded design. A signal name is a design commitment that
outlives whoever typed it, so do not transcribe them.

| Brief's event | Status | Ground it in |
|---|---|---|
| `HarvestResult(bool, Vector3)` | **Dead.** `2026-07-25-house-and-hand-ROADMAP.md:245` — *"RESOLVED 2026-07-25 — no crafting in v1 … (harvest = nothing)"* | The *shape* survives: fire on the mechanical outcome. Here that is the tribute accepted or refused at the attic hatch. |
| `ExtractionPhaseChanged(string)` | **Abstract canon only** — `LEVEL-BIBLE.md` §6's Extraction Contract is still doctrine for level design generally. | The current loop's equivalent: the **nightly tribute / bedtime ritual / dome**. |
| `CreatureAttack(Node3D, float)` | Presumes a visible attacking entity. The antagonist is the thing in the attic, with 2–6 kids at a sleepover, and Stage 1 proves dread **without** a creature. | Name the event after what the *simulation* did, not after an assumed cause. |
| `TensionChanged(float)` | The quantity is real; the ownership is not this layer's. | `vfx-escalation`'s session-monotonic derivation. **Never `CycleDriver.Phase`** — cyclic, wraps every 120 s, resets escalation twice a minute. |

Two more corrections to the shape:

- **A `string` phase name on the wire is worse than an ordinal.** `ActorEvent` is the house
  answer: an enum whose ordinals are a serialization contract, appended never reordered, with a
  test pinning the mapping. A stringly-typed phase is one typo from silence.
- **Fire on the outcome, from the authoritative tick, once.** `SandboxAvatar` models it:
  `ActorEvent.Bump` fires on `ev.BumpImpact >= BumpSoundThreshold` behind a cooldown, from the
  simulated tick, with threshold and debounce deliberately left in controller code — *"Event
  GENERATION (thresholds, cooldowns, debounce) stays in controller code; profiles own only the
  response."* That separation keeps "did it happen" testable independently of "what does it
  sound like."

Inherited and easy to forget in a dispatch layer: `LEVEL-BIBLE.md` §8.1 — *"Audio alone is never
sufficient, because the tether severs it on purpose."* An urgency cue wired as an audio-only
dispatch has violated the redundant-channel rule by construction, however well it fires.

## What is provable headless, and what is not

**Unlike most of this family, dispatch wiring is genuinely CI-testable, and it should be tested.**

| Provable in `tests/Run-*.ps1` | Needs a headed session with humans |
|---|---|
| The event fired **once** — not zero, not twice | Whether it sounds aligned with the visual |
| Both responders ran, in authored order | Whether the timing reads as one event or two |
| A replicated cue landed on **every** peer with the right sequence | Whether the moment landed at all |
| A stale or reordered packet did not clobber a newer one | Whether players reacted in the same second |
| A late joiner got the targeted sync and is in the same state | Whether the mix is right |
| A reconnect did not double-subscribe or double-fire | |
| A `.tres` row resolves to the exact authored values | |

`Run-PresentationSelfTest.ps1` is the existing home and already does the last of these — it
drives `FireCore` directly because `Fire` no-ops headless, counts pooled players as **deltas**
rather than assuming zero state, and pins the hand-authored `.tres` files value-by-value. Extend
it rather than starting a parallel suite. Where a decision can be pulled out of a `Node` into a
pure static function, do it: `CyclePhase.FromElapsed` is the house precedent.

State the limit as bluntly as the repo already does — `Run-VoiceTest.ps1:30`:

> *"Audio quality/feel is explicitly not provable here — that is the weekend manual playtest."*

This repo shipped an entire island rotated 90° through a green headless suite. A green dispatch
suite proves the cue fired correctly and nothing about whether it worked.

## Tuning guide

Starting points with reasons attached. None is a gate.

- **One physics tick of latch latency (~16 ms at 60 Hz)** is the accepted cost of the foot-plant
  path. It sits below the mixer's own buffer, so reordering `PlayStepCosmetics` and
  `AnimateVisual` buys nothing audible and risks the consume-once contract. Measure first.
- **Pre-warm the visual; never delay the audio.**
- **Transfer channel 6** for a new audio/atmosphere lane, beside voice/move/prop/cycle so a burst
  of cues never queues behind movement. Confirm against `NetCodec` at build time.
- **Reliable for discrete cues, unreliable only for continuously-resampled quantities.** The test
  is "does a later message supersede this one?" If no, reliable.
- **Keep the event vocabulary small and append-only.** `ActorEvent` covers the entire game in 10
  ordinals. A vocabulary growing faster than the design is one nobody maps.
- **If dispatch code ever enforces a spike budget**, §4.4's ceiling is roughly one true spike per
  session and the comment carries `[research default — pending Talon confirmation]` **verbatim**,
  so a default nobody chose cannot harden into a decision nobody remembers making.

## Integration points

- **`ActorFx` / `EventResponse` / `PresentationProfile`** — first stop for any actor-scale cue.
- **`SfxLab.PlayStream3D`** — the one compliant positional one-shot path, and the ≤24
  concurrent-3D-player budget every new emitter counts *inside*. Owned by `sound-optimization`.
- **`CycleDriver`** — the replication contract to copy and the `Synced` gate to respect.
  **`Gameplay.OnPeerConnected`** is the late-join funnel; **`NetCodec`** holds the channel constants.
- **`vfx-escalation`** — `ShouldFireEvent` / `RegisterEventSpend`, the narrow substitute for a
  bus, and the owner of any session-monotonic quantity. **`vfx-audio-sync`** owns the mode and
  the ratio; this skill fires what it authorises and reports what actually fired.
- **`scripts/telemetry/`** — a live Firebase path with **zero audio hooks today**. The dispatch
  layer is the natural place to count fires, which makes the ratio `vfx-audio-sync` wants
  measured measurable without new infrastructure.
- **`scripts/voice/`** — out of bounds. World-audio-versus-voice interaction is a mix question
  and lives on the world side.

## Precedent

In-repo first, because that is the precedent that binds:

- **`ActorFx.cs` + `EventResponse.cs`** — data-driven audio+VFX co-dispatch from one `[Export]`ed
  resource, with the coupling it removes written into its class doc. The closest thing Sail has
  to the brief's bus, and it needs no autoload, no singleton and no subscription.
- **`CycleDriver.cs`** — server-owns-the-input, sim-tick advance, authority-mode RPC, staleness
  guard with its `!isSync` exception, targeted late-join sync, `Synced` gate.
- **`AvatarVisual.cs`** — the crossing-index latch: aligning a sound to a procedural animation
  beat with no animation clock to hang a callback on.
- **`Gameplay.cs:240-247`** and **`VoiceManager._Ready`** — the two halves of the
  subscription-lifetime rule, one unsubscribing and one documenting why it never has to.
- **The UI classes** — the house `[Signal]` + `EmitSignal(SignalName.X, …)` idiom, and evidence
  that Sail's custom signals live in UI, not in gameplay.

External, second: **Unity ScriptableObject event channels** and **Unreal Gameplay Tags +
delegates**. The brief is right that both converge on "central channel, independent subscribers";
the honest reading is that they converge on it *at a scale Sail is nowhere near*, and that both
ecosystems spend most of their own guidance on subscription lifetime — the cost the brief files
under troubleshooting and this file treats as the architecture.

## Troubleshooting

Roughly the order these actually occur.

- **The sound plays twice** — a duplicate `+=` from a re-entered node with no matching `-=`, or a
  cue fired from a replayed prediction tick rather than a first-time-simulated one.
- **Nothing plays and there is no error** — the ordinal is unmapped in the `.tres` (silence is
  the designed default), a property-name typo was dropped silently on load, or the profile is
  null and the warn-once already fired earlier in the session.
- **Nothing plays on the dedicated server** — by design; `Fire` early-outs when
  `NetworkManager.Instance.IsHeadless`, which is also why headless tests drive `FireCore`.
- **`NullReferenceException` on a singleton during startup** — autoload order. Publish in
  `_EnterTree`, and give consumers a readiness flag rather than a bare null check.
- **`System.ObjectDisposedException` when a cue fires** — a freed subscriber still connected to a
  custom signal or a capturing lambda; the two cases Godot's C# docs name as not auto-disconnected.
- **Some players hear it and others do not** — the roll or the timer is client-side.
- **Everyone hears it but visibly not together** — the cue was re-derived locally per client from
  a shared input instead of replicated as a cause. **A mid-round joiner in the wrong state** is
  the same bug's other face: no targeted late-join sync.
- **The cue lands noticeably after its visual** — look for an `await` or `CallDeferred` between
  the decision and `Play()` first, then read `AudioServer.GetOutputLatency()` once. Never fix it
  by delaying the visual.
- **A method track sometimes does not fire** — something seeked. Seeking skips events between
  frames and `update_only` bypasses method tracks entirely.
- **Green headless suite, audibly wrong game** — expected. Headless proves the wire, never the
  sound.

## Caveats

- **No numbers as law.** One tick of latch latency, channel 6, the 0.5 s cadence not to inherit —
  every figure is a starting point with its reasoning attached, and the reasoning is what survives
  a change of value.
- **Unverified is stated, not implied.** The C# signal-disconnection semantics,
  `AnimationMixer.callback_mode_method`, `AnimationPlayer.seek`, `AudioServer.GetOutputLatency` /
  `GetTimeToNextMix` / `GetTimeSinceLastMix`, and the audio-mixing-in-chunks explanation were
  checked against Godot 4.7 docs. The engine's default `output_latency` value and the end-of-clip
  method-key reliability issue are `unverified against Godot 4.7`. Brief code is not evidence.
- **`GpuParticles3D.AmountRatio` is an art knob, not a perf knob.** The brief's
  coordinated-intensity sample lerps it as if it were the latter. `vfx-particles` verified this
  against the doc line and this skill does not relitigate it: a tension responder may drive
  `AmountRatio` for look, never for budget, and the escalation quantity it reads is
  `vfx-escalation`'s to derive.
- **Not the voice pipeline.** `scripts/voice/` is shipped and tuned. Never fork `VoiceConfig`,
  never add DSP to the `Voice` bus.
- **Not a resolver.** §9's tone ratio stays open behind its ripeness trigger; §6.2's night-floor
  conflict is a shipped decision in live opposition to doctrine; §4.4's ceiling keeps its
  `[research default — pending Talon confirmation]` marker; `LEVEL-BIBLE.md` §8.3 is
  `[BLANK — Talon]` and bears directly on whether `ActorEvent.Bark` may ever be mapped casually.
- **Not an argument to build a bus.** Making dispatch tidy is not evidence that indirection is
  needed. The trigger is a genuine second listener and it has not arrived.
- **Correct wiring is necessary and nowhere near sufficient.** A cue that fires once, on every
  peer, in the same instant, on the exact animation beat can still be the wrong sound at the wrong
  moment. That judgement is `vfx-audio-sync`'s and `/direct`'s, and this skill has no opinion on it.

*Scope note: written 2026-07-28 against `feat/neighbourhood-exterior` @ `0e715f3`, where
`ActorFx`/`EventResponse`/`PresentationProfile` ship and work for actor events, there are zero
audio asset files, no `AnimationPlayer` anywhere in the tree, no event bus (a written decision,
not an omission), nothing atmosphere-shaped subscribing to anything, and no test asserting
anything about an `AudioStream` or an `AudioServer` bus. **Current bite: genuinely non-zero — an
actor-scale cue can be added today as one appended `ActorEvent` ordinal plus a `.tres` row plus a
`PresentationSelfTest` assertion, with no new code.** First real test: the first cue that must
land on every client in the same instant — a nightly-tribute or dome beat — authored server-side,
replicated the `CycleDriver` way on its own channel, with a headless case asserting one server
decision, N client applications in sequence, a mid-round joiner synced, and no double fire across
a reconnect. Passing looks like a recorded 2–6 player session in which two people react in the
same second, the log shows exactly one emission, the late joiner is in the same state as everyone
else, and nobody can find the second copy of the sound.*
