<#
.SYNOPSIS
    PHYS-1 (2026-09-20) -- the three captures Talon asked for, from the holder's own eye.

.DESCRIPTION
    Unregistered and manual, on the SAME udp/7916 as Run-PhysicsFeelTest.ps1 -- the pattern every
    lane since CLOCK-1 has followed (a registered suite beside its own capture harness on one
    port). Both take the machine-wide mutex, so they can never co-exist, and only the registered
    half is in Run-AllTests.ps1.

    Three passes, three fixtures, one windowed first-person bot each:

      domino   five cereal boxes 2 cm apart in the z = 0 walkway; the bot carries CARRY-1's crate
               straight down its own walkway into them. Marks are on a 0.25 s grid across the
               moment of contact, because DOOR-1 measured that a 0.1 s grid QUEUES (a viewport
               capture costs ~0.18 s) and reports times that are up to a second wrong.
      pyramid  six cans stacked 3-2-1 at the end of the aisle, and a seventh can on the floor
               that the bot shoves into them. This is the fixture Run-PhysicsFeelTest does NOT
               stage: the suite's bars are an angle and a distance, and a pyramid is a picture.
      roll     one can on an empty stretch of the z = 0 walkway, shoved and then watched over
               three frames a second apart as it rolls away down the aisle.

    EACH PASS HAS ITS OWN BOT NAME, and that is not cosmetic: BotHarness names a frame
    <stem>-<mark>s.png with the stem defaulting to --name, so three passes sharing one name and
    overlapping mark grids would silently overwrite each other's frames.

    A --bot builds no camera of its own (CELEBRATE-1). --first-person-cam gives it the real
    FirstPersonCamera on its own avatar, which is the lens this packet's captures have to be shot
    through: the whole subject is what the person who knocked the stack over sees.

.NOTES
    Pure ASCII (PowerShell 5.1 reads a BOM-less file as ANSI; see the rules file's REACH-1 entry).
    Windowed, so it needs a desktop session -- and a windowed peer must bypass Start-Godot, which
    always injects --headless.
#>
[CmdletBinding()]
param(
    [int]$Port = 7916,
    [string]$OutDir = "",
    [int]$MutexTimeoutMinutes = 120,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if ($OutDir -eq "") { $OutDir = Join-Path $script:Root "docs/qa/2026-09-20-phys-1" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Fixtures. Ids are assigned from 1 in seed order by --seed-test-props.
$DominoSeed = "41.00,0.14,0,box;41.21,0.14,0,box;41.42,0.14,0,box;41.63,0.14,0,box;41.84,0.14,0,box"
# A 3-2-1 pyramid of cans standing on the floor plus the shove can in front of it. Rows are
# 0.075 m apart (a can is 0.07 m across, so they touch); the upper rows sit in the valleys.
$PyramidSeed = ("41.00,0.06,-0.075,can;41.00,0.06,0.000,can;41.00,0.06,0.075,can;" +
                "41.00,0.18,-0.037,can;41.00,0.18,0.037,can;" +
                "41.00,0.30,0.000,can;" +
                "39.50,0.06,0,can")
$RollSeed = "41.50,0.06,0,can"

$passes = @(
    @{ Name = "domino";  Bot = "DominoCam";  Seed = $DominoSeed;  WalkTo = "43.0,0"; Marks = "4.5,4.75,5,5.25,5.5"; Duration = 16 }
    @{ Name = "pyramid"; Bot = "PyramidCam"; Seed = $PyramidSeed; WalkTo = "42.5,0"; Marks = "4.5,5,5.5,6.5";       Duration = 16 }
    @{ Name = "roll";    Bot = "RollCam";    Seed = $RollSeed;    WalkTo = "43.5,0"; Marks = "6,7,8";               Duration = 16 }
)

Write-Host "=== physics-feel captures -> $OutDir ===" -ForegroundColor White
$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    foreach ($pass in $passes) {
        $name = $pass.Name
        Write-Host "--- $name ---" -ForegroundColor Cyan
        $serverOut = Join-Path $script:LogDir "physcap-$name.server.out.log"
        $server = Start-Godot @("--server", "--port", $Port, "--world", "supermarket",
            "--spawn-room", "search", "--spawn-index", "$($pass.Bot)=0",
            "--seed-test-props", $pass.Seed) "physcap-$name.server"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
            Write-Fail "server never reported listening on udp/$Port; see $serverOut"
        }
        $crate = Get-AuthoredPropId $serverOut "SearchRoom/Prop_0"

        # Windowed, so Start-Godot (which always injects --headless) is bypassed deliberately.
        $botArgs = @(
            "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $pass.Bot,
            "--windowed", "--first-person-cam",
            "--carry-script", "36,0.22,0,1.0,-1", "--carry-grab-retry", "0.6",
            "--carry-target-prop", $crate,
            "--carry-walk-to", $pass.WalkTo,
            "--capture-dir", $OutDir, "--capture-at", $pass.Marks,
            "--world", "supermarket", "--duration", $pass.Duration,
            "--log", (Join-Path $script:LogDir "physcap-$name.jsonl"))
        $bot = Start-Process -FilePath $script:GodotExe -ArgumentList $botArgs `
            -RedirectStandardOutput (Join-Path $script:LogDir "physcap-$name.bot.out.log") `
            -RedirectStandardError (Join-Path $script:LogDir "physcap-$name.bot.err.log") `
            -PassThru
        $null = $bot.Handle
        $procs += $bot
        if (-not $bot.WaitForExit(($pass.Duration + 90) * 1000)) {
            Write-Host "  $name bot did not exit in time" -ForegroundColor Yellow
        }
        Stop-Proc $server
        Start-Sleep -Milliseconds 700
    }
} finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

# CELEBRATE-1's cheapest check, and the reason it is printed rather than assumed: a frame two
# orders of magnitude smaller than its siblings is a photograph of a wall, or of nothing at all.
Write-Host ""
Write-Host "frames written:" -ForegroundColor White
Get-ChildItem -Path $OutDir -Filter "*Cam-*.png" | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-28} {1,8} bytes  {2}" -f $_.Name, $_.Length, $_.LastWriteTime.ToString("HH:mm:ss"))
}
