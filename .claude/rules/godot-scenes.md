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
