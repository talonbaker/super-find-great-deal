# Watis World — Steam image asset spec (fill-in sheet for capsule art)

Every image Steam asks for, with exact pixel sizes, format, where it appears, and what to keep
inside the "safe zone." This is the sheet to draw against — hand the finished PNGs back and they
drop straight into Steamworks (Store → Graphical Assets) and the depot.

**General rules**
- Format: **PNG** (JPG allowed for screenshots). No alpha on capsules — they must read on any
  background. RGB, sRGB.
- The **logo/wordmark must be legible at the smallest size** it's ever shown. Design the small
  capsule first, scale up — not the other way round. **There is no wordmark asset to work
  from** (re-verified against the tree 2026-09-04, STEAM-1). `Branding.cs:42` names
  `res://resources/TitleWordmark.png` as `WordmarkImagePath` and guards it with
  `ResourceLoader.Exists`; the file is not in the tree, so every screen falls back to the text
  wordmark. That fallback is deliberate and `Branding.cs` says at length not to fake the asset.
  *(This bullet cited `Branding.cs:25` until 2026-09-04; line 25 is now `Wordmark` itself.)*
  Capsule art is therefore purpose-drawn from scratch. **The name no longer blocks it** — BRAND-1
  landed `Wordmark = "Watis World"` on 2026-09-04, so the text to set is settled; what is still
  open is the Steamworks Store Name field, which is a partner-site value. See
  `2026-08-29-STEAM-STATE-private-playtest.md` §2.
- Steam overlays a **"Coming Soon" / discount flags** on the top-left of capsules; keep that
  corner clear of critical art or text.
- No screenshots-of-UI as capsule art, no marketing copy baked in beyond the title/tagline, no
  Steam trademarks. (Valve rejects capsules that violate these.)

## Capsules (store browsing)

**Corrected 2026-08-30 — see the correction section below.** The sizes below are current;
the struck-through value after each is the legacy (pre-2018) size this file wrongly carried
until today, kept visible rather than erased.

| Asset | Size (px) | Where it shows | Notes / safe zone |
|---|---|---|---|
| **Small Capsule** | 462 × 174 ~~(231 × 87, legacy)~~ | Search results, tag pages, top-sellers rows | Logo must be readable here. Title only; no fine detail. |
| **Header Capsule** | 920 × 430 ~~(460 × 215, legacy)~~ | Top of the store page, wishlist, cart, friend activity | The primary capsule. Title + key art. Keep title clear of the top-left flag corner. |
| **Main / Large Capsule** | 1232 × 706 ~~(616 × 353, legacy)~~ | Front-page features, daily deals, "specials" carousels | Title + hero art. Highest-visibility slot. |
| **Vertical Capsule** | 748 × 896 ~~(374 × 448, legacy)~~ | Autumn/feature sales, some carousels | Portrait composition; recompose art, don't just crop the header. |
| **Page Background** | 1438 × 810 | Behind the store page (heavily darkened/blurred by Steam) | Atmospheric, low-contrast; edges fade. Don't put anything readable here. |

## Library assets (owner's library — matters for testers once they have access)

| Asset | Size (px) | Where it shows | Notes |
|---|---|---|---|
| **Library Capsule** | 600 × 900 | Library grid (the box-art tile) | Portrait key art + logo. Testers see this constantly. |
| **Library Hero** | 3840 × 1240 | Banner across the top of the library page | Wide key art, **no logo** (logo is a separate overlay). Center-safe: keep focus in the middle ~50%. |
| **Library Logo** | up to 1280 × 720, transparent PNG | Overlaid on the hero | Wordmark only, transparent background. Specify anchor (e.g. bottom-left) in Steamworks. |

## Screenshots

| Asset | Size (px) | Count | Notes |
|---|---|---|---|
| **Screenshots** | 1920 × 1080 (16:9) | **5 minimum**, up to ~10–15 | Real in-game frames. A separate capture pass handles populating `docs/store/screenshots/` — this spec doesn't touch that directory. Lead with the most legible frame — Steam uses the first as a fallback thumbnail. Consider one frame showing the day/night sky, now that the cycle is real and shipped. |

## Trailer (deferred, but the spec for when you make one)

| Asset | Spec | Notes |
|---|---|---|
| **Trailer video** | 1920 × 1080, H.264 MP4, ≥ 30 fps | Optional for a playtest but the single biggest conversion lever. First ~5s must show gameplay. |

## Priority order for a playtest page

If drawing all of it at once is too much, this order gets a credible page up fastest:
1. **Header Capsule (920×430)** — required for the page to exist.
2. **Small Capsule (462×174)** — required for search.
3. **Library Capsule (600×900)** — testers stare at it.
4. Screenshots — handled by the separate capture pass, no drawing needed.
5. Library Hero + Logo, then Main/Vertical/Background as time allows.

## Changes from the 2026-07-24 draft

- Noted the real wordmark (`resources/TitleWordmark.png`) and blurred island/palm-tree backdrop
  (`resources/MenuIslandBackdrop.png`) that shipped via PR #65 — neither existed when the old
  draft was written, and both are a legitimate starting reference for capsule composition.
- Added a day/night-sky screenshot suggestion, since that system is now real (see
  store-description.md).
- Removed the old draft's screenshot capture how-to (F12/Snip/Game Bar instructions) from this
  file — that belongs in `docs/store/screenshots/README.md`, which a separate, parallel
  screenshot-capture pass owns; this file is left untouched here to avoid clobbering that work.
- Sizes, formats, and the priority-order list are unchanged — nothing in the codebase bears on
  Steam's fixed asset dimensions.

## Correction, 2026-08-29

**The wordmark bullet above is wrong.** It is kept rather than deleted so the error stays
legible: `resources/TitleWordmark.png` does not exist on the trunk, checked directly rather
than inherited from another document. Whatever PR #65 was understood to have shipped, the file
is not in the tree today. `resources/MenuIslandBackdrop.png` is also no longer a useful
reference — MENU-2's dark pass replaced that look on 2026-08-29.

Everything else in this document was re-checked on the same date and still holds: the pixel
sizes, the safe zones, the design-the-small-capsule-first rule and the priority order are all
Steam-side facts that no repo change can move. This file remains the sheet to draw against.
Only the "we already have art to start from" claim failed.

## Correction, 2026-08-30

**The paragraph directly above is itself half wrong.** "The pixel sizes... are Steam-side
facts that no repo change can move" is true about Steam — Valve does own these numbers, not
this repo — but it does not follow that this file's numbers were right, and they were not.
**The four store-capsule sizes this file carried (Small, Header, Main/Large, Vertical) were
Valve's *legacy* dimensions — exactly half the current ones.** The Page Background, Library
Capsule, Library Hero and Library Logo rows were already correct; only the four
store-browsing capsules were stale.

Measured directly from PSD canvas headers (height then width, big-endian `uint32`, at byte
offsets 14 and 18 of the file — read with a script, not eyeballed) in the Steam asset
template pack Talon supplied 2026-08-30, at `C:\Users\talon\Pictures\Steam Game Templates`.
That pack is **not vendored in this repo**; the path above is where it lives on Talon's
machine. Every PSD in the pack was measured, not just the four in question — see the
template index below.

| Asset | This file said | Actual current size | Legacy size (exactly half) |
|---|---|---|---|
| Small Capsule | 231 × 87 | **462 × 174** | 231 × 87 |
| Header Capsule | 460 × 215 | **920 × 430** | 460 × 215 |
| Main / Large Capsule | 616 × 353 | **1232 × 706** | 616 × 353 |
| Vertical Capsule | 374 × 448 | **748 × 896** | 374 × 448 |

Every legacy number is exactly half its current counterpart — identical aspect ratio — which
is why the error was invisible by inspection: art composed at the legacy size looks correct,
just soft once Steam displays it at the real size it's requested at. Valve ships the legacy
numbers in a subfolder of the pack literally named `legacy templates`; this file's wrong
numbers matched those folders, not the current top-level templates sitting one level up from
them.

**Filename trap, so the next person does not re-trip it:**
`bundle assets/store_capsule_small_464x174.psd` — Valve's own filename claims 464×174. Its
measured PSD canvas is **462×174**. The canvas is what Steam actually reads; the filename is a label Valve got
wrong by two pixels on its own template. Take the header, never the name, for every asset in
the pack — this file's own wrong numbers survived one full correction pass (2026-08-29) by
being repeated with confidence rather than re-derived from a source.

The two placeholder PNGs in this directory were also cut to the legacy sizes. They have been
re-cut to the current sizes and renamed (`capsule-header-PLACEHOLDER-920x430.png`,
`capsule-small-PLACEHOLDER-462x174.png`); the legacy-sized files and their `.import`
sidecars are removed. See the outbox report for how the re-cut was verified.

The design-the-small-capsule-first rule, the safe zones, and the priority order remain
correct as *directions* — the ordering and reasoning in this file were never sizes, and
nothing above disturbs them. Only the numbers were stale.

## Template index — Talon's Steam asset template pack

Talon's pack (`C:\Users\talon\Pictures\Steam Game Templates`, supplied 2026-08-30) is **not
vendored in this repo** — draw against the file at that path, using the canvas measured
below rather than the filename (see the filename trap above). Paths are relative to the pack
root.

| Asset (spec row) | Template file | Measured canvas |
|---|---|---|
| Small Capsule | `store page assets\store_capsule_small.psd` | 462 × 174 |
| Header Capsule | `store page assets\store_capsule_header.psd` | 920 × 430 |
| Main / Large Capsule | `store page assets\store_capsule_main.psd` | 1232 × 706 |
| Vertical Capsule | `store page assets\store_capsule_vertical.psd` | 748 × 896 |
| Page Background | `store page assets\store_page_background.psd` | 1438 × 810 |
| Library Capsule | `library assets\library_capsule.psd` | 600 × 900 |
| Library Hero | `library assets\library_hero.psd` | 3840 × 1240 |
| Library Logo | `library assets\library_logo_transparent.psd` | 1280 × 720 (transparent) |

A second, near-identical set of the same five store-page templates (small/header/main/
vertical/background) also ships under `bundle assets\` — for a Steam *bundle's* store page
rather than a single game's page. Measured dimensions are identical to the row above in every
case; not listed twice.

### In the pack, not in this spec — not required for a private playtest page

These ship in Talon's pack but nothing in this spec currently asks for them. Listed here so
the pack's contents are legible from this file; **this is not a work queue**, and none of it
blocks a playtest page:

| Asset | Template file | Measured canvas |
|---|---|---|
| Community / Client Icon | `Community and Client Icons\shortcut_icon_256x256.psd` | 256 × 256 |
| Broadcast (left panel) | `broadcast assets\store_broadcast_left.psd` | 199 × 433 |
| Broadcast (right panel) | `broadcast assets\store_broadcast_right.psd` | 199 × 433 |
| Event Cover | `event assets\event_cover.psd` | 800 × 450 |
| Event Header | `event assets\event_header.psd` | 1920 × 622 |
| Bundle Header | `bundle assets\store_capsule_header_bundle_1414x464.psd` | 1414 × 464 |

**One more file found and not placed above:** `library assets\library_header.psd` (920 ×
430 current, 460 × 215 in its own `legacy templates` subfolder) — an older Steamworks
"Library Header" asset, dimensionally identical to the store Header Capsule but distinct
from Library Hero and not mapped to any row in this spec. Its current relevance to
Steamworks is unclear from the pack alone; flagged rather than guessed at. See the outbox
report's Open questions.
