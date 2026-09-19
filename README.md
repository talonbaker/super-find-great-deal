# Watis World

A co-op multiplayer game in **Godot 4.7 (C#/mono, .NET 8)** for PC/Steam. This repository is
the **MVP extraction** of the long-running `Sail` development repository: it contains exactly
what the current playable build reaches — the app shell, the client-hosted server-authoritative
netcode with Steam relay and lobbies, proximity voice, the networked avatar and carry system,
and the "bubble test" playtest level — and nothing else. See [`MVP-SCOPE.md`](MVP-SCOPE.md) for
what is in and out and why, [`DECISION-LOG.md`](DECISION-LOG.md) for how each call was made,
and [`ARCHITECTURE.md`](ARCHITECTURE.md) for the mental model.

What the game *is* lives in [`docs/CANON.md`](docs/CANON.md) and nowhere else.

## Requirements

- **Godot 4.7-stable, mono build** — `Godot_v4.7-stable_mono_win64_console.exe` on PATH (Windows)
  or `Godot_v4.7-stable_mono_linux.x86_64` (Linux; set `SAIL_GODOT`/`GODOT_BIN` for the test
  scripts). Export templates 4.7.stable.mono for exports.
- **.NET SDK 8.0**
- Windows + Windows PowerShell 5.1 for the scene-test orchestrators and the export scripts.
- Steam running for the interactive Host/Join path (the App ID resolves to Valve's Spacewar 480
  until the real one is configured — see `docs/store/2026-08-29-STEAM-STATE-private-playtest.md`).

## Running it

```powershell
dotnet build WatisWorld.sln

# Normal client: splash -> main menu -> Host (name -> room code) / Join (name + code) / Settings
Godot_v4.7-stable_mono_win64_console.exe --path .

# A standalone dedicated server (ENet, LAN/CI)
Godot_v4.7-stable_mono_win64_console.exe --headless --path . -- --server --port 7777

# A Steam-relay server (anonymous game-server login; players connect through Valve's relays)
Godot_v4.7-stable_mono_win64_console.exe --headless --path . -- --server --transport steam --port 7777

# A scripted bot, direct connect — local debugging and all of CI
Godot_v4.7-stable_mono_win64_console.exe --headless --path . -- `
    --bot --address 127.0.0.1:7777 --name Bot1 --log tests\logs\bot1.jsonl --duration 15

# The offline sandbox (no server, no network peer)
Godot_v4.7-stable_mono_win64_console.exe --path . res://scenes/game/sandbox/Sandbox.tscn

# The interaction / feel sandbox -- same avatar and camera, a different carry system
Godot_v4.7-stable_mono_win64_console.exe --path . res://scenes/game/sandbox/FeelSandbox.tscn
```

Flags go **after** a bare `--`; one before it is swallowed by the engine and looks like a hang.
The full flag list is `scripts/LaunchOptions.cs`.

**In play:** WASD move, mouse look, Space jump, E interact (grab/drop the golden cubes, pull the
reset lever, use a TV), Q throw, right mouse hold aim, F flashlight, V push-to-talk, Esc pause.
The world is the bubble test: pop bubbles, share one counter, explore the TV rooms, and mind the
lake — night is genuinely dark.

**The feel sandbox** (`FeelSandbox.tscn`) is the shipped avatar and camera driving a *different*
interaction system — the weight-lagged carry ported from `shader-lab` on 2026-09-05, with two
release modes: **tumble** (let go, physics takes it) and **snap** (it clicks into a marked slot).
It exists to judge that system under real game controls, which is the one thing the original lab
could not do: it ran on a capsule under an orbit camera, so the view turned and the character did
not. WASD, mouse look, Space, `E` grab/place, `Q` throw (heft-scaled), `T` flips the held item's
release mode, `R` resets the props. The props are masses, 0.4 kg to 22 kg, named after their
weights on purpose — a prop that looks like a lantern gets judged on how lanterns ought to feel.
`SandboxAvatar.InteractOverride` is the seam; the shipped `CarryController` is untouched and simply
never asked anything in that scene. Add `-- --feel-shot PATH[,seconds]` to photograph it and quit.

## Verification

Two commands make the full suite; neither alone is it.

```powershell
dotnet test tests/unit/SailNet.Tests.csproj          # Godot-free xUnit (runs anywhere, incl. CI)
powershell -File tests/Run-AllTests.ps1               # the Godot scene suites (Windows)
```

`Run-AllTests.ps1` builds once, imports once, runs every registered suite and prints one
summary block. Each `tests/Run-*.ps1` also runs alone (`-SkipBuild` skips the rebuild); logs land
in `tests/logs/`. Read `.claude/rules/test-suite.md` before relying on a count.

Headless self-tests that need no PowerShell (each exits non-zero on failure):

```
godot --headless --path . -- --steam-selftest
godot --headless --path . -- --bubbletest-selftest
godot --headless --path . -- --screenflow-selftest
godot --headless --path . -- --hudlayout-selftest
godot --headless --path . -- --tvportal-selftest
godot --headless --path . -- --cycle-selftest / --night-cycle-selftest / --run-driver-selftest
godot --headless --path . -- --flow-selftest / --reconnect-selftest / --telemetry-self-test
godot --headless --path . res://tests/scenes/NetStepSelfTest.tscn      (and the other tests/scenes)
```

## Releasing

`deploy/Export-WindowsClient.ps1`, `deploy/Export-MacClient.ps1` and `deploy/Export-LinuxServer.ps1`
export the client and dedicated-server builds into `build/`; `deploy/steam/` turns a client
export into a SteamPipe build (`deploy/steam/README.md`). The playtest distribution route and
its checklist are in `docs/store/`.

## Working in this repo

`CLAUDE.md` is the entry point for any agent or contributor: canon, the bibles, the rules under
`.claude/rules/`, and the bible check every gameplay feature owes before it is called done.
