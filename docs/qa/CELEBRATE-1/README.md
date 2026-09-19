# CELEBRATE-1 — the level notices when the last bubble goes

Produced by `tests/Run-CelebrateCapture.ps1 -Label 2026-09-04`. A dedicated server runs the **real
`bubbletest` level** (112 authored bubbles, frozen at noon) with `--bubble-pop-all-at`, and a
windowed client joins it and captures its own viewport across the completion.

## What it does, in one paragraph

When the shared bubble tally reaches its total, the game plays a short rising four-note chime,
sends up a small burst of pale-gold sparkles off every player standing in the world, and puts one
line of text near the top of the screen: *"That's every bubble in the world."* Then it stops. It
does not take the camera, dim the screen, pause anything, cover the play area or swallow a single
key — you can keep walking, jumping and honking straight through it, and about a second and a half
later there is no trace of it left. It happens for **everybody in the session at the same moment**,
not just whoever touched the last bubble, because the tally is shared. And it only ever happens for
a person: a test bot that collects every bubble hears nothing and sees nothing.

## The frames, in order

| Frame | What it shows |
|---|---|
| `CelebCap-9.6s.png` | **Before.** The tally is still climbing; no line, no sparkles. |
| `CelebCap-10s.png` | **The moment**, ~0.2 s in. The line is fading up, the first sparkles are off the head. |
| `CelebCap-10.4s.png` | **The beat at full.** Tally reads 112, the line is up, the burst is at its widest. |
| `CelebCap-11.2s.png` | **~1 s in.** Sparkles gone, line still up, world entirely unchanged behind it. |
| `CelebCap-13.2s.png` | **After.** Nothing left on screen but the ordinary HUD. |

The timestamps are the capture bot's own elapsed clock; the celebration fired at 9.85 s of it.

## What you cannot see here

The **sound**. A PNG cannot carry it — `tests/Run-CelebrateTest.ps1` proves it fires on every peer
in a session at the same moment and exactly once per completion, and
`tests/unit/CelebrateTests.cs` pins its length, headroom and shape (it climbs; it does not clip; it
is the same sound every time).

## Two things about the capture rig, so a reader does not misread a frame

- The client runs `--celebrate-force`. The celebration is gated on a **person** driving a body, so
  a capture bot correctly refuses it — that refusal is exactly what `Run-CelebrateTest.ps1`
  asserts. The flag opens that one gate for that one process. It cannot unlock an achievement:
  `AchievementRuntime` is built (or not) in `SandboxAvatar.ConfigureAsNetworked`, which never
  reads it.
- The camera is parked about **2 m** from the body so the sparkles are legible at all. In play the
  follow camera sits three times further back, so they read considerably smaller than they do
  here. `BubbleCelebration.PuffCount` / `PuffSizeM` are the two knobs if that is still too much.
