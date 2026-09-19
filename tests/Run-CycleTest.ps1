<#
.SYNOPSIS
    Tidal-cycle phase clock (CycleDriver) convergence proof: server-authoritative phase,
    replicated at low cadence with client-side extrapolation + snap-correct, converges
    identically across peers — including a peer that joins mid-cycle and a peer that drops
    and rejoins.

.DESCRIPTION
    [1/2] --cycle-selftest: pure-logic checks of the phase-wrap arithmetic (CyclePhase) and
    the phase-driven sun/moon/sky curves (DayNightSky.Evaluate) that need no live scene or
    network — see CycleSelfTest.cs.

    [2/2] Live scenario: one dedicated ENet server in the code-built "open" world, started
    with a short --cycle-period (12s, vs. the real 120s) and --cycle-start-phase 0.3 so a
    full wrap, a mid-cycle late join, and a reconnect are all observable inside a headless
    test's duration instead of a real 120s cycle:

      - BotEarly1 / BotEarly2: connect at t=0, stay connected the whole run (30s = 2.5
        cycles at the 12s test period). Their own JSONL views prove (a) phase advances
        monotonically within a cycle and wraps cleanly (cyclesElapsed increments exactly at
        each wrap, never off-by-one), and (b) two independently-running peers agree on phase
        within a tight tolerance at matched wall-clock moments.
      - BotLate: connects ~3s after the server starts (well after the server's phase has
        moved off its 0.3 start value, per BUILD-SPEC §5's own framing: "a player joining at
        t=95s must arrive at high tide" — this is that scenario, scaled to the test period).
        (c) its VERY FIRST JSONL sample must already show cycleSynced=true and a phase close
        to the server's true concurrent phase (cross-checked against BotEarly1's matching
        sample) — never the zero-initialized default. This is BUILD-SPEC §5's explicitly
        named "single most likely bug in the whole feature."
      - BotRejoin: connects at t=0, then (--force-reconnect-at) deliberately drops its own
        connection and redials mid-run — the same test-only mechanism Run-ReconnectTest.ps1
        already uses to exercise a real Steam-transport reconnect over the ENet transport CI
        actually has (PR #50, which would make this identity-stable, has not merged as of
        this packet — see its dispatch doc). (d) after the redial, its phase re-syncs to the
        live server truth rather than freezing at its pre-drop value or resetting to 0.

    Negative proof (repo convention): assertion (c) was verified to actually fail against a
    deliberately-broken build (CycleDriver.ClientTick's `if (!Synced) return;` guard removed,
    so an unsynced client silently advances its own local clock from elapsed=0 instead of
    holding for the first authoritative update) before being fixed — see the PR body for the
    captured FAIL output.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [int]$Port = 7830,
    [double]$CyclePeriodSec = 12,
    [float]$CycleStartPhase = 0.3,
    [double]$EarlyDurationSec = 30,
    [double]$LateDelaySec = 3,
    [double]$LateDurationSec = 6,
    [double]$RejoinDurationSec = 16,
    [double]$RejoinAtSec = 6,
    [double]$CrossViewToleranceSec = 1.5
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

Write-Host "=== cycle: CycleDriver phase-clock convergence test ===" -ForegroundColor White

Write-Host "[1/2] running cycle phase-math + light-curve self-test..." -ForegroundColor Cyan
$selfTestProc = Start-Godot @("--cycle-selftest") "cycle-selftest"
if (-not (Wait-ForExit $selfTestProc 60)) { Stop-Proc $selfTestProc; Write-Fail "cycle self-test did not exit in time" }
if ($selfTestProc.ExitCode -ne 0) { Write-Fail "cycle self-test exited $($selfTestProc.ExitCode); see cycle-selftest.out.log" }
$selfTestOut = Join-Path $script:LogDir "cycle-selftest.out.log"
if (-not (Select-String -Path $selfTestOut -Pattern "\[cycle-selftest\] PASS" -Quiet)) {
    Write-Fail "cycle self-test never printed PASS; see cycle-selftest.out.log"
}
Write-Host "        self-test PASS" -ForegroundColor DarkGray

Write-Host ""
Write-Host "[2/2] live scenario (server + 4 bots: 2 early observers, 1 late joiner, 1 forced-reconnect)..." -ForegroundColor Cyan

$procs = @()
try {
    $serverOut = Join-Path $script:LogDir "cycle-server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "open",
        "--cycle-period", $CyclePeriodSec, "--cycle-start-phase", $CycleStartPhase) "cycle-server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "cycle-test server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id)), period=${CyclePeriodSec}s startPhase=$CycleStartPhase"

    $log1 = Join-Path $script:LogDir "cycle-early1.jsonl"
    $bot1 = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "CycleEarly1",
        "--log", $log1, "--duration", $EarlyDurationSec, "--world", "open") "cycle-early1"
    $procs += $bot1

    $log2 = Join-Path $script:LogDir "cycle-early2.jsonl"
    $bot2 = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "CycleEarly2",
        "--log", $log2, "--duration", $EarlyDurationSec, "--world", "open") "cycle-early2"
    $procs += $bot2

    $logRejoin = Join-Path $script:LogDir "cycle-rejoin.jsonl"
    $botRejoin = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "CycleRejoin",
        "--log", $logRejoin, "--duration", $RejoinDurationSec, "--world", "open",
        "--force-reconnect-at", $RejoinAtSec) "cycle-rejoin"
    $procs += $botRejoin

    Start-Sleep -Seconds $LateDelaySec

    $logLate = Join-Path $script:LogDir "cycle-late.jsonl"
    $botLate = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "CycleLate",
        "--log", $logLate, "--duration", $LateDurationSec, "--world", "open") "cycle-late"
    $procs += $botLate

    $bots = @(
        @{ Name = "CycleEarly1"; Proc = $bot1; JsonLog = $log1 }
        @{ Name = "CycleEarly2"; Proc = $bot2; JsonLog = $log2 }
        @{ Name = "CycleRejoin"; Proc = $botRejoin; JsonLog = $logRejoin }
        @{ Name = "CycleLate"; Proc = $botLate; JsonLog = $logLate }
    )

    $maxDuration = [math]::Max($EarlyDurationSec, [math]::Max($RejoinDurationSec, $LateDelaySec + $LateDurationSec))
    $deadline = (Get-Date).AddSeconds($maxDuration + 60)
    foreach ($b in $bots) {
        $remaining = [int]((($deadline - (Get-Date)).TotalSeconds) * 1000)
        if ($remaining -lt 1000) { $remaining = 1000 }
        if (-not $b.Proc.WaitForExit($remaining)) { Write-Fail "$($b.Name) did not exit within timeout" }
    }
    foreach ($b in $bots) {
        if ($b.Proc.ExitCode -ne 0) { Write-Fail "$($b.Name) exited with code $($b.Proc.ExitCode); see $($b.JsonLog)" }
    }
} finally {
    Stop-Procs $procs
}

Write-Host "[2/2] verifying phase convergence..." -ForegroundColor Cyan

function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

$early1 = Get-Samples $log1
$early2 = Get-Samples $log2
$rejoin = Get-Samples $logRejoin
$late = Get-Samples $logLate

$failures = New-Object System.Collections.Generic.List[string]

# Circular phase distance in SECONDS-of-period (0..period/2), so a wrap (phase 0.98 -> 0.02)
# reads as "close", not as a huge divergence.
function Get-CircularPhaseDistSec([double]$a, [double]$b, [double]$period) {
    $raw = [math]::Abs($a - $b)
    $circ = [math]::Min($raw, 1.0 - $raw)
    return $circ * $period
}

# --- (a) monotonic + wrapping, on BotEarly1's own view ---------------------------------
# Reconstructs total elapsed time (cyclesElapsed*period + phase*period) per sample and
# asserts it is non-decreasing across the WHOLE run (the real monotonic proof, independent
# of the wrap itself), that phase never silently regresses without cyclesElapsed advancing
# by exactly 1 at that same sample, and that at least one wrap was actually observed (the
# 30s run at a 12s period must cross at least two).
function Test-MonotonicWrap([string]$Name, $Samples) {
    $prevElapsed = -1.0
    $prevPhase = -1.0
    $prevCycles = -1
    $maxCycles = 0
    foreach ($s in $Samples) {
        if ($null -eq $s.PSObject.Properties["cyclePhase"]) { continue }
        $phase = [double]$s.cyclePhase
        $cycles = [int]$s.cyclesElapsed
        if (-not [bool]$s.cycleSynced) { continue } # pre-sync samples carry no meaningful phase.
        $elapsed = $cycles * $CyclePeriodSec + $phase * $CyclePeriodSec
        if ($prevElapsed -ge 0 -and $elapsed -lt $prevElapsed - 0.05) {
            $script:failures.Add("$Name : total elapsed time regressed ($prevElapsed -> $elapsed) - phase/cyclesElapsed disagree about direction of time")
        }
        if ($prevCycles -ge 0 -and $phase -lt $prevPhase -and ($cycles - $prevCycles) -ne 1) {
            $script:failures.Add("$Name : phase decreased ($prevPhase -> $phase) without cyclesElapsed advancing by exactly 1 (was $prevCycles, now $cycles)")
        }
        if ($cycles -gt $maxCycles) { $maxCycles = $cycles }
        $prevElapsed = $elapsed
        $prevPhase = $phase
        $prevCycles = $cycles
    }
    if ($maxCycles -lt 2) {
        $script:failures.Add("$Name : never observed cyclesElapsed reach 2 over a ${EarlyDurationSec}s run at a ${CyclePeriodSec}s period - the wrap was never actually exercised")
    }
}
Test-MonotonicWrap "CycleEarly1" $early1
Test-MonotonicWrap "CycleEarly2" $early2

# --- (b) cross-view agreement: Early1 vs Early2 at matched wall-clock moments -----------
$early2ByIndex = @($early2)
$agreementChecked = 0
foreach ($s1 in $early1) {
    if ($null -eq $s1.PSObject.Properties["cyclePhase"] -or -not [bool]$s1.cycleSynced) { continue }
    $bestIdx = -1; $bestDelta = [double]::MaxValue
    for ($i = 0; $i -lt $early2ByIndex.Count; $i++) {
        $s2 = $early2ByIndex[$i]
        if (-not [bool]$s2.cycleSynced) { continue }
        $d = [math]::Abs([double]$s1.wall - [double]$s2.wall)
        if ($d -lt $bestDelta) { $bestDelta = $d; $bestIdx = $i }
    }
    if ($bestIdx -lt 0 -or $bestDelta -gt 300) { continue } # no sample close enough in wall-time to compare.
    $s2 = $early2ByIndex[$bestIdx]
    $distSec = Get-CircularPhaseDistSec ([double]$s1.cyclePhase) ([double]$s2.cyclePhase) $CyclePeriodSec
    $agreementChecked++
    if ($distSec -gt $CrossViewToleranceSec) {
        $failures.Add("cross-view: CycleEarly1 phase=$($s1.cyclePhase) vs CycleEarly2 phase=$($s2.cyclePhase) at wall~$($s1.wall)ms differ by ${distSec}s (tolerance ${CrossViewToleranceSec}s)")
    }
}
if ($agreementChecked -eq 0) {
    $failures.Add("cross-view: no matched sample pair found between CycleEarly1 and CycleEarly2 - test staging problem, not a real pass")
} else {
    Write-Host "        cross-view: checked $agreementChecked matched sample pairs, all within ${CrossViewToleranceSec}s" -ForegroundColor DarkGray
}

# --- (c) late joiner gets the server's phase, not 0 -------------------------------------
# BUILD-SPEC §5's named #1 likely bug. Event-anchored: the FIRST sample CycleLate ever
# writes must already be synced and away from the zero-initialized default.
if ($late.Count -eq 0) {
    $failures.Add("late-join: CycleLate produced no samples")
} else {
    $firstLate = $late[0]
    if (-not [bool]$firstLate.cycleSynced) {
        $failures.Add("late-join: CycleLate's very first sample has cycleSynced=false - it rendered before the server's phase arrived")
    } elseif ([double]$firstLate.cyclePhase -lt 0.1) {
        $failures.Add("late-join: CycleLate's very first sample reports phase=$($firstLate.cyclePhase) - indistinguishable from the t=0 default the server was NOT at (started at phase $CycleStartPhase, ${LateDelaySec}s before this bot even connected)")
    } else {
        # Cross-check against the concurrently-connected CycleEarly1's nearest sample - not
        # just "away from 0" but "matches the live truth".
        $bestIdx = -1; $bestDelta = [double]::MaxValue
        for ($i = 0; $i -lt $early1.Count; $i++) {
            if (-not [bool]$early1[$i].cycleSynced) { continue }
            $d = [math]::Abs([double]$firstLate.wall - [double]$early1[$i].wall)
            if ($d -lt $bestDelta) { $bestDelta = $d; $bestIdx = $i }
        }
        if ($bestIdx -ge 0 -and $bestDelta -le 500) {
            $distSec = Get-CircularPhaseDistSec ([double]$firstLate.cyclePhase) ([double]$early1[$bestIdx].cyclePhase) $CyclePeriodSec
            if ($distSec -gt $CrossViewToleranceSec) {
                $failures.Add("late-join: CycleLate's first phase ($($firstLate.cyclePhase)) disagrees with concurrent CycleEarly1 ($($early1[$bestIdx].cyclePhase)) by ${distSec}s")
            } else {
                Write-Host "        late-join: CycleLate's first sample (phase=$($firstLate.cyclePhase), synced=true) matches concurrent truth within ${distSec}s" -ForegroundColor DarkGray
            }
        }
    }
}

# --- (d) forced reconnect re-syncs, not freezes or resets to 0 -------------------------
# Same self-id-change anchor Run-ReconnectTest.ps1 uses: the resumed ENet connection gets a
# brand-new peer id, a discrete, un-missable "the redial happened" marker.
if ($rejoin.Count -lt 3) {
    $failures.Add("rejoin: CycleRejoin log has only $($rejoin.Count) samples")
} else {
    $firstSelf = $rejoin[0].self
    $resumeIdx = -1
    for ($i = 1; $i -lt $rejoin.Count; $i++) {
        if ($rejoin[$i].self -ne $firstSelf) { $resumeIdx = $i; break }
    }
    if ($resumeIdx -lt 0) {
        $failures.Add("rejoin: CycleRejoin's own peer id never changed across the whole run - the forced reconnect never actually redialed, so this run proves nothing")
    } else {
        # Scan forward from the redial for the first re-synced sample.
        $settledIdx = -1
        for ($i = $resumeIdx; $i -lt $rejoin.Count; $i++) {
            if ([bool]$rejoin[$i].cycleSynced) { $settledIdx = $i; break }
        }
        if ($settledIdx -lt 0) {
            $failures.Add("rejoin: CycleRejoin never reported cycleSynced=true after the peer-id change at sample $resumeIdx - resync never completed")
        } else {
            $resumed = $rejoin[$settledIdx]
            if ([double]$resumed.cyclePhase -lt 0.02 -and [int]$resumed.cyclesElapsed -eq 0) {
                $failures.Add("rejoin: first post-resume synced sample reads phase=$($resumed.cyclePhase) cyclesElapsed=0 - indistinguishable from a reset to t=0 rather than a resync to the live phase")
            }
            # Cross-check against Early1's concurrent phase, same as the late-join proof.
            $bestIdx = -1; $bestDelta = [double]::MaxValue
            for ($i = 0; $i -lt $early1.Count; $i++) {
                if (-not [bool]$early1[$i].cycleSynced) { continue }
                $d = [math]::Abs([double]$resumed.wall - [double]$early1[$i].wall)
                if ($d -lt $bestDelta) { $bestDelta = $d; $bestIdx = $i }
            }
            if ($bestIdx -ge 0 -and $bestDelta -le 500) {
                $distSec = Get-CircularPhaseDistSec ([double]$resumed.cyclePhase) ([double]$early1[$bestIdx].cyclePhase) $CyclePeriodSec
                if ($distSec -gt $CrossViewToleranceSec) {
                    $failures.Add("rejoin: post-resume phase ($($resumed.cyclePhase)) disagrees with concurrent CycleEarly1 ($($early1[$bestIdx].cyclePhase)) by ${distSec}s")
                } else {
                    Write-Host "        rejoin: post-resume phase (sample $settledIdx, phase=$($resumed.cyclePhase)) matches concurrent truth within ${distSec}s" -ForegroundColor DarkGray
                }
            }
        }
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "CYCLE TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "CYCLE TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: phase monotonic + wrapping on both early peers, cross-view agreement within ${CrossViewToleranceSec}s, late joiner synced to live phase (not 0), forced reconnect re-synced to live phase." -ForegroundColor Green
Write-Host ""
Write-Host "CYCLE TEST OVERALL: PASS" -ForegroundColor Green
exit 0
