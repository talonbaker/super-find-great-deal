<#
.SYNOPSIS
    LD-3: the SEEN half of the weenie-chain matrix -- one headed capture per ordered pair of the
    six vantage/target points, from the eye a player has at the best standing spot on that pad.

.DESCRIPTION
    tools/dev/ld3_weenie_chain.py computes which summits are visible from which, by raycast against
    the section files. This photographs the same lines from inside a running session so the two can
    be compared cell by cell in docs/levels/2026-09-02-LD-3-weenie-chain.md. The camera list is NOT
    typed here: it is read from `ld3_weenie_chain.py --cams`, so a moved television or a moved spawn
    ring moves the shot with it and the seen half cannot drift from the computed half.

    Each frame is named `from-<vantage>-to-<target>/LD3-5s.png` under docs/qa/LD-3/: the camera sits
    at eye height (1.10 m) on the vantage's pad -- at whichever of the nine standing spots the
    raycast found clearest -- and looks at the target's own eye point. Default 75-degree FOV, so a
    5 m pad at 120 m is ~60 px wide; the frame answers "is the silhouette there", not "can you read
    the screen". --capture-cam is mandatory: a bot's own follow camera frames the bot.

    ONE server, thirty bots, run one at a time; -From and -To filter the list so a batch fits a
    foreground call. HEADED: needs a GPU and a display, and per .claude/rules/test-suite.md only one
    headed run may be live on this machine at a time. Deliberately NOT in Run-AllTests.ps1.

        powershell -File tools/dev/ld3_capture.ps1                 # all thirty
        powershell -File tools/dev/ld3_capture.ps1 -From spawn     # the five from the spawn ring
        powershell -File tools/dev/ld3_capture.ps1 -Only from-blue-to-tangle
#>
[CmdletBinding()]
param(
    [int]$Port = 46713,
    [string]$OutDir = "",
    [double]$DurationSec = 10,
    [double]$CaptureAtSec = 5,
    [string]$From = "",
    [string]$To = "",
    [string]$Only = ""
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\..\..\tests\_Common.ps1"

if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot "..\..\docs\qa\LD-3" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

# Windowed launcher at a FORCED resolution. Start-Godot forces --headless, which is right for every
# other suite and wrong for a capture; --resolution goes before the -- separator because it is an
# engine flag, not one of ours.
function Start-GodotWindowed([string[]]$UserArgs, [string]$Tag) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $err = Join-Path $script:LogDir "$Tag.err.log"
    $a = @("--path", $script:Root, "--resolution", "1920x1080", "--") + $UserArgs
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList $a `
        -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

$World = "bubbletest"
$Phase = 0.25   # midday: these are sightline shots, not lighting shots

# The shot list, derived -- never typed.
$camsScript = Join-Path $PSScriptRoot "ld3_weenie_chain.py"
$camLines = & python $camsScript --cams
if ($LASTEXITCODE -ne 0 -or -not $camLines) { Write-Fail "ld3_weenie_chain.py --cams produced nothing" }
$shots = @()
foreach ($line in $camLines) {
    $parts = $line.Trim().Split(",")
    if ($parts.Count -ne 7) { continue }
    $tag = $parts[0]
    $shots += @{ Tag = $tag; Cam = ($parts[1..6] -join ",") }
}
if ($Only) { $shots = $shots | Where-Object { $_.Tag -eq $Only } }
if ($From) { $shots = $shots | Where-Object { $_.Tag -like "from-$From-to-*" } }
if ($To)   { $shots = $shots | Where-Object { $_.Tag -like "*-to-$To" } }
if (-not $shots) { Write-Fail "no shot matched the filter" }

Reset-LogDir
Invoke-BuildAndImport

Write-Host ("=== LD-3 captures: {0} shot(s) -> {1} ===" -f @($shots).Count, $OutDir) -ForegroundColor White

$server = $null
try {
    $server = Start-Godot @(
        "--server", "--port", $Port, "--world", $World,
        "--cycle-start-phase", $Phase, "--cycle-start-day", 0,
        "--log-dir", $script:LogDir) "ld3-server"
    if (-not (Wait-ForLogLine (Join-Path $script:LogDir "ld3-server.out.log") "\[server\] listening" 60)) {
        Write-Fail "server never reported listening"
    }

    foreach ($s in $shots) {
        $shotDir = Join-Path $OutDir $s.Tag
        New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
        Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

        Write-Host ("[{0}] cam {1}" -f $s.Tag, $s.Cam) -ForegroundColor Cyan
        $bot = Start-GodotWindowed @(
            "--bot", "--address", "127.0.0.1:$Port", "--name", "LD3",
            "--duration", $DurationSec, "--world", $World,
            "--cycle-start-phase", $Phase, "--cycle-start-day", 0,
            "--capture-dir", $shotDir, "--capture-at", $CaptureAtSec,
            "--capture-cam", $s.Cam) "ld3-bot-$($s.Tag)"
        try {
            if (-not (Wait-ForExit $bot ([int]$DurationSec + 90))) { Write-Fail "the $($s.Tag) bot never exited" }
        }
        finally { if (-not $bot.HasExited) { Stop-Proc $bot } }

        $pngs = Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue
        if (-not $pngs) { Write-Fail "$($s.Tag) produced no PNG -- check ld3-bot-$($s.Tag).out.log" }
        foreach ($p in $pngs) { Write-Host ("        {0}  ({1:N0} bytes)" -f $p.Name, $p.Length) -ForegroundColor Gray }
    }
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Proc $server }
}

Write-Host ""
Write-Host "LD-3 CAPTURES: done." -ForegroundColor Green
exit 0
