<#
.SYNOPSIS
    FP-1: the first-person rig, measured and photographed in a real session.

.DESCRIPTION
    One headless dedicated server plus ONE WINDOWED client that renders through the real
    FirstPersonCamera (--first-person-selftest, which implies --first-person-cam) and saves its
    own viewport to a PNG at a fixed elapsed mark.

    THIS IS THE ONE SUITE IN THE REGISTRY THAT NEEDS A RENDERER. Every other Run-*.ps1 launches
    --headless. It cannot: the two things under test are what a camera renders and what a camera
    culls, and .claude/rules/test-suite.md's own entry on --capture-cam is the precedent -- "for a
    global shader parameter the RENDER is the only witness" is the same law one system over. If
    the client never prints its SUMMARY line, a missing display/GPU is the first thing to check.

    WHAT IS ASSERTED, and each one says why it is not vacuous:

      1. THE ACTIVE CAMERA IS THE FIRST-PERSON LENS. Not "a FirstPersonCamera was constructed" --
         the viewport's own current camera. A camera that is built and never made current is
         exactly what a flat grey capture is made of (the --capture-cam entry in the rules file).

      2. THE LENS SITS ON THE CHARACTER'S MEASURED EYELINE, within a centimetre of
         AvatarProportions.EyeHeightM above the body's own origin -- not at a typed height, and
         not at the third-person rig's focus height.

      3. THE OWN BODY WOULD BE IN FRAME, AND IS CULLED ANYWAY. The positive control first: the
         probe pitches the look fully down and confirms this avatar's own meshes really do fall
         inside the frustum from there. Only then does it assert that the lens drops
         AvatarVisual.FirstPersonHiddenLayer and that every one of those meshes is on that layer
         and no other. Without the first half, a cull mask that excluded a body which was never in
         shot would pass this suite forever while defending nothing -- CELEBRATE-1's "a latch that
         is protected by something else is not being tested", one subsystem over.

      4. THE PLAYER IS NOT SHOWN THEIR OWN NAME. Checked as the label's own visibility, not as the
         suppression flag, so a change that reaches the label by another route is still caught.

      5. THE LENS DEFAULTS ARE THE PACKET'S: fov 75 deg, near 0.05 m.

      6. A CAPTURE LANDED, and is a real frame rather than a zero-byte stub.

    GATED ON THE SUMMARY LINE, NOT ON THE EXIT CODE, then on the exit code afterwards. Same
    reasoning Run-SupermarketWorldTest.ps1's header spells out: a process that dies before it
    reports exits non-zero for reasons that have nothing to do with the thing under test, and
    "never reported" is a different problem from "reported a failure".

        [fp-selftest] SUMMARY failures=<n> result=PASS|FAIL

    THE CURSOR IS DELIBERATELY NOT CAPTURED by this run (--first-person-cam passes
    captureMouse:false). A suite that seizes the mouse of whoever is at the keyboard is a suite
    nobody runs twice.

    PROVED ABLE TO FAIL (FP-1, 2026-09-19) -- see the handoff for the two plants and what each
    one printed. A test that has never once failed has not been shown to work.

    Exit 0 = PASS. No human interaction; a window opens and closes on its own.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [int]$Port = 7896,
    [double]$DurationSec = 12,
    # Two marks, both after the walk below has finished. Comma-separated, which is --capture-at's
    # bare-mark form (the other form leads with a directory; see LaunchOptions).
    [string]$CaptureAtSec = "8,10",
    [string]$CaptureDir = ""
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== FP-1: the first-person rig ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

if ([string]::IsNullOrWhiteSpace($CaptureDir)) {
    $CaptureDir = Join-Path $script:LogDir "fp-capture"
}
if (Test-Path $CaptureDir) { Remove-Item -Recurse -Force $CaptureDir }
New-Item -ItemType Directory -Path $CaptureDir -Force | Out-Null

$procs = @()
try {
    Write-Host "[1/3] launching dedicated server on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "fp-server.out.log"
    $server = Start-Process -FilePath $script:GodotExe `
        -ArgumentList @("--headless", "--path", $script:Root, "--", "--server", "--port", $Port) `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "fp-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle   # cache the handle so ExitCode is readable after exit (PS 5.1 quirk)
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "the server never reported listening within 30s; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    # A WINDOWED client -- no --headless. Engine flags before the bare --, game flags after; a
    # game flag on the wrong side is swallowed and looks exactly like a hang.
    Write-Host "[2/3] launching a windowed first-person client for ${DurationSec}s..." -ForegroundColor Cyan
    $clientOut = Join-Path $script:LogDir "fp-client.out.log"
    $client = Start-Process -FilePath $script:GodotExe `
        -ArgumentList @("--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", "FpProbe",
            "--windowed", "--first-person-selftest",
            # WALK TO THE MIDDLE OF THE ROOM AND STAND THERE, so the capture is a photograph of a
            # room rather than of a wall. The deterministic walk brain heads radially OUTWARD from
            # the world origin, which in a 10 x 10 m room means it spends the run with its face
            # 30 cm from a corner -- a frame that is honestly rendered and says nothing. The
            # holding room is centred on the origin, so 0,0 is "the middle of where you spawned"
            # rather than a coordinate anyone typed off a level.
            "--goto-script", "0,0",
            "--capture-dir", $CaptureDir, "--capture-at", $CaptureAtSec,
            "--duration", $DurationSec) `
        -RedirectStandardOutput $clientOut `
        -RedirectStandardError (Join-Path $script:LogDir "fp-client.err.log") `
        -PassThru
    $null = $client.Handle
    $procs += $client

    if (-not (Wait-ForExit $client ([int]($DurationSec + 60)))) {
        Stop-Proc $client
        Write-Fail "the first-person client did not exit within its duration + 60s; see $clientOut"
    }
} finally {
    Stop-Procs $procs
}

Write-Host "[3/3] reading the probe's own report..." -ForegroundColor Cyan

if (-not (Test-Path $clientOut)) { Write-Fail "no fp-client.out.log was written at all" }

# The camera line first: it is the packet's "(a) the active camera is the FirstPersonCamera (log
# line)" and it also names the eye height and the cull layer for a reader of the log.
$camLine = Select-String -Path $clientOut -Pattern "^\[fp\] first-person camera active" | Select-Object -Last 1
if (-not $camLine) {
    Write-Fail ("the client never printed its [fp] camera line -- the first-person rig was never " +
                "attached. See fp-client.out.log and .err.log")
}
Write-Host "         $($camLine.Line.Trim())" -ForegroundColor DarkGray

# THE SUMMARY LINE, and not the exit code -- see the .DESCRIPTION for why.
$summary = Select-String -Path $clientOut -Pattern "^\[fp-selftest\] SUMMARY " | Select-Object -Last 1
if (-not $summary) {
    Write-Fail ("the first-person probe never printed its SUMMARY line; it did not reach the end " +
                "of its own checks. A missing display or GPU is the first suspect -- this is the " +
                "one suite that is not headless. See fp-client.out.log and .err.log")
}
Write-Host "         $($summary.Line.Trim())" -ForegroundColor DarkGray

if ($summary.Line -notmatch "result=PASS") {
    Select-String -Path $clientOut -Pattern "^\[fp-selftest\] FAIL " |
        ForEach-Object { Write-Host "         $($_.Line.Trim())" -ForegroundColor Red }
    Write-Fail "the first-person probe reported failures; see the lines above"
}

if ($client.ExitCode -ne 0) {
    Write-Fail ("the probe reported result=PASS but the client exited $($client.ExitCode) -- that " +
                "is a teardown artifact, not a failing check (.claude/rules/test-suite.md). See " +
                "fp-client.err.log")
}

# The capture: the packet's "(b) a capture shows the room from eye height with no own-body mesh in
# frame". The probe above is what PROVES the framing and the cull; this half proves a real frame
# reached disk for a human to look at, because an assertion about a render that nobody can see is
# an assertion about a number.
$shots = @(Get-ChildItem -Path $CaptureDir -Filter "*.png" -ErrorAction SilentlyContinue)
if ($shots.Count -lt 1) {
    Write-Fail "no capture landed in $CaptureDir -- --capture-dir/--capture-at wrote nothing"
}
foreach ($shot in $shots) {
    # A viewport PNG of a real 3D frame is tens of kilobytes; a stub or a truncated write is not.
    if ($shot.Length -lt 4096) {
        Write-Fail "capture $($shot.Name) is only $($shot.Length) bytes -- that is not a rendered frame"
    }
    Write-Host ("         capture {0} ({1:N0} bytes)" -f $shot.Name, $shot.Length) -ForegroundColor DarkGray
}

Select-String -Path $clientOut -Pattern "^\[fp-selftest\]   " |
    ForEach-Object { Write-Host "         $($_.Line.Trim())" -ForegroundColor DarkGray }

Write-Host ""
Write-Host ("PASS: the viewport renders through the FirstPersonCamera, the lens sits on the " +
            "character's measured eyeline at fov 75 / near 0.05, this avatar's own meshes fall " +
            "inside the frustum and every one of them is on the layer this lens does not render, " +
            "the local nameplate is not drawn, and $($shots.Count) capture(s) reached disk.") -ForegroundColor Green
Write-Host ""
Write-Host "FIRST PERSON TEST OVERALL: PASS" -ForegroundColor Green
exit 0
