<#
.SYNOPSIS
    HEADED movement playground (MOVE-3d, 2026-08-26). Launch it and play it.

.DESCRIPTION
    Not a pass/fail suite in its default mode: it needs a real viewport and a real pair of hands.
    The question it exists for is a feel question — does the MOVE-3
    movement (the acceleration ramp, the gears, the skid, coyote time, the jump buffer and the
    variable-height jump) feel good to drive? — and no assertion answers that.

    The body is a real SandboxAvatar on the shipped AvatarMotor, driven by the same
    LocalInputIntentSource a session uses, through the same SandboxCamera. Nothing is simulated
    for the harness's benefit, and every number in the top-left readout is read back out of the
    live nodes each frame.

      WASD    move                     SHIFT   sprint
      mouse   look                     L-CTRL  walk
      SPACE   jump — HOLD it for height, tap it for a short hop
      TAB     teleport to the next course (they cycle)
      R       respawn where you are
      ESC     release the mouse (press it again to take it back)

    The readout's `verb` and `air` lines are MOVE-5's: which crouch verb the body is in (NORMAL,
    TUCK, SLIDE, DUCK WALK), the shared verb clock and the bound it is counting against, and the
    air jumps spent this flight against the variant they are being spent under. The chain jump has
    no readout and never will — spec §6.6, swept by ChainNoUiAuditTests — so what it is doing is
    read off the body's posture and nowhere else.

    The knob panel (MOVE-4d, extended by MOVE-5d and MOVE-5f) is up on the right when the scene
    opens: a slider for every row in MotorTuningKnobs.All, the test that pins each one named on
    its own row, and the coupled invariants evaluated live. It takes neither the camera nor the
    controls — drive the body with it open, which is the only way a jump can be tuned while you
    are looking at the slider.

    NO COUNT OF ANYTHING IS TYPED IN THIS BLOCK, and that is deliberate (MOVE-5i). This help said
    "FIFTY-THREE tuning constants", "nine groups" and "Two courses" for as long as it took two
    waves to add five rows, a group and a course, and every one of those numbers was wrong by the
    time MOVE-5h measured them in engine. The panel derives its own row and group counts from
    MotorTuningKnobs.All (see MovementPlayground's BuildTuningPanel), and this script derives the
    same three figures from the source at launch and prints them in the banner below. Trust the
    banner; there is nothing here to go stale.

    PGUP and PGDN are the way to reach the groups at the bottom of the panel — the row list is
    long enough that walking it on a plain arrow key is a long hold.

    Several rows ship at their exact no-ops and are the ones to turn up first: ChainBonusMps at
    0.00 (try 0.60), AirJumpMode at 0 (try 1, then 2 for the Kick), AnticipationMode at 0 and
    CameraDipStrengthM at 0.00. The discrete rows — the toggles, the mode switches and the wire
    widths — paint a tick per legal value on their tracks, so a three-stop slider reads as three
    stops.

      F1      show / hide the panel
      UP/DOWN select a knob            LEFT/RIGHT  nudge it one step (SHIFT for ten)
      PGUP/PGDN  jump a whole group    HOME        put this knob back to its shipped value
      F2      save user://movement-tuning.json (every edit already writes it)
      F4      reload that file         F7          reset everything to the shipped values
      F5      print the tuning as C# to the console, the CLIPBOARD and a timestamped archive
              under user://movement-tuning-prints/ — SHIFT+F5 prints every row in the table

    F7 does NOT touch the file, so F7 then F4 is a clean A/B between the shipped feel and yours.

    Nudge from the keyboard with the mouse still captured — that is the path that lets you tune a
    jump you are performing. Dragging a slider with the pointer needs ESC first, and ESC frees the
    mouse, which is what stops the body: that is the sandbox's own pre-existing behaviour and the
    panel does not change it.

    The courses sit 160 m apart on X and are reachable only through TAB, which cycles. Flow
    (MOVE-3f) is the playable half and is where you start; Calibration (MOVE-3e) is the measured
    half, ledge and gap banks that bracket the real jump so some are makeable and some deliberately
    are not; Scramble (MOVE-6) is the tall one, a pile you climb with one guaranteed route hidden
    in it. The live count is derived and printed in the banner below; TAB's destination is a
    function of MovementPlayground.BuildCourses's roster order, which is the only place that order
    is stated and is not something this help is allowed to guess at.

    -Capture drives a short SCRIPTED intent instead of a keyboard and writes in-engine viewport
    captures plus the readout's own numbers to a dated folder — a sprint, a held sprint jump, a
    jog tap jump, a teleport, a respawn, the knob panel, the landing-dip A/B, and last of all the
    scramble's arrival frame and a sprint at its apron rim. It is how a change to the harness gets
    checked without a human, and how the jump measurement gets held against a known figure:

        held sprint jump   apex 1.534 m   distance 7.200 m   airtime 0.833 s
        jog tap            apex 0.467 m   distance 1.710 m   airtime 0.317 s

    Wildly different numbers mean the harness's measurement is wrong, not the motor. Implies
    capture-then-exit.

    This block used to read 6.192 m / 0.717 s, and it was stale on its own branch — MOVE-4e caught
    it. MOVE-3b measured those before the Ridge Run flow course existed, and the scripted sprint
    jump launches from that course and lands on it, so the body now falls further than it rose.
    The apex is unchanged; every jog-tap figure is unchanged; 3 of 3 runs on this tree and 3 of 3
    on its base agree. 1.534 m / 0.717 s is still real for a fully-held STANDING jump on flat
    ground, which is a different jump from this one.

    Every beat from the teleport onward runs on the CALIBRATION course, and it is selected by
    identity — MovementCourse.IndexOfCourse(typeof(CalibrationCourse)) — not by "the next one
    along". It used to be the latter, and MOVE-6's third course silently moved the whole
    landing-dip A/B onto a jittered rubble pile, taking MOVE-4f's calibrated 17.20 m/s drop to
    17.69 m/s without failing anything (MOVE-5h found it, MOVE-5i fixed it). A fourth course
    cannot repeat that; MovementCourseSelectionTests adds one and proves it.

    -Headless is the marathon's mode (MOVE-1, 2026-09-04) and the reason this script is now in
    Run-AllTests.ps1's registry, where its help block used to say it never could be. It drives the
    same scripted intent as -Capture with --headless in front of it, so the readout's numbers -
    every course built, the jump arcs, the knob writer, the landing-dip A/B, the void-floor count -
    are measured and printed on every marathon, while the PNGs (which need a real rasterizer and
    come back "[CAPTURE FAILED]" under --headless) are simply not asked for. The marathon stays
    headless and zero-interaction, which is the promise in Run-AllTests.ps1's own header, and the
    headed -Capture path below is untouched and is still what an evidence run uses.

    ONE GPU on this machine: never launch this HEADED while another session is mid
    headed-verification. -Headless is exempt - it opens no window.
#>
[CmdletBinding()]
param(
    # Where -Capture writes. Empty = captures\movement-playground\<yyyy-MM-dd-HHmmss>.
    [string]$OutDir = "",
    [switch]$SkipBuild,
    [switch]$Capture,
    # MOVE-1: the scripted capture with --headless in front of it and no PNG assertion. What
    # Run-AllTests.ps1 passes. Implies -Capture; the two are not independent switches.
    [switch]$Headless,
    # Capture the engine's stdout. Worth passing with -Capture: Start-Process otherwise drops the
    # measured lines on the floor and the run has no evidence but its pictures.
    [string]$LogFile = ""
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# -------------------------------------------------------------------------------------------------
# Counted, never typed (MOVE-5i).
#
# This help block carried "FIFTY-THREE tuning constants", "nine groups" and "Two courses" through
# two waves that changed all three, and nothing caught it until a verification packet measured them
# in engine. A script cannot call MotorTuningKnobs.All the way MovementPlayground does, but it can
# read the same source files the panel is generated from, which is one step closer to the truth
# than a number in a comment and cannot drift from it silently.
#
# Deliberately tolerant: if the shape of either file changes, this reports "?" and the banner says
# so. A launcher must never refuse to launch the playground over a cosmetic count.
# -------------------------------------------------------------------------------------------------
function Get-PlaygroundInventory {
    param([string]$Root)

    $result = [ordered]@{ Rows = "?"; Groups = "?"; Courses = "?"; CourseNames = @() }

    $knobFile = Join-Path $Root "scripts\net\MotorTuningKnobs.cs"
    if (Test-Path $knobFile) {
        $knobText = Get-Content -Path $knobFile -Raw
        # One "Index = N," per MotorKnob declaration, and one Group per declaration too.
        $rows = ([regex]::Matches($knobText, '(?m)^\s*Index\s*=\s*\d+\s*,')).Count
        $groups = [regex]::Matches($knobText, 'Group\s*=\s*"([^"]+)"') |
            ForEach-Object { $_.Groups[1].Value } |
            Select-Object -Unique
        if ($rows -gt 0) { $result.Rows = $rows }
        if ($groups.Count -gt 0) { $result.Groups = $groups.Count }
    }

    # A course is a MovementCourse subclass in the playground folder; the abstract contract itself
    # is not one, and it is excluded by the base-class match rather than by name.
    $courseDir = Join-Path $Root "scripts\dev\playground"
    if (Test-Path $courseDir) {
        $names = @()
        foreach ($file in Get-ChildItem -Path $courseDir -Filter "*.cs") {
            $text = Get-Content -Path $file.FullName -Raw
            if ($text -match 'class\s+(\w+)\s*:\s*MovementCourse\b') {
                $names += $Matches[1]
            }
        }
        if ($names.Count -gt 0) {
            $result.Courses = $names.Count
            $result.CourseNames = $names
        }
    }

    return [pscustomobject]$result
}

if (-not $SkipBuild) { Invoke-BuildAndImport }

$inventory = Get-PlaygroundInventory -Root $script:Root

$sceneArgs = @("--path", $script:Root, "res://scenes/dev/MovementPlayground.tscn")
# -Headless is -Capture without a rasterizer, not a third mode: one scripted brain, one readout.
if ($Headless) { $Capture = $true; $sceneArgs = @("--headless") + $sceneArgs }

if ($Capture) {
    # Resolved against the repo root _Common found, not a $PSScriptRoot-relative literal: the
    # latter lands at the drive root on the first run (see Run-LoadoutCapture.ps1's note).
    if ([string]::IsNullOrEmpty($OutDir)) {
        $stamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
        $OutDir = Join-Path $script:Root "captures\movement-playground\$stamp"
    }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $OutDir = (Resolve-Path $OutDir).Path
    # Read by MovementPlayground._Ready; its presence is what selects the scripted intent over
    # the keyboard, so the scene has exactly one switch rather than a flag and an env var that
    # can disagree.
    $env:SAIL_MOVEPG_OUT = $OutDir
    # After the "--", always: a Godot CLI flag placed before it is silently swallowed and the
    # run looks exactly like a hang.
    $sceneArgs += @("--", "--movement-playground-quit")
    $mode = if ($Headless) { "SCRIPTED, HEADLESS - readout only" } else { "SCRIPTED CAPTURE" }
    Write-Host "=== movement playground ($mode) ===" -ForegroundColor White
    Write-Host "  writing captures to $OutDir" -ForegroundColor Gray
}
else {
    # Cleared rather than left standing: a stale value from an earlier -Capture in the same shell
    # would silently hand the keyboard run to the scripted brain.
    $env:SAIL_MOVEPG_OUT = ""
    Write-Host "=== movement playground (HEADED, PLAYABLE) ===" -ForegroundColor White
}

# Derived a moment ago from the same sources the panel and the harness are built out of. Printed in
# both modes, because the capture path exits before the keybind banner below and its log is where a
# verification packet goes looking for these numbers.
# Listed in FILE order, which is not the roster order MovementPlayground.BuildCourses adds them in;
# it is a census, not a TAB itinerary, and it does not claim to be one.
$courseList = if ($inventory.CourseNames.Count -gt 0) { ($inventory.CourseNames | Sort-Object) -join ", " } else { "unknown" }
Write-Host "  knob table: $($inventory.Rows) rows in $($inventory.Groups) groups (counted from scripts\net\MotorTuningKnobs.cs)" -ForegroundColor Gray
Write-Host "  courses:    $($inventory.Courses) - $courseList (counted from scripts\dev\playground\)" -ForegroundColor Gray
if ($inventory.Rows -eq "?" -or $inventory.Groups -eq "?" -or $inventory.Courses -eq "?") {
    Write-Host "  (a '?' means the source moved and this script's count did not follow - fix the count, do not type one)" -ForegroundColor Yellow
}

$startArgs = @{ FilePath = $script:GodotExe; ArgumentList = $sceneArgs; PassThru = $true }
if (-not [string]::IsNullOrEmpty($LogFile)) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogFile) | Out-Null
    $startArgs["RedirectStandardOutput"] = $LogFile
    Write-Host "  engine stdout -> $LogFile" -ForegroundColor Gray
}
$p = Start-Process @startArgs
$null = $p.Handle

if (-not $Capture) {
    Write-Host ""
    Write-Host "  WASD move   mouse look   SHIFT sprint   L-CTRL walk" -ForegroundColor Cyan
    Write-Host "  SPACE jump - HOLD for height, tap for a short hop" -ForegroundColor Cyan
    Write-Host "  TAB next course (cycles)   R respawn   ESC release the mouse" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  KNOB PANEL (right of screen, MOVE-4d + MOVE-5d + MOVE-5f) - $($inventory.Rows) rows in $($inventory.Groups) groups" -ForegroundColor Cyan
    Write-Host "  F1 show/hide   UP/DOWN select   LEFT/RIGHT nudge (SHIFT x10)   HOME reset knob" -ForegroundColor Cyan
    Write-Host "  PGUP/PGDN jump a whole group - the fastest way to the ones at the bottom" -ForegroundColor Cyan
    Write-Host "  F2 save   F4 reload   F5 print C# to the clipboard (SHIFT = every row)   F7 reset all" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Starts on the Flow course; TAB cycles through all $($inventory.Courses) of them." -ForegroundColor Gray
    Write-Host "  MOVE-5: hold SPACE through a landing to crouch. Slide if fast, tuck if slow." -ForegroundColor Gray
    Write-Host "  ChainBonusMps and AirJumpMode ship at their no-ops - move them to feel them." -ForegroundColor Gray
    Write-Host "  F7 leaves the saved file alone, so F7 then F4 is an A/B: shipped, then yours." -ForegroundColor Gray
    exit 0
}

$timeoutSec = 300
$exited = Wait-ForExit $p $timeoutSec
# CLEARED THE MOMENT THE RUN IS OVER, BEFORE ANY EXIT PATH (MOVE-1, and it cost a marathon).
# Run-AllTests.ps1 dot-sources and calls every suite in ONE PowerShell process, so an env var set
# here outlives this script. MovementPlayground._Ready checks SAIL_MOVEPG_OUT BEFORE it checks
# --bike-selftest and takes the capture branch when it is set - so a leaked value silently hands
# the next two bike suites the playground's capture harness instead of the self-test they asked
# for, and they fail looking for an OVERALL line the run never prints. A suite must leave the
# environment exactly as it found it.
$env:SAIL_MOVEPG_OUT = ""
if (-not $exited) {
    Stop-Proc $p
    Write-Fail "movement playground capture timed out after $timeoutSec s"
}

# Under --headless the viewport texture read-back is a dummy and every beat records
# "[CAPTURE FAILED]" in the readout, so PNGs are asked for in the headed mode only. The readout
# below is the measurement in both modes and is required in both.
$shots = @(Get-ChildItem -Path $OutDir -Filter "*.png" -ErrorAction SilentlyContinue)
if (-not $Headless -and $shots.Count -eq 0) { Write-Fail "movement playground produced no captures in $OutDir" }

$readout = Join-Path $OutDir "readout.txt"
if (-not (Test-Path $readout)) { Write-Fail "movement playground wrote no readout.txt in $OutDir" }

Write-Host ""
Get-Content $readout | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
Write-Host ""
if ($Headless) {
    Write-Host "PASS: readout.txt written to $OutDir (headless: no PNGs asked for)" -ForegroundColor Green
} else {
    Write-Host "PASS: $($shots.Count) captures and readout.txt written to $OutDir" -ForegroundColor Green
}
exit 0
