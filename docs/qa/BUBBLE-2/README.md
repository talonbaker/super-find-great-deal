# BUBBLE-2 — the bubble in the tower, before and after

Produced by `tools/dev/bubble2_capture.ps1`. **`--capture-cam` on every shot**, so nothing here is
framed by a bot's follow camera — the subject is a place 130 m from where the bot stands, and a
capture bot renders a flat grey world without the flag (`.claude/rules/test-suite.md`;
`docs/testing/TESTER-HANDOFF.md`: *"`--capture-cam` is MANDATORY or you get a false pass"*).
Noon, `--cycle-freeze`, so the before and after runs are lit identically.

**Two cameras, two runs, and neither camera moves between them.** The only difference between a
`before-*` frame and its `after-*` frame is the bubble.

| Directory | Camera (world) | What it shows |
|---|---|---|
| `before-close` | `75.9,26.5,-60.5` → `75.8,20.8,-65.95` | `Bubble_Tangle_10` at its old `(76.401, 20.499, -65.948)`, i.e. **not visible at all** — it is inside `Tangle/Tower/Jumble01`. The one bubble in frame is its neighbour `Bubble_Tangle_11`. |
| `after-close` | *identical* | **Two** bubbles: `Tangle_11` where it always was, and `Tangle_10` now in open air at `(75.151, 20.999, -65.948)`, on the stair line between treads 19 and 20. |
| `before-wide` | `78.6,25.4,-75.2` → `75.9,21.0,-65.9` | The spire's foot from the south-east. Context, and the word Talon used: the magenta in this frame is the tangle's own ground and goal colour. |
| `after-wide` | *identical* | The same frame with the bubble restored to reachable air. |

## The magenta

Talon called it "the magenta area". **The Tangle is the only section in the level that reads
magenta**, and it is not a guess — `resources/materials/bubbletest/tangle_ground.tres` is
`HSV(300, 0.30, 0.482)` and `tangle_goal.tres` is `HSV(300, 0.86, 0.555)`. Hue 300° is magenta by
definition. Every other section's rung-1 ground and rung-3 goal sit elsewhere on the wheel: blue
222°, cyan 183°, green 127°, hub 38°, red 16°. The pink checker under the pile in all four frames
is that ground; the brighter violet chips on the stair's last tread and the spire's spur pad are
the goal colour.

## What a reader should take from the pair

The bubble was never dim, mis-lit or hidden behind something. It was **inside** a 2.04 × 3.15 ×
4.03 m block, with 100% of its collider shell buried, so there was no angle from which it could be
seen and no position a body could occupy that would pop it. That is why the before frame simply
has nothing there.
