# Watis World — System requirements (playtest build)

Built on the project's stated performance floor: the game renders with Godot's **Forward+**
backend, which requires a **Vulkan-capable dedicated GPU**. Integrated graphics are explicitly
**not** a support target — the floor is comparable to R.E.P.O. / Lethal Company (Steam min specs),
with a **GTX 970 / R9 390** as the minimum GPU.

> Storage size below **was** real on 2026-07-25: measured directly from the repo's
> `build/windows-client/` — **193 MiB (201,259,171 bytes) on disk** — and rounded up to
> **~250 MB** for store headroom.
>
> **Stale as of 2026-08-29, and deliberately not corrected.** Nothing has been re-exported since
> that measurement and a month of work has landed on the trunk in between. The figure is
> **unverified, not known-wrong** — there is no basis to replace it with a different number, and
> inventing one would be worse than flagging this one. **Do not put it on a store page without a
> fresh export.** Re-measure `build/windows-client/` after the next export and update both
> tables below and `store-metadata.md`.

## Windows

**Minimum**
| | |
|---|---|
| OS | Windows 10 (64-bit) |
| Processor | Quad-core, Intel Core i5-4460 / AMD FX-8350 or better |
| Memory | 8 GB RAM |
| Graphics | **Dedicated, Vulkan-capable** — NVIDIA GTX 970 / AMD Radeon R9 390 (integrated graphics not supported) |
| DirectX / API | Vulkan 1.2 |
| Network | Broadband internet connection (multiplayer required) |
| Storage | ~250 MB available (measured build: 193 MiB / 201,259,171 bytes) |
| Additional | Microphone recommended for proximity voice chat |

**Recommended**
| | |
|---|---|
| OS | Windows 11 (64-bit) |
| Processor | 6-core, Intel Core i5-10400 / AMD Ryzen 5 3600 or better |
| Memory | 16 GB RAM |
| Graphics | NVIDIA GTX 1660 / AMD RX 590 or better |
| Network | Broadband internet connection |
| Storage | 250 MB available on an SSD |

## macOS

> **Not ready to list yet.** Re-verified 2026-07-25 directly against master: there is no macOS
> export preset in `export_presets.cfg` (only `LinuxServer` and `WindowsClient` exist), no
> `.github/workflows` CI directory of any kind, and `build/macos-client/` in the repo is empty.
> The macOS export preset, CI pipeline, and depot template referenced by an earlier draft of this
> page (and by commit `0be0ae4`, "Win build, macOS path + CI") exist only on a separate,
> **unmerged** branch (`feat/steam-playtest-page`) — none of that work is in master's history.
> Ship this page **Windows-only** until a macOS export actually lands on master and produces a
> real build to size and requirement-test. The tables below are kept as a ready-to-fill draft for
> when that happens, not a claim about the current build.
>
> **Re-verified 2026-08-29 and still true.** `export_presets.cfg` on the trunk holds exactly two
> presets, `LinuxServer` and `WindowsClient` — checked directly. **The branch pointer above is
> out of date, though:** the macOS work now lives in **PR #342 (STEAM-MAC-1)**, which is open,
> finished in both directions, and waiting on one decision about the ETC2 import line
> (`import_etc2_astc` false → true across 17 `.import` files; Windows and Linux unaffected).
> Godot 4.7 ships only a universal macOS template, so without that line there is no Mac build at
> all. Windows-only remains correct **today**, and stops being correct the moment #342 merges.

**Minimum** *(draft — not yet backed by a real build)*
| | |
|---|---|
| OS | macOS 12 Monterey or later |
| Processor | Apple Silicon (M1) — or Intel Mac with a Metal-capable GPU |
| Memory | 8 GB RAM |
| Graphics | Apple M1 integrated GPU / Metal-capable discrete GPU (Godot Forward+ runs on Metal via MoltenVK) |
| Network | Broadband internet connection |
| Storage | `<no build exists yet — cannot measure>` |
| Additional | Microphone recommended for proximity voice chat |

**Recommended** *(draft)*
| | |
|---|---|
| OS | macOS 13 Ventura or later |
| Processor | Apple Silicon (M2 or better) |
| Memory | 16 GB RAM |

## Notes for the Steamworks form

- Windows requirements go under the app's **Windows** tab. Don't create a macOS tab in Steamworks
  until there's an actual macOS build behind it — see the note above.
- Steam wants Min and Rec as separate columns per OS — the tables above map 1:1.
- The **integrated-graphics-not-supported** line is deliberate: it's cheaper to set the
  expectation on the store page than to field "why is it black-screening on my laptop" reports.

## Changes from the 2026-07-24 draft

- **Storage size filled in with a real number** (the old draft's `<confirm from build>`
  placeholder): `build/windows-client/` measures 193 MiB (201,259,171 bytes) as of 2026-07-25.
  Listed as ~250 MB on the store page to leave rounding headroom.
- **macOS section rewritten from "Built + CI-verified to launch" to "not ready to list."** That
  claim doesn't hold on master: no export preset, no CI workflow, no build contents. The macOS
  work is real but lives only on the unmerged `feat/steam-playtest-page` branch. Kept the draft
  spec tables (unchanged) so they're ready to activate the moment a real macOS build exists —
  but they should not be published to Steam until then.
- Windows tables carried forward unchanged from the old draft; nothing in the codebase
  contradicted the GPU floor or the Forward+/Vulkan requirement.
