<#
.SYNOPSIS
    Proves the Host flow's client-local server spawn (LocalServerHost) headlessly:
    spawn -> readiness-by-stdout -> join -> reap, crash visibility, and orphan
    prevention on a hard-killed host.

.DESCRIPTION
    The retired matchmaking provisioner's guarantees, re-proven where they now live (the
    hosting client itself). Three phases, all over ENet (CI has no Steam; the spawn/
    readiness/reap machinery is transport-agnostic — the Steam leg of readiness parsing
    is the same code matching a different listening line):

      1. Spawn + join + clean reap - --host-selftest spawns a dedicated child via
         LocalServerHost, learns its address from the child's own listening line, joins
         it through the normal client path, plays briefly, exits; the child must be
         reaped on the clean exit, with NO crash line logged (voluntary stop).
      2. Crash visibility - kill the CHILD mid-session; the host must log
         "[host] server child exited" (the harness/log hook) — the player-facing path
         rides the normal "Disconnected from server" flow.
      3. Orphan prevention - hard-kill the HOST mid-session; the child must die with it
         (Windows job object, kill-on-job-close), zero orphaned processes.

    Exit 0 = PASS.
#>
[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# Guarded: the marathon runs every child with -SkipBuild and owns the one reset at its start.
# An unguarded reset here would delete EVERY earlier suite's logs mid-run (this suite is not first),
# destroying the evidence for any failure that already happened -- which is exactly why the
# Carry-family flake survived five marathons undiagnosed. See issue #4.
if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

function Wait-ForPidExit([int]$TargetPid, [int]$TimeoutSec) {
    for ($i = 0; $i -lt ($TimeoutSec * 4); $i++) {
        if (-not (Get-Process -Id $TargetPid -ErrorAction SilentlyContinue)) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

$procs = @()
try {
    # --- 1. Spawn + join + clean reap -------------------------------------------
    Write-Host "[1/3] host-selftest: spawn via LocalServerHost, join, clean reap..." -ForegroundColor Cyan
    $hostLog = Join-Path $script:LogDir "hostself.jsonl"
    $p = Start-Godot @("--host-selftest", "--name", "HostSelf", "--duration", "6",
        "--log", $hostLog, "--log-dir", $script:LogDir) "hostself"
    $procs += $p
    if (-not (Wait-ForExit $p 90)) { Write-Fail "host self-test did not exit in time" }
    if ($p.ExitCode -ne 0) { Write-Fail "host self-test exited $($p.ExitCode); see hostself.err.log" }

    $out = Get-Content (Join-Path $script:LogDir "hostself.out.log") -Raw
    $ready = [regex]::Match($out, "\[host-selftest\] child ready pid (\d+) at 127\.0\.0\.1:(\d+)")
    if (-not $ready.Success) { Write-Fail "no readiness line - LocalServerHost never parsed the child's listening line" }
    $childPid = [int]$ready.Groups[1].Value
    if ($out -notmatch "\[client\] connected as peer") { Write-Fail "host client never connected to its spawned server" }
    if (-not (Test-Path $hostLog)) { Write-Fail "host client produced no position log" }
    $lines = @(Get-Content $hostLog | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -lt 3) { Write-Fail "host log too short ($($lines.Count) samples) - join likely failed" }
    if (-not (Wait-ForPidExit $childPid 10)) {
        try { Stop-Process -Id $childPid -Force -ErrorAction SilentlyContinue } catch {}
        Write-Fail "child server (pid $childPid) was NOT reaped on clean exit"
    }
    if ($out -match "\[host\] server child exited") { Write-Fail "clean shutdown logged a crash line - voluntary stops must be silent" }
    # A voluntary stop must shut the child down GRACEFULLY (close its control channel, let it
    # run its own teardown) rather than force-kill it. Over Steam that graceful teardown is
    # what runs SteamGameServer.LogOff(); a hard kill skips it, orphaning the anonymous
    # game-server session at Steam's backend so an immediate re-host fails GameServer.Init
    # with CantCreate for ~1-2 min. Proven here over ENet at the mechanism level: the child
    # exits on its own before the grace deadline.
    if ($out -notmatch "shut down gracefully") {
        Write-Fail "clean reap force-killed the child instead of shutting it down gracefully - the Steam log-off would be skipped, orphaning the game-server session and blocking an immediate re-host"
    }
    Write-Host "      ok - spawned, joined, reaped GRACEFULLY (pid $childPid), no false crash report" -ForegroundColor DarkGreen

    # --- 2. Crash visibility ------------------------------------------------------
    Write-Host "[2/3] crash visibility: killing the child mid-session..." -ForegroundColor Cyan
    $p2 = Start-Godot @("--host-selftest", "--name", "HostCrash", "--duration", "60",
        "--log-dir", $script:LogDir) "hostcrash"
    $procs += $p2
    $out2Path = Join-Path $script:LogDir "hostcrash.out.log"
    if (-not (Wait-ForLogLine $out2Path "\[host-selftest\] child ready pid \d+" 60)) {
        Write-Fail "second host self-test never became ready"
    }
    $ready2 = [regex]::Match((Get-Content $out2Path -Raw), "\[host-selftest\] child ready pid (\d+)")
    $childPid2 = [int]$ready2.Groups[1].Value
    Stop-Process -Id $childPid2 -Force
    if (-not (Wait-ForLogLine $out2Path "\[host\] server child exited \(code" 15)) {
        Write-Fail "host never reported the child's crash"
    }
    Stop-Proc $p2
    Write-Host "      ok - child crash surfaced in the host's log" -ForegroundColor DarkGreen

    # --- 3. Orphan prevention on a hard-killed host --------------------------------
    Write-Host "[3/3] orphan prevention: hard-killing the host, the child must die..." -ForegroundColor Cyan
    $p3 = Start-Godot @("--host-selftest", "--name", "HostOrphan", "--duration", "60",
        "--log-dir", $script:LogDir) "hostorphan"
    $procs += $p3
    $out3Path = Join-Path $script:LogDir "hostorphan.out.log"
    if (-not (Wait-ForLogLine $out3Path "\[host-selftest\] child ready pid \d+" 60)) {
        Write-Fail "third host self-test never became ready"
    }
    $ready3 = [regex]::Match((Get-Content $out3Path -Raw), "\[host-selftest\] child ready pid (\d+)")
    $childPid3 = [int]$ready3.Groups[1].Value
    Stop-Process -Id $p3.Id -Force   # the hard kill: no _ExitTree, no Dispose — only the job object stands
    if (-not (Wait-ForPidExit $childPid3 20)) {
        try { Stop-Process -Id $childPid3 -Force -ErrorAction SilentlyContinue } catch {}
        Write-Fail "child server (pid $childPid3) survived its host's hard kill - job object failed, orphan left"
    }
    Write-Host "      ok - job object reaped the child (pid $childPid3), zero orphans" -ForegroundColor DarkGreen
} finally {
    Stop-Procs $procs
}

Write-Host ""
Write-Host "PASS: LocalServerHost spawn/readiness/join/reap proven, child crashes are visible, and a hard-killed host leaves zero orphans." -ForegroundColor Green
exit 0
