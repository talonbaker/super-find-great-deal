<#
.SYNOPSIS
    Headless test of the offline mechanics sandbox (mvp/sandbox slice): carry rules,
    stumble logic, voice-range publishing, and physics integration (move/jump/pickup/
    drop/throw). No server, matchmaking, or network peer involved.

.DESCRIPTION
    Launches the SandboxSelfTest scene headless; the scene prints one line per check
    and "SANDBOX-TEST OVERALL: PASS" plus exit code 0 when everything is green.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== sandbox mechanics test ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}

$out = Join-Path $script:LogDir "sandbox.out.log"
$err = Join-Path $script:LogDir "sandbox.err.log"
$p = Start-Process -FilePath $script:GodotExe `
    -ArgumentList @("--headless", "--path", $script:Root, "res://scenes/game/sandbox/SandboxSelfTest.tscn") `
    -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
$null = $p.Handle

if (-not (Wait-ForExit $p 90)) {
    Stop-Proc $p
    Write-Fail "sandbox self-test timed out"
}

if (Test-Path $out) {
    Get-Content $out | Where-Object { $_ -match "^SANDBOX-TEST" } | ForEach-Object {
        $color = if ($_ -match "FAIL") { "Red" } else { "Gray" }
        Write-Host "  $_" -ForegroundColor $color
    }
}

if ($p.ExitCode -ne 0) { Write-Fail "sandbox self-test reported failures (exit $($p.ExitCode))" }
if (-not (Select-String -Path $out -Pattern "SANDBOX-TEST OVERALL: PASS" -Quiet)) {
    Write-Fail "sandbox self-test did not print OVERALL: PASS"
}

Write-Host ""
Write-Host "PASS: sandbox mechanics suite green." -ForegroundColor Green
exit 0
