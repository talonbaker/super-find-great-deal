<#
.SYNOPSIS
    Runs the headless Steam-transport logic checks (no Steam client required).

.DESCRIPTION
    Launches the game with --steam-selftest: pure-logic assertions over the steam:<id64>
    address codec, --transport selection/fallback, App ID resolution order, and the
    identity-keyed connection rate limiter. This is the CI-safe half of the Steam
    transport's test split — real relay connectivity needs live Steam accounts and is a
    weekend manual protocol (docs/STEAM.md), never mocked here. Exit 0 = PASS.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# Guarded: the marathon runs every child with -SkipBuild and owns the one reset at its start.
# An unguarded reset here would delete EVERY earlier suite's logs mid-run (this suite is not first),
# destroying the evidence for any failure that already happened -- which is exactly why the
# Carry-family flake survived five marathons undiagnosed. See issue #4.
if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

Write-Host "[1/1] running steam-transport logic self-test..." -ForegroundColor Cyan
$proc = Start-Godot @("--steam-selftest") "steam-selftest"
if (-not (Wait-ForExit $proc 60)) { Stop-Proc $proc; Write-Fail "steam self-test did not exit in time" }
if ($proc.ExitCode -ne 0) { Write-Fail "steam self-test exited $($proc.ExitCode); see steam-selftest.out.log" }

$out = Join-Path $script:LogDir "steam-selftest.out.log"
if (-not (Select-String -Path $out -Pattern "\[steam-selftest\] PASS" -Quiet)) {
    Write-Fail "steam self-test never printed PASS; see steam-selftest.out.log"
}

Write-Host ""
Write-Host "PASS: steam-transport logic checks all green." -ForegroundColor Green
exit 0
