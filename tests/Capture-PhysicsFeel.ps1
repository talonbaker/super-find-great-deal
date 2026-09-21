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
# THE FIFTH RUN OF THIS HARNESS PHOTOGRAPHED THE PHYSICS AND IT WAS STILL UNREADABLE, for two
# reasons that are worth more than the frames (INT-2B, measured 2026-09-21, from the v5 run's own
# JSONL and a pixel diff of its PNGs):
#   1. THE CAPTURE CLOCK IS NOT THE PROP CLOCK, and they are ~3.15 s apart. --capture-at fires on
#      BotHarness._elapsed, which starts when the harness enters _Process (LaunchOptions:
#      "the bot's own seconds since entering"); the JSONL's t is Time.GetTicksMsec(), from process
#      start. In v5 the bot's first sample was t=3154 ms, so mark 6.5 s was prop-clock 9.65 s.
#      The row's five boxes went from 0.0 deg (t=7.68 s) to 66/66/66/68/90 deg (t=9.21 s) -- so
#      EVERY ONE of the nine marks landed after the row had already settled, and all nine frames
#      were byte-for-byte the same picture. A mark grid computed against a server-clock shove
#      must have the connect-and-load time subtracted, or be wide enough to not care.
#   2. A FIXTURE 3.7-4.1 m AWAY IS ABOUT 25 PIXELS. Diffing DominoCam-7s against -9.5s changed
#      2426 px and every one of them was the highlighted deal can on the right shelf (x 902-951);
#      the region the row projects into changed by EXACTLY ZERO. The yellow crate that dominates
#      those frames is CARRY-1's Prop_3 (1017) at (38, 0.22, 0), 2.5 m out.
# So the fixtures move to 1.4-2.1 m and the mark grid is both earlier and wider.
# CARRY-1's crates are the constraint: 1014 at (36, 0.22, 0) and 1017 at (38, 0.22, 0), each a
# 0.44 m cube at z = 0, which leaves a clear band at x in (36.22, 37.78) on the camera's axis.
# AND THE DOMINO ROW NOW FALLS ACROSS THE VIEW, NOT AWAY FROM IT. A row laid along X in front of
# a camera looking down X is seen end-on -- the nearest box hides the other four and the topple
# is straight away from the lens, which is the one direction a chain cannot be read in. The row
# is laid along Z and shoved along +Z (screen right) instead: same geometry, same bars, broadside.
# At yaw 0 a box's 0.06 m depth lies along Z, so yaw 0 is edge-on to a +Z shove exactly as yaw 90
# is to a +X one. The topple spin about X: w = +8 rad/s carries the top toward +Z (X_hat cross
# Y_hat = +Z_hat), mirroring the -8 about Z that PHYS-2 measured for a +X shove.
# Eye height is 1.06 m, solved from the v5 frames rather than assumed: crate 1017's centre lands
# 46 px below centre at pitch -13 and 2.5 m, which puts the eye at 1.06. Each pitch below aims at
# its own fixture's centre.
$DominoSeed = "37.20,0.14,-0.25,box;37.20,0.14,-0.15,box;37.20,0.14,-0.05,box;37.20,0.14,0.05,box;37.20,0.14,0.15,box"
# A 3-2-1 pyramid of cans standing on the floor plus the shove can in front of it. Rows are
# 0.075 m apart (a can is 0.07 m across, so they touch); the upper rows sit in the valleys.
# x = 37.40 with the shove can at 36.60: both inside the clear band between CARRY-1's two crates,
# with 0.8 m of run-up, which is over the 0.5 m the can needs to be rolling before it arrives.
$PyramidSeed = ("37.40,0.06,-0.075,can;37.40,0.06,0.000,can;37.40,0.06,0.075,can;" +
                "37.40,0.18,-0.037,can;37.40,0.18,0.037,can;" +
                "37.40,0.30,0.000,can;" +
                "36.60,0.035,0.000,can,90")
# The roll starts 1.1 m from the lens and travels AWAY down the aisle, offset to z = +0.55 so it
# passes CARRY-1's crate 1017 (z +/- 0.22) instead of stopping against it.
$RollSeed = "36.60,0.035,0.55,can,90"
$CamAt = "35.5,0"
# Marks: earlier by the ~3.15 s of finding 1 AND widened, so the grid straddles the fall whether
# the connect-and-load offset is that or zero. 0.5 s steps (DOOR-1: a 0.1 s grid queues).
$Marks = "3,3.5,4,4.5,5,5.5,6,6.5,7,8,9"
$passes = @(
    @{ Name = "domino";  Bot = "DominoCam";  Seed = $DominoSeed;  Look = "-90,-28";
       Shove = "1,0,0,2.8,8,8,0,0";
       Marks = $Marks; Duration = 16 }
    @{ Name = "pyramid"; Bot = "PyramidCam"; Seed = $PyramidSeed; Look = "-90,-25";
       Shove = "7,2.0,0,0,8.5,0,0,-57.14";
       Marks = $Marks; Duration = 16 }
    @{ Name = "roll";    Bot = "RollCam";    Seed = $RollSeed;    Look = "-90,-24";
       Shove = "1,1.0,0,0,8.5,0,0,-28.57";
       Marks = $Marks; Duration = 16 }
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
        # --gpu-index 1 IS LOAD-BEARING FOR THE CAPTURE GRID, and it is STALL-1's finding spent on
        # this harness (INT-2B, 2026-09-21). Godot picks device #0, the RTX 4070, which has NO
        # DISPLAY ATTACHED on this machine; every frame is then copied across to the Intel UHD 770
        # that owns the screen, and a windowed client runs at about 4 fps with a ~0.5 s stall once
        # a second (STALL-1 table rows 2 vs 3: 255.851 ms -> 2.837 ms median, 269 stalls -> 0).
        # Measured consequence HERE, by correlating each PNG's mtime against the bot's own JSONL
        # clock: a 0.5 s mark grid came out 1.5-2.0 s apart and the FIRST mark of the run, typed
        # 3 s, actually fired at prop-clock 10.93 s. The row's whole fall takes 1.5 s (tilt 0 deg
        # at t=7.62 s, 57 deg at 8.38, 66.9 deg at 9.14), so it fell BETWEEN two marks and all
        # eleven frames were the settled pile -- which is what the fourth and fifth runs of this
        # harness were really photographing, and why their frames were identical to the byte.
        # An ENGINE flag, so it goes before the bare -- (the rule this file's header already
        # states). The server stays headless and is unaffected.
        $botArgs = @(
            "--gpu-index", "1",
            "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $pass.Bot,
            "--windowed", "--first-person-cam", "--fp-look", $pass.Look,
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
