# Crew state, the package wire and the save — one protocol bump

**Packet:** NET-1 (systems-design). **Base:** `main` @ `c16414d`. **Written:** 2026-09-04.
**Implemented by:** ECON-1 (Opus programming). **Read-only on code; nothing here was built.**

> **The ruling this document is written under.** Talon's 2026-09-04 courier brief is the
> direction. `docs/CANON.md` carries no premise and says so; where this document and that file
> appear to disagree, the brief is the ruling and the file is what is out of date
> (`CLAUDE.md:20-23`). Nothing below argues canon at anything. §0 of `CANON.md` — the durable
> engineering tenets — *is* binding on the engineering calls, and is cited where it decides one.

---

## 1. Verdict

**The foundation already supports shared-state crew coordination, and no rewrite is needed.**
Every mechanism the design's shared surfaces require is present, shipped, and exercised by a
suite: a server-authoritative store with a stable id space (`PropRegistry`), a
server-computes-then-one-reliable-`CallLocal`-RPC-carrying-absolute-values idiom
(`QuotaLedger.cs:184-197`, `BubbleCounter.cs:216-230`), a per-system late-join dump funnelled in
a fixed order (`Gameplay.cs:699-813`, nine `SendXTo` calls), a typed-denial contract
(`QuotaLedger.QuotaDenial`, `PropManager.GrabDenial`), a replicated global clock
(`CycleDriver`), a reset/persistence seam (`WorldStateSlices.cs:15-62`), and a hard-capped
6-player session (`NetProfile.cs:148`) every budget is calibrated to. The shared mailroom pile,
the shared rent ledger, the shared reputation, and players watching each other plan in real time
are all *the same shape* as `BubbleCounter` and `QuotaLedger`, at a larger field count. **This is
not "parallel movement in a shared space" — the codebase already does genuine shared adjudicated
state and proves it across three processes.**

The gaps, ranked by what blocks ECON-1 soonest:

| # | Gap | Where | Severity |
|---|---|---|---|
| 1 | **`PropState` has no room for package state** — five fields, no flags, no payload (`PropState.cs:41-46`) | needs a sibling registry (§3) | **blocking** — the whole package loop |
| 2 | **Nothing persists but `user://settings.cfg` and telemetry**; `CaptureNightSnapshot` is a reserved no-op (`WorldStateSlices.cs:38-42`, `PropManager.cs:435-438`) | needs a save (§7) | **blocking iff Q7 = crews persist** |
| 3 | **Loss into water is impossible** — anything below the kill plane teleports home (`PropManager.cs:301-309`) | needs a terminal rule (§5) | high — a whole failure mode is unreachable |
| 4 | **No server-side consumer of impact** — `ServerTick` discards `StepEvents` (`SandboxAvatar.cs:1799-1800`, `out _`) | needs a damage consumer (§5) | high — "damaged" has no cause |
| 5 | **Single held item, server-enforced** (`PropManager.cs:46-50`) | a decision, not a defect (§4) | Talon's, Q8 |
| 6 | **Radio is half-wired** — `VoiceRoute.Pa` exists, no production `PaResolver` (`VoiceManager.cs:90`, wired only by `--voice-pa-all`, `Gameplay.cs:651-654`) | one resolver + one replicated bit (§8) | medium |
| 7 | **No media path of any kind** — `grep VideoStream\|VideoPlayer\|CameraFeed scripts/` returns nothing | dispatcher is out (§9) | closed by §9 |
| 8 | **SteamID64 identity is unreachable in the ENet bot suites** — `SteamId64Of` returns 0 off Steam (`NetworkManager.cs:462-463`), so the resumed branch never fires in CI (`Gameplay.cs:961-965`) | §11 assertion 6 cannot be proven headlessly as written | **surfaced, not resolved** (§11, Open questions) |
| 9 | **The late-joiner *broadcast* path is unproven on today's `main`** — the one suite with a late joiner exits it before the only post-dump broadcast (§11) | a fixture-timing fault, not a replication one | **must not be assumed working by ECON-1** |

One protocol bump, 14 → 15, carries every wire change below (§10). Bump exactly once.

### Line citations corrected against `c16414d`

The dispatch's own cites were verified. Six had drifted or pointed at a neighbouring line; they
are corrected here and used in the corrected form throughout.

| Dispatch said | Actually at `c16414d` |
|---|---|
| `PropState.cs:37-46` — five fields | the doc comment is `:36-40`; **the five fields are `:41-46`** |
| `PropManager.cs:46` — one held item | `:46-49` is the doc comment; **the ordinal `HandsFull = 1` is `:50`**. Also **the file is `scripts/game/props/PropManager.cs`** (plural `props`) |
| `ReconnectRegistry.cs:22` — keys on SteamID64 | `:22` is `WindowSec = 60.0`. **The SteamID64 keying is stated at `:8-10` and realised at `:35`** (`Dictionary<ulong, Entry>`) |
| `WorldStateSlices.cs:38-44` — reserved no-op | **`:38-42`**; the file ends at `:62` |
| `MoveState.cs:286-290` — "says why" | **the reason (replays must be silent) is `:280-285`**; `:286-299` is the `StepEvents` struct itself |
| `VoiceSpeaker.cs:16, :90` — the PA route | `:16` is `Pa` ✓; **`:90` is `TickEnvelope`. The PA route's actual application is `SetRoute` at `:265-278`** |

Everything else in the dispatch's audit summary verified exactly as stated, including
`NetCodec.cs:181` (58 bytes), `NetProfile.cs:110` (v14), `:148` (cap 6), `:160-164` (30 Hz),
`:180-181` (next free channel 17), `PropManager.cs:219` (`ServerConsume`), `:301-309` (kill
plane), `:394` (`CarriedMassKgFor`), `QuotaLedger.cs:44-221`, `:166` (`ServerBank`, no in-world
caller), `QuotaMath.cs:52` (strict increase), `PlaythroughMachine.cs:165-170` (the verdict),
`Gameplay.cs:699-813` (the funnel), `BubbleCounter.cs:216-245`, and `VoiceProximityGate.cs:22-30`.

### The bible check

```
Bibles applied:  MECHANICS-BIBLE (this spec is entirely systems resolving their own state —
                 ledgers, flags, timers, a verdict, a save); DESIGN-BIBLE (the ripeness law,
                 applied to the Open questions); INTERACTION-BIBLE §2 by reference only, where
                 a denial must be perceivable. BEHAVIOR-BIBLE does not apply — nothing here
                 moves on its own. THRILL-BIBLE not read (via /direct only, and this packet
                 was not directed to feel).
Items checked:   §1 boundary conditions (rent at exactly the demand; deadline at exactly the
                 cycle index; mass at zero/negative/NaN); §3 races (a delivery landing on the
                 verdict tick; two riders grabbing one package; a damage event on the same
                 step as a consume); §4 idempotency (absolute-value broadcasts; Damaged set
                 once; the store's double reset pass); §5 save/load (§7 decides it explicitly
                 rather than leaving it undecided — the "expensive case" that item names);
                 §7 win/loss priority (RentMissed's position in the ordered predicate chain);
                 §8.2 the Holdable field set (§3 fills ten fields for Package); plus the
                 late-join limb of §2 (a joiner is a state transition on every shared system).
Result:          pass, with three items answered rather than closed: §5 save/load is gated on
                 Talon's Q7 and both branches are specified; §8.2's `throw_condition` and
                 `on_owner_death` for a package are answered here for the first time (§3.6);
                 §3's race between a delivery payout and the rent verdict is resolved by the
                 existing band-live guard (QuotaLedger.cs:174-180), not by new code.
```

---

## 2. Crew state — one node per system, in the ledger idiom

### 2.1 The fork

**(a) The ledger idiom.** Money stays in `QuotaLedger` unchanged. One new sibling node carries
the remaining crew scalars. Each is a plain `Node` child of `Gameplay` with hand-written `[Rpc]`
methods, absolute-value broadcasts, a `Synced` bool, a `SendXTo(peerId)` dump, and a registered
`IWorldStateSlice`.

**(b) A generic replicated store.** One node holding a small typed dictionary, one dump RPC, one
slice, that every future shared value rides.

### 2.2 Recommendation — (a), grouped by system rather than by value

**Take (a).** Not one node *per value* — that is the strawman the fork's phrasing invites, and it
would grow the late-join funnel by four. One node per *system*: `QuotaLedger` keeps money
(unchanged), and **one** new node, `CrewState`, carries reputation, the rent-period index, the
rent due, and the unlocked tier as a fixed typed record. The cycle index is not stored at all —
`CycleDriver.CyclesElapsed` is already replicated, already dumped at
`Gameplay.cs:766` and already sequence-guarded (`CycleDriver.cs:222-226`); a second copy of a
replicated fact is the defect `WorldStateSlices`' own "no second copy of the quota fact" note
already names (core-spine spec §5.5, last row).

Net late-join cost: **one** new `SendXTo`, taking the funnel from nine to ten (plus one more for
packages, §3, taking it to eleven). That is linear and legible, and every entry in it already
carries a one-paragraph comment saying why it sits where it does.

### 2.3 Why (b) loses

- **Canon 0.3 — the cheap or simple option wins; mechanisms with many knobs lose to mechanisms
  with few.** A generic store is not the simple option; it is the *general* one. It buys a
  key-registration convention, a per-key type discipline, a per-key denial vocabulary and a
  migration story, to save four fields.
- **Canon 0.1 — follow modern industry standards, not a bespoke solution where a standard one
  exists.** Godot's standard generic replicated store is `MultiplayerSynchronizer`. This project
  deliberately does not use one anywhere, and the reasons are recorded in the channel ladder
  (`NetProfile.cs:166-185`): the value of hand-written RPCs here is that each stream gets its own
  channel and its own head-of-line-blocking argument. A hand-rolled dictionary is *neither* the
  standard generic mechanism *nor* the idiom this repo already has — it is a third thing.
- **It is a second wire idiom, and the changelog says why that costs.** `NetProfile.cs:23-110` is
  a hundred lines of "a meaning change without a length change is the silently-wrong failure this
  field exists to refuse." A typed dictionary's payload length does not change when a key's
  *meaning* does, so a generic store systematically produces exactly the failure the protocol
  version exists to catch, and produces it in the one place the version gate cannot see.
- **MECHANICS-BIBLE §4 (idempotency) is free in (a) and work in (b).** The ledger idiom's
  idempotency is structural — absolute values, so a duplicate application is a no-op by
  construction (`QuotaLedger.cs:22-23`). A dictionary of mixed values has to re-establish that
  per key, per type.
- **MECHANICS-BIBLE §3 (races).** A per-system channel gives a deterministic arrival order
  *within* a system for free; `QuotaLedger` deliberately shares `RunChannel` with the verdict "so
  no client can ever display a banked count the verdict contradicts" (`QuotaLedger.cs:19-23`). A
  single generic store forces every shared value onto one ordering whether that is the right one
  or not, and takes that argument away from the system that should be making it.

**What (b) would genuinely buy, stated honestly so the choice is real:** the late-join funnel
stops growing. That cost is O(systems) and it is the only real cost of (a). Today it is nine
entries and ~110 lines of comment in one method (`Gameplay.cs:699-813`). At the eleven this spec
takes it to, it is still one screen. Revisit (b) when that funnel passes ~20 entries or when a
system needs shared values it cannot name at compile time (player-authored labels, mod data) —
neither is true of the courier design.

### 2.4 `CrewState` — the specification

Node name `CrewState`, a `Node` child of `Gameplay`, added identically on every peer (the
every-peer law — `QuotaLedger.cs:10-12`). `public static CrewState? Instance` on the
`QuotaLedger.cs:48` / `BubbleCounter.cs:48` convention.

**Channel: `NetProfile.CrewChannel = 17`** — the next free channel, and the ladder at
`NetProfile.cs:180-181` says to check it against the tip you are merging into, not the tip you
branched from. **§3's package channel takes 18.** Add both to the ladder comment in the same
edit, or the next two packets both reach for 17.

Why its own channel and not `RunChannel`: reputation and the rent due are *inputs to* the
verdict, not co-equal with it. `QuotaLedger` shares `RunChannel` precisely so banked and verdict
cannot be displayed inconsistently; reputation has no such coupling and does not deserve to sit
in front of the verdict's ordered traffic.

Replicated fields, all `int`, all absolute on the wire:

| Field | Meaning | Bounds |
|---|---|---|
| `Reputation` | crew-wide standing | clamped `[0, 1000]` — MECHANICS-BIBLE §1: a value that can silently go negative surfaces later as something unrelated |
| `RentPeriodIndex` | which rent period the crew is in, 1-based | `>= 1`, strictly increasing, never rewound |
| `RentDue` | this period's rent | from the schedule (§6); mirrors `QuotaLedger.DemandCurrentRound`'s shape |
| `UnlockedTier` | highest unlocked job tier | `>= 0` |
| `CrewId` | stable crew identity for the save (§7) | `ulong`, set once at session start, never broadcast as a delta |

```csharp
// Server -> everyone (CallLocal): absolute post-change values, applied identically wherever
// it runs. The QuotaLedger.BroadcastBank idiom, field for field.
[Rpc(MultiplayerApi.RpcMode.Authority,
     TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
     TransferChannel = NetProfile.CrewChannel, CallLocal = true)]
private void BroadcastCrewState(int reputation, int rentPeriodIndex, int rentDue,
                                int unlockedTier, uint seq);

// Server -> one joining peer. The SendQuotaTo shape (QuotaLedger.cs:221-239).
public void SendCrewStateTo(int peerId);          // server-only guard, then RpcId(...)
[Rpc(MultiplayerApi.RpcMode.Authority,
     TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
private void SyncCrewStateTo(int reputation, int rentPeriodIndex, int rentDue,
                             int unlockedTier, ulong crewId, uint seq);  // sets Synced = true

// Server -> the one requester whose action was refused. DeliverDenial's exact shape,
// host-as-player short-circuit included (QuotaLedger.cs:202-217).
public enum CrewDenial               // ordinals ride the wire — APPEND ONLY
{
    None = 0,
    NotAcceptingNow = 1,   // play is not band-live (PlaythroughStates.IsBandLive)
    Unaffordable = 2,      // the crew cannot pay
    TierLocked = 3,        // above UnlockedTier
}
```

`uint _seq`, **never rewound by a reset** — the `_seq` rule, `WorldStateSlices.cs:20-23` and
`QuotaLedger.cs:34-35`. `ResetForNewPlaythrough()` zeroes reputation and the tier and puts
`RentPeriodIndex` back to 1; it does **not** touch `_seq` and does **not** touch `CrewId`.

### 2.5 Late-join position, and why exactly there

Insert `_crewState.SendCrewStateTo((int)id);` in `Gameplay.OnPeerConnected` **immediately after
`_quotaLedger.SendQuotaTo((int)id)` (today `Gameplay.cs:782`) and before
`_quotaLedger.TestRegisterConnectIndex` (`:786`).**

Three reasons, in the order the funnel's own comments argue them:

1. **It layers on the ledger, exactly as the ledger layers on the flow state.** The funnel is
   ordered by dependency: flow state (`:778`) → quota (`:782`), because "a joiner's
   banked/demand numbers must match the server's before any surface reads them." Reputation and
   the rent due are read by the same surfaces that read banked, so they must land in the same
   pass, after it.
2. **The zero default is a true-sounding wrong answer** — the exact hazard `:779-781` names for
   the ledger. Reputation 0 reads as "a crew with a terrible record", not as "not synced yet";
   `RentDue` 0 reads as "nothing owed". Both are worse than a `Synced == false` gate.
3. **Before the test hook at `:786`**, so the connect-index registration stays the last thing in
   the ledger group and a future scheduled-crew-action test hook has an obvious home next to it.

**§3's `SendPackagesTo` goes immediately after this**, and §3 argues its own position.

---

## 3. The package registry

### 3.1 Shape

**A server-authoritative sibling of `PropRegistry`, keyed by prop id. `PropState` is not
widened.** `PropState` is a `readonly record struct` of five fields serialised into the
snapshot-adjacent prop stream and into the spawner's data array (`PropState.cs:41-46`,
`PropManager.cs:939-945`); every prop in the game pays for any field added to it, and a crate
has no destination. `PackageRegistry` is pure logic — no scene tree, no physics, no networking —
on `PropRegistry`'s own model (`PropRegistry.cs:6-15`), so it is headless-testable the way
`PropRegistrySelfTest` tests its sibling. `PackageManager` is the `Node` that owns the RPCs, on
`PropManager`'s model.

Keying by prop id (not by a second id space) is what makes "holder is derived, never stored"
true: `PackageRegistry` never carries a holder, and every reader asks
`PropManager.FindHeldBy` / the `PropState` for that same id (`PropManager.cs:388-389`). One id
space, one holder fact, no two-copies-of-the-truth race — MECHANICS-BIBLE §3.

### 3.2 Fields

```csharp
[System.Flags]
public enum PackageFlags          // ordinals ride the wire — APPEND ONLY, never reorder
{
    None      = 0,
    Fragile   = 1 << 0,
    Heavy     = 1 << 1,
    Valuable  = 1 << 2,
    Illicit   = 1 << 3,
    Sealed    = 1 << 4,
    Resealed  = 1 << 5,
    Opened    = 1 << 6,
    Damaged   = 1 << 7,
    Stolen    = 1 << 8,
    Lost      = 1 << 9,
}

public readonly record struct PackageState(
    int          PropId,          // the PropRegistry id; the ONE id space
    PackageFlags Flags,
    float        MassKg,          // authoritative; PropManager reads Carryable.MassKg today
    int          DestinationId,   // a marker id, see 3.3
    int          DeadlineCycle,   // see 3.4
    int          Payout,          // int, in the ledger's own unit
    ulong        AssignedToSteamId // 0 = unassigned; see 3.5
);
```

Ten fields' worth of MECHANICS-BIBLE §8.2 Holdable contract, answered (§3.6).

**`MassKg` is authoritative here, and the mass the motor uses stays
`Carryable.MassKg`.** `PropManager.CarriedMassKgFor` reads the body's mass
(`PropManager.cs:394-398`) and that is the number `CarryController.ComputeSpeedFactor` sees on
both the server and the owner's prediction (`SandboxAvatar.cs:1394`, `:1786`). ECON-1 sets the
body's mass **at spawn from the registry** and never again — two independently authored masses
for one object is exactly the "2× divergence nobody had actually decided on" defect
`PropManager.cs:18-29` records. The registry field exists so the save (§7) and the job board can
read a package's mass without a live node.

### 3.3 `DestinationId`

An `int` marker id, resolved per world through a static table on the world script, the way
`BubbleTestLayout` already holds its anchors as static `Vector3`s
(`BubbleTestLayout.cs:39-90`: `HubAnchor`, `RedAnchor`, `BlueAnchor`, `CyanAnchor`,
`GreenAnchor`, `TangleAnchor`, `TvRoomAnchor`). **The wire carries the id, never the position** —
the authored-scene contract (`ARCHITECTURE.md:75-88`) says every peer already has the same
scene, so the position is reconstructible locally and does not belong on the wire. Ids are
authored constants and are **append-only for the same reason `PropKind` is**.

### 3.4 `DeadlineCycle` — the unit, and why

**Unit: an absolute `CycleDriver` cycle index — the value of `CycleDriver.CyclesElapsed` at
which the package is late.** Evaluated at the `NightToDawn` crossing, the one instant this
codebase evaluates anything (`PlaythroughMachine.cs:165-181`: *"Predicates are evaluated here
and nowhere else: no polling path can produce a verdict mid-round"*).

Rejected alternatives, with the reason each loses:

- **A server tick.** There is no global replicated server tick. `_serverTick` is per-avatar and
  is packed into that avatar's own snapshot (`SandboxAvatar.cs:1775`, `:1827-1830`); it is a
  reconciliation sequence number, not a world clock. Using it would invent a second clock.
- **A wall-clock seconds-remaining float.** Floats drift, must be re-broadcast to stay honest,
  and would collide with the clock's own re-anchor seam (core-spine spec §1.5, *"re-anchor,
  never pause"*). An int cycle index is exact, is already replicated
  (`CycleDriver.cs:87-94`, broadcast with `Phase` "not just re-derived locally"), survives a
  re-anchor by construction, and costs 4 bytes.
- **A cycle phase pair `(cycle, phase)`.** Sub-cycle precision buys nothing: nothing in this
  design resolves a deadline mid-cycle, because the verdict does not.

**Boundary, stated rather than left to fall out (MECHANICS-BIBLE §1):** a package is late when
`CyclesElapsed >= DeadlineCycle` **at the verdict instant** — inclusive at the boundary, so a
deadline of N means "delivered by the end of cycle N−1". `DeadlineCycle = 0` means *no
deadline*, and is the default.

### 3.5 `AssignedToSteamId`

`ulong`, the crew member a package was matched to during the sort. **0 = unassigned.** Keyed by
SteamID64 rather than peer id because peer ids are recycled every connection and SteamID64 is
the stable identity that survives a reconnect (`ReconnectRegistry.cs:8-10`;
`NetworkManager.cs:456-461`). `Gameplay` already caches `peerId -> SteamID64` at connect time,
and caches it *then* precisely because it is unresolvable at disconnect time
(`Gameplay.cs:125-132`, `:702-705`) — so the mapping ECON-1 needs already exists and is already
correct.

> **The wall, and it is real.** `NetworkManager.SteamId64Of` returns **0 on the ENet transport**
> (`NetworkManager.cs:462-463`), which is every bot and every CI suite. `Gameplay.cs:961-965`
> records the consequence in the reconnect work: *"OnPeerConnected's call to this can never
> actually take the resumed=true branch in ENet-bot CI."* An assignment keyed on SteamID64 is
> therefore **always 0 for every bot**, which makes "assigned to nobody" the only reachable state
> in a headless suite and makes §11's assertion 6 unprovable as stated. §11 specifies the
> workaround; the underlying fork is in Open questions, ripe.

### 3.6 The MECHANICS-BIBLE §8.2 Holdable field set, filled

| Field | Package's answer |
|---|---|
| `carry_mode` | `carried` — both arms, no handle. `PropManager.IsArmfulPose` returns true for every shipped kind (`:248-249`) and Package joins them. |
| `weight_class` / encumbrance | `MassKg`, through the one shipped formula: `1/(1 + kg × 0.055)`, floored at `0.55` (`CarryController.cs:34-36`, `:79-85`). |
| `hands_or_arms_required` | two (armful pose). |
| `is_active_agent` | no. BEHAVIOR-BIBLE does not apply. |
| `noise_when_moved` | none in the MVP — there is no shared noise channel in this repo. |
| `visibility_while_carried` | visible; the existing carry anchor and pose, unchanged. |
| `drop_condition` | the shipped drop verb, unchanged (`SandboxAvatar.HandleCarryIntent`). |
| `throw_condition` | **throwing is permitted, and it is a design lever, not an oversight.** Throwing already exists as a shipped verb and already produces a Loose prop that the server simulates and streams (`PropManager.cs:295-330`). A thrown Fragile package taking a `BumpImpact` is the cheapest possible source of §5's damage, needs no new verb, and is the honest reading of the brief's "damaged/opened by accident". |
| `on_owner_death` | `drop_in_world_retrievable`. `ReleaseHeldBy` already does exactly this on the shipped incapacitation path; a package is a prop and inherits it. |
| `on_owner_disconnect` | `drop_in_world_retrievable`, **and the assignment survives**. These are separate fields on purpose (MECHANICS-BIBLE §8.2), and `ReconnectRegistry.ResumeData.HeldPropIds` (`:31`) already re-grants held props on reconnect through `TryRestoreHeldProp` (`Gameplay.cs:754-755`). `AssignedToSteamId` is untouched by a disconnect — that is the whole reason it is keyed on SteamID64. |

### 3.7 Replication

**Channel: `NetProfile.PackageChannel = 18.`** Its own channel on the ladder's own reasoning
(`NetProfile.cs:166-185`). Specifically: the mailroom sort is a *burst* — six players
re-assigning a pile of packages inside a few seconds — and the prop channel (4) is carrying the
loose-transform stream at 30 Hz for every prop in flight at the same moment
(`PropManager.cs:288-293`). Putting an assignment burst behind a physics stream, or a physics
stream behind an assignment burst, is precisely the head-of-line-blocking the ladder exists to
prevent.

```csharp
// Server -> everyone (CallLocal): one package's complete absolute state. Absolute, never a
// delta, so a duplicate application is idempotent by construction (QuotaLedger.cs:22-23).
[Rpc(MultiplayerApi.RpcMode.Authority,
     TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
     TransferChannel = NetProfile.PackageChannel, CallLocal = true)]
private void BroadcastPackage(int propId, int flags, float massKg, int destinationId,
                              int deadlineCycle, int payout, ulong assignedTo, uint seq);

// Server -> everyone: this package left the registry (delivered, or Lost-and-collected).
// Separate from BroadcastPackage because "no entry" and "an entry with Lost set" are
// different facts and a client must not have to infer one from the absence of the other.
[Rpc(..., TransferChannel = NetProfile.PackageChannel, CallLocal = true)]
private void BroadcastPackageRemoved(int propId, int reason, uint seq);   // reason: append-only enum

// Server -> one joining peer: every package, one message per package. PropManager.SendDumpTo's
// exact shape (:939-945) — a loop of RpcId, not one batched payload.
public void SendPackagesTo(int peerId);

// Server -> the one requester. DeliverDenial's shape.
public enum PackageDenial { None = 0, NotAcceptingNow = 1, NotYours = 2, Gone = 3, Sealed = 4 }
```

**One message per package in the dump, not a batch.** `PropManager.SendDumpTo` does exactly
this and the reason is worth restating: the per-item message is the *same* message a live change
uses, so a joiner's convergence path and the steady-state path are one code path with one set of
bugs. A batched dump is a second decoder that only ever runs on join — the least-tested code in
the system, on the most fragile moment.

**Late-join position: immediately after `SendCrewStateTo` (§2.5), and after
`_propManager.SendDumpTo` at `Gameplay.cs:759`.** Not negotiable: a `PackageState` is keyed by a
prop id, so a package broadcast that arrives before that prop's `ApplyPropState` names a prop the
joiner has never heard of. The prop dump *must* precede the package dump. It already does, by 25
lines.

**Store slice:** `PackageManager` registers as an `IMapScopedSlice` — the same scope
`PropManager` has (core-spine spec §5.2, "Props / world items … map-scoped"), because a package
lying where it was dropped across a night boundary is the same fact as a prop lying where it
fell. `ResetForNewPlaythrough` clears the registry; `CaptureNightSnapshot` is where §7's save
write lands if Q7 says crews persist.

### 3.8 `PropKind.Package` and the default-arm trap

`PropKind.Package = 2` — the next ordinal after `Crate = 0`, `Ball = 1` (`PropState.cs:13-17`).
Append-only; reordering "would silently turn every existing crate into a ball on a mixed-version
session" (`:9-11`).

**The trap.** `NetworkedProp._Ready` resolves the kind with

```csharp
Body = GetNodeOrNull<Carryable>("Body") ?? Kind switch
{
    PropKind.Ball => new Carryable { Kind = Carryable.Shape.Ball },
    _             => new Carryable { Kind = Carryable.Shape.Crate },   // NetworkedProp.cs:82
};
```

The `_ =>` arm renders **any** unknown kind as a Crate, silently. `NetProfile.cs:38-42` names
this exact line as the worst kind of data-shape change and says the protocol version exists to
convert it into a refused connection.

**What replaces it:** an explicit arm per kind, and a default that is loud.

```csharp
Body = GetNodeOrNull<Carryable>("Body") ?? Kind switch
{
    PropKind.Ball    => new Carryable { Kind = Carryable.Shape.Ball },
    PropKind.Crate   => new Carryable { Kind = Carryable.Shape.Crate },
    PropKind.Package => new Carryable { Kind = Carryable.Shape.Crate },  // authored .tscn is the
                                                                        // real path; this is the
                                                                        // runtime-spawn fallback
    _ => LoudFallbackCrate(Kind),   // GD.PushError($"[prop] unknown PropKind {(int)Kind} — the
                                    // protocol gate should have refused this peer"), then a Crate
};
```

The default is unreachable in production *because* the version gate refuses a stale peer — which
is exactly why it must scream rather than guess. A silent guess is the one failure mode that
survives the gate being wrong.

**Package appearance is authored, not constructed.** Under the authored-scene contract
(`ARCHITECTURE.md:75-84`) a package is a `.tscn` prefab instanced into the world with a
`Carryable` child named `Body`, and the switch above never runs for it. The runtime-spawn arm
exists only for `--seed-test-props`-style paths and for §11's fixture.

---

## 4. Backpack — single slot for the first playtest

### 4.1 Recommendation

**One package per rider.** The foundation as-is: `GrabDenial.HandsFull` refuses every further
grab while holding anything, and refuses rather than swaps "so nothing a player is carrying is
ever released by a press that was meant as a pickup" (`PropManager.cs:46-50`).
`CarriedMassKgFor` returns one prop's mass (`:394-398`). This is Talon's call (HANDOFF Q8) and
the spec carries both; the recommendation is single-slot because it makes the mailroom sort a
*real* decision — one rider, one package, so matching weight to rider is the whole minigame —
and because it costs zero wire.

### 4.2 N-slot, costed precisely

**Input bits.** `buttons2` has **seven free bits** (`NetCodec.cs:61`, `:131`, `:178`). A slot
select needs one edge bit plus a slot index; at four slots that is 1 + 2 = **3 of 7**, leaving
four. `InputEntryBytes` stays **26** and `PackInputs`/`UnpackInputs` change by two lines each.
**This has been done before in this codebase**: protocol v8 spent the first buttons byte's last
two bits on exactly this — "the slot-select edge + index (`NetCodec.FlagSelectSlot` /
`FlagSelectSlotIndex`)" for a two-slot carry that was later cut (`NetProfile.cs:35-38`). The
shape is known and the history is on record.

**Denial.** `GrabDenial.HandsFull = 1` stops being "holding anything" and becomes "every slot
full". The *ordinal and its meaning do not change* — "your hands are full" is still true — so no
ordinal is appended and nothing on the wire moves. `PropManager.cs:46-49`'s doc comment is the
edit.

**Mass.** `CarriedMassKgFor` sums instead of returning one:
`foreach (int id in HeldPropIdsFor(peerId)) sum += NodeFor(id).Body.MassKg`. `HeldPropIdsFor`
already exists and already "returns every match rather than assuming that invariant"
(`PropManager.cs:947-951`) — it was written for this. `_heldByPeer` becomes a multimap;
`FindHeldBy` keeps returning *the* prop in hand (the active slot) because every one of its
callers means "the thing they are actually using" (`:382-384`).

**The carry anchor and pose.** One anchor exists today (`CarryController.AnchorProvider`,
`:44`). N slots need N anchors, and only the active one is the armful pose
(`PropManager.IsArmfulPose`, `:248`); the inactive ones need a stowed anchor and a stowed pose,
which is new animation contract work, not netcode. **This is the real cost of N-slot — it is an
`ANIMATION-CONTRACT.md` change, not a wire change.**

**The wire.** `PropState.Mode = Held` plus `HolderPeerId` already expresses "peer P holds prop
X"; N of them is N rows in a registry that is already a dictionary. **No new prop RPC, no
`PropState` field, no snapshot bit.**

**Total: three input bits, four one-line code changes, and a new stowed pose.** Cheap on the
wire; not free in animation. Both arms of Q8 are affordable inside the one v15 bump *if the
input bits are spent in that bump* — see §10's note.

---

## 5. Loss and damage

### 5.1 Lost — a registry flag with a terminal rule

`PackageFlags.Lost` is set by the server, once, when **any** of:

1. **Below the kill plane.** `PropManager`'s loose loop already detects this and teleports the
   prop home (`PropManager.cs:301-309`, against `Carryable.KillPlaneY` /
   `NetProfile.KillPlaneY`, default `-30f` at `NetProfile.cs:256`). ECON-1 adds the flag set
   **before** the existing recovery and leaves the recovery intact: the package comes home
   *marked*, which is both more legible and cheaper than inventing a despawn.
2. **Resting on the lake floor for N seconds.** `WaterGeometry.LakeFootprint` carries
   `FloorY` (`:261`), and the bubble test's lake is
   `LakeFootprint.Disc(centre, radius, floorY: -6.0f)` (`:336-337`) — **24 m above the kill
   plane**. This is the gap the audit named: a package dropped in the lake settles on the lake
   floor and is never recovered by anything, because nothing looks there. Rule: a package whose
   `PropState.Mode == Resting` and whose position satisfies
   `LakeFootprint.InLakeRegion(pos, lake) && pos.Y <= FloorY + 0.5f` for `N` consecutive server
   seconds is Lost. `N` is a knob; **pick 3.0 s** — long enough that a package bouncing through
   the shallows is not lost, short enough to be legible.
3. **Consumed.** `PropManager.ServerConsume` (`:219-232`) is the only permanent removal, and it
   already releases the prop from its holder first — load-bearing, not tidiness (`:209-218`).
   A consumed package emits `BroadcastPackageRemoved`.

**Recovery of a loose package found elsewhere is just a Loose prop — no system.** The existing
grab path already picks it up, the existing registry already knows its flags, and the existing
dump already tells a joiner about it. Building a "recovery" mechanism would be building a
second way to pick something up. Canon 0.3.

**Idempotency (MECHANICS-BIBLE §4).** `Lost` is set once: the server checks
`(flags & Lost) == 0` before setting and broadcasting. A package that is already Lost and falls
again produces no message.

**Boundary (MECHANICS-BIBLE §1).** The kill-plane test is `< KillPlaneY`, strictly, matching the
shipped `PropManager.cs:301` and `SandboxAvatar.cs:1821` — "out of bounds means one thing
everywhere" (`:1816-1818`). The lake-floor test is `<=`, inclusive, because resting *on* the
floor is the case being detected.

### 5.2 Damage — a server-side consumer of `StepEvents`, outside the replay

`AvatarMotor.Step` emits `StepEvents` with `BumpImpact` (largest non-floor impact speed carried
into an obstacle this step) and `FallSpeed` (`MoveState.cs:286-299`). Today **the server throws
them away**: `SandboxAvatar.ServerTick` calls
`AvatarMotor.Step(this, _state, e.Intent, authoritativeSpeedFactor, TickDelta, out _)`
(`SandboxAvatar.cs:1799-1800`).

**Change `out _` to a real `out StepEvents ev` there, and consume it inside that loop.**

**Why that site and not another.** `MoveState.cs:280-285` states the constraint: *"The step
itself never plays SFX or triggers visuals — reconciliation replays steps dozens of times a
second and those replays must be silent. The caller consumes these events only when a tick is
simulated for the first time."* There are four `AvatarMotor.Step` call sites
(`SandboxAvatar.cs:1327`, `:1409`, `:1596`, `:1799`):

- `:1596` is `Reconcile`'s replay loop — **the forbidden one**, and it already passes `out _`.
- `:1409` is `OwnerTick`'s local prediction — a client, not authority. A damage consumer there
  would be a client asserting damage, which the parity law (`CANON.md` §0.9) forbids.
- `:1327` is `OfflineTick` — the offline playground, no server.
- **`:1799` is `ServerTick`'s loop over `_serverQueue.TakeForTick()` — the server simulating each
  input entry exactly once, for the first time.** It is the only authoritative first-simulation
  site in the codebase, and it is therefore the only correct home for a damage consumer.

**The rule.** On a step where the peer holds a package:

```
if (ev.BumpImpact >= PackageDamageImpactMps  ||  ev.FallSpeed >= PackageDamageFallMps)
    ServerMarkDamaged(heldPackagePropId);
```

`ServerMarkDamaged` sets `Damaged` **once** — checks `(flags & Damaged) == 0`, sets, broadcasts
`BroadcastPackage`, returns. Idempotent by construction (MECHANICS-BIBLE §4); a rider grinding
along a wall produces one broadcast, not sixty a second.

**Thresholds are knobs, and both are real numbers.** `PackageDamageImpactMps` and
`PackageDamageFallMps` live beside the other tunables. Anchor for a first pass: the sprint
ceiling is **4.86 m/s** (`AvatarMotor.MoveSpeed × SprintMultiplier`, quoted at
`PropManager.cs:26-28`), so an impact threshold **at** that value means "only a full-sprint
collision damages a package" and anything below it is free. Start there; it is a value call and
the playtest owns it.

**`Fragile` is the gate, not the threshold.** Only a package with `PackageFlags.Fragile` takes
damage from this consumer in the first pass. A non-fragile package needs a second threshold
nobody has a number for, and inventing one is inventing a knob (canon 0.3).

**Race (MECHANICS-BIBLE §3): damage on the same tick as a consume.** Resolution order is fixed:
the damage consumer runs **inside** `ServerTick`'s input loop; `ServerConsume` runs from a
gameplay handler outside it. `ServerMarkDamaged` therefore checks the registry still holds the id
and returns false if not — a package delivered on the tick it was scraped is delivered clean, and
that is the deliberate answer rather than the accidental one.

### 5.3 Heavy — mass sums, and 0.55 is the ceiling

`Heavy` is not a separate mechanic. It is `MassKg`, through the one shipped formula:

```
speedFactor = max(0.55, 1 / (1 + kg × 0.055))          CarryController.cs:79-85
```

`MinSpeedFactor = 0.55f` at `CarryController.cs:36`, mirrored as `AvatarMotor.MinSpeedFactor` at
`AvatarMotor.cs:360` and clamped at `AvatarMotor.cs:1945`.

**Say it plainly, because the design depends on it: 0.55 is a hard floor, and it is therefore
the design's encumbrance ceiling.** The formula reaches 0.55 at
`kg = (1/0.55 − 1)/0.055 ≈ **14.9 kg**`. **Beyond ~15 kg, extra mass costs a rider nothing.**
Any design that wants "this package is so heavy you can barely move" past that point is asking
for a *different* mechanic — a capability denial, which MECHANICS-BIBLE §8.3's research default
already recommends as the primary carry penalty ("carrying primarily *disables verbs* … rather
than primarily applying a speed multiplier"). Do not raise the floor to get there; a floor
exists so a laden player is never unplayable, and the airborne ceiling depends on it
(`AvatarMotor.cs:1727`).

The formula is already hardened against a designer typo — negative and NaN masses are treated as
weightless rather than making the player *faster*, with the pole at `mass == −18.18` documented
at `CarryController.cs:69-78`. MECHANICS-BIBLE §6's zero/max/negative check is already passed.

---

## 6. The rent cycle and the verdict

### 6.1 `$200 → $400 → $800 → $1600` is a `.tres` edit

`QuotaLedger` is the rent ledger in all but name. The schedule is data
(`assets/run/quota_schedule.tres`) and `QuotaMath.Demand` is
`d(1) = early[0]`, then `d(n) = max(ceil(d(n−1) × factor), d(n−1) + 1)` with a strict-increase
floor (`QuotaMath.cs:39-55`, the floor at `:51`).

```
EarlyRoundsDemand = PackedInt32Array(200)
TailGrowthFactor  = 2.0
CarrySurplus      = true
```

gives `d = 200, 400, 800, 1600, 3200, …` — **the brief's curve exactly, with no code change.**
`CarrySurplus = true` (the shipped value) means a surplus banked in period N counts toward
N+1 — the ledger is cumulative and "surplus banked in round N automatically counts toward round
N+1" (`QuotaLedger.cs:12-17`). That is the right default for rent: a crew that overpays is ahead,
not merely lucky. `CarrySurplus = false` flips the verdict to a per-period bucket **with no code
change** (`:16-17`), so the fork is a data edit if Talon wants it.

Overflow is already handled: `DemandClamp = 1_000_000_000` (`QuotaMath.cs:32`). At ×2,
`d(n) = 200 × 2^(n−1)`, so `d(23) = 838,860,800` and `d(24)` clamps. A session does not get
within twenty periods of that.

### 6.2 "Banked" means the delivery payout

`QuotaLedger.ServerBank(peerId, cacheUnits, out denial)` is a **declared slot with no in-world
caller** (`:37-42`, `:166-186`). **ECON-1's delivery handler is that caller.** On a successful
delivery the server calls `ServerBank(riderPeerId, package.Payout, out denial)`.

Three properties come free and all three are load-bearing:

- **The band-live guard.** Banking is accepted in `RoundIntro`/`InRound` and denied otherwise
  (`QuotaMath.BankingOpen`, `QuotaLedger.cs:174-180`). A delivery racing the verdict lands one
  tick later and **keeps its package** — "the haul is NEVER consumed on a denial … this method
  never touches props at all" (`:41-42`). MECHANICS-BIBLE §3's race, already resolved.
- **Typed denial to the requester only**, reliable, host-as-player short-circuit included
  (`:202-217`).
- **Absolute broadcast on `RunChannel`**, deliberately the same channel as the verdict, so no
  client can display a balance the verdict contradicts (`:19-23`).

**Ordering rule for ECON-1, and it is the one thing that can go wrong here:** consume the
package **only after** `ServerBank` returns true. `ServerBank` returning false is the
"denied, keep your haul" path, and consuming first would destroy a package the ledger refused to
pay for.

### 6.3 `RentMissed` — the predicate and the ordinal

Today `RunOutcomeKind` has exactly one ordinal, `QuotaMissed = 0`
(`PlaythroughState.cs:33-39`), mirrored in the UI layer at `PlaythroughFlow.cs:29-38`, and
exactly one predicate ships (`QuotaMissedPredicate`, registered at `PlaythroughDriver.cs:155`).

```csharp
public enum RunOutcomeKind : byte      // APPEND ONLY — ordinals cross the wire
{
    QuotaMissed = 0,
    RentMissed  = 1,      // new in v15
}
```

**Append both mirrors in the same edit** — `PlaythroughState.cs:36` and
`PlaythroughFlow.cs:30`. `LiveFlowViews.ToView` is a straight cast
(`LiveFlowViews.cs:34`), so a mirror that drifts by one ordinal shows the wrong loss screen with
no error anywhere.

`RentMissedPredicate : ILossPredicate` reads the same `QuotaLedger` and returns
`new RunOutcome(RunOutcomeKind.RentMissed, round, ledger.CumulativeDemand,
ledger.CumulativeBanked)` when `ledger.IsQuotaMissed()`.

**Why a new ordinal rather than reusing `QuotaMissed`:** the loss screen's copy is keyed on the
ordinal and reads `"THE CACHE RAN DRY"` (`LossScreen.cs:87`), which is a sentence about a retired
premise. Rent needs its own line, and one appended ordinal is cheaper than making one ordinal
mean two things.

**Priority (MECHANICS-BIBLE §7).** *"Registration order is evaluation order is priority order …
explicit priority, never code-order accident"* (`PlaythroughState.cs:56-61`). **Register
`RentMissedPredicate` first**, ahead of `QuotaMissedPredicate` at `PlaythroughDriver.cs:155`.
Rent is the crew-wide, unavoidable failure; a quota is a per-run one. If Talon's design replaces
quota entirely, ECON-1 registers only `RentMissedPredicate` — the chain is a `List` and an empty
slot is legal.

### 6.4 Q6 — which cycle is the rent period. **Talon's, carried as three arms.**

| Arm | What it is | Cost | Risk |
|---|---|---|---|
| **(A) The day/night cycle** — **recommended** | One rent period = one `CycleDriver` cycle. Verdict at `NightToDawn`, which is already the *only* verdict instant (`PlaythroughMachine.cs:165-181`). Period is a knob (`CycleDriver.Setup(..., periodSec, ...)`, `:127`; default 120 s, `:8`). | **Zero new machinery.** The clock is replicated, dumped on join (`Gameplay.cs:766`), sequence-guarded, and re-anchor-safe. `DeadlineCycle` (§3.4) is in the same unit for free. | A 120 s rent period is almost certainly too fast for a courier run. **This is why the period is a knob** — set it at playtest, not here. |
| **(B) One real session** | Rent is due once, at session end. | Needs a session-end event that does not exist: `PlaythroughState.Loss` "never times out" (`PlaythroughMachine.cs:155`) and there is no graceful-end state. ECON-1 would build a new terminal transition and a new broadcast. | Removes escalation entirely — the doubling curve has nothing to double *over*. The brief's `$200 → $1600` progression is unreachable. |
| **(C) N deliveries** | Rent is due every N successful deliveries. | Needs a counter that is not the clock, its own broadcast, its own late-join dump, and its own reset. Roughly a third `CrewState`-shaped node. | Couples pressure to *success*: a crew that delivers well is punished sooner, which inverts the intended pressure. It also makes the deadline unit (§3.4) incoherent — a package deadline in "deliveries" is a deadline the rider can move by working. |

**Recommendation: (A), with `periodSec` a knob and the first playtest value set by Talon.** It is
the only arm that costs nothing, and it is the only arm in which `DeadlineCycle` and the rent
period are the same unit — which is what makes "this package is due before rent" a sentence the
code can evaluate.

---

## 7. The save

**Gated on HANDOFF Q7.** If crews are one session, §7.6 is the shape of the no-op and nothing
below is built. If crews persist, this is the whole of it.

### 7.1 Where the seam already is

`IMapScopedSlice.CaptureNightSnapshot(int round)` is a **reserved across-nights write path**,
declared and deliberately empty: *"No-op bodies are legal and expected today; when disk
persistence is ever built it lands here without re-plumbing a single manager"*
(`WorldStateSlices.cs:38-42`). `PropManager` implements it as a no-op (`PropManager.cs:435-438`).
**The save writes from there and nowhere else.** That is what the seam is for, and using it means
ECON-1 adds no new fan-out.

### 7.2 The file

- **Path:** `user://crew/<crewId>.json`, `crewId` the `ulong` from §2.4.
- **Format:** JSON, `System.Text.Json` (already a dependency — `BotHarness.cs:3`).
- **Schema version:** an `int` `schema` field, first. **A file with a higher `schema` than the
  build understands is not loaded and not overwritten** — it is left alone and the session starts
  fresh, with a log line. Silently downgrading a player's crew is worse than losing a session.

**In the file:**

| Key | Source |
|---|---|
| `schema` | constant |
| `crewId` | `CrewState.CrewId` |
| `cumulativeBanked`, `bankedThisRound`, `currentRound` | `QuotaLedger` (`:71-73`) |
| `reputation`, `rentPeriodIndex`, `unlockedTier` | `CrewState` |
| `cycleIndex` | `CycleDriver.CyclesElapsed` |
| `savedAtUnixSec` | wall clock, for the conflict rule |

**Not in the file, and each for a stated reason:**

- **Props and packages.** `PropRegistry` ids are monotonic and never rewound
  (`PropManager.cs:447-448`); a package restored under a stale id would collide with the fresh
  world's id space. Packages regenerate from the job board.
- **Positions.** `spawn` is dealt by join order (`Gameplay.cs:701`, `:724`) and the world may
  differ. There is nothing a saved position means across sessions.
- **Held state.** `on_owner_disconnect` is `drop_in_world_retrievable` (§3.6); a saved hand is a
  contradiction of that rule.
- **Achievements, settings, display, audio, graphics.** Already in `user://settings.cfg`, with
  their own owners (`AchievementStore.cs:26`, `DisplaySettings.cs:15`, `AudioSettings.cs:15`,
  `GraphicsSettings.cs:46`). Do not put a second copy anywhere.

### 7.3 When it is written

1. **At the `NightToDawn` verdict**, from `CaptureNightSnapshot` — after the night-scoped
   cutoffs, which is where the seam already fans (`WorldStateSlices.cs:38-39`).
2. **On graceful shutdown**, from the server node's `_ExitTree`.

**Server process only.** `Gameplay._isServer` gates it. A client never writes a crew file — it
has no authoritative state to write, and the parity law says the server owns it (`CANON.md`
§0.9).

**Crash loss is accepted**, exactly as the core-spine spec already accepts it (§5.1: *"a
playthrough lives and dies with its server process, and crash-loss is accepted"*). A crash costs
at most one cycle.

### 7.4 When it is read

**Before the first `SendXTo`.** Concretely: in `Gameplay`'s server setup, before
`_propManager.SpawnInitialProps` at `Gameplay.cs:696`, and therefore long before any peer
connects. `QuotaLedger.Setup` and `CrewState.Setup` take the loaded values as parameters, exactly
as `QuotaLedger.Setup` already takes `earlyOverride` and the test schedule
(`QuotaLedger.cs:117-142`).

**Joiners are fed from the host's dump and never from their own file.** A joiner's `SendQuotaTo`
/ `SendCrewStateTo` payload is authoritative, full stop. A client that has its own crew file for
the same `crewId` ignores it for the duration of the session. This is not a preference; it is
canon 0.9 — *"gameplay-relevant world state is server data, identical on every client."*

### 7.5 Conflict rules

- **Two hosts with the same `crewId`.** Both write their own file on their own machine; there is
  no shared store and no arbitration, because there is no server between them. **This is
  accepted and stated:** a crew's canonical history is whichever member is hosting. The design
  consequence — that hosting is a role with continuity attached — belongs in front of Talon and
  is in Open questions.
- **A joiner with an older file for this `crewId`.** Nothing happens. §7.4: joiners are fed from
  the dump. The stale file is left on disk untouched, so if that player hosts later they resume
  from their own last-hosted state.
- **A file for a `crewId` this host has never hosted.** Loaded normally. `crewId` is the key; who
  wrote it is not.
- **A corrupt or unparseable file.** Log, start fresh, **do not overwrite until the next
  scheduled write.** Same posture as the higher-`schema` case.

### 7.6 If Q7 = crews are one session: the shape of the no-op

`CaptureNightSnapshot` stays empty. `CrewState` still exists, still replicates, still resets at
the playthrough boundary — the crew is real, it just does not outlive the process. **No file, no
path, no schema, no conflict rules, and none of §7.9's hazard.** ECON-2's save row drops out
entirely. This is a genuinely cheaper world and it is why the question is worth asking.

### 7.7 **`user://` resolves by project NAME — one profile for every worktree on this machine**

`project.godot:13` is `config/name="Watis World"`. Godot resolves `user://` from the project
name, so **every worktree of this repo on this machine — `Watis_Game`, `Watis-move1`,
`Watis-net1`, `Watis-level1` — shares one `user://` directory.** That is not a hypothetical.
This repo has already been bitten by it, and the bite is recorded in the code:

> *"A SCRIPTED BODY EARNS NOTHING (W7-8, 2026-08-30). isOwner alone is not 'a player': every
> suite bot is the owner of its own avatar, and AchievementStore writes into
> user://settings.cfg, which resolves by project NAME and is therefore the ONE real profile
> shared by every worktree on this machine. Run-AimTest's scripted 2s -> 6s aim hold clears the
> 3.0 s 'Tough guy' threshold, and it had already spent Talon's first-time unlock."*
> — `SandboxAvatar.cs:895-903`

**What it means for a crew save.** A crew file written by a suite's headless server lands in the
same directory as Talon's real crew file. Three suites running concurrently in three worktrees
write the same path. A `crewId` collision between a fixture and a real crew silently rewrites a
real crew's history — the same defect class as the spent achievement, one order of magnitude
worse, because a crew save is the only durable record of hours of play.

**The rule, and it is not optional.** Follow the precedent the achievement bug produced: gate on
*what kind of session this is*, not on a build flag.

1. **The crew save is written only when the session has a human in it.** The predicate already
   exists and is already the right one: `IIntentSource.IsHumanInput`, used at
   `SandboxAvatar.cs:904` for exactly this reason. A server whose peers are all scripted writes
   nothing.
2. **Plus an explicit override for the suites that must exercise the save path:**
   `--crew-save-dir <path>`, on the `--log`/`LogDir` model (`LaunchOptions.cs:102`). Every suite
   that touches the save passes a per-run directory under `tests/logs/`. **Absent the flag and
   absent a human, no file is written and none is read.**
3. **`crewId` for a fixture is a reserved sentinel range** so a fixture can never collide with a
   real crew even if rule 1 is bypassed.

**For the suites specifically:** `Run-CourierSyncTest.ps1` (§11) must pass `--crew-save-dir` and
must assert the real `user://crew/` directory is untouched — an ABSENCE check, which needs its
own positive control (§11.6).

---

## 8. Radio — crew-wide PTT on the PA route

### 8.1 What exists

`VoiceRoute.Pa` is a real route with real handling: no distance falloff, routed through a PA bus
with lowpass/distortion/reverb (`VoiceSpeaker.cs:8-17`, applied at `VoiceSpeaker.cs:265-278` —
`AttenuationModel = Disabled`, `MaxDistance = 0`, `VolumeDb = -6`). The route is re-resolved
every tick from replicated state (`VoiceSpeaker.cs:19-27`).

**What is missing is the predicate.** `VoiceManager.PaResolver` (`:90`) is `null` in every
production path; the only thing that ever sets it is the test flag `--voice-pa-all`
(`Gameplay.cs:651-654`, `LaunchOptions.cs:631-636`, `:1238-1239`).

### 8.2 Membership

**All crew, for the MVP.** There is one crew per session and it is every connected peer, capped
at 6 (`NetProfile.cs:148`). **No membership replication is needed** — `Multiplayer.GetPeers()` is
already the crew. Adding a roster now would be a knob with one legal value (canon 0.3). The
`CrewState` node is where a roster lands the day sub-crews exist, and its slot is `CrewId`.

### 8.3 The binding — one replicated bit, and it is already free

The transmitting state must be replicated, because `PaResolver` is read on **both** sides:
`VoiceManager.cs:80-89` makes this a hard contract — *"THE CONTRACT THIS PUTS ON WHOEVER WIRES
PA: set it on the server as well as on clients. A client-only wiring is safe while
`VoiceProximityGate.EnabledByDefault` is false (nothing is culled) and silently wrong the moment
it is true."*

**Use the snapshot's one spare bit.** `flags2` bit 7 is free and is documented as the last one:

> *"Bit 7 of the snapshot's `flags2` byte is FREE — one spare"* — `NetCodec.cs:126-132`
> (`Flags2SnapshotSpare = 128`).

`MoveState.RadioTransmitting` → `flags2` bit 7 → packed at `NetCodec.cs:304-307`, unpacked at
`:371-372`. **Zero added bytes; `SnapshotBytes` stays 58.**

Why this and not a reliable RPC on a new channel:

- **It is per-avatar continuous boolean state, which is exactly what the snapshot carries.** A
  reliable per-keypress RPC would need its own channel, its own late-join dump and its own
  `Synced` bool — three new things to carry one bit that the existing 30 Hz stream carries for
  free, including on late join (the snapshot *is* the late-join mechanism for per-avatar state).
- **Both sides resolve the same predicate from the same replicated state**, which is precisely
  what `VoiceManager.cs:82-83` requires: *"PA is a pure function of replicated state and the
  server is the source of that state, so the same predicate resolves identically on both sides."*
  The server reads `MoveState` directly; a client reads the snapshot it just unpacked.

**The cost, stated because the changelog demands it: this spends the last spare snapshot bit.**
After v15 the next snapshot flag is a length change (`NetProfile.cs:64-67` says this in advance
about the first flags byte, and `NetCodec.cs:126-132` says it about the second). That is a real
price and it is why the alternative is written out above rather than dismissed. **It is worth
paying**, because the alternative costs a channel, a dump and a sync bool for one bit, and canon
0.3 says the mechanism with fewer moving parts wins when the tenets above it are equal — which
here they are: both are correct, both are safe, and the snapshot is faster.

**Input.** The PTT key is client-local. Voice capture is client-side (`VoiceManager._capture`)
and PTT gates capture; the *replicated* bit is set from the owner's intent on the same tick, so
`MoveIntent` gains a `RadioPtt` bool riding **one of `buttons2`'s seven free bits**
(`NetCodec.cs:178`). `InputEntryBytes` stays 26.

### 8.4 The production resolver

Set on **both** the server and every client, at the same site `--voice-pa-all` uses today
(`Gameplay.cs:651-654`), unconditionally rather than behind a flag:

```csharp
VoiceManager.Instance.PaResolver = peerId =>
{
    SandboxAvatar? a = AvatarFor(peerId);
    return a != null && a.RadioTransmitting;   // read off the replicated MoveState
};
```

`UnbindPlayersRoot` already clears it (`VoiceManager.cs:152`), so the teardown path is done.

### 8.5 The proximity gate

`VoiceProximityGate.EnabledByDefault` is **`false`** (`:38`) and stays false — flipping it
reverses a documented architectural stance and *"That is Talon's call, not an agent's"*
(`:16-20`). With the gate off, PA and proximity relay identically and the radio is free.

**The interaction, for the day it is flipped:** the exemption reads the same `PaResolver` on the
server (`:22-30`), which §8.4 now wires — so flipping the gate no longer silently mutes the
intercom. The server logs which way it went at its first gated relay (`:29-30`), so a muted PA is
diagnosable from a log rather than from a playtest. **§8.4 is the prerequisite that makes the
gate flippable at all.**

### 8.6 Bandwidth delta per speaker

From the one measured figure in the repo (`VoiceProximityGate.cs:11-14`): host upstream at six
players is **~1.5 Mbps, of which ~two thirds is voice**, and culling by proximity is worth
roughly **120 → 48 KB/s** of host upstream.

- **Gate off (today, and the shipping default):** **zero delta.** Every accepted peer already
  gets every voice packet (`:34-36`); routing one through the PA bus instead of a 3D attenuator
  is a client-side audio-bus decision and moves no bytes.
- **Gate on:** 120 KB/s ÷ 6 talkers ÷ 5 recipients ≈ **4 KB/s per speaker per recipient**, so one
  crew-wide PTT speaker costs the host **up to ~20 KB/s** that the gate would otherwise have
  saved. Six simultaneous PA talkers is the ungated 120 KB/s exactly — i.e. **the worst case of
  crew-wide PTT is today's shipping cost**, which is the honest bound and is why this is
  affordable.

---

## 9. Dispatcher — out of the playtest

**Out, and the reason is not a judgement call.** There is no media path in this repository:
`grep -rl "VideoStream\|VideoPlayer\|CameraFeed" scripts/` returns **nothing**. A
switchboard/camera dispatcher as described needs one, and building a video path is a program, not
a packet. Independently, host upstream is already ~1.5 Mbps at six players with two-thirds of it
voice (`VoiceProximityGate.cs:11-14`); adding even one video stream to that budget is the kind of
change canon 0.2 (*"performance and security beat anything flashy"*) decides without discussion.

**The cheapest future shape, and it needs no new wire at all.** A dispatcher is a peer whose
camera follows another peer's **already-replicated avatar**. Every ingredient ships:

- Remote avatars are already fully replicated on every peer at 30 Hz
  (`NetCodec.SnapshotBytes = 58`, `NetProfile.SnapshotIntervalTicks = 2`).
- A follow camera on a chosen target already exists — `--spectate-cam` attaches one
  (`LaunchOptions.cs:22`, `:785`), `Gameplay.cs:827` notes the Players root already carries a
  spectate camera, and `SandboxAvatar.cs:833` explicitly says the seam *"is what a future
  spectate mode will use."*
- The radio (§8) is the dispatcher's voice, already crew-wide and already free.

So the dispatcher is: **a UI for picking which peer to follow, plus the shipped spectate rig,
plus §8's radio. No video, no new channel, no protocol change.** That is the whole design, and it
should be built *after* the delivery loop is proven, not before.

---

## 10. Protocol 14 → 15 — the changelog entry

**Paste this into `NetProfile.cs`'s `ProtocolVersion` doc block, after the v13 paragraph
(currently ending at `:103`), and change `:110` to `15`.** Format matches the existing entries.

```csharp
/// <para><b>v15 (2026-09-04, ECON-1 — the courier economy: crew state, packages, the radio).</b>
/// The largest single bump in this list, and deliberately ONE bump rather than four: the crew
/// ledger, the package wire, the radio bit and the new prop kind all ship together, so a peer is
/// either wholly on the courier build or refused at the gate. Landed in v15:
///
/// <b>1. Two new PropKind ordinals' worth of hazard, one ordinal spent.</b>
/// <c>PropKind.Package = 2</c> (after Crate = 0, Ball = 1). This is the data-shape change v8's
/// own note calls the worst kind: a stale client receiving an unknown kind used to fall through
/// <c>NetworkedProp._Ready</c>'s switch to the Crate default and render the wrong object instead
/// of failing loudly. In this bump that default arm is replaced by an explicit arm per kind plus
/// a loud <c>GD.PushError</c> fallback, so the silent-wrong path no longer exists even if the
/// gate is ever wrong.
///
/// <b>2. Two new reliable channels.</b> <see cref="CrewChannel"/> = 17 (CrewState: reputation,
/// rent period, rent due, unlocked tier — absolute broadcasts, a late-join dump, a typed denial)
/// and <see cref="PackageChannel"/> = 18 (PackageRegistry: per-package absolute broadcasts, a
/// removal event, a per-package late-join dump, a typed denial). Next free channel is now 19.
/// New channels do not by themselves force a bump — SightChannel (13) and FlashlightChannel (16)
/// both argued their way out of one on fail-blind/fail-dark grounds — but neither of these fails
/// blind: a peer without CrewState renders a rent of 0, which is a WRONG number rather than an
/// absent one, and a peer without PackageRegistry renders a package as an unmarked prop it can
/// pick up and carry to nowhere. Both are the silently-wrong case this field exists to refuse.
///
/// <b>3. New RPC methods, none of which a v14 build has anything to dispatch to.</b>
/// CrewState: BroadcastCrewState, SyncCrewStateTo, OnCrewDenied.
/// PackageManager: BroadcastPackage, BroadcastPackageRemoved, SyncPackageTo, OnPackageDenied.
/// (v7's precedent: a brand-new RPC method is by itself sufficient grounds.)
///
/// <b>4. The snapshot's LAST spare bit is spent — flags2 bit 7 becomes RadioTransmitting</b>
/// (<c>NetCodec.Flags2SnapshotSpare</c>, documented as the one spare since v13). Packet LENGTH is
/// unchanged at 58 bytes, which is exactly why the bump is mandatory rather than optional — the
/// v9/v10/v13 case, three times over: a v14 peer would parse a v15 snapshot without any error at
/// all and simply never route a teammate's voice through the PA bus, and with
/// VoiceProximityGate enabled would silently cull the intercom it could not see was open.
/// <b>The snapshot's flags2 byte is now FULL. Both snapshot flags bytes are now full.</b> The
/// next snapshot flag is a layout change and a length change, not another spare-bit reuse.
///
/// <b>5. One input bit spent: buttons2 gains RadioPtt</b> (the owner's push-to-talk edge, which
/// is what sets the flag in 4 authoritatively). <c>InputEntryBytes</c> is UNCHANGED at 26 —
/// buttons2 had seven free bits since v12 and now has six. No length change, meaning change only,
/// which is v6's original case.
///
/// <b>6. Two appended wire enums, ordinals only.</b> <c>RunOutcomeKind.RentMissed = 1</c> (the
/// first ordinal appended to that enum since it shipped with one) and <c>PackageFlags</c>, a new
/// [Flags] enum whose bits ride the package broadcast. Both APPEND-ONLY; the UI-layer mirror
/// <c>Sail.Ui.Flow.RunOutcomeKind</c> must be appended in the SAME edit — LiveFlowViews.ToView is
/// a straight numeric cast, so a mirror one ordinal out of step shows the wrong loss screen with
/// no error anywhere.
///
/// <b>Bandwidth cost, priced rather than assumed.</b> Snapshot: <b>0 bytes</b> — the radio bit
/// rides an existing byte. Input: <b>0 bytes</b>. The two new channels are event-driven and
/// bursty, not continuous: a full six-player mailroom sort is bounded by the pile size (one
/// ~30-byte reliable message per package per re-assignment) and the steady state between sorts is
/// silent. The crew ledger is one ~24-byte message per delivery. Against the 48.3 kB/s baseline
/// for six avatar streams, the courier economy's steady-state contribution is under 1%.</para>
```

Also edit in the same commit, or the entry lies:

- `NetProfile.cs:110` → `public const int ProtocolVersion = 15;`
- `NetProfile.cs:176-181` — the channel ladder comment: add `17 Crew · 18 Package`, and change
  **"NEXT FREE CHANNEL IS 17"** to **19**.
- `NetCodec.cs:126-132` — `Flags2SnapshotSpare` is no longer spare; the comment becomes "flags2
  is now FULL".
- `NetCodec.cs:178` — "buttons2 keeps its seven free bits" → six.
- `NetProfile.cs:64-67` — the "the snapshot flags byte is now FULL" note now covers both bytes.

---

## 11. The proof harness — `tests/Run-CourierSyncTest.ps1`

### 11.1 The ruling on the `Run-BubbleSyncTest` red

**Required by the dispatch correction, and it is settled by the logs, not by inference.**

`tests/Run-BubbleSyncTest.ps1` ("Bubbles: shared counter", registered at
`Run-AllTests.ps1:92`) fails reproducibly with **exactly one** failure:

```
BUBBLE SYNC TEST FAILED (1 failure(s)):
  - C (late) never saw the reset return the tally to 0 and every bubble to visible
```

**Verdict: (b) — the harness composition is at fault, not the game-side shared counter, and not
the replication path.** The evidence, from the discriminated run's own logs
(`C:\repos\Watis-move1-base\tests\logs\`, 2026-09-04 17:26 UTC, idle machine):

**The disconfirming fact for (a), and it is decisive.** The server's log has the events in this
order:

```
2026-09-04T17:26:47.636Z INFO [server] peer left peer=1681046017 players=2
[bubbletest] reset lever pulled byPeer=1
[bubbletest] reset
```

**Bot C had already disconnected from the server before the reset broadcast was emitted.** The
broadcast did not fail to reach C — it was never addressed to C, because C was gone. A
replication defect requires the peer to have been connected when the message went out; it was
not. There is no version of (a) consistent with this log.

**Why C was gone, arithmetically.** C's lifetime is `--duration $LateDurationSec` = **26 s**
(`Run-BubbleSyncTest.ps1:59`, applied at `:126-128`), measured from **C's own launch**. The reset
is `--bubble-reset-at $ResetAtSec` = **30 s** (`:57`), measured from **the server's** launch,
plus the lever's ~4 s two-stage arm/confirm. C connected at 17:26:21.62 and exited at 17:26:47.64;
the accepted pull landed ~17:26:47.7. **C missed the broadcast by roughly 100 ms**, and nothing in
the script gates one clock against the other. Sample counts confirm it: A and B logged 176 samples
over 36.4 s and C logged 128 over 26.4 s — **the same 4.8 Hz sampling rate over a window 10 s
shorter, ending 5 s earlier.** (Note for the record: the shorter window is C's *earlier exit*, not
"a common exit wall-clock" — `$WitnessDurationSec` is 36 and `$LateDurationSec` is 26.)

**Why it is byte-identical across runs.** Both clocks are deterministic on an idle machine, so
the race lands the same way every time. **Reproducible is not the same as correct** — this is a
race that always loses, which is the most misleading kind.

**The counter itself is proven, positively, by this very log.** A and B both observed
`count 2 → 0` with empty `popped` and empty `hidden` after the reset (25 and 27 post-reset
samples respectively); C's very first sample was `synced=true, count=2, popped=[2,4],
hidden=[2,4]`; the client-side forge appeared as `hidden=[0,2,4]` on all three peers and moved no
tally on any of them; and every lever control passed (`armed=2 accepted=0 tally 2 -> 2`, then
`accepted=1 refused=1`, one broadcast).

**What is and is not proven, stated precisely, because ECON-1 depends on the distinction:**

- **The late-join DUMP (`SendXTo`) is proven.** C's first sample is correct and synced. §2's and
  §3's dump design rests on a mechanism this suite verifies.
- **The late-joiner BROADCAST-after-dump path is UNPROVEN on today's `main` — not disproven,
  unproven.** The only suite with a late joiner exits it before the only post-dump state-changing
  broadcast, so no assertion in this repository has ever observed a late joiner adopt one. That
  is the exact shape of every RPC in §2, §3 and §6 and of ECON-1's own acceptance criterion 5.

**Conclusion for §11's shape: the model may be copied.** Its structure — three processes, three
independent logs, observation-keyed convergence assertions, an absence check with a positive
control for each — is sound and is what makes this ruling possible at all. **One composition rule
inside it must not be copied**, and it is named in §11.2.

*Fixing `Run-BubbleSyncTest` is FIX-1's, not this packet's, and not ECON-1's.*

### 11.2 The timing property `Run-CourierSyncTest` must have

**A late joiner's sampling window must provably span every state-changing broadcast it is
asserted against — and the span must be established by a log-line gate, never by a duration.**

Three mechanical rules, all of which `Run-BubbleSyncTest` satisfies for its *launches* and none of
which it satisfies for its *exits*:

1. **Every scheduled server-side event is gated by `Wait-ForLogLine` on the server's own log
   before any bot is allowed to exit** — the script already does this to *launch* C
   (`:119-124`, gating on `[bubbletest] pop id=…`); it must equally do it to *retain* C.
2. **A late joiner's `--duration` is derived from the observed gate, not from a constant.** Launch
   the late bot with a duration long enough to cover the worst case, and let the assertions —
   which are observation-keyed — decide when enough has been seen.
3. **The sample count is its own positive control.** Assert that the late joiner logged at least
   one sample **strictly after** the sample in which the broadcast's effect first appears on the
   server-present peers. A window that closed early then fails as *"the late joiner's window
   closed before the broadcast; this assertion was vacuous"* — naming the harness — instead of as
   *"the broadcast never arrived"* — accusing the game. **That distinction is worth the whole
   assertion**, and its absence is what cost a day here.

### 11.3 Fixture and processes

`--world open` (the code-built CI slab, `ARCHITECTURE.md:89-95`) plus a
`--courier-selftest` fixture on `BubbleSelfTest`'s model: installs a known set of packages at
known points with known flags, a destination marker, and a scripted assignment/delivery schedule
on `--quota-bank-at`'s model (`QuotaLedger.cs:101-104`, `:264-279`) — a server-side timed
schedule keyed by **connect index**, never by peer id, because *"peer ids are unpredictable;
connect order is not"* (`:258-260`).

- **Server:** `--server --world open --courier-selftest --courier-deliver-at <t> --net-sim …`
- **Bot A (the rider):** joins first (connect index 0 → `GameWorld.SpawnPoints[0]`), is assigned a
  Fragile package, carries it into a wall at sprint, then delivers.
- **Bot B (the witness):** present throughout, stands still, witnesses everything.
- **Bot C (the late joiner):** launched **only after** the server's log confirms both an
  assignment and a delivery, and **retained until** the server's log confirms the post-join
  broadcast it is asserted against (§11.2).
- **Bot D (the reconnector):** `--force-reconnect-at <sec>` (`LaunchOptions.cs:405`, `:1085`),
  assigned a package before the drop.

All bots run under `--net-sim <latencyMs>,<lossPct>,<jitterMs>` (`LaunchOptions.cs:155-161`,
`:929-938`). All must pass `--crew-save-dir` under `tests/logs/` (§7.7).

`BotHarness.Sample` gains a `CourierSample` record on `BubbleSample`'s model
(`BotHarness.cs:583-590`): `{ Synced, PackageIds[], Flags[], AssignedTo[], CrewRep, CrewRentDue,
QuotaBanked }` — `null` outside `--courier-selftest`, exactly as `Bubble` is.

### 11.4 The six assertions, each with its positive control

| # | Assertion | Positive control — what makes it non-vacuous |
|---|---|---|
| **1** | **Same package id space on every peer.** Every peer's every sample reports the same `PackageIds` set as the server's fixture constant, restated in the script rather than derived (`Run-BubbleSyncTest.ps1:66-72`'s discipline). Checked **first**: an id space that disagrees makes every later assertion meaningless. | The fixture's package count is asserted `> 0` and asserted equal to the restated constant. A zero-package run passes every other assertion vacuously. |
| **2** | **Ledger and flags agree on all peers.** At every sample after the delivery, all three of A/B/C report the same `QuotaBanked`, the same `CrewRentDue`, and the same `Flags` for every package id — compared **across three separate processes' log files**, never within one. | The banked value must have **changed** during the run: assert a sample with the pre-delivery value and a sample with the post-delivery value both exist on each peer. "All peers agree on 0" is agreement about nothing. |
| **3** | **The late joiner's FIRST sample is `Synced` and correct.** C's first `CourierSample` has `Synced == true`, the full package id space, the post-delivery flags, the post-delivery `QuotaBanked` and the correct `AssignedTo` for A's package. | The state C is asserted to have adopted must be **different from the initial state** — assert `QuotaBanked > 0` in C's first sample. A dump that happens to match a zero-initialised client proves nothing (this is the hazard `Gameplay.cs:779-781` names in prose: "the zero default here looks like 'nothing banked yet', a true-sounding wrong answer"). |
| **3b** | **The late joiner adopts a broadcast that arrives AFTER its dump.** A second delivery is scheduled after C joins; C's samples show the ledger moving. **This is the assertion no suite in this repo currently makes (§11.1).** | §11.2's rule 3: assert C logged at least one sample strictly after the server logged the second delivery, and fail with *"C's window closed before the broadcast — this assertion was vacuous"* if not. |
| **4** | **A forged client mutation moves nothing.** The fixture makes every **client** locally attempt a package mutation (re-flag, re-assign, mark delivered) — `BubbleSelfTest`'s forge, `Run-BubbleSyncTest.ps1:27-29`. No peer's `Flags`/`AssignedTo`/`QuotaBanked` ever reflects it, on any sample. | **The forge must be observed to have run.** As in the bubble suite (`:341-346`), the client-local effect must appear somewhere in the sample stream (a local-only "attempted" counter). Without it, "no client path mutates" was never exercised. This is precisely the control that *did* fire in the bubble run (`hidden=[0,2,4]`), which is why that assertion is trustworthy there. |
| **5** | **A Fragile package carried into a wall at speed shows `Damaged` on all peers.** Bot A sprints into a fixture wall while holding a Fragile package; every peer's subsequent samples show `Damaged` set on that package id. | Two controls, both required: **(i)** assert the flag was **absent** before the impact on all peers — otherwise the fixture might have spawned it damaged; **(ii)** assert the server logged **exactly one** `[courier] damaged id=… impact=…` line — the idempotency proof from §5.2, and the thing that catches a per-tick broadcast storm. |
| **6** | **A reconnecting peer keeps its assignment.** Bot D drops and returns via `--force-reconnect-at`; its `AssignedTo` is unchanged across the gap on every peer. | The assignment must be **non-default before the drop** — assert D's package shows `AssignedTo != 0` in a pre-drop sample on all peers, or "unchanged" is a statement about 0. |

**All six run under `--net-sim` loss and jitter**, so every one of them is also a proof that a
dropped reliable message is retried rather than lost.

### 11.5 Assertion 6 has a wall, and the script must not pretend otherwise

**`AssignedToSteamId` is 0 for every bot.** `NetworkManager.SteamId64Of` returns 0 on the ENet
transport (`NetworkManager.cs:462-463`), and `Gameplay.cs:961-965` already records the
consequence for the reconnect work: *"OnPeerConnected's call to this can never actually take the
resumed=true branch in ENet-bot CI."* Assertion 6's positive control (`AssignedTo != 0`) is
therefore **unsatisfiable as written in a headless suite**.

Three ways out. ECON-1 must be told which; the choice is a direction, so it is in Open questions.

1. **Prove it at the pure-logic layer.** A `dotnet test` / self-test over `PackageRegistry` with
   an injected identity, on `ReconnectSelfTest`'s explicit precedent — *"the live Steam path is
   Talon's next interactive session"* (`ReconnectSelfTest.cs:163`, `Gameplay.cs:961-965`).
   **Cheapest, and it is what this repo already does for the same wall.** The scene suite then
   asserts the *weaker* live property: D's package is still in the registry and still not
   assigned to anybody else.
2. **A test-only stable identity for ENet bots** (`--fake-steamid <id64>`), making the resumed
   branch reachable in CI for the first time. **Buys a live proof of a path that has never had
   one** — and touches the identity the auth boundary and rate limiter key on
   (`NetworkManager.cs:465`), which is a security surface. Not an agent's call.
3. **Key assignments on the transport peer id instead.** Cheapest to test, **and wrong**: peer ids
   are recycled every connection (`ReconnectRegistry.cs:8-10`), so an assignment would follow
   whoever inherits the id. Listed so nobody proposes it as an optimisation later.

**Recommended: (1) now, (2) surfaced.** Do not let assertion 6 be written in a form that passes
vacuously.

### 11.6 The save's absence check

If Q7 says crews persist: assert the real `user://crew/` directory is **byte-identical before and
after the run** (§7.7). Positive control: assert the run's own `--crew-save-dir` **did** receive a
file — otherwise "the real profile was untouched" is true of a run that wrote nothing anywhere.

---

## What ECON-1 builds first

Ordered. Each step is independently verifiable before the next begins.

1. **The protocol bump and the ladder, in one commit, first.** `ProtocolVersion = 15`, the
   changelog entry from §10, channels 17 and 18 in the ladder, "next free is 19". Doing this last
   is how two packets both claim channel 17.
2. **`NetworkedProp`'s default arm** (§3.8) — one switch, before any new `PropKind` exists to fall
   through it.
3. **`PropKind.Package = 2`** and the authored package prefab.
4. **`PackageRegistry`** — pure logic, no scene tree, headless-testable, on `PropRegistry`'s
   model. Unit tests before `PackageManager` exists.
5. **`PackageManager`** — the node, its four RPCs, `SendPackagesTo`, its `IMapScopedSlice`
   registration, and its funnel entry after `SendCrewStateTo` (§3.7).
6. **`CrewState`** (§2.4) and its funnel entry after `SendQuotaTo` (§2.5).
7. **The rent schedule** — the `.tres` edit (§6.1). Two lines, and it is the whole escalation
   curve.
8. **Delivery → `QuotaLedger.ServerBank`** (§6.2), observing the bank-before-consume ordering.
9. **`RunOutcomeKind.RentMissed` + `RentMissedPredicate`**, both mirrors appended in the same edit
   (§6.3).
10. **`Lost`** (§5.1) — the kill-plane flag and the lake-floor rule.
11. **`Damaged`** (§5.2) — the `out _` → `out ev` change at `SandboxAvatar.cs:1799-1800` and the
    idempotent marker.
12. **The radio** (§8) — `flags2` bit 7, the `buttons2` PTT bit, and the production `PaResolver`
    on both sides.
13. **`Run-CourierSyncTest.ps1`** (§11), written to §11.2's timing property from the first line.
14. **The save** (§7) — **only if Q7 says crews persist**, and only with §7.7's gating in place.

**Two things ECON-1 must be told before it starts:**

- **Do not dispatch ECON-1 believing the late-joiner broadcast path is proven. On today's `main`
  it is not** (§11.1). The late-join *dump* is proven; the late-joiner *broadcast-after-dump* has
  never been observed by any assertion in this repository, because the only suite with a late
  joiner exits it before the only such broadcast. ECON-1's acceptance criterion 5 is the first
  time it will be tested, and §11.2's timing property is what makes that test real.
- **`user://` is one shared profile for every worktree on this machine** (§7.7). Any save work
  that ignores this will overwrite Talon's real data, and the precedent is already in the code at
  `SandboxAvatar.cs:895-903`.

---

## Open questions

Ripe: trigger named, options stated, costs attached. **None of these is a value call — every one
is a direction.**

1. **Q6 — the rent period.** §6.4 carries all three arms with costs and recommends the day/night
   cycle with the period a knob. *Trigger: ECON-1 step 7.* Arms (B) and (C) each require
   machinery that does not exist; (A) requires only a number.
2. **Q7 — do crews persist across sessions?** §7 is built either way. **Yes** costs a versioned
   host-owned file, the conflict rules of §7.5, and the `user://` hazard of §7.7. **No** costs
   nothing and deletes §7 entirely. *Trigger: ECON-1 step 14 / ECON-2's save row.*
3. **Q8 — one package per rider, or several?** §4 recommends single-slot and costs N-slot
   precisely: three of `buttons2`'s seven free bits, four one-line code changes, and **a new
   stowed carry pose**, which is `ANIMATION-CONTRACT.md` work rather than netcode. *Trigger:
   ECON-1 step 3.* **If N-slot is wanted at all, the input bits must be spent in the v15 bump** —
   spending them later is a second bump.
4. **Identity in CI (§11.5).** Assertion 6 cannot be proven headlessly because
   `SteamId64Of` returns 0 off Steam. Arms: prove it at the pure-logic layer (cheap, the repo's
   existing answer to this exact wall); or add a test-only stable ENet identity, which **touches
   the auth boundary and rate limiter** and is not an agent's call. *Trigger: ECON-1 step 13.*
5. **Hosting is a role with continuity attached (§7.5).** If crews persist, a crew's canonical
   history lives on whichever member is hosting, and there is no arbitration between two hosts
   with the same crew id. That is a *design* consequence — "the host is the one who remembers" —
   not a bug, and Talon should know it before it is discovered in a playtest. *Trigger: the first
   session where a different member hosts.*
6. **The proximity gate stays off (§8.5).** `VoiceProximityGate.EnabledByDefault = false` and
   flipping it reverses a documented architectural stance; the file says explicitly that this is
   Talon's call and not an agent's (`:16-20`). §8.4 removes the one thing that made flipping it
   dangerous (a PA with no server-side resolver), so **the question becomes askable for the first
   time**: flipping it is worth ~120 → 48 KB/s of host upstream. *Trigger: the first six-player
   playtest with bandwidth complaints.*
7. **The encumbrance ceiling (§5.3).** Mass stops mattering at ~14.9 kg because the speed floor is
   0.55. If the design wants a package heavy enough to genuinely change what a rider can do, that
   is a **capability denial**, not a heavier number — MECHANICS-BIBLE §8.3's research default,
   still marked *pending Talon confirmation*. *Trigger: the first "this package should feel
   heavy" playtest note.*

---

*NET-1, systems-design. Read-only on code; no suite was run and none was needed (§11 rules on
existing evidence rather than generating new evidence). Every number above is measured from the
files cited or quoted from them; none is invented.*
