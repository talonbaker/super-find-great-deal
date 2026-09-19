---
name: vfx-pulse
description: Use when a directed feeling in TIDE calls for the world itself to seem alive under the player — fog that breathes, ambient light that swells and settles, several environmental systems moving on one slow shared rhythm, or a tempo that quickens as pressure rises.
---

# vfx-pulse

## Overview

The **sustained** layer, not the sharp one. A pulse is ambient rhythm that implies the space
has agency without ever showing a creature — and its whole value is that the player is *in*
it rather than *watching* it. Source is
`docs/superpowers/research/atmospheric-vfx-RESEARCH-RESULT.md` §3.

Two things distinguish this skill from every other `vfx-*`:

1. **It is the easiest one to build and the easiest one to ruin.** The maths is a sine. The
   failure mode is a design failure, not a performance one: a pulse the player notices has
   stopped being a state and become an effect, and at that point it is doing the opposite of
   what it was directed to do. See *The amplitude ceiling*.
2. **It is the one skill whose correctness depends on there being exactly one of it.**
   Independent per-node timers drift, and drift converts "one thing breathing" into "several
   unrelated glitches" — which is also the most common bug report the research records. See
   *One conductor, many subscribers*.

## Directed, not decided

**`/direct` and `docs/THRILL-BIBLE.md` decide which feeling, why here, and whether the device
is already spent (§10 device register). This skill decides how, in Godot. It executes a
direction; it never originates one.**

Sections served:

- **§2.1 ambiguity is the fuel** — and specifically its corollary: *"the world must seem to
  have intent you cannot read. Not intent it has; intent it seems to have."* A breathing world
  is the purest form of that corollary available without an entity, because a breath has no
  direction, no target and no legible purpose. **A pulse that becomes readable — as a warning,
  as a proximity meter, as a countdown — has converted seeming-intent into information and
  spent the fuel.** That is the failure this skill guards hardest against.
- **§3.5 rhythm is the director's real work** — *"spacing, not intensity."* This skill is the
  literal instrument for that sentence at the ambient layer, which makes it unusually easy to
  mistake for the director's job. It is not. The conductor's tempo curve is a rhythm decision
  and the rhythm decisions belong to `/direct`.

Governed by:

- **§8.2 startle is release, never build.** A pulse is a build device. The instant it produces
  a startle — an amplitude spike, a sudden onset, a tempo jump the eye catches — it has become
  a release used as an engine, which §8.2 says desensitises, breaks immersion and reads as
  cheap. **This is the binding prohibition on this skill and *The amplitude ceiling* is its
  operational form.**
- **§8.6 unrelenting tension.** A rhythm that only ever quickens is a build that never breaks
  (§3.1) and it normalises: the player stops being tense and cannot be made tense again
  cheaply. The tempo curve therefore needs valleys, and the valleys are the director's, not
  this skill's. Related: §8.3 forbids reliable telegraphs, which is a second, independent
  reason the tempo must not be a function of the clock — see *Integration points*.

**Two things this skill must not resolve.** `THRILL-BIBLE.md` §9's axis is decided 2026-07-26
(horror, dread-forward, atmosphere the primary instrument); only the dread/absurd **ratio**
stays open (ripeness trigger = the first playtest where anyone is actually frightened). A
rhythm that reads as comic wheezing and one that reads as something large and patient spend
that ratio differently, so build the mechanism and leave the character as a knob.
`THRILL-BIBLE.md` §6.2's night-floor conflict — `DayNightSky.MinAmbientEnergy` versus the
night reversal — is a shipped decision in live opposition to a doctrine section, and no
`vfx-*` skill resolves it. A pulse riding ambient energy sits directly on top of that fork
(see *What can actually breathe*); report, do not settle.

## When to Use

- A `/direct` call has asked for a place to feel alive, watchful, or as if it has a rhythm,
  and something has to be built
- Several atmospheric channels need to move as one thing rather than as several things
- An existing ambient effect reads as a glitch, or reads as too obvious, and the failure needs
  diagnosing (see *Troubleshooting*)
- Someone is about to give three nodes their own `Time.GetTicksMsec()` timer

**Not for:**

- **Deciding that a place should breathe** — `/direct`. This skill will happily make an empty
  room breathe and the room will still be empty.
- **A heartbeat** — see *What is rejected, and why*. The research's lub-dub pattern is refused
  here on two independent grounds and neither is performance.
- **Any sharp beat.** A spike (`THRILL-BIBLE.md` §4) needs stake, violation, simultaneity and
  legibility; a pulse supplies none of them and cannot be escalated into one. If a direction
  needs a moment, this is the wrong skill and reaching for amplitude is the wrong fix.
- **Transient "something is here" cues** — foliage disturbance, ghost ripples, prop nudges.
  Research §1's territory, a different skill. A pulse is continuous by definition.
- **Persistent marks in the world** — `vfx-corruption`.
- **The escalation curve itself** — `vfx-escalation` owns the session-monotonic quantity, the
  dread state machine and the tension budget. This skill consumes a tempo; it does not decide
  how tempo relates to how far into the session anyone is.
- **Audio rhythm.** There is no ambient bed in TIDE (`THRILL-BIBLE.md` §6.3, `unbuilt`), so
  there is nothing for an audio pulse to modulate and nothing for a wrong-silence device to
  withdraw. That is an investment someone has to make first, not a gap to fill here.

## One conductor, many subscribers

**This is not a style preference. It is the correctness condition for the whole skill.**

Two nodes each running `sin(t / period)` from their own accumulator will agree at t=0 and
disagree forever after. Floating-point drift, differing `_Process` entry order, a node added
mid-session, a node that was culled and resumed — any of these desynchronises them. Once
desynchronised the fog is swelling while the light is settling, and the player is looking at
two unrelated wobbles instead of one thing breathing. The research records "feels like a
glitch" as the top symptom and phase drift as the top cause; in a 2–6 player game it also
breaks `THRILL-BIBLE.md` §4.1's simultaneity condition, because now no two players are even
seeing the same wobble.

**This repo already made this ruling and wrote it down.** `scripts/game/world/CycleDriver.cs`
exists precisely so the sun, the moon, the sky and (later) the sea height all derive from one
value — its `BUILD-SPEC` citation is verbatim *"do not build three independent timers."* A
breath conductor is the same architecture at a shorter period, and it should look like a
sibling of `CycleDriver`/`DayNightSky`, not like something new.

**Subscriber shape: read a singleton, do not receive a signal.** The research emits a `Beat`
signal every frame. The repo's shipped idiom is the opposite and better here:
`DayNightSky._Process` reads `CycleDriver.Instance.Phase` directly every frame behind a
`Synced` gate. A per-frame signal emission to N subscribers is marshalling churn for a value
every subscriber wants anyway, and it inverts control — subscribers should pull the current
breath, not be pushed a beat.

```csharp
// Sibling of CycleDriver in shape: no local accumulator. Breath is a pure function of the
// replicated clock every frame, so two clients can never drift apart the way two independent
// `_t += delta` timers would — see Integration points.
public partial class BreathConductor : Node
{
    public static BreathConductor? Instance { get; private set; }

    /// <summary>Current breath in [-1, 1]. Subscribers READ this; nobody else advances it.
    /// Read-only by construction — a public setter here is how the drift bug comes back.</summary>
    public float Breath { get; private set; }

    /// <summary>Seconds per breath. Set by whoever owns tempo (vfx-escalation), never
    /// derived here, and never derived from CycleDriver.Phase — see Integration points.</summary>
    public float PeriodSeconds { get; set; } = 3.2f;

    /// <summary>Mirrors the period CycleDriver.Setup was actually called with (e.g.
    /// --cycle-period). Not derived here: CycleDriver's own period is private, and inventing
    /// a second source for it is exactly the "second timer" this skill forbids.</summary>
    public double CycleDriverPeriodSeconds { get; set; } = 120.0;

    public override void _Ready() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public override void _Process(double delta)
    {
        var cycle = CycleDriver.Instance;
        if (cycle is null || !cycle.Synced)
            return; // hold the last value rather than free-run ahead of the clock.

        // CyclesElapsed + Phase is the replicated, snap-corrected, agreed-by-every-client
        // elapsed time (in units of the CycleDriver's own period) that Integration points
        // describes. Deriving Breath from it directly — instead of accumulating delta in a
        // private field — is what makes two clients' breath agree with no new RPC.
        double elapsedSec = (cycle.CyclesElapsed + cycle.Phase) * CycleDriverPeriodSeconds;
        Breath = Mathf.Sin((float)(elapsedSec * Mathf.Tau / Mathf.Max(0.05f, PeriodSeconds)));
    }
}
```

Note what is *not* in there: no amplitude, no target property, no reference to any visual
system. The conductor publishes a number in `[-1, 1]`; each subscriber owns its own amplitude
because each channel has a different perceptual sensitivity, and a shared amplitude would make
the fog and the light cross the visibility threshold at different times anyway.

**Shader subscribers get the same rule, and it is easy to get wrong.** Writing
`sin(TIME * ...)` inside a shader is free and tempting, and it silently reintroduces the
problem: two materials each running their own `TIME` sine are two independent timers that
happen to be on the GPU. Push the conductor's `Breath` into **one shared uniform** on the
shared material once per frame instead. That is one `SetShaderParameter` per material per
frame regardless of instance count, and it keeps `ART-BIBLE.md` §4.2's material sharing intact
(the same sharing `Playground.tscn` already relies on — one `ShaderMaterial` subresource used
as `material_override` across every floor slab).

## The amplitude ceiling

**Named rule: amplitude crosses into conscious perception before period does. Escalate period.
Hold amplitude.**

The research states the symptom (*"reduce amplitude before reducing period; amplitude is what
crosses into conscious perception first"*) as troubleshooting advice. Under
`THRILL-BIBLE.md` §8.2 it is not advice, it is the contract:

- A pulse the player is *in* is a state. A pulse the player *watches* is an effect.
- Amplitude is what moves it across that line. A slow deep swell is noticed; a fast shallow
  one usually is not.
- An effect the player watches, arriving on a rhythm, is a **telegraph** — and a rhythmic
  telegraph is §8.3's failure (players learn when they are safe from the trough) delivered by
  §8.2's mechanism (a perceptible spike used as a build). One mistake, two prohibitions.

Operationally:

- **When tempo escalates, escalate the period only.** Do not scale amplitude with the same
  input. It is the natural thing to do and it is wrong: quickening *and* deepening compounds
  two visibility increases at once, and the pulse surfaces into awareness at exactly the moment
  the director wanted the player under maximum unexamined pressure.
- **Raise amplitude only at an instant the director has explicitly designated as a break**
  (`THRILL-BIBLE.md` §3.1). That is a directed beat with a §10 register entry, not a curve.
- **Amplitude is per-channel and per-channel calibration is the real work.** Fog density,
  ambient energy and a shader tint do not have the same perceptual gain. A single "amplitude"
  knob shared across channels means at least one of them is over the line.
- **A pulse should be describable by playtesters as a feeling and not as a thing.** "It felt
  like the place was watching" passes. "The fog pulses" fails, and the fix is amplitude, never
  period.

## What can actually breathe in Sail

Honest inventory of the channels that exist today. Two of the four obvious ones are already
owned by another system and writing to them is a bug, not a tuning choice.

| Channel | Verdict |
|---|---|
| `Environment.FogDensity` | **Available.** Verified property (Godot 4.7 class ref; `BootWarmup.cs:96`); `Playground.tscn` authors `fog_density = 0.004`. Whole-scene, one write per frame, no per-instance cost. |
| A uniform on a shared `ShaderMaterial` | **Best available.** One write, every instance, zero draw-call impact, and it composes with §4.2's material sharing instead of fighting it. |
| `Environment.AmbientLightEnergy` | **Owned by `DayNightSky`. Do not write it.** |
| `DirectionalLight3D.LightEnergy` (sun/moon) | **Owned by `DayNightSky`. Do not write it.** |
| `Environment.VolumetricFogDensity` | Exists (verified), and is the research's preferred channel. **Refused by default** — volumetric fog is real per-frame GPU cost and the perf floor is a GTX 970 on Forward+ in a scene the perf brief already calls overdraw- and draw-call-bound. Not this skill's call to enable; if volumetrics are on for other reasons, breathing them is nearly free. |
| Particle emission rate | Weak. The repo uses `CpuParticles3D` one-shots (`JuiceFx`) and has no ambient particle bed, so there is currently no continuous emission to modulate. |

**The `DayNightSky` conflict is concrete, not theoretical.** `DayNightSky.Apply` writes
`_env.AmbientLightEnergy`, both `DirectionalLight3D.LightEnergy` values, the light colours and
four sky colours **every frame** from its phase curve. A pulse that also writes any of those
is in a last-writer-wins race with a system that rewrites unconditionally: depending on node
order the pulse either vanishes entirely or fights visibly. **Rule: never write a property
`DayNightSky` owns.** If a direction genuinely needs light to breathe, the correct route is a
multiplier *inside* `DayNightSky`'s own apply path — which is a change to that class, owned by
whoever owns it, not a second writer bolted alongside. And note where that lands: ambient
energy at night is `MinAmbientEnergy`, the §6.2 fork. A pulse on ambient light is a pulse on
the contested value. Report it; do not settle it.

**Per-frame `Environment` writes are established precedent, not new risk.** The *cost* of
writing `Environment.FogDensity` every frame is *unverified against Godot 4.7* — but
`DayNightSky._Process` already sets `AmbientLightEnergy` plus four `ProceduralSkyMaterial`
colours every frame in the shipped build, so a single additional `Environment` property write
is within a pattern this project already ships and profiles. Do not treat it as free; do treat
it as precedented.

## What is rejected, and why

**The heartbeat light (research §3.2) is refused.** Its lub-dub Gaussian double-pulse on a
`DirectionalLight3D` is the most quotable pattern in §3 and it fails here twice:

1. **It writes a property `DayNightSky` owns.** See above. Mechanically broken before any
   design argument.
2. **A recognisable heartbeat is a genre signifier, and a decoded signifier is an effect.**
   The player identifies "heartbeat" within about one cycle, and from that instant they are
   watching a horror-game device rather than being in a space. That is exactly
   `THRILL-BIBLE.md` §2.1's corollary inverted — intent it *seems* to have, replaced by a
   convention the player can name — and §8.2's "predictability is the enemy of fear."

There is also a quieter arithmetic problem worth naming, because it generalises: a Gaussian
double-pulse has a far higher peak-to-mean ratio than a sine at the same nominal amplitude, so
it is **more** perceptible for the same configured number. Any non-sinusoidal breath shape
needs its amplitude re-derived against *The amplitude ceiling*, not carried over.

**Perfect sync with any other rhythm is reserved, not default.** The research's instinct is
right and worth keeping: if a player-facing rhythm ever exists, having the ambient breath
suddenly lock to it is a legible escalation cue precisely because it was never locked before.
That is a directed beat with a §10 register entry — not a thing this skill does on its own.

## Tuning guide

**Every number here is reasoning, not a gate.** Playtest calls, all of them.

- **Period, 2.5–4 s at baseline.** The reasoning: slower than a resting heart rate reads as
  "big and calm" rather than "urgent," which is the register `THRILL-BIBLE.md` §1 wants for
  sustained dread as opposed to fear. Faster than roughly a second stops reading as breathing
  and starts reading as an alarm — a different emotional register, and per §8.2 an alarm used
  as a build is a startle used as an engine.
- **Amplitude around 8% of the base value** is the research's starting figure for fog. Treat
  it as a *ceiling to test downward from*, not a target: the correct amplitude is the largest
  one no playtester mentions. Per channel, always.
- **±5% period jitter** once several systems pulse together, so the rhythm is not mechanically
  perfect. The reasoning is that metronomic regularity reads as sound design rather than as a
  creature. **But jitter belongs to the conductor, not to the subscribers** — jittering
  independently is the drift bug wearing a costume. One jittered period, shared.
- **Onset matters more than the numbers.** A pulse that switches on reads as an effect
  starting. Fade the amplitude in over several periods so there is no instant at which
  anything began; the player should be unable to say when it started, which is the same
  property that makes withdrawing it later land (§6.3's logic, applied visually).
- **The tempo curve is the director's.** Where it quickens, where it slackens, whether it has
  valleys at all — §3.5 and §8.6. This skill implements a curve; it does not draw one.

## Integration points

**`CycleDriver.Instance.Phase`, gated on `Synced`.** `scripts/game/world/CycleDriver.cs` is
the server-authoritative clock — sim-tick advance, 2 Hz unreliable broadcast on its own
channel, latest-wins staleness guard, targeted `SendPhaseTo` for late join and reconnect.
Read it; author nothing new. **Never derive anything visible from `Phase` while `Synced` is
false** — those fields sit at a zero-initialised default until the first authoritative update,
and treating that as "the server's phase is 0" is the bug `CycleDriver`'s own doc names as the
single most likely one in the feature. `DayNightSky._Process` is the working consumer shape.

**`Phase` is cyclic and tempo must not ride it.** `Phase` wraps in `[0,1)` over a 120 s
default period; `CyclesElapsed` counts the wraps. It is **not** the research's monotonic 0→1
`DayProgress` across a ~900 s session. Research §3.4's
`PeriodSeconds = Mathf.Lerp(3.2f, 1.0f, eased * eased)` transcribed onto `Phase` produces a
breath that quickens toward night and then *slackens back* every two minutes — a build with a
guaranteed reprieve on a fixed schedule. That is worse than merely wrong:

- It is a **reliable telegraph** (§8.3). Players decode "the world is calming down, so it is
  about to be dawn," and the pulse has become a clock readout — the exact conversion from
  seeming-intent to information that §2.1 forbids.
- It is a **build that resets rather than breaks** (§3.1/§3.2). A reset returns the player to
  zero; a break is supposed to leave the larger tension standing.

**`vfx-escalation` owns the session-monotonic derivation** — its `SessionProgress` seam, whose
source (`CyclesElapsed` vs. a sibling driver vs. the tide) is itself still an open decision.
Take `PeriodSeconds` from there, or take it as an explicit input. Do not solve it here and do
not build a second clock.

**`Phase` is the wrong source for tempo and the right source for agreement.** This is worth
stating positively, because it recovers most of what the research wanted from replication for
free. Every client already agrees on `Phase` — it is replicated, staleness-guarded,
snap-corrected and delivered to late joiners through the same funnel `PropManager.SendDumpTo`
uses. Seeding the conductor's phase offset as a deterministic function of `Phase` therefore
gives every client the same breath at the same instant with no new RPC, no new authority
question and no bandwidth, which is what `THRILL-BIBLE.md` §4.1's simultaneity condition needs.

One caveat that follows from `CycleDriver`'s design: it **snap-corrects, never lerps** (a
lerp there could let a client's derived sea height lag the authoritative one). So on a
reconnect the seed jumps, and the breath jumps with it. At the amplitudes this skill argues
for, a discontinuity in a shallow sine is invisible — which is a further argument for the
amplitude ceiling, and a further argument against a sharply-peaked waveform, where the same
snap would read as a visible stutter at exactly the worst moment.

**Composition with the rest of the family.** Research §3 suggests firing an occasional
disturbance on a beat boundary — roughly one beat in twenty — so a physical event almost
imperceptibly aligns with the ambient rhythm, implying the environment and the presence are
the same thing. It is a strong, cheap idea and it is **a device**, so it goes in
`THRILL-BIBLE.md` §10 and the decision to use it is `/direct`'s. This skill supplies the beat;
`vfx-disturbance` supplies the event; neither decides they should meet.

## Precedent

**In-repo, and these matter more than the external ones:**

- `scripts/game/world/CycleDriver.cs` — the single-conductor rule already shipped, with the
  ruling written into its class doc (*"do not build three independent timers"*), plus the
  `Instance` singleton, the `Synced` gate and the pure-function seam pulled out for headless
  testing. A breath conductor is this file at a shorter period.
- `scripts/game/world/DayNightSky.cs` — the correct subscriber shape: read
  `CycleDriver.Instance.Phase` every frame behind `Synced`, hold the last state otherwise,
  never advance a guess. Also the file that owns the light properties this skill must not
  touch.
- `resources/shaders/DecorativeWater.gdshader` — two scrolling sine layers driving a normal
  perturbation, with the cost reasoned about in a header comment before a value was authored.
  The house precedent for cheap analytic motion and for justifying it in writing.

**External, via the research:** the general horror-sound-design principle of a layered ambient
bed with a subtle rhythmic threat layer underneath — extended here to a visual analog. Note
that TIDE has no ambient bed at all (`THRILL-BIBLE.md` §6.3, `unbuilt`), so the visual pulse is
currently carrying alone a job the reference material assumed audio would share.

## Troubleshooting

- **"It looks like a glitch."** Phase drift. Find the second timer — a node with its own
  accumulator, or a shader running its own `sin(TIME)`. There must be exactly one advancing
  value in the whole scene.
- **"Too obvious / distracting."** Reduce amplitude, not period. Per-channel: the offender is
  usually one channel over the line rather than all of them, and a global reduction hides that.
- **"The pulse disappeared."** You wrote a property `DayNightSky` owns and it rewrote you the
  same frame. Check `AmbientLightEnergy` and both `LightEnergy` values first.
- **"Players started predicting things from it."** The pulse has become information — §2.1 and
  §8.3. Almost always tempo bound to something the player can also see; if it is bound to
  `Phase`, that is the cyclic-clock bug above.
- **"It feels like an alarm, not breathing."** Period has gone too short. This is a register
  change, not an intensity change, and it needs `/direct`'s consent because it is a different
  feeling from the one that was directed.
- **"Nobody noticed it at all."** This may be a pass. Ask what the place *felt* like, not
  whether they saw a pulse; the target is a feeling reported without a mechanism attached. If
  the place also felt like nothing, the failure is upstream — an empty room breathing is still
  an empty room, and the finding goes to `/direct`.
- **"Frame time got worse."** Unlikely from this system — a handful of sine evaluations per
  frame. Check for per-instance material duplication or a per-node `_Process` on many nodes
  before suspecting the maths; the research's own note is that the cost risk is
  over-subscription, not arithmetic.

## Caveats

- **Verified vs. unverified, explicitly.** Verified against the Godot 4.7 class reference:
  `Environment.FogDensity`, `Environment.VolumetricFogDensity`,
  `Environment.AdjustmentSaturation`, `BaseMaterial3D.EmissionEnergyMultiplier`. Verified by
  shipping repo usage: `FogEnabled`/`FogDensity` from C# (`BootWarmup.cs`), `fog_density` in
  `Playground.tscn`, per-frame `Environment` and `ProceduralSkyMaterial` writes
  (`DayNightSky.Apply`), shared `ShaderMaterial` as `material_override` across many instances
  (`Playground.tscn`), analytic sine motion in a fragment shader
  (`DecorativeWater.gdshader`). **Unverified against Godot 4.7:** the per-frame cost of an
  `Environment` property write, `WORLD_POSITION` as a fragment built-in (both repo shaders use
  a vertex-computed varying instead), and volumetric-fog cost on the 970 floor. Do not
  transcribe research code as though it were checked.
- **No numbers as law.** Periods, amplitudes, jitter percentages, beat ratios — playtest calls.
  This file argues for orderings and ceilings.
- **Perf floor is a GTX 970 on Forward+** (`project.godot`: `forward_plus`). Never propose
  integrated graphics as a target. This system is cheap; the reason volumetric fog is refused
  by default is the floor, not this skill's arithmetic.
- **Composes, never defines** (`THRILL-BIBLE.md` §0). A breathing world does not have intent
  and does not grant any entity a capability. Route any "the feeling needs the creature to…"
  finding to `/spec-entity`.
- **A pulse cannot rescue a flat stretch.** §8.6's failure is a rhythm problem at the session
  scale, and adding an ambient wobble to a stretch with no clock and no stake produces
  atmosphere, not dread — the exact distinction `THRILL-BIBLE.md` §2.2 draws in those words.

*Scope note: written 2026-07-25 against a repo where nothing pulses, no conductor of any kind
exists at this timescale, and there is no ambient audio bed for a rhythm to share the work
with (`THRILL-BIBLE.md` §6.3 is `unbuilt` for that reason). The only shipped periodic value is
`CycleDriver.Phase` at a 120 s default period, which is three orders of magnitude too slow to
be a breath and is the wrong quantity for tempo besides. Current bite: none. First real test:
the jungle island's first atmosphere pass, once `/direct` has asked for a place to feel alive.
Passing looks like a playtester describing how a place felt without describing anything that
moved — and a `grep` for a second time accumulator in the scene finding nothing.*
