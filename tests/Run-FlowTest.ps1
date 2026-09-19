<#
.SYNOPSIS
    CORE-PROG-A1 (core-spine spec) live proof: the playthrough spine end to end - Boot ->
    RoundIntro -> InRound -> verdict -> RoundEnd -> UpgradeLobby -> re-anchored RoundIntro(2)
    -> Loss -> Play Again -> a fresh playthrough - plus the quota ledger's replication,
    late-join sync, and the banking denial rules, all over a real ENet session.

.DESCRIPTION
    [1/2] --flow-selftest: the quota schedule RESOURCE path - assets/run/quota_schedule.tres
    loads, drives QuotaMath through the file's own fields, and a field tweak changes the
    curve through unchanged code (acceptance criterion 6's .tres half).

    [2/2] Live scenario: dedicated ENet server, "open" world, 12 s cycle, flow timers
    1/2/2 s, quota early curve [1,999] (round 1 trivially survivable, round 2 doomed),
    two scheduled server-side banks (the drop-off stand-in):
      - t=6  : bank 3 units for bot0 -> ACCEPTED (InRound), replicates to every peer.
      - t=11.5: bank 2 units for bot0 -> DENIED NotAcceptingNow (RoundEnd - the verdict
        landed at ~10.8 s), delivered to bot0 ONLY, and the banked count is untouched
        (the haul is never consumed on a denial - spec section 6 case 6).
    FlowEarly (t~0, 36 s) rides the whole arc and sends RequestPlayAgain at its t=29
    (server Loss landed at ~25.6 s). FlowLate joins at t~11 - its FIRST sample must
    already carry flowSynced + quotaSynced with the server's banked count (late-join
    funnel, acceptance criterion 7).

    Asserted per peer from JSONL + logs:
      (a) first samples synced, never zero-defaults;
      (b) the exact applied transition sequence (LoopUiTelemetry's FlowTransitions mirror):
          ...2>3@1, 3>4@1, 4>1@2, 1>2@2, 2>5@2, 5>1@1, 1>2@1 - survive, lobby, re-anchored
          round 2, quota loss, Play Again, fresh round 1;
      (c) round-2 demand numbers are the server's (999 / cumulative 1000) on every peer;
      (d) the re-anchor: at round-2 InRound the clock reads cyclesElapsed=1, early phase,
          and the crossing history holds exactly ONE DawnToDay - exactly-once held through
          the re-anchor (spec section 1.5's forward-only invariant, live);
      (e) Loss carries RunOutcomeKind.QuotaMissed at round 2; Play Again yields round 1,
          banked 0, demand back to 1 (the boundary + RunReset sequence, T10);
      (f) denial isolation: bot0 saw NotAcceptingNow, bot1 never saw any denial; banked
          stayed 3 through it;
      (g) the entry guard runs a REAL store (CORE-PROG-A2): the A1-era no-store error is
          ABSENT, T3 runs the documented pristine first pass and T10 fans in full - both
          listing the complete registered slice set (the spec section 5.2 completeness
          probe) - and the T10 double pass collapses to one fan (the dedupe line proves
          the RunReset path reached the store).

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [int]$Port = 7882, # unique across tests/ — 7851 is Run-VendingTest's
    [double]$CyclePeriodSec = 12,
    [double]$EarlyDurationSec = 36,
    [double]$LateDelaySec = 8,
    [double]$LateDurationSec = 26
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

Write-Host "=== flow: playthrough spine + quota ledger (CORE-PROG-A1) ===" -ForegroundColor White

Write-Host "[1/2] running flow self-test (quota schedule resource path)..." -ForegroundColor Cyan
$selfTestProc = Start-Godot @("--flow-selftest") "flow-selftest"
if (-not (Wait-ForExit $selfTestProc 60)) { Stop-Proc $selfTestProc; Write-Fail "flow self-test did not exit in time" }
if ($selfTestProc.ExitCode -ne 0) { Write-Fail "flow self-test exited $($selfTestProc.ExitCode); see flow-selftest.out.log" }
$selfTestOut = Join-Path $script:LogDir "flow-selftest.out.log"
if (-not (Select-String -Path $selfTestOut -Pattern "\[flow-selftest\] PASS" -Quiet)) {
    Write-Fail "flow self-test never printed PASS; see flow-selftest.out.log"
}
Write-Host "        self-test PASS" -ForegroundColor DarkGray

Write-Host ""
Write-Host "[2/2] live scenario (server + early observer + late joiner)..." -ForegroundColor Cyan

$procs = @()
try {
    $serverOut = Join-Path $script:LogDir "flow-server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "open",
        "--cycle-period", $CyclePeriodSec, "--cycle-start-phase", 0,
        "--flow-timers", "1,2,2",
        "--quota-early", "1,999",
        "--quota-bank-at", "6,3,0",
        "--quota-bank-at", "11.5,2,0") "flow-server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "flow server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id)), period=${CyclePeriodSec}s timers=1/2/2 early=[1,999]"

    $logEarly = Join-Path $script:LogDir "flow-early.jsonl"
    $botEarly = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "FlowEarly",
        "--log", $logEarly, "--duration", $EarlyDurationSec, "--world", "open",
        "--cycle-period", $CyclePeriodSec, "--flow-play-again-at", 29) "flow-early"
    $procs += $botEarly

    Start-Sleep -Seconds $LateDelaySec

    $logLate = Join-Path $script:LogDir "flow-late.jsonl"
    $botLate = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "FlowLate",
        "--log", $logLate, "--duration", $LateDurationSec, "--world", "open",
        "--cycle-period", $CyclePeriodSec) "flow-late"
    $procs += $botLate

    $bots = @(
        @{ Name = "FlowEarly"; Proc = $botEarly }
        @{ Name = "FlowLate"; Proc = $botLate }
    )
    $maxDuration = [math]::Max($EarlyDurationSec, $LateDelaySec + $LateDurationSec)
    $deadline = (Get-Date).AddSeconds($maxDuration + 60)
    foreach ($b in $bots) {
        $remaining = [int]((($deadline - (Get-Date)).TotalSeconds) * 1000)
        if ($remaining -lt 1000) { $remaining = 1000 }
        if (-not $b.Proc.WaitForExit($remaining)) { Write-Fail "$($b.Name) did not exit within timeout" }
    }
    foreach ($b in $bots) {
        if ($b.Proc.ExitCode -ne 0) { Write-Fail "$($b.Name) exited with code $($b.Proc.ExitCode)" }
    }
} finally {
    Stop-Procs $procs
}

Write-Host "[2/2] verifying flow + quota assertions..." -ForegroundColor Cyan

function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

$early = Get-Samples $logEarly
$late = Get-Samples $logLate
$failures = New-Object System.Collections.Generic.List[string]

# PlaythroughState ordinals: Boot=0 RoundIntro=1 InRound=2 RoundEnd=3 UpgradeLobby=4 Loss=5.

# --- (a) never the zero default -------------------------------------------------------------
foreach ($pair in @(@{ Who = "FlowEarly"; S = $early[0] }, @{ Who = "FlowLate"; S = $late[0] })) {
    if (-not [bool]$pair.S.flowSynced) { $failures.Add("$($pair.Who): first sample flowSynced=false - rendered before the flow sync landed") }
    if (-not [bool]$pair.S.quotaSynced) { $failures.Add("$($pair.Who): first sample quotaSynced=false - rendered before the quota sync landed") }
}

# --- (b) the applied transition sequence, per peer ------------------------------------------
# FlowEarly connects during round 1 (intro or in-round) and must then witness exactly this
# arc. Its first witnessed transitions may include 0>1@1 / 1>2@1 depending on connect speed,
# so the assertion is: the expected arc appears as a CONTIGUOUS subsequence ending the list.
$expectedArc = @("2>3@1", "3>4@1", "4>1@2", "1>2@2", "2>5@2", "5>1@1", "1>2@1")
$earlyFinal = @($early[-1].flowTransitions)
if ($earlyFinal.Count -lt $expectedArc.Count) {
    $failures.Add("FlowEarly: only $($earlyFinal.Count) transitions witnessed, expected at least $($expectedArc.Count): [$($earlyFinal -join ' ')]")
} else {
    $tail = @($earlyFinal | Select-Object -Last $expectedArc.Count)
    for ($i = 0; $i -lt $expectedArc.Count; $i++) {
        if ($tail[$i] -ne $expectedArc[$i]) {
            $failures.Add("FlowEarly: transition tail[$i] = '$($tail[$i])', expected '$($expectedArc[$i])' (full: [$($earlyFinal -join ' ')])")
        }
    }
}
Write-Host "        FlowEarly transitions: [$($earlyFinal -join ' ')]" -ForegroundColor DarkGray

# --- (c) round-2 demand numbers are the server's, on every peer ------------------------------
foreach ($pair in @(@{ Who = "FlowEarly"; Set = $early }, @{ Who = "FlowLate"; Set = $late })) {
    $round2 = @($pair.Set | Where-Object { [int]$_.flowRound -eq 2 -and [int]$_.flowState -ge 1 -and [bool]$_.quotaSynced }) | Select-Object -First 1
    if (-not $round2) {
        $failures.Add("$($pair.Who): never sampled round 2 - the lobby->intro advance did not reach this peer")
    } else {
        if ([int]$round2.quotaDemandRound -ne 999) { $failures.Add("$($pair.Who): round-2 demand = $([int]$round2.quotaDemandRound), expected the server's 999") }
        if ([int]$round2.quotaCumDemand -ne 1000) { $failures.Add("$($pair.Who): round-2 cumulative demand = $([int]$round2.quotaCumDemand), expected 1000") }
    }
}

# --- (d) the re-anchor: fresh day, cycle 1, exactly one DawnToDay in history -----------------
$reanchored = @($early | Where-Object { [int]$_.flowRound -eq 2 -and [int]$_.flowState -eq 2 }) | Select-Object -First 1
if (-not $reanchored) {
    $failures.Add("re-anchor: FlowEarly never sampled InRound of round 2")
} else {
    if ([int]$reanchored.cyclesElapsed -ne 1) {
        $failures.Add("re-anchor: at round-2 InRound cyclesElapsed = $([int]$reanchored.cyclesElapsed), expected 1 (round N+1 <-> cycles N)")
    }
    if ([double]$reanchored.cyclePhase -gt 0.45) {
        $failures.Add("re-anchor: at round-2 InRound phase = $([double]$reanchored.cyclePhase) - not a fresh day (expected < 0.45)")
    }
    $dawnCount = @($reanchored.runHistory | Where-Object { [int]$_.kind -eq 3 }).Count
    if ($dawnCount -ne 1) {
        $failures.Add("re-anchor: history holds $dawnCount DawnToDay crossings at round-2 InRound, expected exactly 1 - exactly-once broke across the re-anchor")
    }
}

# --- (e) the loss and the fresh playthrough --------------------------------------------------
$loss = @($early | Where-Object { [int]$_.flowState -eq 5 }) | Select-Object -First 1
if (-not $loss) {
    $failures.Add("loss: FlowEarly never sampled Loss")
} else {
    if ([int]$loss.flowOutcomeKind -ne 0) { $failures.Add("loss: outcome kind = $([int]$loss.flowOutcomeKind), expected 0 (QuotaMissed)") }
    if ([int]$loss.flowRound -ne 2) { $failures.Add("loss: round = $([int]$loss.flowRound), expected 2") }
}
$lossIdx = -1
for ($i = 0; $i -lt $early.Count; $i++) { if ([int]$early[$i].flowState -eq 5) { $lossIdx = $i; break } }
if ($lossIdx -ge 0) {
    $fresh = @($early[($lossIdx + 1)..($early.Count - 1)] | Where-Object { [int]$_.flowState -in @(1, 2) -and [int]$_.flowRound -eq 1 }) | Select-Object -First 1
    if (-not $fresh) {
        $failures.Add("play-again: no post-Loss sample in RoundIntro/InRound of round 1 - T10 never landed")
    } else {
        if ([int]$fresh.quotaBanked -ne 0) { $failures.Add("play-again: banked = $([int]$fresh.quotaBanked) in the fresh playthrough, expected 0 (ledger reset)") }
        if ([int]$fresh.quotaDemandRound -ne 1) { $failures.Add("play-again: round-1 demand = $([int]$fresh.quotaDemandRound), expected 1") }
        if ([int]$fresh.flowOutcomeKind -ne -1) { $failures.Add("play-again: LastOutcome still latched in the fresh playthrough") }
    }
}

# --- (f) banking: replication, denial isolation, haul never consumed -------------------------
$serverLines = Get-Content $serverOut
$bankLine = $serverLines | Where-Object { $_ -match "\[quota\] test-bank at=6" } | Select-Object -First 1
if (-not $bankLine) { $failures.Add("bank: server never logged the t=6 test bank") }
elseif ($bankLine -notmatch "accepted=True") { $failures.Add("bank: the t=6 in-round bank was not accepted: $bankLine") }
$denyLine = $serverLines | Where-Object { $_ -match "\[quota\] test-bank at=11\.5" } | Select-Object -First 1
if (-not $denyLine) { $failures.Add("deny: server never logged the t=11.5 test bank") }
elseif ($denyLine -notmatch "accepted=False denial=NotAcceptingNow") { $failures.Add("deny: the t=11.5 RoundEnd bank was not denied as NotAcceptingNow: $denyLine") }
elseif ($denyLine -notmatch "cumBanked=3") { $failures.Add("deny: banked count moved on a denial: $denyLine (expected cumBanked=3 - the haul is never consumed)") }

$earlyBanked = @($early | Where-Object { [int]$_.quotaBanked -eq 3 }) | Select-Object -First 1
if (-not $earlyBanked) { $failures.Add("bank: FlowEarly never saw quotaBanked=3 - the bank broadcast did not replicate") }
$earlyDenied = @($early | Where-Object { [int]$_.quotaLastDenial -eq 1 }) | Select-Object -First 1
if (-not $earlyDenied) { $failures.Add("deny: FlowEarly (the requester) never saw NotAcceptingNow") }
$lateDenied = @($late | Where-Object { [int]$_.quotaLastDenial -ne 0 }) | Select-Object -First 1
if ($lateDenied) { $failures.Add("deny: FlowLate saw a denial ($([int]$lateDenied.quotaLastDenial)) - denials must reach only the requester") }

# --- late-join ledger sync (acceptance criterion 7) ------------------------------------------
if ([int]$late[0].quotaBanked -ne 3) {
    $failures.Add("late-join: FlowLate's FIRST sample has quotaBanked=$([int]$late[0].quotaBanked), expected the server's 3")
}

# --- (g) the entry guard runs a REAL store now (CORE-PROG-A2: the A1-era loud-error
# assertion flipped to absence, exactly as scope 7 promised) --------------------------------
$serverErr = Join-Path $script:LogDir "flow-server.err.log"
$storeErrors = @()
if (Test-Path $serverErr) {
    $storeErrors = @(Select-String -Path $serverErr -Pattern "no WorldStateStore at playthrough start")
}
if ($storeErrors.Count -gt 0) {
    $failures.Add("entry-guard: the no-store error still fires ($($storeErrors.Count)x) - the store was not handed to PlaythroughDriver")
}
# The boundary itself, at BOTH playthrough starts: T3 (session start) is the documented
# pristine first pass (spec section 5.4's licensed optimization - session-start state IS the
# initial state, and a full T3 fan would destroy StartAsServer's test seeding); T10 (Play
# Again) fans in FULL. Each line lists the complete registered slice set - the
# RegisteredSliceIds completeness probe against spec section 5.2's mutator table (acceptance
# criterion 1), asserted live where the real Gameplay construction actually registered them.
# The six survivors of the MVP extraction (DECISION-LOG 2.3/2.4/2.5): photo-registry,
# polaroid-film-bank, arrows, wallets, campfire, torches, glow-sticks and wall-map went with the
# camp economy that owned them, and eel-charges with the eel seam. Derived, not transcribed:
# `grep -rn 'SliceId =>' scripts/` returns exactly these six, so this string IS the completeness
# probe. Re-derive it the same way if a slice is ever added or cut.
$expectedSlices = "reconnect-registry,props,quota-ledger,water,incapacitation,player-sight"
$pristineLines = @(Select-String -Path $serverOut -Pattern "\[worldstate\] boundary reset: pristine first pass")
$fanLines = @(Select-String -Path $serverOut -Pattern "\[worldstate\] boundary reset fanned over")
if ($pristineLines.Count -ne 1) {
    $failures.Add("entry-guard: expected exactly one pristine T3 pass, found $($pristineLines.Count)")
}
if ($fanLines.Count -ne 1) {
    $failures.Add("entry-guard: expected exactly one FULL boundary fan (T10 Play Again), found $($fanLines.Count)")
}
foreach ($line in @($pristineLines) + @($fanLines)) {
    if ($line.Line -notmatch [regex]::Escape($expectedSlices)) {
        $failures.Add("entry-guard: boundary slice list mismatch - expected '$expectedSlices' in: $($line.Line)")
    }
}
# The T10 double pass (boundary + RunReset) collapses to one fan per frame, by design.
if (-not (Select-String -Path $serverOut -Pattern "\[worldstate\] boundary reset deduped" -Quiet)) {
    $failures.Add("entry-guard: the T10 RunReset second pass never reached the store (no dedupe line) - the store is not the RunReset subscriber")
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "FLOW TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "FLOW TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: full arc survive->lobby->re-anchored round 2->quota loss->Play Again->fresh playthrough on every peer; round-2 demand 999/1000 replicated; re-anchor landed cycle 1 phase<0.45 with exactly one DawnToDay; bank replicated (3), RoundEnd bank denied to requester only with haul intact; late joiner synced (banked=3 first sample); entry guard runs the REAL store (pristine T3 pass + one full T10 fan + dedupe, complete slice list, no no-store error)." -ForegroundColor Green
Write-Host ""
Write-Host "FLOW TEST OVERALL: PASS" -ForegroundColor Green
exit 0
