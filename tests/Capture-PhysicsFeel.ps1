<#
.SYNOPSIS
    PHYS-1 (2026-09-20) -- the three captures Talon asked for, from the holder's own eye.

.DESCRIPTION
    Unregistered and manual, on the SAME udp/7916 as Run-PhysicsFeelTest.ps1 -- the pattern every
    lane since CLOCK-1 has followed (a registered suite beside its own capture harness on one
    port). Both take the machine-wide mutex, so they can never co-exist, and only the registered
    half is in Run-AllTests.ps1.

    Three passes, three fixtures, one windowed first-person bot each, every fixture struck by
    --phys-shove on the server (PHYS-2; the walk it replaced is measured at the $passes block):
      domino   five cereal boxes 2 cm apart in the z = 0 walkway; the row's head is struck at
               2.8 m/s into the other four. Marks are on a 0.5 s grid across the moment of
               contact, because DOOR-1 measured that a 0.1 s grid QUEUES (a viewport capture
               costs ~0.18 s) and reports times that are up to a second wrong.
      pyramid  six cans stacked 3-2-1 at the end of the aisle, and a seventh can on the floor
               that is laid down and rolled into them. This is the fixture Run-PhysicsFeelTest
               does NOT stage: the suite's bars are an angle and a distance, and a pyramid is a
               picture.
      roll     one can on an empty stretch of the z = 0 walkway, laid down, rolled at 1 m/s and
               watched over frames a second apart as it rolls away down the aisle.
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
# PHYS-2 (2026-09-21): THE SHOVE IS THE SERVER'S, NOT A WALK, AND THE CAMERA NEVER MOVES.
# Two runs of this harness measured why, in order:
#   1. PHYS-1's walking holder grabbed the crate at x = 36 and stopped dead at x = 37.74 against
#      Prop_3 (1017), another of CARRY-1's crates in the same walkway -- nine `[phys] wake
#      prop=1017` lines and never within 3 m of the row -- and the marks (4.5-5.5 s) were placed
#      for a contact that would have been at ~9 s even unblocked. Twelve frames, three distinct
#      byte sizes, all of a shelf face.
#   2. Re-staged with --phys-shove and the camera PINNED to SearchSpawn_1 by --spawn-index: the
#      avatar moved (its own samples say 44.5 then 45.9) and the first-person camera did not --
#      every frame was shot from the join-order spawn (35.5, 1.1, 0) looking down the aisle at
#      the bot's own avatar ten metres away. A --fp-look camera does not follow a spawn-pin
#      teleport; Measure-ShopFloor's shots walk with --goto-script and never saw this.
# So the camera stays where join order puts the first peer, SearchSpawn_0 at (35.5, 1.1, 0), told
# to walk to exactly that point so BotHarness never wanders, and the FIXTURE is placed in front of
# it: the z = 0 walkway 3.2-4.5 m east, offset to z = +0.55 (screen right for a camera looking +X).
# The fourth run of this harness had the row at x = 38.0, z = 0.4 and CARRY-1's crate Prop_3 at
# (37.9, 0) hid it: the frames show a yellow crate mid-aisle with one cardboard corner behind it.
# CARRY-1's four crates stand at z = 0 between x = 36 and 38, so the fixtures start a metre past
# the last of them and a further 0.15 m to the right. Yaw -90 looks +X
# (Measure-ShopFloor's ShopEast; a run of this harness at +90 photographed the drop-off bin behind
# the camera); pitch -20 puts a floor-level fixture at 3 m in the centre of a 1.04 m eye.
# The shove clock starts when the server's PropManager starts stepping and --capture-at counts
# from the client's launch, ~0.5 s after the server reports listening, so the two clocks agree to
# about a second and the mark grids are wide enough to absorb that (0.5 s steps; DOOR-1 measured
# that a 0.1 s grid queues behind the ~0.18 s a viewport capture costs).
#   domino   the row's head (prop 1, x = 38.00) struck EAST at 2.8 m/s with a forward pitch of
#            8 rad/s at t = 8 s -- a hand knocking a box over, not a puck sliding into it -- and
#            the other four are woken in turn by the shipped contact path, each by the leading
#            edge of the one before (PropPhysics.StrikePoint).
#   pyramid  the seventh can (prop 7) is SEEDED lying on its side (the fifth seed field, 90
#            degrees about X; spinning an upright can over was measured not to work, see
#            LaunchOptions.SeedTestProps) and rolled EAST at 2 m/s (omega_z = -v/r) at t = 8.5 s
#            into the base of the 3-2-1 stack.
#   roll     one can, seeded on its side, rolled EAST at the packet's 1 m/s at 8.5 s, away from
#            the camera down the open walkway.
# Edge-on (yaw 90) at a pitch of 0.10: the domino geometry Run-PhysicsFeelTest derives and measures.
$DominoSeed = "39.20,0.14,0.55,box,0,90;39.30,0.14,0.55,box,0,90;39.40,0.14,0.55,box,0,90;39.50,0.14,0.55,box,0,90;39.60,0.14,0.55,box,0,90"
# A 3-2-1 pyramid of cans standing on the floor plus the shove can in front of it. Rows are
# 0.075 m apart (a can is 0.07 m across, so they touch); the upper rows sit in the valleys.
$PyramidSeed = ("40.00,0.06,0.475,can;40.00,0.06,0.550,can;40.00,0.06,0.625,can;" +
                "40.00,0.18,0.513,can;40.00,0.18,0.587,can;" +
                "40.00,0.30,0.550,can;" +
                "38.50,0.035,0.55,can,90")
$RollSeed = "38.70,0.035,0.55,can,90"
$CamAt = "35.5,0"
$LookEast = "-90,-13"
$passes = @(
    @{ Name = "domino";  Bot = "DominoCam";  Seed = $DominoSeed;
       Shove = "1,2.8,0,0,8,0,0,-8";
       Marks = "6.5,7,7.5,8,8.5,9,9.5,10,10.5"; Duration = 16 }
    @{ Name = "pyramid"; Bot = "PyramidCam"; Seed = $PyramidSeed;
       Shove = "7,2.0,0,0,8.5,0,0,-57.14";
       Marks = "7.5,8,8.5,9,9.5,10,10.5,11,12"; Duration = 16 }
    @{ Name = "roll";    Bot = "RollCam";    Seed = $RollSeed;
       Shove = "1,1.0,0,0,8.5,0,0,-28.57";
       Marks = "7.5,8,8.5,9,9.5,10,11,12"; Duration = 16 }
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
            "--spawn-room", "search",
            "--seed-test-props", $pass.Seed,
            "--phys-shove", $pass.Shove) "physcap-$name.server"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
            Write-Fail "server never reported listening on udp/$Port; see $serverOut"
        }
        # Windowed, so Start-Godot (which always injects --headless) is bypassed deliberately.
        $botArgs = @(
            "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $pass.Bot,
            "--windowed", "--first-person-cam", "--fp-look", $LookEast,
            "--goto-script", $CamAt,
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
