---
name: sound-spatial-audio
description: Use when a Sail world sound must be placed in space — choosing between AudioStreamPlayer, 2D and 3D, distance falloff and audible range, air absorption, room reverb, which listener hears the mix, or diagnosing unlocatable sounds. How far a sound carries, not how many may carry — that is sound-optimization.
---

# sound-spatial-audio

## Overview

The foundational layer of the `sound-*` family: **where a sound is, how far it carries, what
the room does to it, and whether a player can point at it.** Everything else in the family
assumes this works — if players cannot localise by ear, audio stops being an information
channel and becomes decoration.

Sail's setting makes it unusually worth doing well. A 90s suburban house is a far more
**reverb-legible** space than an open forest: bedroom, hallway, stairwell, garage, crawlspace
are acoustically distinct in a way that "clearing" and "denser clearing" are not, and the walls
that make them distinct are already in the level geometry. Interior architecture gives spatial
audio real work to do — an advantage to spend, not a constraint to route around.

## Infrastructure, not direction

Substrate, in exactly the sense `vfx-particles` is substrate for the `vfx-*` family. No affect
voice, and it should not grow one. **What should be audible and why there** → `/direct` and
`docs/THRILL-BIBLE.md`; **reinforce / withhold / decouple and the sync ratio** →
`vfx-audio-sync`; **how near and which way a hidden threat is** → `vfx-proximity`, which owns a
perceptual contract this skill can break by accident (pattern 7); **how it is built,
positioned, filtered and reverbed** → here.

The one doctrinal claim worth making, and no more: a sound the player cannot place cannot carry
information — and `LEVEL-BIBLE.md` §8.1 already rules that **audio alone is never a sufficient
cue**, because the tether severs it on purpose. Spatial audio's job is to be a strong
*supporting* channel. A proposal arguing that a cue can be audio-only because the localisation
is good has left this skill and contradicted a bible.

## When to Use

- A world sound needs a node type chosen and its falloff and range set
- Players cannot tell where a sound came from, or misjudge how far away it is
- A room, floor or exterior needs an acoustic identity distinct from its neighbours
- An `AudioListener3D` question arises — spectating, a death cam, a camera that is not the ear
- Distance-based muffling (air absorption, a wall, a closed door) is wanted
- A directional cue reads as centred, or front and back are being confused
- Someone is about to hand-roll distance math in C# instead of using the engine's

**Not for:** whether a sound should exist or what it makes players feel (`/direct`); the
reinforce/withhold/decouple call and the sync ratio (`vfx-audio-sync`); sensing a hidden
threat's bearing, which carries its own rule that a bearing may be deliberately stale and must
never resolve into a clean readout (`vfx-proximity` — read it and defer); the proximity voice
pipeline, whose attenuation constants are marked *"Do not fork these"* (`scripts/voice/`);
emitter budget and pooling (`sound-optimization`, and `SfxLab`'s existing pool doctrine); where
a space belongs in a level (`/spec-level`).

## What exists in the repo today

| Thing | State |
|---|---|
| `scripts/voice/` proximity voice | **The only tuned 3D audio that ships.** `AudioStreamPlayer3D`, `InverseDistance`, `UnitSize = 6.0f`, `MaxDistance = 24.0f`, bus `Voice`, plus a `Pa` route that disables attenuation outright. Out of bounds to modify. |
| `SfxLab.PlayStream3D` | The only other positional path. Pooled, bus `Sfx`, `maxDistance = 40f` default, per-shot pitch jitter. |
| `AudioListener3D` | **Nowhere in the tree.** The ear is whatever `Camera3D` is current; nothing manages it. |
| Reverb buses / zones | **Do not exist.** The only `AudioEffectReverb` in the repo is on the `PA` bus in `VoiceManager.EnsurePaBusName()`. |
| Air absorption | **Unconfigured everywhere.** Every emitter runs engine defaults. |
| Buses | `Master`, `Sfx`, `Voice`, `VoiceCapture`, `PA` — all created in code, no `default_bus_layout.tres`. |
| Audio asset files | **Zero.** Every sound is synthesised at runtime. |
| Tests asserting an `AudioStream` or a bus | **None.** `SfxLab` has zero coverage. |

Two facts that shape everything below.
`docs/superpowers/specs/2026-07-28-neighbourhood-ring-design.md` names *"the forest, the school
building, lighting, shaders, **audio**"* as **not in scope** for the ring work in flight — so
nothing here is owed to the ring today. And `AudioStreamPlayer3D.AreaMask` **defaults to `0`**
(**Verified 2026-07-29 against the installed Godot v4.7-stable.mono**, headless: instantiating
`AudioStreamPlayer3D` and `AudioStreamPlayer2D` in a `SceneTree._initialize()` script,
`--headless --script`, printed `area_mask = 0` for both, and `ClassDB.class_get_property_list`
confirms the same property; the C# `AreaMask` binding reads the identical engine default), so
the engine's entire built-in area-reverb path is switched off on every emitter Sail creates.
Nobody turned it off; it was never on.

## The scale reconciliation — read before setting any number

The brief proposes `UnitSize = 1f` / `MaxDistance = 60f`. The repo's tuned values are
`UnitSize = 6.0f` / `MaxDistance = 24.0f` for voice and `maxDistance = 40f` for SFX. These are
not the same scale, and taking the brief's numbers would quietly shrink the audible world.

**The knobs do different jobs. Verified against Godot 4.7:**

- `UnitSize` — *"The factor for the attenuation effect. Higher values make the sound audible
  over a larger distance."* The falloff shape. Default `10.0`.
- `MaxDistance` — *"The distance past which the sound can no longer be heard at all. Only has
  an effect if set to a value greater than `0.0`... unlike `UnitSize` whose behavior depends on
  the `AttenuationModel`, `MaxDistance` always works in a linear fashion. This can be used to
  prevent the `AudioStreamPlayer3D` from requiring audio mixing when the listener is far away,
  which saves CPU resources."* Default `0.0` — **no cutoff at all by default.**
  `vfx-audio-sync` verified this same pair against 4.7; restated here because it is what the
  brief gets wrong.

`MaxDistance` is a hard wall; `UnitSize` is the curve leading to it. `UnitSize = 1` against the
repo's `6.0` is a **sixfold steeper falloff** — under `InverseDistance` gain scales roughly as
`UnitSize / distance`, so a source at 6 m sits about 15 dB quieter. Pairing `UnitSize = 1` with
`MaxDistance = 60` yields an emitter effectively inaudible for the last fifty of those sixty
metres: **not "a longer range", a near-field sound with a pointless wide cutoff.**

**Sail treats one world unit as roughly one metre by convention, not by engine guarantee.**
Godot's 3D audio is unitless. `VoiceConfig` documents its `24.0` as *"gone past ~24 m"* against
a 5 m/s walk speed, and `VoiceRange.MaxDistanceMeters` names metres in the field itself. That
convention is the only thing making these numbers comparable.

| Emitter class | UnitSize | MaxDistance | Reason |
|---|---|---|---|
| Room-scale prop (a door, a toy) | ~3–6 | ~15–20 | Must not carry through the house. Inside voice range so a teammate can be told. |
| Player SFX (steps, bumps) | pool default | 40 (`SfxLab`) | Already shipped, already tuned by ear. Do not fork it to match a table. |
| Proximity voice | **6.0 — fixed** | **24.0 — fixed** | `scripts/voice/`. Not yours. |
| House-wide / exterior event | ~10–20 | ~60–80 | `VoiceRange.Megaphone` is `(20f, 80f)` — the only shipped long-carry precedent. Copy its shape. |

**Anchor to the 24 m voice range deliberately, in both directions.** A sound audible past 24 m
is one players hear with nobody in range to ask about it; a sound audible only inside 6 m is
one only its finder ever knows about. Which a cue wants is a `/direct` call under
`THRILL-BIBLE.md` §5.4 (information asymmetry), not a value to pick here. State which side a
number lands on.

## Core patterns

### 1. Node choice

- **`AudioStreamPlayer`** — non-positional: UI, menus, music, global stingers. `SfxLab.PlayUi`
  is the shipped example, outside the 3D pool entirely.
- **`AudioStreamPlayer2D`** — has no role in Sail. The game is 3D throughout.
- **`AudioStreamPlayer3D`** — everything else, always via `SfxLab.PlayStream3D`. Spawn-per-sound
  is named as an anti-pattern in `docs/ATMOSPHERIC-VFX-INTEGRATION.md`; `sound-optimization`
  owns why.

Not a stylistic choice. **A non-positional player is heard identically by every listener, which
flattens exactly the per-player asymmetry `vfx-proximity` exists to create.** Reach for it when
the sound is genuinely not in the world, and be able to say why.

### 2. Attenuation model — and the name that will not compile

**The brief's `AttenuationModelEnum.InverseDistanceSquared` does not exist.**
**Verified against Godot 4.7**, `AudioStreamPlayer3D.AttenuationModelEnum` is:

| Member | Doc line |
|---|---|
| `InverseDistance` | *"Attenuation of loudness according to linear distance."* — the default (`attenuation_model` default `0`) |
| `InverseSquareDistance` | *"Attenuation of loudness according to squared distance."* |
| `Logarithmic` | *"Attenuation of loudness according to logarithmic distance."* |
| `Disabled` | *"No attenuation of loudness according to distance. The sound will still be heard positionally, unlike an `AudioStreamPlayer`."* |

The correct name is `InverseSquareDistance`. A brief shipping `InverseDistanceSquared` was
never compiled.

Its *reasoning* holds — inverse-square reads steeper and more realistic, and is the standard
physical model. But **the repo chose `InverseDistance` for voice** (`VoiceSpeaker.SetRoute`)
for a different and equally good reason: a gentler curve keeps a teammate intelligible across
a room instead of dropping them off a cliff at 8 m. Speech and world sound want different
curves. Do not "standardise" them; note that they differ and why.

`Disabled` with a positive `MaxDistance` gives *"linear attenuation clamped to a sphere of a
defined size"* (**verified**) — the shape a house-wide announcement wants, and what
`VoiceSpeaker`'s `Pa` route uses with `MaxDistance = 0` for no cutoff at all.

### 3. Air absorption — the engine already does it, and the brief's version is a no-op

**Verified against Godot 4.7:** the class doc says *"For greater realism, a low-pass filter is
applied to distant sounds. This can be disabled by setting `attenuation_filter_cutoff_hz` to
`20500`."* `AttenuationFilterCutoffHz` defaults to **`5000.0`**; `AttenuationFilterDb`
(*"Amount how much the filter affects the loudness, in decibels"*) defaults to **`-24.0`**.

Two corrections. **The brief's "core setup" sets exactly those two defaults** — that block is a
restatement dressed as a tuned decision. And **`DynamicAirAbsorption` lerps the cutoff to
`20000f` at full clarity, but the documented off switch is `20500`** — at `20000` the filter is
still engaged, so the clear end of the ramp is quietly not clear. Off by 500 where nobody looks.

**The per-frame cost is the bigger problem.** `DynamicAirAbsorption._Process` writes two
properties on every emitter every frame, each crossing the C#↔engine marshalling boundary. At
one emitter in an editor run this is invisible. At Sail's documented ceiling — *"≤24 concurrent
3D players INCLUDING the 5 voice speakers and ambience"* (`SfxLab.cs:42`) — it is 48 interop
writes per frame, ~2,880/s at 60 fps, on the CPU already decoding up to five Opus streams.
**Per-frame property writes to audio nodes are the CPU pattern that shows up at player count
and never in a single-player editor run.** That is the general rule; this is one instance.

Cheaper shapes, in order:

1. **Use the built-in filter.** It already does distance-based rolloff, in engine, with zero
   C#. Tune the two properties once per emitter class and stop. Right for almost everything.
2. **Write only on change.** Recompute on a slow cadence or on a distance-bucket crossing, and
   early-out inside an epsilon. `vfx-proximity` pattern 1 states the same discipline for shader
   parameters: distance checks are free, per-frame writes are not.
3. **Skip emitters that cannot be heard.** Past `MaxDistance` nothing is mixed; a `_Process`
   still running there is pure waste.

If a hand-rolled ramp survives that, the brief's arithmetic is fine —
`t = clamp((dist - full) / (muffled - full), 0, 1)`, then lerp. Note `FullClarityDistance = 5f`
/ `MuffledDistance = 50f` are outdoor-scale; inside a house 50 m is past every wall.

### 4. Reverb zoning — two mechanisms, and the brief hand-rolls the wrong one

Two distinct phenomena, and conflating them is where zone systems go wrong:

- **Source-room character** — *the sound was made in the stairwell.* A function of emitter position.
- **Listener-room character** — *I am in a crawlspace.* A function of ear position.

**Godot already implements the first, natively, and Sail has it switched off.** **Verified
against Godot 4.7**, `Area3D` carries `AudioBusOverride` (*"If `true`, the area's audio bus
overrides the default audio bus"*), `AudioBusName`, `ReverbBusEnabled` (*"the area applies
reverb to its associated audio"*), `ReverbBusName`, `ReverbBusAmount` and `ReverbBusUniformity`
(both *"Ranges from `0` to `1` with `0.1` precision"*, both default `0.0`), and
`AudioStreamPlayer3D.AreaMask` selects *"which `Area3D` layers affect the sound for reverb and
audio bus effects."* `ReverbBusAmount` is a **send amount, not a bus mute** — precisely the
parallel send the brief's design cannot express. **Try this path first: zero C#, and it cannot
stack effects.** Two things to settle, both cheap: `AreaMask` is `0` on every Sail emitter so
nothing routes until it is set (**verified**); and whether the area lookup uses the *emitter's*
or the *listener's* position is **unverified against Godot 4.7** — the property names imply
emitter, which would make it source-room-only. Test with one area and one emitter, record the
answer, and do not design on a guess.

**The brief's `EnvironmentReverbZones` has a silent cumulative bug and must not be built as
written.** Every instance constructs `new AudioEffectReverb { ... }` in `_Ready` and calls
`AudioServer.AddBusEffect` on a **shared, named** bus. Four canopy zones in a level means four
chained reverbs. Nothing errors. The mix gets wetter and more expensive as the level grows, the
symptom scales with level size rather than with any one zone, and whoever notices will be
debugging the wrong file. Two further defects in the same sample:

- **`Dry = 1f - WetLevel * 0.5f`** silently attenuates the direct signal 17.5 % at
  `WetLevel = 0.35`. `Dry` is *"the volume ratio of the original audio"*, range `0`–`1`
  (**verified**). On a bus carrying the whole world, keep `Dry = 1` unless ducking is the point
  — otherwise it is a mix change wearing a reverb setting's clothes.
- **Hard-switching `SetBusVolumeDb` between `0f` and `-80f`** on a boundary. See pattern 5.

**The correct shape is already in the repo.** `SfxLab.EnsureBus()` is idempotent by
construction (`if (AudioServer.GetBusIndex(Bus) >= 0) return;`), and
`VoiceManager.EnsurePaBusName()` is the shipped precedent for a bus whose effect chain —
distortion, lowpass, `AudioEffectReverb { RoomSize = 0.7, Damping = 0.4, Wet = 0.25, Dry = 0.9 }`
— is **constructed once, in one place, by the system that owns the bus.** Zones may only
*select* or *crossfade between* pre-built configurations. A zone that constructs an effect has
taken ownership of a global.

**Pre-author presets as reusable scenes.** The brief is right about that and wrong about the
vocabulary — for Sail the set is a house, not a forest: **bedroom** (small, heavily damped;
soft furnishings), **hallway** (small, brighter, narrow; most transitions cross it),
**stairwell** (tall, long bright tail — the most distinctive interior in a suburban house, and
vertical), **garage** (large, hard, boomy; reads as *not in the house any more*),
**crawlspace/attic** (tight, very damped, close — barely reverb at all, which is the point),
**open street** (near-dry, faint slap; sound leaves and does not return), **community green**
(dry, slightly wider — distinguishable from the street mainly by *less*). Three to four
distinct bus configurations reused across a level is the right order of magnitude; reverb is
moderately expensive and those seven collapse onto far fewer settings than they look like.
Unprofiled on the GTX 970 floor.

### 5. Crossfading, and the doorway

**A bus flipped between `0 dB` and `-80 dB` is audible as a snap.** A reverb tail is a decaying
signal; muting the bus does not let it decay, it truncates it mid-tail, and the ear reads a
truncated tail as a glitch rather than a room change. Coming the other way, a tail arriving at
full level with no source is equally wrong.

What a crossfade actually requires:

- **A slew, not a jump** — and a frame-rate-independent one (`1 - exp(-delta / tau)`), so the
  fade is the same on a 144 Hz machine and a 60 Hz one.
- **The tail must survive the transition.** Keep one effect instance alive and move its
  parameters, or run a real parallel send (`Area3D.ReverbBusAmount`). Never mute the bus the
  tail lives on.
- **Asymmetric timing is defensible** — entering a small space quicker than leaving it is
  closer to how the ear works than a symmetric fade.

**The doorway is a real bug, not an edge case.** A player standing on a boundary retriggers
`BodyEntered` / `BodyExited` as their collider jitters across it, and standing in a doorway is
a *common* state in a house. A design where each signal directly sets a bus parameter
oscillates audibly. **Ref-count occupancy instead**: zones push and pop themselves, the router
reads the innermost occupied room. Enter-before-exit then produces no discontinuity, and
overlapping zones (a hallway overlapping a stairwell) resolve by depth rather than signal order.

### 6. The listener, and who counts as "the player"

**`AudioListener3D` exists in Godot 4.7** (**verified**): *"Once added to the scene tree and
enabled using `MakeCurrent()`, this node will override the location sounds are heard from."*
The `AudioStreamPlayer3D` class doc: *"By default, audio is heard from the camera position."*
And the trap: *"There may be more than one `AudioListener3D` marked as 'current' in the scene
tree, but only the one that was made current last will be used."* `ClearCurrent()` hands
control back to the camera.

Sail has none, so the ear is the current `Camera3D`. Correct for first-person, wrong the moment
the camera detaches — a death cam, a spectator view. Sail has no spectator audio route at all
(`docs/ATMOSPHERIC-VFX-INTEGRATION.md:117`); that is a dependency to report, not to build
sideways.

**The multiplayer bug in the brief, plainly.** `OnBodyEntered` firing on
`body.IsInGroup("player")` is wrong in a 2–6 player session because that group contains
**remote** players. `AudioServer` is per-process — there is one mix on this machine. A teammate
walking into the garage would change *this* client's reverb, and two players in different rooms
would fight over the bus. The check must be **"is this the locally-controlled player"** (the
peer whose authority matches `Multiplayer.GetUniqueId()`), not "is this a player". This is the
class of bug the repo keeps shipping: built exactly as literally described, multiplayer
implication left unhandled because nobody wrote it down.

**And the group name itself fails silently.** Godot `.tscn` group wiring gives no error for a
misspelled group: `"players"` instead of `"player"` in a `[node]` header means the zone never
fires, and a zone that does nothing looks identical to a zone that was never wired. Prefer a
typed check (`body is AvatarController`, or an interface) where a string is the only thing
between working and inert; if a group is used, assert its membership once at startup.

**The listener-side router, done correctly:**

```csharp
/// <summary>
/// Listener-side room character. ONE AudioEffectReverb on the world bus, created once
/// (SfxLab.EnsureBus's idempotent shape). Zones never construct effects and never touch bus
/// volume — they push and pop a preset and the router slews, so the tail always survives the
/// transition. Occupancy is a stack: a doorway re-entry is a no-op, overlaps resolve
/// innermost-wins.
/// </summary>
public sealed partial class ReverbRouter : Node
{
    public readonly record struct Room(string Name, float RoomSize, float Damping, float Wet);

    public static readonly Room Outside = new("outside", 0.10f, 0.90f, 0.03f);

    private const float SlewSec = 0.35f;   // not a step, not a lag. Headed playtest call.
    private const float Settled = 0.002f;  // below this the write is inaudible — skip it

    private static ReverbRouter? _instance;
    private readonly System.Collections.Generic.List<Room> _occupied = new();
    private AudioEffectReverb? _verb;
    private Room _current = Outside;

    public override void _Ready()
    {
        _instance = this;
        int bus = AudioServer.GetBusIndex(SfxLab.Bus);
        if (bus < 0)
            return; // SfxLab creates its bus lazily; no bus yet means no world audio yet

        // Idempotent: adopt an existing reverb rather than chaining a second onto the bus.
        for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
        {
            if (AudioServer.GetBusEffect(bus, i) is AudioEffectReverb existing)
            {
                _verb = existing;
                return;
            }
        }
        _verb = new AudioEffectReverb
        {
            RoomSize = _current.RoomSize, Damping = _current.Damping, Wet = _current.Wet,
            Dry = 1f, // the bus carries the direct signal; ducking it here is a mix change
        };
        AudioServer.AddBusEffect(bus, _verb);
    }

    /// <summary>Call ONLY for the locally-controlled player. AudioServer is per-process, so
    /// routing a remote player's crossing through here changes this client's mix because a
    /// teammate walked into the garage.</summary>
    public static void Enter(Room room) => _instance?._occupied.Add(room);
    public static void Exit(Room room) => _instance?._occupied.Remove(room);

    public override void _Process(double delta)
    {
        if (_verb == null)
            return;
        Room want = _occupied.Count > 0 ? _occupied[^1] : Outside;
        float k = 1f - Mathf.Exp(-(float)delta / SlewSec); // frame-rate independent approach
        var next = new Room(want.Name,
            Mathf.Lerp(_current.RoomSize, want.RoomSize, k),
            Mathf.Lerp(_current.Damping, want.Damping, k),
            Mathf.Lerp(_current.Wet, want.Wet, k));

        if (Mathf.Abs(next.RoomSize - _current.RoomSize) < Settled
            && Mathf.Abs(next.Damping - _current.Damping) < Settled
            && Mathf.Abs(next.Wet - _current.Wet) < Settled)
            return; // settled: stop writing to the audio server every frame

        _current = next;
        (_verb.RoomSize, _verb.Damping, _verb.Wet) = (next.RoomSize, next.Damping, next.Wet);
    }
}
```

**Test one thing before trusting it:** whether mutating a live `AudioEffectReverb`'s properties
is picked up by the running effect without re-adding it to the bus is **unverified against
Godot 4.7**. `AudioServer.GetBusEffectInstance` returns the `AudioEffectInstance` separately
from the `AudioEffect` resource — exactly the seam where a resource edit might not propagate.
If it does not, the fallback is `Area3D.ReverbBusAmount`: a real parallel send, engine-tuned.

### 7. The rear cue is a telegraph, and the bearing is not yours

`AudioStreamPlayer3D` places sound relative to the active listener, and stereo panning genuinely
cannot disambiguate directly-behind from directly-in-front — interaural cues are identical on
the cone of confusion. That part of the brief is correct.

**The proposed fix is not.** `RearThreatCue` fires a non-positional stinger whenever
`playerForward · threatDir < -0.5`. A cue that *always* fires when something is behind you is a
perfectly reliable telegraph, and `THRILL-BIBLE.md` §8.3 is explicit: *"If a cue always precedes
a threat, players learn to relax in its absence — the cue has taught them when they are safe.
Break the correlation with false alarms and unsignalled events."* A reliable rear cue hands
players a clean **all-clear** for the entire time it is silent, which is worth more to them than
the warning is worth to you.

**And a clean binary "it is behind you" readout is exactly what `vfx-proximity` withholds.**
That skill owns the contract that a bearing may be deliberately stale, that the stale rate is a
§8.3 control surface belonging to `/direct`, and that a cue resolving into a location has become
a threat indicator — a mechanic, not dread. **Defer.** Whether a rear cue exists is `/direct`'s
call and how honest it is is `vfx-proximity`'s; this skill builds only the panning and filtering
underneath.

What *is* this skill's to offer, none of it a stinger: **`AttenuationFilterCutoffHz` as a
rear-biased filter** — real pinnae shadow highs from behind, and a physical cue degrades
gracefully where a discrete one fires; **head movement**, which resolves the cone of confusion
for free in first person, so if players are not turning, look at camera or input; and
**`PanningStrength`** — **verified present**, *"scales the panning strength for this node by
multiplying the base `ProjectSettings.audio/general/3d_panning_strength`"*, with a product of
`0.0` meaning *"stereo panning is disabled and the volume is the same for all channels."* Check
that before concluding panning is broken.

**HRTF.** Godot 4.7 ships **no** HRTF or binaural convolution — `hrtf` and `binaural` return
zero hits across the whole GodotSharp 4.7.0 API surface (**verified**). Panning law plus
attenuation, filtering and reverb captures most of the perceptual benefit on headphones; true
binaural needs a third-party DSP addon. **Stretch goal, not v1**, and not something to plan a
device around.

### 8. Doppler and polyphony

**Verified against Godot 4.7:** `DopplerTrackingEnum` is exactly `Disabled` (default),
`IdleStep` (*"during process frames"*) and `PhysicsStep` (*"during physics frames"*) — the
brief's `PhysicsStep` is right. Easy to miss: *"If `DopplerTracking` is not `Disabled` but the
current `Camera3D`/`AudioListener3D` has doppler tracking disabled, the Doppler effect will be
heard but will not take the movement of the current listener into account."* **Both ends must be
enabled**, and Sail has enabled neither. Turn it on only where relative speed is real — a thrown
object, something passing fast — never on a static prop or an ambient loop.

`MaxPolyphony` is **verified**: *"the maximum number of sounds this node can play at the same
time. Playing additional sounds after this value is reached will cut off the oldest sounds"*,
**default `1`**. Raising it on a pooled node interacts with `SfxLab.Rent`'s
prefer-a-non-`Playing`-slot logic — `sound-optimization`'s territory.

## Tuning guide

Starting points with reasons. None is a gate.

- **`MaxDistance` is CPU, not just design** — its own doc line names preventing mixing when the
  listener is far away. Leaving it at `0.0` means every emitter mixes forever.
- **Slew ~0.3–0.4 s on a room change** — fast enough to feel like a threshold, slow enough not
  to click. Asymmetric in/out is defensible.
- **Three to four reverb configurations per level** — reverb is moderately expensive and the
  seven presets collapse onto fewer distinct settings than they look. Unprofiled.
- **Wet ~0.2–0.35 for a real interior, well under 0.1 outdoors.** The brief's `0.35` for "dense
  canopy" is an interior number on an exterior; a forest is not a room.
- **Rear filtering over rear signalling** — a physical cue that degrades beats a discrete cue
  that fires.
- **Anchor ranges to 24 m** — not because 24 is special, but because it is the distance at which
  two players can talk about what they heard.

## Integration points

- **`scripts/voice/`** — `ProximityUnitSize` / `ProximityMaxDistance` are marked *"Do not fork
  these."* A reference frame, never a thing to write. World-audio-vs-voice interaction is a
  **mix** question and lives on the world side, per `vfx-audio-sync`. `VoiceSpeaker`'s `Pa`
  route (`AttenuationModel = Disabled`, `MaxDistance = 0`) is the shipped example of
  deliberately un-spatialising a sound.
- **`SfxLab`** — the one compliant positional path, the `Sfx` bus, and the `EnsureBus`
  idempotence every new bus must copy. `sound-optimization` owns its pool.
- **`SettingsPanel`** — the only mixer UI: Master and Voice sliders, **no Sfx slider**. Any new
  bus a player should be able to turn down needs one added there.
- **`vfx-proximity`** — owns hidden-threat bearing and distance. This skill supplies the
  spatialisation it rides on and never the readout.
- **`vfx-audio-sync`** — owns the ambient bed (which does not exist), wrong silence, and the
  rule that an urgency cue and a dread cue may never share a bus, sound family, frequency band
  **or spatial signature**. That last clause is this skill's to honour: two cues sharing a
  spatial signature have been merged whatever their buses say.
- **`sound-optimization`** — emitter count, pooling, the ≤24 concurrent-3D-player budget. Every
  reverb bus and extra emitter proposed here is spent against it.

## Precedent

- **`scripts/voice/`** — proof Sail already does positional 3D audio properly, and the only
  values in the repo tuned by ear against a real walk speed. `VoiceManager.EnsurePaBusName()` is
  the house precedent for a bus whose effect chain is built once by its owner.
- **`SfxLab.EnsureBus()`** — the idempotent bus-creation shape the reverb work must copy.
- **`CyclePhase.FromElapsed` + `CycleSelfTest`** — the house precedent for pulling a decision out
  of a `Node` into a pure static function *purely so it can be tested headless*.
- **Hunt: Showdown** — the reference for spatial audio as primary threat detection in
  multiplayer: long sightless engagements resolved by ear. What does not transfer is the scale —
  Hunt is a large outdoor map with rifle reports carrying hundreds of metres; Sail is a house at
  ~24 m social range. The principle transfers; none of the distances do.

## What is actually testable

**Genuinely CI-provable headless, today** — all of it pure functions in the
`CyclePhase.FromElapsed` shape (a `public static class` taking numbers and returning numbers,
asserted by a self-test with no scene tree):

- **Distance-to-gain arithmetic**, if written as a function rather than as property writes
  inside `_Process`.
- **Zone-membership logic** — the occupancy stack, innermost-wins resolution, doorway
  enter/exit ordering, and that a remote player's crossing never reaches the router.
- **Bus idempotence** — that N zones yield exactly one `AudioEffectReverb`, via
  `AudioServer.GetBusEffectCount`. The direct regression test for pattern 4's bug, and cheap.

**Not provable headless, ever:** whether a player can localise a sound; whether a room sounds
like that room; whether a crossfade snaps; whether the rear filter helps.
`tests/Run-VoiceTest.ps1:30` states the limit for the shipped voice system — *"Audio
quality/feel is explicitly not provable here — that is the weekend manual playtest (two clients,
walk toward/away while one holds push-to-talk)"* — and applies verbatim here. The repo has
shipped an island rotated 90° through a fully green suite.

**The practical acceptance test is one sentence: a player hears something, calls out a direction
and a rough distance over proximity voice, and is right.** Two humans, a headed build, a house
with rooms in it. Nothing else settles it.

## Troubleshooting

- **Everything sounds centred** — no active listener: no `Camera3D` with `Current` on the
  viewport, or an `AudioListener3D` made current and never cleared. Only the *last* one made
  current is used.
- **A sound cuts off abruptly at a fixed range** — `MaxDistance` is a hard wall, not a fade.
  Raise it and let `UnitSize` do the falloff; the cutoff exists to stop mixing, not to shape.
- **Everything is inaudible past a few metres** — `UnitSize` too small for the world scale.
  Compare against the shipped `6.0`, not against `1.0`.
- **Players misjudge distance** — check `AttenuationModel`; `InverseSquareDistance` reads
  steeper and more realistic than `InverseDistance` for world sound. Also check the attenuation
  filter is not disabled — losing highs *is* a distance cue.
- **Reverb sounds boxy outdoors** — interior `Wet` / `RoomSize` on an exterior preset. Outdoors
  wants near-dry.
- **The mix gets wetter the longer the level is** — pattern 4's stacking bug. Count effects with
  `AudioServer.GetBusEffectCount`.
- **A snap walking through a door** — a bus volume switched rather than crossfaded, or a tail
  truncated by a mute. Pattern 5.
- **Reverb flickers in a doorway** — `BodyEntered`/`BodyExited` retriggering. Ref-count occupancy.
- **My reverb changes when a teammate walks somewhere** — the `IsInGroup("player")` bug.
  `AudioServer` is per-process; gate on the locally-controlled player.
- **The zone does nothing at all** — a misspelled group name in the `.tscn` `[node]` header fails
  silently, or `Area3D.Monitoring` is `false`, or `AreaMask` is still at its `0` default.
- **Frame cost appears only with 4+ players** — per-frame property writes on every emitter.
  Pattern 3.
- **Players always know when something is behind them** — the rear cue became a reliable
  telegraph. §8.3. Route it back through `/direct` and `vfx-proximity`; do not tune the threshold.

## Caveats

- **No numbers as law.** Every `UnitSize`, `MaxDistance`, wet level, slew time and preset here is
  a starting point with its reasoning attached; the reasoning is the part that transfers. None
  has been profiled on the GTX 970 floor.
- **Unverified is stated, not implied.** Checked against GodotSharp 4.7.0: both enums,
  `MaxPolyphony`, the two attenuation-filter properties, `UnitSize` / `MaxDistance`,
  `PanningStrength`, `AudioEffectReverb`'s ranges, `AudioListener3D`, `Area3D`'s audio
  properties, `AreaMask`'s `0` default, and the absence of any HRTF surface. Still unverified:
  whether `Area3D` audio lookup is emitter- or listener-positioned, and whether a live
  `AudioEffectReverb` picks up parameter mutations. **The brief's samples are not evidence and
  two of them would not compile.**
- **Not the voice pipeline.** `scripts/voice/` is shipped and tuned. Never fork or "harmonise"
  its values.
- **No affect.** This skill never argues a sound should exist. Making it localisable is not an
  argument that it belongs.
- **Not a resolver.** `LEVEL-BIBLE.md` §8.3 is `[BLANK — Talon]` on whether Sail commits to a
  dedicated diegetic audio channel that is *always* meaningful, and the bible notes it
  *"constrains sound design broadly, which is why it is a call rather than a detail"* — it bears
  directly on whether a reverb change is allowed to *mean* something. §8.1's inherited rule
  stands regardless: **audio alone is never sufficient**, because the tether severs it on
  purpose. `THRILL-BIBLE.md` §6.2's night-floor-vs-night-reversal conflict is live and relevant
  — a dark that costs sight raises the stakes on spatial audio enormously — and it is *"a fork
  for Talon"*, not one a sound skill settles by building for one side. §9's tone **axis** is
  decided (2026-07-26); the **ratio** is still open, ripeness trigger *"the first playtest in
  which anyone is actually frightened"* — a preset that commits a space to comic or to sincere
  should say which way it leans rather than deciding.
- **The rear cue is not this skill's to build.** §8.3 and `vfx-proximity` both govern it.

*Scope note: written 2026-07-28 against `feat/neighbourhood-exterior`, where proximity voice in
`scripts/voice/` is the only tuned 3D audio that ships, `SfxLab.PlayStream3D` is the only other
positional path, and there is no reverb bus, no zone system, no `AudioListener3D`, no
air-absorption configuration, no audio asset file, and no test asserting anything about an
`AudioStream` or a bus. The 2026-07-28 neighbourhood-ring design names audio as out of scope for
the work in flight. **Current bite: none — the API corrections and the scale reconciliation are
checked and usable; nothing is implemented.** First real test: one house interior with three or
four authored reverb presets behind a locally-gated router, walked headed by two players on
separate machines with proximity voice open. Passing looks like a player calling out "something
in the garage" from the hallway, being right, and neither of them hearing the other's room
change.*
