<#
.SYNOPSIS
    Headless determinism test of the shared movement step (AvatarMotor.Step) — the
    property server-authoritative prediction/reconciliation rests on.

.DESCRIPTION
    Launches the NetStepSelfTest scene headless. It runs a 600-tick scripted input tape
    over real collision geometry four ways (live per-frame sim, two batched replays, a
    midpoint restart) and asserts the trajectories agree within reconciliation epsilon,
    plus input-sanitation invariants (NaN containment, inflated-input clamping).
    The scene prints one line per check and "NETSTEP-TEST OVERALL: PASS" with exit 0
    when everything is green.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== netcode step determinism test ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$out = Join-Path $script:LogDir "netstep.out.log"
$err = Join-Path $script:LogDir "netstep.err.log"
$p = Start-Process -FilePath $script:GodotExe `
    -ArgumentList @("--headless", "--path", $script:Root, "res://tests/scenes/NetStepSelfTest.tscn") `
    -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
$null = $p.Handle

if (-not (Wait-ForExit $p 120)) {
    Stop-Proc $p
    Write-Fail "netstep self-test timed out"
}

if (Test-Path $out) {
    Get-Content $out | Where-Object { $_ -match "^NETSTEP-TEST" } | ForEach-Object {
        $color = if ($_ -match "FAIL") { "Red" } else { "Gray" }
        Write-Host "  $_" -ForegroundColor $color
    }
}

if ($p.ExitCode -ne 0) { Write-Fail "netstep self-test reported failures (exit $($p.ExitCode))" }
if (-not (Select-String -Path $out -Pattern "NETSTEP-TEST OVERALL: PASS" -Quiet)) {
    Write-Fail "netstep self-test did not print OVERALL: PASS"
}

Write-Host ""
Write-Host "PASS: movement step determinism suite green." -ForegroundColor Green
exit 0
