<#
.SYNOPSIS
    Headless test of the pure prop-state store (PropRegistry) — the single source of truth
    the server's networked-object authority rests on.

.DESCRIPTION
    Launches the PropRegistrySelfTest scene headless. It exercises the store's state-machine
    invariants with no scene tree, physics, or net stack: stable ascending ids, first-grab-wins
    holder arbitration, legal Held/Loose/Resting transitions, and defensive rejection of bad
    ids. The scene prints one line per check and "PROPREG-TEST OVERALL: PASS" with exit 0 when
    everything is green.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== net-objects: prop registry store test ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$out = Join-Path $script:LogDir "propreg.out.log"
$err = Join-Path $script:LogDir "propreg.err.log"
$p = Start-Process -FilePath $script:GodotExe `
    -ArgumentList @("--headless", "--path", $script:Root, "res://tests/scenes/PropRegistrySelfTest.tscn") `
    -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
$null = $p.Handle

if (-not (Wait-ForExit $p 120)) {
    Stop-Proc $p
    Write-Fail "prop registry self-test timed out"
}

if (Test-Path $out) {
    Get-Content $out | Where-Object { $_ -match "^PROPREG-TEST" } | ForEach-Object {
        $color = if ($_ -match "FAIL") { "Red" } else { "Gray" }
        Write-Host "  $_" -ForegroundColor $color
    }
}

if ($p.ExitCode -ne 0) { Write-Fail "prop registry self-test reported failures (exit $($p.ExitCode))" }
if (-not (Select-String -Path $out -Pattern "PROPREG-TEST OVERALL: PASS" -Quiet)) {
    Write-Fail "prop registry self-test did not print OVERALL: PASS"
}

Write-Host ""
Write-Host "PASS: prop registry store suite green." -ForegroundColor Green
exit 0
