<#
.SYNOPSIS
    PHYS-1 (2026-09-20) -- dominoes fall, cans roll, nothing freaks out.

.DESCRIPTION
    Talon's verdict after his first ride, which is this suite's whole specification:

        "I want to manipulate objects and know they cannot clip through walls, the floor, or
         other objects. I want to know they won't freak out and make other objects jump around
         randomly. Make it do what the player expects: if they're placing an object on a shelf
         and accidentally hit a bunch of boxes, those boxes should fall over like dominoes, and
         cans should roll around."

    ONE server and TWO bots on udp/7916 -- the port the orchestrator ASSIGNED for this wave
    (.claude/rules/test-suite.md, FEEL-1 section: "PORTS ARE ASSIGNED BY THE ORCHESTRATOR IN THE
    DISPATCH. A LANE NEVER COMPUTES NEXT FREE."). One pass of one carried crate produces every
    bar below, which is deliberate: the bars are about one continuous physical event and staging
    them separately would let a stack be knocked over by a fixture rather than by a carry.

    THE FIXTURE. Five cereal boxes standing in a row 2 cm apart in the z = 0 walkway, and a can
    on the floor past them. Seeded props are born RESTING -- frozen kinematic on every peer --
    which is precisely the state P1 is about: before this packet a held crate driven into them
    was stopped by an immovable wall of boxes.

    THE BARS, in the order the phases measure them:
      (1) UNTOUCHED IS UNTOUCHED     no seeded prop moves at all in the twenty seconds
                                     before the bot even picks the crate up.
      (2) DOMINOES                   every box tilts past 60 degrees, within 2 s of the first,
                                     on the holder's view AND on a witness's.
      (3) THE CAN ROLLS              travels at least 1 m and stops within 4 m.
      (4) NO FREAKOUT                no unheld prop exceeds MaxPropSpeed; at most 2 [phys] clamp
                                     lines; zero last-good restores; zero props out of bounds.
      (5) EVERYTHING SETTLES         every woken prop is back at Resting by the end of the run --
                                     the honest half of "stacks are stable", since rest in this
                                     build is a LATCH and a frozen body is trivially stable.
      (6) A THROW IS CONTAINED       the crate thrown at the end never leaves the room envelope
                                     and never ends up under the floor.

    Every tilt is computed from the prop quaternion BotHarness already logs (up.y = 1 - 2(x^2+z^2)),
    so this suite needs no new instrumentation on the game side.

.NOTES
    Pure ASCII, by the rules file's REACH-1 entry (PowerShell 5.1 reads a BOM-less file as ANSI
    and mojibake produces a parser cascade 200 lines from anything that is wrong).
#>
[CmdletBinding()]
param(
    [int]$Port = 7916,
    [double]$DurationSec = 60,
    [double]$MaxPropSpeed = 3.0,
    [double]$TiltDeg = 60,
    [double]$DominoWindowSec = 2.0,
    [double]$MinRollM = 1.0,
    [double]$MaxRollM = 4.0,
    [int]$MaxClampLines = 2,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# The row: five boxes, 2 cm of air between them. A cereal box is 0.19 m wide and 0.28 m tall, so
# a pitch of 0.21 m leaves the gap the packet names and a falling box (0.28 m of reach) still
# arrives at its neighbour. y = 0.14 stands it on the floor.
$BoxPitch = 0.21
$BoxX0 = 41.00
$BoxIds = 1..5                 # --seed-test-props assigns ids from 1 in seed order
$RollCanId = 6
$CratePropPath = "SearchRoom/Prop_0"    # CARRY-1's crate at world (36, 0.22, 0)
$CanX = 43.50

# The search room is the supermarket seam's +40 x block. Anything outside this envelope has left
# the room, which is the "cannot clip through walls or the floor" half of the bar.
$RoomMinX = 33.0; $RoomMaxX = 47.5
$RoomMinZ = -6.0; $RoomMaxZ = 6.0
$RoomMinY = -0.5; $RoomMaxY = 6.0

$seed = @()
for ($i = 0; $i -lt 5; $i++) {
    $x = [math]::Round($BoxX0 + $i * $BoxPitch, 2)
    $seed += "$x,0.14,0,box"
}
$seed += "$CanX,0.06,0,can"
$seedArg = ($seed -join ";")

Write-Host "=== physics feel: dominoes fall, cans roll, nothing freaks out ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$failures = New-Object System.Collections.Generic.List[string]
$procs = @()
try {
    Write-Host "[1/4] launching dedicated server (supermarket, search room) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "physfeel.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "supermarket",
        "--spawn-room", "search",
        "--spawn-index", "PhysBot=0,WitnessBot=3",
        "--seed-test-props", $seedArg) "physfeel.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
        Write-Fail "server never reported listening on udp/$Port; see $serverOut"
    }
    if (-not (Wait-ForLogLine $serverOut "seed-test-props: seeded 6 test prop" 20)) {
        Write-Fail "the server did not seed all six fixture props; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id)), row at x=$BoxX0..$([math]::Round($BoxX0 + 4 * $BoxPitch,2)) z=0, can at x=$CanX"

    $CrateProp = Get-AuthoredPropId $serverOut $CratePropPath
    Write-Host "        the carried crate is authored prop $CrateProp (from the server's adoption log)"

    Write-Host "[2/4] launching the driver and the witness..." -ForegroundColor Cyan

    # PhysBot: walk to CARRY-1's crate at (36, 0.22, 0) and then STAND THERE FOR TWENTY SECONDS
    # before grabbing it, which is how bar (1) gets a real untouched window. The packet asks for
    # thirty seconds of a row standing still; twenty is what fits beside the event in one run,
    # and the measurement is the same measurement.
    #
    # Then it walks STRAIGHT down its own z = 0 walkway through the row and past the can (HOLD-1's
    # rule: one point, one straight line, never diagonally). It never voluntarily drops (-1) and
    # throws the crate 30 s after the grab, by which time the walk is long finished and everything
    # it knocked over has had time to settle -- that throw is bar (6).
    $physLog = Join-Path $script:LogDir "physfeel.phys.jsonl"
    $physBot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "PhysBot",
        "--log", $physLog, "--duration", $DurationSec, "--world", "supermarket",
        "--carry-script", "36,0.22,0,20.0,-1,30", "--carry-grab-retry", "0.6",
        "--carry-target-prop", $CrateProp,
        "--carry-walk-to", "44.0,0") "physfeel.phys"
    $procs += $physBot
    Start-Sleep -Milliseconds 400

    # WitnessBot: holds nothing, touches nothing, stands at the far spawn. It is the peer that
    # proves the dominoes REPLICATED -- P1 wakes props on the server alone, and a stack that fell
    # only on the host would be the DOOR-1 defect ("shoved 3 prop(s)" while every client's copy
    # sat still) wearing a new hat.
    $witLog = Join-Path $script:LogDir "physfeel.witness.jsonl"
    $witBot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "WitnessBot",
        "--log", $witLog, "--duration", $DurationSec, "--world", "supermarket") "physfeel.witness"
    $procs += $witBot

    $bots = @(
        @{ Name = "PhysBot"; Proc = $physBot; JsonLog = $physLog }
        @{ Name = "WitnessBot"; Proc = $witBot; JsonLog = $witLog }
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

Write-Host "[3/4] reading the logs..." -ForegroundColor Cyan

function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

# Every sample of one prop, as one view saw it. `up` is the world Y component of the prop's own
# up axis, from the quaternion BotHarness logs: for q = (x, y, z, w) the rotated (0,1,0) has
# y = 1 - 2(x^2 + z^2). 1.0 is upright, 0.0 is exactly on its side, -1.0 is upside down.
function Get-PropRows($Samples, [int]$PropId) {
    $out = @()
    foreach ($s in $Samples) {
        $p = @($s.props) | Where-Object { [int]$_.id -eq $PropId } | Select-Object -First 1
        if ($null -eq $p) { continue }
        $qx = [double]$p.qx; $qz = [double]$p.qz
        $out += [pscustomobject]@{
            T = [double]$s.t
            X = [double]$p.x; Y = [double]$p.y; Z = [double]$p.z
            Holder = [int]$p.holder
            Spd = [double]$p.spd
            Up = 1.0 - 2.0 * ($qx * $qx + $qz * $qz)
        }
    }
    return $out
}

$physSamples = Get-Samples $physLog
$witSamples = Get-Samples $witLog
$serverText = @(Get-Content $serverOut)

$views = @(
    @{ Name = "holder"; Samples = $physSamples }
    @{ Name = "witness"; Samples = $witSamples }
)

# STAGING FIRST, and in its own words. Every assertion below is about props being knocked over by
# a CARRIED CRATE; if the crate was never carried, the bars are being judged on an event that was
# never staged, which is the failure mode the rules file records four separate times.
$heldEver = @($physSamples | ForEach-Object { [int]$_.heldPropId } | Where-Object { $_ -gt 0 })
if ($heldEver.Count -eq 0) {
    $crateRows = Get-PropRows $physSamples $CrateProp
    $closest = 999.0
    foreach ($s in $physSamples) {
        $me = @($s.peers) | Where-Object { [int]$_.id -eq [int]$s.self } | Select-Object -First 1
        $c = @($s.props) | Where-Object { [int]$_.id -eq $CrateProp } | Select-Object -First 1
        if ($null -eq $me -or $null -eq $c) { continue }
        $d = [math]::Sqrt([math]::Pow([double]$me.x - [double]$c.x, 2) +
                          [math]::Pow([double]$me.y - [double]$c.y, 2) +
                          [math]::Pow([double]$me.z - [double]$c.z, 2))
        if ($d -lt $closest) { $closest = $d }
    }
    Write-Fail ("STAGING: PhysBot never held prop $CrateProp -- closest approach " +
        ("{0:F2}" -f $closest) + " m in 3D against the client's 1.5 m PickupRadius. " +
        "Inside 1.5 m and motionless is a press that found nothing; several metres out is the " +
        "four-corridor mis-walk. Nothing below is about physics. See $serverOut")
}

# --- bar (1): untouched is untouched ------------------------------------------------------
# THE WINDOW IS EVERYTHING BEFORE THE GRAB, and that choice is load-bearing. The obvious window
# -- "before the holder came within 2 m of the row" -- is WRONG here, because CARRY-1's authored
# crate Prop_3 stands at (38, 0.22, 0), squarely in this walkway: the held crate knocks THAT one
# awake first, and a 1 kg crate shoved down the aisle can reach the row before its holder does.
# That is P1 working, and a bar that called it a failure would be measuring the fixture.
#
# Before the grab nothing in the room has been touched by anything at all, so the window is
# honest AND it is long: the bot stands beside its crate for twenty seconds first.
$arriveT = [double]::PositiveInfinity
foreach ($s in $physSamples) {
    if ([int]$s.heldPropId -gt 0) { $arriveT = [double]$s.t; break }
}
if ([double]::IsInfinity($arriveT)) {
    $failures.Add("PhysBot never grabbed the crate -- bars 2-5 were never staged")
    $arriveT = 0.0
}

$restWorst = 0.0
foreach ($id in ($BoxIds + $RollCanId)) {
    $rows = @(Get-PropRows $physSamples $id | Where-Object { $_.T -lt $arriveT })
    if ($rows.Count -lt 2) { continue }
    $x0 = $rows[0].X; $y0 = $rows[0].Y; $z0 = $rows[0].Z
    foreach ($r in $rows) {
        $d = [math]::Sqrt([math]::Pow($r.X - $x0, 2) + [math]::Pow($r.Y - $y0, 2) + [math]::Pow($r.Z - $z0, 2))
        if ($d -gt $restWorst) { $restWorst = $d }
    }
}
Write-Host ("        untouched: worst movement {0:F4} m over the {1:F1} s before the grab" -f $restWorst, $arriveT) -ForegroundColor DarkGray
# This IS the "no wake storm at rest" bar. A [phys] wake line carries no timestamp, so counting
# those lines could never tell a wake before the crate arrived from one after it; the props'
# own positions over a window derived from the run can, and do.
if ($restWorst -gt 0.01) {
    $failures.Add(("a seeded prop moved {0:F3} m before anything touched it (bar 0.01) -- a stack that drifts at rest is not a stack" -f $restWorst))
}


# --- bar (2): the dominoes ----------------------------------------------------------------
$upBar = [math]::Cos($TiltDeg * [math]::PI / 180.0)
foreach ($view in $views) {
    $fellAt = @{}
    foreach ($id in $BoxIds) {
        $rows = @(Get-PropRows $view.Samples $id)
        $t = $null
        foreach ($r in $rows) { if ($r.Up -lt $upBar) { $t = $r.T; break } }
        if ($null -ne $t) { $fellAt[$id] = $t }
    }
    $fellCount = $fellAt.Count
    if ($fellCount -eq 0) {
        Write-Host ("        {0,-8} dominoes: NONE of the {1} boxes tilted past {2} deg" -f $view.Name, $BoxIds.Count, $TiltDeg) -ForegroundColor DarkGray
        $failures.Add("$($view.Name): not one of the five boxes tilted past $TiltDeg deg -- a resting prop is still an immovable wall on this view")
        continue
    }
    $first = ($fellAt.Values | Measure-Object -Minimum).Minimum
    $last = ($fellAt.Values | Measure-Object -Maximum).Maximum
    $spread = $last - $first
    Write-Host ("        {0,-8} dominoes: {1}/{2} boxes past {3} deg, first at t={4:F2}s, all within {5:F2}s" -f `
        $view.Name, $fellCount, $BoxIds.Count, $TiltDeg, $first, $spread) -ForegroundColor DarkGray
    if ($fellCount -lt $BoxIds.Count) {
        $failures.Add("$($view.Name): only $fellCount of $($BoxIds.Count) boxes went over -- the chain stopped part way")
    }
    if ($spread -gt $DominoWindowSec) {
        $failures.Add(("{0}: the row took {1:F2}s to go over (bar {2:F1}s) -- that is a shove passed along, not a domino" -f $view.Name, $spread, $DominoWindowSec))
    }
}

# --- bar (3): the can rolls ---------------------------------------------------------------
foreach ($view in $views) {
    $rows = @(Get-PropRows $view.Samples $RollCanId)
    if ($rows.Count -lt 2) {
        $failures.Add("$($view.Name): the can was never sampled")
        continue
    }
    $x0 = $rows[0].X; $z0 = $rows[0].Z
    $travel = 0.0; $prev = $rows[0]
    $final = 0.0; $peak = 0.0
    foreach ($r in $rows) {
        $travel += [math]::Sqrt([math]::Pow($r.X - $prev.X, 2) + [math]::Pow($r.Z - $prev.Z, 2))
        $prev = $r
        if ($r.Spd -gt $peak) { $peak = $r.Spd }
    }
    $lastRow = $rows[$rows.Count - 1]
    $final = [math]::Sqrt([math]::Pow($lastRow.X - $x0, 2) + [math]::Pow($lastRow.Z - $z0, 2))
    Write-Host ("        {0,-8} can: path {1:F2} m, net {2:F2} m from where it stood, peak {3:F2} m/s" -f `
        $view.Name, $travel, $final, $peak) -ForegroundColor DarkGray
    if ($view.Name -ne "holder") { continue }   # the bar is measured once, on the simulating side's view
    if ($travel -lt $MinRollM) {
        $failures.Add(("the can travelled {0:F2} m (bar at least {1:F1} m) -- it was nudged and did not roll" -f $travel, $MinRollM))
    }
    if ($final -gt $MaxRollM) {
        $failures.Add(("the can ended {0:F2} m from where it stood (bar at most {1:F1} m) -- it rolled out of the aisle" -f $final, $MaxRollM))
    }
}

# --- bar (4): no freakout -----------------------------------------------------------------
$fastest = 0.0; $fastestId = 0
foreach ($view in $views) {
    foreach ($s in $view.Samples) {
        foreach ($p in @($s.props)) {
            if ([int]$p.holder -ne 0) { continue }
            $spd = [double]$p.spd
            if ($spd -gt $fastest) { $fastest = $spd; $fastestId = [int]$p.id }
        }
    }
}
$clampLines = @($serverText | Select-String -Pattern "^\[phys\] clamp ")
$restoreLines = @($serverText | Select-String -Pattern "\[reach\] layer2 .*(RestoredLastGood|Stuck)")
$waitLines = @($serverText | Select-String -Pattern "^\[phys\] rest-wait ")
$wakeLines = @($serverText | Select-String -Pattern "^\[phys\] wake ")
Write-Host ("        freakout: fastest unheld prop {0:F2} m/s (prop {1}, bar {2:F1}); {3} clamp line(s); {4} last-good restore(s); {5} rest-wait line(s); {6} wake(s)" -f `
    $fastest, $fastestId, $MaxPropSpeed, $clampLines.Count, $restoreLines.Count, $waitLines.Count, $wakeLines.Count) -ForegroundColor DarkGray
if ($fastest -gt $MaxPropSpeed) {
    $failures.Add(("prop $fastestId was seen at {0:F2} m/s with nobody holding it (bar {1:F1}) -- something flung it" -f $fastest, $MaxPropSpeed))
}
if ($clampLines.Count -gt $MaxClampLines) {
    $failures.Add("the per-tick clamp bit $($clampLines.Count) time(s) (bar $MaxClampLines) -- the solver is finding energy this packet did not hand it")
}
if ($restoreLines.Count -gt 0) {
    $failures.Add("the rest audit teleported $($restoreLines.Count) prop(s) back to a last-good pose in ordinary play -- that IS the freakout Talon described")
}
if ($wakeLines.Count -eq 0) {
    $failures.Add("the server logged not one [phys] wake for the whole run -- P1's contact path never ran, so every green bar above is about something else")
}

# --- bar (5): everything settles ----------------------------------------------------------
# Rest in this build is a LATCH, so a prop back at Resting reads on every peer as spd = 0 and
# holder = 0 with its position no longer changing. The honest question is not whether a frozen
# body is stable -- it is whether a woken one ever gets back.
$stillMoving = @()
foreach ($id in ($BoxIds + $RollCanId)) {
    $rows = @(Get-PropRows $physSamples $id)
    if ($rows.Count -lt 3) { continue }
    $tail = $rows[($rows.Count - 3)..($rows.Count - 1)]
    $maxTail = ($tail | ForEach-Object { $_.Spd } | Measure-Object -Maximum).Maximum
    if ($maxTail -gt 0.05) { $stillMoving += "$id at $([math]::Round($maxTail,2)) m/s" }
}
Write-Host ("        settled: {0} of {1} knocked prop(s) still moving in the last three samples" -f `
    $stillMoving.Count, ($BoxIds.Count + 1)) -ForegroundColor DarkGray
if ($stillMoving.Count -gt 0) {
    $failures.Add("props never came back to rest by the end of the run: $($stillMoving -join ', ') -- a woken prop that never settles is P1 and the rest audit unable to both hold")
}

# --- bar (6): containment -----------------------------------------------------------------
$escapes = @()
foreach ($view in $views) {
    foreach ($s in $view.Samples) {
        foreach ($p in @($s.props)) {
            $x = [double]$p.x; $y = [double]$p.y; $z = [double]$p.z
            if ($x -lt $RoomMinX -or $x -gt $RoomMaxX -or
                $z -lt $RoomMinZ -or $z -gt $RoomMaxZ -or
                $y -lt $RoomMinY -or $y -gt $RoomMaxY) {
                $escapes += ("prop $([int]$p.id) at ({0:F2}, {1:F2}, {2:F2}) on the $($view.Name) view" -f $x, $y, $z)
            }
        }
    }
}
$escapes = @($escapes | Select-Object -Unique)
Write-Host ("        contained: {0} sample(s) outside the room envelope x[{1},{2}] y[{3},{4}] z[{5},{6}]" -f `
    $escapes.Count, $RoomMinX, $RoomMaxX, $RoomMinY, $RoomMaxY, $RoomMinZ, $RoomMaxZ) -ForegroundColor DarkGray
if ($escapes.Count -gt 0) {
    $failures.Add("a prop left the room: $($escapes[0]) (and $($escapes.Count - 1) more) -- something clipped through a wall or the floor")
}

# Machine-readable evidence for the handoff and for whoever reads this next, printed
# unconditionally so a green run still hands over its numbers (FEEL-1's LAGTABLE pattern).
Write-Host ("PHYS1BARS restWorst={0:F4} fastestUnheld={1:F2} clamps={2} restores={3} wakes={4} waits={5}" -f `
    $restWorst, $fastest, $clampLines.Count, $restoreLines.Count, $wakeLines.Count, $waitLines.Count)

Write-Host "[4/4] verdict" -ForegroundColor Cyan
Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "PHYSICS-FEEL-TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "PHYSICS-FEEL-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: the row went over like dominoes on both views, the can rolled, nothing was flung, everything settled, and nothing left the room." -ForegroundColor Green
Write-Host ""
Write-Host "PHYSICS-FEEL-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
