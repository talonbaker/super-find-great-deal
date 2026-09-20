<#
.SYNOPSIS
    REACH-1: placement integrity layers 2 and 3 (program doc S5b). The planted room's eight cases,
    the Confirm-time refusal and acceptance on a live two-bot round, and what the audit costs
    with 150 props being disturbed.

.DESCRIPTION
    Talon's ruling behind all of this (2026-09-19, program S1 item 12): the hidden object is
    never clipped, out of bounds, or unreachable by accident. A seeker who cannot find the object
    because of a physics glitch is the worst outcome this design has.

    Everything about the RULE is tested engine-free in tests/unit/ReachabilityTests.cs -- the six
    S5b cases against a fake sampler, the ring geometry, every refusal reason. None of that is
    repeated here. This suite exists for the four things a pure function cannot reach.

    PHASE 1 -- THE PLANTED ROOM (no bots, no clients).
      --reach-selftest, which implies --world reachplant: a copy of the search room with S5b's
      six failure cases AUTHORED into it as real fixtures (ReachPlantRoom.tscn), audited by the
      shipped layer-2 and layer-3 code on a real server with a real physics space.

        Prop_0_Wall        inside a 0.6 m partition wall  -> StaticOverlap/Stuck, InsideStatic
        Prop_1_ShelfBack   inside a 0.3 m shelf back      -> StaticOverlap/Stuck, InsideStatic
        Prop_2_HighLedge   on a ledge 3 m up              -> None/Good,           NoStandingPoint
        Prop_3_UnderBin    under an overturned bin        -> None/Good,           reachable, first hit MOVABLE
        Prop_5_InBox       inside a closed movable box    -> None/Good,           reachable, first hit MOVABLE
        Prop_8_Shallow     0.12 m into a plinth           -> StaticOverlap/DEPENETRATED, reachable
        Prop_7_Faller      shoved into an open pit        -> KillPlane/RestoredLastGood, back within 0.05 m
        Prop_2_HighLedge   Confirm pressed while it is
                           still in the air (REVIEW-1 C2) -> still Loose, still un-frozen, still travelling

      The two "first hit MOVABLE" assertions are what give the bin and the box their teeth: a
      room where the bin was never authored would pass both of them on a clear line to the crate,
      and the boolean alone cannot tell those apart.

      THE EIGHTH CASE IS THE ONE WHERE THE AUDIT COULD MOVE THE GAME RATHER THAN MEASURE IT.
      ReachabilityFactSource.AuditBeforeConfirm used to re-audit unconditionally, and
      PropManager.ServerAuditRest guards only PropMode.Held -- it cannot guard on Resting,
      because the settle latch calls it on a prop that is still Loose. So a target still in
      flight reached NetworkedProp.SettleToRest, which freezes the body and pins the transform,
      and a prop a metre up passes both of layer 2's tests. A hider who dropped the object at the
      Confirm plate and pressed inside the ~0.3 s settle window froze it in mid-air and the
      seeker was sent to find a can hanging off the floor. Planted (guard removed) it printed
      `modeAfterPress=Resting frozenAfterPress=True travelledAfter=0.000m -> FAIL`; with the
      guard, `modeAfterPress=Loose frozenAfterPress=False travelledAfter=0.092m -> PASS`.
      It goes through the shipped press seam, not through ServerAuditRest directly, because the
      defect was in WHICH of those two a press reaches.

    PHASE 2 -- CONFIRM REFUSED, on a live round with two clients.
      A crate is seeded INSIDE the search room's pillar (--seed-test-props, which does not go
      through the place RPC and therefore does not get layer 1's refusal -- that is the "dev
      script" the packet's smoke asks for) and named as the target with --reach-target. Its
      LAST GOOD transform is that same pose, because a prop born inside the scenery has never
      had a legal one. So on the Confirm press: layer 2 re-runs, depenetration cannot clear
      0.72 m against its 0.15 m ceiling, the fallback lands on a last-good that is itself bad,
      and layer 3 answers InsideStatic. The round refuses with NobodyCouldReachThat and does NOT
      enter Seeking.

    PHASE 3 -- CONFIRM ACCEPTED, same staging, crate on the open floor instead.
      The control for phase 2, and it is not optional: a layer 3 that refused everything would
      pass phase 2 perfectly.

    PHASE 4 -- WHAT IT COSTS.
      150 crates seeded across the search room, two bots walking among them, and the cost probe
      (--reach-cost) shoving every prop back into loose physics every few seconds so all 150
      latch Resting in a wave. Reports rest audits/sec, integrity queries/sec and server physics
      frame time p50/p95. The probe does the shoving rather than the bots because two scripted
      bots cannot reliably disturb 150 props in twenty seconds; read the numbers as an upper
      bound (see ReachCostProbe's own note).

    THE SUITE GATES ON THE LINES, NOT THE EXIT CODE, for phases 1 and 4 -- the reason
    Run-SupermarketWorldTest.ps1 does: a process that dies before its own summary exits non-zero
    for reasons that have nothing to do with the subject.

    PROVED ABLE TO FAIL. See the DEVIATIONS / EVIDENCE block in
    docs/agents/handoffs/2026-09-19-REACH-1.md for the planted faults and what they printed.

    Exit 0 = PASS. Headless throughout. One Godot at a time: this suite takes the machine-wide
    suite mutex like every other.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param(
    # 7903. The ladder as of REACH-1: Run-CarryNetTest 7893/7894 (+7895 reserved),
    # Run-RoundLoopSmoke 7896, Run-FirstPersonTest 7897, Run-PlaceTest 7898, VOICE-1 7899,
    # DOOR-1 7900, SFX-1 7902 -- 7901 left to whoever claimed it in the same wave. INT-0's
    # measured lesson applies here and is why this number was asked for rather than computed:
    # a "next free port" read off a snapshot of tests/ is not free when several lanes branch off
    # one base at once (three of them picked 7896). A collision at RUN TIME with another
    # worktree's Godot is contention and clears on a re-run; two entries of one registry fail
    # identically every time.
    [int]$Port = 7903,
    [int]$MutexTimeoutMinutes = 30,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# --- the world, in world space ----------------------------------------------------------------
# SearchRoom.tscn is instanced at x = +40 by Supermarket.tscn. Interior x in [33, 47],
# z in [-5, 5], floor top y = 0. SearchPillar is a 1 x 2 x 1 static block at world (46, 1, 2.5).
$PillarCentre = "46,1,2.5"      # a crate seeded here is buried in static geometry
$OpenFloor    = "40,0.25,0"     # ... and here it is on the open floor, clear of Prop_0..3
$SeededPropId = 1               # PropRegistry.Register starts at 1; authored props are 1000+

# The round script for phases 2 and 3. Seconds from the FIRST PLAYER ARRIVING (the driver's
# script-clock gate), so these are margins over connection time rather than guesses about it.
# The hands default to "holding a rack object, not holding the target", so Start is accepted and
# Confirm is judged on reachability alone -- which is the one fact this suite is about.
$RoundScript   = "start@4,confirm@10"
$BotDurationSec = 22

# Phase 4.
$CostSeconds      = 20
$CostBotDuration  = 16
$CostPropTarget   = 150

Write-Host "=== REACH-1: the hidden object stays findable (placement integrity layers 2 and 3) ===" -ForegroundColor White

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
$script:Failures = @()
function Add-Failure([string]$m) { $script:Failures += $m }

# 150 crate positions on a grid in the search room, clear of the four authored props' aisle, of
# the pillar (x >= 45.5, z in [2,3]) and of the two bot spawn markers at z = 0. Generated rather
# than typed: a hundred and fifty literals is a hundred and fifty chances to put one in a wall.
function Get-CostSeed([int]$Count) {
    # 22 columns x 7 rows = 154 candidates, capped at $Count. The cap is checked in BOTH loops:
    # `break` leaves only the inner one, and the first run of this suite duly seeded 154 crates
    # and reported them as the measurement's population.
    $triples = New-Object System.Collections.Generic.List[string]
    foreach ($j in 0..6) {
        if ($triples.Count -ge $Count) { break }
        foreach ($i in 0..21) {
            if ($triples.Count -ge $Count) { break }
            $x = 34.4 + 0.5 * $i
            $z = -4.6 + 0.6 * $j
            $triples.Add(("{0:F2},0.25,{1:F2}" -f $x, $z))
        }
    }
    return ($triples -join ";")
}

# NB: the parameter is GameArgs, not Args. $Args is a PowerShell AUTOMATIC variable and a
# function parameter of that name is silently not the thing the caller passed -- the first run of
# this suite launched three servers with no game flags at all and they sat there doing nothing,
# which reads exactly like a server that failed to bind.
function Start-ReachServer([string]$Tag, [string[]]$GameArgs) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList (
        @("--headless", "--path", $script:Root, "--") + $GameArgs) `
        -RedirectStandardOutput $out `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

function Start-ReachBot([string]$Tag, [string]$Name, [int]$DurationSec) {
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
        "--log", (Join-Path $script:LogDir "$Tag.jsonl"), "--duration", $DurationSec,
        "--world", "supermarket") `
        -RedirectStandardOutput (Join-Path $script:LogDir "$Tag.out.log") `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    # ==========================================================================================
    # PHASE 1 -- the planted room
    # ==========================================================================================
    Write-Host "[1/4] the planted room: six S5b cases, the depenetration branch and the Confirm-on-a-mover..." -ForegroundColor Cyan
    $plantOut = Join-Path $script:LogDir "reach-plant.out.log"
    $plant = Start-ReachServer "reach-plant" @("--server", "--port", $Port, "--reach-selftest")
    $procs += $plant
    if (-not (Wait-ForExit $plant 180)) {
        Stop-Proc $plant
        Add-Failure "the planted self-test did not finish within 180 s; see reach-plant.out.log"
    }
    Start-Sleep -Milliseconds 300

    $plantLines = @()
    if (Test-Path $plantOut) { $plantLines = @(Get-Content $plantOut) }
    $verdicts = @($plantLines | Where-Object { $_ -match '^\[reach-selftest\] VERDICT ' })
    $summary  = @($plantLines | Where-Object { $_ -match '^\[reach-selftest\] SUMMARY ' })

    foreach ($v in $verdicts) { Write-Host "        $v" -ForegroundColor DarkGray }
    if ($summary.Count -eq 0) {
        Add-Failure "the planted self-test printed no SUMMARY line at all -- it died before it finished; see reach-plant.out.log"
    } else {
        Write-Host "        $($summary[-1])" -ForegroundColor DarkGray
        if ($summary[-1] -notmatch 'result=PASS') {
            Add-Failure "planted self-test: $($summary[-1])"
        }
        if ($summary[-1] -match 'cases=(\d+)') {
            $cases = [int]$matches[1]
            if ($verdicts.Count -ne $cases) {
                Add-Failure "planted self-test reported cases=$cases but printed $($verdicts.Count) VERDICT line(s) -- a case was skipped rather than judged"
            }
        }
    }

    # ==========================================================================================
    # PHASE 2 -- Confirm refused, live round, target inside the pillar
    # ==========================================================================================
    Write-Host "[2/4] Confirm on a target inside the pillar -> NobodyCouldReachThat..." -ForegroundColor Cyan
    $refuseOut = Join-Path $script:LogDir "reach-refuse.out.log"
    $server = Start-ReachServer "reach-refuse" @(
        "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
        "--seed-test-props", $PillarCentre, "--reach-target", $SeededPropId,
        "--round-script", $RoundScript)
    $procs += $server
    if (-not (Wait-ForLogLine $refuseOut "\[server\] listening" 60)) {
        Stop-Proc $server
        Write-Fail "the server never reported listening on udp/$Port; see reach-refuse.out.log"
    }

    $bots = @()
    foreach ($name in @("ReachRefuseA", "ReachRefuseB")) {
        $p = Start-ReachBot "reach-$name" $name $BotDurationSec
        $procs += $p
        $bots += $p
        # A short stagger so join ORDER is unambiguous -- the first to arrive is the hider.
        Start-Sleep -Milliseconds 1200
    }
    foreach ($b in $bots) {
        if (-not (Wait-ForExit $b ($BotDurationSec + 60))) {
            Stop-Proc $b
            Add-Failure "a refuse-phase bot did not exit within its own duration + 60 s"
        }
    }
    Stop-Proc $server
    Start-Sleep -Milliseconds 500

    $refuseLines = @()
    if (Test-Path $refuseOut) { $refuseLines = @(Get-Content $refuseOut) }
    $refusals = @($refuseLines | Where-Object { $_ -match '\[round\] refused: (\w+)' } |
        ForEach-Object { if ($_ -match '\[round\] refused: (\w+)') { $matches[1] } })
    $seeking  = @($refuseLines | Where-Object { $_ -match '\[round\] phase Hiding -> Seeking' })
    $layer3   = @($refuseLines | Where-Object { $_ -match '^\[reach\] layer3 \[confirm\]' })

    if ($layer3.Count -eq 0) {
        Add-Failure "no '[reach] layer3 [confirm]' line -- the Confirm-time audit never ran, so whatever the round did it did NOT do it on a measured fact"
    } else {
        Write-Host "        $($layer3[-1])" -ForegroundColor DarkGray
        if ($layer3[-1] -notmatch 'UNREACHABLE \(InsideStatic\)') {
            Add-Failure "the Confirm-time audit said '$($layer3[-1])'; expected UNREACHABLE (InsideStatic) for a crate seeded inside the pillar"
        }
    }
    if ($refusals -notcontains "NobodyCouldReachThat") {
        Add-Failure "the server never logged 'refused: NobodyCouldReachThat' (it logged: $($refusals -join ', ')) -- Confirm was not refused for the reason S5b names"
    } else {
        Write-Host "        server refused Confirm: NobodyCouldReachThat" -ForegroundColor DarkGray
    }
    if ($seeking.Count -gt 0) {
        Add-Failure "the round entered Seeking anyway -- a refusal that does not stop the transition is not a refusal"
    }

    # ==========================================================================================
    # PHASE 3 -- Confirm accepted, the control
    # ==========================================================================================
    Write-Host "[3/4] Confirm on a target on the open floor -> accepted..." -ForegroundColor Cyan
    $allowOut = Join-Path $script:LogDir "reach-allow.out.log"
    $server = Start-ReachServer "reach-allow" @(
        "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
        "--seed-test-props", $OpenFloor, "--reach-target", $SeededPropId,
        "--round-script", $RoundScript)
    $procs += $server
    if (-not (Wait-ForLogLine $allowOut "\[server\] listening" 60)) {
        Stop-Proc $server
        Write-Fail "the server never reported listening on udp/$Port for the accept phase; see reach-allow.out.log"
    }

    $bots = @()
    foreach ($name in @("ReachAllowA", "ReachAllowB")) {
        $p = Start-ReachBot "reach-$name" $name $BotDurationSec
        $procs += $p
        $bots += $p
        Start-Sleep -Milliseconds 1200
    }
    foreach ($b in $bots) {
        if (-not (Wait-ForExit $b ($BotDurationSec + 60))) {
            Stop-Proc $b
            Add-Failure "an accept-phase bot did not exit within its own duration + 60 s"
        }
    }
    Stop-Proc $server
    Start-Sleep -Milliseconds 500

    $allowLines = @()
    if (Test-Path $allowOut) { $allowLines = @(Get-Content $allowOut) }
    $allowRefusals = @($allowLines | Where-Object { $_ -match '\[round\] refused: (\w+)' } |
        ForEach-Object { if ($_ -match '\[round\] refused: (\w+)') { $matches[1] } })
    $allowSeeking  = @($allowLines | Where-Object { $_ -match '\[round\] phase Hiding -> Seeking' })
    $allowLayer3   = @($allowLines | Where-Object { $_ -match '^\[reach\] layer3 \[confirm\]' })

    if ($allowLayer3.Count -eq 0) {
        Add-Failure "accept phase: no '[reach] layer3 [confirm]' line -- the audit never ran, so the acceptance proves nothing about layer 3"
    } else {
        Write-Host "        $($allowLayer3[-1])" -ForegroundColor DarkGray
        if ($allowLayer3[-1] -notmatch 'reachable \(Reachable\)') {
            Add-Failure "accept phase: the Confirm-time audit said '$($allowLayer3[-1])'; expected reachable"
        }
    }
    if ($allowRefusals -contains "NobodyCouldReachThat") {
        Add-Failure "accept phase: the server refused Confirm with NobodyCouldReachThat for a crate on the open floor -- layer 3 is refusing everything, which would also make phase 2 pass"
    }
    if ($allowSeeking.Count -eq 0) {
        Add-Failure "accept phase: the round never entered Seeking (refusals logged: $($allowRefusals -join ', '))"
    } else {
        Write-Host "        round entered Seeking on the Confirm press" -ForegroundColor DarkGray
    }

    # ==========================================================================================
    # PHASE 4 -- what it costs
    # ==========================================================================================
    Write-Host "[4/4] cost: $CostPropTarget props disturbed, audits/s, queries/s, frame time..." -ForegroundColor Cyan
    $costOut = Join-Path $script:LogDir "reach-cost.out.log"
    $server = Start-ReachServer "reach-cost" @(
        "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
        "--seed-test-props", (Get-CostSeed $CostPropTarget), "--reach-cost", $CostSeconds)
    $procs += $server
    if (-not (Wait-ForLogLine $costOut "\[server\] listening" 60)) {
        Stop-Proc $server
        Write-Fail "the server never reported listening on udp/$Port for the cost phase; see reach-cost.out.log"
    }

    $bots = @()
    foreach ($name in @("ReachCostA", "ReachCostB")) {
        $p = Start-ReachBot "reach-$name" $name $CostBotDuration
        $procs += $p
        $bots += $p
        Start-Sleep -Milliseconds 800
    }
    foreach ($b in $bots) {
        if (-not (Wait-ForExit $b ($CostBotDuration + 60))) {
            Stop-Proc $b
            Add-Failure "a cost-phase bot did not exit within its own duration + 60 s"
        }
    }
    # The probe quits the server itself when its window closes; give it that long plus slack.
    if (-not (Wait-ForExit $server 120)) {
        Stop-Proc $server
        Add-Failure "the cost probe never quit the server -- it did not reach its own report"
    }
    Start-Sleep -Milliseconds 300

    $costLines = @()
    if (Test-Path $costOut) { $costLines = @(Get-Content $costOut) }
    $costSummary = @($costLines | Where-Object { $_ -match '^\[reach-cost\] SUMMARY ' })
    foreach ($l in @($costLines | Where-Object { $_ -match '^\[reach-cost\] (props|rest audits|integrity|server physics)' })) {
        Write-Host "        $l" -ForegroundColor DarkGray
    }
    if ($costSummary.Count -eq 0) {
        Add-Failure "the cost probe printed no SUMMARY line; see reach-cost.out.log"
    } else {
        Write-Host "        $($costSummary[-1])" -ForegroundColor DarkGray
        # The one ASSERTION in this phase, and it is about the measurement rather than about a
        # budget: a cost run that disturbed nothing reports a beautiful frame time and means
        # nothing at all. Talon has not set a frame-time budget for this game, and inventing one
        # here would be a number nobody agreed to.
        if ($costSummary[-1] -match 'props=(\d+)') {
            # AT LEAST the seeded population, not exactly it: the world also has every AUTHORED
            # prop in it. Measured the hard way -- the first version of this check asserted
            # equality, went red at "expected exactly 150", and the four extra crates CARRY-1
            # authored were the level, not a bug in the seed. It has moved twice since, exactly
            # as predicted: 154 at REACH-1 (150 + CARRY-1's 4), and 283 at HOLD-1's BTN-1 merge
            # (150 + the 133 the world now authors -- SHELF-1's 130 plus BTN-1's rack of 3).
            # The check is written as a floor precisely so a growing level is not a red.
            $propCount = [int]$matches[1]
            if ($propCount -lt $CostPropTarget) {
                Add-Failure "the cost run had $propCount prop(s) in the world, expected at least the $CostPropTarget seeded -- the measurement is of the wrong population"
            }
        }
        if ($costSummary[-1] -match 'audits=(\d+)') {
            $auditCount = [int]$matches[1]
            if ($auditCount -lt $CostPropTarget) {
                Add-Failure "the cost run ran only $auditCount rest audit(s) -- with $CostPropTarget props shoved repeatedly, that means the props never settled and the number measures an idle server"
            }
        }
    }
} finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host ""
if ($script:Failures.Count -gt 0) {
    Write-Host "REACH-TEST FAILED ($($script:Failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "REACH-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: the planted room's eight cases; Confirm refused NobodyCouldReachThat inside the pillar and accepted on the open floor; the audit's cost measured with $CostPropTarget props." -ForegroundColor Green
Write-Host ""
Write-Host "REACH-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
