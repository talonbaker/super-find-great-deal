# Watis World — Steam store text (private pre-alpha network/performance test)

> **Voice check (updated 2026-07-25 per Talon):** this is **not a game preview**. There is no
> game yet. This is a **private pre-alpha test of the multiplayer foundations** — connection
> stability, performance, and networking — shared with a small invited group, not the public.
> Do not write marketing copy ("mess around with friends," feature-pitch bullets). Do not
> imply a "Coming Soon" release — there is no release, no date, nothing being announced.
> Every line below leads with *what is being tested*, not what's fun to do. The current
> implemented systems are listed only as context a tester needs, never as a pitch.
>
> **Correction, 2026-08-29 — the caveat this note used to carry is now false.** It read: *"no
> `TidalDeath`, `WaterLevel`, `Drown`, or `RisingTide` class exists in `scripts/` as of
> 2026-07-25."* **Drowning ships.** `RespawnService.cs:28` defines `RespawnCause.Drowned`;
> `RespawnService.cs:210` calls `ServerKill` with it; the threshold is
> `Water.WaterGeometry.DrownAfterSec` = **3.0 s submerged**, owned by the water contract and
> adjudicated on the server. The paragraph existed to keep this page honest about not
> overselling, and it now does the opposite — it under-sells what a tester will actually meet,
> and a tester who drowns without warning will report it as a bug. The voice rule stands; the
> factual claim does not.

---

## Store name

**Watis World** *(working name — this is a private infrastructure test, not a
product name reveal)*

---

## Short description (≤ 300 chars)

Steam field: *Short Description.*

```
Private pre-alpha test build. There is no game here yet — this build exists to validate
multiplayer connection stability, performance, and networking under real conditions with a
small invited group. If you have access, thank you: report anything that lags, desyncs,
disconnects, or crashes. That's the entire point of this build.
```

---

## About This Game (Steam BBCode)

Steam field: *About This Game.* Paste as-is; Steam renders the BBCode.

```
[h2]This is a private infrastructure test — not a game[/h2]
There is no game here yet. This build exists for exactly one reason: to validate the
multiplayer foundations — connection, performance, and networking — with real people on real
hardware before anything is built on top of them. You were invited because we need that data
from you specifically, not because there's something to preview.

[h2]What this build is testing[/h2]
[list]
[*][b]Performance.[/b] Frame rate, stutters, load times — on your actual machine, not a dev box.
[*][b]Connection & networking.[/b] Host/join, reconnect after a drop, desync, lag between players.
[*][b]Crash-free operation.[/b] If it crashes, that is the single most valuable thing you can report.
[/list]

[h2]What's implemented right now (context, not a pitch)[/h2]
So you know what you're looking at while you test the above:
[list]
[*]Online host/join with proximity voice chat.
[*]Grab/carry/throw physics on networked props.
[*]A day/night cycle.
[*]Six placeholder player avatars.
[*]Water you can drown in. Stay under for three seconds and you die and respawn. It is
server-adjudicated, it is intentional, and it is not a bug — report it only if it fires when
you were not submerged.
[/list]
None of this is finished art or final design — it exists only as load on the systems being tested.

[h2]How to report[/h2]
The build asks a quick, skippable survey when you quit. Beyond that, tell us directly:
[list]
[*]What you were doing when something went wrong.
[*]Whether multiplayer stayed smooth — lag, desync, disconnects, rejoining.
[*]Any crash, and what led up to it.
[/list]
[i]<Talon: drop your feedback channel here — a Steam Community thread, a Discord invite, or a form link.>[/i]
```

---

## Bullets (context list, NOT a marketing shortlist — do not use as capsule pitch copy)

If Steamworks forces a short bullet field, use these — they describe scope, not appeal:

- Private pre-alpha — connection/performance/networking test, invited testers only
- No public release, no date, no game preview
- Implemented so far: online host/join, proximity voice, carry/throw physics, day/night cycle, six placeholder avatars
- Your report of what broke is the only deliverable that matters

---

## Changes from the 2026-07-24 draft

- Added the day/night cycle bullet (`CycleDriver.cs` / `DayNightSky.cs`, merged via PR #65 —
  didn't exist when the original draft was written).
- *(2026-08-23 correction, re-checked 2026-09-04: the entry below is a dated record of the
  2026-07-24 draft and keeps its historical name. It is no longer true — `Branding.Wordmark` is
  `"Watis World"` in `scripts/ui/Branding.cs:25` today, and `Branding.Studio` is
  `"Great-Grand-Software"` at line 36. The 2026-08-23 wording of this note said `"WORKING
  TITLE"`; BRAND-1 replaced that on 2026-09-04.)*
- Softened the "working title" framing on the name — `Branding.Wordmark = "TIDE"` is now
  actually compiled into the build (window title, main menu, title screen), so it isn't
  provisional in the same way it was on 2026-07-24. The tide-mechanic honesty caveat still
  stands and still governs the About text.
- Added a line noting the in-game exit feedback survey (`FeedbackPanel.cs`) and first-launch
  playtest foreword (`PlaytestForewordPanel.cs`) — both real and shipped, not in the old draft.
- Specified "six" creatures (roster confirmed in `AvatarVisual.cs`: puffling, gruffling,
  gruffling_fem, fluffling, snuffling, huffling) instead of the old draft's vaguer "a cast of."
- Re-confirmed the central honesty claim — no tide/drowning mechanic exists in `scripts/` as of
  this pass. Everything else (voice, carry/throw, co-op) still checks out against current code.

## Reframed 2026-07-25 (Talon, direct instruction)

Rewritten top-to-bottom: this is explicitly **not** a game-preview playtest — it's a **private
pre-alpha test of connection/performance/networking foundations**, shared with a small invited
group, with no public release and no "Coming Soon" framing. The previous pass's "small co-op
sandbox to mess around in with friends" pitch language is gone; every section now leads with
what's being validated (performance, connection/networking, crash-free), and the implemented
systems (voice, carry/throw, avatars, day/night) are listed only as context, never as a hook.
See `store-metadata.md`'s matching change for the Release status / visibility update.

## Corrected 2026-08-29 — and this document still needs a real rewrite

Two false claims were fixed in place (see the voice-check block and the implemented-systems
list): the drowning caveat, and the omission of drowning from what a tester meets. The
`Branding.Wordmark = "TIDE"` line in the changelog above is also stale — it is
`"Watis World"` now, verified at `Branding.cs:25` on 2026-09-04. *(This paragraph said
`"WORKING TITLE"` at `Branding.cs:19` until STEAM-1 re-checked it against the tree; BRAND-1
had since landed the real name and the file had grown by six lines.)*

**What is deliberately NOT done here, and why.** The body text describes a build from
2026-07-25. The trunk a tester would actually download is the Bubble Test, and the gap between
the two is not a caveat — it is a rewrite. That rewrite is held, on purpose, for two reasons:

1. ~~**The name is unresolved.**~~ **Resolved 2026-09-04 (BRAND-1): the name is `Watis
   World`,** in canon and in the build. What is left is the Steamworks **Store Name** field,
   which is a partner-site value and Talon's, not a repo one
   (`2026-08-29-STEAM-STATE-private-playtest.md` §2). This reason no longer holds the rewrite.
2. **Talon's playtest and build notes are pending.** Copy that describes the tester experience
   is exactly what those notes will redirect. Writing it first is how you write it twice.

Neither of those blocks a *private* playtest — invited testers do not browse to a page. Do the
rewrite when the name resolves and the notes have landed, in that order, and do it against the
build rather than against this file.
