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
    pile   = @{ Goto = "34.9,1.05";  Look = "90,-28"; Marks = "10,13,16" }
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

function Start-Shot([string]$Tag, [string]$Name, [string]$Goto, [string]$Look, [string]$Marks, [int]$Dur) {
    # Engine flags before the bare --, game flags after. Wrong side is swallowed and looks
    # exactly like a hang (.claude/rules/test-suite.md).
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--path", $script:Root, "--",
        "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
        "--windowed", "--first-person-cam",
        "--goto-script", $Goto, "--fp-look", $Look,
        "--capture-dir", $OutDir, "--capture-at", $Marks,
        "--world", "supermarket", "--duration", $Dur,
        "--log", (Join-Path $script:LogDir "$Tag.jsonl")) `
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
        Write-Host ">>> $prefix  goto=$($cfg.Goto) look=$($cfg.Look)" -ForegroundColor Cyan

        $h = Start-CaptureHost "shotcap-$prefix" @()
        $procs += $h
        if (-not (Wait-ForLogLine (Join-Path $script:LogDir "shotcap-$prefix.host.out.log") "\[server\] listening" 120)) {
            Stop-Proc $h
            Write-Fail "the capture host never reported listening on udp/$Port"
        }
        $shot = Start-Shot "shotcap-$prefix" $prefix $cfg.Goto $cfg.Look $cfg.Marks 22
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
