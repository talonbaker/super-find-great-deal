<#
.SYNOPSIS
    SOLO-1: two first-person captures of the holding room's board -- the ONE-ROW board a solo
    tester sees, and the THREE-ROW board with a WAITING row on it. UNREGISTERED and manual: it
    opens a window and writes PNGs, which is not a pass/fail thing and does not belong in the
    marathon.

.DESCRIPTION
    WINDOWED, AND GIVEN A CAMERA AND SOMEWHERE TO STAND, for the three reasons
    Capture-HoldingBoard.ps1 spells out: a --bot client builds no camera of its own (CELEBRATE-1),
    DeterministicWalkIntentSource walks radially outward and spends the run facing a corner
    (FP-1), and with a camera on the avatar the brain no longer decides where it points. Same
    board mark, same lens, same 4 m -- so these frames are comparable with HOLD-1's own.

    PHASE 1 -- THE SOLO BOARD. A server with --solo and ONE windowed lens. The board must paint a
    single row reading "SoloCam . NEXT: YOU HIDE . 0", a header that says MATCH 1 . ROUND 1 OF 2 .
    HOLDING, and NO status line under the rows, because in a solo session the one peer holds both
    roles and nobody is waiting. That last one is the negative half and it is the reason this
    phase is a capture rather than a paragraph: a status line that said "1 WAITING" to the only
    player in the game would be a readout calling them a spectator in their own session.

    PHASE 2 -- THE WAITING BOARD. A server WITHOUT the flag, two headless bots (who become the
    hider and the seeker by join order), and then a windowed lens joining THIRD. Its own row must
    read "SoloCam3 . WAITING . 0" against the other two's NEXT: HIDES / NEXT: SEEKS, and the
    status line under the rows must say "1 WAITING . NEXT FREE SEAT IS YOURS".

    THREE HUMANS IN ONE ROOM IS NOW LEGAL, and that reverses a rule written in
    Capture-HoldingBoard.ps1's own header. That script records losing a run to
    "[round] refused: NeedTwoPlayers - TWO PLAYERS ARE NEEDED TO START (phase=Holding humans=3)"
    and concludes "a capture script for this game gets two windows, no more". The refusal is what
    SOLO-1 removed (Talon, 2026-09-20: "with two or more players, they can wait in the room"), so
    the conclusion no longer follows -- but the number of WINDOWS is still one here, because a
    second lens would cost a frame rate and show nothing a headless bot cannot.

    NEITHER PHASE RUNS A --round-script, deliberately. The moment a round starts, the driver
    teleports people out of the holding room -- including, at the reset edge, every peer on the
    roster, which is where the lens is standing. A board capture belongs in Holding, and Holding
    is where both of these shots live.

    BEFORE BELIEVING A FRAME, COMPARE ITS FILE SIZE against one you know is good. Two orders of
    magnitude of PNG is what "this is a photograph of a wall" looks like.

.PARAMETER SkipBuild
    Reuse the existing build and import.
#>
[CmdletBinding()]
param(
    # Run-SoloSmoke.ps1's own port. This script never runs beside it (it is unregistered and
    # manual, and both take the machine-wide mutex), and sharing the number keeps one line in the
    # ladder rather than two -- the pattern CLOCK-1 established and every lane since has followed.
    [int]$Port = 7912,
    [int]$MutexTimeoutMinutes = 120,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

$CaptureDir = Join-Path $script:Root "docs\qa\2026-09-20-solo-1"

# WHERE THE LENS STANDS, in holding-room local metres, copied from Capture-HoldingBoard.ps1 so the
# two sets of frames are comparable: the board is 3.6 x 2.2 m centred at (-3.1, 1.55, -4.94) on
# the -Z wall, a player at (-3.1, -0.94) is exactly 4 m from it and square on, and pitch +8 deg is
# atan(0.51 / 4) -- the board's centre against a 1.04 m eyeline.
$BoardGoto = "-3.1,-0.94"
$BoardLook = "0,8"

# Marks a few seconds apart, well clear of DOOR-1's measured ~0.18 s capture cost, and generous
# rather than precise: the offset between a bot's --capture-at clock and the round's is the
# CONNECT time and it has moved by a full second between two runs of one script. Shoot, look,
# keep the ones you wanted.
$SoloMarks = "8,12,16"
$SoloDuration = 24

# Phase 2's lens joins after the other two, so its marks are on ITS own clock and start later
# only in wall time. The two headless bots outlive it, or the round would end by disconnect and
# the board would repaint mid-capture.
$WaitMarks = "8,12,16"
$WaitDuration = 24
$PlayerDuration = 44

Write-Host "=== SOLO-1: capturing the solo board and the waiting board ===" -ForegroundColor White
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

    # Engine flags before the bare --, game flags after. Wrong side is swallowed and looks exactly
    # like a hang (.claude/rules/test-suite.md).
    function Start-BoardServer([string]$Tag, [string[]]$Extra = @()) {
        $out = Join-Path $script:LogDir "solocap-$Tag-server.out.log"
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList (@(
            "--headless", "--path", $script:Root, "--",
            "--server", "--port", $Port, "--world", "supermarket", "--log-clock") + $Extra) `
            -RedirectStandardOutput $out `
            -RedirectStandardError (Join-Path $script:LogDir "solocap-$Tag-server.err.log") `
            -PassThru -NoNewWindow
        $null = $p.Handle
        if (-not (Wait-ForLogLine $out "\[server\] listening" 60)) {
            Write-Fail "the $Tag server never reported listening on udp/$Port; see $out"
        }
        return $p
    }
    function Start-Lens([string]$Name, [string]$Marks, [int]$DurationSec) {
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
            "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
            "--windowed", "--first-person-cam",
            "--goto-script", $BoardGoto, "--fp-look", $BoardLook,
            "--capture-dir", $CaptureDir, "--capture-at", $Marks,
            "--log-clock", "--duration", $DurationSec) `
            -RedirectStandardOutput (Join-Path $script:LogDir "solocap-$Name.out.log") `
            -RedirectStandardError (Join-Path $script:LogDir "solocap-$Name.err.log") `
            -PassThru
        $null = $p.Handle
        return $p
    }
    function Start-Filler([string]$Name, [int]$DurationSec) {
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
            "--headless", "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
            "--duration", $DurationSec, "--world", "supermarket", "--log-clock") `
            -RedirectStandardOutput (Join-Path $script:LogDir "solocap-$Name.out.log") `
            -RedirectStandardError (Join-Path $script:LogDir "solocap-$Name.err.log") `
            -PassThru -NoNewWindow
        $null = $p.Handle
        return $p
    }

    # --- PHASE 1: the one-row solo board ------------------------------------------------------
    Write-Host "[1/2] the SOLO board -- one player, one row, no status line..." -ForegroundColor Cyan
    $server = Start-BoardServer "solo" @("--solo")
    $procs += $server
    $soloCam = Start-Lens "SoloCam" $SoloMarks $SoloDuration
    $procs += $soloCam
    if (-not (Wait-ForExit $soloCam ($SoloDuration + 60))) { Stop-Proc $soloCam }
    Stop-Proc $server
    Start-Sleep -Milliseconds 800

    # --- PHASE 2: the three-row board with a WAITING row --------------------------------------
    Write-Host "[2/2] the WAITING board -- two players and a third who is not in the match..." -ForegroundColor Cyan
    $server2 = Start-BoardServer "wait"
    $procs += $server2
    $fillA = Start-Filler "SoloCam1" $PlayerDuration
    $procs += $fillA
    # A stagger so join ORDER is unambiguous: the first to arrive is this round's hider, and two
    # clients racing to connect makes which-one-hides a coin flip (CLOCK-1's note).
    Start-Sleep -Milliseconds 1500
    $fillB = Start-Filler "SoloCam2" $PlayerDuration
    $procs += $fillB
    Start-Sleep -Milliseconds 1500
    $waitCam = Start-Lens "SoloCam3" $WaitMarks $WaitDuration
    $procs += $waitCam

    foreach ($p in @($waitCam, $fillA, $fillB)) {
        if (-not (Wait-ForExit $p ($PlayerDuration + 60))) { Stop-Proc $p }
    }
    Stop-Proc $server2
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
Write-Host ""
Write-Host "what to look for:" -ForegroundColor White
Write-Host "  SoloCam*   ONE row 'SoloCam . NEXT: YOU HIDE . 0', and NOTHING under the rows."
Write-Host "  SoloCam3*  THREE rows, the lens's own reading 'SoloCam3 . WAITING . 0', and"
Write-Host "             '1 WAITING . NEXT FREE SEAT IS YOURS' under them."
