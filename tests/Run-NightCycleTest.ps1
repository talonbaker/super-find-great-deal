<#
.SYNOPSIS
    CycleBands (WP-N1) pure-logic proof: the single source of truth for the loop-v1 day's
    phase bands (day/dusk-sweep/night/dawn-sweep) and their escalation across a run (design
    §1: "night lengthens as the run progresses").

.DESCRIPTION
    --night-cycle-selftest: no live scene, no network, no display — see
    NightCycleSelfTest.cs. Covers:
      - Band boundaries are continuous and exhaustive for every day 1-5.
      - The wrap seam: phase 0.999 -> 0.000 has no discontinuity in band progress, and the
        day index advances exactly once.
      - Night width increases monotonically across days 1-5 and clamps at day 5 (day 9 is
        fed in and proven identical to day 5).
      - The dusk-sweep width is identical on all five days (the escapability contract from
        design §5 depends on this never drifting).
      - Dome radius (NightDome.EvaluateRadius) is continuous across every band boundary
        (including the wrap) and monotonically decreasing through the dusk sweep, for every
        day.
      - Degenerate inputs: period <= 0, negative elapsed, CyclesElapsed far past day 5.
      - The escapability contract, measured against SPRINT (2026-07-26 playtest fix): the
        hoodlab-shipped geometry's front speed sits in the 1.05-1.15x window against
        MoveSpeed * SprintMultiplier (4.86 m/s), AND a simulated player fleeing at that speed
        is overtaken from beyond the safe-return radius while making it home from inside it,
        on every day 1-5. The original packet checked the ratio against the base walk speed
        only, passed, and shipped a wall a running player outran — so the ratio alone is not
        the assertion; the simulated flight is.
      - Notes 2/3: the dome swallows its own centre. At every phase of every night band the
        house itself reads fully inside the dark and the wall mesh is fully dissolved — the
        16 m resting floor that left a permanently lit halo cannot come back silently.
      - Note 4: the lit side's enclosure curve is monotonic as the front closes, zero while
        the front is beyond the enclosure radius, and still negligible at 70% out (the wide
        golden vista at the top of the sweep is the read Talon asked to keep).

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

Write-Host "=== night-cycle: CycleBands escalation + dome-radius proof ===" -ForegroundColor White

Write-Host "[1/1] running night-cycle self-test..." -ForegroundColor Cyan
$proc = Start-Godot @("--night-cycle-selftest") "night-cycle-selftest"
if (-not (Wait-ForExit $proc 60)) { Stop-Proc $proc; Write-Fail "night-cycle self-test did not exit in time" }
if ($proc.ExitCode -ne 0) { Write-Fail "night-cycle self-test exited $($proc.ExitCode); see night-cycle-selftest.out.log" }
$out = Join-Path $script:LogDir "night-cycle-selftest.out.log"
if (-not (Select-String -Path $out -Pattern "\[night-cycle-selftest\] PASS" -Quiet)) {
    Write-Fail "night-cycle self-test never printed PASS; see night-cycle-selftest.out.log"
}
Write-Host "        self-test PASS" -ForegroundColor DarkGray

Write-Host ""
Write-Host "PASS: band exhaustiveness/continuity, wrap seam, night-width monotonic escalation + day-5 clamp, dusk-sweep-width invariance, dome-radius continuity + monotonicity, sprint escapability (ratio + simulated flight), centre engulfment, lit-side enclosure, degenerate inputs." -ForegroundColor Green
Write-Host ""
Write-Host "NIGHT-CYCLE TEST OVERALL: PASS" -ForegroundColor Green
exit 0
