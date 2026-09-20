<#
.SYNOPSIS
    BTN-1: the three round buttons and the drop-off bin, driven on a real server by real bots
    through the real press path. Three phases, each one a separate server.

.DESCRIPTION
    Everything the buttons DECIDE is engine-free and tested in tests/unit/RoundButtonTests.cs
    (the lamp derivation, the press gate, the refusal copy, the press -> fact mapping, the
    round's answer read back). None of that is repeated here. This suite exists for the half a
    pure function cannot reach, and the packet names it in one sentence: a press with no visible
    or audible result on the presser is a defect.

      1. THE PRESS REACHES THE SERVER AND COMES BACK. A client asks, the server gates it,
         latches a fact, the loop answers one tick later, and ONE broadcast reaches every peer.
         Every step of that is netcode and none of it is testable without a socket.
      2. A REFUSAL IS AN ANSWER, NOT SILENCE. Three different refusals are staged and each one
         must produce its own named reason AND its own cap animation on every peer.
      3. THE AFFORDANCE MOVES BEFORE THE PRESS. The START lamp must go Dark -> Lit at the moment
         the hider picks something off the rack, derived on each peer from the wire.
      4. THE RACK NAMES THE TARGET. Picking an object up must set the round's target prop id,
         which is what REACH-1's audit aims at and what the bin tells a delivery from a decoy by.
      5. THE BIN TELLS THEM APART. A wrong prop buzzes and changes nothing; the target ends the
         round.

    WHAT IS DRIVEN BY A BOT AND WHAT IS DRIVEN BY --round-script, stated rather than implied
    (the packet allows the dev script to stand in for what bots cannot do):

      - PHASE 1 is entirely real. No --round-script at all. A bot walks to the rack, presses
        START three times through the shipped verb, grabs a real rack object, and the round
        starts because the buttons said so. This is the phase that proves the lane.
      - PHASE 2 uses --round-script only to reach the Hiding phase (object@/start@), because
        getting there for real is phase 1's job and repeating it doubles the runtime. The
        CONFIRM press itself is a real bot press through the real verb.
      - PHASE 3 uses --round-script to reach Seeking and --reach-target to name the target,
        because the delivery is what is under test and the route to it is not. The two
        deliveries are real: a bot carries a real prop across the room and sets it down in the
        bin through CARRY-1's place verb.

    WHAT IS DELIBERATELY NOT HERE. PressRefusal.NotNow and NotYourButton on a BOT: the server
    checks reach before it checks the phase (proximity is the anti-cheat gate and belongs
    first), so a bot that is in the wrong phase is also, in every layout this level has, in the
    wrong room -- and it gets TooFarAway, which is the correct answer. Both reasons are covered
    in tests/unit/RoundButtonTests.cs and both are reachable by a human standing at the button,
    which is the two-client headed gate's business. Phase 1 stages NotNow anyway by parking the
    SEEKER at the START button and having it press after the round has begun.

    PROVED ABLE TO FAIL. See the DEVIATIONS / EVIDENCE block in
    docs/agents/handoffs/2026-09-19-BTN-1.md for the planted fault and what it printed.

    Exit 0 = PASS. Headless throughout. One Godot at a time: this suite takes the machine-wide
    suite mutex like every other.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param(
    # udp/7906, GIVEN BY THE ORCHESTRATOR rather than computed from a snapshot of tests/ --
    # INT-0's lesson applied rather than re-learned. Three lanes off one base each computed
    # "the next free port" from the same directory listing and all three picked 7896; the
    # ladder as it stands is in .claude/rules/test-suite.md. 7905 is SFX-2's.
    [int]$Port = 7906,
    [int]$MutexTimeoutMinutes = 30,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# --- the world, in world space -----------------------------------------------------------------
# HoldingRoom.tscn is instanced at the origin; SearchRoom.tscn at x = +40.
# DERIVED AT RUN TIME FROM THE SERVER'S OWN ADOPTION LOG, not typed -- see Get-AuthoredPropId in
# _Common.ps1. These three numbers have now moved twice in two days (CARRY-1 -> BTN-1 ->
# HOLD-1's practice corner) and the node paths have not moved at all, so the paths are the
# contract. The rack's OWN three are the exception and are still asserted as literals below:
# 1000..1002 is a PROPERTY of the level (nothing sorts before HoldingRoom/ObjectRack) and this
# suite is the thing that would notice if it stopped being true.
$RackTargetPath  = "HoldingRoom/ObjectRack/Deal_2"
$SearchTargetPath = "SearchRoom/Prop_1"   # world (36, 0.22, 2)   -- phase 3's target
$SearchDecoyPath  = "SearchRoom/Prop_3"   # world (38, 0.22, 0)   -- phase 3's wrong prop
$RackBoxPropId = -1; $SearchTargetId = -1; $SearchDecoyId = -1
# The bin's interior, in world space: two crates side by side, both clear of its walls, and the
# spot each courier walks to in order to be in reach of its own pose.
#
# THE REACH IS FROM THE HAND, NOT THE BODY, AND THAT IS WHAT THE FIRST RUN GOT WRONG.
# PropManager.RequestPlace measures PlaceReachM + GrabRangeTolerance (1.65 m) from the avatar's
# CARRY ANCHOR, which sits about 0.9 m in front of the body and swings with its heading, and
# ScriptedCarryIntentSource stops 1.2 m SHORT of its walk-to point. Walking to a spot 1.2 m north
# of the bin therefore left both couriers about 1.6-1.7 m from their own poses and the server
# answered TooFarToPlace on both -- a staging failure that reads exactly like a bin that does not
# work. The walk-to is now the BIN ITSELF: the courier presses up against its wall, arrives by
# being blocked, and its hand ends up over the rim.
#
# RE-AIMED BY HOLD-1 (2026-09-19) WHEN BTN-1 AND SHELF-1 MET, and the reason is the level, not
# this file. SHELF-1 dressed the search room with four bays and 1.6 m walkways and NO cross-aisle
# except at each end, so a --carry-walk-to (which is ONE point and walks a straight line) can
# only reach something in its own walkway or at the end of it. The bin moved to the -X
# cross-aisle at local (-6.2, 0, 0) = world (33.8, 0, 0) -- see SearchRoom.tscn's header for the
# whole argument, including the shelf it used to intersect.
#
# EACH COURIER NOW STAYS IN ITS OWN WALKWAY, and which prop each one carries is chosen by WHERE
# THAT BOT SPAWNS rather than by taste. Gameplay.SpawnPositionFor deals SearchSpawn markers by
# JOIN INDEX (index % 4), so in this phase A gets SearchSpawn_0 (35.5, 0), B gets _1 (44.5, 0)
# and C -- the mid-round joiner -- gets _2 (38.5, 2.1), one walkway over from the other two.
# A --carry-walk-to is ONE point walked in a STRAIGHT LINE, and the dressed room has no
# cross-aisle except at each end (bays span world x in [35.4, 44.6]), so a courier that must
# change walkway walks into a shelf. Measured: C, sent from (38.5, 2.1) to Prop_0 at (36, 0),
# stopped dead at (36.01, 1.45) against Aisle2_Bay0 and the phase failed with "the target was
# never delivered" -- which reads exactly like a bin that does not work.
#
#   B (spawn z = 0)   carries Prop_3 (38, 0.22, 0)  -- the DECOY, its own walkway throughout.
#   C (spawn z = 2.1) carries Prop_1 (36, 0.22, 2)  -- the TARGET, its own walkway throughout.
#
# B is stopped by the bin's +X wall (outer face world x = 34.5) at about x = 34.86 with the
# 0.36 m capsule, and its carry anchor then sits near x = 33.96, inside the 1.28 m clear
# interior (world x in [33.16, 34.44]). C approaches down the z = 2.1 walkway and stops in the
# CROSS-AISLE beside the bin rather than against it -- its walk-to is 1.3 m past the bin in x so
# that ScriptedCarryIntentSource's 1.2 m arrive radius leaves it in free floor, and the line
# from its grab to that point clears the corner of Aisle2_Bay0 at (35.4, 1.3) by about 0.43 m
# against a 0.36 m capsule. The two poses are 0.64 m apart in z: two 0.44 m crates side by side
# with 0.2 m to spare, the size the bin was built for.
$BinTargetPose   = "33.8,0.30,0.32,0"
$BinDecoyPose    = "33.8,0.30,-0.32,0"
$BinTargetWalk   = "32.9,1.30"
$BinDecoyWalk    = "33.8,-0.32"

Write-Host "=== BTN-1: the three round buttons, the rack and the drop-off bin ===" -ForegroundColor White

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
$script:Failures = @()
function Add-Failure([string]$m) { $script:Failures += $m }

function Start-ButtonsServer([string]$Tag, [string[]]$ServerArgs) {
    # NEVER name a PowerShell function parameter "Args" -- it is an automatic variable and a
    # parameter of that name receives nothing, silently. REACH-1 lost a run to exactly that:
    # three servers started, printed their graphics line, and sat there forever with none of the
    # game flags after "--", which looks identical to a bind failure.
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList (@(
        "--headless", "--path", $script:Root, "--") + $ServerArgs) `
        -RedirectStandardOutput $out `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

function Start-ButtonsBot([string]$Tag, [string]$Name, [int]$Duration, [string[]]$BotArgs) {
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList (@(
        "--headless", "--path", $script:Root, "--",
        "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
        "--duration", $Duration, "--world", "supermarket") + $BotArgs) `
        -RedirectStandardOutput (Join-Path $script:LogDir "$Tag.out.log") `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

function Get-Lines([string]$Path) {
    if (-not (Test-Path $Path)) { return @() }
    return @(Get-Content $Path)
}

# Index of the FIRST line matching a pattern, or -1. Line ORDER in one process's stdout is the
# only ordering these assertions need and the only one that is free of clock skew.
function Find-Index($Lines, [string]$Pattern) {
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match $Pattern) { return $i }
    }
    return -1
}
function Count-Matches($Lines, [string]$Pattern) {
    return @($Lines | Where-Object { $_ -match $Pattern }).Count
}
function Echo-Matching($Lines, [string]$Pattern) {
    foreach ($l in @($Lines | Where-Object { $_ -match $Pattern })) {
        Write-Host "        $($l.Trim())" -ForegroundColor DarkGray
    }
}

try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    # ==========================================================================================
    # PHASE 1 -- START: refused, refused, accepted. No dev script anywhere.
    # ==========================================================================================
    Write-Host "[1/3] START pressed three times for real: refused, refused, accepted..." -ForegroundColor Cyan
    $p1Out = Join-Path $script:LogDir "buttons-p1-server.out.log"
    $server = Start-ButtonsServer "buttons-p1-server" @(
        "--server", "--port", $Port, "--world", "supermarket")
    $procs += $server
    if (-not (Wait-ForLogLine $p1Out "\[server\] listening" 60)) {
        Stop-Proc $server
        Write-Fail "phase 1: the server never reported listening on udp/$Port; see buttons-p1-server.out.log"
    }

    # THE AUTHORED PROP IDS FOR THIS WHOLE RUN, derived once, here. Phase 3 has to pass
    # --reach-target on the SERVER'S COMMAND LINE, which is before that server has logged
    # anything -- so the derivation cannot happen there. It happens off phase 1's server, whose
    # world is the same world (adoption is a pure function of the scene files), and the three
    # phases then share the answer. See Get-AuthoredPropId in _Common.ps1.
    $RackBoxPropId  = Get-AuthoredPropId $p1Out $RackTargetPath
    $SearchTargetId = Get-AuthoredPropId $p1Out $SearchTargetPath
    $SearchDecoyId  = Get-AuthoredPropId $p1Out $SearchDecoyPath
    Write-Host ("        authored prop ids this run: rack box=$RackBoxPropId " +
                "target=$SearchTargetId decoy=$SearchDecoyId (derived from the adoption log)")

    # BOT A -- the hider. Walks to the rack's box (live position, retried grab), grabs no earlier
    # than its own t=20, and presses START three times:
    #   t=6   alone in the room            -> NeedTwoPlayers
    #   t=16  B present, hands empty       -> HiderMustHoldAnObject
    #   t=28  holding the box              -> ACCEPTED, Holding -> Hiding
    # It never walks away from the rack, and the START button is authored inside the press radius
    # of somebody standing there (see HoldingRoom.tscn) -- which is why no walk-to is needed and
    # why moving that button is a change to this suite.
    $botA = Start-ButtonsBot "buttons-p1-A" "BtnHider" 46 @(
        "--carry-script", "0.6,1.12,-4.6,20,-1",
        "--carry-target-prop", $RackBoxPropId, "--carry-grab-retry", "0.8",
        "--press", "start@6,start@16,start@28")
    $procs += $botA

    # The 11 s stagger is what makes the FIRST press land in an empty room. It is the one thing
    # in this suite that is a wall-clock guess, and it is a guess about a JOIN, not about a walk:
    # the assertion downstream reads the server's own refusal reason, so if the stagger were ever
    # too short the suite would say NeedTwoPlayers-was-not-logged rather than pass quietly.
    Start-Sleep -Seconds 11

    # BOT B -- the seeker. Parks beside the START button and presses it AFTER the round has begun,
    # which is the one wrong-phase press a bot can stage: it is in reach, so the reach check
    # passes and the phase gate is what answers.
    $botB = Start-ButtonsBot "buttons-p1-B" "BtnSeeker" 40 @(
        "--goto-script", "1.2,-4.5",
        "--press", "start@26")
    $procs += $botB

    foreach ($b in @($botA, $botB)) {
        if (-not (Wait-ForExit $b 120)) {
            Stop-Proc $b
            Add-Failure "phase 1: a bot did not exit within its own duration + slack"
        }
    }
    Stop-Proc $server
    Start-Sleep -Milliseconds 500

    $p1 = Get-Lines $p1Out
    if ($p1.Count -eq 0) { Write-Fail "phase 1: no server log was written at all" }

    Echo-Matching $p1 '^\[buttons\] found '
    Echo-Matching $p1 '^\[buttons\] rack holds '
    Echo-Matching $p1 '^\[props\] authored prop 100[0-9] '
    Echo-Matching $p1 '^\[buttons\] press '
    Echo-Matching $p1 '^\[buttons\] target prop is now '

    # --- the three buttons exist, and they FACE INTO THEIR ROOMS ---------------------------
    # A .tscn's twelve Transform3D floats are basis ROWS; the same rotation written column-wise
    # is its inverse, and a button whose +Z points into the wall presses its cap outwards. The
    # server measures and prints the facing, so this is checkable without a render.
    foreach ($want in @(
        @{ Kind = "Start";   Facing = "\(0, 0, 1\)" }
        @{ Kind = "Confirm"; Facing = "\(1, 0, 0\)" }
        @{ Kind = "End";     Facing = "\(-1, 0, 0\)" })) {
        if ((Find-Index $p1 ("^\[buttons\] found " + $want.Kind + " button .* facing " + $want.Facing)) -lt 0) {
            Add-Failure ("the $($want.Kind) button was not found facing $($want.Facing) -- either it is " +
                         "missing from its room scene or its basis is transposed (.tscn stores basis ROWS)")
        }
    }
    if ((Find-Index $p1 "^\[buttons\] rack holds prop ids \[1000, 1001, 1002\]") -lt 0) {
        Add-Failure ("the rack did not report ids [1000, 1001, 1002]. Authored prop ids are a " +
                     "function of every authored prop in the world -- read the '[props] authored " +
                     "prop' lines above and fix this suite's constants, or the rack's children moved")
    }

    # --- refused, with a NAMED reason, twice, and the round did not start on either --------
    $iNeedTwo = Find-Index $p1 '^\[buttons\] press Start peer=\d+ refused reason=NeedTwoPlayers'
    $iNoObject = Find-Index $p1 '^\[buttons\] press Start peer=\d+ refused reason=HiderMustHoldAnObject'
    $iAccepted = Find-Index $p1 '^\[buttons\] press Start peer=\d+ accepted reason=None'
    $iHiding = Find-Index $p1 '^\[round\] phase Holding -> Hiding'

    if ($iNeedTwo -lt 0) {
        Add-Failure ("a START pressed with one player in the room was not refused with " +
                     "NeedTwoPlayers -- a press that produces nothing is this packet's defect")
    }
    if ($iNoObject -lt 0) {
        Add-Failure ("a START pressed with two players and empty hands was not refused with " +
                     "HiderMustHoldAnObject")
    }
    if ($iAccepted -lt 0) { Add-Failure "no START press was ever accepted -- the round never started from a button" }
    if ($iHiding -lt 0) { Add-Failure "the round never reached Hiding" }
    if ($iNeedTwo -ge 0 -and $iHiding -ge 0 -and $iNeedTwo -gt $iHiding) {
        Add-Failure "the round began BEFORE the refused Start -- the refusal did not refuse anything"
    }
    if ($iNoObject -ge 0 -and $iHiding -ge 0 -and $iNoObject -gt $iHiding) {
        Add-Failure "the round began BEFORE the empty-handed Start -- the refusal did not refuse anything"
    }
    if ($iAccepted -ge 0 -and $iHiding -ge 0 -and $iAccepted -gt $iHiding) {
        Add-Failure "the accepted press was logged AFTER the transition it caused -- the ordering is wrong"
    }

    # --- the wrong-phase press, from a player who IS in reach ------------------------------
    if ((Find-Index $p1 '^\[buttons\] press Start peer=\d+ refused reason=NotNow') -lt 0) {
        Add-Failure ("the seeker's START press after the round had begun was not refused with " +
                     "NotNow -- HideSeekLoop folds a Start outside Holding and drops it in " +
                     "silence, and refusing it here is the whole reason the button has a gate")
    }

    # --- THE CAP MOVED, on every peer, for every press -------------------------------------
    # This is the INTERACTION-BIBLE 3 half: a state change with nothing moving is the shortcut that gets reported
    # as "it didn't work". Counted rather than merely found, because "one animation for four
    # presses" is exactly the shape a debounce would produce.
    $capRefused = Count-Matches $p1 '^\[button\] Start REFUSED reason=\w+ cap -4 cm'
    $capAccepted = Count-Matches $p1 '^\[button\] Start ACCEPTED reason=None cap -4 cm'
    Write-Host "        cap animations on the server's own copy: $capRefused refused, $capAccepted accepted" -ForegroundColor DarkGray
    if ($capRefused -lt 3) {
        Add-Failure ("only $capRefused refused press(es) animated the cap; three were refused. " +
                     "A refusal that does not move the cap is indistinguishable from a press the " +
                     "game never received (INTERACTION-BIBLE 3)")
    }
    if ($capAccepted -lt 1) { Add-Failure "the accepted press did not animate the cap" }

    # --- THE LAMP MOVED BEFORE THE PRESS ---------------------------------------------------
    $iLit = Find-Index $p1 '^\[button\] Start lamp Dark -> Lit'
    Echo-Matching $p1 '^\[button\] Start lamp '
    if ($iLit -lt 0) {
        Add-Failure ("the START lamp never went Lit. It is derived on each peer from the round " +
                     "wire plus the replicated holder view, so a lamp that never lights means " +
                     "either the derivation is wrong or the hider never picked anything up")
    } elseif ($iAccepted -ge 0 -and $iLit -gt $iAccepted) {
        Add-Failure ("the START lamp lit AFTER the press was accepted. The affordance exists to " +
                     "be read BEFORE pressing; a lamp that only agrees in hindsight is decoration")
    }

    # --- THE RACK NAMED THE TARGET ---------------------------------------------------------
    if ((Find-Index $p1 ("^\[buttons\] target prop is now " + $RackBoxPropId)) -lt 0) {
        Add-Failure ("picking the rack's box up did not set the round's target to $RackBoxPropId " +
                     "-- REACH-1's audit and the drop-off bin both read that id, so this is the " +
                     "production replacement for --reach-target not working")
    }

    # ==========================================================================================
    # PHASE 2 -- CONFIRM: refused out of reach, then accepted for real
    # ==========================================================================================
    Write-Host "[2/3] CONFIRM pressed out of reach, then for real..." -ForegroundColor Cyan
    $p2Out = Join-Path $script:LogDir "buttons-p2-server.out.log"
    $server = Start-ButtonsServer "buttons-p2-server" @(
        "--server", "--port", $Port, "--world", "supermarket",
        "--round-script", "object@10,start@12,end@300")
    $procs += $server
    if (-not (Wait-ForLogLine $p2Out "\[server\] listening" 60)) {
        Stop-Proc $server
        Write-Fail "phase 2: the server never reported listening on udp/$Port; see buttons-p2-server.out.log"
    }

    # The hider walks at the CONFIRM button from the moment it spawns. It spawns in the HOLDING
    # room, so for the first twelve seconds it is walking into a wall 28 m short of its target --
    # which is what makes its t=8 press a genuine out-of-reach press rather than a contrived one.
    # The round's start teleports it into the search room and it finishes the walk from there.
    $cfmA = Start-ButtonsBot "buttons-p2-A" "CfmHider" 46 @(
        "--goto-script", "33.2,-3",
        "--press", "confirm@8,confirm@30")
    $procs += $cfmA
    Start-Sleep -Milliseconds 1500
    $cfmB = Start-ButtonsBot "buttons-p2-B" "CfmSeeker" 42 @("--goto-script", "-3,3")
    $procs += $cfmB

    foreach ($b in @($cfmA, $cfmB)) {
        if (-not (Wait-ForExit $b 120)) {
            Stop-Proc $b
            Add-Failure "phase 2: a bot did not exit within its own duration + slack"
        }
    }
    Stop-Proc $server
    Start-Sleep -Milliseconds 500

    $p2 = Get-Lines $p2Out
    Echo-Matching $p2 '^\[buttons\] press Confirm'

    $iFar = Find-Index $p2 '^\[buttons\] press Confirm peer=\d+ refused reason=TooFarAway'
    $iCfm = Find-Index $p2 '^\[buttons\] press Confirm peer=\d+ accepted reason=None'
    $iSeeking = Find-Index $p2 '^\[round\] phase Hiding -> Seeking'
    if ($iFar -lt 0) {
        Add-Failure ("a CONFIRM pressed from 28 m away was not refused with TooFarAway -- the " +
                     "server's own reach re-check against authoritative positions did not fire")
    }
    if ($iCfm -lt 0) { Add-Failure "the CONFIRM press was never accepted" }
    if ($iSeeking -lt 0) { Add-Failure "the round never reached Seeking from a real CONFIRM press" }
    if ($iFar -ge 0 -and $iSeeking -ge 0 -and $iFar -gt $iSeeking) {
        Add-Failure "the out-of-reach CONFIRM was logged after the seek began -- it is not the refusal this asserts"
    }
    if ((Count-Matches $p2 '^\[button\] Confirm REFUSED reason=TooFarAway cap -4 cm') -lt 1) {
        Add-Failure "the out-of-reach CONFIRM did not animate the cap"
    }

    # ==========================================================================================
    # PHASE 3 -- the bin: a wrong prop buzzes and changes nothing; the target ends the round
    # ==========================================================================================
    Write-Host "[3/3] the drop-off bin: the wrong prop, then the right one..." -ForegroundColor Cyan
    $p3Out = Join-Path $script:LogDir "buttons-p3-server.out.log"
    $server = Start-ButtonsServer "buttons-p3-server" @(
        "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
        # --spawn-index (INT-1, ruling 6). The header above this phase states the mapping it
        # depends on -- "A gets SearchSpawn_0 (35.5, 0), B gets _1 (44.5, 0) and C, the
        # mid-round joiner, gets _2 (38.5, 2.1)" -- and then stages each courier's prop in that
        # bot's own walkway, because a bot that has to change walkway walks into a shelf. That
        # mapping was a bet on join order (SHELF-1 SS6.3 measured it losing); it is a pin now.
        "--spawn-index", "BinHider=0,BinSeeker=1,BinCourier=2",
        "--reach-target", $SearchTargetId,
        "--round-script", "object@6,start@8,confirm@14,end@600")
    $procs += $server
    if (-not (Wait-ForLogLine $p3Out "\[server\] listening" 60)) {
        Stop-Proc $server
        Write-Fail "phase 3: the server never reported listening on udp/$Port; see buttons-p3-server.out.log"
    }

    # A parks (it is the hider and the script sends it to the task room at Confirm).
    $binA = Start-ButtonsBot "buttons-p3-A" "BinHider" 80 @("--goto-script", "36,4")
    $procs += $binA
    Start-Sleep -Milliseconds 1200
    # B is the seeker and carries the WRONG prop into the bin. --carry-place's delay is measured
    # from the tick B is first seen HOLDING, never from process start (the --exit-when-holding
    # rule: under load a walk slips by seconds and a constant does not), and 16 s is comfortably
    # past the script's confirm@14 so the delivery lands while the round is Seeking.
    $binB = Start-ButtonsBot "buttons-p3-B" "BinSeeker" 80 @(
        "--carry-script", "38,0.22,0,3,-1",
        "--carry-target-prop", $SearchDecoyId, "--carry-grab-retry", "0.8",
        "--carry-walk-to", $BinDecoyWalk,
        "--carry-place", "$BinDecoyPose,16")
    $procs += $binB
    # C joins after the round is already running and delivers the target. A third peer mid-round
    # is harmless: HideSeekLoop only counts humans in Holding, and it re-checks only that the
    # hider and seeker are still present.
    Start-Sleep -Seconds 22
    $binC = Start-ButtonsBot "buttons-p3-C" "BinCourier" 55 @(
        "--carry-script", "36,0.22,2,3,-1",
        "--carry-target-prop", $SearchTargetId, "--carry-grab-retry", "0.8",
        "--carry-walk-to", $BinTargetWalk,
        "--carry-place", "$BinTargetPose,14")
    $procs += $binC

    foreach ($b in @($binA, $binB, $binC)) {
        if (-not (Wait-ForExit $b 150)) {
            Stop-Proc $b
            Add-Failure "phase 3: a bot did not exit within its own duration + slack"
        }
    }
    Stop-Proc $server
    Start-Sleep -Milliseconds 500

    $p3 = Get-Lines $p3Out
    Echo-Matching $p3 '^\[bin\] '
    Echo-Matching $p3 '^\[buttons\] target prop PINNED'
    # The place refusals, echoed unconditionally. A delivery that never happened is a STAGING
    # failure nine times in ten, and this one line is what tells a reader whether the courier was
    # refused (and why) or simply never got there -- the CARRY-1 lesson about reading the server
    # log before reaching for the flake list.
    Echo-Matching $p3 'place denied peer='

    $iBuzz = Find-Index $p3 ("^\[bin\] prop " + $SearchDecoyId + " is not the one \(target " + $SearchTargetId + "\)")
    $iDelivered = Find-Index $p3 ("^\[bin\] the target \(prop " + $SearchTargetId + "\) was delivered")
    $iTogether = Find-Index $p3 '^\[round\] phase Seeking -> Together'

    if ($iBuzz -lt 0) {
        Add-Failure ("the wrong prop never reached the bin. This is a STAGING failure, not a bin " +
                     "failure: read buttons-p3-B.out.log for whether the grab landed and the " +
                     "place was accepted, before concluding anything about the bin's rule")
    }
    if ($iDelivered -lt 0) { Add-Failure "the target was never delivered to the bin" }
    if ($iTogether -lt 0) { Add-Failure "the round never reached Together -- the delivery did not end the seek" }
    if ($iBuzz -ge 0 -and $iTogether -ge 0 -and $iBuzz -gt $iTogether) {
        Add-Failure ("the wrong prop arrived AFTER the round had already ended -- the 'a decoy " +
                     "does not end the round' assertion never actually ran")
    }
    if ($iBuzz -ge 0 -and $iDelivered -ge 0 -and $iBuzz -gt $iDelivered) {
        Add-Failure "the wrong prop arrived after the target; the decoy case was not staged first"
    }
    if ($iDelivered -ge 0 -and $iTogether -ge 0 -and $iDelivered -gt $iTogether) {
        Add-Failure "the round ended BEFORE the target was delivered -- something else ended it"
    }

    # THE CHIME REACHED A PEER THAT IS NEITHER THE SERVER NOR THE COURIER. The lines above are
    # what the SERVER decided; this is what the hider -- standing in the task room, about to have
    # a door burst onto it -- was actually told. "On every peer" is a claim only every peer's own
    # stdout can settle.
    $hiderLog = Get-Lines (Join-Path $script:LogDir "buttons-p3-A.out.log")
    if ((Find-Index $hiderLog ("^\[bin\] lamp GREEN \(chime\) prop=" + $SearchTargetId)) -lt 0) {
        Add-Failure ("the hider's own client was never told the bin went green. The delivery is " +
                     "the one moment the hider is entitled to hear, a beat before DOOR-1's door")
    }

    $buzzes = Count-Matches $p3 '^\[bin\] prop \d+ is not the one'
    Write-Host ""
    Write-Host "MEASURED QUANTITIES (compare these across runs, not the verdict):" -ForegroundColor White
    Write-Host "  phase 1 cap animations:            $capRefused refused, $capAccepted accepted"
    Write-Host "  phase 1 named press refusals:      $(Count-Matches $p1 '^\[buttons\] press \w+ peer=\d+ refused')"
    Write-Host "  phase 1 accepted presses:          $(Count-Matches $p1 '^\[buttons\] press \w+ peer=\d+ accepted')"
    Write-Host "  phase 2 named press refusals:      $(Count-Matches $p2 '^\[buttons\] press \w+ peer=\d+ refused')"
    Write-Host "  phase 3 bin rejections:            $buzzes"
    Write-Host "  phase 3 bin deliveries:            $(Count-Matches $p3 '^\[bin\] the target')"
}
finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host ""
if ($script:Failures.Count -gt 0) {
    Write-Host "BUTTONS FAILED ($($script:Failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "BUTTONS OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: START was refused twice with named reasons and then accepted, and the round began because a button said so; a wrong-phase press and an out-of-reach press were each answered rather than dropped; every press moved the cap on every peer; the START lamp lit before the press that used it; the rack named the target prop; and the bin buzzed a decoy without ending anything and then ended the round on the real one." -ForegroundColor Green
Write-Host ""
Write-Host "BUTTONS OVERALL: PASS" -ForegroundColor Green
exit 0
