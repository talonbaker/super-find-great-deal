<#
.SYNOPSIS
    Headless self-test of the bike prototype in the movement playground (BIKE-0, 2026-09-01).

.DESCRIPTION
    Launches scenes/dev/MovementPlayground.tscn with --bike-selftest after the "--". A scripted
    brain drives the SAME BikeLayer a keyboard does, through the shipped AvatarMotor, on the
    Roller Run's own geometry, and measures the mechanics in the engine: the tuning blends in and
    restores, the ground mount hops, riding beats the foot cap, the rolling dismount clamps and
    stumbles (and hops instead with the switch off), the mid-air mount bursts once, the landing
    dismount bursts, the drift swings the nose and keeps speed, the slope pays only while mounted,
    the slide jump is taller, the hold ramp climbs. Every check prints
    "BIKE-SELFTEST <name>: PASS|FAIL - <numbers>"; exit 0 only on all green.

    It proves nothing about FEEL - that is Talon's, headed, via Run-MovementPlayground.ps1.

    REGISTERED in Run-AllTests.ps1 as of MOVE-1 (2026-09-04). This block used to read
    "Deliberately NOT in Run-AllTests.ps1: it is a lab suite for a prototype that ships nowhere."
    That reason is now false. The bike is Watis World's core hook and Talon feel-tests it live, so
    a bike regression has to turn the marathon red on the run that caused it rather than wait for
    someone to remember a standalone command. It still runs standalone; -SkipBuild is what the
    marathon passes.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

# Belt AND braces on the leak Run-MovementPlayground.ps1 also fixes: MovementPlayground
# checks SAIL_MOVEPG_OUT before it checks our flag and takes the capture branch when it is
# set. A suite must not depend on what ran before it in the same process.
$env:SAIL_MOVEPG_OUT = ""

$out = Join-Path $script:LogDir "bike-selftest.out.log"
$err = Join-Path $script:LogDir "bike-selftest.err.log"
# The scene path goes BEFORE the "--" (it is the engine's), the flag AFTER it (it is ours).
$args = @("--headless", "--path", $script:Root, "res://scenes/dev/MovementPlayground.tscn", "--", "--bike-selftest")

Write-Host "[1/1] running the bike self-test (headless, ~60 s)..." -ForegroundColor Cyan
$proc = Start-Process -FilePath $script:GodotExe -ArgumentList $args `
    -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
$null = $proc.Handle
if (-not (Wait-ForExit $proc 240)) { Stop-Proc $proc; Write-Fail "bike self-test did not exit in time; see bike-selftest.out.log" }

if (Test-Path $out) { Select-String -Path $out -Pattern "^BIKE-SELFTEST" | ForEach-Object { Write-Host "  $($_.Line)" } }
if ($proc.ExitCode -ne 0) { Write-Fail "bike self-test exited $($proc.ExitCode); see bike-selftest.out.log" }
if (-not (Select-String -Path $out -Pattern "BIKE-SELFTEST OVERALL: PASS" -Quiet)) {
    Write-Fail "bike self-test never printed OVERALL: PASS; see bike-selftest.out.log"
}

Write-Host ""
Write-Host "PASS: bike self-test all green." -ForegroundColor Green
exit 0
