<#
.SYNOPSIS
    Headless self-test of the bike's HANDLING model in the movement playground
    (BIKE-2x, ported to Watis World by MOVE-1 on 2026-09-04).

.DESCRIPTION
    Launches scenes/dev/MovementPlayground.tscn with --bike-handling-selftest after the "--".
    The sibling of Run-BikeSelfTest.ps1: that one measures BIKE-0's state machine (mount,
    dismount, the slope tax, the hold ramp), this one measures what BIKE-2x layered on top -
    the lean, the speed-widened turn curve, the drift's entry gate and charge tiers, the bike
    camera's channels, and the telemetry the session writes. Every check prints
    "BIKE-HANDLING-SELFTEST <name>: PASS|FAIL - <numbers>"; exit 0 only on all green.

    It proves nothing about FEEL - that is Talon's, headed, via Run-MovementPlayground.ps1.

    Registered in Run-AllTests.ps1 as of MOVE-1. In Sail this suite had no runner at all and
    its sibling carried a header saying it was "deliberately NOT in Run-AllTests.ps1: a lab
    suite for a prototype that ships nowhere". That reason expired on 2026-09-04: the bike is
    this repo's core hook and Talon is feel-testing it weekly, so a bike regression has to turn
    the marathon red rather than wait for someone to remember a standalone command.
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

$out = Join-Path $script:LogDir "bike-handling-selftest.out.log"
$err = Join-Path $script:LogDir "bike-handling-selftest.err.log"
# The scene path goes BEFORE the "--" (it is the engine's), the flag AFTER it (it is ours).
$args = @("--headless", "--path", $script:Root, "res://scenes/dev/MovementPlayground.tscn", "--", "--bike-handling-selftest")

Write-Host "[1/1] running the bike handling self-test (headless, ~60 s)..." -ForegroundColor Cyan
$proc = Start-Process -FilePath $script:GodotExe -ArgumentList $args `
    -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
$null = $proc.Handle
if (-not (Wait-ForExit $proc 240)) { Stop-Proc $proc; Write-Fail "bike handling self-test did not exit in time; see bike-handling-selftest.out.log" }

if (Test-Path $out) { Select-String -Path $out -Pattern "^BIKE-HANDLING-SELFTEST" | ForEach-Object { Write-Host "  $($_.Line)" } }
if ($proc.ExitCode -ne 0) { Write-Fail "bike handling self-test exited $($proc.ExitCode); see bike-handling-selftest.out.log" }
if (-not (Select-String -Path $out -Pattern "BIKE-HANDLING-SELFTEST OVERALL: PASS" -Quiet)) {
    Write-Fail "bike handling self-test never printed OVERALL: PASS; see bike-handling-selftest.out.log"
}

Write-Host ""
Write-Host "PASS: bike handling self-test all green." -ForegroundColor Green
exit 0
