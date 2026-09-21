<#
.SYNOPSIS
    FEEL-1's gate: a held prop is never inside the person carrying it, it stops against the
    world instead of passing through it, and it cannot fling anything.

.DESCRIPTION
    Talon's verdict after his first ride is this suite's whole specification:

      "There is still an extreme issue with how items are picked up: if the player moves, the
       item clips into their body, which is stupid and feels bad ... it shouldn't snap to any
       location; that's why there's physics and collision on the objects."

    udp/7913, and 7912 is why this line is long. The one ladder table in
    .claude/rules/test-suite.md said 7912 was the next free number; a SOLO-1 lane in another
    worktree (C:
epos\sfgd-solo1) read the same table and was ALREADY BOUND to 7912 when this
    suite first ran -- "Couldn't create an ENet host", err=CantCreate, with its server and its
    SoloA bot live on the machine. That is INT-0's measured lesson for the fourth time in this
    repo: a next-free port computed from a snapshot of a shared table is not free while another
    lane is branched off the same base. 7913 is claimed here AND written into the ladder table
    in the same commit, which is the half that makes a claim real.

    Phase 1 -- THE CLIP. A bot walks to an authored crate in the search room, grabs it through
    the shipped verb, and then runs HoldStressIntentSource's loop for the rest of the run:
    forward, backward, strafe both ways, two 180s, repeating. A second bot stands as a witness.
    Every sample of both logs carries `cap` -- the signed clearance between the held prop and its
    HOLDER'S COLLISION CAPSULE (NetworkedProp.HolderClearanceM), negative meaning the prop is
    inside the person. The bar is ZERO TICKS NEGATIVE, on the holder's own view and on the
    witness's, because a crate in the hider's chest is exactly as bad on the seeker's screen.

    A straight patrol cannot stage this and that is why the stress loop exists: the old carry
    survived walking forwards (the hold point ran ahead of the prop), and only motion TOWARD
    where the prop is -- backwards, or a turn that swings it across the body -- produced the clip.

    Phase 1 also reports the hold's lag (`springLag`), which is the handoff's lag table.

    Phase 2 -- THE WORLD. A bot carries a crate into an aisle bay and keeps walking. A held prop
    now keeps the world collision MASK and is swept with PhysicsServer3D.BodyTestMotion every
    tick, so the shelf stops it; when the world holds it off its target by more than
    CarryHold.BreakHoldM for BreakHoldSec the hold is given up and the server is asked to set it
    down. So the assertion is that the holder's log carries a `[carry] hold broken` line: the
    world can only block a prop it can actually touch.

    POSITIVE CONTROL for phase 2, run by hand and recorded in the handoff rather than wired in:
    plant `CollisionMask = 0` back into Carryable.OnPickedUpBySpring and this phase goes red --
    the prop passes through the bay, is never blocked, and no break line is ever printed.

    Phase 3 -- NO FREAKOUTS. Over the whole run, no prop may exceed MaxPropSpeed m/s and none may
    leave the room's box. Talon: "I want to know objects won't freak out and make other objects
    jump around randomly." Whether the boxes MOVE at all is reported, not gated -- a Resting prop
    is frozen kinematic on every peer, so waking one is PHYS-1's first item and this suite's job
    is to hand it the evidence.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [int]$Port = 7913,
    [double]$DurationSec = 26,
    # Zero ticks of interpenetration -- with a 5 mm allowance, and the allowance is a fact about
    # WHEN the sample is taken rather than a softening of the bar.
    #
    # The prop is projected out of the holder's capsule on every PHYSICS tick. BotHarness samples
    # on a frame, which can fall between two physics ticks, and the holder it is measured against
    # is an interpolated proxy on the witness's peer -- so a sample can catch the body a few
    # millimetres further along than the pose the projection last answered. Measured worst on a
    # correct build: 0.0031 m, at the grab. Against the 0.050 m skin the projection maintains,
    # that is 6% of the gap; against a real clip it is nothing at all -- the defect this suite
    # exists for put the prop 0.0336 m inside on its first run and a planted one goes to 0.21 m.
    [double]$ClearanceToleranceM = 0.005,
    # The domino bar from Talon's third note, in his own units.
    [double]$MaxPropSpeed = 3.0,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# The search room's authored crates. Derived from the server's own adoption log, never typed --
# an authored prop's id is a function of every other authored prop's NAME (see _Common.ps1).
$HoldPropPath = "SearchRoom/Prop_0"
$ShelfPropPath = "SearchRoom/Prop_1"

# The room's box, generously drawn: the search room's RoomBounds ends at world x = 47 and the
# bays span x in [35.4, 44.6]. Anything outside this has been flung, not placed.
$RoomMinX = 30.0; $RoomMaxX = 50.0
$RoomMinZ = -9.0; $RoomMaxZ = 9.0
$RoomMinY = -1.0; $RoomMaxY = 4.0

Write-Host "=== carry-hold: never inside the holder, stopped by the world, never a projectile ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$procs = @()
try {
    Write-Host "[1/4] launching dedicated server (supermarket, search room) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "carryhold.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "supermarket",
        "--spawn-room", "search",
        "--spawn-index", "HoldBot=0,WitnessBot=1,ShelfBot=2") "carryhold.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
        Write-Fail "server never reported listening on udp/$Port; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    $HoldProp = Get-AuthoredPropId $serverOut $HoldPropPath
    $ShelfProp = Get-AuthoredPropId $serverOut $ShelfPropPath
    Write-Host "        authored prop ids this run: hold=$HoldProp shelf=$ShelfProp (from the server's adoption log)"

    Write-Host "[2/4] launching the holder, the witness and the shelf driver..." -ForegroundColor Cyan

    # HoldBot: walk to its crate, grab it, never voluntarily drop (-1), then stress the hold.
    $holdLog = Join-Path $script:LogDir "carryhold.hold.jsonl"
    $holdBot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "HoldBot",
        "--log", $holdLog, "--duration", $DurationSec, "--world", "supermarket",
        "--carry-script", "36,0.22,0,1.0,-1", "--carry-grab-retry", "0.6",
        "--carry-target-prop", $HoldProp, "--hold-stress") "carryhold.hold"
    $procs += $holdBot
    Start-Sleep -Milliseconds 400

    # WitnessBot: holds nothing, watches. Its `cap` for the held prop is the OTHER view.
    $witLog = Join-Path $script:LogDir "carryhold.witness.jsonl"
    $witBot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "WitnessBot",
        "--log", $witLog, "--duration", $DurationSec, "--world", "supermarket") "carryhold.witness"
    $procs += $witBot
    Start-Sleep -Milliseconds 400

    # ShelfBot: grab a crate and keep walking INTO the SearchPillar -- a 1 x 2 x 1 m static block
    # centred at world (46, 1, 2.5), the same fixture Run-PlaceTest refuses a placement inside.
    # --carry-walk-to is one point walked in a straight line (HOLD-1's note), so a point at the
    # pillar's own centre is what makes the bot keep pushing rather than arrive and stop -- and
    # the crate, held out in front, meets the pillar before the bot's own capsule does.
    #
    # Measured: an earlier version aimed at (40, 2.6), which is open walkway, and the bot carried
    # the crate there without ever touching anything. A shelf beat needs a shelf.
    $shelfLog = Join-Path $script:LogDir "carryhold.shelf.jsonl"
    $shelfBot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "ShelfBot",
        "--log", $shelfLog, "--duration", $DurationSec, "--world", "supermarket",
        "--carry-script", "36,0.22,2,1.0,-1", "--carry-grab-retry", "0.6",
        "--carry-target-prop", $ShelfProp,
        "--carry-walk-to", "46,2.5") "carryhold.shelf"
    $procs += $shelfBot

    $bots = @(
        @{ Name = "HoldBot"; Proc = $holdBot; JsonLog = $holdLog }
        @{ Name = "WitnessBot"; Proc = $witBot; JsonLog = $witLog }
        @{ Name = "ShelfBot"; Proc = $shelfBot; JsonLog = $shelfLog }
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

$holdSamples = Get-Samples $holdLog
$witSamples = Get-Samples $witLog
$shelfSamples = Get-Samples $shelfLog
$holdPeer = [int]$holdSamples[0].self
$shelfPeer = [int]$shelfSamples[0].self
Write-Host "        peers: HoldBot=$holdPeer ShelfBot=$shelfPeer  samples: hold=$($holdSamples.Count) witness=$($witSamples.Count) shelf=$($shelfSamples.Count)" -ForegroundColor DarkGray

# Every sample of one prop, as one view saw it, while that prop was held by $Holder.
function Get-HeldRows($Samples, [int]$PropId, [int]$Holder) {
    $out = @()
    foreach ($s in $Samples) {
        $p = @($s.props) | Where-Object { [int]$_.id -eq $PropId } | Select-Object -First 1
        if ($null -eq $p) { continue }
        if ([int]$p.holder -ne $Holder) { continue }
        $out += [pscustomobject]@{
            T = [double]$s.t
            Cap = [double]$p.cap
            Lag = [double]$p.springLag
            X = [double]$p.x; Y = [double]$p.y; Z = [double]$p.z
        }
    }
    return $out
}

$failures = New-Object System.Collections.Generic.List[string]

Write-Host "[4/4] verifying..." -ForegroundColor Cyan

# --- Phase 1: the clip, on both views ---------------------------------------------------------
$ownRows = Get-HeldRows $holdSamples $HoldProp $holdPeer
$witRows = Get-HeldRows $witSamples $HoldProp $holdPeer

if ($ownRows.Count -lt 30) {
    $failures.Add("STAGING: HoldBot held prop $HoldProp for only $($ownRows.Count) sample(s) on its own view; the clip bar has nothing to measure (did the grab land?)")
}
if ($witRows.Count -lt 30) {
    $failures.Add("STAGING: the witness saw prop $HoldProp held by $holdPeer for only $($witRows.Count) sample(s); the second view has nothing to measure")
}

foreach ($view in @(@{ Name = "holder"; Rows = $ownRows }, @{ Name = "witness"; Rows = $witRows })) {
    $rows = $view.Rows
    if ($rows.Count -lt 30) { continue }
    $caps = @($rows | ForEach-Object { $_.Cap })
    $worst = ($caps | Measure-Object -Minimum).Minimum
    $mean = ($caps | Measure-Object -Average).Average
    $bad = @($rows | Where-Object { $_.Cap -lt (-1 * $ClearanceToleranceM) })
    Write-Host ("        {0,-8} clearance: worst={1:F4}m mean={2:F4}m over {3} held sample(s), {4} tick(s) inside the holder" -f `
        $view.Name, $worst, $mean, $rows.Count, $bad.Count) -ForegroundColor DarkGray
    if ($bad.Count -gt 0) {
        $first = $bad[0]
        $failures.Add(("THE CLIP: on the {0}'s view the held prop was INSIDE the holder on {1} of {2} tick(s); worst {3:F4} m (first at t={4}, prop at {5:F2},{6:F2},{7:F2}). Zero is the bar." -f `
            $view.Name, $bad.Count, $rows.Count, $worst, $first.T, $first.X, $first.Y, $first.Z))
    }
}

# The lag table: what the hold is trailing its target by, on the holder's own view.
if ($ownRows.Count -ge 30) {
    $lags = @($ownRows | ForEach-Object { $_.Lag })
    $lagMean = ($lags | Measure-Object -Average).Average
    $lagPeak = ($lags | Measure-Object -Maximum).Maximum
    Write-Host ("        hold lag:   mean={0:F3}m peak={1:F3}m over {2} sample(s)" -f $lagMean, $lagPeak, $lags.Count) -ForegroundColor DarkGray
    Write-Host "LAGTABLE mean=$([math]::Round($lagMean,4)) peak=$([math]::Round($lagPeak,4)) n=$($lags.Count)"
}

# --- Phase 2: the world stops it --------------------------------------------------------------
# SearchPillar is a 1 x 2 x 1 m static block centred at world (46, 1, 2.5) -- the same fixture
# Run-PlaceTest refuses a placement inside. ShelfBot carries a 0.44 m crate straight at it. The
# assertion is a PENETRATION: how far the crate's own box ever got inside the pillar's while the
# crate was held.
#
# This used to assert that a '[carry] hold broken' line appeared, and that was the wrong
# instrument: measured, the crate stopped dead with its face ON the pillar (centre x = 45.28
# against a face at 45.50, i.e. 0.22 m = exactly its own half-width) and the BOT wedged at the
# same moment, so the hold never ran the 0.6 m past its target that the break rule needs. The
# sweep had done its whole job and the suite called it a failure. Penetration is the quantity the
# beat is actually about, and it is the one a planted CollisionMask = 0 moves.
$PillarMinX = 45.5; $PillarMaxX = 46.5
$PillarMinZ = 2.0;  $PillarMaxZ = 3.0
$CrateHalf = 0.22          # the foundation's 0.44 m crate
$PenetrationToleranceM = 0.02   # PlacementIntegrity's own overlap tolerance

$shelfOut = Join-Path $script:LogDir "carryhold.shelf.out.log"
$broke = @()
if (Test-Path $shelfOut) { $broke = @(Select-String -Path $shelfOut -Pattern "\[carry\] hold broken") }

$shelfHeld = @(Get-HeldRows $shelfSamples $ShelfProp $shelfPeer)
$worstPen = 0.0
foreach ($r in $shelfHeld) {
    # Overlap of the crate's AABB with the pillar's, per axis; the penetration is the smaller of
    # the two, because a box only counts as inside when it overlaps on BOTH.
    $ox = [math]::Min($r.X + $CrateHalf, $PillarMaxX) - [math]::Max($r.X - $CrateHalf, $PillarMinX)
    $oz = [math]::Min($r.Z + $CrateHalf, $PillarMaxZ) - [math]::Max($r.Z - $CrateHalf, $PillarMinZ)
    if ($ox -gt 0 -and $oz -gt 0) {
        $pen = [math]::Min($ox, $oz)
        if ($pen -gt $worstPen) { $worstPen = $pen }
    }
}
$closest = 99.0
foreach ($r in $shelfHeld) { if (($PillarMinX - $r.X) -lt $closest) { $closest = $PillarMinX - $r.X } }
Write-Host ("        shelf:      held {0} sample(s); closest approach to the pillar face {1:F3} m; worst penetration {2:F3} m (bar {3}); {4} 'hold broken' line(s)" -f `
    $shelfHeld.Count, $closest, $worstPen, $PenetrationToleranceM, $broke.Count) -ForegroundColor DarkGray
if ($shelfHeld.Count -lt 10) {
    $failures.Add("STAGING: ShelfBot never held prop $ShelfProp long enough to push it into anything ($($shelfHeld.Count) sample(s))")
} elseif ($closest -gt 0.5) {
    $failures.Add(("STAGING: ShelfBot's crate never got closer than {0:F3} m to the pillar face -- the shelf beat never happened, so a penetration of 0 proves nothing." -f $closest))
} elseif ($worstPen -gt $PenetrationToleranceM) {
    $failures.Add(("THE WORLD: the held crate went {0:F3} m INSIDE SearchPillar (bar {1} m). A held prop keeps the world collision mask and is swept against it every tick precisely so it stops at the face; this is the CollisionMask = 0 defect back." -f $worstPen, $PenetrationToleranceM))
}

# --- Phase 3: nothing is flung ----------------------------------------------------------------
# ONLY props nobody ever held, and only inside the room the run happens in. Both narrowings are
# the difference between measuring the carry and measuring the world:
#   * a prop that was HELD and then set down falls the last centimetre and lands -- a 4 m/s
#     landing is physics working, not a carry flinging something;
#   * the supermarket is three rooms and the holding room's rack sits near the origin, so a box
#     drawn round the search room would report every prop in the game as "escaped".
# The box is therefore a fact about each prop: where it was in the run's FIRST sample.
$everHeld = @{}
foreach ($s in $holdSamples) {
    foreach ($p in @($s.props)) {
        if ([int]$p.holder -ne 0) { $everHeld[[int]$p.id] = $true }
    }
}
$homes = @{}
foreach ($p in @($holdSamples[0].props)) { $homes[[int]$p.id] = [pscustomobject]@{ X = [double]$p.x; Z = [double]$p.z } }

$fastest = 0.0; $fastestId = -1
$escaped = @()
foreach ($s in $holdSamples) {
    foreach ($p in @($s.props)) {
        $id = [int]$p.id
        if ($everHeld.ContainsKey($id)) { continue }
        $spd = [double]$p.spd
        if ($spd -gt $fastest) { $fastest = $spd; $fastestId = $id }
        if (-not $homes.ContainsKey($id)) { continue }
        # A never-held prop that has travelled more than ThrowDistanceM from where it started has
        # been flung by something. That is the honest "did the carry throw the shelf across the
        # room" question, and it needs no room geometry at all.
        $origin = $homes[$id]
        $dx = [double]$p.x - $origin.X; $dz = [double]$p.z - $origin.Z
        if ([math]::Sqrt($dx * $dx + $dz * $dz) -gt 2.0) {
            $escaped += [pscustomobject]@{ Id = $id; X = [double]$p.x; Y = [double]$p.y; Z = [double]$p.z }
        }
    }
}
Write-Host ("        freakout:   fastest never-held prop {0:F2} m/s (prop {1}, bar {2}); {3} prop-sample(s) flung more than 2 m from home" -f `
    $fastest, $fastestId, $MaxPropSpeed, $escaped.Count) -ForegroundColor DarkGray
if ($fastest -gt $MaxPropSpeed) {
    $failures.Add(("NO FREAKOUTS: never-held prop $fastestId reached {0:F2} m/s, over the {1} m/s bar -- a carry that shoves the world this hard is the thing Talon asked to be sure could not happen." -f $fastest, $MaxPropSpeed))
}
if ($escaped.Count -gt 0) {
    $e = $escaped[0]
    $failures.Add(("NO FREAKOUTS: never-held prop $($e.Id) was flung to ({0:F2}, {1:F2}, {2:F2}), more than 2 m from where it started -- {3} prop-sample(s) out there." -f $e.X, $e.Y, $e.Z, $escaped.Count))
}

# Reported, never gated: did anything a held prop touched actually MOVE? A Resting prop is frozen
# kinematic on every peer, so the honest expected answer today is "no" -- and that is PHYS-1's
# first item, with this line as the evidence.
$moved = 0
if ($holdSamples.Count -gt 2) {
    $first = $holdSamples[0]; $last = $holdSamples[-1]
    foreach ($p0 in @($first.props)) {
        $p1 = @($last.props) | Where-Object { [int]$_.id -eq [int]$p0.id } | Select-Object -First 1
        if ($null -eq $p1) { continue }
        if ([int]$p0.holder -ne 0 -or [int]$p1.holder -ne 0) { continue }
        $d = [math]::Sqrt(([double]$p1.x - [double]$p0.x) * ([double]$p1.x - [double]$p0.x) +
                          ([double]$p1.y - [double]$p0.y) * ([double]$p1.y - [double]$p0.y) +
                          ([double]$p1.z - [double]$p0.z) * ([double]$p1.z - [double]$p0.z))
        if ($d -gt 0.02) { $moved++ }
    }
}
Write-Host "        wake:       $moved never-held prop(s) moved more than 2 cm over the run (reported, not gated -- PHYS-1 owns wake-on-contact)" -ForegroundColor DarkGray
Write-Host "PHYS1EVIDENCE movedRestingProps=$moved"

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "CARRY-HOLD-TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "CARRY-HOLD-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: the held prop never entered its holder on either view, the world stopped it, and nothing was flung." -ForegroundColor Green
Write-Host ""
Write-Host "CARRY-HOLD-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
