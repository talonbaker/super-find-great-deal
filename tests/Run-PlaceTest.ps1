<#
.SYNOPSIS
    The PLACE verb, server-authoritative: a prop set down at a scripted transform is observed at
    that transform by a DIFFERENT peer, and every illegal placement is refused with the right
    reason while the prop stays in the hand.

.DESCRIPTION
    CARRY-1's own gate (packet step 3, program doc §5b — placement integrity layer 1).

    Runs one headless server in the real "supermarket" world with --spawn-room search, so every
    bot spawns in the SEARCH room, which is the only room with authored props in it (SearchRoom.tscn:
    Prop_0..Prop_3, whose adopted ids are DERIVED from the server's own log at run time -- see
    the $PropA..$PropD block below for why
    those numbers moved), an authored RoomBounds volume, and a freestanding
    interior pillar. The pillar is there for exactly one reason: "inside a wall" needs a transform
    that is inside the room's BOUNDS and inside STATIC GEOMETRY at once, and every real wall is the
    boundary, so a crate pushed into one would fail the bounds test first and the overlap test
    would never be proved to fire at all.

    Four bots, each grabbing its own prop so nothing contends:

      - PlaceBotA  grabs 1003, carries it to (40, ·, 0), and PLACES it at a scripted transform
                   0.2 m further on, at 35 degrees of yaw. The legal case.
      - PlaceBotB  grabs 1006 and stands still, then asks to place it 5 m away. The beyond-reach
                   case. B is ALSO the independent witness to A's placement — the assertion that
                   matters is made on B's log, not on A's, because a holder agreeing with itself
                   about where it put something proves nothing about replication.
      - PlaceBotC  grabs 1004, walks to the pillar, and asks to place INSIDE it. Refused
                   DoesNotFitThere (4), prop still held.
      - PlaceBotD  grabs 1005, walks to the +X wall, and asks to place THROUGH it, at a transform
                   outside the room's bounds volume. Refused OutsideRoom (5), prop still held.

    Every bot logs one JSONL sample per tick (BotHarness), now including each prop's ORIENTATION
    (a quaternion — a crate that landed square but face-down is 180 degrees wrong and position
    alone cannot see it) and this peer's newest place-refusal ordinal.

    Asserted:

      1. Placed where asked, as seen by someone else - in bot B's LAST sample, prop 1003 is
         unheld and sits within 0.05 m and 5 degrees of A's intended transform.
      2. Beyond reach is refused    - B's newest refusal is TooFarToPlace (3) and B still holds 1006.
      3. Inside geometry is refused - C's newest refusal is DoesNotFitThere (4) and C still holds 1004.
      4. Outside the room is refused- D's newest refusal is OutsideRoom (5) and D still holds 1005.

    Assertions 2-4 check the REASON, not merely "nothing happened": a refusal for the wrong reason
    and a packet that never arrived both look like "the prop is still held", and the whole point of
    the PlaceDenial ordinal existing is that a player is told which of them it was.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    # 7898, NOT 7896 (INT-0, 2026-09-19). THREE lanes off BASE-1 -- ROUND-1, FP-1 and CARRY-1 --
    # each independently picked 7896 as "the next free port", because each was reading the same
    # Run-CarryNetTest 7893/7894/7895 ladder and none could see the others. The merge put all three
    # suites at the end of Run-AllTests.ps1's registry, where they run back to back on one socket.
    # ROUND-1 keeps 7896 (it documented the claim in three places), FP-1 took 7897, this takes
    # 7898. Measured rather than reasoned: the first post-merge run of Run-FirstPersonTest.ps1 on
    # 7896 died with `Couldn't create an ENet host` right after Run-RoundLoopSmoke.ps1 released it.
    [int]$Port = 7898,
    [double]$DurationSec = 26,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# --- the world, in world space -----------------------------------------------------------------
# SearchRoom.tscn is instanced at x = +40 by Supermarket.tscn, so every coordinate below is the
# room's own local value plus 40. Interior: x in [33, 47], z in [-5, 5], floor top y = 0.
# THE IDS MOVED, AND THEY WILL MOVE AGAIN (BTN-1, 2026-09-19). PropManager.AdoptAuthoredProps
# numbers authored props by sorting on NODE PATH across the whole world, so adding props to ANY
# room renumbers every room after it alphabetically. BTN-1 put three objects on a rack in
# HoldingRoom.tscn; "HoldingRoom/..." sorts before "SearchRoom/...", so those three took
# 1000..1002 and this room's four moved up by three.
#
# HOLD-1 (2026-09-19) merged BTN-1 and SHELF-1 into one tree and RE-DERIVED these four off the
# server's own adoption log rather than trusting either branch's copy. They did not move a second
# time, and the reason is worth keeping: SHELF-1's hundred-and-twenty-six products live under a
# container node named Stock/, and "S" sorts after "P", so they land ABOVE Prop_0..3 rather than
# under them. 1003..1006 is the value on the merged tree, verified against
# "[props] authored prop 1003 <- /root/Gameplay/World/SearchRoom/Prop_0 (Crate)".
#
# The durable fix is for a suite to DERIVE these from the server's own adoption log rather than
# type them -- the server now prints one "[props] authored prop <id> <- <path>" line per prop at
# world build for exactly that. Done here as a constant bump rather than a rewrite because this
# is another lane's suite and the four values are the whole dependency; the next lane to move
# them should spend the twenty lines instead.
# DERIVED AT RUN TIME, NOT TYPED. Filled in from the server's own adoption log the moment it
# reports listening -- see Get-AuthoredPropId in _Common.ps1 for the whole argument. The NODE
# PATHS are the contract now; the numbers are whatever the world happens to hand out today.
$PropAPath = "SearchRoom/Prop_0"   # world (36, 0.22, 0)
$PropBPath = "SearchRoom/Prop_3"   # world (38, 0.22, 0)
$PropCPath = "SearchRoom/Prop_1"   # world (36, 0.22, 2)
$PropDPath = "SearchRoom/Prop_2"   # world (36, 0.22, -2)
$PropA = -1; $PropB = -1; $PropC = -1; $PropD = -1

# A's intended pose. y = 0.225 is the crate's half-height plus 5 mm, i.e. resting on the floor
# rather than hovering: the assertion is "within 0.05 m of where it was asked to go", so a
# placement authored 8 cm in the air would fail on the settle, correctly, and prove nothing.
$PlaceX = 40.2; $PlaceY = 0.225; $PlaceZ = 0.0; $PlaceYawDeg = 35

# PropManager.PlaceDenial ordinals — the wire contract.
$DenyTooFar        = 3
$DenyDoesNotFit    = 4
$DenyOutsideRoom   = 5

Write-Host "=== place: set it down exactly there, and refuse with a reason when you can't ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$procs = @()
try {
    Write-Host "[1/3] launching dedicated server (supermarket world, spawning in the search room)..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "place.server.out.log"
    # --spawn-index (INT-1, ruling 6): the four bots each get THEIR OWN marker by NAME rather
    # than by join order. The comment below this launch has always said "bots connect in order,
    # so each of the four starts at its own marker"; join order races (SHELF-1 SS6.3), and in a
    # room that is four corridors a bot on the wrong marker walks into a shelf. Now it is true
    # by construction instead of by hope.
    $server = Start-Godot @("--server", "--port", $Port, "--world", "supermarket",
        "--spawn-room", "search",
        "--spawn-index", "PlaceBotA=0,PlaceBotB=1,PlaceBotC=2,PlaceBotD=3") "place.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    $PropA = Get-AuthoredPropId $serverOut $PropAPath
    $PropB = Get-AuthoredPropId $serverOut $PropBPath
    $PropC = Get-AuthoredPropId $serverOut $PropCPath
    $PropD = Get-AuthoredPropId $serverOut $PropDPath
    Write-Host "        authored prop ids this run: A=$PropA B=$PropB C=$PropC D=$PropD (derived from the server's adoption log)"

    Write-Host "[2/3] launching four scripted place bots..." -ForegroundColor Cyan

    # EACH BOT NAMES ITS PROP (--carry-target-prop), ADDED BY SHELF-1 AND NOT OPTIONAL ANY MORE.
    # This suite used to say "walk to (36, 0.22, -2) and press E", and in a room containing four
    # crates that was the same instruction as "pick up crate 1002". The search room now authors
    # 130 props: on the first run against the dressed room, bot D pressed E at its 1.2 m arrive
    # radius and came away holding 1013, a cereal box on the next shelf -- correctly, because a
    # crate at y = 0.22 is 1.22 m from an avatar standing 1.2 m short and a box at y = 0.565 on
    # the aisle 0.94 m away is 1.02 m. Nearest-carryable is the right rule for a player and it is
    # the whole texture of a stocked aisle; it is simply no longer a way for a suite to name an
    # object. The flag already existed for CARRY-1's version of this lesson ("walk to the ball",
    # not "walk to (x, z)") and SHELF-1 extended it to the grab. See
    # SandboxAvatar.ScriptedGrabPropId -- it narrows the choice, never widens the reach.

    # Bots connect in order, and the server hands out search-room markers 0..3 by join order, so
    # each of the four starts at its own marker. Two bots on one marker spawn inside each other,
    # which is why SearchRoom.tscn has four.
    #
    # --carry-place's delay is measured from the tick the bot is first seen HOLDING, never from
    # process start: under a loaded marathon a walk slips by seconds and a wall-clock constant
    # does not. 7 s is roughly twice the longest walk here (about 11 m at ~3.6 m/s).
    $aLog = Join-Path $script:LogDir "placeA.jsonl"
    $botA = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "PlaceBotA",
        "--log", $aLog, "--duration", $DurationSec, "--world", "supermarket",
        "--carry-script", "36,0.22,0,1.0,-1", "--carry-grab-retry", "0.6",
        "--carry-target-prop", $PropA,
        "--carry-walk-to", "40,0",
        "--carry-place", "$PlaceX,$PlaceY,$PlaceZ,$PlaceYawDeg,7") "placeA"
    $procs += $botA
    Start-Sleep -Milliseconds 400

    # B: the witness, and the beyond-reach case. It places at (45, 1, 0) while standing at x ~= 38
    # — about 7 m from its own hand against a 1.65 m allowance (PlaceReachM + the grab tolerance),
    # so it is refused for distance and for nothing else.
    $bLog = Join-Path $script:LogDir "placeB.jsonl"
    $botB = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "PlaceBotB",
        "--log", $bLog, "--duration", $DurationSec, "--world", "supermarket",
        "--carry-script", "38,0.22,0,1.0,-1", "--carry-grab-retry", "0.6",
        "--carry-target-prop", $PropB,
        "--carry-place", "45,1,0,0,7") "placeB"
    $procs += $botB
    Start-Sleep -Milliseconds 400

    # C: into the pillar. SearchPillar is a 1 x 2 x 1 m static block centred at world (46, 1, 2.5);
    # a crate at its centre is buried in it, and is still comfortably inside the room's bounds, so
    # the refusal can only come from the overlap test.
    $cLog = Join-Path $script:LogDir "placeC.jsonl"
    $botC = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "PlaceBotC",
        "--log", $cLog, "--duration", $DurationSec, "--world", "supermarket",
        "--carry-script", "36,0.22,2,1.0,-1", "--carry-grab-retry", "0.6",
        "--carry-target-prop", $PropC,
        "--carry-walk-to", "46,2.5",
        "--carry-place", "46,1,2.5,0,7") "placeC"
    $procs += $botC
    Start-Sleep -Milliseconds 400

    # D: through the +X wall. The room's RoomBounds volume ends at world x = 47; a 0.44 m crate
    # centred at 47.35 has every corner past it, so the bounds test refuses before the overlap
    # test is ever reached — which is the ordering this suite is also pinning.
    $dLog = Join-Path $script:LogDir "placeD.jsonl"
    $botD = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "PlaceBotD",
        "--log", $dLog, "--duration", $DurationSec, "--world", "supermarket",
        "--carry-script", "36,0.22,-2,1.0,-1", "--carry-grab-retry", "0.6",
        "--carry-target-prop", $PropD,
        "--carry-walk-to", "47,-3",
        "--carry-place", "47.35,1,-3,0,7") "placeD"
    $procs += $botD

    $bots = @(
        @{ Name = "PlaceBotA"; Proc = $botA; JsonLog = $aLog }
        @{ Name = "PlaceBotB"; Proc = $botB; JsonLog = $bLog }
        @{ Name = "PlaceBotC"; Proc = $botC; JsonLog = $cLog }
        @{ Name = "PlaceBotD"; Proc = $botD; JsonLog = $dLog }
    )
    $deadline = (Get-Date).AddSeconds($DurationSec + 90)
    foreach ($b in $bots) {
        $remainingMs = [int]((($deadline - (Get-Date)).TotalSeconds) * 1000)
        if ($remainingMs -lt 1000) { $remainingMs = 1000 }
        if (-not $b.Proc.WaitForExit($remainingMs)) { Write-Fail "$($b.Name) did not exit within timeout" }
    }
    foreach ($b in $bots) {
        if ($b.Proc.ExitCode -ne 0) { Write-Fail "$($b.Name) exited with code $($b.Proc.ExitCode); see $($b.JsonLog)" }
    }
} finally {
    Stop-Procs $procs
}

Write-Host "[3/3] verifying placement and refusals..." -ForegroundColor Cyan

function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

function Get-Prop($Sample, [int]$PropId) {
    $p = @($Sample.props) | Where-Object { [int]$_.id -eq $PropId }
    if ($null -eq $p -or @($p).Count -eq 0) { return $null }
    return ($p | Select-Object -First 1)
}

# Angle between two unit quaternions, in degrees. |dot| rather than dot: q and -q are the same
# rotation, so the sign carries no information and using it would report a perfect match as 360
# degrees off half the time.
function Get-QuatAngleDeg($Prop, [double]$YawDeg) {
    $half = ([math]::PI * $YawDeg / 180.0) / 2.0
    $wx = 0.0; $wy = [math]::Sin($half); $wz = 0.0; $ww = [math]::Cos($half)
    $dot = [math]::Abs([double]$Prop.qx * $wx + [double]$Prop.qy * $wy + [double]$Prop.qz * $wz + [double]$Prop.qw * $ww)
    if ($dot -gt 1.0) { $dot = 1.0 }
    return 2.0 * [math]::Acos($dot) * 180.0 / [math]::PI
}

$samplesA = Get-Samples $aLog
$samplesB = Get-Samples $bLog
$samplesC = Get-Samples $cLog
$samplesD = Get-Samples $dLog

$peerA = [int]$samplesA[0].self
$peerB = [int]$samplesB[0].self
$peerC = [int]$samplesC[0].self
$peerD = [int]$samplesD[0].self
Write-Host "        peers: A=$peerA B=$peerB C=$peerC D=$peerD" -ForegroundColor DarkGray

$failures = New-Object System.Collections.Generic.List[string]

# --- 1: the legal placement, as a DIFFERENT peer sees it ---------------------------------------
# B's final sample: the run's last settled word about the world, on a machine that neither made
# the placement nor holds the prop.
$bLast = $samplesB[-1]
$placed = Get-Prop $bLast $PropA
if ($null -eq $placed) {
    $failures.Add("bot B's final sample has no prop $PropA at all - it never replicated")
} elseif ([int]$placed.holder -ne 0) {
    $failures.Add("bot B's final sample: prop $PropA still held by $($placed.holder) - A's place never landed")
} else {
    $dx = [double]$placed.x - $PlaceX
    $dy = [double]$placed.y - $PlaceY
    $dz = [double]$placed.z - $PlaceZ
    $dist = [math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
    $ang = Get-QuatAngleDeg $placed $PlaceYawDeg
    Write-Host ("        B sees prop {0} at ({1:F3}, {2:F3}, {3:F3}) - {4:F3} m and {5:F2} deg from the intended pose" -f `
        $PropA, [double]$placed.x, [double]$placed.y, [double]$placed.z, $dist, $ang) -ForegroundColor DarkGray
    if ($dist -gt 0.05) {
        $failures.Add(("bot B sees prop {0} {1:F3} m from the intended transform ({2}, {3}, {4}); tolerance 0.05 m" -f `
            $PropA, $dist, $PlaceX, $PlaceY, $PlaceZ))
    }
    if ($ang -gt 5.0) {
        $failures.Add(("bot B sees prop {0} rotated {1:F2} deg from the intended {2} deg yaw; tolerance 5 deg" -f `
            $PropA, $ang, $PlaceYawDeg))
    }
}

# --- 2, 3 & 4: every illegal placement refused, WITH ITS REASON, prop still in hand -------------
$cases = @(
    @{ Bot = "B"; Samples = $samplesB; Peer = $peerB; Prop = $PropB; Deny = $DenyTooFar;
       Why = "beyond arm's reach (TooFarToPlace)" }
    @{ Bot = "C"; Samples = $samplesC; Peer = $peerC; Prop = $PropC; Deny = $DenyDoesNotFit;
       Why = "inside the pillar (DoesNotFitThere)" }
    @{ Bot = "D"; Samples = $samplesD; Peer = $peerD; Prop = $PropD; Deny = $DenyOutsideRoom;
       Why = "outside the room bounds (OutsideRoom)" }
)
foreach ($c in $cases) {
    $last = $c.Samples[-1]
    $deny = [int]$last.placeDeny
    if ($deny -ne $c.Deny) {
        $failures.Add("bot $($c.Bot): newest place refusal was ordinal $deny, expected $($c.Deny) - $($c.Why)")
    }
    $held = Get-Prop $last $c.Prop
    if ($null -eq $held) {
        $failures.Add("bot $($c.Bot)'s final sample has no prop $($c.Prop)")
    } elseif ([int]$held.holder -ne $c.Peer) {
        $failures.Add("bot $($c.Bot): prop $($c.Prop) holder=$($held.holder) in the final sample, expected $($c.Peer) - a REFUSED place must never cost the player what they were carrying")
    } else {
        Write-Host "        bot $($c.Bot) refused $($c.Why), still holding prop $($c.Prop)" -ForegroundColor DarkGray
    }
}

# --- instrumentation (not an assertion): the spring-vs-anchor mismatch the handoff reports ------
# Off = the held prop's distance from the holder's own carry anchor, sampled on the HOLDER's peer,
# which for a spring-carried prop is exactly the lag between what the holder sees and what every
# other peer derives.
$springSamples = @()
foreach ($set in @(@{ S = $samplesA; P = $peerA; Id = $PropA }, @{ S = $samplesC; P = $peerC; Id = $PropC },
                   @{ S = $samplesD; P = $peerD; Id = $PropD })) {
    foreach ($s in $set.S) {
        $p = Get-Prop $s $set.Id
        if ($null -ne $p -and [int]$p.holder -eq $set.P) { $springSamples += [double]$p.springLag }
    }
}
if ($springSamples.Count -gt 0) {
    $m = ($springSamples | Measure-Object -Average -Maximum)
    Write-Host ("        spring-vs-anchor mismatch while held: mean {0:F3} m, peak {1:F3} m over {2} samples" -f `
        $m.Average, $m.Maximum, $springSamples.Count) -ForegroundColor DarkGray
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "PLACE-TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "PLACE-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: placed within 0.05 m / 5 deg on an observing peer; beyond-reach, inside-geometry and outside-bounds each refused with their own reason, prop still held." -ForegroundColor Green
Write-Host ""
Write-Host "PLACE-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
