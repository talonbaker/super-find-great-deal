<#
.SYNOPSIS
    Headless test of the lake water contract (W2) — the depth-to-state machine, the cold
    clock, the sputter-out, and Soaked.

.DESCRIPTION
    Launches the WaterSelfTest scene headless. It builds a synthetic lake with real
    collision geometry and drives a real CharacterBody3D through AvatarMotor.Step, then
    MEASURES the result off the body rather than off the state machine's own opinion:

      - dry ground 40 m below the water plane is still dry (the lateral gate)
      - wading resolves on the shelf and moves at 0.55x; sprint is refused
      - the deep has NO FLOOR, so "the swimmer stopped falling" is unfakeable proof that
        the horizontal-only hold is wired
      - jump is refused while swimming; the go-under sinks the body
      - a control-locked camper pushing full stick travels zero metres
      - Soaked applies its 0.9x
      - the cold collects a swimmer, control returns exactly once, and the tape is ruined
        exactly once per episode
      - the late-join dump round-trips swim state and chill

    Every "X is denied" check is paired with a positive control proving the harness can
    observe X being granted — a diagnostic that only ever reports "absent" proves nothing.

    The scene prints one line per check and "WATER-TEST OVERALL: PASS" with exit 0 when
    everything is green. The arithmetic underneath (hysteresis, the cold curve, the
    anti-unwinnable sweep, codec fuzzing) is proved far more thoroughly in the engine-free
    tier: dotnet test, WaterGeometryTests / WaterChillTests / WaterSputterTests /
    WaterDumpTests / WaterConstantSourceTests.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== lake water contract (W2) ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$out = Join-Path $script:LogDir "water.out.log"
$err = Join-Path $script:LogDir "water.err.log"
$p = Start-Process -FilePath $script:GodotExe `
    -ArgumentList @("--headless", "--path", $script:Root, "res://tests/scenes/WaterSelfTest.tscn") `
    -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
$null = $p.Handle

if (-not (Wait-ForExit $p 120)) {
    Stop-Proc $p
    Write-Fail "water self-test timed out"
}

if (Test-Path $out) {
    Get-Content $out | Where-Object { $_ -match "^\[water-selftest\] FAIL|^WATER-TEST" } | ForEach-Object {
        $color = if ($_ -match "FAIL") { "Red" } else { "Gray" }
        Write-Host "  $_" -ForegroundColor $color
    }
}

if ($p.ExitCode -ne 0) { Write-Fail "water self-test reported failures (exit $($p.ExitCode)); see water.out.log" }
if (-not (Select-String -Path $out -Pattern "WATER-TEST OVERALL: PASS" -Quiet)) {
    Write-Fail "water self-test did not print OVERALL: PASS"
}

Write-Host ""
Write-Host "PASS: lake water contract green." -ForegroundColor Green
exit 0
