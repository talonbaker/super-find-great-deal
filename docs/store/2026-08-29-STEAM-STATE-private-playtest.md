# The Steam side, everything except the build — 2026-08-29

**Scope:** what Steam needs from you that is *not* an executable. The build and the upload are
deliberately out of scope — those live in `PLAYTEST-SETUP-CHECKLIST.md` §6 and
`deploy/steam/Upload-Steam.ps1`, and uploads stay HELD until you say go.

**Status in one line:** the Steamworks *account* work is nearly done and has been for a month;
what is actually missing is **art, a name, and one Depot ID** — and every one of those is yours,
not mine.

---

## 1. The 2026-07-25 store docs are a month stale. Here is what I re-verified against the trunk.

`docs/store/` holds five documents written on 2026-07-25. They are good and mostly still right,
but do not read them without this table. Everything in the "true now" column was checked against
`feat/2026-08-27-bubble-test` at `ac842ee1`, not against another document.

| Doc says | True now | Consequence |
|---|---|---|
| `capsule-spec.md`: *"Master now ships a real wordmark asset at `resources/TitleWordmark.png`"* | **That file does not exist.** `Branding.cs:42` names the path (`WordmarkImagePath`) and every screen guards it with `ResourceLoader.Exists`; this row cited line 25 until STEAM-1 re-checked it on 2026-09-04, and line 25 is now `Wordmark` itself; every screen falls back to the text wordmark | Not a bug — the fallback is deliberate and the comment says not to fake the asset. But you have **no wordmark to build capsules from**. |
| `PLAYTEST-SETUP-CHECKLIST.md` §5: the in-game title is *"SHRIMP TESTING: AS A WORKING TITLE"* | **`Branding.Wordmark` is `"Watis World"`** (`Branding.cs:25`), and `Branding.Studio` is `"Great-Grand-Software"` (line 36) — BRAND-1, 2026-09-04 | Item 5 is rewritten and now records the real name. *(This row read `"WORKING TITLE"` until STEAM-1 re-checked it on 2026-09-04.)* |
| `store-description.md`: *"no `TidalDeath`, `WaterLevel`, `Drown` or `RisingTide` class exists"* | **Drowning ships.** `RespawnCause.Drowned`, 3.0 s submerged, server-adjudicated | The honesty caveat that paragraph exists to make is now false, and the description under-sells what testers will actually meet. |
| `PLAYTEST-SETUP-CHECKLIST.md`: *"all 22 headless test suites green"* | 66 scene suites and 2 912 xUnit tests **on `Sail`'s tip, 2026-08-29**. **On this repo, 2026-09-04: 48 scene suites (47 PASS / 1 FAIL) and 2 288 xUnit (0 failed).** The MVP extraction is a smaller tree | Cosmetic, but it is the number a future reader would quote — and there are now two right answers depending on which repo. |
| `system-requirements.md`: macOS not listable — no export preset on master | **Still true on the trunk.** `export_presets.cfg` has only `LinuxServer` and `WindowsClient` | The macOS preset is in **PR #342, still open**. Windows-only remains correct *today*. |
| `store-metadata.md`: storage ~250 MB from a 193 MiB build measured 2026-07-25 | **Unverified.** Nothing has been re-exported since | Do not quote it on a store page without a fresh export. Flagged, not corrected. |

**None of these blocks anything.** They are listed so the next person does not inherit them the
way this session nearly inherited a retired premise.

---

## 2. ~~The thing actually blocking a store page is the name~~ — **the name landed, 2026-09-04**

> **Updated by STEAM-1, 2026-09-04.** BRAND-1 landed the real name on 2026-09-04:
> `Branding.cs:25` is `Wordmark = "Watis World"` and line 36 is
> `Studio = "Great-Grand-Software"`. **Two of the three names below have collapsed into one.**
> The name-agnostic *rule* was satisfied, not repealed — those are the two strings Talon named
> and nothing else was renamed. What survives from this section is the last bullet and the
> clearance note.

`docs/CANON.md` §2.12 records **Watis World** as the name and is explicit that
*Sleepaway Camp* is dead for legal reasons — an active remake of the 1983 film.

So there are two names in play right now, not three:

- **`Watis World`** — canon's name, what all five store docs use, and now what the game
  actually renders, everywhere. *(This section previously listed a separate `WORKING TITLE`
  here as "what the game actually renders". That has been true of nothing since 2026-09-04.)*
- Whatever the Steamworks **Store Name** field says today, which nobody in this session can see.

This does not block a *private* playtest — invited testers do not browse to a page. It no longer
blocks capsule art either: a capsule is a wordmark with a picture behind it, and the wordmark
now exists as a string (there is still no `resources/TitleWordmark.png` — see §1). **The Store
Name field is yours and I have not touched it.**

One practical note: whatever you pick needs a real clearance pass before it goes anywhere public,
not just a trademark search. That is what the title-clearance brief was for and the lesson it
recorded is that the check happens *before* art is drawn, not after.

---

## 3. What Steamworks wants from you, ranked by what is actually stopping progress

**Blocking now**

1. **The Mac Depot ID.** App Admin → SteamPipe → Depots → Add Depot. Steamworks is the only
   thing that can mint one; it cannot be invented or guessed. Without it there is no macOS
   upload path at all, and PR #342 sits open either way.
2. **The ETC2 line in PR #342.** `import_etc2_astc` false → true, 17 `.import` files,
   Windows and Linux unaffected. Godot 4.7 ships only a universal macOS template, so without it
   there is no Mac build. **Asked five times across three sessions now.** The PR is finished in
   both directions; it needs one word.
3. **Capsule art.** `capsule-spec.md` has every exact pixel size and safe zone and is still
   accurate — it is the sheet to draw against. The priority pair is Header 460×215 and Small
   231×87; design the small one first and scale up, because the wordmark has to survive it.
   The image prompts I gave you earlier this session are for exactly this.

**Not blocking, but wanted before anyone outside sees a page**

4. **Screenshots.** `docs/store/screenshots/` holds five, and they predate DARK-1's darker pass,
   MENU-2, BODY-2's box kid and the whole bubble test. Steam requires screenshots to be real
   gameplay, so these have to be captured, not generated — and they should not be shot until you
   have signed off the darker pass, or they get reshot. That sequencing is why BT-12b is parked.
5. **The feedback-channel link.** `store-description.md:70` is still a literal placeholder.
   Discord, a Steam Community thread, or a form — your call, one line to fix.
6. **Store Name field.** §2 resolved on 2026-09-04 — the name is `Watis World` — so this is now unblocked and waiting only on Talon.

---

## 3b. When the three numbers arrive: the App ID hand-off (added 2026-08-30, packet MRF-A)

**Why this section exists.** The App ID hand-off was broken in a way that could only ever fire
*on the day the real numbers landed*, so nothing on the Spacewar route could have caught it. It
is fixed in code now; what is left is a checklist of who flips what, and one decision that is
being made deliberately rather than left silent.

**What was broken, and is not any more** (`scripts/net/steam/SteamService.cs`):

- `ResolveAppId` read the env var `STEAM_APP_ID` — **a name Steam never sets**. Steam sets
  `SteamAppId` and `SteamGameId`. So a build *launched by Steam under the real App ID* resolved
  to Spacewar. It now reads Valve's names, ranked above this project's own `STEAM_APP_ID`.
- `PrepareNative` then **overwrote** `SteamAppId`/`SteamGameId` with whatever it had resolved,
  before `SteamAPI.Init()` ran — so even a correct resolution could be undone. It now refuses to
  overwrite a real App ID with the Spacewar fallback (`ShouldWriteAppIdEnv`).

**480 was and still is correct today.** The fallback was never the defect and has not changed;
`NetProfile.FallbackSteamAppId` stays its single source. The decision table is asserted
headlessly by `--steam-selftest` (`tests/Run-SteamLogicTest.ps1`), including the "Spacewar must
never clobber a real ID" case — the one that only starts mattering once this section is actioned.

> **STEAM-1, 2026-09-04 — one number has landed and rows 1–2 are deliberately NOT done yet.**
> Talon supplied App ID **4951240** and set the partner-site executable to `watis_game.exe` and
> the install folder to `watis_install_folder`. The repo now agrees with the executable name;
> the App ID went into `deploy/steam/README.md` and onto `Upload-Steam.ps1`'s command line, and
> **`FallbackSteamAppId` stays 480**. The reasoning is in that README: Spacewar fails *safe*
> (it initialises for everyone) where a real ID compiled into the binary fails `SteamAPI.Init()`
> for anyone who does not own that app; the fallback only ever runs when Steam did *not* launch
> the build; and row 2 makes it a compatibility break mid-playtest. Rows 1–2 become correct in
> one commit once it is settled which app `4951240` is — §4c retired the Playtest app as the
> vehicle, and `4951240` is the Playtest app's number. Row 3 is now enforced rather than merely
> intended: `Export-WindowsClient.ps1` fails the export if `steam_appid.txt` appears in it.

### The checklist — who flips what, when the numbers land

The three numbers are the **base app's App ID**, the **Windows client Depot ID** and the **Mac
Depot ID** (§3 — still blocking, still only Steamworks can mint them). The App ID is the only one
this section is about; the depot IDs go on `Upload-Steam.ps1`'s command line and touch no code.

| # | What | Where | Who |
|---|---|---|---|
| 1 | Replace `480` with the real App ID | `scripts/net/NetProfile.cs`, `FallbackSteamAppId` — the single source; **do not** add a second one | whoever holds the number |
| 2 | Change `GameTag` off `"mp-foundation"` | Same file, a few lines up. Its own comment says "change it with the App ID": it is the lobby-directory scope key, and leaving it is how this title's room codes stay collidable with anything else developing against the shared Spacewar ID | same person, same commit |
| 3 | Do **not** commit `steam_appid.txt` | `.gitignore:26` already blocks it (this said `:32` until STEAM-1 re-checked it, 2026-09-04). A Steam-launched build does not need it — Valve's env vars are the hand-off, and they now work. It is a local-dev convenience only | — |
| 4 | Re-run `tests/Run-SteamLogicTest.ps1` | It asserts the resolution ORDER, not the value, so it should stay green; if it does not, the change was bigger than one constant | whoever did 1–2 |
| 5 | Verify on a build Steam actually launched | The positive control, per the standing rule: launch the real build **from the Steam client** and confirm it initialises under the real App ID, not 480. A resolution path that has never once been seen to read Steam's own env has not been shown to work | Talon, on the playtest build |

### `SteamAPI.RestartAppIfNecessary` — decided deliberately, and recorded either way

Valve's `RestartAppIfNecessary` relaunches the executable *through Steam* when it was started
directly, so that Steam's env hand-off exists at all. It appears nowhere in this repo.

**Decision: correctly omitted for this playtest, and it stays omitted until the app is
released.** The reasons, in order:

1. The route Talon ruled for (§4c) is **Release Override keys on an unreleased base app**. Every
   tester launches from their Steam library, which *is* launching through Steam — the case
   `RestartAppIfNecessary` exists to repair does not arise.
2. It is a hard relaunch of the process. Getting it wrong on a dev machine — where the
   "executable" is the Godot binary, not an exported build — is a restart loop, and this repo
   runs the game headless from the CLI constantly.
3. The gap it leaves is narrow and known: someone double-clicking the exported `.exe` directly
   gets no Steam env, falls through to `steam_appid.txt` if they made one, and otherwise gets
   Spacewar. That is a developer's problem, not a tester's.

**Revisit trigger, so this is a decision and not a silence:** add it if the app is ever
*released* (a released app can be launched by shortcut, by another launcher, or by a modded
client), or if a tester is ever asked to run the executable outside Steam. It would go in
`SteamService.EnsureClient`, before `SteamAPI.Init()`, guarded so it can never fire in a
headless or CI run.

---

## 4. Making it genuinely private — the part that is easy to get wrong

The setting that controls public discoverability lives on the **main game app**, not on the
Playtest app. That split is what the checklist calls out and it is worth repeating, because
getting it wrong publishes the existence of the game rather than the build.

- Playtest app **4951240**, client depot **1718371** (yours, provided 2026-07-25).
- Leave Playtest signup on **limited**, never "Open signup" and never Friend Invites.
- On the **main app**: **Edit Store Page → Special Settings tab → "Show the Steam Playtest
  signup link on the store page for this game"** stays **not-Visible**. That is the literal
  control name and path, confirmed against Valve's own screenshot on the Playtest doc. Changing
  it requires publishing the store page, so it is not a silent flip in either direction.
- Admit testers by **Steam key**. ~~or the manual grant list~~ — **there is no manual grant
  list. That claim was false.** Valve: *"Players are selected randomly from the pool of
  signups"*, optionally filtered by country. If specific named people must get in, keys are the
  **only** documented mechanism. Corrected 2026-08-29 against
  `partner.steamgames.com/doc/features/playtest`.
- **Verify it rather than trusting the toggle**: have someone with no access try to find it. If
  they cannot see that it exists, it is private. This is the same positive-control law the test
  suites run under — a privacy setting you have never seen fail closed has not been shown to work.

**But read §4b before doing any of this.** The paragraph above makes the Playtest as private as
a Playtest can be configured to be, and Valve's own documentation says that ceiling is lower
than this section assumed.

---

## 4b. Verified against Valve, 2026-08-29 — a released Playtest app is not confidential

Everything here is quoted from Valve's own pages (`partner.steamgames.com/doc/features/playtest`
unless noted). No third-party sources.

**1. A Playtest app has to be released to be playable, and releasing surfaces it.**

> "Keys aside, details of an app on Steam will become visible to many players and/or third party
> web crawlers once the app: 1) publishes a Store page, 2) becomes available for Pre-Load or
> 3) gets Released, **even if it is released with a 'Hidden' store page.**"

Making the Playtest playable *is* completing its release process. So the Playtest route puts the
app into state (3) by construction. Valve does not enumerate which fields leak or name SteamDB;
"third party web crawlers" is its own phrase.

**2. Valve answers the confidentiality question directly, and the answer is no.**

> "Q: Is my Steam Playtest confidential? A: **No** — players signing up for a Playtest aren't
> under nondisclosure agreements with you, and there shouldn't be an expectation of secrecy."

> "a confidential playtest will only ever be as confidential as your 'least confidential'
> playtester."

**3. Valve's own recommendation for an unannounced game is to not use a Playtest app at all.**

> "if you really want to run a confidential playtest for an unannounced game (sometimes called a
> 'friends & family' alpha), **the most secure way to attempt this is via Release Override Steam
> keys on the unreleased base app (i.e. not a Playtest App).**"

Release Override packages *"are specially flagged to ignore the current release state of the
Application and make it immediately playable"*
(`partner.steamgames.com/doc/store/application/packages`) — no release, no store page, nothing
entering any of the three surfacing states. Capped at **2 500 keys** per app, which is three
orders of magnitude more than this test needs. Valve's surfacing rule opens with the words
*"Keys aside"*, which is the explicit carve-out.

**What this does to §6's reasoning.** §6 concluded "keep the Playtest app" and named the failure
mode precisely — *"the game's existence became public before you chose to make it public, and
that one cannot be undone."* It then chose the route that Valve documents as causing exactly
that. Two of its three supporting arguments do not survive:

| §6 argued | Verified |
|---|---|
| A zip cannot exercise `SteamLobby`/`SteamPeer`/`SteamService`, so the vehicle must be a real Steam app | **Still true, and still decisive.** But Release Override keys on the base app are also a real Steam app identity with real lobbies — this argument rules out the *zip*, not the base app. |
| The Playtest app is "already provisioned", so switching would spend real effort | **Weak.** `Upload-Steam.ps1` takes `-AppId` and `-DepotIdClient` as parameters (`Upload-Steam.ps1:26-27`). Switching is two different numbers on one command line, not a rewrite. |
| Keeping it saves configuration already paid for | **Inverted.** The Playtest route needs a *second* app released, reviewed and configured, plus the two-app privacy split that §4 exists to stop you misconfiguring. The base-app route needs neither. |

**This is a fork for Talon and it is not resolved here.** Both routes exercise Steam networking,
which is the thing under test. The Playtest route costs an app record that surfaces on release
and cannot be un-surfaced; the base-app route costs the Mac path's assumptions and whatever the
base app's own depot situation is. It is ripe now because it has to be decided **before** the
first upload, and it is irreversible in one direction only.

**One consolation: nothing has been uploaded, so the window is still open.** The Playtest app
exists but the surfacing rule triggers on release, not on creation.

---

## 4c. THE RULING, 2026-08-30 — unreleased base app, private keys, no Playtest app

**Talon:** *"I would like an unreleased playtest page with private keys."*

**That resolves the §4b fork, and it lands on the route Valve documents as the confidential
one.** Recorded here in mechanism terms because the phrase "playtest page" can be read two ways
and only one of them is private.

### What was chosen

**Release Override Steam keys on the unreleased base app.** Valve's own words:

> "if you really want to run a confidential playtest for an unannounced game (sometimes called a
> 'friends & family' alpha), **the most secure way to attempt this is via Release Override Steam
> keys on the unreleased base app (i.e. not a Playtest App).**"

Release Override packages *"are specially flagged to ignore the current release state of the
Application and make it immediately playable"* — so the app is **never released**, publishes **no
store page**, and enters **none** of the three states that surface an app. Valve's surfacing rule
opens with the words *"Keys aside"*, which is the explicit carve-out this route sits in.

### What was NOT chosen, and why it matters that it wasn't

**The Playtest app (4951240) is not the vehicle.** A Playtest must be **released** to be playable,
and Valve: details become visible to players and third-party crawlers once an app *"gets Released,
**even if it is released with a 'Hidden' store page.'"* That is irreversible. Valve also answers
the question outright — *"Q: Is my Steam Playtest confidential? A: **No**"*.

**Do not read this ruling as "configure the Playtest app more privately".** The §4 checklist makes
a Playtest as private as a Playtest can be configured to be, and that ceiling is still below what
was asked for. §4 now applies only if this ruling is ever reversed.

### What it changes in practice

| | |
|---|---|
| App | The **base app**, not Playtest app 4951240 |
| Release | **Never released.** No store page published, no pre-load |
| Access | **Release Override keys only**, capped at 2 500 per app — three orders of magnitude more than needed |
| Store page | Not required, and not to be published |
| `Upload-Steam.ps1` | Takes `-AppId` / `-DepotIdClient` as parameters (lines 26–27), so this is **two different numbers on one command line**, not a rewrite |
| The Mac Depot ID | Still needed for a Mac build, still only Steamworks can mint one |

### The two things this route does NOT protect against

Stated plainly, because a privacy decision that oversells itself is worse than none:

1. **Testers are not under NDA.** Valve: *"a confidential playtest will only ever be as
   confidential as your 'least confidential' playtester."* Keys control *access*, not *silence*.
2. **The tester's own machine can leak it.** Third-party overlays and web extensions surface
   whatever a Steam client is running, *"even unreleased or unannounced games"*, and some prompt
   the player for details about the unrecognised title. Valve's mitigation is a **nondescript
   codename** on the app until announcement — which pairs neatly with the name being unresolved
   anyway (§2).

### Still verify rather than trust the toggle

Unchanged from §4 and it still applies: **have someone with no access try to find it.** A privacy
setting you have never seen fail closed has not been shown to work. That is the same positive-
control law the suites run under.

### Consequence for the AI-disclosure question (§5)

Softer than it looked. The disclosure surfaces on a **store page**, and this route publishes none
— so nothing is displayed to anybody during the playtest. It becomes live only if the game is
later released or a page goes up, and Valve allows the survey to be edited freely **before**
approval. The §5 finding stands: there is no published Valve requirement to declare AI-generated
*capsule art*, and no exemption either.

---

## 5. AI content disclosure — verified 2026-08-29, and the answer is not the one this section gave

~~If you ship AI-generated capsule art, that is pre-generated content and it gets declared.~~
**That was stated with more confidence than Valve's documentation supports.** What is actually
true:

**The field.** Valve calls it the **Content Survey**; its third section is headed *"3) Generative
Artificial Intelligence Content"*. It is completed before submitting the store page and build for
review. **Valve publishes no App Admin navigation path for it** — the docs place it only as a
pre-review obligation and a release-checklist item, so do not expect to find it from a written
path.

**The split, in Valve's words** (`partner.steamgames.com/doc/gettingstarted/contentsurvey`):

> "**Pre-Generated:** Any kind of content **that ships with your game and is consumed by players**
> that is created with the help of AI tools during development."

> "**Live-Generated:** Any kind of content created with the help of AI tools while the game is
> running." — this one carries an extra duty: you must describe your guardrails against illegal
> output.

**Does capsule art have to be declared? Valve does not say, and the two Valve sources disagree.**

- The **documentation** scopes it twice to content *"that ships with your game, and is consumed
  by players"*. A store capsule does neither. On the doc's own words, capsule art is out of scope.
- The **2024-01-10 policy announcement** defines Pre-Generated with no such clause — *"Any kind
  of content (art/code/sound/etc) created with the help of AI tools during development"* — which
  would cover it. Valve never withdrew that announcement and has never reconciled the two.
- The **Store Graphical Asset Rules** page contains **zero** occurrences of "AI", "artificial" or
  "generative". Nine other Steamworks pages were checked the same way, all zero.

So: **there is no published Valve requirement to declare AI-generated capsule art.** There is
also no exemption, the survey field is free text, and Valve reserves a general judgment right —
*"Products on Steam must adhere to the content rules, regardless of whether it is disclosed in
these surveys."* The honest position is that declaring it is safe and cheap, and *not* declaring
it is defensible on the documentation's literal wording. It is your call, not a rule.

**What surfaces publicly.** Not a badge and not a checkbox — your own prose. Steam renders a
block headed **"AI Generated Content Disclosure — The developers describe how their game uses AI
Generated Content like this:"** followed by your free text verbatim, sitting between About This
Game and System Requirements. Verified on a live store page, not just in the docs.

**Where it surfaces for a playtest.** On the **main app's** page, necessarily — *"Your playtest
will not have its own unique store page, it will only show up as a section on the base game's
page."* **Whether a Playtest app owes its own Content Survey, Valve does not say.** The Demos doc
states a per-app survey duty explicitly; the Playtest doc never mentions the survey at all, and
describes its review as *"only… capsule images and icons"*. That asymmetry is suggestive and is
not a statement.

**Amending later.** Free before approval. After approval, *"some questions in the survey cannot
be edited without first contacting Steam Support"* — Valve **does not name which questions**, and
does not say whether the AI section is among them. No stated penalty for amending. So the "get it
right the first time" instinct in the old text was sound even though its premise was overstated.

**Revision date: none published.** Steamworks docs carry no last-updated stamps. The only dated
artifact is the 2024-01-10 announcement, body last edited 2024-01-12. No later Valve announcement
revising the AI policy was found.

Worth knowing regardless: none of this touches the repo's own trailer path, which is Blender
previz plus in-engine capture and has no AI generation in it.

---


## 6. Is Steam Playtest even the right vehicle? — the thought, since you asked for one

> **RULED 2026-08-30 — this section's conclusion is REVERSED. Read §4c below.** Talon:
> *"I would like an unreleased playtest page with private keys."* That is the base-app Release
> Override route, not the Playtest app, and it is the one Valve itself recommends for an
> unannounced game. The reasoning below is kept rather than deleted because the *failure mode it
> names* — "the game's existence became public before you chose to make it public, and that one
> cannot be undone" — is exactly right, and is why the ruling went the other way.

For a handful of invited testers, a separate Playtest app is heavy: its own App ID, its own
depot, its own store-page fields, and a privacy configuration split across two apps that is easy
to misconfigure. The obvious cheaper options are a **password-protected beta branch** on the main
app, or simply zipping the build and handing it over.

**Take the Playtest app anyway, and the reason is not distribution.**

`scripts/net/steam/` contains `SteamLobby`, `SteamPeer` and `SteamService` — there is a real
Steam networking and lobby path in this build. That code cannot be exercised by a zip. It needs a
genuine Steam app identity, a real lobby, and real people on real machines with real NAT between
them. The multiplayer foundation *is the thing under test*, so the delivery mechanism has to be
the one the shipping game would use. A zip would test the game and quietly skip the layer you
most need evidence about.

The password-beta-branch alternative would also exercise Steam, and it is genuinely simpler. The
reason to prefer the Playtest app is that it is **already provisioned** — App ID and depot exist,
`Upload-Steam.ps1` is written against them, and the macOS work in #342 assumes them. Switching
now would spend real effort to save configuration you have already paid for.

So: keep the Playtest app, and spend the saved attention on §4 instead — because the failure mode
here is not "the test was inconvenient", it is "the game's existence became public before you
chose to make it public", and that one cannot be undone.

---

## 7. Split of work

**Yours, and nobody can do them for you:** the name; the Mac Depot ID; the ETC2 answer; drawing
or generating the capsule art; the feedback-channel link; the Steamworks toggles in §4; the AI
disclosure (now a judgment call, not a rule — see §5); **the §4b vehicle fork, which is new and
has to be settled before the first upload**; and the go-ahead for any upload.

**Mine on your word:** correcting the six stale claims in §1 across the five store docs; rewriting
`store-description.md` now that drowning and the bubble test exist; capturing real screenshots
once you have signed off the look; running the upload.

**Done since this was written (2026-08-29):** the six stale claims are corrected across all five
docs on `docs/2026-08-29-store-1-stale-claims`, each checked against the trunk rather than
against another document. The `store-description.md` rewrite is **deliberately not done** — the
name is unresolved and Talon'''s playtest notes are pending, so writing tester-facing copy now
means writing it twice; the reasoning is recorded in that file. §4, §5 and §6 of this document
have themselves been corrected against Valve'''s own pages.

---

## 8. What this document does not claim

- The build size has **not** been re-measured; the 193 MiB figure is from 2026-07-25.
- The Steamworks **Store Name** field, the current Playtest visibility toggles and the AI-survey
  wording were **not** inspected — I have no Steamworks access and did not guess at them.
- Whether a Mac tester's Gatekeeper experience is acceptable is recorded in PR #342 as one
  written sentence and is **unverified on hardware**; no Mac was available.
- No upload has happened, and none will without an explicit go-ahead.
