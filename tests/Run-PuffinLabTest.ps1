<#
.SYNOPSIS
    EGG-1: the Puffin Lab throwback. One headless phase -- the EGG-2 seam, the absence check that
    nothing in the ported scene can cover or stop the screen, and the measured proof that Sail's
    own avatar fits down the whole route to the return television.

.DESCRIPTION
    --puffinlab-selftest (offline, one process). Instantiates the real shipped
    scenes/game/world/puffinlab/PuffinLab.tscn and asserts:

      - the seam EGG-2 reads: a Marker3D at "Arrival" and a TvPortal at "ReturnTv", both DIRECT
        children of the root, checked by node path and exact type. Sail's own
        MpFoundation.Game.World.TvPortal specifically, and exactly one of them: mp-foundation
        ships a TvPortal class of its own, and a second portal type in the world would be a
        television TvPortalHost's tree walk silently never subscribes to;

      - NO CanvasLayer, ColorRect, Control, WorldEnvironment or Camera3D, in the PACKED state of
        all three scene files OR in the live tree. Both halves matter and they catch different
        mistakes: packed catches an overlay authored into the data, live catches one a script
        adds in _Ready. Two of these really were in the source -- LabAuthored.tscn carried a
        full-screen vignette CanvasLayer and a WorldEnvironment, and a CanvasLayer instanced into
        a running level draws over the WHOLE game for the rest of the session -- so this is a
        regression guard on a defect that existed, not a formality;

      - THE ABSENCE CHECK CARRIES ITS OWN POSITIVE CONTROL. The same walker is pointed at a
        throwaway subtree holding one planted CanvasLayer and one planted ColorRect and is
        required to find both. An absence check looking in the wrong place passes everything
        forever, and every green run before the day someone notices is worthless;

      - the LIVE avatar's own capsule -- measured off the model the build actually loads, never a
        literal -- stands at all 19 stations of the route: the arrival, the lab, the torn wall,
        six stations of the descending crawl, the long hallway, THE TINY DOOR, the black room and
        the spot in front of the return TV. Floor is found by a downward ray at each station
        rather than trusted from the authored y, headroom by an upward one, and the body that is
        being stood on is excluded from the overlap query -- a vertical capsule on the crawl's
        24-degree slope always clips the surface it is resting on, and the first version of this
        check reported the tunnel blocked by its own floor;

      - THE CLEARANCE CHECK CARRIES ITS OWN POSITIVE CONTROL TOO: the same query is run inside
        the lab's floor slab and must come back solid. A shape query that is not reaching the
        world reports every station as free;

      - a real body parked in ReturnTv's real trigger raises BodyAtScreen. Geometry cannot prove
        this half: a trigger on the wrong collision mask, or sitting behind the cabinet hull,
        leaves a television that looks perfect and does nothing. WHERE it sends the player is
        EGG-2's wiring and is deliberately not asserted here.

    WHY THE FIT CHECK IS THE POINT. The source was authored around a 0.9 m avatar and its tiny
    door is a 1.05 m opening -- the only way into the room the return TV stands in. Ported 1:1,
    Sail's 1.20 m avatar does not pass it, and the symptom is a player wedged in a doorway 20 m
    underground with no way back. The port scales the two authored rooms instead of widening the
    door, so "puffin-sized tiny doors" stay tiny; this phase is what turns that from a claim into
    a measurement, and it re-measures against the live model every run, so the day the player
    model changes height this goes red instead of the level going quietly impassable.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
param([switch]$SkipBuild)

. "$PSScriptRoot/_Common.ps1"

Write-Host "=== EGG-1: the Puffin Lab throwback ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

Write-Host "[1/1] seam, overlay absence, route clearance and the way out (headless self-test)..." -ForegroundColor Cyan

$proc = Start-Godot @("--puffinlab-selftest") "puffinlab-selftest"
if (-not (Wait-ForExit $proc 90)) { Stop-Proc $proc; Write-Fail "the Puffin Lab self-test did not exit in time" }
$selfOut = Join-Path $script:LogDir "puffinlab-selftest.out.log"
if ($proc.ExitCode -ne 0) {
    Write-Fail "Puffin Lab self-test exited $($proc.ExitCode); see puffinlab-selftest.out.log"
}
if (-not (Select-String -Path $selfOut -Pattern "\[puffinlab-selftest\] PASS" -Quiet)) {
    Write-Fail "the Puffin Lab self-test never printed PASS; see puffinlab-selftest.out.log"
}

# The measured lines are the evidence, so they are echoed rather than left buried in a log.
Select-String -Path $selfOut -Pattern "^\[puffinlab-selftest\]  " | ForEach-Object {
    Write-Host "         $($_.Line.Trim())" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "PASS: the EGG-2 seam is wired (Arrival + one Sail TvPortal at ReturnTv); no CanvasLayer, ColorRect, Control, WorldEnvironment or Camera3D packed or live (control: the walker finds two planted ones); the live avatar's own capsule stands at all 19 route stations including the 1.05 m tiny door (control: the same query reports the floor slab solid); a body in ReturnTv's trigger raises BodyAtScreen." -ForegroundColor Green
Write-Host ""
Write-Host "PUFFIN LAB TEST OVERALL: PASS" -ForegroundColor Green
exit 0
