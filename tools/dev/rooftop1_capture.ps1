<#
.SYNOPSIS
    ROOFTOP-1's headed evidence: seven shots of the seventh television, the two roofs, the sky
    deck, the diving board, the hundredth bubble and the fall, written to docs/qa/ROOFTOP-1/. Not a pass/fail
    suite -- it produces frames a human looks at, and it is deliberately not registered in
    Run-AllTests.ps1 for that reason.

.DESCRIPTION
    --capture-cam ON EVERY SHOT, and it is not optional. A --bot client builds no camera of its
    own, so --capture-dir without --capture-cam writes a correct HUD over the engine's flat grey
    clear colour, with nothing in the log to say the world was never drawn
    (docs/testing/TESTER-HANDOFF.md: "--capture-cam is MANDATORY or you get a false pass").

    NOON AND FROZEN, and for this feature that is a finding rather than a habit. BubbleTestWorld
    binds the fog to a 28 m sight range at night (SightPresentation.FogDensityWithSight), which is
    0.107 m^-1 -- about 5e-10 of transmittance at 200 m. From the sky deck at any night phase the
    level below is not hazy, it is absent. The vantage is a daylight reward and these captures say
    so by being taken in daylight; see BubbleTestLayout.SkyDeckY.

    THE SHOTS.

      two-roofs   -- STANDING ON THE LANDING ROOF, looking east at the far one. This is the
                     shot the whole re-siting turns on: two surfaces 78.64 m apart in plan with
                     the far one 5.80 m lower, the tunnel between them, and the seventh
                     television's lit screen on the far slab. Talon, 2026-09-05: "That's the wrong
                     roof. It's the one farther back. It's the one out more." This frame is what
                     "farther back and out more" looks like.
      tunnel      -- along the escape hall's ceiling (Route/HallCeil, the "little tunnel") toward
                     the far roof, holding the 3.864 m climb Talon's cube stack has to make. The
                     step is the subject; the numbers for it are in the report and in
                     BubbleTestLayout.LabRoofClimbM.
      roof-tv     -- the seventh television on the far roof, close, from the north-west. This is
                     the answer to "what's on the top of the puffling lab roof? there's no TV on
                     here?"
      deck        -- the sky deck from outside and slightly above: the 14 m platform, the board
                     off its north edge, and the way out behind the arrival point. 200 m up with
                     nothing under it.
      board       -- the diving board and the hundredth bubble on the end of it, from deck height.
                     The joke has to read as a diving board or it is just a plank.
      map         -- what a player standing on the deck actually sees when they look down. This is
                     the shot the height was chosen for, and it is the one that can fail: a
                     vantage that looks into grey haze is not the reward Talon described.
      fall        -- the frame from 100 m, half way down the board tip's fall line, looking at
                     where it lands. The fixed capture camera cannot follow a body, so this is
                     what the fall SHOWS rather than a picture of somebody falling: the whole map
                     coming up at you.

    A missing PNG is reported and does not stop the remaining shots: this is evidence gathering,
    not a gate.

.PARAMETER SkipBuild
    Reuse the existing build and import.
#>
[CmdletBinding()]
param(
    [int]$Port = 45884,
    [string]$OutDir = "",
    [double]$DurationSec = 26,
    [double]$CaptureAtSec = 18,
    [switch]$SkipBuild,
    [string[]]$Only = @()
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\..\..\tests\_Common.ps1"

# NOT a param() default: PowerShell 5.1 does not reliably populate $PSScriptRoot while binding
# param defaults under `powershell -File`, and the failure is silent. Same workaround every
# *Capture script in tests/ uses.
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) "docs\qa\ROOFTOP-1"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

function Start-GodotWindowed([string[]]$UserArgs, [string]$Tag) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $err = Join-Path $script:LogDir "$Tag.err.log"
    # Engine flags before the `--`, game flags after. Wrong side is swallowed and looks exactly
    # like a hang (.claude/rules/test-suite.md).
    $a = @("--path", $script:Root, "--resolution", "1920x1080", "--") + $UserArgs
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList $a `
        -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
    $null = $p.Handle
    return $p
}

Reset-LogDir
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" }

# x,y,z, tx,ty,tz -- all WORLD space.
#   the LANDING roof  Lab/HubAccess_Off/Ceiling  top y =  -6.410  x  78.55..101.45  z 81.31..98.69
#   "the tunnel"      Route/HallCeil             top y = -16.070  x 115.81..171.83  z 69.58..74.54
#   the FAR roof      Route/VoidCeil             top y = -12.206  x 162.04..173.90  z 74.13..85.45
#   the television   (168, -12.186, 80.5), on LabRoof.tscn's TvPad on the FAR roof
#   the sky deck     floor y = 200, x/z +-7 about the origin
#   the board        y = 200, z -7..-17, 1.2 m wide
#   the bubble       (0, 201.05, -15.9)
$shots = @(
    @{ Tag = "two-roofs";  Cam = "88,-3.2,92,168,-11.9,80.5" }
    @{ Tag = "tunnel";     Cam = "145,-11.2,71.5,168,-13.2,78" }
    @{ Tag = "roof-tv";    Cam = "161.5,-8.2,87.6,168,-11.6,80.5" }
    @{ Tag = "deck";       Cam = "17,208,16,0,200,-6" }
    @{ Tag = "board";      Cam = "4.5,202.2,-4.0,0,201.05,-15.9" }
    # FROM THE END OF THE BOARD, aimed 78 degrees below horizontal. The first attempt at this
    # shot stood on the deck aimed at (0,0,-58) and came back FLAT GREY -- and the cause was the
    # framing, not the fog: at that angle the frame covered a ~60 m radius, which from 200 m is
    # the grey hub plaza and nothing else. From the tip, at this angle, the frame reaches 238 m
    # out at its top edge and straight down at its bottom, which is the whole 165 m level. Left in
    # the file because "the vantage looks into haze" and "the camera was pointed at the one grey
    # square in the middle" produce the same picture and have completely different fixes.
    # The camera then sits 2.5 m PAST the board's tip rather than on it: from the tip itself the
    # last metre of plank and the hundredth bubble's film fill the middle of the lens, which is a
    # third way to get a picture of nothing.
    @{ Tag = "map";        Cam = "0,201.8,-19.5,0,0,-62" }
    @{ Tag = "fall";       Cam = "0,100,-17,0,0,-24" }
)

Write-Host "=== ROOFTOP-1 captures -> $OutDir ===" -ForegroundColor White
$missing = @()

foreach ($s in $shots) {
    if ($Only.Count -gt 0 -and $Only -notcontains $s.Tag) { continue }
    $shotDir = Join-Path $OutDir $s.Tag
    New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
    Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

    Write-Host ("[{0}] cam {1}..." -f $s.Tag, $s.Cam) -ForegroundColor Cyan
    $procs = @()
    try {
        $serverOut = Join-Path $script:LogDir "r1cap-server-$($s.Tag).out.log"
        $server = Start-Godot @(
            "--server", "--port", $Port, "--world", "bubbletest",
            "--cycle-start-phase", "noon", "--cycle-freeze",
            "--log-dir", $script:LogDir) "r1cap-server-$($s.Tag)"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
            Write-Host "      server never listened; skipping $($s.Tag)" -ForegroundColor Yellow
            $missing += $s.Tag
            continue
        }

        # The bot walks nowhere in particular -- it exists so there is a client rendering the
        # world at all. The camera is what frames every subject here, and none of them is a body.
        $bot = Start-GodotWindowed @(
            "--bot", "--address", "127.0.0.1:$Port", "--name", "R1Cap",
            "--duration", $DurationSec, "--world", "bubbletest",
            "--cycle-start-phase", "noon",
            "--capture-dir", $shotDir, "--capture-at", $CaptureAtSec,
            "--capture-cam", $s.Cam) "r1cap-bot-$($s.Tag)"
        $procs += $bot
        if (-not (Wait-ForExit $bot ([int]$DurationSec + 90))) {
            Write-Host "      the $($s.Tag) capture bot never exited" -ForegroundColor Yellow
        }
    }
    finally {
        foreach ($p in $procs) { if ($p -and -not $p.HasExited) { Stop-Proc $p } }
    }

    $pngs = Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue
    if (-not $pngs) {
        Write-Host "      NO PNG for $($s.Tag) -- see r1cap-bot-$($s.Tag).out.log" -ForegroundColor Yellow
        $missing += $s.Tag
    } else {
        foreach ($p in $pngs) { Write-Host ("        {0}  ({1:N0} bytes)" -f $p.Name, $p.Length) -ForegroundColor Gray }
    }
}

if ($missing.Count -gt 0) {
    Write-Host ("MISSING: {0}" -f ($missing -join ", ")) -ForegroundColor Yellow
} else {
    Write-Host "all shots captured." -ForegroundColor Green
}
