<#
.SYNOPSIS
    Proves Practice Mode's spawn -> connect -> reap loop headlessly.

.DESCRIPTION
    Runs the client in the Practice self-test mode (--practice): it spawns the same child
    dedicated server the menu button spawns, auto-connects to it, plays briefly, then exits.
    Asserts the client actually joined the spawned server, and - critically - that the child
    server process is REAPED when the client exits (no orphaned dedicated server left behind).
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

$procs = @()
try {
    Write-Host "[1/3] launching Practice self-test..." -ForegroundColor Cyan
    $practiceLog = Join-Path $script:LogDir "practice.jsonl"
    $p = Start-Godot @("--practice", "--name", "PracticeSelf", "--duration", "6",
        "--log", $practiceLog, "--log-dir", $script:LogDir) "practice"
    $procs += $p
    if (-not (Wait-ForExit $p 60)) { Write-Fail "practice self-test did not exit in time" }
    if ($p.ExitCode -ne 0) { Write-Fail "practice self-test exited $($p.ExitCode); see practice.err.log" }

    Write-Host "[2/3] verifying it joined the spawned server..." -ForegroundColor Cyan
    $out = Get-Content (Join-Path $script:LogDir "practice.out.log") -Raw
    $spawn = [regex]::Match($out, "spawned dedicated server pid (\d+) on udp/(\d+)")
    if (-not $spawn.Success) { Write-Fail "no evidence a child server was spawned" }
    $childPid = [int]$spawn.Groups[1].Value
    $childPort = $spawn.Groups[2].Value
    if ($out -notmatch "\[client\] connected as peer") { Write-Fail "practice client never connected to the spawned server" }
    if (-not (Test-Path $practiceLog)) { Write-Fail "practice client produced no position log" }
    $lines = @(Get-Content $practiceLog | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -lt 3) { Write-Fail "practice log too short - join likely failed" }
    Write-Host "      joined child server (pid $childPid) on udp/$childPort" -ForegroundColor DarkGreen

    Write-Host "[3/3] verifying the child server was reaped..." -ForegroundColor Cyan
    $alive = $true
    for ($i = 0; $i -lt 20; $i++) {
        $proc = Get-Process -Id $childPid -ErrorAction SilentlyContinue
        if (-not $proc) { $alive = $false; break }
        Start-Sleep -Milliseconds 250
    }
    if ($alive) {
        try { Stop-Process -Id $childPid -Force -ErrorAction SilentlyContinue } catch {}
        Write-Fail "child dedicated server (pid $childPid) was NOT reaped - orphaned process left running"
    }
    Write-Host "      child server reaped cleanly" -ForegroundColor DarkGreen
} finally {
    Stop-Procs $procs
}

Write-Host ""
Write-Host "PASS: Practice Mode spawned a real dedicated server, the client joined it, and the server was reaped on exit." -ForegroundColor Green
exit 0
