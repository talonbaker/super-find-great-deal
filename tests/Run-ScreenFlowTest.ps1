#requires -Version 5.1
# CORE-PROG-B1: in-engine headless walk of the flow screens (connect gate, round intro,
# tally, upgrade lobby, loss, quota strip, nightfall treatment) against the scripted fake
# playthrough — per-step routing, late-join landing for every state, and the kit-reuse
# audit counted from the real node trees. The Godot-free decision tables live in dotnet
# test (FlowScreensTests/FlowCopyTests/UiKitStateTests/DeadNameAuditTests); this proves
# the screens CONSTRUCT and route under the engine.
param([switch]$SkipBuild)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}

# --world fullhud is NOT decoration -- same mechanism as Run-HudLayoutTest.ps1, see its note.
# FlowScreens.cs gates the quota strip on HudProfile.Current.QuotaStrip, which resolves from the
# autoloaded NetworkManager's Options.World; the flow arc this suite walks is the full-HUD one, and
# "bubbletest" wears the stripped BT-8 profile. The dependency used to ride on the "camp" default;
# LAUNCH-1 moved that default, so it is named here instead. Measured 2026-08-28: red without the
# flag, PASS with it.
$p = Start-Godot @("--screenflow-selftest", "--world", "fullhud") "screenflow.selftest"
if (-not (Wait-ForExit $p 120)) {
    Stop-Proc $p
    Write-Fail "screenflow self-test did not exit within 120s"
}
if ($p.ExitCode -ne 0) {
    Write-Fail "screenflow self-test failed (exit $($p.ExitCode)); see tests\logs\screenflow.selftest.*.log"
}
Write-Host "PASS: screenflow self-test" -ForegroundColor Green

# --- phase 2: host-flow failure routing (F1, 2026-08-30) ---------------------------------
# A LODGER, and it is worth saying why rather than letting a future reader assume drift.
# Run-HostFailureTest.ps1 is its own suite and belongs in Run-AllTests.ps1's table; that file
# was owned by another agent during the session packet MRF-A landed in, so it is carried here
# rather than left unregistered to rot. It is not off-charter either — this suite is the
# in-engine proof that the screens CONSTRUCT and ROUTE, and "which screen does a failed
# connect land on" is that same question one layer up. Give it its own Run-AllTests entry when
# that file frees up, and delete this block in the same change.
& (Join-Path $PSScriptRoot "Run-HostFailureTest.ps1") -SkipBuild
if ($LASTEXITCODE -ne 0) {
    Write-Fail "host-flow failure routing (F1) failed; see tests\logs\hostfail.out.log"
}
exit 0
