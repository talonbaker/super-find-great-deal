# Prune backlog

What BASE-1 (2026-09-19) deliberately did **not** remove when it forked this repo out of
`Watis_Game@93bef3d`, and why. The rule the packet set was: *the build must be green and the kept
suites must pass; anything dead whose deletion would drag past that goal goes here instead of
being fought now.* Everything below is that — dead or wrong-for-this-game, and cheap to remove on
a day when it is the job rather than a detour inside somebody else's.

Nothing here blocks a lane. Read it before you spend an afternoon wondering why something
irrelevant is still in the tree.

---

## 1. The day/night cycle and the run spine

`scripts/game/world/{CycleDriver,CycleBands,CycleSelfTest,NightCycleSelfTest,RunDriver,
RunDriverResetRequest,RunDriverSelfTest,OutdoorAtmosphere,AmbientBed,SparseSfx*,SpeedWind*,
NearestKAudio,LoopingSfxEmitter,CadenceClock,SessionSummary*}.cs`, plus
`tests/Run-CycleTest.ps1`, `Run-NightCycleTest.ps1`, `Run-RunDriverTest.ps1`, and
`scripts/ui/hud/{DayPhaseWidget,HudClock}.cs`.

**Why it stayed.** `CycleDriver` and `RunDriver` are reached from `BotHarness`, `NetworkManager`,
`LocalServerHost`, `NetworkedEntity`, `Gameplay`, `UiThemeService` and `ScreenRouter`; three green
scene suites and several xUnit files are written against them. Pulling the thread is a day, not an
hour, and it touches the bot harness every other lane depends on.

**Why it should go.** This game is three lit indoor rooms. There is no sky, no sun, no night and
no "run" — the round is the unit of time and ROUND-1 owns it. `OutdoorAtmosphere` is the sole
writer of sky/sun/moon/ambient for a world with no sky; the ambient audio beds are outdoor beds.

**Watch out for:** `RunDriver`'s reset edge is currently the only "the world starts again" signal
in the tree. Do not delete it before ROUND-1's own reset exists, or the deletion silently removes
a hook someone is about to want.

## 2. `MoveState`'s water and incapacitation fields

`MoveState.Water`, `.Soaked`, `.ControlLocked`, `.Incapacity`, `.ImpulseRagdoll`, the branches in
`AvatarMotor.Step` that read them, their bits in `NetCodec`, and the two contract files kept
alive for them: `scripts/game/water/{WaterContract,WaterGeometry}.cs` and
`scripts/game/failure/IncapacityContract.cs`.

**Why it stayed.** These are **wire format and prediction**. Removing a field from `MoveState`
changes the snapshot layout, the input entry, `NetProfile.ProtocolVersion` and the reconciliation
replay all at once — exactly the surface BASE-1 was told not to touch. Nothing writes them now, so
every body resolves `Dry` and `Active` forever and the behaviour is already gone; what is left is
bytes.

**Why it should go.** 58 snapshot bytes carry state no system produces. The flags byte's spare
bits are spoken for by systems that do not exist.

**Watch out for:** `WaterGeometry.ActiveWaters` was defaulted to EMPTY at the fork. It used to
default to a camp lake spanning x −150..−45; a room authored anywhere in that box would have had
players wading and then swimming through the floor with no lake rendered. If these fields ever
come back, that default does not.

## 3. Namespaces, identifiers and the test project's name

The assembly's root namespace is still `MpFoundation`; types live under `MpFoundation.*` and
`Sail.Game.*`; `tests/unit/SailNet.Tests.csproj` keeps its name and `RootNamespace`.

**Why it stayed.** BASE-1 was explicitly told not to rename namespaces or C# identifiers — a
retired noun in an identifier is residue, not a setting, and a mechanical rename is its own
deliberate pass. The csproj rename would have been a namespace change.

**Why it should go.** `Sail.Game.Water` in a supermarket is a confusing place for a new reader to
land.

**Watch out for:** the two UI tests that find the repo root by walking up until they see
`SuperFindGreatDeal.csproj`, and `project.godot`'s `dotnet/project/assembly_name`, which must
match the csproj's `AssemblyName` exactly.

## 4. The export binary names

`export_presets.cfg` exports `build/linux-server/watis_game_server.x86_64` and
`build/mac-client/watis_game.zip`; `deploy/Export-LinuxServer.ps1`, `deploy/Export-MacClient.ps1`,
`deploy/steam/Upload-Steam.ps1` and `deploy/steam/depot_build_mac.vdf.template` all name those
paths, and the Mac script asserts the bundle's internal layout (`watis_game.app/Contents/...`)
string by string.

**Why it stayed.** It is a coupled web across five files including a `.vdf` template and an
in-zip path assertion, and none of it is on the path to a running two-client holding room.
`application/product_name` and `config/name` WERE changed, so the window title and the user://
profile are already correct.

**Why it should go.** The day this ships anywhere, the binary is called `watis_game`.

## 5. `docs/store/**` and `deploy/steam/**`

Watis World's store copy, capsule spec, system requirements, App ID (5016210) and depot rows.

**Why it stayed.** The Steam plumbing in `deploy/` is live and the App ID facts in those documents
are what it is configured against; the packet said to leave the App ID alone. Rewriting store
copy is Talon's, not a lane's.

**Why it should go.** Every word of the marketing copy describes a different game.

## 6. Leftover assets and scenes with no user

- `scenes/dev/` is now empty of everything but what `MotorTuning*` needs.
- `assets/creatures/boxkid/` is the player model and is LIVE — it is not on this list. Read the
  name as "the player", not as evidence of a setting.
- `assets/models/objects/` still carries furniture (TV, couch, picture frame, light stand) that
  only the deleted rooms used. `TvPortal.cs` is kept (the epoch-bump teleport moved to
  `RoomTeleport.cs`, but the portal node itself may still be wanted), and `tv_static.gdshader`
  is referenced from it.
- `resources/shaders/ui_menu_bubbles.gdshader` is the MENU backdrop, not the deleted bubbles. It
  is live. Do not delete it on the strength of its name.

**Why it stayed.** Deleting an asset that turns out to be referenced from a `.tscn` fails at load
time, not at build time, and the failure is a black screen rather than a compiler error. The
clearly-unreferenced ones (`assets/environment/bubbletest`, `resources/materials/bubbletest`,
`assets/models/puffinlab`, `assets/run`, `assets/materials/water`, three orphaned shaders) WERE
removed after checking every reference; this entry is the rest.

## 7. Two suites that are owed back, not gone (one paid)

- ~~**The authored-prop adoption proof.**~~ **PAID — SHELF-1, 2026-09-19.**
  `tests/Run-AuthoredPropTest.ps1` was deleted because the supermarket had no authored props to
  adopt. It has 130 now, and the suite is back on udp/7908, registered last in
  `Run-AllTests.ps1`.
  **It is not the deleted script restored, and the difference is the point.** The old one proved
  an authored prop could be grabbed, thrown and settled to the same place on two peers — all of
  which `Run-CarryTest` and `Run-PlaceTest` now cover on the same authored crates. What 130
  authored props actually put at risk is the ID ASSIGNMENT RULE: ids are handed out by ordinal
  node-path sort, so the id of every prop depends on the name of every other one, and nothing
  about a rename is visible in a diff. The new suite asserts the whole id block (count, range,
  per-block `PropKind`, the authored pose of each block's first id) on a server, on a client
  present from the start and on one that joins eight seconds late, and it is proved able to fail
  by renaming one product node.
- **Carry through a teleport.** `tests/Run-CarryNetTest.ps1`'s PHASE 3 proved that a carried prop
  survives its holder being teleported by the server — the prop follows only because
  `NetworkedProp.BindToHolder` re-derives its transform from the holder's carry anchor every
  frame, and the witness must be an independent peer. It belongs with **ROUND-1**'s phase-driven
  room changes. The reasoning is written out where the phase used to be, in that script.

## 8. The HOW TO PLAY copy

Two of Talon's three verbatim lines were removed (water that kills you, bubbles that kill you),
not reworded. The third — double-tap to run — is still true and still shipping.

**Owed:** replacement lines in his own register, from him. Until then the screen says one true
thing rather than three charming untrue ones.
