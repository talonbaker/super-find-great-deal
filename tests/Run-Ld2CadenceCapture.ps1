<#
.SYNOPSIS
    LD-2: the headed proof that bubbletest's --bt-perf-readout draws the two cadence lines.

.DESCRIPTION
    One dedicated server on bubbletest, one WINDOWED capture bot that sprints from the spawn ring
    down the hub's south connector onto the cyan run (--goto-script 0,70 --goto-sprint), arrives,
    and stands there. Three frames along the way, all in-engine (ViewportCapture, never a screen
    grab), with --bt-perf-readout on so the screen-space label is in every one:

      ~4 s   still on the plaza, about to move           since-act climbing, stop total small
      ~9 s   mid-sprint on the connector / the run        stop total frozen while moving
      ~25 s  arrived at z = 70, standing                  stop total climbing again

    A goto brain neither jumps nor grabs, so since-act mostly reads the bot's whole life — but the
    straight line from the spawn ring to z = 70 passes through a bubble on the connector, and on
    the first run the bot POPPED it: since-act reset to 0 with "last: pop", which is the
    BubbleCounter.Changed hook firing live. Whether it pops again depends on the bubble having
    respawned; the script does not depend on it either way. What the frames prove is that the
    lines exist, update, and sit where a player can read them; the numbers themselves are proved
    by CadenceClockTests on synthetic streams.

    The same numbers reach the bot's stdout once a second on the [bubbletest.perf] line
    (since_act= stop_last_min= stop_total= last_act=), and this script asserts on THAT as well as
    on the PNGs: a frame proves the label drew, the log proves the clock behind it moved.

    --capture-cam is MANDATORY for a bot (it has no follow camera of its own and writes a flat
    frame without one); it is parked beside the cyan run's entry looking down the run, so the
    arrived body is in the third frame.

    HEADED: needs a GPU and a real display; per .claude/rules/test-suite.md only one headed run
    may be live on this machine at a time. Deliberately NOT in Run-AllTests.ps1.

        powershell -File tests/Run-Ld2CadenceCapture.ps1 -OutDir docs/qa/LD-2
#>
[CmdletBinding()]
param(
    [int]$Port = 46712,
    [string]$OutDir = "",
    [double]$DurationSec = 32,
    [double[]]$CaptureAtSec = @(4, 9, 25),
    # x,y,z, tx,ty,tz — beside the cyan run's entry, looking south down the run at the arrival point.
    [string]$Cam = "9,4,52,0,1,70",
    [string]$Target = "0,70"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot "..\docs\qa\LD-2" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
# Logs go under tests/logs (gitignored), never beside the frames: a Godot log carries the
# machine's absolute paths and a capture directory is committed.
$LogDir = Join-Path $script:LogDir "ld2-capture"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$script:LogDir = $LogDir

# Start-Godot forces --headless; a capture needs a window and a renderer. Same shape as
# Run-BodyCapture, with the explicit 1920x1080 so the frame is the size Talon plays at.
function Start-GodotWindowed([string[]]$UserArgs, [string]$Tag) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $err = Join-Path $script:LogDir "$Tag.err.log"
    $a = @("--path", $script:Root, "--resolution", "1920x1080", "--") + $UserArgs
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList $a `
        -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

Write-Host "=== LD-2: the cadence readout, photographed -> $OutDir ===" -ForegroundColor White
Get-ChildItem -Path $OutDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

$procs = @()
try {
    $server = Start-Godot @(
        "--server", "--port", $Port, "--world", "bubbletest",
        "--log-dir", $LogDir) "ld2-server"
    $procs += $server
    $serverOut = Join-Path $LogDir "ld2-server.out.log"
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "server never reported listening"
    }

    $botArgs = @(
        "--bot", "--address", "127.0.0.1:$Port", "--name", "LD2",
        "--duration", $DurationSec, "--world", "bubbletest",
        "--goto-script", $Target, "--goto-sprint",
        "--capture-dir", $OutDir, "--capture-cam", $Cam,
        "--log-dir", $LogDir,
        "--bt-perf-readout")
    foreach ($t in $CaptureAtSec) { $botArgs += @("--capture-at", $t) }

    $bot = Start-GodotWindowed $botArgs "ld2-bot"
    $procs += $bot
    if (-not (Wait-ForExit $bot ([int]$DurationSec + 90))) {
        Write-Fail "the capture bot never exited"
    }
}
finally {
    foreach ($p in $procs) { if (-not $p.HasExited) { Stop-Proc $p } }
}

$pngs = @(Get-ChildItem -Path $OutDir -Filter *.png -ErrorAction SilentlyContinue)
if ($pngs.Count -lt $CaptureAtSec.Count) {
    Write-Fail "expected $($CaptureAtSec.Count) PNGs, got $($pngs.Count) -- check tests/logs/ld2-capture/ld2-bot.out.log"
}
foreach ($p in $pngs) { Write-Host ("        {0}  ({1:N0} bytes)" -f $p.Name, $p.Length) -ForegroundColor Gray }

# The clock behind the label: the once-a-second console line must carry the cadence fields, and
# stop_total must have MOVED between the first line and the last (the bot stands at both ends).
$botOut = Join-Path $LogDir "ld2-bot.out.log"
$perf = @(Select-String -Path $botOut -Pattern "\[bubbletest\.perf\] .*since_act=([0-9.]+) stop_last_min=([0-9.]+) stop_total=([0-9.]+) last_act=(\S+)")
if ($perf.Count -lt 5) { Write-Fail "fewer than 5 [bubbletest.perf] lines with cadence fields in $botOut ($($perf.Count))" }
$first = [double]$perf[0].Matches[0].Groups[3].Value
$last  = [double]$perf[-1].Matches[0].Groups[3].Value
$sincePrev = [double]$perf[-2].Matches[0].Groups[1].Value
$sinceLast = [double]$perf[-1].Matches[0].Groups[1].Value
$lastAct = $perf[-1].Matches[0].Groups[4].Value
Write-Host ("        perf lines: {0}; stop_total first={1} last={2}; since_act last={3} (last_act={4})" -f $perf.Count, $first, $last, $sinceLast, $lastAct) -ForegroundColor Gray
if ($last -le $first) { Write-Fail "stop_total did not grow across the run ($first -> $last): the clock is not being fed" }
# The bot stands still at the end, so since-act must be COUNTING between the last two lines
# (~1 s apart) whatever reset it earlier — a stuck clock reads the same number twice.
if ($sinceLast -le $sincePrev) { Write-Fail "since_act did not advance across the last two lines ($sincePrev -> $sinceLast): the clock is stuck" }

Write-Host "PASS: LD-2 cadence capture" -ForegroundColor Green
exit 0
