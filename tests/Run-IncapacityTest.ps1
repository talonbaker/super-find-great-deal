<#
.SYNOPSIS
    Headless test of the failure states (phase 1c) — control denial, the drag, and the
    anti-softlock hold in deep water.

.DESCRIPTION
    Launches the IncapacitationSelfTest scene headless. It drives a real CharacterBody3D
    through AvatarMotor.Step and MEASURES the result off the body rather than off the state
    machine's own opinion:

      - a knocked-out, frozen or momentarily-ragdolled camper pushing full stick, sprint AND
        jump travels zero metres — the ragdoll-you-can-still-steer guard, at the only layer
        that can actually enforce it
      - the motor obeys those fields without ever authoring them, so a reconciliation replay
        cannot lose the lock
      - a frozen body cannot self-propel, but DOES slide when the server injects the drag
        velocity — and slides well under walking pace
      - a frozen body over the bottomless deep is held at the surface where a teammate can
        reach it, instead of sinking out of the drag verb's reach

    Every denial check is paired with a positive control proving the harness can observe the
    thing being granted — a diagnostic that only ever reports "absent" proves nothing. Check 1
    is the control for checks 2-5; check 10 is the control for check 9.

    The scene prints one line per check and "INCAPACITY-TEST OVERALL: PASS" with exit 0 when
    everything is green. The arithmetic underneath (every state's entry/exit/behaviour, the
    dawn floor, impulse stacking, the atomic cascade and its negative control, the wire
    format) is proved far more thoroughly in the engine-free tier: dotnet test,
    IncapacitationMachineTests / IncapacityCascadeTests / IncapacityWireTests.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== failure states (phase 1c) ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$out = Join-Path $script:LogDir "incapacity.out.log"
$err = Join-Path $script:LogDir "incapacity.err.log"
$p = Start-Process -FilePath $script:GodotExe `
    -ArgumentList @("--headless", "--path", $script:Root, "res://tests/scenes/IncapacitationSelfTest.tscn") `
    -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
$null = $p.Handle

if (-not (Wait-ForExit $p 120)) {
    Stop-Proc $p
    Write-Fail "incapacity self-test timed out"
}

if (Test-Path $out) {
    Get-Content $out | Where-Object { $_ -match "^\[incapacity-selftest\] FAIL|^INCAPACITY-TEST" } | ForEach-Object {
        $color = if ($_ -match "FAIL") { "Red" } else { "Gray" }
        Write-Host "  $_" -ForegroundColor $color
    }
}

if ($p.ExitCode -ne 0) { Write-Fail "incapacity self-test reported failures (exit $($p.ExitCode)); see incapacity.out.log" }
if (-not (Select-String -Path $out -Pattern "INCAPACITY-TEST OVERALL: PASS" -Quiet)) {
    Write-Fail "incapacity self-test did not print OVERALL: PASS"
}

Write-Host ""
Write-Host "PASS: failure states green." -ForegroundColor Green
exit 0
