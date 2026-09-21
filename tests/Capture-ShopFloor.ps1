<#
.SYNOPSIS
    STOCK-1: what the filled shop floor looks like. Unregistered, manual, asserts nothing.

.DESCRIPTION
    Windowed host plus windowed first-person clients standing where Talon will stand. It shares
    udp/7911 with tests/Run-StockTest.ps1, which is the established pattern (CLOCK-1's
    Capture-RoundClock beside Run-RoundClockTest): both take the machine-wide mutex, so the two
    can never be alive at the same moment, and only the registered half is in Run-AllTests.ps1.

    NEEDS A DESKTOP SESSION. ActorFx and every renderer are gated on DisplayServer.GetName(), and
    a headless peer photographs nothing -- see .claude/rules/test-suite.md.

    THE POSES, and why each one. All coordinates are WORLD: SearchRoom.tscn is instanced at
    x = +40, so the interior is x in [33, 47], z in [-5, 5]. Aisle centres are z = -3.15, -1.05,
    +1.05, +3.15 and the walkways between them are z = -4.2, -2.1, 0, +2.1, +4.2.

      aisle        the walkway between aisles 0 and 1, looking down its whole 9.2 m length. This
                   is the shot that answers Talon's ask, and it is the one the BEFORE pass
                   repeats from the identical pose.
      endcap       the +X cross-aisle, facing EndCap_0 from 1.15 m.
      pile         a floor bin from 1.2 m with the lens tipped 25 degrees down, into the oranges.
      gap-with-can a hole with a near-miss can standing in it, from the walkway -- the shot that
                   shows the hiding place rather than the density.

.PARAMETER Pass
    'after' (the default), 'before' (the base tree's scene files, restored afterwards), or a
    single pose name.

.PARAMETER OutDir
    Where the PNGs land. Default docs/qa/2026-09-20-stock-1.
#>
[CmdletBinding()]
param(
    [ValidateSet("after", "before", "aisle", "endcap", "pile", "gap")][string]$Pass = "after",
    [string]$OutDir = "",
    [int]$Port = 7911,
    [int]$MutexTimeoutMinutes = 120,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if ($OutDir -eq "") { $OutDir = Join-Path $script:Root "docs/qa/2026-09-20-stock-1" }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

Write-Host "=== STOCK-1 captures (pass: $Pass) -> $OutDir ===" -ForegroundColor White

# name, goto (x,z), fp-look (yaw[,pitch]), capture marks
# yaw 0 faces -Z; +90 faces -X; -90 faces +X; 180 faces +Z.
$Poses = @{
    aisle  = @{ Goto = "35.6,-2.1"; Look = "-90,-4"; Marks = "10,13,16" }
    endcap = @{ Goto = "46.0,-1.05"; Look = "90,-6";  Marks = "10,13,16" }
    # Bin_4 at world (46.4, 0), in the +X cross-aisle. The first cut aimed at Bin_1 in the -X
    # cross-aisle and BTN-1's DROP-OFF BIN stands at (33.8, 0) between the camera and it -- a
    # dark tub with no mound in it by design, filling the frame. Aim at a bin nothing is parked
    # in front of.
    # THE BIN SHOT USES A FIXED CAMERA, and it took two failed poses to earn that.
    #
    # A bin's rim is 0.60 m and the avatar's eye is 1.040 m, so from any distance a walking bot
    # can stop at, the near wall occludes the fruit. The first attempt aimed at Bin_1 in the -X
    # cross-aisle and photographed BTN-1's DROP-OFF BIN instead (a dark tub at (33.8, 0) with no
    # mound in it BY DESIGN) filling the frame -- which read exactly like a mound that was not
    # rendering. It renders: StockSelfTest now asserts all six bins carry 174 produce instances
    # between them, which is the check that settled it. The second attempt aimed along the z = 0
    # walkway and the bot was stopped by CARRY-1's crate Prop_3 at (38, 0) -- SHELF-1 S6's rule
    # about this room, for a third time.
    #
    # --capture-cam takes the walk out of the picture: half a metre from Bin_4 at (46.4, 0) and
    # three quarters of a metre up, looking down into it. CELEBRATE-1's rule still holds -- a
    # capture bot must be GIVEN a camera -- and this is the other flag that gives it one.
    pile   = @{ Cam = "45.85,1.12,0.0,46.4,0.34,0.0"; Marks = "10,13,16" }
    gap    = @{ Goto = "35.6,-2.1";  Look = "-90,-2"; Marks = "10,13,16" }
}

function Start-CaptureHost([string]$Tag, [string[]]$Extra) {
    $out = Join-Path $script:LogDir "$Tag.host.out.log"
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList (@(
        "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
        "--windowed", "--perf-log", (Join-Path $script:LogDir "$Tag.perf.jsonl")) + $Extra) `
        -RedirectStandardOutput $out `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.host.err.log") `
        -PassThru
    $null = $p.Handle
    return $p
}

function Start-Shot([string]$Tag, [string]$Name, [string]$Goto, [string]$Look, [string]$Marks, [int]$Dur, [string]$Cam) {
    # Engine flags before the bare --, game flags after. Wrong side is swallowed and looks
    # exactly like a hang (.claude/rules/test-suite.md).
    #
    # A pose gives EITHER a walk plus a first-person lens, or a FIXED camera. --capture-cam wins
    # over --first-person-cam when both are present (BotHarness calls MakeCurrent after the
    # avatar attaches), so they are never passed together.
    if ($Cam) {
        $lens = @("--capture-cam", $Cam)
    } else {
        $lens = @("--first-person-cam", "--goto-script", $Goto, "--fp-look", $Look)
    }
    $argv = @("--path", $script:Root, "--",
        "--bot", "--address", "127.0.0.1:$Port", "--name", $Name, "--windowed") + $lens + @(
        "--capture-dir", $OutDir, "--capture-at", $Marks,
        "--world", "supermarket", "--duration", $Dur,
        # THE DRAW-CALL READING HAS TO COME FROM THE PEER WITH A CAMERA. The host gets
        # --perf-log too and reports 6 draw calls and 806 primitives from every pose -- because a
        # --server peer has no avatar and no camera, so its viewport draws the HUD and the clear
        # colour and nothing else. That number looks like a wonderful result and is a photograph
        # of a UI. PerfHud only exists on a non-headless peer (Gameplay gates it on IsHeadless),
        # which is why this is the windowed CLIENT.
        "--perf-log", (Join-Path $script:LogDir "$Tag.client.perf.jsonl"),
        "--log", (Join-Path $script:LogDir "$Tag.jsonl"))
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList $argv `
        -RedirectStandardOutput (Join-Path $script:LogDir "$Tag.out.log") `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru
    $null = $p.Handle
    return $p
}

# The BEFORE pass swaps in the base tree's two scene files, shoots, and swaps back. A file swap
# inside THIS worktree, never `git stash` -- the stash stack is shared with every other worktree
# on this machine and another session's `git stash -u` has eaten a lane's work before.
$BaseSha = "c0b024a"
$Swapped = @("scenes/game/world/supermarket/SearchRoom.tscn", "scenes/game/props/FloorBin.tscn")

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
$restored = $false
try {
    if (-not $SkipBuild) {
        if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    $which = @()
    switch ($Pass) {
        "after"  { $which = @("aisle", "endcap", "pile", "gap") }
        "before" { $which = @("aisle") }
        default  { $which = @($Pass) }
    }

    if ($Pass -eq "before") {
        Write-Host "  swapping in the base tree's scene files ($BaseSha)..." -ForegroundColor Yellow
        & git -C $script:Root checkout $BaseSha -- $Swapped
        if ($LASTEXITCODE -ne 0) { Write-Fail "could not check out the base scene files" }
        & $script:GodotExe --headless --path $script:Root --import | Out-Null
    }

    foreach ($pose in $which) {
        $cfg = $Poses[$pose]
        $prefix = if ($Pass -eq "before") { "$pose-before" } else { $pose }
        Write-Host ""
        if ($cfg.Cam) { $how = "cam=$($cfg.Cam)" } else { $how = "goto=$($cfg.Goto) look=$($cfg.Look)" }
        Write-Host ">>> $prefix  $how" -ForegroundColor Cyan

        $h = Start-CaptureHost "shotcap-$prefix" @()
        $procs += $h
        if (-not (Wait-ForLogLine (Join-Path $script:LogDir "shotcap-$prefix.host.out.log") "\[server\] listening" 120)) {
            Stop-Proc $h
            Write-Fail "the capture host never reported listening on udp/$Port"
        }
        $shot = Start-Shot "shotcap-$prefix" $prefix $cfg.Goto $cfg.Look $cfg.Marks 22 $cfg.Cam
        $procs += $shot
        if (-not (Wait-ForExit $shot 120)) { Stop-Proc $shot }
        Stop-Proc $h
        Start-Sleep -Milliseconds 800
    }
} finally {
    Stop-Procs $procs
    if ($Pass -eq "before") {
        # Restore unconditionally, including on a hard fail. A worktree left holding the base
        # tree's level files is a lane that silently gates the wrong build.
        & git -C $script:Root checkout HEAD -- $Swapped
        & $script:GodotExe --headless --path $script:Root --import | Out-Null
        $restored = $true
    }
    Exit-SuiteMutex $mutex
}

if ($Pass -eq "before" -and -not $restored) {
    Write-Host "WARNING: the base scene files may still be checked out. Run: git checkout HEAD -- $($Swapped -join ' ')" -ForegroundColor Red
}

Write-Host ""
Write-Host "Frames in $OutDir :" -ForegroundColor White
foreach ($f in @(Get-ChildItem -Path $OutDir -Filter "*.png" -ErrorAction SilentlyContinue | Sort-Object Name)) {
    # FP-1's lesson: compare a capture's SIZE to one known good before believing it. A few
    # hundred bytes, or 14 KB against a known 69 KB, is a photograph of a wall.
    Write-Host ("  {0,-34} {1,9:N0} bytes" -f $f.Name, $f.Length)
}
Write-Host ""
Write-Host "CAPTURE-SHOPFLOOR: done (this script asserts nothing)." -ForegroundColor Green
exit 0
