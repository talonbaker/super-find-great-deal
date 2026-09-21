<#
.SYNOPSIS
    FEEL-1: what the new carry looks like from the holder's eye. Unregistered, manual, asserts
    nothing.

.DESCRIPTION
    Four shots, one per thing Talon asked for. Shares udp/7913 with tests/Run-CarryHoldTest.ps1,
    which is the established pattern (CLOCK-1's Capture-RoundClock beside Run-RoundClockTest):
    both take the machine-wide mutex, so the two can never be alive at the same moment, and only
    the registered half is in Run-AllTests.ps1.

    NEEDS A DESKTOP SESSION. Every renderer is gated on DisplayServer.GetName(), and a headless
    peer photographs nothing -- see .claude/rules/test-suite.md.

      held        a can grabbed and held exactly where it was grabbed -- off-centre, at the angle
                  it was lying at, not snapped to a socket.
      held-close  the same can, wheel rolled all the way IN (the band's floor, which is derived
                  from the can's own bulk and the body's capsule).
      held-far    the same can, wheel rolled all the way OUT.
      shelf       a crate driven into SearchPillar and stopped at its face -- "that's why there's
                  physics and collision on the objects".
      backwards   the holder's eye while walking BACKWARD with the crate, which is the motion the
                  old carry clipped through the body on.

    The wheel shots go through NetworkedProp.ScrollHold (--hold-notches), the same method a wheel
    event calls, so they are photographs of the shipped verb rather than of a pose invented here.

.PARAMETER OutDir
    Where the PNGs land. Default docs/qa/2026-09-20-feel-1.
#>
[CmdletBinding()]
param(
    [ValidateSet("all", "held", "held-close", "held-far", "shelf", "backwards")][string]$Pass = "all",
    [string]$OutDir = "",
    [int]$Port = 7913,
    [int]$MutexTimeoutMinutes = 120,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if ($OutDir -eq "") { $OutDir = Join-Path $script:Root "docs/qa/2026-09-20-feel-1" }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

Write-Host "=== FEEL-1 captures (pass: $Pass) -> $OutDir ===" -ForegroundColor White

# Each shot: the prop it grabs, where it walks first, how many wheel notches, and whether it runs
# the hold-stress loop (which is what puts the body in a backward walk).
# THE LENS HAS TO BE POINTED WHERE THE BODY IS POINTED, and for a bot those are two different
# things. FirstPersonCamera carries its own yaw/pitch (a human's mouse drives it); a scripted bot
# has no mouse, so without --fp-look the lens sits at its default while the BODY turns to face
# wherever the brain is walking -- and the held prop, which rides the body's aim, is off-camera.
# The first capture pass shot five frames of a shelf with the object out of frame in every one.
# Yaw convention (STOCK-1's table): 0 faces -Z, -90 faces +X, +90 faces -X, 180 faces +Z.
$Shots = @{
    "held"       = @{ Look = "-90,-12"; Prop = "SearchRoom/Prop_0"; Script = "36,0.22,0,1.0,-1"; Notches = 0;   Stress = $false; Marks = "9,11,13" }
    "held-close" = @{ Look = "-90,-12"; Prop = "SearchRoom/Prop_0"; Script = "36,0.22,0,1.0,-1"; Notches = -9;  Stress = $false; Marks = "9,11,13" }
    "held-far"   = @{ Look = "-90,-12"; Prop = "SearchRoom/Prop_0"; Script = "36,0.22,0,1.0,-1"; Notches = 9;   Stress = $false; Marks = "9,11,13" }
    "shelf"      = @{ Look = "-90,-10"; Prop = "SearchRoom/Prop_1"; Script = "36,0.22,2,1.0,-1"; Notches = 0;   Stress = $false; Marks = "12,14,16"; WalkTo = "46,2.5" }
    "backwards"  = @{ Look = "-90,-12"; Prop = "SearchRoom/Prop_0"; Script = "36,0.22,0,1.0,-1"; Notches = 0;   Stress = $true;  Marks = "11,12.5,14" }
}

function Start-CaptureHost([string]$Tag) {
    $out = Join-Path $script:LogDir "$Tag.host.out.log"
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
        "--windowed") `
        -RedirectStandardOutput $out `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.host.err.log") `
        -PassThru
    $null = $p.Handle
    return $p
}

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
try {
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }
    if (-not $SkipBuild) { Invoke-BuildAndImport }

    $which = if ($Pass -eq "all") { @("held", "held-close", "held-far", "shelf", "backwards") } else { @($Pass) }

    foreach ($name in $which) {
        $cfg = $Shots[$name]
        Write-Host ""
        Write-Host ">>> $name  prop=$($cfg.Prop) notches=$($cfg.Notches) stress=$($cfg.Stress)" -ForegroundColor Cyan

        $h = Start-CaptureHost "feelcap-$name"
        $procs += $h
        $hostOut = Join-Path $script:LogDir "feelcap-$name.host.out.log"
        if (-not (Wait-ForLogLine $hostOut "\[server\] listening" 150)) {
            Stop-Proc $h
            Write-Fail "the capture host never reported listening on udp/$Port"
        }
        # Derived from the host's own adoption log, never typed -- an authored prop's id is a
        # function of every other authored prop's NAME (see _Common.ps1).
        $propId = Get-AuthoredPropId $hostOut $cfg.Prop

        # Engine flags before the bare --, game flags after. Wrong side is swallowed and looks
        # exactly like a hang (.claude/rules/test-suite.md).
        $args = @(
            "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $name,
            "--windowed", "--first-person-cam",
            "--carry-script", $cfg.Script,
            "--carry-target-prop", $propId,
            "--capture-dir", $OutDir, "--capture-at", $cfg.Marks,
            "--fp-look", $cfg.Look,
            "--world", "supermarket", "--duration", 20,
            "--log", (Join-Path $script:LogDir "feelcap-$name.jsonl"))
        # NO --carry-grab-retry here, unlike the suites. A retry that fires while the bot is
        # already holding something is refused -- correctly -- and the refusal CARD then covers
        # the middle of every frame ("CAN'T PICK THAT UP / Your hands are full"). A suite wants
        # the retry because a missed grab costs it a phase; a photograph wants a clean frame.
        if ($cfg.Notches -ne 0) { $args += @("--hold-notches", $cfg.Notches) }
        if ($cfg.Stress) { $args += "--hold-stress" }
        if ($cfg.ContainsKey("WalkTo")) { $args += @("--carry-walk-to", $cfg.WalkTo) }

        $shot = Start-Process -FilePath $script:GodotExe -ArgumentList $args `
            -RedirectStandardOutput (Join-Path $script:LogDir "feelcap-$name.out.log") `
            -RedirectStandardError (Join-Path $script:LogDir "feelcap-$name.err.log") -PassThru
        $null = $shot.Handle
        $procs += $shot
        if (-not (Wait-ForExit $shot 120)) { Stop-Proc $shot }
        Stop-Proc $h
        Start-Sleep -Milliseconds 800
    }
} finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host ""
Write-Host "frames in $OutDir :" -ForegroundColor Green
Get-ChildItem -Path $OutDir -Filter *.png | ForEach-Object {
    # A frame two orders of magnitude smaller than its siblings is a photograph of a wall --
    # CELEBRATE-1's measured trap, and the cheapest check there is.
    Write-Host ("  {0,-34} {1,8:N0} bytes" -f $_.Name, $_.Length)
}
