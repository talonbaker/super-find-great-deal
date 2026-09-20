# Private Steam Playtest — setup checklist

Everything left to do before the private playtest can actually go out, in the order it needs to
happen. This exists so none of it has to be held in your head or re-derived from chat history.
Nothing here has been done automatically — every box is a real action on your end (Steamworks
account, drawing, or a go-ahead for me to run something).

## 1. Steamworks: Release Override keys on the **unreleased base app**

**Corrected 2026-08-30. Authority: `2026-08-29-STEAM-STATE-private-playtest.md` §4c — Talon's
ruling, *"I would like an unreleased playtest page with private keys."*** The vehicle is the
**base app, never released**, admitted by **Release Override Steam keys**. There is **no store
page** on this route and there is **no Playtest app**.

> **IDs CORRECTED 2026-09-05 — read this before using any number below.** `4951240` is the BASE
> app, named `trill` (Type: Game). The Playtest/Beta app is **`Watis World`, AppID `5016210`**, and
> its Windows client depot is **`5016211`** (`trill Playtest Content`). Depot `1718371` belongs to
> another app; using it produced `Access Denied` on a real upload. Talon's ruling to ship to the
> Playtest app is unchanged — it targets **5016210 / 5016211**. Every "4951240" below that is
> called the Playtest app is wrong; see `deploy/steam/README.md` for the corrected table.

**SUPERSEDED 2026-09-05 — Talon has settled it: the Playtest app IS the vehicle.**
His ruling, asked directly and with the Valve research below put in front of him first:
*"Not the base app. I only want playtest app."* The upload therefore targets
**`-AppId 4951240 -DepotIdClient 1718371`**, the Playtest app and its client depot he supplied
on 2026-07-25. The 2026-08-30 ruling under it is now history; read it as the cost, not the plan.

**What that decision costs, so it stays on the record.** A Playtest must be *released* to be
playable, and Valve says details surface to players and third-party crawlers once an app gets
released *"even if it is released with a 'Hidden' store page"* — irreversible. Valve also answers
the confidentiality question outright: *"Q: Is my Steam Playtest confidential? A: **No**."*
Valve's own recommendation for an unannounced game is Release Override keys on the unreleased
base app. **Talon was shown all of this and chose the Playtest app anyway.** This is an informed
reversal, not an oversight, and it is not to be "corrected" back by anyone reading §4c later.

*(The two-app privacy split described in `2026-08-29-STEAM-STATE-private-playtest.md` §4 —
Playtest signup type, the main app's "Special Settings" toggle — is BACK IN SCOPE under this
ruling. It was written for exactly this route.)*

- [x] **Which app is which: SETTLED from the partner site, 2026-09-05.** `4951240` is the BASE
      app, named `trill` (Type: Game). The Playtest/Beta app is **`Watis World`, AppID
      `5016210`**, and its Windows client depot is **`5016211`** ("trill Playtest Content").
      Depot `1718371` belongs to another app and produced `Access Denied` on a real upload.
- [x] **First upload done, 2026-09-05: BuildID `25145262`** to 5016210 / 5016211, 187.8 MB.
- [ ] **Set the build live**: SteamPipe → Builds → BuildID `25145262` → choose a branch →
      **Set Build Live**, then Publish if the page asks. **Uploading alone does not make it
      installable** — the depot exists but nothing will pull it until a build is live.
- [ ] Keep Playtest signup on **limited**. Never "Open signup", never Friend Invites.
- [ ] On the **main app**: *Edit Store Page → Special Settings → "Show the Steam Playtest signup
      link on the store page for this game"* stays **not-Visible**. Changing it requires
      publishing the store page, so it cannot flip silently in either direction.
- [ ] Admit testers by **Steam key** — there is no manual grant list; Valve selects signups
      randomly from the pool.
- [ ] **Do not release the base app and do not publish a store page.** Both are surfacing states.
- [ ] App Admin → **Steam Keys** → request **Release Override** keys (capped at 2 500 per app;
      three orders of magnitude more than this test needs) and hand them out directly.
- [ ] Consider a **nondescript codename** on the app until announcement — Valve's own mitigation
      for third-party overlays that surface what a Steam client is running. It pairs with the
      name being unresolved anyway.
- [ ] Verify it, don't just trust the toggle: have someone who hasn't been given a key try to
      find it. If they can't see it exists, it's actually private.
- [ ] Know what this does **not** protect against: testers are not under NDA, and *"a
      confidential playtest will only ever be as confidential as your 'least confidential'
      playtester."*

## 2. Create the Mac Depot **on the base app**

Needed before I can upload the macOS build — SteamPipe requires a real Depot ID that only
Steamworks can hand out, I can't invent one. It blocks the *upload* only: the macOS build itself
exports and verifies without it (see §6).

- [x] **Superseded 2026-09-05.** The Mac depot already exists on the Playtest app: **`5016212`**
      ("trill Playtest Depot mac os"). It carries a partner-site warning — *"this depot isn't
      referenced by any packages for this app"* — so a build uploaded to it would reach nobody
      until that package configuration is fixed. Fix that before attempting a macOS upload.

## 2a. ~~One ruling I need from you~~ — **RULED AND DONE, 2026-08-30**

**Talon, 2026-08-30:** *"I need both mac and PC. I need this to work for both for this playtest
specifically testing connection between both platforms."* A macOS build is a requirement, so the
setting below is **on**. `project.godot` now reads `import_etc2_astc=true` and the reimport is
committed. **Nothing is owed here any more** — kept for the reasoning, and because the measured
cost belongs on the record.

- [x] Godot 4.7 ships exactly one macOS export template and it is a **universal** (Intel + Apple
      Silicon) binary — there is no Intel-only one to fall back to. Godot refuses to build
      universal unless `rendering/textures/vram_compression/import_etc2_astc` is on in
      `project.godot`, and that line is currently **off**, with a comment beside it citing
      `ART-BIBLE.md` §4.4 ("desktop compression only"). Turning it on adds a second,
      mobile-format compressed copy of the **17** textures in the tree that use VRAM compression;
      Windows and Linux go on selecting the desktop format exactly as they do now, so the Windows
      build is unaffected. **Without that one line there is no macOS build at all.** It is a
      one-word ruling and a one-line change — `deploy\Export-MacClient.ps1` used to stop with
      this exact explanation rather than flipping a guarded setting on its own.
- **What it actually cost, measured 2026-08-30 (W7-7):** the exact file count, the byte delta and
  before/after captures of the affected textures are in
  `docs/agents/roles/programming/outbox/2026-08-30-W7-7-the-mac-build-path.md`. The
  `ART-BIBLE.md` §4.4 guard was **measured, not overridden** — if the visual cost is ever judged
  too high, the evidence to judge it by is in that report.

## 3. Fill in the feedback channel link

- [ ] `docs/store/store-description.md:70` still has a literal placeholder. Decide: Discord
      invite, a Steam Community thread, or a form link — then either edit it yourself or tell me
      which and I will.

## 4. Capsule art — **no longer a blocker** (corrected 2026-08-30)

- [ ] ~~Draw the real capsule art before the playtest.~~ **Not required on the ruled route.**
      Capsule art is store-page art, and §4c's route **publishes no store page at all** — nothing
      is displayed to anybody during the playtest. Header (920×430) and Small (462×174) stay the
      priority pair *whenever a page eventually goes up*; sizes and guidance in
      `docs/store/capsule-spec.md`, and `capsule-header-PLACEHOLDER-920x430.png` remains a
      functional stand-in. Moved off the critical path, not deleted.
      (Corrected MRF-B / F14, 2026-08-30: this line named `capsule-header-PLACEHOLDER-460x215.png`
      alongside the 460×215 / 231×87 pair. Both halves were stale for one reason — #352 re-cut the
      two placeholders from the LEGACY sizes to 920×430 and 462×174, renamed them, and deleted the
      old files and their `.import` sidecars — so the filename dangled and the sizes beside it were
      wrong in the same sentence. The numbers above are `capsule-spec.md`'s own table.)
      (Related, same reason: the AI-content disclosure surfaces on a store page, so it is also
      not live during this playtest — see `2026-08-29-STEAM-STATE-private-playtest.md` §5.)

## 5. Update the Steamworks store name (low urgency)

- [ ] **Re-checked against the tree 2026-09-04 (STEAM-1). The build now renders a real name.**
      `Branding.cs:25` is `public const string Wordmark = "Watis World"`, and
      `Branding.cs:36` is `public const string Studio = "Great-Grand-Software"` — BRAND-1
      landed both on 2026-09-04, so the splash's `[ STUDIO MARK ]` placeholder is gone too.
      *(This item previously said the build renders `WORKING TITLE`, citing `Branding.cs:19`.
      Both the string and the line number were stale; the file has grown since. Before that it
      said "SHRIMP TESTING: AS A WORKING TITLE", corrected 2026-08-29.)*
- [ ] **The name-agnostic rule was satisfied, not repealed.** `Branding.cs` says so at length:
      those two strings are the two Talon named, nothing else was renamed, and the dead-name
      audit (`DeadNameAuditTests`) still sweeps every UI-facing source.
- [ ] **The Steamworks Store Name field is still yours and is still unread from here.** Two
      names are in play now rather than three — `Watis World` (what the build shows, and what
      every doc in `docs/store/` uses) and whatever that field currently holds. Making them
      agree is a partner-site action. A clearance pass before anything goes public is still
      owed, per `2026-08-29-STEAM-STATE-private-playtest.md` §2.

## 6. Once 1–5 are done: the actual upload

I'll run these — just tell me to go, each time, per your standing rule about never uploading
without an explicit go-ahead.

- [ ] **Windows** — the build is ready right now, and it now exports as **`watis_game.exe`**
      (STEAM-1, 2026-09-04, to match the executable name set on the partner site). Command
      shape: `deploy\steam\Upload-Steam.ps1 -Username <account> -AppId <app> -DepotIdClient
      <client depot>`; add `-DryRun` first to render and print the SteamPipe scripts without
      calling `steamcmd`. **Which app the number is is the open item** — see §1's 2026-09-04
      update; the `-AppId` and the `-DepotIdClient` have to belong to the same app. First run on
      this machine prompts interactively for a **Steam Guard code**, so you'll need to be at the
      keyboard for that one prompt.
- [ ] **Before that upload can install and launch:** the partner site's **Installation → Launch
      Options → Executable** field must read `watis_game.exe` and the **Install Folder** field
      `watis_install_folder`. Talon set both on 2026-09-04; the repo now agrees with them. A
      mismatch here installs the depot fine and then fails to start it.
- [ ] **macOS** — §2a is ruled and done, so this now needs only the Depot ID from step 2. It does
      not go up the same way Windows does. Export it with `deploy\Export-MacClient.ps1` (universal,
      ad-hoc signed on this Windows machine — no Apple Developer account is involved, and none
      is needed). Then the depot has to be pushed **from WSL**, not from Windows: the app's
      launcher binary carries a POSIX executable bit that an NTFS extraction throws away, and a
      depot missing it installs an app no Mac can open. `Upload-Steam.ps1 -DepotIdMac <id>`
      refuses a live upload from Windows for that reason and says so;
      `deploy\steam\depot_build_mac.vdf.template` carries the WSL extract command. `steamcmd`
      for Linux inside WSL was installed for the July pipeline and has **not** been re-checked
      this pass — expect to install or update it.
- [ ] After a successful upload: Steamworks → **SteamPipe** → **Builds** → set the new build
      live on a branch (e.g. `default`). Uploading alone doesn't make it playable — this step
      does.

## Where things stand right now

*(Build lines rechecked 2026-08-30 by measurement on `feat/2026-08-30-w7-7-mac-path`, not by
reading the previous version of this section.)*

- **Windows build:** exports clean — see the measured size in the W7-7 report. (The old "all 22
  headless test suites green" figure is a 2026-07-25 number and is long dead — the suite has
  roughly tripled since. **See the measured count below; do not quote 22.**)
- **macOS build:** exports clean now that §2a is ruled on — Mach-O universal (x86_64 + arm64),
  executable bit intact, .NET payloads and the Steam library both inside the bundle, `Info.plist`
  carrying the microphone usage string the voice chat needs. **Ad-hoc signed** — Godot's own
  built-in signer does this from Windows; no Apple Developer account, no Xcode, no Mac required
  to build it. Ready to upload once step 2 gives it a Depot ID.
- **What a Mac tester actually sees:** installed through Steam, it launches normally — Steam
  does not quarantine what it installs, so there is no "unidentified developer" dialog. A tester
  handed the `.zip` directly (Discord, email, a browser download) gets exactly one Gatekeeper
  block and has to right-click the app → **Open** → **Open**; after that it launches normally
  forever. That is expected for an unnotarised build, not a bug.
- **UNVERIFIED — nobody has launched it on a Mac.** Every check above is a structural check of
  the artefact made on Windows and in WSL. No Mac hardware was available, so "it runs" is not
  yet a claim anyone has earned. **That is the one remaining test, and a successful export does
  not imply a successful launch.**
- **Nothing has been uploaded to Steam yet.** Uploads stay HELD until Talon says go, every time.

### Test-suite count — measured 2026-08-29 on the promotion tip

Measured by GATE-1 on `36be9d56`, on a verified-idle machine, and quoted from what the run
printed rather than from any document:

- **Scene suites: 66 — 65 PASS / 1 FAIL.**
- **xUnit: 2 912 total — 0 failed / 2 908 passed / 4 skipped.**
- `--bubbletest-selftest`: **PASS**, exit 0, with its collider-audit positive control firing —
  so the audit is proven able to report "present", not just "absent".

The single red is `Loop: full D-N-D-N (L12)`, which has now come up as the sole red in three
independent marathons on three different trees with the same failure string. It is a known
open item, not a regression from this work, and it is the next thing owed a real
discrimination. The three other suites that were red last session all passed here.

**Do not quote "22".** That figure was from 2026-07-25 and this number has been wrong four
times in `.claude/rules/test-suite.md` alone — count the lines your own run emits and quote
those, because a stale total is how a truncated run passes for a complete one.

### Test-suite count — re-measured 2026-09-04 on this repo (STEAM-1)

The block above was measured on `Sail`'s promotion tip. **This repository is the MVP extraction
and its numbers are different** — quoted from what these runs printed, on
`feat/2026-09-04-steam-1-app-id-and-exe-name` off `playtest/2026-09-04-combined` @ `1f4d688`:

- **Scene suites: 48 — 47 PASS / 1 FAIL.** Counted from the run's own `=== summary ===` block.
  The registry has grown from `DECISION-LOG.md` §4's 40 as the week's packets added suites.
- **xUnit: 2 288 total — 0 failed / 2 288 passed / 0 skipped.**
- The single red is `World: tidal-cycle phase`, discriminated over 16 runs across two worktrees
  as the documented load flake — the divergence is bit-identical every time and 147 of 152
  matched pairs are inside tolerance in passing and failing runs alike. See
  `.claude/rules/test-suite.md` and `docs/agents/roles/programming/outbox/2026-09-04-STEAM-1-report.md`.

Neither block is deleted: the 2026-08-29 one is a dated record of a different tree.
