<#
.SYNOPSIS
    TASK-1: headed photographs of the sorting job, from inside a first-person client.
    NOT a registered suite -- it opens windows and asserts nothing. It exists so the packet's
    captures can be reproduced rather than being files somebody once made.

.DESCRIPTION
    THREE PASSES, because the three shots need three different things to be true at once and
    only one peer is ever in the task room.

      -Pass colour   Round 1 sorts by COLOUR. A windowed HIDER that does no sorting at all,
                     stood in front of the three bins with a fixed look, so the plates are
                     photographed clean: RED / BLUE / YELLOW, each tinted to the colour it is
                     asking for, with the shape icons hidden.

      -Pass burst    The same round, with the windowed hider running --sort-script. It fills
                     two bins, picks up a fourth object and is still holding it when the door
                     goes -- so the frames around the burst are the packet's "an object knocked
                     out of the player's hands", from the hider's own eyes.

      -Pass shape    Round 2 sorts by SHAPE. Roles swap at the reset edge, so the SECOND joiner
                     is round 2's hider; it is the windowed one here, stood at the same place,
                     and the plates read CUBE / BALL / CAN with the small unshaded icon above
                     each word and no tint. The same three boxes, re-labelled -- which is the
                     whole mechanic in one photograph pair with the colour pass.

    WHERE A BOT STANDS AND WHERE IT LOOKS ARE TWO DIFFERENT PROBLEMS and FP-1 paid for both
    (.claude/rules/test-suite.md). --goto-script gives it somewhere to stand; --fp-look aims the
    lens, because with a first-person camera the brain no longer decides where it points.

    Yaw 0 is -Z (FirstPersonCamera's convention) and the bins are on the task room's +Z wall at
    x = 1.2 / 2.0 / 2.8 local, which is world (81.2 / 82.0 / 82.8, ., 4.6). The stand point and
    the look are on the same axis on purpose -- see the comment above $BinStand.

    MARKS ARE TAKEN GENEROUSLY AND THE GOOD ONES ARE KEPT. --capture-at counts from the CLIENT'S
    OWN launch and --round-script counts from the first player ARRIVING, and the gap is however
    long Godot takes to boot a WINDOW. DOOR-1 measured that a 0.25 s grid does not queue and a
    0.1 s one does, so nothing here is closer than half a second.

.PARAMETER Pass
    colour (the RED/BLUE/YELLOW plates), burst (the drop, mid-carry) or shape (the CUBE/BALL/CAN
    plates in round 2).
#>
[CmdletBinding()]
param(
    [ValidateSet("colour", "burst", "shape")][string]$Pass = "colour",
    # 7907, the same port the registered suite claims. Safe: this never runs at the same time as
    # that suite -- both take the machine-wide mutex.
    [int]$Port = 7907,
    [int]$MutexTimeoutMinutes = 120,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# Standing at the bins: world (82.0, 3.0) -- the middle bin's own x, about 1.6 m out from its
# plate -- looking straight at it. Yaw 180 is +Z (yaw 0 is -Z) and the plates sit 0.25 m below
# the 1.04 m eyeline, which is about 4 degrees down at this range.
#
# The first pass stood at (81.5, 2.0) on a bearing of -169 degrees and put the row small and
# left of centre: --goto-script's arrive radius is 1.5 m, so a stand point chosen for its exact
# distance is only ever within a metre and a half of it. Aim the look straight down an axis and
# accept the slop in RANGE rather than in bearing, which is the half a photograph survives.
$BinStand = "82.0,3.0"
$BinLook  = "180,-4"

switch ($Pass) {
    "colour" {
        # No sorting: the plates are the subject and an object arcing through the frame is not.
        $Script     = "start@8,confirm@14,found@60,end@66"
        $WindowedIsFirst = $true
        $SortArgs   = @("--goto-script", $BinStand, "--fp-look", $BinLook)
        $Marks      = "20,24,28,32,36,40"
        $Duration   = 56
        $OtherDur   = 62
    }
    "burst" {
        # The hider works the job and is caught mid-carry. The burst lands at found + TellSec
        # (0.4 s), i.e. server t = 60.4.
        #
        # THE CLIENT'S CAPTURE CLOCK IS NOT THE ROUND'S CLOCK and the offset is bigger than
        # DOOR-1's note implies. --round-script counts from the first player ARRIVING and
        # --capture-at counts from this process's own launch, and a WINDOWED client's boot is
        # several seconds of that. Measured on the first pass here: the 59.5 s mark photographed
        # the TALLY card, i.e. server t = 67, so the client runs about 7 s AHEAD of the round.
        # The grid below is centred on server 60.4 minus that offset, spread wide because the
        # offset itself moved by a second between two runs of CLOCK-1's harness.
        $Script     = "start@8,confirm@14,found@60,end@66"
        $WindowedIsFirst = $true
        $SortArgs   = @("--sort-script", "1004:0,1007:2,1010:2,1004:0,1013:-1")
        # The grid was walked onto the burst by MTIME rather than by arithmetic, which is
        # DOOR-1's own advice: a PNG's mtime is the capture's wall clock and the only
        # cross-process anchor it has. Second pass: the 54.5 s frame landed 2.406 s before
        # the door's own `wall=` stamp, so the grid moved up by that and widened, because
        # the offset itself moves about a second between runs.
        $Marks      = "20,24,55.5,56,56.5,57,57.5,58,58.5,59"
        $Duration   = 70
        $OtherDur   = 76
    }
    "shape" {
        # Round 2. The reset edge swaps the roles, so the SECOND joiner hides this time and is
        # the one that lands in the task room -- hence $WindowedIsFirst = $false.
        #
        # `lost@36` IS NOT OPTIONAL. `found` is a LEVEL, not a press (ROUND-1: "a bin does not
        # pulse"), so it latches on and stays on -- and without clearing it, round 2 leaves
        # Seeking on its first tick. The first run of this pass photographed exactly that: the
        # strip read "FOUND . YOU HIDE . ROUND 2 . SORTED 0 . BY SHAPE" fourteen seconds into a
        # round that was supposed to be running.
        #
        # AND IT SORTS RATHER THAN STANDING, because --goto-script LATCHES its arrival and
        # cannot be re-aimed: in round 1 this client is the seeker, so the burst lets it walk
        # into the task room, where it reaches the stand point and latches for good. In round 2
        # it would then stand wherever the teleport dropped it. The sort brain has no latch --
        # it is a loop, and it is gated on Seeking -- so it walks to the bins under its own
        # steam, and the camera follows its heading, which is what puts the plates in frame.
        # The three steps are one object of each SHAPE into its own bin, all correct.
        $Script     = "start@8,confirm@12,found@22,end@28,lost@36,start@46,confirm@50"
        $WindowedIsFirst = $false
        $SortArgs   = @("--sort-script", "1004:0,1005:1,1006:2")
        # Measured: the three deliveries land at 46.6 / 49.3 / 52.2 s of this client's own
        # life (it is the SECOND joiner, so its clock runs about four seconds BEHIND the
        # round script rather than ahead of it as the burst pass's does). The marks sit on
        # those three moments plus the idle after the last one, when the bot is stood at
        # bin 2 with all three plates across the frame.
        $Marks      = "44,46.5,47,49,49.5,52,52.5,54,56,58"
        $Duration   = 84
        $OtherDur   = 90
    }
}

$CaptureDir = Join-Path $script:LogDir "sort-capture-$Pass"
if (Test-Path $CaptureDir) { Remove-Item -Recurse -Force $CaptureDir }
New-Item -ItemType Directory -Path $CaptureDir -Force | Out-Null

Write-Host "=== TASK-1 captures, pass '$Pass' ===" -ForegroundColor White

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    New-Item -ItemType Directory -Path $CaptureDir -Force | Out-Null

    $serverOut = Join-Path $script:LogDir "sortcap-server.out.log"
    $server = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket",
        "--round-script", $Script) `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "sortcap-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "the server never reported listening; see $serverOut"
    }

    # WINDOWED, no --headless: what is being photographed is what a camera renders. Engine flags
    # before the bare --, game flags after.
    function Start-CaptureClient([string]$Name, [string[]]$Extra, [string]$Marks, [int]$Dur) {
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList (@(
            "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
            "--windowed", "--first-person-cam",
            "--capture-dir", $CaptureDir, "--capture-at", $Marks,
            "--duration", $Dur) + $Extra) `
            -RedirectStandardOutput (Join-Path $script:LogDir "sortcap-$Name.out.log") `
            -RedirectStandardError (Join-Path $script:LogDir "sortcap-$Name.err.log") `
            -PassThru
        $null = $p.Handle
        return $p
    }

    function Start-Filler([string]$Name, [int]$Dur) {
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
            "--headless", "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
            "--goto-script", "0,0", "--duration", $Dur) `
            -RedirectStandardOutput (Join-Path $script:LogDir "sortcap-$Name.out.log") `
            -RedirectStandardError (Join-Path $script:LogDir "sortcap-$Name.err.log") `
            -PassThru -NoNewWindow
        $null = $p.Handle
        return $p
    }

    # THE JOIN ORDER IS THE ROLE ORDER (HideSeekLoop: the roster is in join order and round 1's
    # hider is index 0), so which of these two goes first is what decides who is photographing
    # the task room. The OTHER one always outlives the windowed one: a role holder leaving
    # mid-round commits the card as EndedByDisconnect, which CLOCK-1's first capture run
    # photographed by accident.
    # THE SECOND CLIENT IS GATED ON THE FIRST ONE'S OWN CONNECT LINE, not on a sleep, and this
    # was measured rather than reasoned: a 1200 ms head start is NOT enough for a WINDOWED client
    # to beat a headless one to the server, because creating a window and a renderer costs
    # several seconds that a --headless boot does not pay. The third run of this pass flipped the
    # roles -- the shot came back reading "FOUND . YOU SEEK . ROUND 1" from an empty search room,
    # with `hider=False` in the door's own line -- while the two runs before it had been fine.
    # Same rule as everywhere else in tests/: gate on the event, never on a guess about how long
    # it takes (.claude/rules/test-suite.md, FIX-1).
    function Wait-Connected([string]$Name) {
        $log = Join-Path $script:LogDir "sortcap-$Name.out.log"
        if (-not (Wait-ForLogLine $log "\[client\] connected as peer" 90)) {
            Write-Fail "$Name never connected; see $log"
        }
    }

    if ($WindowedIsFirst) {
        Write-Host "[1/2] the windowed hider, ${Duration}s..." -ForegroundColor Cyan
        $shooter = Start-CaptureClient "SortShot" $SortArgs $Marks $Duration
        $procs += $shooter
        Wait-Connected "SortShot"
        Write-Host "[2/2] the headless seeker, ${OtherDur}s..." -ForegroundColor Cyan
        $other = Start-Filler "SortFiller" $OtherDur
        $procs += $other
    } else {
        Write-Host "[1/2] the headless first joiner, ${OtherDur}s..." -ForegroundColor Cyan
        $other = Start-Filler "SortFiller" $OtherDur
        $procs += $other
        Wait-Connected "SortFiller"
        Write-Host "[2/2] the windowed second joiner (round 2's hider), ${Duration}s..." -ForegroundColor Cyan
        $shooter = Start-CaptureClient "SortShot" $SortArgs $Marks $Duration
        $procs += $shooter
    }

    foreach ($p in @($shooter, $other)) {
        if (-not (Wait-ForExit $p ($OtherDur + 60))) { Stop-Proc $p }
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
    Write-Host ("  {0,-30} {1,9:N0} bytes" -f $shot.Name, $shot.Length)
}
Write-Host ""
Write-Host "What the room did, for matching a frame to a moment:" -ForegroundColor White
$log = Join-Path $script:LogDir "sortcap-SortShot.out.log"
if (Test-Path $log) {
    Select-String -Path $log -Pattern '\[(sort|sort-bot|door)\]' |
        ForEach-Object { Write-Host "    $($_.Line)" -ForegroundColor DarkGray }
}
