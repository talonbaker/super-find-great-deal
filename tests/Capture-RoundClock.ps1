<#
.SYNOPSIS
    CLOCK-1: headed photographs of the wall clock, from inside a first-person client.
    NOT a registered suite -- it opens two windows and asserts nothing. It exists so the packet's
    three captures can be reproduced rather than being three files somebody once made.

.DESCRIPTION
    THE THREE SHOTS THE PACKET ASKS FOR are in three different rooms, and the round is what puts
    a player in a room -- so they cannot come from one client and they cannot come from one pass:

      Pass 1, during Hiding, when the hider is in the search room and the seeker is shut in the
      holding room:
        * the HOLDING room's clock with the HUD strip in the same frame (the seeker's view) --
          this is the shot that proves the diegetic and non-diegetic readouts agree, so both have
          to be in one photograph or it proves nothing;
        * the SEARCH room's clock at 0:03 (the hider's view). The script deliberately does NOT
          press Confirm, so the hide runs all the way to the buzzer and the clock actually
          reaches three.

      Pass 2, during Tally, when the hider is in the task room:
        * the TASK room's clock showing the tally line.

    WHERE A BOT STANDS AND WHERE IT LOOKS ARE TWO DIFFERENT PROBLEMS, and FP-1 paid for both (see
    .claude/rules/test-suite.md). The deterministic walk brain heads radially OUTWARD from the
    world origin, so left alone a bot spends the run with its face in a corner; --goto-script
    gives it somewhere to stand. Each room is 40 m apart along +X, so "the middle of the search
    room" is the world point 40,0 -- and because the round TELEPORTS the hider between rooms, one
    goto target serves one room, which is the other reason there are two passes.

    Yaw 0 is -Z (FirstPersonCamera's own convention note) and the clock is authored on each room's
    -Z wall, so every capture here looks down the same axis. Positive pitch is up; the panel's
    centre is 1.31 m above the eyeline at about 5 m, which is 15 degrees.

    MARKS ARE TAKEN GENEROUSLY AND THE GOOD ONES ARE KEPT. --capture-at counts from the CLIENT'S
    OWN launch, while --round-script counts from the first player ARRIVING, and the gap between
    them is however long Godot takes to boot. Rather than calibrate that, this takes a spread of
    frames around each moment; --log-clock is on, so every frame can be matched to the second the
    clock was actually showing when it was taken.

.PARAMETER Pass
    1 (holding + search, during Hiding) or 2 (task, during Tally).
#>
[CmdletBinding()]
param(
    [ValidateSet(1, 2)][int]$Pass = 1,
    # 7904, the same port the registered suite claims. Safe: this never runs at the same time as
    # that suite -- both take the machine-wide mutex.
    [int]$Port = 7904,
    [int]$MutexTimeoutMinutes = 60,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if ($Pass -eq 1) {
    # No Confirm: the hide runs to the buzzer at t = 38, so the clock really counts down to 0:03.
    $Script = "start@8"
    $HiderGoto = "40,0"      # the middle of the search room
    # Measured on the first run: the 36 s mark landed at SEEKING 2:59, i.e. four seconds past
    # the buzzer, so 0:03 is about 32 s into this client's own life. Dense enough either side
    # that boot-time variance between runs cannot step over it.
    $HiderMarks = "29,30,31,32,33,34,35"
    $HiderDuration = 52
    $SeekerMarks = "18,22,26,30"
    # THE SEEKER OUTLIVES THE HIDER, in both passes. A role holder leaving mid-round commits the
    # card as EndedByDisconnect (HideSeekLoop.RoleHolderMissing), and the first run photographed
    # exactly that: the task clock read ENDED_EARLY instead of a tally.
    $SeekerDuration = 56
} else {
    $Script = "start@8,confirm@14,sorts:3@20,found@30,end@35"
    $HiderGoto = "80,0"      # the middle of the task room
    # Measured: the tally card is on the wall from about 33 s to 38 s of this client's own life
    # (Tally is 6 s). The first run's marks started at 40 and photographed the holding room of
    # round 2 -- the reset edge had already fired and put everybody home.
    $HiderMarks = "32,33,34,35,36,37,38"
    $HiderDuration = 56
    $SeekerMarks = "44"
    # See pass 1's note: the seeker must outlive the hider or the card says ENDED EARLY.
    $SeekerDuration = 62
}

$CaptureDir = Join-Path $script:LogDir "clock-capture-pass$Pass"
if (Test-Path $CaptureDir) { Remove-Item -Recurse -Force $CaptureDir }
New-Item -ItemType Directory -Path $CaptureDir -Force | Out-Null

Write-Host "=== CLOCK-1 captures, pass $Pass ===" -ForegroundColor White

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
        New-Item -ItemType Directory -Path $CaptureDir -Force | Out-Null
    }

    $serverOut = Join-Path $script:LogDir "clockcap-server.out.log"
    $server = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket",
        "--round-script", $Script, "--log-clock") `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "clockcap-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "the server never reported listening; see $serverOut"
    }

    # WINDOWED, no --headless: what is being photographed is what a camera renders. Engine flags
    # before the bare --, game flags after.
    function Start-CaptureClient([string]$Name, [string]$Goto, [string]$Marks, [int]$DurationSec) {
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
            "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
            "--windowed", "--first-person-cam",
            "--goto-script", $Goto,
            "--fp-look", "0,15",
            "--capture-dir", $CaptureDir, "--capture-at", $Marks,
            "--log-clock", "--duration", $DurationSec) `
            -RedirectStandardOutput (Join-Path $script:LogDir "clockcap-$Name.out.log") `
            -RedirectStandardError (Join-Path $script:LogDir "clockcap-$Name.err.log") `
            -PassThru
        $null = $p.Handle
        return $p
    }

    # The HIDER joins first: the roster is in join order and round 1's hider is index 0.
    Write-Host "[1/2] the hider (goto $HiderGoto), windowed, ${HiderDuration}s..." -ForegroundColor Cyan
    $hider = Start-CaptureClient "ClockHider" $HiderGoto $HiderMarks $HiderDuration
    $procs += $hider
    Start-Sleep -Milliseconds 1200

    Write-Host "[2/2] the seeker (goto 0,0 -- the holding room), windowed, ${SeekerDuration}s..." -ForegroundColor Cyan
    $seeker = Start-CaptureClient "ClockSeeker" "0,0" $SeekerMarks $SeekerDuration
    $procs += $seeker

    foreach ($p in @($hider, $seeker)) {
        if (-not (Wait-ForExit $p ($HiderDuration + 60))) { Stop-Proc $p }
    }
    Stop-Proc $server
}
finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host ""
Write-Host "Frames in $CaptureDir :" -ForegroundColor White
foreach ($shot in @(Get-ChildItem -Path $CaptureDir -Filter "*.png" -ErrorAction SilentlyContinue | Sort-Object Name)) {
    # FP-1's lesson, in .claude/rules/test-suite.md: compare a capture's SIZE to one you know is
    # good before believing it. Two orders of magnitude of PNG is what "a photograph of a wall"
    # looks like.
    Write-Host ("  {0,-28} {1,9:N0} bytes" -f $shot.Name, $shot.Length)
}
Write-Host ""
Write-Host "What each client's clocks read, for matching a frame to a second:" -ForegroundColor White
foreach ($n in @("ClockHider", "ClockSeeker")) {
    $log = Join-Path $script:LogDir "clockcap-$n.out.log"
    if (-not (Test-Path $log)) { continue }
    Write-Host "  --- $n ---" -ForegroundColor DarkGray
    Select-String -Path $log -Pattern '\[clock\] \d+ (clock|cue) ' |
        ForEach-Object { Write-Host "    $($_.Line)" -ForegroundColor DarkGray }
}
