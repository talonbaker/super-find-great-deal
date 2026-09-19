#requires -Version 5.1
# PLAY-1: in-engine headless measurement of the HUD's two shared columns and of the standing
# "UI never blocks an unlocked view" law, against the REAL node rectangles rather than the
# constants that were supposed to produce them.
#
#   1. the nightfall treatment, measured while the player still has camera and movement, must be
#      permitted by UiCoverageLaw - WITH a positive control (a full-screen wash run through the
#      identical measurement, which must come back not-permitted, or the check proves nothing);
#   2. day/phase readout, winter-cache strip and phase toast pairwise disjoint at the top centre,
#      in the cache's NOT-MET state and again in its MET state;
#   3. the room code clear of every rectangle the carry block draws;
#   4. UiColumns.LoadoutBlockHeight still matches what that block actually occupies.
#
# The Godot-free half (the law's predicate table, the column arithmetic) lives in dotnet test:
# tests/unit/HudLayoutTests.cs. This proves the widgets LAY OUT that way under the engine.
#
# EXPECTED NOISE, not a defect: three "ERROR: Not supported by this display server" stacks out of
# ControlGlyphs.GlyphFor -> DisplayServer.KeyboardGetKeycodeFromPhysical. These are client render
# surfaces being built under --headless on purpose; the call falls back and the layout is
# unaffected. The self-test's own verdict is the last line of the log.
param([switch]$SkipBuild)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}

# --world fullhud is NOT decoration. GameHud reads HudProfile.Current, which reads the autoloaded
# NetworkManager's Options.World, so WHICH readouts this self-test can measure is decided by the
# launch's world. The coverage law it asserts is the FULL HUD's, and only "fullhud" (any non-
# bubbletest id) wears HudProfile.Full -- "bubbletest" wears the stripped BT-8 profile, where two
# of the three upper-centre blocks are never built and the disjointness check passes for the wrong
# reason. That dependency used to ride on an old default world and was therefore invisible; LAUNCH-1
# moved the default to "bubbletest" and this suite went red with it. Naming the world here states
# the dependency instead of inheriting it. Measured 2026-08-28: without this flag the self-test
# prints "found 2 of the 3 upper-centre blocks" and exits 1; with it, PASS.
$p = Start-Godot @("--hudlayout-selftest", "--world", "fullhud") "hudlayout.selftest"
if (-not (Wait-ForExit $p 120)) {
    Stop-Proc $p
    Write-Fail "hud-layout self-test did not exit within 120s"
}
if ($p.ExitCode -ne 0) {
    Write-Fail "hud-layout self-test failed (exit $($p.ExitCode)); see tests\logs\hudlayout.selftest.*.log"
}
Write-Host "PASS: hud-layout self-test" -ForegroundColor Green
exit 0
