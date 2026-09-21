<#
.SYNOPSIS
    PHYS-1 (2026-09-20) -- dominoes fall, cans roll, nothing freaks out.

.DESCRIPTION
    Talon's verdict after his first ride, which is this suite's whole specification:

        "I want to manipulate objects and know they cannot clip through walls, the floor, or
         other objects. I want to know they won't freak out and make other objects jump around
         randomly. Make it do what the player expects: if they're placing an object on a shelf
         and accidentally hit a bunch of boxes, those boxes should fall over like dominoes, and
         cans should roll around."

    PHYS-2 (2026-09-20) CHANGED THE MOVER AND NOTHING ELSE. The row and the can are struck by
    --phys-shove at a speed this file chooses, instead of by a walking bot whose approach PHYS-1
    measured at 0.66, 0.70, 0.89, 1.18, 2.88 and 3.30 m/s across six runs of one build. Every bar
    below is unchanged except (1), whose window now ends when the shoved box first moves rather
    than at the bot's grab -- see the bar for why that is not circular.

    ONE server and TWO bots on udp/7916 -- the port the orchestrator ASSIGNED for this wave
    (.claude/rules/test-suite.md, FEEL-1 section: "PORTS ARE ASSIGNED BY THE ORCHESTRATOR IN THE
    DISPATCH. A LANE NEVER COMPUTES NEXT FREE."). One pass of one carried crate produces every
    bar below, which is deliberate: the bars are about one continuous physical event and staging
    them separately would let a stack be knocked over by a fixture rather than by a carry.

    THE FIXTURE. Five cereal boxes standing in a row 2 cm apart in the z = -2 walkway, and a can
    on the floor past them. Seeded props are born RESTING -- frozen kinematic on every peer --
    which is precisely the state P1 is about: before this packet a held crate driven into them
    was stopped by an immovable wall of boxes. The head of the row and the can are each given one
    known velocity by --phys-shove; every box after the first is woken by the SHIPPED contact
    path, which is why the [phys] wake count is still the evidence that P1 ran.

    THE BARS, in the order the phases measure them:
      (1) UNTOUCHED IS UNTOUCHED     no seeded prop the suite did not shove moves at all in the
                                     whole window before the row is struck.
      (2) DOMINOES                   every box tilts past 60 degrees, within 2 s of the first,
                                     on the holder's view AND on a witness's.
      (3) THE CAN ROLLS              travels at least 1 m and stops within 4 m.
      (4) NO FREAKOUT                no unheld prop exceeds MaxPropSpeed; at most 2 [phys] clamp
                                     lines; zero last-good restores; zero props out of bounds.
      (5) EVERYTHING SETTLES         every woken prop is back at Resting by the end of the run --
                                     the honest half of "stacks are stable", since rest in this
                                     build is a LATCH and a frozen body is trivially stable.
      (6) A THROW IS CONTAINED       the crate thrown at the end never leaves the room envelope
                                     and never ends up under the floor.

    Every tilt is computed from the prop quaternion BotHarness already logs (up.y = 1 - 2(x^2+z^2)),
    so this suite needs no new instrumentation on the game side.

.NOTES
    Pure ASCII, by the rules file's REACH-1 entry (PowerShell 5.1 reads a BOM-less file as ANSI
    and mojibake produces a parser cascade 200 lines from anything that is wrong).
#>
[CmdletBinding()]
param(
    [int]$Port = 7916,
    [double]$DurationSec = 70,
    [double]$MaxPropSpeed = 3.0,
    [double]$TiltDeg = 60,
    [double]$DominoWindowSec = 2.0,
    [double]$MinRollM = 1.0,
    [double]$MaxRollM = 4.0,
    # THE SHOVE IS THE SUITE'S, AND PHYS-1 MEASURED WHY (PHYS-2, 2026-09-20). Staged by walking a
    # bot into the row, this fixture was handed peak approach speeds of 0.66, 0.70, 0.89, 1.18,
    # 2.88 and 3.30 m/s across six runs of ONE build -- the debris the bot made perturbed its own
    # walk -- and the chain came out 2/5, 3/5, 4/5 and 5/5 on that unchanged build. Every other
    # bar was stable to two decimals over the same runs, which is what identifies the shove as the
    # variable. --phys-shove hands PropManager.ServerNudgeLoose a number this file chooses.
    #
    # 2.8 m/s is under P2's 3.0 bar on purpose: a fixture that had to be CLAMPED to be staged
    # would be measuring the clamp. The can's 1.0 m/s is the packet's own wording -- "a can nudged
    # at 1 m/s on the floor travels at least 1 m".
    [double]$RowShoveMps = 2.8,
    [double]$CanShoveMps = 1.0,
    # THE HEAD OF THE ROW IS KNOCKED OVER, NOT SLID (PHYS-2, 2026-09-21, measured). A box given
    # 2.8 m/s and no spin slides 2 cm into its frozen neighbour and passes the velocity on as a
    # slide: the funnel woke 2, 3 and 4 at 1.66, 0.99 and 0.34 m/s and not one tilted, because a
    # box hit through its centre translates. A hand that knocks a box hits it high, and the
    # number for that is a forward pitch: -8 rad/s about Z tips the top toward +X (the row runs
    # east) and its top-front edge is then what strikes the next box -- see
    # PropPhysics.StrikePoint for where the impulse lands from there on.
    [double]$RowTopplePitchRadPerSec = -8.0,
    # THE CAN IS SEEDED ON ITS SIDE, AND THAT IS PHYSICS RATHER THAN CONVENIENCE. A can standing
    # on its end does not roll when you push it: tipping needs friction > r/h_com = 0.035/0.06 =
    # 0.58 and tin's is 0.25, so it SKIDS -- mu*g = 2.45 m/s^2, i.e. 0.20 m from 1 m/s against a
    # 1 m bar. PHYS-2 first tried laying it down with a spin about +X and measured that fail for
    # the same reason (the base skids out from under the spin; tilt peaked at 1 degree, three
    # runs), so --seed-test-props grew a fifth field: degrees of roll about X. 90 puts the can's
    # axis along Z, which is the orientation a can rolling along X has to have.
    [double]$CanRollXDeg = 90,
    # On the SERVER's clock (the same one --seed-props-drop uses), which leads every bot's by the
    # connect delay. Nothing below correlates the two: see bar (1).
    [double]$RowShoveAtSec = 40,
    [double]$CanShoveAtSec = 48,
    # How long the row must stand still before the suite strikes it, in the BOT's own clock. The
    # packet asks for thirty seconds; the schedule above buys between twenty-five and thirty-five
    # depending on how long the bots take to connect, and this is the floor under that.
    [double]$UntouchedBarSec = 15,
    [int]$MaxClampLines = 10,
    # Twice the bar: past this a clamp is catching a solver explosion, not trimming an excursion.
    [double]$MaxTrimFromMps = 6.0,
    # Faster than any prop can travel: past this a per-sample jump is a teleport, not a speed.
    [double]$TeleportSpeedMps = 12.0,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# THE ROW STANDS EDGE-ON, LIKE DOMINOES DO -- PHYS-2 (2026-09-21), and every number here was
# measured before it was typed. A cereal box is 0.19 wide x 0.28 tall x 0.06 deep with its centre
# of mass 0.21 m up (CerealBox.tscn). Struck across its 0.19 m WIDTH it has to tip 24 degrees
# (atan(0.095/0.21)) before it is committed, and the row is then:
#   at 2 cm of air   a stack of books -- struck at 2.8 m/s it slid 8/6/4/2 cm as a block and
#                    nothing tilted (three runs). PHYS-1's 5/5 was two avatars walking THROUGH it.
#   at 11 cm of air  a lean that stalls -- box 1 reached 52 degrees, box 2 23, box 3 7, and all
#                    three came to rest leaning on the next and froze there (33 / 20 / 4). A
#                    stubby block hands its neighbour a lean, not a fall.
# Struck across its 0.06 m DEPTH the same box is committed at 8 degrees, which is a domino: so the
# boxes are seeded with their broad face across the row (the sixth seed field, yaw 90) at a pitch
# of 0.10 m -- 4 cm of air, and a box falling through 8 degrees has swung its top corner 0.04 m,
# so each one is committed before it touches the next and touches it high.
$BoxPitch = 0.10
$BoxX0 = 42.30
$BoxIds = 1..5                 # --seed-test-props assigns ids from 1 in seed order
$RollCanId = 6
$ShovedBoxId = 1               # the head of the row: the one prop the SUITE moves

# THE ROW STANDS ON THE FLOOR AND THE MOVER IS A SHOVE THIS FILE CHOOSES (PHYS-2). The history
# below is PHYS-1's and is kept because each line of it is a fixture that was tried and measured;
# what changed is only the last step. A bot walking into the row was the third mover tried, it
# worked, and then it turned out not to be REPEATABLE (see $RowShoveMps above). The row and the
# can are now struck by --phys-shove at a fixed speed, and PhysBot's carry stays in the run for
# bars (1) and (6) -- it stands beside its crate 6 m west of the row and never reaches it.
#
# What the suite gives up by doing this, stated rather than smuggled: the HELD-CRATE and AVATAR
# arms of P1's wake no longer appear in these bars. They are covered by PropPhysicsTests branch
# by branch, and PHYS-1's runs 4-7 measured both live (4/5 and 5/5 with the bot's own capsule,
# recorded in its handoff section 6). What these bars now measure is the part that was never repeatable:
# a known shove in, a five-box chain and a rolling can out.
#
# The three earlier fixtures, each of which measured why it could not work:
#
#  run 1/2  Row on the floor, knocked by the CARRIED CRATE. It never touched them: FEEL-1 holds a
#           prop on the VIEW RAY and a bot has no pitch, so a carried crate rides at y 0.45-0.89
#           and passes clean OVER a 0.28 m cereal box. "NONE of the 5 boxes tilted" was the suite
#           correctly reporting that nothing had hit anything.
#  run 3    Row hung at shelf height (0.62) so the crate WOULD reach it. It did -- four of the
#           five were knocked off and the hold broke on the fifth, which is FEEL-1's break rule
#           working. But they fell 0.62 m and LANDED UPRIGHT (y 0.13, standing), because a row
#           with nothing under it cannot domino: a box knocked off a shelf falls straight down
#           instead of toppling into its neighbour.
#
#  runs 4-7 Row on the floor, knocked by the BOT'S OWN CAPSULE walking through it. This works --
#           4/5 and 5/5 measured, on the holder's view and a witness's -- and it is not
#           repeatable: see $RowShoveMps. The capsule is still the honest player-side mover and
#           its evidence is PHYS-1's, not this file's, any more.
#
# A domino chain needs the row to stand ON something, so the first box goes OVER and its top
# strikes the next. That is the floor. The head of the row is now shoved east along it at a fixed
# speed, and every box after it is woken by the SHIPPED contact path (P1) exactly as a player's
# shove would wake it -- which is why the `[phys] wake` count is still the evidence that P1 ran.
$RowY = 0.14                  # a cereal box's half-height: standing on the floor
$CratePropPath = "SearchRoom/Prop_2"    # CARRY-1's crate at world (36, 0.22, -2)
$RowZ = -2.0
# EAST OF THE SPAWN, and run 4 is why. PhysBot is pinned to SearchSpawn_3 at (41.5, -2.1) and its
# first leg is WESTWARD, to the crate at x = 36; a row anywhere on that leg is knocked over on the
# way out, twenty seconds before the carry that is supposed to knock it. Run 4 measured exactly
# that -- the first box tilted at t = 2.40 s and bar (1) reported 0.248 m of movement "before
# anything touched it", which was true of the crate and false of the bot's own shoulder.
# Starting at 42.30 puts the whole fixture past the spawn (capsule radius ~0.36 m reaches 41.86),
# so the outbound leg never sees it and the return leg walks the length of it.
$CanY = 0.035                 # a can's RADIUS: it is seeded lying on its side (see $CanRollXDeg)
# At the east end of the bays (they span x 35.4-44.6), shoved EAST into the open cross-aisle. The
# floor bin at x = 46.4 is 1.8 m away, which is past where a can nudged at 1 m/s can reach and is
# also a backstop if it is not: a can that stops against the bin is still inside the net-4 m bar.
$CanX = 44.60

# The search room is the supermarket seam's +40 x block. Anything outside this envelope has left
# the room, which is the "cannot clip through walls or the floor" half of the bar.
$RoomMinX = 33.0; $RoomMaxX = 47.5
$RoomMinZ = -6.0; $RoomMaxZ = 6.0
$RoomMinY = -0.5; $RoomMaxY = 6.0

$seed = @()
for ($i = 0; $i -lt 5; $i++) {
    $x = [math]::Round($BoxX0 + $i * $BoxPitch, 2)
    $seed += "$x,$RowY,$RowZ,box,0,90"
}
$seed += "$CanX,$CanY,$RowZ,can,$CanRollXDeg"
$seedArg = ($seed -join ";")

# The two events this run stages, as numbers rather than as a bot's walk:
#   1. the row's head, knocked EAST into the rest of the row (velocity plus a forward pitch);
#   2. the can, already on its side, rolled EAST down the open cross-aisle at the packet's 1 m/s.
# The spin on (2) is pure rolling for a 0.035 m radius: v = omega x r, so omega_z = -v/r. Getting
# it wrong costs a few centimetres of skid and friction then sorts it out; getting it ABSENT
# costs the whole bar, because a can pushed without spin slides.
$RollRadPerSec = [math]::Round(-$CanShoveMps / 0.035, 2)
$shoveArg = ("$ShovedBoxId,$RowShoveMps,0,0,$RowShoveAtSec,0,0,$RowTopplePitchRadPerSec;" +
             "$RollCanId,$CanShoveMps,0,0,$CanShoveAtSec,0,0,$RollRadPerSec")

Write-Host "=== physics feel: dominoes fall, cans roll, nothing freaks out ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$failures = New-Object System.Collections.Generic.List[string]
$procs = @()
try {
    Write-Host "[1/4] launching dedicated server (supermarket, search room) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "physfeel.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "supermarket",
        "--spawn-room", "search",
        "--spawn-index", "PhysBot=3,WitnessBot=2",
        "--seed-test-props", $seedArg,
        "--phys-shove", $shoveArg) "physfeel.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
        Write-Fail "server never reported listening on udp/$Port; see $serverOut"
    }
    if (-not (Wait-ForLogLine $serverOut "seed-test-props: seeded 6 test prop" 20)) {
        Write-Fail "the server did not seed all six fixture props; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id)), row at x=$BoxX0..$([math]::Round($BoxX0 + 4 * $BoxPitch,2)) z=$RowZ y=$RowY, can at x=$CanX"
    Write-Host "        the shove is this suite's: row head prop $ShovedBoxId at $RowShoveMps m/s pitched $RowTopplePitchRadPerSec rad/s (t=$RowShoveAtSec s); can prop $RollCanId seeded on its side, rolled at $CanShoveMps m/s / $RollRadPerSec rad/s (t=$CanShoveAtSec s), server clock"

    $CrateProp = Get-AuthoredPropId $serverOut $CratePropPath
    Write-Host "        the carried crate is authored prop $CrateProp (from the server's adoption log)"

    Write-Host "[2/4] launching the driver and the witness..." -ForegroundColor Cyan

    # THE z = -2 WALKWAY, and not the z = 0 one, because CARRY-1's authored crate Prop_3 stands
    # at (38, 0.22, 0) and SHELF-1 measured that a CharacterBody3D does not push a RigidBody3D.
    # The bot leaned on it for the whole of two runs at x = 37.70 and never reached the row --
    # which is a real finding about the level and about P1's limits (see the handoff), and is a
    # terrible place to stage a measurement about something else. Nothing stands between x = 36
    # and x = 45 at z = -2: the floor bins are at world x 33.6 / 46.4.
    #
    # PhysBot: walk to CARRY-1's crate at (36, 0.22, -2), grab it at t = 20 and STAY THERE. It is
    # no longer the mover (PHYS-2): it carries the crate for bar (6)'s throw and its samples are
    # the holder's view of everything else. It has no --carry-walk-to at all, so it never leaves
    # x = 36 -- six metres west of the row's head at 42.30 -- and cannot touch the fixture with
    # its own capsule. That separation is the point: the only thing that moves the row is the
    # number this file chose.
    #
    # It never voluntarily drops (-1) and throws the crate 30 s after the grab, east down the
    # empty aisle, which is bar (6).
    $physLog = Join-Path $script:LogDir "physfeel.phys.jsonl"
    $physBot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "PhysBot",
        "--log", $physLog, "--duration", $DurationSec, "--world", "supermarket",
        "--carry-script", "36,0.22,-2,20.0,-1,30", "--carry-grab-retry", "0.6",
        "--carry-target-prop", $CrateProp) "physfeel.phys"
    $procs += $physBot
    Start-Sleep -Milliseconds 400

    # TailBot IS GONE (PHYS-2). It existed as a SECOND MOVER, because a body walking into the row
    # knocked three or four boxes and then bogged down in the debris it had just made (3/5, 4/5,
    # 4/5, 3/5 measured) -- a CharacterBody3D cannot push a RigidBody3D, so a fallen box under its
    # feet stops it advancing. With the shove chosen by this file the row has one clean mover and
    # needs no second bot, which also takes a Godot launch off a contended machine and removes the
    # can this suite used to have to keep out of its own bars.

    # WitnessBot: holds nothing, touches nothing, stands at the far spawn. It is the peer that
    # proves the dominoes REPLICATED -- P1 wakes props on the server alone, and a stack that fell
    # only on the host would be the DOOR-1 defect ("shoved 3 prop(s)" while every client's copy
    # sat still) wearing a new hat.
    $witLog = Join-Path $script:LogDir "physfeel.witness.jsonl"
    $witBot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "WitnessBot",
        "--log", $witLog, "--duration", $DurationSec, "--world", "supermarket") "physfeel.witness"
    $procs += $witBot

    $bots = @(
        @{ Name = "PhysBot"; Proc = $physBot; JsonLog = $physLog }
        @{ Name = "WitnessBot"; Proc = $witBot; JsonLog = $witLog }
    )
    $deadline = (Get-Date).AddSeconds($DurationSec + 90)
    foreach ($b in $bots) {
        $remainingMs = [int]((($deadline - (Get-Date)).TotalSeconds) * 1000)
        if ($remainingMs -lt 1000) { $remainingMs = 1000 }
        if (-not $b.Proc.WaitForExit($remainingMs)) { Write-Fail "$($b.Name) did not exit within timeout" }
    }
    foreach ($b in $bots) {
        if ($b.Proc.ExitCode -ne 0) { Write-Fail "$($b.Name) exited with code $($b.Proc.ExitCode); see $($b.JsonLog)" }
    }
} finally {
    Stop-Procs $procs
}

Write-Host "[3/4] reading the logs..." -ForegroundColor Cyan

function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

# Every sample of one prop, as one view saw it. `up` is the world Y component of the prop's own
# up axis, from the quaternion BotHarness logs: for q = (x, y, z, w) the rotated (0,1,0) has
# y = 1 - 2(x^2 + z^2). 1.0 is upright, 0.0 is exactly on its side, -1.0 is upside down.
function Get-PropRows($Samples, [int]$PropId) {
    $out = @()
    foreach ($s in $Samples) {
        $p = @($s.props) | Where-Object { [int]$_.id -eq $PropId } | Select-Object -First 1
        if ($null -eq $p) { continue }
        $qx = [double]$p.qx; $qz = [double]$p.qz
        $out += [pscustomobject]@{
            T = [double]$s.t / 1000.0   # BotHarness stamps MILLISECONDS; every bar here is in seconds
            X = [double]$p.x; Y = [double]$p.y; Z = [double]$p.z
            Holder = [int]$p.holder
            Spd = [double]$p.spd
            Up = 1.0 - 2.0 * ($qx * $qx + $qz * $qz)
        }
    }
    return $out
}

$physSamples = Get-Samples $physLog
$witSamples = Get-Samples $witLog
$serverText = @(Get-Content $serverOut)

$views = @(
    @{ Name = "holder"; Samples = $physSamples }
    @{ Name = "witness"; Samples = $witSamples }
)

# STAGING FIRST, and in its own words. Every assertion below is about props being knocked over by
# a CARRIED CRATE; if the crate was never carried, the bars are being judged on an event that was
# never staged, which is the failure mode the rules file records four separate times.
$heldEver = @($physSamples | ForEach-Object { [int]$_.heldPropId } | Where-Object { $_ -gt 0 })
if ($heldEver.Count -eq 0) {
    $crateRows = Get-PropRows $physSamples $CrateProp
    $closest = 999.0
    foreach ($s in $physSamples) {
        $me = @($s.peers) | Where-Object { [int]$_.id -eq [int]$s.self } | Select-Object -First 1
        $c = @($s.props) | Where-Object { [int]$_.id -eq $CrateProp } | Select-Object -First 1
        if ($null -eq $me -or $null -eq $c) { continue }
        $d = [math]::Sqrt([math]::Pow([double]$me.x - [double]$c.x, 2) +
                          [math]::Pow([double]$me.y - [double]$c.y, 2) +
                          [math]::Pow([double]$me.z - [double]$c.z, 2))
        if ($d -lt $closest) { $closest = $d }
    }
    Write-Fail ("STAGING: PhysBot never held prop $CrateProp -- closest approach " +
        ("{0:F2}" -f $closest) + " m in 3D against the client's 1.5 m PickupRadius. " +
        "Inside 1.5 m and motionless is a press that found nothing; several metres out is the " +
        "four-corridor mis-walk. Nothing below is about physics. See $serverOut")
}

# --- STAGING, second half: the shove actually fired --------------------------------------
# A --phys-shove entry that names a prop id nothing seeded is a silent no-op, and every bar below
# would then read as "the physics did not work" about a command line that never asked it to. The
# server prints one line per shove it spends, so that is what is checked, and it is checked before
# anything is judged.
$shoveLines = @($serverText | Select-String -Pattern "^\[phys\] shove ")
if ($shoveLines.Count -lt 2) {
    Write-Fail ("STAGING: the server logged $($shoveLines.Count) [phys] shove line(s), expected 2 " +
        "(the row's head knocked, the can rolled). Nothing below is about physics -- the " +
        "fixture never fired. See $serverOut")
}
foreach ($l in $shoveLines) { Write-Host "        $l" -ForegroundColor DarkGray }

# --- bar (1): untouched is untouched ------------------------------------------------------
# THE WINDOW ENDS WHEN THE SHOVED BOX FIRST MOVES, AND IT IS READ OFF THE PROPS THEMSELVES.
# The server's shove clock and a bot's own clock are different clocks separated by the connect
# delay, and nothing in these logs correlates them -- so a window typed in server seconds would
# be a guess about how long the bots took to join. The head of the row is the one prop this file
# moves deliberately, so the first sample in which it has left its seeded pose IS the moment the
# fixture was struck, measured in the same clock as everything else.
#
# What the bar then says is the honest one: for that whole window, NOTHING ELSE moved. The other
# four boxes and the can are props nothing has touched at all, and a wake storm, a solver drift or
# an early contact would show up in them. (Measuring the shoved box against itself would be
# circular, which is why it is excluded.)
$shovedRows = @(Get-PropRows $physSamples $ShovedBoxId)
if ($shovedRows.Count -lt 2) {
    Write-Fail "STAGING: the holder never sampled prop $ShovedBoxId, the head of the row; see $physLog"
}
$struckT = [double]::PositiveInfinity
$sx0 = $shovedRows[0].X; $sy0 = $shovedRows[0].Y; $sz0 = $shovedRows[0].Z
foreach ($r in $shovedRows) {
    $d = [math]::Sqrt([math]::Pow($r.X - $sx0, 2) + [math]::Pow($r.Y - $sy0, 2) + [math]::Pow($r.Z - $sz0, 2))
    if ($d -gt 0.01) { $struckT = $r.T; break }
}
if ([double]::IsInfinity($struckT)) {
    $failures.Add("the head of the row never moved on the holder's view -- the shove was logged and nothing came of it")
    $struckT = 0.0
}

$restWorst = 0.0
foreach ($id in ($BoxIds + $RollCanId)) {
    if ($id -eq $ShovedBoxId) { continue }
    $rows = @(Get-PropRows $physSamples $id | Where-Object { $_.T -lt $struckT })
    if ($rows.Count -lt 2) { continue }
    $x0 = $rows[0].X; $y0 = $rows[0].Y; $z0 = $rows[0].Z
    foreach ($r in $rows) {
        $d = [math]::Sqrt([math]::Pow($r.X - $x0, 2) + [math]::Pow($r.Y - $y0, 2) + [math]::Pow($r.Z - $z0, 2))
        if ($d -gt $restWorst) { $restWorst = $d }
    }
}
Write-Host ("        untouched: worst movement {0:F4} m by the five props nothing touched, over the {1:F1} s before the row was struck" -f $restWorst, $struckT) -ForegroundColor DarkGray
# This IS the "no wake storm at rest" bar. A [phys] wake line carries no timestamp, so counting
# those lines could never tell a wake before the shove from one after it; the props' own positions
# over a window derived from the run can, and do.
if ($restWorst -gt 0.01) {
    $failures.Add(("a seeded prop moved {0:F3} m before anything touched it (bar 0.01) -- a stack that drifts at rest is not a stack" -f $restWorst))
}
if ($struckT -lt $UntouchedBarSec) {
    $failures.Add(("the row stood untouched for only {0:F1} s (bar {1:F1} s) -- something reached it before the suite's own shove did" -f $struckT, $UntouchedBarSec))
}


# --- bar (2): the dominoes ----------------------------------------------------------------
$upBar = [math]::Cos($TiltDeg * [math]::PI / 180.0)
foreach ($view in $views) {
    $fellAt = @{}
    foreach ($id in $BoxIds) {
        $rows = @(Get-PropRows $view.Samples $id)
        $t = $null
        foreach ($r in $rows) { if ($r.Up -lt $upBar) { $t = $r.T; break } }
        if ($null -ne $t) { $fellAt[$id] = $t }
    }
    $fellCount = $fellAt.Count
    if ($fellCount -eq 0) {
        Write-Host ("        {0,-8} dominoes: NONE of the {1} boxes tilted past {2} deg" -f $view.Name, $BoxIds.Count, $TiltDeg) -ForegroundColor DarkGray
        $failures.Add("$($view.Name): not one of the five boxes tilted past $TiltDeg deg -- a resting prop is still an immovable wall on this view")
        continue
    }
    $first = ($fellAt.Values | Measure-Object -Minimum).Minimum
    $last = ($fellAt.Values | Measure-Object -Maximum).Maximum
    $spread = $last - $first
    Write-Host ("        {0,-8} dominoes: {1}/{2} boxes past {3} deg, first at t={4:F2}s, all within {5:F2}s" -f `
        $view.Name, $fellCount, $BoxIds.Count, $TiltDeg, $first, $spread) -ForegroundColor DarkGray
    if ($fellCount -lt $BoxIds.Count) {
        $failures.Add("$($view.Name): only $fellCount of $($BoxIds.Count) boxes went over -- the chain stopped part way")
    }
    if ($spread -gt $DominoWindowSec) {
        $failures.Add(("{0}: the row took {1:F2}s to go over (bar {2:F1}s) -- that is a shove passed along, not a domino" -f $view.Name, $spread, $DominoWindowSec))
    }
}

# --- bar (3): the can rolls ---------------------------------------------------------------
foreach ($view in $views) {
    $rows = @(Get-PropRows $view.Samples $RollCanId)
    if ($rows.Count -lt 2) {
        $failures.Add("$($view.Name): the can was never sampled")
        continue
    }
    $x0 = $rows[0].X; $z0 = $rows[0].Z
    $travel = 0.0; $prev = $rows[0]
    $final = 0.0; $peak = 0.0
    foreach ($r in $rows) {
        $travel += [math]::Sqrt([math]::Pow($r.X - $prev.X, 2) + [math]::Pow($r.Z - $prev.Z, 2))
        $prev = $r
        if ($r.Spd -gt $peak) { $peak = $r.Spd }
    }
    $lastRow = $rows[$rows.Count - 1]
    $final = [math]::Sqrt([math]::Pow($lastRow.X - $x0, 2) + [math]::Pow($lastRow.Z - $z0, 2))
    Write-Host ("        {0,-8} can: path {1:F2} m, net {2:F2} m from where it stood, peak {3:F2} m/s" -f `
        $view.Name, $travel, $final, $peak) -ForegroundColor DarkGray
    if ($view.Name -ne "holder") { continue }   # the bar is measured once, on the simulating side's view
    if ($travel -lt $MinRollM) {
        $failures.Add(("the can travelled {0:F2} m (bar at least {1:F1} m) -- it was nudged and did not roll" -f $travel, $MinRollM))
    }
    if ($final -gt $MaxRollM) {
        $failures.Add(("the can ended {0:F2} m from where it stood (bar at most {1:F1} m) -- it rolled out of the aisle" -f $final, $MaxRollM))
    }
}

# --- bar (4): no freakout -----------------------------------------------------------------
# SCOPED TO THE FIXTURE, and the first run is why. Judged over EVERY prop in the world this bar
# reported `prop 1014 was seen at 8.95 m/s` -- which was the crate PhysBot had just THROWN, at
# exactly the speed CARRY-1's throw leaves the hand at. A throw is P2's one named exception, so
# the bar was correct about the number and wrong about the prop. The six seeded props are never
# held and never thrown, so they are the population the "nothing gets flung" claim is about.
$fixtureIds = @($BoxIds + $RollCanId)
$fastest = 0.0; $fastestId = 0; $peakAny = 0.0
$teleports = @()
foreach ($view in $views) {
    foreach ($s in $view.Samples) {
        foreach ($p in @($s.props)) {
            if ($fixtureIds -notcontains [int]$p.id) { continue }
            if ([int]$p.holder -ne 0) { continue }
            $spd = [double]$p.spd
            if ($spd -gt $peakAny) { $peakAny = $spd }
        }
    }
    # HORIZONTAL speed, from consecutive samples, because that is the component P2's clamp
    # actually bounds. The logged `spd` is the whole magnitude and is dominated by the FALL for
    # anything knocked off a surface -- run 3 reported 3.78 m/s for a box that had just dropped
    # 0.62 m, which is 3.5 m/s of gravity and about 1.4 m/s of shove. Sampled at ~5 Hz this
    # under-reads a peak, and it is the honest measure available without a new log field.
    foreach ($id in $fixtureIds) {
        $rows = @(Get-PropRows $view.Samples $id)
        for ($i = 1; $i -lt $rows.Count; $i++) {
            $dt = $rows[$i].T - $rows[$i - 1].T
            if ($dt -le 0) { continue }
            $dx = $rows[$i].X - $rows[$i - 1].X
            $dz = $rows[$i].Z - $rows[$i - 1].Z
            $h = [math]::Sqrt($dx * $dx + $dz * $dz) / $dt
            # A TELEPORT IS NOT A SPEED, and run 6 is why: this reported `prop 6 at 15.90 m/s
            # HORIZONTAL` in the same line as `peak total incl. fall 2.93`, which is arithmetically
            # impossible for one moving body. It was the rest audit putting a rolled can back at
            # its last-good pose -- a 3.3 m jump between two 5 Hz samples. Counted as what it is,
            # because a prop jumping IS P3's whole subject, and kept out of the speed.
            if ($h -gt $TeleportSpeedMps) { $teleports += "$($view.Name)/prop $id"; continue }
            if ($h -gt $fastest) { $fastest = $h; $fastestId = $id }
        }
    }
}
$clampLines = @($serverText | Select-String -Pattern "^\[phys\] clamp ")
$restoreLines = @($serverText | Select-String -Pattern "\[reach\] layer2 .*(RestoredLastGood|Stuck)")
$waitLines = @($serverText | Select-String -Pattern "^\[phys\] rest-wait ")
$wakeLines = @($serverText | Select-String -Pattern "^\[phys\] wake ")
Write-Host ("        freakout: fastest unheld prop {0:F2} m/s HORIZONTAL (prop {1}, bar {2:F1}; peak total incl. fall {7:F2}); {3} clamp line(s); {4} last-good restore(s); {5} rest-wait line(s); {6} wake(s)" -f `
    $fastest, $fastestId, $MaxPropSpeed, $clampLines.Count, $restoreLines.Count, $waitLines.Count, $wakeLines.Count, $peakAny) -ForegroundColor DarkGray
Write-Host ("        audit:    {0} rest-wait line(s), {1} restore(s), {2} position jump(s) in the samples" -f $waitLines.Count, $restoreLines.Count, $teleports.Count) -ForegroundColor DarkGray
if ($fastest -gt $MaxPropSpeed) {
    $failures.Add(("prop $fastestId was seen travelling {0:F2} m/s HORIZONTALLY with nobody holding it (bar {1:F1}) -- something flung it" -f $fastest, $MaxPropSpeed))
}
# THE MAGNITUDE, NOT THE COUNT, is what tells a backstop from an explosion. Measured in a
# five-box collapse: five clamps, every one trimming to 1.53-3.00 m/s horizontal -- i.e. the bar
# being enforced on a prop that was barely over it. A clamp that trimmed a prop from 15 m/s would
# be the thing P2 is actually afraid of, and until the log printed what it trimmed FROM the two
# were the same line. The count still has a bar, deliberately loose, because a collapse legitimately
# produces a handful and the packet's "<= 2" is written against a gentler fixture (twenty random
# held-prop bumps into the stocked bay).
$overTrim = @()
foreach ($l in $clampLines) {
    if ("$l" -match 'from ([\d.]+) to') {
        if ([double]$Matches[1] -gt $MaxTrimFromMps) { $overTrim += "$l" }
    }
}
if ($overTrim.Count -gt 0) {
    $failures.Add(("the clamp had to trim a prop from {0} -- that is a solver explosion, not a backstop: {1}" -f `
        $MaxTrimFromMps, $overTrim[0]))
}
if ($clampLines.Count -gt $MaxClampLines) {
    $failures.Add("the per-tick clamp bit $($clampLines.Count) time(s) (bar $MaxClampLines) -- more excursions than a collapse should produce, even if each one was small")
}
# A RESTORE IS NOT AUTOMATICALLY A FAILURE; AN UNGATED ONE IS. Run 6 rolled the can 3.3 m into
# the scenery, its rest pose failed the audit, and the audit did exactly what P3 says: it logged
# `rest-wait prop=6 stuck=0.00s nearestGrabRay=1.19m`, waited while the player stood there, and
# restored only once they had moved away. Calling that a failure would be calling the ruling a
# failure. What must never happen is a restore with NO wait behind it -- a prop teleporting in
# front of somebody -- so that is the bar.
$gatedProps = @($waitLines | ForEach-Object { if ($_ -match 'prop=(\d+)') { $Matches[1] } })
$ungated = @($restoreLines | Where-Object {
    if ($_ -match 'prop=(\d+)') { $gatedProps -notcontains $Matches[1] } else { $true } })
if ($ungated.Count -gt 0) {
    $failures.Add("the rest audit teleported $($ungated.Count) prop(s) with no [phys] rest-wait behind it -- a snap-back nobody waited on is the freakout Talon described")
}
if ($teleports.Count -gt 0 -and $ungated.Count -eq 0) {
    Write-Host ("        (the {0} position jump(s) above are those gated restores, landing after the player left)" -f $teleports.Count) -ForegroundColor DarkGray
}
if ($wakeLines.Count -eq 0) {
    $failures.Add("the server logged not one [phys] wake for the whole run -- P1's contact path never ran, so every green bar above is about something else")
}

# --- bar (5): everything settles ----------------------------------------------------------
# Rest in this build is a LATCH, so a prop back at Resting reads on every peer as spd = 0 and
# holder = 0 with its position no longer changing. The honest question is not whether a frozen
# body is stable -- it is whether a woken one ever gets back.
$stillMoving = @()
foreach ($id in ($BoxIds + $RollCanId)) {
    $rows = @(Get-PropRows $physSamples $id)
    if ($rows.Count -lt 3) { continue }
    $tail = $rows[($rows.Count - 3)..($rows.Count - 1)]
    $maxTail = ($tail | ForEach-Object { $_.Spd } | Measure-Object -Maximum).Maximum
    if ($maxTail -gt 0.05) { $stillMoving += "$id at $([math]::Round($maxTail,2)) m/s" }
}
Write-Host ("        settled: {0} of {1} knocked prop(s) still moving in the last three samples" -f `
    $stillMoving.Count, ($BoxIds.Count + 1)) -ForegroundColor DarkGray
if ($stillMoving.Count -gt 0) {
    $failures.Add("props never came back to rest by the end of the run: $($stillMoving -join ', ') -- a woken prop that never settles is P1 and the rest audit unable to both hold")
}

# --- bar (6): containment -----------------------------------------------------------------
$escapes = @()
foreach ($view in $views) {
    foreach ($s in $view.Samples) {
        foreach ($p in @($s.props)) {
            # The fixture only. The envelope below is the SEARCH room's, and the world also holds
            # the holding room's and the task room's props -- the first run flagged 64 samples of
            # `prop 1000 at (-0.60, 1.09, -4.60)`, which is a holding-room prop sitting exactly
            # where its level put it.
            if ($fixtureIds -notcontains [int]$p.id) { continue }
            $x = [double]$p.x; $y = [double]$p.y; $z = [double]$p.z
            if ($x -lt $RoomMinX -or $x -gt $RoomMaxX -or
                $z -lt $RoomMinZ -or $z -gt $RoomMaxZ -or
                $y -lt $RoomMinY -or $y -gt $RoomMaxY) {
                $escapes += ("prop $([int]$p.id) at ({0:F2}, {1:F2}, {2:F2}) on the $($view.Name) view" -f $x, $y, $z)
            }
        }
    }
}
$escapes = @($escapes | Select-Object -Unique)
Write-Host ("        contained: {0} sample(s) outside the room envelope x[{1},{2}] y[{3},{4}] z[{5},{6}]" -f `
    $escapes.Count, $RoomMinX, $RoomMaxX, $RoomMinY, $RoomMaxY, $RoomMinZ, $RoomMaxZ) -ForegroundColor DarkGray
if ($escapes.Count -gt 0) {
    $failures.Add("a prop left the room: $($escapes[0]) (and $($escapes.Count - 1) more) -- something clipped through a wall or the floor")
}

# Machine-readable evidence for the handoff and for whoever reads this next, printed
# unconditionally so a green run still hands over its numbers (FEEL-1's LAGTABLE pattern).
Write-Host ("PHYS1BARS restWorst={0:F4} untouchedSec={7:F1} shoveMps={8:F2} fastestUnheldHoriz={1:F2} clamps={2} restores={3} wakes={4} waits={5} jumps={6}" -f `
    $restWorst, $fastest, $clampLines.Count, $restoreLines.Count, $wakeLines.Count, $waitLines.Count, $teleports.Count, `
    $struckT, $RowShoveMps)

Write-Host "[4/4] verdict" -ForegroundColor Cyan
Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "PHYSICS-FEEL-TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "PHYSICS-FEEL-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: the row went over like dominoes on both views, the can rolled, nothing was flung, everything settled, and nothing left the room." -ForegroundColor Green
Write-Host ""
Write-Host "PHYSICS-FEEL-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
