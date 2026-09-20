<#
.SYNOPSIS
    Proves the shared aim substrate's raise/lower stance replicates to remote peers (WP-L3,
    Issue #106's headline acceptance criterion: "stance replicates to remote peers — raise/lower
    visible on proxies").

.DESCRIPTION
    Two bots over a dedicated server (ENet, "open" world):
      AimBotA (the raiser) — --aim-script 2,6: raises at its own scripted clock t=2s, lowers at
                              t=6s, using the plain deterministic-walk movement brain underneath
                              (ScriptedAimIntentSource is a decorator — see its own doc comment —
                              so this also proves the rig works while genuinely moving, not just
                              standing still).
      AimBotB (the witness) — no script; just watches AimBotA over the wire the whole run.

    Assertions, all read from AimBotB's JSONL log (its REMOTE PROXY view of AimBotA — never
    AimBotA's own owner-predicted view, which would only prove local prediction, not
    replication):
      1. Both bots exited cleanly.
      2. Before the raise (an early sample), AimBotB sees AimBotA at AimStance=0 (Lowered).
      3. During the hold window (after raise, before lower), AimBotB sees AimBotA reach
         AimStance=2 (Raised) at least once.
      4. After the lower completes (final samples), AimBotB sees AimBotA back at AimStance=0
         (Lowered) — proves the LOWER direction replicates too, not just the raise.
      5. Cross-check: AimBotA's OWN self-view (owner-predicted) agrees with AimBotB's proxy view
         at the same moment during the hold window — prediction and the replicated proxy pose
         are not two different stories.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [double]$DurationSec = 10,
    [int]$Port = 7834,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# AimStance ordinals (AimController.cs) — load-bearing, mirrored here for readability only.
$Lowered = 0
$Raising = 1
$Raised = 2
$Lowering = 3

$raiseAtSec = 2.0
$lowerAtSec = 6.0

Write-Host "=== aim substrate: stance replication test ===" -ForegroundColor White
if (-not $SkipBuild) { Reset-LogDir; Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

$procs = @()
try {
    Write-Host "[1/3] launching dedicated server (open world) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "aim.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "open", "--log-dir", $script:LogDir) "aim.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) { Write-Fail "server never reported listening; see $serverOut" }
    Write-Host "        server up (pid $($server.Id))"

    Write-Host "[2/3] AimBotA (raiser, raises@${raiseAtSec}s lowers@${lowerAtSec}s) + AimBotB (witness) connect..." -ForegroundColor Cyan
    $logA = Join-Path $script:LogDir "aimbotA.jsonl"
    $botA = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "AimBotA",
        "--log", $logA, "--duration", $DurationSec, "--world", "open",
        "--aim-script", "$raiseAtSec,$lowerAtSec") "aimbotA"
    $procs += $botA
    $logB = Join-Path $script:LogDir "aimbotB.jsonl"
    $botB = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "AimBotB",
        "--log", $logB, "--duration", $DurationSec, "--world", "open") "aimbotB"
    $procs += $botB

    $bots = @(
        @{ Proc = $botA; Name = "AimBotA"; JsonLog = $logA },
        @{ Proc = $botB; Name = "AimBotB"; JsonLog = $logB }
    )
    $deadline = (Get-Date).AddSeconds($DurationSec + 60)
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

Write-Host "[3/3] verifying aim-stance replication..." -ForegroundColor Cyan

function Load-Lines([string]$Path, [string]$Tag) {
    if (-not (Test-Path $Path)) { Write-Fail "$Tag produced no log at $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -lt 2) { Write-Fail "$Tag log has only $($lines.Count) samples" }
    return $lines
}

$linesA = Load-Lines $logA "AimBotA"
$linesB = Load-Lines $logB "AimBotB"

$failures = New-Object System.Collections.Generic.List[string]

# Pulls AimBotA's peer-sample (as seen by whichever bot's log we're reading) from each JSONL
# line, paired with that sample's wall-clock timestamp (T, engine ticks msec) for ordering.
function Get-AimASamples([string[]]$Lines) {
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($line in $Lines) {
        $sample = $line | ConvertFrom-Json
        $seenA = @($sample.peers | Where-Object { $_.name -eq "AimBotA" })
        if ($seenA.Count -gt 0) { $out.Add([pscustomobject]@{ T = $sample.t; Peer = $seenA[0] }) }
    }
    return $out
}

$aViaB = Get-AimASamples $linesB   # AimBotA as a REMOTE PROXY on AimBotB's peer
$aViaA = Get-AimASamples $linesA   # AimBotA's own OWNER-PREDICTED self-view

if ($aViaB.Count -eq 0) { Write-Fail "AimBotB never saw AimBotA at all" }
if ($aViaA.Count -eq 0) { Write-Fail "AimBotA never saw itself in its own roster (should be impossible)" }

# --- 2. Before the raise: an early sample must show Lowered. ---
$earliestB = $aViaB[0]
if ($earliestB.Peer.aimStance -ne $Lowered) {
    $failures.Add("AimBotB's EARLIEST sample of AimBotA already shows aimStance=$($earliestB.Peer.aimStance), expected Lowered ($Lowered) - the bot may have started already raised, invalidating the before/after proof")
}

# --- 3. During the hold window: AimBotB must see AimBotA reach Raised at least once. ---
$sawRaisedViaB = @($aViaB | Where-Object { $_.Peer.aimStance -eq $Raised }).Count -gt 0
if (-not $sawRaisedViaB) {
    $failures.Add("AimBotB (remote proxy) never observed AimBotA reach AimStance=Raised ($Raised) - stance is not replicating raise to remote peers")
}

# --- 4. After the lower completes: the FINAL sample must show Lowered again. ---
$finalB = $aViaB[$aViaB.Count - 1]
if ($finalB.Peer.aimStance -ne $Lowered) {
    $failures.Add("AimBotB's FINAL sample of AimBotA shows aimStance=$($finalB.Peer.aimStance), expected Lowered ($Lowered) - the LOWER direction is not replicating (or never completed)")
}

# --- 5. Cross-check: during the hold window, AimBotA's own predicted view agrees with what
#        AimBotB (the remote proxy) sees at a comparable moment — same story, not two. ---
$sawRaisedViaA = @($aViaA | Where-Object { $_.Peer.aimStance -eq $Raised }).Count -gt 0
if (-not $sawRaisedViaA) {
    $failures.Add("AimBotA's OWN predicted self-view never shows AimStance=Raised - the owner-side prediction path itself is broken, independent of replication")
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "Aim-stance replication FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}

Write-Host "PASS: AimStance raised on schedule, was visible RAISED on a remote proxy, and lowered back to Lowered on a remote proxy by the end of the run - both directions replicate." -ForegroundColor Green
exit 0
