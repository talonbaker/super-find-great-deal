---
paths:
  - "**/*.tscn"
  - "project.godot"
---

# Godot scene and project-file traps — each has already cost a session

- **`Transform3D`'s 12 floats in a `.tscn` are basis ROWS.** A pure rotation written
  column-wise comes out inverted, and only a render shows it. Never trust a
  hand-computed transform without a headed look.
- **Groups go in the `[node]` header.** Wiring a group anywhere else — or a one
  character group-name mismatch — fails silently and looks exactly like a feature
  that does nothing.
- **Every part of a level is authored in its scene file, never built in code** (Talon,
  2026-08-27). `BubbleTestSelfTest` counts meshes, colliders and bodies in each section's
  packed state and again live, and fails on any difference. A section that builds a rock
  in `_Ready` turns the suite red.
- **MultiMesh buffer stride is not always 12.** A layer carrying per-instance colour is
  stride 16; parsing with the wrong stride yields plausible garbage, not a crash. Check for
  per-instance colour before parsing any MultiMesh out of a `.tscn`.
- **Godot silently strips `project.godot` settings that match the engine default**,
  comments included. Do not treat an absent line as evidence a setting was never
  set; the export presets are the durable guard.

## Two more, both measured on the supermarket's first LEVEL prefab (CLOCK-1, 2026-09-19)

Cited from [`docs/agents/handoffs/2026-09-19-CLOCK-1.md`](../../docs/agents/handoffs/2026-09-19-CLOCK-1.md)
§3.1 and §3.2, and added here by INT-0B because CLOCK-1 deliberately left this shared file alone
while five lanes were branched off one base.

- **An `[Export]` on a nested PackedScene instance line is SILENTLY DROPPED on this build.**
  `RoundClock` carried `[Export] public string Room` and each room's `.tscn` set `room = "..."`
  on the instance line; it read back as the empty string on all three clocks, with no error and
  nothing in a diff to see — because the NATIVE properties on the same line (transform, mesh,
  mass) apply perfectly, so the clock hangs in exactly the right place not knowing where it is.
  **Derive identity from the node NAME instead** (`RoundClock.ResolveRoom` walks up to the
  section scene's root name); a node name is native and survives instancing. This is the THIRD
  time this repo has paid for the same trap — `PropManager.AuthoredKindOf` and
  `Carryable.LoadLiftM` are the other two.
- **Every LEVEL prefab must be listed in `SupermarketWorldSelfTest.SectionScenes`, or the
  packed-vs-live check cannot see it.** `CountNodes` stops at an instance boundary (a node with
  its own `SceneFilePath` is that file's business), which is the rule the PACKED side already
  used — so a prefab that builds a child in `_Ready` is invisible from its room's count.
  Measured: `AddChild(new Node3D())` planted in `RoundClock._Ready` left all three rooms green
  at `packed=27/38/44`, and failed only once `RoundClock.tscn` was in `SectionScenes`
  (`packed but 5 live`). There is no mechanism that notices a prefab was forgotten; it is a list.
