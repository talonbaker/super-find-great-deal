# Skills index

The skills this repository carries, by track. Each is a directory under `.claude/skills/` with a
`SKILL.md`; invoke by name (`/direct`). All are manually invoked and none resolves a blank by
inventing a value. The asset-generation and design-contract tracks (creature specs, mesh
generation, clip authoring, level/pressure/interaction contracts) stay in the `Sail` repository
until the pipeline they serve is live again — see `MVP-SCOPE.md` §3.1.

## Direction

| Skill | Purpose |
|---|---|
| `/direct` | the horror director: decides the feeling a thing must manufacture, runs `THRILL-BIBLE.md` gates, writes back to its ledger; never specifies implementation |

## Atmospheric VFX (execute a direction in Godot; never originate one)

| Skill | Purpose |
|---|---|
| `/vfx-escalation` | the conductor: session escalation curve, dread state machine, tension budget |
| `/vfx-lighting` | WorldEnvironment / sun / fog making a directed feeling into atmosphere, against the shipped `CycleDriver` and `OutdoorAtmosphere` |
| `/vfx-disturbance` | "something is here" without showing what |
| `/vfx-proximity` | perceptual encoding of how near / which way a threat is, never seen |
| `/vfx-corruption` | persistent physical wrongness left in a level |
| `/vfx-pulse` | the world alive on one slow shared rhythm |
| `/vfx-particles` | the substrate: MultiMesh, pooling, culling, the frame budget on the low-end floor |
| `/vfx-audio-sync` | align, withhold, or decouple world audio against visuals; hands the *how* to the sound family |

`docs/ATMOSPHERIC-VFX-INTEGRATION.md` is the map.

## Audio substrate

| Skill | Purpose |
|---|---|
| `/sound-integration-guide` | the family's map, entry point, build order, and voice-budget arithmetic |
| `/sound-soundscape-construction` | layered ambient beds: frequency lanes, depth, choreography, ducking under voice |
| `/sound-spatial-audio` | placing sound in space: falloff, absorption, reverb zones, the listener |
| `/sound-real-time-effects` | bus effect chains and runtime-driven filters |
| `/sound-optimization` | the concurrent-voice budget, pooling, steal policy |
| `/sound-procedural-reactive` | variety and state-response from a zero-asset budget |
| `/sound-threat-audio` | a threat's voice and its nearness in sound |
| `/sound-anticipation-escalation` | making a tension value audible via bus DSP |
| `/sound-silence-negative-space` | audio beats executed by taking sound away |
| `/sound-event-wiring` | triggers, beat alignment, fire-exactly-once, cross-client simultaneity |

## Interface

| Skill | Purpose |
|---|---|
| `/ui-design` | UI/UX craft for any screen / menu / HUD — hierarchy, spacing, type, colour, states, motion, accessibility; the substrate is `docs/UI-DESIGN-SYSTEM.md` |
