# Skills

Four tracks. **Direction** decides what a thing should make people feel. **Atmospheric VFX**
turns a direction into Godot. **Audio substrate** turns an audio-sync decision into Godot DSP and
synthesis. **Interface** is portable UI/UX craft. They share one discipline — manually invoked,
never resolving a blank by inventing a value — and nothing else.

| Track | Skills | Produces |
|---|---|---|
| Direction | `/direct` | directorial calls checked against `docs/THRILL-BIBLE.md`, plus its ledger |
| Atmospheric VFX | `/vfx-escalation` `/vfx-lighting` `/vfx-disturbance` `/vfx-proximity` `/vfx-corruption` `/vfx-pulse` `/vfx-particles` `/vfx-audio-sync` | Godot 4.7 C# + shader work executing a direction |
| Audio substrate | `/sound-integration-guide` `/sound-soundscape-construction` `/sound-spatial-audio` `/sound-real-time-effects` `/sound-optimization` `/sound-procedural-reactive` `/sound-threat-audio` `/sound-anticipation-escalation` `/sound-silence-negative-space` `/sound-event-wiring` | Godot 4.7 C# DSP, synthesis and replication executing an audio-sync decision |
| Interface | `/ui-design` | screens, HUD, menus and forms |

Three of them chain: direction → atmospheric VFX (`/vfx-audio-sync` specifically) → audio
substrate. `/direct` is the only one that originates a feeling; a VFX or sound skill invoked
bare has nothing to build. Interface chains with nothing.

The asset-generation track (`/spec-creature` → `/compile-spec` → `/generate-asset` →
`/validate-asset` → `/author-clips`) and the design-contract track (`/spec-entity`,
`/spec-pressure`, `/spec-interaction`, `/spec-urgency-cue`, `/spec-level`) live in the `Sail`
repository; re-import them when that pipeline is live here (`MVP-SCOPE.md` §3.1).

Some skill bodies cite research and audit documents under `docs/superpowers/` that were not
carried into this repository; those citations resolve in `Sail`.

Full index with one-line purposes: `docs/agents/SKILLS.md`.
