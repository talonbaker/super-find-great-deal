<#
.SYNOPSIS
    Proves avatar-roster-choice replication (P3, 2026-07-23): every peer renders the SAME
    creature for a given player, a late joiner sees existing players' choices immediately
    (not just eventually), and an invalid/spoofed roster key can never survive as anything
    but the clamped default.

.DESCRIPTION
    Mirrors Run-PropSyncTest.ps1's spawn-parity shape, applied to
    SandboxAvatar.AvatarKey instead of prop state:

      Stage 1 — AvatarBotA connects alone (SAIL_AVATAR=greybox_classic) and is given time to
                fully settle, so its AvatarKey has genuinely propagated to the server
                before anyone else joins.
      Stage 2 — AvatarBotB (SAIL_AVATAR=boxkid, the LATE JOINER) and AvatarBotC
                (SAIL_AVATAR=<invalid>, the hostile/stale-client stand-in) connect
                together.

    Assertions:
      1. Every bot exited cleanly.
      2. Late-join: AvatarBotB's FIRST sample that includes AvatarBotA as a peer already
         shows AvatarBotA's real key ("greybox_classic"), not the "greybox_primitive" default — proving
         the spawn=true property lands with the peer's CURRENT choice, not a stale one the
         late joiner would have to wait out (docs/superpowers/2026-07-23-wp-avatar-identity
         -dispatch.md item 1's late-join requirement).
      3. Validate-on-receipt: every bot's final view of AvatarBotC's key reads "greybox_primitive"
         (the clamp), never the invalid string it was launched with.
      4. Final full-roster cross-view parity: every bot agrees with every other bot on
         every player's AvatarKey.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [double]$DurationSec = 10,
    [int]$Port = 7815,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# id -> expected AvatarKey (C's launched key is deliberately invalid; NormalizeAvatarKey
# must clamp it to the default on every peer, including C's own self-view).
$expectedKey = @{
    "AvatarBotA" = "greybox_classic"
    "AvatarBotB" = "boxkid"
    "AvatarBotC" = "greybox_primitive"   # clamped from the bogus key it actually launches with
}

Write-Host "=== avatar-identity: roster-choice replication test ===" -ForegroundColor White
if (-not $SkipBuild) { Reset-LogDir; Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

$procs = @()
try {
    Write-Host "[1/4] launching dedicated server (open world) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "avatarid.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "open", "--log-dir", $script:LogDir) "avatarid.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) { Write-Fail "server never reported listening; see $serverOut" }
    Write-Host "        server up (pid $($server.Id))"

    Write-Host "[2/4] stage 1: AvatarBotA connects alone (SAIL_AVATAR=greybox_classic)..." -ForegroundColor Cyan
    $logA = Join-Path $script:LogDir "avatarbotA.jsonl"
    $env:SAIL_AVATAR = "greybox_classic"
    $botA = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "AvatarBotA",
        "--log", $logA, "--duration", $DurationSec, "--world", "open") "avatarbotA"
    $procs += $botA
    Remove-Item Env:\SAIL_AVATAR
    # Give A's own spawn (and its Sync-replicated AvatarKey) time to genuinely settle at
    # the server before anyone else connects — the whole point of stage 2 is that B joins
    # into a world where A's choice is ALREADY the server's live authoritative view, not a
    # race with A's own first tick. "peer joined" lands in matchserver.log (ServerLog), not
    # the stdout redirect — see Gameplay.OnPeerConnected.
    $serverLog = Join-Path $script:LogDir "matchserver.log"
    if (-not (Wait-ForLogLine $serverLog "peer joined" 15)) { Write-Fail "server never logged AvatarBotA joining" }
    Start-Sleep -Seconds 2

    Write-Host "[3/4] stage 2: AvatarBotB (late joiner, boxkid) + AvatarBotC (invalid key) connect..." -ForegroundColor Cyan
    $logB = Join-Path $script:LogDir "avatarbotB.jsonl"
    $env:SAIL_AVATAR = "boxkid"
    $botB = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "AvatarBotB",
        "--log", $logB, "--duration", $DurationSec, "--world", "open") "avatarbotB"
    $procs += $botB
    $logC = Join-Path $script:LogDir "avatarbotC.jsonl"
    $env:SAIL_AVATAR = "totally-bogus-not-a-roster-entry"
    $botC = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "AvatarBotC",
        "--log", $logC, "--duration", $DurationSec, "--world", "open") "avatarbotC"
    $procs += $botC
    Remove-Item Env:\SAIL_AVATAR

    $bots = @(
        @{ Proc = $botA; Name = "AvatarBotA"; JsonLog = $logA },
        @{ Proc = $botB; Name = "AvatarBotB"; JsonLog = $logB },
        @{ Proc = $botC; Name = "AvatarBotC"; JsonLog = $logC }
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

Write-Host "[4/4] verifying avatar-key replication..." -ForegroundColor Cyan

function Load-Lines([string]$Path, [string]$Tag) {
    if (-not (Test-Path $Path)) { Write-Fail "$Tag produced no log at $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -lt 2) { Write-Fail "$Tag log has only $($lines.Count) samples" }
    return $lines
}

$linesA = Load-Lines $logA "AvatarBotA"
$linesB = Load-Lines $logB "AvatarBotB"
$linesC = Load-Lines $logC "AvatarBotC"

$failures = New-Object System.Collections.Generic.List[string]

# --- 2. Late-join: AvatarBotB's FIRST sample containing peer A must already carry A's
#        real key, not the greybox_primitive default it would show if the spawn-time value hadn't
#        actually landed yet. ---
$firstAViaB = $null
foreach ($line in $linesB) {
    $sample = $line | ConvertFrom-Json
    $seenA = @($sample.peers | Where-Object { $_.name -eq "AvatarBotA" })
    if ($seenA.Count -gt 0) { $firstAViaB = $seenA[0]; break }
}
if ($null -eq $firstAViaB) {
    $failures.Add("AvatarBotB (late joiner) never saw AvatarBotA at all")
} elseif ($firstAViaB.avatarKey -ne "greybox_classic") {
    $failures.Add(("late-join FAILED: AvatarBotB's first sighting of AvatarBotA shows avatarKey='{0}', expected 'greybox_classic' " +
        "(spawn-replication did not deliver the current choice to the late joiner)" -f $firstAViaB.avatarKey))
}

# --- Build each bot's final full-roster (3-player) view, same pattern as
#     Run-MultiplayerTest.ps1 / Run-PropSyncTest.ps1: last sample with the complete roster. ---
function Final-View([string[]]$Lines, [string]$Tag) {
    for ($li = $Lines.Count - 1; $li -ge 0; $li--) {
        $sample = $Lines[$li] | ConvertFrom-Json
        if (@($sample.peers).Count -ge 3) {
            $m = @{}
            foreach ($p in $sample.peers) { $m[$p.name] = $p.avatarKey }
            return $m
        }
    }
    Write-Fail "$Tag never had a sample containing the full 3-player roster"
}
$viewA = Final-View $linesA "AvatarBotA"
$viewB = Final-View $linesB "AvatarBotB"
$viewC = Final-View $linesC "AvatarBotC"
$views = @{ AvatarBotA = $viewA; AvatarBotB = $viewB; AvatarBotC = $viewC }

# --- 3 + 2(final-state): every observer's final view matches the expected (post-clamp) key. ---
foreach ($observerName in $views.Keys) {
    $view = $views[$observerName]
    foreach ($subjectName in $expectedKey.Keys) {
        if (-not $view.ContainsKey($subjectName)) {
            $failures.Add("$observerName never saw $subjectName in its final roster")
            continue
        }
        $got = $view[$subjectName]
        $want = $expectedKey[$subjectName]
        if ($got -ne $want) {
            $failures.Add(("{0}'s final view of {1} shows avatarKey='{2}', expected '{3}'" -f $observerName, $subjectName, $got, $want))
        }
    }
}

# --- 4. Cross-view parity: every pair of observers agrees on every subject's key. ---
$names = @($expectedKey.Keys)
foreach ($subjectName in $names) {
    foreach ($oi in $names) {
        foreach ($oj in $names) {
            if ($oi -ge $oj) { continue }
            $a = $views[$oi][$subjectName]
            $b = $views[$oj][$subjectName]
            if ($a -ne $b) {
                $failures.Add("$oi and $oj disagree on ${subjectName}: '$a' vs '$b'")
            }
        }
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "Avatar-identity replication FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}

Write-Host "PASS: roster choice replicated identically to every peer, the late joiner saw existing choices immediately, and the invalid key clamped to 'greybox_primitive' everywhere." -ForegroundColor Green
exit 0
