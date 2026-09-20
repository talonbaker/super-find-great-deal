<#
.SYNOPSIS
    BASE-1: the supermarket's compliance test. One headless boot that instantiates the real
    shipped scenes/game/world/supermarket/Supermarket.tscn and asserts the spawn contract, that
    nothing in any room is built in code, and that the rooms are far enough apart for the round
    to work.

.DESCRIPTION
    --supermarket-selftest (offline, one process, no server, no bots). It asserts:

      - EVERY NAMED SPAWN MARKER, and EXACTLY as many as the contract says. HoldingSpawn_0..3,
        SearchSpawn_0..1, TaskSpawn_0..3, Vestibule_0. Exactly, not at least: Gameplay.SpawnPlayer
        indexes the spawn array by join order and every peer builds its own copy of the world, so
        a duplicate marker desyncs where a player is just as surely as a missing one strands them.
        SpawnPoints itself must be the holding room's markers -- that is where every session
        starts and where every late joiner lands.

      - AUTHORED, NOT BUILT. Each room scene is walked as PACKED STATE (SceneState -- no
        instantiation, no _Ready, nothing runs) and its nodes counted, then the same scene is
        instantiated into a live tree and counted again. Equal counts mean nothing was constructed
        at runtime. This is the mechanical form of the standing rule (.claude/rules/godot-scenes.md,
        Talon 2026-08-27): every part of a level is authored in its scene file so it can be opened
        and flown around in the editor. A reviewer cannot see the difference in a diff -- a room
        that builds a shelf in _Ready looks identical in the inspector -- so it is measured.
        SHELF-1 is about to put a hundred props in the search room, and this is what keeps them
        authored.

      - ROOM SEPARATION. No two rooms' markers may come within 30 m of each other. Proximity voice
        cuts off at 24 m and VoiceProximityGate is ON by default for this world, so the separation
        is what makes "the hider cannot hear the seeker through the wall" a property of the
        geometry rather than of a gate somebody could switch off. Asserted against the MEASURED
        marker positions, not against the seam file's transforms, so moving a room in the editor
        and forgetting the consequence is a red rather than a quiet loss of the game.

    THIS SUITE GATES ON THE SUMMARY LINE, NOT ON THE EXIT CODE, and that is deliberate. The
    self-test always prints exactly one machine-readable line before it quits:

        [supermarket-selftest] SUMMARY failures=<n> result=PASS|FAIL

    A process that dies before it reaches that line -- a missing scene, a load error, a crash in
    someone else's _Ready -- exits non-zero for reasons that have nothing to do with the level,
    and a runner reading only the exit code cannot tell that apart from a real red. Reading the
    line separates "never reported" from "reported a failure", which are different problems with
    different fixes. The exit code is still checked, after the line.

    PROVED ABLE TO FAIL (BASE-1, 2026-09-19). HoldingSpawn_3 was renamed to HoldingSpawn_9 in
    HoldingRoom.tscn and this suite went red on exactly the right check --
    "room 'holding' has 3 HoldingSpawn_n marker(s); the contract says 4" -- and then green again
    when the name was put back. A test that has never once failed has not been shown to work.

    Exit 0 = PASS. No human interaction. Headless -- no GPU, no display, no network.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== BASE-1: the supermarket world ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

Write-Host "[1/1] spawn contract, authored-vs-live node counts, room separation (headless self-test)..." -ForegroundColor Cyan

$proc = Start-Godot @("--supermarket-selftest") "supermarket-selftest"
if (-not (Wait-ForExit $proc 90)) {
    Stop-Proc $proc
    Write-Fail "the supermarket self-test did not exit in time"
}

$selfOut = Join-Path $script:LogDir "supermarket-selftest.out.log"
if (-not (Test-Path $selfOut)) { Write-Fail "no supermarket-selftest.out.log was written at all" }

# THE LINE FIRST -- see the .DESCRIPTION for why it, and not the exit code, is the gate.
$summary = Select-String -Path $selfOut -Pattern "^\[supermarket-selftest\] SUMMARY " | Select-Object -Last 1
if (-not $summary) {
    Write-Fail ("the supermarket self-test never printed its SUMMARY line; it did not reach the " +
                "end of its own checks. See supermarket-selftest.out.log and .err.log")
}
Write-Host "         $($summary.Line.Trim())" -ForegroundColor DarkGray

if ($summary.Line -notmatch "result=PASS") {
    Select-String -Path $selfOut -Pattern "^\[supermarket-selftest\] FAIL " |
        ForEach-Object { Write-Host "         $($_.Line.Trim())" -ForegroundColor Red }
    $errLog = Join-Path $script:LogDir "supermarket-selftest.err.log"
    if (Test-Path $errLog) {
        Select-String -Path $errLog -Pattern "supermarket-selftest" |
            ForEach-Object { Write-Host "         $($_.Line.Trim())" -ForegroundColor Red }
    }
    Write-Fail "the supermarket self-test reported failures; see the lines above"
}

# The exit code is checked AFTER the line, so a teardown artifact is reported as what it is
# rather than as a level defect (.claude/rules/test-suite.md on reading a suite's red).
if ($proc.ExitCode -ne 0) {
    Write-Fail ("the supermarket self-test reported result=PASS but exited $($proc.ExitCode) -- " +
                "that is a teardown artifact, not a failing check. See supermarket-selftest.err.log")
}

# The measured lines are the evidence, so they are echoed rather than left buried in a log.
Select-String -Path $selfOut -Pattern "^\[supermarket-selftest\]   " |
    ForEach-Object { Write-Host "         $($_.Line.Trim())" -ForegroundColor DarkGray }

Write-Host ""
Write-Host "PASS: every named spawn marker is present in the contracted count; SpawnPoints is the holding room's; every room's live node count equals its packed count (nothing is built in code); no two rooms come within 30 m of each other." -ForegroundColor Green
Write-Host ""
Write-Host "SUPERMARKET WORLD TEST OVERALL: PASS" -ForegroundColor Green
exit 0
