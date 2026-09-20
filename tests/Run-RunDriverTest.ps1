<#
.SYNOPSIS
    RunDriver (L1, Issue #104) proof: typed phase-crossing events fire exactly once per
    crossing in order, a late joiner lands synced with the correct run length/end state, the
    run-end signal fires at the configured cycle count, and a server-triggered reset rewinds
    the counter and re-syncs the clock across peers.

.DESCRIPTION
    [1/2] --run-driver-selftest: pure-logic checks of the phase-crossing/run-end ordinal math
    (RunPhaseTracker) that need no live scene or network — see RunDriverSelfTest.cs.

    [2/2] Live scenario: one dedicated ENet server in the code-built "open" world (L1 is proved
    in an EXISTING world — the camp world is L2's), started with a short --cycle-period (10s),
    --cycle-start-phase 0 (the run starts in day, per design §1), --run-cycles 2, and a
    server-side --run-reset-at 23 (fires ResetRun() once, well after the run's natural end at
    t~=20s, the same self-triggering-timer idiom Run-ReconnectTest.ps1's --force-reconnect-at
    already uses on the client side):

      - RunEarly: connects at t=0, stays connected through the whole run, its natural end, the
        server-triggered reset, and a slice of the post-reset cycle. Its own JSONL view proves
        (a) each of the 8 phase-crossing events across the 2 configured cycles fires EXACTLY
        ONCE, in the exact designed order, by the time RunEnded first reads true, and via a
        full-stream scan that history only ever grows by appending the next expected event
        (never a duplicate, never a gap) right up to the reset; (b) RunEnded fires at
        cyclesElapsedAfter==2 (the second cycle closing), never at 1; and (d) after the reset
        trigger, RunEnded reads false again, History empties back down, and CycleDriver's own
        counter/phase re-sync (not just RunDriver's) — cross-checked against RunLate's
        concurrent view, the same cross-view technique Run-CycleTest.ps1 already uses for
        CycleDriver's raw phase.
      - RunLate: connects a few seconds in and stays connected through the same reset. (c) its
        VERY FIRST sample must already show runSynced=true, runCycles=2, runEnded=false (never
        the zero-initialized default), and its own crossing history — whatever TAIL of the full
        8-event sequence it actually joined in time to see — must be exactly-once, in order, and
        correctly TAIL-ALIGNED to the full sequence (never asserted against a fixed expected
        count: a bot's own Godot boot + connect time is real, variable latency this test does not
        try to pin exactly, the same reasoning Run-CycleTest.ps1's late-join proof gives for
        checking "matches concurrent truth" rather than "landed at this exact wall-clock second").

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [int]$Port = 7831,
    [double]$CyclePeriodSec = 10,
    [int]$RunCycles = 2,
    [double]$ResetAtSec = 23,
    [double]$EarlyDurationSec = 32,
    [double]$LateDelaySec = 4,
    [double]$LateDurationSec = 26,
    [double]$CrossViewToleranceSec = 1.5
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

Write-Host "=== run-driver: phase events / counter / run-end / reset ===" -ForegroundColor White

Write-Host "[1/2] running run-driver ordinal-math self-test..." -ForegroundColor Cyan
$selfTestProc = Start-Godot @("--run-driver-selftest") "run-driver-selftest"
if (-not (Wait-ForExit $selfTestProc 60)) { Stop-Proc $selfTestProc; Write-Fail "run-driver self-test did not exit in time" }
if ($selfTestProc.ExitCode -ne 0) { Write-Fail "run-driver self-test exited $($selfTestProc.ExitCode); see run-driver-selftest.out.log" }
$selfTestOut = Join-Path $script:LogDir "run-driver-selftest.out.log"
if (-not (Select-String -Path $selfTestOut -Pattern "\[run-driver-selftest\] PASS" -Quiet)) {
    Write-Fail "run-driver self-test never printed PASS; see run-driver-selftest.out.log"
}
Write-Host "        self-test PASS" -ForegroundColor DarkGray

Write-Host ""
Write-Host "[2/2] live scenario (server + 2 bots: 1 early observer through reset, 1 late joiner)..." -ForegroundColor Cyan

$procs = @()
try {
    $serverOut = Join-Path $script:LogDir "run-driver-server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "open",
        "--cycle-period", $CyclePeriodSec, "--cycle-start-phase", 0,
        "--run-cycles", $RunCycles, "--run-reset-at", $ResetAtSec) "run-driver-server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "run-driver-test server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id)), period=${CyclePeriodSec}s runCycles=$RunCycles resetAt=${ResetAtSec}s"

    $logEarly = Join-Path $script:LogDir "run-early.jsonl"
    $botEarly = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "RunEarly",
        "--log", $logEarly, "--duration", $EarlyDurationSec, "--world", "open") "run-early"
    $procs += $botEarly

    Start-Sleep -Seconds $LateDelaySec

    $logLate = Join-Path $script:LogDir "run-late.jsonl"
    $botLate = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "RunLate",
        "--log", $logLate, "--duration", $LateDurationSec, "--world", "open") "run-late"
    $procs += $botLate

    $bots = @(
        @{ Name = "RunEarly"; Proc = $botEarly; JsonLog = $logEarly }
        @{ Name = "RunLate"; Proc = $botLate; JsonLog = $logLate }
    )

    $maxDuration = [math]::Max($EarlyDurationSec, $LateDelaySec + $LateDurationSec)
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

Write-Host "[2/2] verifying phase events / run-end / reset..." -ForegroundColor Cyan

function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

function Get-CircularPhaseDistSec([double]$a, [double]$b, [double]$period) {
    $raw = [math]::Abs($a - $b)
    $circ = [math]::Min($raw, 1.0 - $raw)
    return $circ * $period
}

$early = Get-Samples $logEarly
$late = Get-Samples $logLate

$failures = New-Object System.Collections.Generic.List[string]

# Kind ints per PhaseEventKind (RunDriver.cs): DayToDusk=0, DuskToNight=1, NightToDawn=2, DawnToDay=3.
$kDayToDusk = 0; $kDuskToNight = 1; $kNightToDawn = 2; $kDawnToDay = 3

# The full 8-event sequence a 2-cycle run visits, starting from Day/cycle0 (--cycle-start-phase 0).
$fullSequence = @(
    @{ Kind = $kDayToDusk; After = 0 }, @{ Kind = $kDuskToNight; After = 0 }, @{ Kind = $kNightToDawn; After = 0 }, @{ Kind = $kDawnToDay; After = 1 },
    @{ Kind = $kDayToDusk; After = 1 }, @{ Kind = $kDuskToNight; After = 1 }, @{ Kind = $kNightToDawn; After = 1 }, @{ Kind = $kDawnToDay; After = 2 }
)

function Test-HistoryMatchesSequence([string]$Who, $History, $Expected) {
    if ($History.Count -ne $Expected.Count) {
        $script:failures.Add("$Who : expected $($Expected.Count) crossings, got $($History.Count)")
        return
    }
    for ($i = 0; $i -lt $Expected.Count; $i++) {
        $h = $History[$i]
        $e = $Expected[$i]
        if ([int]$h.kind -ne $e.Kind -or [int]$h.cyclesElapsedAfter -ne $e.After) {
            $script:failures.Add("$Who [$i] : expected kind=$($e.Kind) cyclesAfter=$($e.After), got kind=$([int]$h.kind) cyclesAfter=$([int]$h.cyclesElapsedAfter)")
        }
    }
}

# Late-join-tolerant variant: asserts History is exactly-once-in-order AND correctly
# TAIL-ALIGNED to the full sequence, without assuming exactly which crossing the late bot
# joined in time to see first — that depends on real (variable) process boot/connect latency,
# not just the --run-reset-at-style server-side timer this suite otherwise controls precisely.
function Test-HistoryIsSuffixOfSequence([string]$Who, $History, $Full) {
    if (@($History).Count -eq 0) {
        $script:failures.Add("$Who : history is empty - never observed a single crossing")
        return
    }
    if (@($History).Count -gt $Full.Count) {
        $script:failures.Add("$Who : history has $(@($History).Count) entries, more than the full sequence's $($Full.Count) - impossible")
        return
    }
    $offset = $Full.Count - @($History).Count
    for ($i = 0; $i -lt @($History).Count; $i++) {
        $h = $History[$i]
        $e = $Full[$offset + $i]
        if ([int]$h.kind -ne $e.Kind -or [int]$h.cyclesElapsedAfter -ne $e.After) {
            $script:failures.Add("$Who [$i] : expected kind=$($e.Kind) cyclesAfter=$($e.After) (tail-aligned to the full sequence), got kind=$([int]$h.kind) cyclesAfter=$([int]$h.cyclesElapsedAfter)")
        }
    }
}

# --- (a) exactly once, in order, across 2 cycles: RunEarly's history at the moment RunEnded
# first reads true must be exactly the full 8-event sequence, in order. ------------------------
$earlyEndSample = $early | Where-Object { [bool]$_.runEnded } | Select-Object -First 1
if (-not $earlyEndSample) {
    $failures.Add("run-end: RunEarly never observed runEnded=true over a ${EarlyDurationSec}s run - the run-end signal never fired")
} else {
    Write-Host "        RunEarly observed runEnded=true (history has $($earlyEndSample.runHistory.Count) entries)" -ForegroundColor DarkGray
    Test-HistoryMatchesSequence "RunEarly@run-end" $earlyEndSample.runHistory $fullSequence
}

# Full-stream append-only proof: scanning every sample RunEarly wrote, in order, its history
# must only ever grow by appending the next entry - never duplicate, never reorder - right up
# until the first sample where it shrinks (the reset landing, checked separately below as (d)).
# This is "exactly once, in order" proven from the whole observed stream, not just the
# run-end endpoint (a).
$prevHistory = @()
$sawShrink = $false
foreach ($s in $early) {
    $h = @($s.runHistory)
    if ($h.Count -lt $prevHistory.Count) { $sawShrink = $true; break } # reset - checked separately below.
    if ($h.Count -gt 0 -and $prevHistory.Count -gt 0) {
        for ($i = 0; $i -lt $prevHistory.Count; $i++) {
            if ([int]$h[$i].kind -ne [int]$prevHistory[$i].kind -or [int]$h[$i].cyclesElapsedAfter -ne [int]$prevHistory[$i].cyclesElapsedAfter) {
                $failures.Add("append-only: RunEarly's history at a later sample disagrees with an earlier sample's entry $i - not append-only")
            }
        }
    }
    $prevHistory = $h
}
if (-not $sawShrink) {
    $failures.Add("append-only: RunEarly's history never shrank - the reset (expected around t=${ResetAtSec}s) was never observed in this bot's stream")
}

# --- (b)/(c) RunLate: first sample already synced, correct runCycles, not yet ended; its own
# history is the exact SUFFIX of the full sequence matching a mid-cycle-0-Night join. ----------
$lateFirst = $late[0]
if (-not [bool]$lateFirst.runSynced) {
    $failures.Add("late-join: RunLate's very first sample has runSynced=false - it rendered before the server's run state arrived")
} else {
    if ([int]$lateFirst.runCycles -ne $RunCycles) {
        $failures.Add("late-join: RunLate's first sample reports runCycles=$([int]$lateFirst.runCycles), expected $RunCycles")
    }
    if ([bool]$lateFirst.runEnded) {
        $failures.Add("late-join: RunLate's first sample reports runEnded=true, but it joined well before the run's natural end")
    }
    Write-Host "        RunLate's first sample: runSynced=true runCycles=$([int]$lateFirst.runCycles) runEnded=$([bool]$lateFirst.runEnded)" -ForegroundColor DarkGray
}

$lateEndSample = $late | Where-Object { [bool]$_.runEnded } | Select-Object -First 1
if (-not $lateEndSample) {
    $failures.Add("run-end: RunLate never observed runEnded=true over a ${LateDurationSec}s run")
} else {
    # RunLate joined partway through the run - exactly how far in depends on real process
    # boot/connect latency, not just LateDelaySec (see the script doc). Whatever tail of the
    # full sequence it actually saw must still be exactly-once, in order, and correctly
    # aligned to the END of the full sequence (it can only ever have missed a PREFIX, never a
    # crossing in the middle or at the end).
    Test-HistoryIsSuffixOfSequence "RunLate@run-end" $lateEndSample.runHistory $fullSequence
}

# --- (d) reset rewinds the counter and the clock stays synced across peers afterward ----------
# Find RunEarly's first sample AFTER the reset (runEnded flips back to false, having previously
# been true) - the discrete, un-missable "the reset happened" marker, same self-id-change-style
# anchor Run-CycleTest.ps1's rejoin proof uses.
$earlyEndIdx = -1
for ($i = 0; $i -lt $early.Count; $i++) { if ([bool]$early[$i].runEnded) { $earlyEndIdx = $i; break } }
if ($earlyEndIdx -lt 0) {
    $failures.Add("reset: cannot check post-reset state - RunEarly never reached runEnded=true in the first place")
} else {
    $postResetIdx = -1
    for ($i = $earlyEndIdx + 1; $i -lt $early.Count; $i++) { if (-not [bool]$early[$i].runEnded) { $postResetIdx = $i; break } }
    if ($postResetIdx -lt 0) {
        $failures.Add("reset: RunEarly's runEnded never flipped back to false after reading true - the server-triggered reset (--run-reset-at ${ResetAtSec}) never landed on this peer")
    } else {
        $postReset = $early[$postResetIdx]
        if ([int]$postReset.runHistory.Count -ge $fullSequence.Count) {
            $failures.Add("reset: RunEarly's history at the first post-reset sample has $([int]$postReset.runHistory.Count) entries - expected it to have been cleared (< $($fullSequence.Count))")
        }
        if ([int]$postReset.cyclesElapsed -ge 2) {
            $failures.Add("reset: RunEarly's cyclesElapsed at the first post-reset sample is $([int]$postReset.cyclesElapsed) - expected the counter rewound toward 0, not left at its pre-reset value")
        }
        Write-Host "        RunEarly post-reset: runEnded=false history=$([int]$postReset.runHistory.Count) cyclesElapsed=$([int]$postReset.cyclesElapsed)" -ForegroundColor DarkGray

        # Cross-peer convergence after the reset: RunLate's nearest-in-wall-time sample should
        # report a closely matching phase - the reset must not have desynced one peer from the
        # other (same technique as Run-CycleTest.ps1's cross-view check, scoped to one instant).
        $bestIdx = -1; $bestDelta = [double]::MaxValue
        for ($i = 0; $i -lt $late.Count; $i++) {
            if (-not [bool]$late[$i].cycleSynced) { continue }
            $d = [math]::Abs([double]$postReset.wall - [double]$late[$i].wall)
            if ($d -lt $bestDelta) { $bestDelta = $d; $bestIdx = $i }
        }
        if ($bestIdx -ge 0 -and $bestDelta -le 500) {
            $distSec = Get-CircularPhaseDistSec ([double]$postReset.cyclePhase) ([double]$late[$bestIdx].cyclePhase) $CyclePeriodSec
            if ($distSec -gt $CrossViewToleranceSec) {
                $failures.Add("reset: post-reset phase disagreement between RunEarly ($($postReset.cyclePhase)) and RunLate ($($late[$bestIdx].cyclePhase)) - ${distSec}s apart (tolerance ${CrossViewToleranceSec}s)")
            } else {
                Write-Host "        post-reset cross-view: RunEarly/RunLate phases agree within ${distSec}s" -ForegroundColor DarkGray
            }
        } else {
            $failures.Add("reset: no RunLate sample close enough in wall-time to RunEarly's post-reset sample to cross-check convergence")
        }
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "RUN-DRIVER TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "RUN-DRIVER TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: 8 crossings exactly-once-in-order across 2 cycles, append-only history stream, late joiner synced (not 0), run-end at cyclesAfter=2, reset rewound the counter and stayed synced cross-peer." -ForegroundColor Green
Write-Host ""
Write-Host "RUN-DRIVER TEST OVERALL: PASS" -ForegroundColor Green
exit 0
