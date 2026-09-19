<#
.SYNOPSIS
    BUBBLE-2's headed evidence: the same camera, twice, on the bubble that was inside the tangle
    spire's skirt. Written to docs/qa/BUBBLE-2/<Tag>/.

.DESCRIPTION
    Talon, off the 2026-09-04 playtest: "one single bubble is out of access to the player ... it
    is being clipped into one of the towers ... I could not reach the bubble therefore I could not
    successfully conclude that all the bubbles were reachable."

    Bubble_Tangle_10 (id 82) sat directly over stair tread 19 at +1.93 m, which is the pattern
    every other stair bubble in the section follows. W7-1's spire then dropped Tower/Jumble01 on
    top of it and the bubble ended up inside the block. This script frames that spot.

    TWO CAMERAS, TWO RUNS, and the pairs are the evidence -- the cameras do not move between
    runs, so the ONLY difference between a before frame and its after frame is the bubble:

      -Tag before   run with the OLD position restored by hand -- the frames show the skirt block
                    and no bubble anywhere in them, which is exactly what Talon saw.
      -Tag after    run on the shipped tree -- the same two frames with the bubble plainly in the
                    open on the stair line between treads 19 and 20.

    --capture-cam IS NOT OPTIONAL. A bot's own follow camera frames the BOT, which is 130 m away
    at the hub spawn, and a capture bot renders a flat grey world without it
    (.claude/rules/test-suite.md, CELEBRATE-1, 2026-09-04). docs/testing/TESTER-HANDOFF.md states
    it flatly: "--capture-cam is MANDATORY or you get a false pass".

    THE CAMERAS. "close" is world (75.9, 26.5, -60.5) looking at (75.8, 20.8, -65.95) -- 7.9 m
    out and 5.7 m up, straight down onto the pair of positions, with Tower/Jumble01 (the block
    that swallowed the first) between them. "wide" is (78.6, 25.4, -75.2) looking at
    (75.9, 21.0, -65.9), the spire's foot from the south-east, and it is there for the word Talon
    used: the magenta in that frame is the tangle's goal colour -- tangle_goal.tres, HSV(300),
    the only magenta in the level -- worn by the stair's last tread and the spire's spur pad.
    Noon and --cycle-freeze on both, so the runs are lit identically.

.PARAMETER Tag
    Prefix for the subdirectories under docs/qa/BUBBLE-2/ ("<Tag>-close", "<Tag>-wide"). Use
    "before" and "after"; neither camera changes between them.
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Tag = "after",
    [int]$Port = 45884,
    [string]$OutDir = "",
    [double]$DurationSec = 26,
    [double]$CaptureAtSec = 18,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\..\..\tests\_Common.ps1"

# NOT a param() default: PowerShell 5.1 does not reliably populate $PSScriptRoot while binding
# param defaults under `powershell -File`, and the failure is silent. Same workaround
# tools/dev/bubble1_capture.ps1 and every *Capture script in tests/ uses.
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) "docs\qa\BUBBLE-2"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

function Start-GodotWindowed([string[]]$UserArgs, [string]$LogTag) {
    $out = Join-Path $script:LogDir "$LogTag.out.log"
    $err = Join-Path $script:LogDir "$LogTag.err.log"
    # Engine flags before the `--`, game flags after. Wrong side is swallowed and looks exactly
    # like a hang (.claude/rules/test-suite.md).
    $a = @("--path", $script:Root, "--") + $UserArgs
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList $a `
        -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
    $null = $p.Handle
    return $p
}

Reset-LogDir
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" }

# close -- 7.9 m from the subject, looking down the stair line so the block that swallowed the
#          bubble and both positions are in one frame.
# wide  -- the spire's foot from the south-east, for context: the magenta goal chips (the last
#          stair tread and the spire's spur pad) are the "magenta area" of Talon's note.
$shots = @(
    @{ Name = "close"; Cam = "75.9,26.5,-60.5,75.8,20.8,-65.95" }
    @{ Name = "wide";  Cam = "78.6,25.4,-75.2,75.9,21.0,-65.9" }
)

foreach ($shot in $shots) {
$cam = $shot.Cam
$shotDir = Join-Path $OutDir "$Tag-$($shot.Name)"
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "=== BUBBLE-2 capture [$Tag/$($shot.Name)] cam $cam -> $shotDir ===" -ForegroundColor White
$procs = @()
try {
    $serverOut = Join-Path $script:LogDir "b2cap-server-$Tag-$($shot.Name).out.log"
    $server = Start-Godot @(
        "--server", "--port", $Port, "--world", "bubbletest",
        "--cycle-start-phase", "noon", "--cycle-freeze",
        "--log-dir", $script:LogDir) "b2cap-server-$Tag-$($shot.Name)"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Host "      server never listened" -ForegroundColor Yellow
    }
    else {
        # The bot walks nowhere. It exists so there is a client rendering the world at all; the
        # fixed camera is what frames the subject.
        $bot = Start-GodotWindowed @(
            "--bot", "--address", "127.0.0.1:$Port", "--name", "B2Cap",
            "--duration", $DurationSec, "--world", "bubbletest",
            "--cycle-start-phase", "noon",
            "--capture-dir", $shotDir, "--capture-at", $CaptureAtSec,
            "--capture-cam", $cam) "b2cap-bot-$Tag-$($shot.Name)"
        $procs += $bot
        if (-not (Wait-ForExit $bot ([int]$DurationSec + 90))) {
            Write-Host "      the capture bot never exited" -ForegroundColor Yellow
        }
    }
}
finally {
    foreach ($p in $procs) { if ($p -and -not $p.HasExited) { Stop-Proc $p } }
}

$pngs = Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue
if (-not $pngs) {
    Write-Host "      NO PNG -- see b2cap-bot-$Tag-$($shot.Name).out.log" -ForegroundColor Yellow
} else {
    foreach ($p in $pngs) { Write-Host ("        {0}  ({1:N0} bytes)" -f $p.Name, $p.Length) -ForegroundColor Gray }
}
}
