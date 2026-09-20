<#
.SYNOPSIS
    HOLD-1: first-person captures of the holding room -- the board readable from 4 m, and the
    practice corner. UNREGISTERED and manual: it opens windows and writes PNGs, which is not a
    pass/fail thing and does not belong in the marathon.

.DESCRIPTION
    WINDOWED, NOT HEADLESS, and given a camera. CELEBRATE-1 measured that a --bot client builds
    no camera of its own, so a --capture-dir run without one writes frames in which every
    CanvasLayer is perfectly correct over the engine's flat clear colour -- indistinguishable at
    a glance from a level that failed to build. --first-person-cam (FP-1) builds the real rig on
    the bot's own avatar, which is also the lens this feature is FOR.

    AND GIVEN SOMEWHERE TO STAND. FP-1's other measurement: DeterministicWalkIntentSource walks
    radially OUTWARD from the world origin, so a bot left alone spends the whole run with its
    face 30 cm from a corner and produces a 14 KB frame of flat grey that a suite will happily
    call green. --goto-script puts it in front of the thing; --fp-look aims the lens, because
    with a camera on the avatar the brain no longer decides where it points.

    THE MARKS ARE A COARSE GRID AND THE GOOD FRAMES ARE KEPT. DOOR-1 measured a viewport capture
    at ~0.18 s, so a 0.1 s grid queues behind itself and drifts a full second over seven marks
    while a 0.25 s grid does not. Nothing here is that dense -- these are seconds apart -- but
    the other half of that entry applies: the offset between a bot's --capture-at clock and the
    ROUND's clock is the connect time, and it moved by a full second between two runs of one
    script. So shoot generously, look at the frames, keep the ones you wanted.

    BEFORE BELIEVING A FRAME, COMPARE ITS FILE SIZE against one you know is good. Two orders of
    magnitude of PNG is what "this is a photograph of a wall" looks like.

.PARAMETER SkipBuild
    Reuse the existing build and import.
#>
[CmdletBinding()]
param(
    # The board suite's own port. This script never runs beside it (it is unregistered and
    # manual), and sharing the number keeps one line in the ladder rather than two.
    [int]$Port = 7909,
    [int]$MutexTimeoutMinutes = 120,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

$CaptureDir = Join-Path $script:Root "docs\qa\2026-09-19-hold-1"

# The round, so the board has a phase to show and eventually a card in its footer.
#   12 Start -> Hiding   36 Confirm -> Seeking   48 found -> Together   52 End -> Tally
#   58 the reset edge, back to Holding with the card still up
$Script = "start@12,confirm@36,towers:3@44,found@48,end@52"

# WHERE EACH LENS STANDS, in holding-room local metres (the room is instanced at the origin).
#
# The board is 3.6 x 2.2 m centred at (-3.1, 1.55, -4.94) on the -Z wall. A player at
# (-3.1, -0.94) is exactly 4 m from it -- the distance the packet asks it to be readable at --
# and square on. Pitch +8 deg is atan(0.51 / 4): the board's centre is 0.51 m above the 1.04 m
# eyeline, so this is "looking at it", not "looking past it".
$BoardGoto = "-3.1,-0.94"
$BoardLook = "0,8"

# The practice corner runs along the -X wall at x = -4.55, z in [-1.5, 1.5]. Standing at
# (-3.2, 0) and facing -X puts the table, the eight crates, the three near-misses, the target
# box and the pad all in frame. Pitch -12 deg because it is furniture at hand height and the
# eyeline is above it.
$CornerGoto = "-3.2,0"
$CornerLook = "90,-12"

# EXACTLY TWO CAPTURE CLIENTS, AND THAT IS A RULE OF THE GAME RATHER THAN A CHOICE. The first
# cut of this script ran three (a second corner lens at the opposite yaw, to settle which sign
# turns which way without a second run). The round never started: HideSeekLoop requires exactly
# two humans, and the server said so in as many words --
#     [round] refused: NeedTwoPlayers - TWO PLAYERS ARE NEEDED TO START (phase=Holding humans=3)
# -- so every board frame in that run read HOLDING and the footer was empty for 72 seconds. The
# third lens answered the yaw question (+90 faces -X, which is what the corner wants) and was
# then deleted. A capture script for this game gets two windows, no more.
#
# BOTH LENSES LIVE THE WHOLE RUN, for the other half of the same rule: the loop re-checks that
# its role holders are still present, so a lens that exits at 40 s ends the round by disconnect
# and the card in the footer says "ended: X left" instead of a result.
$BoardMarks  = "8,20,40,55,60,66"
$CornerMarks = "10,26,50"
$BoardDuration = 74
$CornerDuration = 74

Write-Host "=== HOLD-1: capturing the holding room ===" -ForegroundColor White
Write-Host "    captures -> $CaptureDir" -ForegroundColor DarkGray

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }
    New-Item -ItemType Directory -Path $CaptureDir -Force | Out-Null

    $serverOut = Join-Path $script:LogDir "holdcap-server.out.log"
    $server = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket",
        "--round-script", $Script, "--log-clock") `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "holdcap-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "the server never reported listening on udp/$Port; see $serverOut"
    }

    # Engine flags before the bare --, game flags after. Wrong side is swallowed and looks
    # exactly like a hang (.claude/rules/test-suite.md).
    function Start-CaptureClient([string]$Name, [string]$Goto, [string]$Look,
                                 [string]$Marks, [int]$DurationSec) {
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
            "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
            "--windowed", "--first-person-cam",
            "--goto-script", $Goto, "--fp-look", $Look,
            "--capture-dir", $CaptureDir, "--capture-at", $Marks,
            "--log-clock", "--duration", $DurationSec) `
            -RedirectStandardOutput (Join-Path $script:LogDir "holdcap-$Name.out.log") `
            -RedirectStandardError (Join-Path $script:LogDir "holdcap-$Name.err.log") `
            -PassThru
        $null = $p.Handle
        return $p
    }

    # The board's lens joins FIRST, so it is round 1's hider and its own row says YOU HIDE --
    # which is the cell the capture is meant to show.
    Write-Host "[1/3] the board, from 4 m, windowed, ${BoardDuration}s..." -ForegroundColor Cyan
    $boardCam = Start-CaptureClient "Board" $BoardGoto $BoardLook $BoardMarks $BoardDuration
    $procs += $boardCam
    Start-Sleep -Milliseconds 1200

    Write-Host "[2/3] the practice corner, windowed, ${CornerDuration}s..." -ForegroundColor Cyan
    $cornerCam = Start-CaptureClient "Corner" $CornerGoto $CornerLook $CornerMarks $CornerDuration
    $procs += $cornerCam

    Write-Host "[3/3] waiting..." -ForegroundColor Cyan
    foreach ($p in @($boardCam, $cornerCam)) {
        if (-not (Wait-ForExit $p ($BoardDuration + 60))) { Stop-Proc $p }
    }
    Stop-Proc $server
}
finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host ""
Write-Host "frames written (compare the sizes -- a 14 KB frame is a photograph of a wall):" -ForegroundColor White
Get-ChildItem $CaptureDir -Filter *.png | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-44} {1,8:N0} bytes  {2}" -f $_.Name, $_.Length, $_.LastWriteTime.ToString("HH:mm:ss.fff"))
}
