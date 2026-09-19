<#
.SYNOPSIS
    BT-0: the Bubble Test level's scene-level compliance test -- the layout contract, the bake
    check that proves no section builds geometry at runtime, the collider audit and its positive
    control, the sky, and a live off-map respawn.

.DESCRIPTION
    --bubbletest-selftest instantiates the real shipped scenes/game/world/bubbletest/BubbleTest.tscn
    (see BubbleTestSelfTest.cs's doc comment) and
    checks it headless.

    The load-bearing assertion is the BAKE CHECK. Talon's constraint on this level is that every
    part of it must be "physically authored and present in the Godot scene file", and that is not
    something a reviewer can see in a diff: a section that builds a rock in _Ready looks identical
    in the inspector to one that does not. So each section is counted twice -- once as PACKED STATE
    (SceneState, nothing instantiated, nothing run) and once live in the tree after _Ready -- and
    the two must be equal. Unequal means something was constructed at runtime.

    The COLLIDER AUDIT is an absence check ("no mesh is missing collision"), so it carries its own
    positive control: a throwaway scene with a mesh and no collider that the audit MUST reject.
    That control failing is printed as an expected result. If it ever stops failing, every green
    run before it was worthless.

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

Write-Host "=== bubble test: BT-0 scene self-test ===" -ForegroundColor White

Write-Host "[1/1] running bubble test world self-test..." -ForegroundColor Cyan
$proc = Start-Godot @("--bubbletest-selftest") "bubbletest-selftest"
if (-not (Wait-ForExit $proc 90)) { Stop-Proc $proc; Write-Fail "bubble test self-test did not exit in time" }
if ($proc.ExitCode -ne 0) { Write-Fail "bubble test self-test exited $($proc.ExitCode); see bubbletest-selftest.out.log" }
$out = Join-Path $script:LogDir "bubbletest-selftest.out.log"
if (-not (Select-String -Path $out -Pattern "\[bubbletest-selftest\] PASS" -Quiet)) {
    Write-Fail "bubble test self-test never printed PASS; see bubbletest-selftest.out.log"
}

# The control is the reason the collider audit means anything, so its absence is a failure in its
# own right -- a run where the audit silently found no meshes would otherwise print PASS.
if (-not (Select-String -Path $out -Pattern "collider-audit positive control FAILED AS EXPECTED" -Quiet)) {
    Write-Fail "the collider audit's positive control did not report its expected failure; the audit is unproven (see bubbletest-selftest.out.log)"
}

# The per-section packed-vs-live counts are the evidence BT-13 reads, so they are echoed here
# rather than left buried in a log nobody opens.
Write-Host "        packed vs live node counts:" -ForegroundColor DarkGray
Select-String -Path $out -Pattern "packed\[mesh=" | ForEach-Object {
    Write-Host "         $($_.Line.Trim())" -ForegroundColor DarkGray
}
Write-Host "        self-test PASS" -ForegroundColor DarkGray

Write-Host ""
Write-Host "PASS: registration, seam file has no geometry, bake compliance per section, collider audit + control, anchors, footprint containment, spawn ring + facing, atmosphere, respawn config, off-map boundary." -ForegroundColor Green
Write-Host ""
Write-Host "BUBBLE-TEST WORLD TEST OVERALL: PASS" -ForegroundColor Green
exit 0
