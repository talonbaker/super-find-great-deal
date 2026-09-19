<#
.SYNOPSIS
    Headless gate for the interaction/feel system (scripts/game/sandbox/feel): targeting,
    grab, the carry spring, both release modes, slot claim/reject, re-grab out of a slot,
    the throw key, and spring stability at the extremes. No avatar, no camera rig, no
    server -- the scene builds its own harness.

.DESCRIPTION
    Launches the FeelSelfTest scene headless. The scene prints one machine-readable line,
    "FEEL-SELFTEST-SUMMARY total=N pass=N fail=N exit=N", and exits 0 when everything is
    green. Gate on that line, never on the engine's exit code alone.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== interaction / feel test ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}

$out = Join-Path $script:LogDir "feel.out.log"
$err = Join-Path $script:LogDir "feel.err.log"
$p = Start-Process -FilePath $script:GodotExe `
    -ArgumentList @("--headless", "--path", $script:Root, "res://tests/scenes/FeelSelfTest.tscn") `
    -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
$null = $p.Handle

if (-not (Wait-ForExit $p 120)) {
    Stop-Proc $p
    Write-Fail "feel self-test timed out"
}

if (Test-Path $err) {
    Get-Content $err | Where-Object { $_ -match "SELFTEST FAIL" } | ForEach-Object {
        Write-Host "  $_" -ForegroundColor Red
    }
}
if (Test-Path $out) {
    Get-Content $out | Where-Object { $_ -match "^FEEL-SELFTEST-SUMMARY|FAILED:" } | ForEach-Object {
        $color = if ($_ -match "fail=0") { "Gray" } else { "Red" }
        Write-Host "  $_" -ForegroundColor $color
    }
}

if ($p.ExitCode -ne 0) { Write-Fail "feel self-test reported failures (exit $($p.ExitCode))" }
if (-not (Select-String -Path $out -Pattern "FEEL-SELFTEST-SUMMARY .* fail=0 " -Quiet)) {
    Write-Fail "feel self-test did not print a fail=0 summary"
}

Write-Host ""
Write-Host "PASS: interaction / feel suite green." -ForegroundColor Green
exit 0
