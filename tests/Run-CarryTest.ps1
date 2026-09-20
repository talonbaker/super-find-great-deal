<#
.SYNOPSIS
    Server-authoritative carry (hold-first) convergence test: prove every peer agrees on who
    holds what, a contested grab is rejected, a drop converges, a disconnecting holder releases
    its prop, and a late joiner converges via the dump.

.DESCRIPTION
    Launches a headless dedicated server in the "propsync" world (server-spawned test props:
    prop 1 = Crate at (2.5, 0.5, 2.5), prop 2 = Ball at (-2.5, 0.5, -2.5)) and four scripted
    headless bots:

      - CarryBotA: walks to prop 1, grabs it (earliest local clock >= 1.0s), holds ~9s, drops.
      - CarryBotB: walks to prop 1 too, attempts a grab at local clock >= 3.5s (well after A
        already holds it) - expected to be rejected (first-grab-wins). Stays connected the
        whole test as a second independent witness to A's grab/hold/drop.
      - CarryBotC: walks to prop 2, grabs it (earliest local clock >= 1.0s), never voluntarily
        drops, and exits (disconnects) after a short duration WHILE STILL HOLDING it.
      - CarryBotD: joins late (~4s after the server starts), after A's grab has already
        converged, and never scripts a grab/drop - its role is purely to observe the late-join
        dump.

    Each bot logs one JSONL sample per tick (BotHarness), including every networked prop's
    current holder. The script normalizes each bot's own log to its own local elapsed clock
    (its first sample's timestamp), since separate OS processes don't share a wall clock, then
    asserts within generously-buffered windows (a fiddly-timing test is expected to need wide
    settle windows, not looser assertions):

      1. Grab converges       - after A's grab, both A's and B's own views show prop 1 held by A.
      2. Contested grab rejected - B's grab attempt never changes prop 1's holder away from A,
                                    and B never observes itself as prop 1's holder.
      3. Drop converges       - after A's drop, both A's and B's own views show prop 1 unheld.
      4. Disconnect-release   - B's view shows prop 2 held by C, then (after C disconnects) unheld.
      5. Late-join            - D's very first sample already shows prop 1 held by A.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [int]$Port = 7799,
    [double]$ABDurationSec = 16,
    [double]$CDurationSec = 5,
    [double]$LateJoinDelaySec = 4,
    [double]$DDurationSec = 4,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# Server's authoritative test-prop roster for the "propsync" world (PropManager.SpawnInitialProps).
$Prop1Id = 1 # Crate at (2.5, 0.5, 2.5)
$Prop2Id = 2 # Ball at (-2.5, 0.5, -2.5)

Write-Host "=== carry: server-authoritative hold-first convergence test ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$procs = @()
try {
    Write-Host "[1/3] launching dedicated server (propsync world) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "carry.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "propsync") "carry.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    Write-Host "[2/3] launching scripted-carry bots..." -ForegroundColor Cyan
    $t0 = Get-Date

    $aLog = Join-Path $script:LogDir "carryA.jsonl"
    $botA = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "CarryBotA",
        "--log", $aLog, "--duration", $ABDurationSec, "--world", "propsync",
        "--carry-script", "2.5,0.5,2.5,1.0,9.0") "carryA"
    $procs += $botA
    Start-Sleep -Milliseconds 300

    $bLog = Join-Path $script:LogDir "carryB.jsonl"
    $botB = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "CarryBotB",
        "--log", $bLog, "--duration", $ABDurationSec, "--world", "propsync",
        "--carry-script", "2.5,0.5,2.5,3.5,-1") "carryB"
    $procs += $botB
    Start-Sleep -Milliseconds 300

    $cLog = Join-Path $script:LogDir "carryC.jsonl"
    $botC = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "CarryBotC",
        "--log", $cLog, "--duration", $CDurationSec, "--world", "propsync",
        "--carry-script", "-2.5,0.5,-2.5,1.0,-1") "carryC"
    $procs += $botC

    # Late joiner: connect ~$LateJoinDelaySec after the server was confirmed listening, well
    # inside A's hold window, to prove the join-time dump converges it immediately.
    $elapsed = ((Get-Date) - $t0).TotalSeconds
    $remain = $LateJoinDelaySec - $elapsed
    if ($remain -gt 0) { Start-Sleep -Seconds $remain }

    $dLog = Join-Path $script:LogDir "carryD.jsonl"
    $botD = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "CarryBotD",
        "--log", $dLog, "--duration", $DDurationSec, "--world", "propsync") "carryD"
    $procs += $botD

    $bots = @(
        @{ Name = "CarryBotA"; Proc = $botA; JsonLog = $aLog }
        @{ Name = "CarryBotB"; Proc = $botB; JsonLog = $bLog }
        @{ Name = "CarryBotC"; Proc = $botC; JsonLog = $cLog }
        @{ Name = "CarryBotD"; Proc = $botD; JsonLog = $dLog }
    )

    $maxDuration = [math]::Max($ABDurationSec, $LateJoinDelaySec + $DDurationSec)
    $deadline = (Get-Date).AddSeconds($maxDuration + 80)
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

Write-Host "[3/3] verifying grab/hold/drop convergence..." -ForegroundColor Cyan

# Loads a bot's JSONL log and normalizes each sample's timestamp to seconds since that bot's own
# first sample (separate OS processes don't share a wall clock, so cross-bot comparisons only
# work in each bot's own local-elapsed terms - which is also the basis the scripted schedules
# were written in).
function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    $parsed = @($lines | ForEach-Object { $_ | ConvertFrom-Json })
    $t0 = [double]$parsed[0].t
    foreach ($s in $parsed) {
        $s | Add-Member -NotePropertyName LocalElapsedSec -NotePropertyValue (([double]$s.t - $t0) / 1000.0) -Force
    }
    return $parsed
}

function Get-Holder($Sample, [int]$PropId) {
    $p = @($Sample.props) | Where-Object { [int]$_.id -eq $PropId }
    if ($null -eq $p -or @($p).Count -eq 0) { return $null }
    return [int]($p | Select-Object -First 1).holder
}

# Walks $Samples once and reports when $PropId was first seen held by $HolderId, and first released
# (holder -> 0) after that. HeldAt/ReleasedAt are -1 when that transition never appears in this log —
# callers MUST treat that as a failure, never as "nothing to check" (see the anchoring note below).
function Get-HoldSpanBy($Samples, [int]$PropId, [int]$HolderId) {
    $heldAt = -1.0; $releasedAt = -1.0
    foreach ($s in $Samples) {
        $h = Get-Holder $s $PropId
        if ($null -eq $h) { continue }
        if ($heldAt -lt 0) {
            if ($h -eq $HolderId) { $heldAt = [double]$s.LocalElapsedSec }
        } elseif ($releasedAt -lt 0 -and $h -eq 0) {
            $releasedAt = [double]$s.LocalElapsedSec
        }
    }
    return @{ HeldAt = $heldAt; ReleasedAt = $releasedAt }
}

$samplesA = Get-Samples $aLog
$samplesB = Get-Samples $bLog
$samplesC = Get-Samples $cLog
$samplesD = Get-Samples $dLog

$peerA = [int]$samplesA[0].self
$peerB = [int]$samplesB[0].self
$peerC = [int]$samplesC[0].self
Write-Host "        peers: A=$peerA B=$peerB C=$peerC" -ForegroundColor DarkGray

$failures = New-Object System.Collections.Generic.List[string]

# --- event anchoring ---------------------------------------------------------------------------
# Every check below is anchored to an OBSERVED transition in the bot's own samples, never to an
# assumed wall clock.
#
# Why: the bots run a scripted wall-clock timeline (A grabs at ~1s, drops ~9s later), and under CPU
# contention — the 22-suite marathon runs these back-to-back — that timeline slips by seconds, while
# a constant like "the drop has landed by 13.0s" does not. When the slip outruns the constant's
# cushion the assertion fails on a prop that behaved perfectly. That was this suite's long-standing
# "load-margin flake": not environmental, just a guess about machine speed baked into a constant.
# Anchoring to the transition removes the guess and tightens the test besides — the hold is now
# checked over its ENTIRE observed span rather than a 3s sample of it.
#
# A missing anchor is a HARD FAILURE, never a skip. An anchor helper that quietly reported "nothing
# to check" would turn a real regression (the grab never converges) into a green run.

# --- 1, 2 & 3: grab converges on A, holds, then the drop converges ----------------------------
foreach ($set in @(@{ Name = "A"; Samples = $samplesA }, @{ Name = "B"; Samples = $samplesB })) {
    $span = Get-HoldSpanBy $set.Samples $Prop1Id $peerA
    if ($span.HeldAt -lt 0) {
        $failures.Add("bot $($set.Name): never observed prop $Prop1Id held by A ($peerA) - A's grab never converged on this peer")
        continue
    }
    if ($span.ReleasedAt -lt 0) {
        $failures.Add(("bot {0}: observed A grab prop {1} at {2:F2}s but never observed the drop before the log ended" -f `
            $set.Name, $Prop1Id, $span.HeldAt))
        continue
    }
    # Across the WHOLE hold, the holder must stay A - no flicker, no steal by B's contested grab.
    foreach ($s in $set.Samples) {
        if ($s.LocalElapsedSec -lt $span.HeldAt -or $s.LocalElapsedSec -ge $span.ReleasedAt) { continue }
        $h = Get-Holder $s $Prop1Id
        if ($h -ne $peerA) {
            $failures.Add(("bot {0} @ {1:F2}s: prop {2} holder={3}, expected A ({4}) during A's hold [{5:F2}..{6:F2}]s" -f `
                $set.Name, $s.LocalElapsedSec, $Prop1Id, $h, $peerA, $span.HeldAt, $span.ReleasedAt))
        }
    }
    # And from the drop onward it must stay loose.
    foreach ($s in $set.Samples) {
        if ($s.LocalElapsedSec -lt $span.ReleasedAt) { continue }
        $h = Get-Holder $s $Prop1Id
        if ($h -ne 0) {
            $failures.Add(("bot {0} @ {1:F2}s: prop {2} holder={3}, expected 0 (resting) after A's drop at {4:F2}s" -f `
                $set.Name, $s.LocalElapsedSec, $Prop1Id, $h, $span.ReleasedAt))
        }
    }
}
# B must never observe ITSELF as prop 1's holder (its grab must have been rejected outright).
# Scans every sample, so it needs no anchor at all.
$bEverHeldProp1 = @($samplesB | Where-Object { (Get-Holder $_ $Prop1Id) -eq $peerB }).Count -gt 0
if ($bEverHeldProp1) { $failures.Add("bot B was, at some point, prop $Prop1Id's holder - contested grab was NOT rejected") }

# --- 4: disconnect-release --------------------------------------------------------------------
# B's own view (B stays connected the whole test) of prop 2: held by C, then released to 0 once C
# disconnects while still holding it. Anchored to the hold B actually observed -- the old
# pre-disconnect window was a 1.5s slice at [3, 4.5]s, the narrowest guess in the suite and the
# first to lose the race when C's grab ran late.
$cSpan = Get-HoldSpanBy $samplesB $Prop2Id $peerC
if ($cSpan.HeldAt -lt 0) {
    $failures.Add("bot B: never observed prop $Prop2Id held by C ($peerC) - C's grab never converged, so the disconnect-release was never staged")
} elseif ($cSpan.ReleasedAt -lt 0) {
    $failures.Add(("bot B: observed C hold prop {0} from {1:F2}s but never observed it released - C's disconnect did NOT release its prop" -f `
        $Prop2Id, $cSpan.HeldAt))
} else {
    # Throughout C's hold, B must agree C is the holder.
    foreach ($s in $samplesB) {
        if ($s.LocalElapsedSec -lt $cSpan.HeldAt -or $s.LocalElapsedSec -ge $cSpan.ReleasedAt) { continue }
        $h = Get-Holder $s $Prop2Id
        if ($h -ne $peerC) {
            $failures.Add(("bot B @ {0:F2}s: prop {1} holder={2}, expected C ({3}) during C's hold [{4:F2}..{5:F2}]s" -f `
                $s.LocalElapsedSec, $Prop2Id, $h, $peerC, $cSpan.HeldAt, $cSpan.ReleasedAt))
        }
    }
    # And once C is gone, it must stay released - never re-attributed to a ghost peer.
    foreach ($s in $samplesB) {
        if ($s.LocalElapsedSec -lt $cSpan.ReleasedAt) { continue }
        $h = Get-Holder $s $Prop2Id
        if ($h -ne 0) {
            $failures.Add(("bot B @ {0:F2}s: prop {1} holder={2}, expected 0 (released) after C's disconnect at {3:F2}s" -f `
                $s.LocalElapsedSec, $Prop2Id, $h, $cSpan.ReleasedAt))
        }
    }
}

# --- 5: late-join convergence -------------------------------------------------------------------
# D's very first sample already reflects the late-join dump.
$dFirst = $samplesD[0]
$dFirstHolder = Get-Holder $dFirst $Prop1Id
if ($dFirstHolder -ne $peerA) {
    $failures.Add("bot D's first sample (t=$($dFirst.LocalElapsedSec)s): prop $Prop1Id holder=$dFirstHolder, expected A ($peerA) via late-join dump")
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "CARRY-TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "CARRY-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: grab converged, contested grab rejected, drop converged, disconnect released, late-join converged." -ForegroundColor Green
Write-Host ""
Write-Host "CARRY-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
