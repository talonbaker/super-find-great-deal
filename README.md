# Super Find Great Deal

A two-player hide-and-startle game in **Godot 4.7 (C#/mono, .NET 8)** for PC/Steam. One player
hides an object in a room full of near-identical ones; the other hunts for it. The moment it is
found, a door bursts open on the hider mid-task. See
[`CLAUDE.md`](CLAUDE.md) for the loop in five lines and
[`docs/design/2026-09-19-supermarket-mvp.md`](docs/design/2026-09-19-supermarket-mvp.md) for the
plan.

Seeded 2026-09-19 from `Watis_Game@93bef3d` — the multiplayer foundation (server-authoritative
movement, networked carry, proximity voice, Steam relay and room codes, the two-client bot
harness) kept, the old game removed. `docs/PRUNE-BACKLOG.md` says what was deliberately left
behind.

## Requirements

- **Godot 4.7-stable, mono build** — `Godot_v4.7-stable_mono_win64_console.exe` on PATH (Windows)
  or `Godot_v4.7-stable_mono_linux.x86_64` (Linux; set `SAIL_GODOT`/`GODOT_BIN` for the test
  scripts). Export templates 4.7.stable.mono for exports.
- **.NET SDK 8.0 or newer** (the projects target `net8.0`).
- Windows + Windows PowerShell 5.1 for the scene-test orchestrators and the export scripts.
- Steam running for the interactive Host/Join path. Direct Connect over ENet needs no Steam at
  all, and is how you run two clients side by side.

## Running it

```powershell
dotnet build SuperFindGreatDeal.sln

# Normal client: splash -> main menu -> Host / Join / Settings
Godot_v4.7-stable_mono_win64_console.exe --path .

# A standalone dedicated server (ENet, LAN/CI)
Godot_v4.7-stable_mono_win64_console.exe --headless --path . -- --server --port 7777

# A Steam-relay server (anonymous game-server login; players connect through Valve's relays)
Godot_v4.7-stable_mono_win64_console.exe --headless --path . -- --server --transport steam --port 7777

# A scripted bot, direct connect -- local debugging and all of CI
Godot_v4.7-stable_mono_win64_console.exe --headless --path . -- `
    --bot --address 127.0.0.1:7777 --name Bot1 --log tests\logs\bot1.jsonl --duration 15

# Practice: one click, a child dedicated server and a client into it
Godot_v4.7-stable_mono_win64_console.exe --path . -- --practice

# The offline sandbox (no server, no network peer)
Godot_v4.7-stable_mono_win64_console.exe --path . res://scenes/game/sandbox/Sandbox.tscn

# The interaction / feel sandbox -- same avatar and camera, the weight-lagged carry
Godot_v4.7-stable_mono_win64_console.exe --path . res://scenes/game/sandbox/FeelSandbox.tscn
```

Game flags go **after** a bare `--`; one before it is swallowed by the engine and looks exactly
like a hang. The full flag list is `scripts/LaunchOptions.cs`.

`--world` defaults to `supermarket`, the three rooms the game is played in. The only other ids
are `open` and `propsync`, which are code-built replication testbeds the scene suites run in and
an interactive Host can never reach.

## Two windowed clients on one machine

This is the shape of every playtest, and it needs no Steam.

```powershell
# 1. Build once.
dotnet build SuperFindGreatDeal.sln

# 2. A dedicated server, in its own terminal. Leave it running.
Godot_v4.7-stable_mono_win64_console.exe --headless --path . -- --server --port 7777

# 3. Two windowed clients, each in its own terminal.
Godot_v4.7-stable_mono_win64_console.exe --path . -- --windowed --name Talon
Godot_v4.7-stable_mono_win64_console.exe --path . -- --windowed --name Player2
```

In each client: **Join → Direct Connect → `127.0.0.1:7777`**. Both land in the holding room and
can see each other's nameplate.

`--windowed` matters when you are running two: the client is fullscreen by default and two
fullscreen windows on one monitor are not something you can play with.

**In play:** WASD move, mouse look, Space jump, E interact, Q throw, right mouse hold aim,
V push-to-talk, F9 screenshot (into `user://screenshots/`), Esc pause.

## Verification

Two commands make the full suite; neither alone is it.

```powershell
dotnet test tests/unit/SailNet.Tests.csproj          # Godot-free xUnit (runs anywhere, incl. CI)
powershell -File tests/Run-AllTests.ps1              # the Godot scene suites (Windows)
```

`Run-AllTests.ps1` builds once, imports once, runs every registered suite and prints one summary
block. Each `tests/Run-*.ps1` also runs alone (`-SkipBuild` skips the rebuild); logs land in
`tests/logs/`. Read `.claude/rules/test-suite.md` before relying on a count — several suites in
the carry and netcode families are measured load-flakes, and that file says how to tell one from
a regression.

The two that matter most while a lane is in flight:

```powershell
powershell -File tests/Run-MultiplayerTest.ps1 -BotCount 2     # replication, on the real world
powershell -File tests/Run-SupermarketWorldTest.ps1            # the level's own compliance test
```
