# Watis World — Steam store metadata & setup (playtest build)

Everything Steamworks asks for on the store-page / app-admin side, filled where it can be and
flagged `<Talon: ...>` where a human decision or account action is required.

## Recommended vehicle: Steam Playtest, set to PRIVATE (invite-only)

**Updated 2026-07-25 per Talon: this is a private pre-alpha test, not a public playtest.** There
is no "Coming Soon," no announced release, no product being previewed — this exists solely to
validate multiplayer connection stability, performance, and networking with a small invited
group. Use Steam's **Playtest** feature, but configure its **visibility as Private/invite-only**
(Steamworks → your app → Playtest → Playtest Settings → restrict to a Friends/invited-users
list), NOT the public "Request Access" button a normal playtest shows on a discoverable store
page. Testers should receive access directly from you (Steam friend invite or a key), not find
this by browsing Steam.

- The Playtest is still a **separate, free app** automatically linked to the main app, with its
  **own App ID and own depot** — the Windows build in this repo uploads to the *Playtest* App ID.
- Because it's private, most of the storefront fields below (tags, categories, capsule art)
  matter far less than they would for a real listing — fill them honestly if Steamworks requires
  something non-empty to save the page, but don't invest real effort in marketing polish for a
  page invited testers will barely look at before clicking through.

**App ID / Depot ID (provided by Talon 2026-07-25):**
- Playtest App ID: **4951240** ("trill")
- Client Depot ID: **1718371**
- These are the `-AppId` / `-DepotIdClient` values for `deploy\steam\Upload-Steam.ps1` when an
  actual upload is authorized. `<Talon: still needs confirming — set Playtest visibility to
  Private/invite-only in Steamworks; the main store App ID, if different from this one, stays
  reserved for if/when there's ever an actual public release.>`

**Build gotcha (fixed 2026-07-25):** `build/windows-client/steam_appid.txt` used to contain `480`
— Valve's public Spacewar test App ID, a placeholder so the client could run and hit Steamworks
locally without a real App ID wired in. **Now swapped to `4951240`** (the real Playtest App ID)
in the current export — this only affects local identity, nothing has been uploaded.

## Basic info

| Field | Value |
|---|---|
| Store name | **Watis World** *(working name; not a product announcement)* |
| Type | Game *(Steamworks has no "tech test" app type — this is a platform constraint, not a claim that a game exists; the Playtest app links to it)* |
| Release status | **Private test — not for public release.** No "Coming Soon," no announced status. |
| Release date | **N/A — there is no release.** Leave blank / unlisted, not "To Be Announced" (TBA implies a future public date is coming; none is). |
| Developer | Talon |
| Publisher | Talon |
| Franchise | *(none)* |
| Price | Free (Playtest, private) |

## Genres / tags

Not a public listing, so these barely matter — fill only if Steamworks requires non-empty
fields to save a private Playtest page. If required, honest tags for what's actually running:

`Multiplayer` · `Online Co-Op` · `Co-op` · `Sandbox` · `Casual` · `Cute` · `Physics` ·
`Early Access`* · `Funny` · `Social`

> *Not literally an Early Access release, but the tag communicates "unfinished, evolving" to
> browsers. Drop it if you'd rather not imply the paid Early Access program.

Steam **categories** (checkboxes in app admin — these gate the feature icons on the page):

- [x] Online Co-op
- [x] Multiplayer
- [ ] Cross-Platform Multiplayer *(off — see Supported platforms below: there is currently no
  macOS build in this repo at all, let alone a verified Win↔Mac play test. Do not check this
  until a macOS export exists and cross-play is actually confirmed.)*
- [ ] Steam Cloud *(off — no `SteamRemoteStorage`/Cloud wiring found anywhere in `scripts/` as
  of this pass. `<Talon: confirm this is really out of scope for the playtest, or flag it for a
  future packet.>`)*
- [ ] Single-player *(off — this is a multiplayer test)*

## Supported platforms

| Platform | State this build |
|---|---|
| Windows (64-bit) | **Supported — built.** `export_presets.cfg` has a `WindowsClient` preset and `build/windows-client/` exists in the repo (see system-requirements.md for the measured size). This is the only real, shippable target right now. |
| macOS (Apple Silicon + Intel) | **Not present on the trunk.** Re-verified **2026-08-29**: `export_presets.cfg` holds exactly two presets, `LinuxServer` and `WindowsClient` — checked directly, not inherited from another document. Do not list macOS as supported until that work actually lands. **Branch pointer updated:** the macOS path is now **PR #342 (STEAM-MAC-1)**, open and finished in both directions, waiting on the ETC2 import-line decision and on a Mac Depot ID that only Steamworks can mint. The older reference to `feat/steam-playtest-page` and to a `.github/workflows` CI file is dead — this repo runs **no** GitHub Actions, by decision. |
| Linux | Not shipped as a client (the Linux export is the legacy dedicated-server build only — see `LinuxServer` preset). |

## Supported languages

- English (Interface / Full Audio: N/A — proximity voice is player voice, not VO / Subtitles: N/A)

## Content / maturity

Steam content survey answers (decided 2026-07-25 — obvious defaults for what's actually in the
build, no open question here):
- Violence: **None.**
- Nudity/sexual content: **None.**
- Gambling: **None.**
- User-generated in-game voice chat: **YES** — proximity voice is live player audio; Steam
  requires disclosing this regardless of content, so it's YES even though there's no moderation
  system behind it yet (private/invited testers only, so the risk profile is low).

## Legal / boilerplate

- Copyright line: `© 2026 Talon`
- EULA: **Steam's standard SSA** (Steam Subscriber Agreement) — no custom EULA. Standard default,
  changing it only matters once there's an actual product with terms worth customizing; not this.

## Changes from the 2026-07-24 draft

- **macOS downgraded from "Built + CI-verified" to "not present."** Re-checked master directly:
  no macOS preset in `export_presets.cfg`, no `.github/workflows` directory at all, empty
  `build/macos-client/`. The macOS work described in the old draft (and in commit `0be0ae4`,
  "Win build, macOS path + CI") lives only on the unmerged `feat/steam-playtest-page` branch —
  it never reached master. The Cross-Platform Multiplayer category is now unchecked outright
  rather than conditionally checked, since there's no macOS build to cross-play against yet.
- Added the `steam_appid.txt = 480` build gotcha as an explicit call-out with the exact file
  path, since it's a real trap the next upload attempt will hit.
- Name field dropped "(working title — Playtest)" — see store-description.md's name note for why.
- Everything else (Playtest vehicle recommendation, tags, content/legal placeholders) carried
  forward unchanged; nothing in the codebase contradicted it.

## Reframed 2026-07-25 (Talon, direct instruction)

This is a **private pre-alpha connection/performance/networking test**, not a public playtest —
no "Coming Soon," no release date, no product being previewed. Changed: Release status and
Release date fields now say so explicitly instead of offering a "Coming Soon" choice; added the
Private/invite-only Playtest visibility instruction (Steamworks defaults a Playtest to publicly
discoverable "Request Access" — that must be switched off); noted that storefront polish (tags,
capsule art) matters far less for a page invited testers won't be browsing. See
`store-description.md`'s matching rewrite.
