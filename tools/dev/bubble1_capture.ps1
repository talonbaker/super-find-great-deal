<#
.SYNOPSIS
    BUBBLE-1's headed evidence: seven shots of where the hundred bubbles now are, written to
    docs/qa/BUBBLE-1/. Not a pass/fail suite -- it produces frames a human looks at, and it is
    deliberately not registered in Run-AllTests.ps1 for that reason.

.DESCRIPTION
    --capture-cam ON EVERY SHOT, and it is not optional. A bot's own follow camera frames the
    BOT, and every subject here is a place rather than a body -- worse, six of the seven are
    sealed rooms the bot is not even in, so a follow-camera shot would be a picture of the hub.
    docs/testing/TESTER-HANDOFF.md states the rule flatly: "--capture-cam is MANDATORY or you get
    a false pass".

    THE SHOTS. Four rooms behind televisions that had no bubbles at all until this packet, two
    looks at the puffin lab which had none either, and one at cyan, which is where most of the
    cut came from.

      roomB   -- RedTv's room, from the north-west corner. Three new bubbles.
      roomC   -- BlueTv's room, the big one, from the west. Three new bubbles.
      roomD   -- TangleTv's room, the one with the 3 m ceiling. Three new bubbles.
      roomF   -- the sunken television's Deep Room: a silt floor under a lit water ceiling, and
                 the one room with nothing to hide a bubble behind. Three new bubbles.
      lab     -- the puffin lab's main room from the gallery corner, holding all four of the
                 bubbles in the room the player arrives in.
      labhall -- the long hallway, looking east toward the tiny door, so the two hallway bubbles
                 read as a trail rather than as two spheres in a corridor.
      cyan    -- the cyan run from the south-west, high enough to hold the whole 140 x 100 m
                 plate. This is the density shot: 33 bubbles became 18, and the two lane runs
                 down x = -30 and x = +30 went from seven and five to three and two. The point of
                 a wide frame here is that the LINE still reads at the lower count -- that is the
                 claim the reduction rests on, and a close-up cannot show it.

    ROOM CAMERAS ARE INSIDE THEIR ROOMS. The six TV rooms are sealed boxes 20 m under the hub
    with no opening of any kind, so there is no vantage outside one that sees into it. Each
    camera below is parked in a corner of the room it is shooting, under that room's ceiling --
    RoomD's is at 3 m rather than 4, which is why its camera is lower than the others.

    A missing PNG is reported and does not stop the remaining shots: this is evidence gathering,
    not a gate, and a machine with a busy GPU should still produce the six it can.

.PARAMETER SkipBuild
    Reuse the existing build and import.
#>
[CmdletBinding()]
param(
    [int]$Port = 45883,
    [string]$OutDir = "",
    [double]$DurationSec = 26,
    [double]$CaptureAtSec = 18,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\..\..\tests\_Common.ps1"

# NOT a param() default: PowerShell 5.1 does not reliably populate $PSScriptRoot while binding
# param defaults under `powershell -File`, and the failure is silent. Same workaround every
# *Capture script in tests/ uses.
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) "docs\qa\BUBBLE-1"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

function Start-GodotWindowed([string[]]$UserArgs, [string]$Tag) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $err = Join-Path $script:LogDir "$Tag.err.log"
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

# x,y,z, tx,ty,tz -- all WORLD space. The TV room section's anchor is (0, -20, 0) and every room
# floor is at world y = -20, so a camera at y = -17 is 3 m up inside its room.
$shots = @(
    @{ Tag = "roomB";   Cam = "-22.5,-16.8,5.0,-17.5,-19.0,-0.5" }
    @{ Tag = "roomC";   Cam = "10.8,-16.8,5.2,17.5,-19.0,-1.0" }
    # RoomD's ceiling is at world y = -17, not -16, so this camera sits a metre lower.
    @{ Tag = "roomD";   Cam = "-5.2,-17.6,-11.8,0.5,-19.0,-17.5" }
    @{ Tag = "roomF";   Cam = "-12.8,-17.0,-11.8,-17.5,-19.0,-18.0" }
    # The lab is anchored at (90, -14, 90); its main room's floor is world y = -14 and its
    # ceiling world y = -6.82, so this is 4.5 m up in the gallery corner.
    @{ Tag = "lab";     Cam = "81,-9.5,97,91,-13.0,90" }
    # The hallway floor is world y = -20.9 and its ceiling -16.48. Eye height, looking east down
    # its 55 m length at the two bubbles in it.
    @{ Tag = "labhall"; Cam = "120,-19.5,72.06,160,-20.4,72.06" }
    @{ Tag = "cyan";    Cam = "-60,40,30,0,1,95" }
)

Write-Host "=== BUBBLE-1 captures -> $OutDir ===" -ForegroundColor White
$missing = @()

foreach ($s in $shots) {
    $shotDir = Join-Path $OutDir $s.Tag
    New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
    Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

    Write-Host ("[{0}] cam {1}..." -f $s.Tag, $s.Cam) -ForegroundColor Cyan
    $procs = @()
    try {
        $serverOut = Join-Path $script:LogDir "b1cap-server-$($s.Tag).out.log"
        $server = Start-Godot @(
            "--server", "--port", $Port, "--world", "bubbletest",
            "--cycle-start-phase", "noon", "--cycle-freeze",
            "--log-dir", $script:LogDir) "b1cap-server-$($s.Tag)"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
            Write-Host "      server never listened; skipping $($s.Tag)" -ForegroundColor Yellow
            $missing += $s.Tag
            continue
        }

        # The bot walks nowhere in particular -- it exists so there is a client rendering the
        # world at all. The camera is what frames every subject here.
        $bot = Start-GodotWindowed @(
            "--bot", "--address", "127.0.0.1:$Port", "--name", "B1Cap",
            "--duration", $DurationSec, "--world", "bubbletest",
            "--cycle-start-phase", "noon",
            "--capture-dir", $shotDir, "--capture-at", $CaptureAtSec,
            "--capture-cam", $s.Cam) "b1cap-bot-$($s.Tag)"
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
        Write-Host "      NO PNG for $($s.Tag) -- see b1cap-bot-$($s.Tag).out.log" -ForegroundColor Yellow
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
