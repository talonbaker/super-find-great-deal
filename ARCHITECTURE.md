# How Super Find Great Deal Fits Together

The high-level mental model — how the pieces connect and what flows where.
For the feature list, build/run commands, and the full test matrix, see [README.md](README.md).

## The one-sentence model

Super Find Great Deal is a **client-hosted, server-authoritative** small-session multiplayer stack: the player who
hosts runs the real game server as a hidden child process, **Steam's lobby directory is the only
"matchmaking"** (no backend to run, ever), and everything a player sees in the world is an
**authored scene the runtime loads** — never geometry built in code.

## Lifecycle — from launch to in a match

```
  Launch  ─►  App shell (Splash → Title → Main Menu)          scripts/ui · scenes/ui
  Boot.tscn        │
                   ├── HOST ──►  spawn dedicated server child  ──►  publish room code
                   │            LocalServerHost (orphan-safe)        as a Steam lobby
                   │                     │                           SteamLobby
                   │                     └──► host then connects to its own child
                   │
                   └── JOIN (room code) ──► find host in Steam lobby ──► connect
                                            SteamLobby.FindHostByCode
                                   │
                                   ▼
   Gameplay.tscn  ── loads on every peer BEFORE connecting (so replication can't race setup)
                   │
                   │  loads the WORLD  ─►  supermarket/Supermarket.tscn  (a seam file
                   │  Gameplay.BuildWorld     instancing three authored room scenes,
                   │                          40 m apart, with named spawn markers)
                   ▼
   In match:  server spawns avatars + owns movement & props   ·   clients predict + render
```

## The layers (bottom → top)

| Layer | Responsibility | Lives in |
|-------|----------------|----------|
| **Transport** | move bytes — ENet (LAN/CI) or the Steam relay | `scripts/net`, `scripts/net/steam` |
| **Session** | version/handshake gate, per-identity rate-limit, room codes, client-local hosting | `scripts/net`, `scripts/net/hosting` |
| **Replication** | who-spawns-what + state sync — `MultiplayerSpawner` + reliable/unreliable RPCs | `scripts/net`, `Gameplay.cs` |
| **Authority** | server simulates avatars & owns the prop registry; owning client predicts + reconciles | `AvatarMotor`, `PropManager` |
| **Gameplay** | avatar, camera, carry/props, proximity voice, the world | `scripts/game`, `scripts/voice` |
| **Round** | *(not wired yet)* the engine-free hide/seek loop, its wire and its teleports | `scripts/game/round`, `RoomTeleport` |
| **Presentation** | menus, room-code HUD, personal pause overlay | `scripts/ui` |

Each layer only knows the one below it. A game built on this foundation touches only **Gameplay** and
**Presentation**; everything under them is inherited unchanged.

## The three flows that define the feel

**1. Movement — server-authoritative, client-predicted**
```
  owning client:  read input ─► predict locally (zero-latency feel) ─► send INPUT only
        server:   simulate the avatar from that input (the one true position)
        client:   reconcile against the server snapshot   ·   other peers: interpolate
```
A client can only ever say *"here is my stick input,"* so movement cheats are structurally
impossible — the server never trusts a claimed position.

**2. Carry / props — authored objects, server-arbitrated state**
```
  press grab ─► ask the server ─► server checks (in range? first grab wins?) ─►
  broadcast the outcome ─► every peer attaches the prop to the holder's hand
  throw/drop ─► server releases it into physics and streams its transform each tick
```
The prop itself is an **authored node already present on every peer** (see below) — the network
only moves its *state* (held → loose → resting), never its existence.

**3. Proximity voice**
```
  client: capture + encode ─► server: relay blindly (tagged, never decoded) ─►
  every other client: decode + play through a 3D audio player on the speaker's avatar ─► falloff by distance
```

## The authored-scene contract (why the editor is WYSIWYG)

This is the rule the whole content side is built on:

- The **world, floor, walls, skybox, props, and spawn points are real nodes in `.tscn` files.**
  The runtime *loads the scene* — it does not construct geometry in code. Move a prop in the
  editor, and that is exactly where it is in the match.
- Because every peer loads the **same** scene, authored props exist identically on all machines by
  construction. The server therefore only syncs their **state**, never spawns their **existence**.
- The one deliberate exception is genuinely dynamic content — **each player's avatar** — which is
  spawned at runtime because it can't exist until that player joins.
- **The level enforces this mechanically:** `SupermarketWorldSelfTest` counts each room scene's
  nodes in its *packed* state (`SceneState` — nothing instantiated, nothing run) and again in the
  live tree, and fails on any difference — a room that builds geometry in `_Ready` turns the suite
  red. `tests/Run-SupermarketWorldTest.ps1` is the gate.
- **Scope note (so nobody "fixes" the wrong thing):** two CI-only worlds sit outside the
  contract on purpose. `--world open` and `--world propsync` load `GameWorld.cs`, a code-built
  slab with a spawn ring that the scene suites run their replication, carry and cheat proofs
  on; propsync's server-spawned crates exist precisely to prove the runtime spawn funnel
  (`PropManager.SpawnFromData`), as does the `--seed-test-props` hook. An interactive launch
  never passes `--world`, so neither is reachable by a player.

## Where your game plugs in

- **New world:** author a `.tscn` (floor + props + `Spawn*` markers) with a small `IGameWorld`
  script, and add it to `Gameplay.BuildWorldScene`. (`Supermarket.tscn` and its three rooms are
  the worked example; `LaunchOptions.DefaultWorld` picks which one a bare launch gets.)
- **New objects:** author a prop prefab (`MeshInstance3D` + `CollisionShape3D` + `Carryable`),
  then drop instances into your world scene where you want them.
- **Everything else** — transport, client-local hosting, the security boundary, movement
  authority, networked carry, proximity voice, the menu shell — you get unchanged. That is what
  this repo was forked out of `Watis_Game` to keep; `docs/PRUNE-BACKLOG.md` says what came with
  it that nobody wants.
