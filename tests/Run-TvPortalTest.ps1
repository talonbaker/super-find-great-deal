<#
.SYNOPSIS
    BT-10: the TV easter egg. Two phases -- a headless self-test that drives the real portal
    triggers through a hub -> room -> hub round trip, and a two-peer bot run that proves the
    teleport converged on BOTH peers and that the transition flash reached exactly ONE of them.

.DESCRIPTION
    PHASE 1, --tvportal-selftest (offline, one process). Instantiates the real shipped
    scenes/game/world/bubbletest/BubbleTest.tscn and asserts:

      - two TvPortals per BubbleTestLayout.TvRoutes row exist -- an entrance named for its row
        and that route's own room return TV, found by path (five rooms all call theirs RoomTv);
      - every entrance is aimed at ITS OWN room's arrival point and each room's TV is aimed
        back above ground -- checked as positions, not as "not zero" (Vector3.Zero is the hub's
        own centre, so an unset Destination would teleport a player somewhere plausible);
      - no two arrivals land within 8 m of each other, which is Talon's note 9 stated as an
        assertion: two entrances quietly sharing one room would pass every other check here;
      - a body placed in EVERY entrance's REAL trigger volume ends up in that entrance's room
        and then walks back out of it -- ONE REAL ROUND TRIP PER ROUTE, not one standing in for
        the rest. The physics server has to notice; a test that called ServerTeleportTo itself
        would pass with the trigger on the wrong mask, or with nobody subscribed, or with the
        Area3D buried behind the cabinet hull;
      - re-entering the same trigger 250 ms later does NOT teleport again -- the 800 ms gate,
        probed once on the first route because the gate is one host's per-peer state;
      - a body parked in a room is not respawned: the room floor is -20 and VoidKillY is -70,
        and the room is 3 m from the origin against a 210 m off-map radius. Both of those are
        why a missing way out would be unrecoverable rather than merely annoying.

    PHASE 2, two peers. A dedicated headless server plus two scripted bots in the bubbletest
    world:

      - TvBotA walks into the hub TV at (-22, 0.7, -21.6);
      - TvBotB walks to a point on the far side of the hub and stays there -- a pure witness.

    Each bot's BotHarness JSONL carries EVERY peer's position as that client sees it, so A's
    trip is read out of B's log as well as A's own. That is the cross-peer assertion; a single
    bot's own view could be a local prediction that never happened on the server.

    THE FLASH CHECK IS AN ABSENCE CHECK AND IT CARRIES ITS CONTROL. "The watcher saw no flash"
    is worth nothing on its own -- a run where the flash never fired at all would also pass it.
    So the mover's log MUST contain [bubbletest.tv.flash] (the positive control) and the
    watcher's log MUST NOT. Both, or the assertion is unproven and this script says so.

    NOT IN Run-AllTests.ps1 YET, deliberately. Phase 2 is currently BLOCKED on a conflict this
    packet does not own (NetProfile.KillPlaneY = -30 vs the TV room's floor at -40 -- see the
    diagnosis this script prints, and the BT-10 report). Wiring a knowingly-red test into the
    shared runner would turn every parallel packet's suite red for a reason none of them caused.
    It joins the runner the moment that conflict is resolved.

    Exit 0 = PASS. No human interaction. Headless throughout -- no GPU, no display.
#>
[CmdletBinding()]
param(
    [int]$Port = 45719,
    # 18 -> 26 at MRF-B / F13, 2026-08-30, and DERIVED rather than nudged. #355 moved the hub
    # spawn ring's centre to (0, 0, 18) with radius 6, so the walk to the hub TV's release point is
    # 40.2 m from Spawn0 and 50.7 m from Spawn3, the far side of the ring. At MoveSpeed 3.8 m/s
    # behind a 9 m/s^2 ramp that is 10.8-13.6 s depending on which spawn a bot draws -- against 18 s
    # this left as little as 4.4 s for the teleport, the samples inside the room and the walk back,
    # and a bot that drew the far spawn was one slow frame from a false "the portal is broken".
    # 26 s keeps at least 12.4 s after the worst-case arrival. Recompute this whenever the ring or
    # MoveSpeed moves; the walk is the whole of it.
    [double]$DurationSec = 26,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# The hub TV's trigger in world space: the portal sits at (-22, 0, -22) rotated 180 degrees about
# Y, and TvPortal puts its walk-in Area3D at local (0, 0.7, -0.42) -- which the rotation maps to
# +Z, i.e. the plaza side. Keep this in step with BubbleTestWorld.HubTvPos; the self-test reads
# the live node instead and would catch a drift between them.
#
# THE TARGET IS DELIBERATELY 1.3 m PAST THE TRIGGER, and that is not a fudge factor.
# WalkToPointIntentSource.ArriveRadius is 1.2 m (scripts/game/sandbox/IntentSources.cs:339): the
# bot releases its stick as soon as it is within 1.2 m of the point it was given, and the walk-in
# trigger is a 0.45 m-deep box. Aimed at the trigger itself, the bot parks a metre short of a
# screen it never touches -- which is exactly what the first two runs of this test did, and it
# looked identical to "the portal is broken". Aiming 1.3 m beyond it along the approach line from
# the SPAWN puts the release point ON the trigger.
#
# RE-DERIVED AT MRF-B / F13, 2026-08-30, because the approach line moved and this did not.
# The trigger centre is (-22, 0.7, -21.58) -- x half-width 0.40, z half-width 0.225 -- and the
# approach used to be measured "from the origin", unit (-0.714, -0.701). #355 moved the spawn ring
# to centre (0, 0, 18) radius 6, so the real approach from Spawn0 (0, 0, 12) is unit
# (-0.548, -0.837): a considerably steeper line down the +Z side. Re-projected, the old target
# (-22.93, -22.51) put the release point OUTSIDE the trigger from two of the six spawns
# (Spawn4 by 0.077 m in x, Spawn5 by 0.017 m) and 0.001 m inside it from a third (Spawn3) --
# i.e. the test was one spawn draw away from a red that would have read as "the portal is broken",
# which is the exact failure this comment block already records happening once.
#
# The value below is the trigger centre plus 1.3 m along Spawn0's approach unit. Checked against
# ALL SIX ring spawns rather than the nominal one, because a bot's spawn index is its join order
# (docs: "bot spawn = launch order"), so the test does not get to choose: the release point lands
# inside the trigger from every spawn, with 0.13-0.40 m to spare in x and 0.10-0.22 m in z.
$HubTvTrigger = @(-22.712, -22.667)
# The witness: the hub's opposite corner, nowhere near a screen.
$WitnessSpot = @(20.0, 20.0)

Write-Host "=== BT-10: the TV that is a doorway ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

# ---------------------------------------------------------------------------------------------
Write-Host "[1/3] round trip, cooldown and stranding checks (headless self-test)..." -ForegroundColor Cyan

$proc = Start-Godot @("--tvportal-selftest") "tvportal-selftest"
if (-not (Wait-ForExit $proc 90)) { Stop-Proc $proc; Write-Fail "the TV portal self-test did not exit in time" }
$selfOut = Join-Path $script:LogDir "tvportal-selftest.out.log"
# Exit codes: 0 clean, 1 a real defect in this packet's work, 2 everything this packet owns is
# green and a conflict it does not own is standing in the way. A 2 is carried to the end rather
# than thrown here, so phase 2 still MEASURES -- including the flash asymmetry, which works today
# and would otherwise never be exercised again while the blocker stands.
$blocked = $false
if ($proc.ExitCode -eq 2) {
    $blocked = $true
} elseif ($proc.ExitCode -ne 0) {
    Write-Fail "TV portal self-test exited $($proc.ExitCode); see tvportal-selftest.out.log"
}
if (-not (Select-String -Path $selfOut -Pattern "\[tvportal-selftest\] PASS" -Quiet)) {
    Write-Fail "the TV portal self-test never printed PASS; see tvportal-selftest.out.log"
}
# The measured lines are the evidence BT-13 reads, so they are echoed rather than left buried.
Select-String -Path $selfOut -Pattern "^\[tvportal-selftest\]  " | ForEach-Object {
    Write-Host "         $($_.Line.Trim())" -ForegroundColor DarkGray
}
Write-Host "        self-test PASS" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------------------------
Write-Host "[2/3] two peers: A walks into the hub TV, B watches..." -ForegroundColor Cyan

$aLog = Join-Path $script:LogDir "tvportalA.jsonl"
$bLog = Join-Path $script:LogDir "tvportalB.jsonl"
$procs = @()
try {
    $serverOut = Join-Path $script:LogDir "tvportal.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "bubbletest") "tvportal.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
        Write-Fail "server never reported listening; see $serverOut"
    }

    $botA = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "TvBotA",
        "--log", $aLog, "--duration", $DurationSec, "--world", "bubbletest",
        "--goto-script", "$($HubTvTrigger[0]),$($HubTvTrigger[1])") "tvportalA"
    $procs += $botA
    Start-Sleep -Milliseconds 400

    $botB = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "TvBotB",
        "--log", $bLog, "--duration", $DurationSec, "--world", "bubbletest",
        "--goto-script", "$($WitnessSpot[0]),$($WitnessSpot[1])") "tvportalB"
    $procs += $botB

    foreach ($p in @($botA, $botB)) {
        if (-not (Wait-ForExit $p ([int]$DurationSec + 90))) { Write-Fail "a TV portal bot never exited" }
    }
    foreach ($p in @($botA, $botB)) {
        if ($p.ExitCode -ne 0) { Write-Fail "a TV portal bot exited $($p.ExitCode); see the tvportal*.out.log files" }
    }
}
finally {
    Stop-Procs $procs
}

# ---------------------------------------------------------------------------------------------
Write-Host "[3/3] verifying the trip on both peers, and the flash on exactly one..." -ForegroundColor Cyan

function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

# The room: 12 x 12 in plan, 4 m ceiling. Generous on y so a body still settling onto the floor
# after the drop from the 0.4 m arrival height still counts as "in the room".
#
# The floor is PARSED from BubbleTestLayout.cs, never retyped. This band used to be the literals
# -41.5..-34.5, so when Talon ruled the room up to -20 (above NetProfile.KillPlaneY) no bot could
# ever satisfy it and the runner reported the old blocker on a level that was already fixed. A
# second copy of a constant is how this repo loses afternoons; parse it, so drift fails loudly.
$script:RoomFloorY = $(
    $layoutSrc = "$PSScriptRoot/../scripts/game/world/bubbletest/BubbleTestLayout.cs"
    $src = Get-Content -Raw -LiteralPath $layoutSrc
    if ($src -match 'TvRoomAnchor\s*=\s*new\(\s*[-0-9.f]+\s*,\s*(-?[0-9.]+)f') { [double]$matches[1] }
    else { throw 'could not parse TvRoomAnchor from BubbleTestLayout.cs' }
)
Write-Host ("  room floor parsed from BubbleTestLayout.TvRoomAnchor: y = {0}" -f $script:RoomFloorY)

function Test-InRoom($p) {
    return ([math]::Abs([double]$p.x) -le 6.5) -and
           ([math]::Abs([double]$p.z) -le 6.5) -and
           ([double]$p.y -ge ($script:RoomFloorY - 1.5)) -and
           ([double]$p.y -le ($script:RoomFloorY + 5.5))
}

$aSamples = Get-Samples $aLog
$bSamples = Get-Samples $bLog

# A's own id, read from its own log rather than assumed: peer ids are assigned by connect order
# and hard-coding 2 would silently pass on a run where the bots connected the other way round.
$aPeerId = [int]$aSamples[-1].self
Write-Host "        TvBotA is peer $aPeerId" -ForegroundColor DarkGray

function Get-PeerTrack($Samples, [int]$PeerId) {
    $track = @()
    foreach ($s in $Samples) {
        $p = @($s.peers) | Where-Object { [int]$_.id -eq $PeerId }
        if ($p -and @($p).Count -gt 0) { $track += @($p)[0] }
    }
    return $track
}

$aOnA = Get-PeerTrack $aSamples $aPeerId
$aOnB = Get-PeerTrack $bSamples $aPeerId
if ($aOnA.Count -eq 0) { Write-Fail "TvBotA never appears in its own log" }
if ($aOnB.Count -eq 0) { Write-Fail "TvBotA never appears in TvBotB's log -- the peers never saw each other, so nothing below means anything" }

$aInRoomOnA = @($aOnA | Where-Object { Test-InRoom $_ })
$aInRoomOnB = @($aOnB | Where-Object { Test-InRoom $_ })

$lastA = $aOnA[-1]
Write-Host ("        A's last own sample:      ({0:N2}, {1:N2}, {2:N2})" -f [double]$lastA.x, [double]$lastA.y, [double]$lastA.z) -ForegroundColor DarkGray
$lastB = $aOnB[-1]
Write-Host ("        A as B last saw it:       ({0:N2}, {1:N2}, {2:N2})" -f [double]$lastB.x, [double]$lastB.y, [double]$lastB.z) -ForegroundColor DarkGray
Write-Host "        samples with A inside the room: own=$($aInRoomOnA.Count) witness=$($aInRoomOnB.Count)" -ForegroundColor DarkGray

# The absence check and its positive control. The flash log line is printed by the peer that
# RECEIVES the flash, so the mover's process must carry it and the watcher's must not. Without
# the first half, a run in which the flash never fired at all would pass the second half.
$aOut = Join-Path $script:LogDir "tvportalA.out.log"
$bOut = Join-Path $script:LogDir "tvportalB.out.log"
$flashOnA = @(Select-String -Path $aOut -Pattern "\[bubbletest\.tv\.flash\]" -ErrorAction SilentlyContinue)
$flashOnB = @(Select-String -Path $bOut -Pattern "\[bubbletest\.tv\.flash\]" -ErrorAction SilentlyContinue)
Write-Host "        flash lines: mover=$($flashOnA.Count) witness=$($flashOnB.Count)" -ForegroundColor DarkGray

if ($flashOnA.Count -eq 0) {
    Write-Fail "POSITIVE CONTROL FAILED: the peer that travelled logged no [bubbletest.tv.flash] at all. The 'the witness saw no flash' assertion below is therefore unproven -- a run where the flash never fires would pass it too"
}
if ($flashOnB.Count -ne 0) {
    Write-Fail "the witness peer received $($flashOnB.Count) transition flash(es). The flash is local to the traveller by direction (THRILL-BIBLE section 12, 'The TV that is a doorway'): a watcher who also flashed would be handed an explanation for the vanishing that they did not earn"
}

# THE KNOWN BLOCKER, diagnosed rather than merely reported as "it did not work". The server
# log is the discriminator: if it shows the teleport firing and the body still never appears in
# the room, the portal is fine and something moved the player back -- which is
# SandboxAvatar.ServerTick's `if (_state.Position.Y < Carryable.KillPlaneY) DoServerReset()`,
# a SECOND out-of-bounds floor at NetProfile.KillPlaneY = -30 that the level's layout contract
# (program section 4 rule 3, which names only RespawnService.VoidKillY = -70) does not account
# for. The room's floor is -40, ten metres under it. Offline this never fires, because ServerTick
# only runs in NetRole.ServerSim -- which is why phase 1 passes and this phase cannot.
$serverLog = Join-Path $script:LogDir "tvportal.server.out.log"
$teleportFired = @(Select-String -Path $serverLog -Pattern "\[bubbletest\.tv\] peer=" -ErrorAction SilentlyContinue)
if ($aInRoomOnA.Count -eq 0 -and $teleportFired.Count -gt 0) {
    Write-Host ""
    Write-Host "BLOCKED (not a BT-10 defect): the server DID teleport the player into the room --" -ForegroundColor Yellow
    foreach ($l in $teleportFired) { Write-Host "    $($l.Line.Trim())" -ForegroundColor Yellow }
    Write-Host "  ...and the player was then snapped back to their spawn point on the next server tick." -ForegroundColor Yellow
    Write-Host "  Cause: TWO out-of-bounds floors exist and only one is per-world." -ForegroundColor Yellow
    Write-Host "    RespawnService.VoidKillY = -70  (set per world by BubbleTestWorld)      clears the room" -ForegroundColor Yellow
    Write-Host "    NetProfile.KillPlaneY    = -30  (global const, SandboxAvatar.ServerTick) cuts it off" -ForegroundColor Yellow
    Write-Host ("  RESOLVED 2026-08-28: Talon ruled the room up to y={0}, above KillPlaneY. If this" -f $script:RoomFloorY) -ForegroundColor Yellow
    Write-Host "  fires again it is a REGRESSION, not the original blocker -- check the anchor first." -ForegroundColor Yellow
    Write-Host "  BT-10 owns neither NetProfile, SandboxAvatar nor BubbleTestLayout's anchors, so it" -ForegroundColor Yellow
    Write-Host "  asserts the conflict rather than picking a side." -ForegroundColor Yellow
    $blocked = $true
}
if ($aInRoomOnA.Count -eq 0 -and -not $blocked) {
    Write-Fail "TvBotA never reached the TV room on its own peer -- it did not walk into the screen, or the portal did not fire (and the server logged no teleport at all, so this is NOT the kill-plane blocker)"
}
if ($aInRoomOnB.Count -eq 0 -and -not $blocked) {
    Write-Fail "TvBotB never saw TvBotA inside the TV room. The teleport did not replicate: the mover's own view moved and the witness's did not, which is the exact shape of a client-side-only teleport"
}

if ($blocked) {
    Write-Host ""
    Write-Host "PROVEN ANYWAY: three portals wired; hub -> room -> hub round trip through the real triggers; 800 ms re-entry gate holds; no void kill or off-map kill on a body idling in the room; the flash reached the traveller and only the traveller." -ForegroundColor Green
    Write-Fail "the kill-plane conflict has REGRESSED: the teleport fired but no body reached the room. This was fixed on 2026-08-28 by moving TvRoomAnchor above NetProfile.KillPlaneY -- check that constant before anything else"
}

Write-Host ""
Write-Host "PASS: three portals wired; hub -> room -> hub round trip through the real triggers; 800 ms re-entry gate holds; no void kill or off-map kill in the room; the teleport converged on both peers; the flash reached the traveller and only the traveller (control: the traveller's own flash line is present)." -ForegroundColor Green
Write-Host ""
Write-Host "TV PORTAL TEST OVERALL: PASS" -ForegroundColor Green
exit 0
