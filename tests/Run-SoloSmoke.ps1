<#
.SYNOPSIS
    SOLO-1: the whole round, run end to end on a real server by ONE player, under --solo. Proves
    the thing the engine-free tier cannot: that a session with a single peer in it walks every
    edge of the loop in a live process -- both teleport destinations, the burst edge into an empty
    task room, two whole rounds, a match card on a one-row board, and a third Start.

.DESCRIPTION
    Everything about solo that is a RULE is tested engine-free in tests/unit/SoloRoundTests.cs
    (one peer holding both roles, the Start gate with and without the flag, the card crediting
    both gains to one person, the draw on a one-row board, the roles swapping to the same peer,
    a second player waiting and then being dealt in). None of it is repeated here.

    WHAT IS LEFT IS WHAT NEEDS A SERVER, AND IT IS FOUR THINGS:

      1. THE FLAG REACHES THE LOOP. --solo is parsed by LaunchOptions, carried on HideSeekTuning
         and read inside HideSeekLoop's Holding branch. Three files and a Godot launch between the
         command line and the behaviour, none of which a unit test crosses. The server says
         "[round] DEV --solo armed" and then lets one player start a round; without the flag the
         same session refuses with NeedTwoPlayers, and this suite runs BOTH so the pass is a
         difference rather than an absence.

      2. THE SOLO PLAYER IS TELEPORTED TO THE RIGHT ROOM ON EVERY EDGE, and in particular is NOT
         sent to the task room at Confirm. One peer holds both roles, so a driver that decided the
         hider's move and then the seeker's would put it in the task room and have the search move
         refused inside RoomTeleport's 800 ms cooldown -- the only player in the game standing in
         the wrong room for most of a second, which no assertion about phases would ever see. This
         asserts the server's own decided destinations: search at Hiding, search at Seeking,
         vestibule at Together, holding at the reset. The task room is never a destination.

      3. THE BURST EDGE STILL HAPPENS WITH NOBODY ON THE OTHER SIDE OF THE DOOR. Seeking ->
         Together is the payoff the whole game is built around and in solo it fires into an empty
         room. The loop must run every edge anyway (the packet's words), so the transition, the
         teleport to the vestibule and the card are all asserted exactly as the two-player smoke
         asserts them.

      4. A MATCH IS TWO ROUNDS HERE TOO, AND ITS CARD IS A DRAW. Both totals on a solo card are
         copied out of the same peer's row, so they are equal and there is no winner to name --
         which is the honest answer and, more to the point, is what a one-row board has to survive
         without crashing. Asserted off the BOT'S OWN folded card, not the server's state.

    NOT ONE WALL-CLOCK GUESS IN THE ASSERTIONS. Every window is derived from the SERVER'S OWN LOG
    -- one line per decided move, one per phase change, one per card -- exactly as
    Run-RoundLoopSmoke.ps1 does, and the bot's lifetime is a CEILING over the schedule it must
    outlive rather than a number typed beside it.

    THE NORMAL SMOKE IS UNTOUCHED. Run-RoundLoopSmoke.ps1 still runs two bots without the flag and
    still asserts everything it always did; this is a separate suite on a separate port because
    the two prove different things and a run that could not tell you which half failed would be
    worth less than either.

    Exit 0 = PASS. Headless throughout -- no GPU, no display. One Godot at a time: this suite takes
    the machine-wide suite mutex like every other.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param(
    # 7912. Taken from the ONE ladder table in .claude/rules/test-suite.md, which STOCK-1 left
    # saying "the next free number is 7912" -- read, not computed from a snapshot of tests/, and
    # the claim is recorded in that table in the same commit as this file. That is INT-0's
    # measured lesson (three lanes off one base all picked 7896) and REVIEW-1's correction to it
    # (a suite that binds a port takes a row whether or not it is registered), applied rather
    # than re-learned for a seventh time.
    #
    # THIS WAVE HAS TWO OTHER LANES IN IT (FEEL-1, SICK-1) and neither one's tests/ is visible
    # from here, so if either also claims 7912 the merge has to move one of them. INT-2 owns that
    # reconciliation; nothing else in this script depends on the value.
    [int]$Port = 7912,
    [int]$MutexTimeoutMinutes = 120,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# How close a body has to be to the destination the server named to count as ARRIVED. The same
# 3 m Run-RoundLoopSmoke.ps1 derives: a teleport lands in one frame, so this is a bound on how far
# the bot's own deterministic walk carries it before the next sample, not a tolerance on the move.
$ArrivalRadiusM = 3.0

# The schedule, in seconds from the FIRST PLAYER ARRIVING (the driver's script-clock gate), so
# these are margins over connection time rather than guesses about it. It is Run-RoundLoopSmoke's
# own shape with the two-player beats removed, deliberately: if solo diverges from the ordinary
# round anywhere except in who holds the roles, the two schedules stop matching and that is
# itself the finding.
#
#   4   a Start with empty hands   -> refused, named, the round does not begin. The control for
#                                     "--solo just accepts everything".
#   8   Start                      -> Hiding, the one peer to the SEARCH room
#   14  Confirm                    -> Seeking, the same peer to the SEARCH room (never the task
#                                     room -- assertion 2 above)
#   18  sorts:3                    -> its hiding half's score
#   22  the object is in the bin   -> Together, the same peer to the VESTIBULE, burst into an
#                                     empty task room
#   28  End                        -> Tally, card for round 1, matchOver=FALSE
#   +6 s                           -> the reset edge, home, roles "swap" onto the same peer
#   36  lost                       -> THE BIN IS EMPTIED. Mandatory: `found` is a LEVEL, not a
#                                     pulse, so round 2 started with it still set would walk
#                                     Hiding -> Seeking -> Together in one tick.
#   40  Start                      -> Hiding, round 2
#   44  sorts:1                    -> a different count, so the two cards cannot be confused
#   46  Confirm                    -> Seeking
#   58  the object is in the bin   -> Together
#   63  End                        -> Tally, card for round 2, matchOver=TRUE, DRAW
#   +10 s                          -> the reset edge
#   78  Start                      -> round 3, match 2, and the score goes back to zero
$Script = ("noobject@3,start@4,object@6,start@8,confirm@14,sorts:3@18,found@22,end@28," +
           "lost@36,start@40,sorts:1@44,confirm@46,found@58,end@63,start@78")

# A CEILING over the schedule's last beat (78) plus enough settle to log round 3's Hiding with the
# score already zeroed. Every assertion below is anchored on a server log line or on a card.
$BotDurationSec = 95

# Phase 2's control: no --solo, one bot, and the round must NOT start. Its only job is to make the
# pass in phase 1 a difference rather than an absence, so it needs to outlive the first two Start
# beats and nothing more.
$ControlDurationSec = 30

Write-Host "=== SOLO-1: one player runs the whole round ===" -ForegroundColor White

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
$script:Failures = @()
function Add-Failure([string]$m) { $script:Failures += $m }

function Read-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { return @() }
    $out = @()
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $out += ,(ConvertFrom-Json $line) } catch { }
    }
    return $out
}
function Dist3([double]$ax, [double]$ay, [double]$az, [double]$bx, [double]$by, [double]$bz) {
    $dx = $ax - $bx; $dy = $ay - $by; $dz = $az - $bz
    return [math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
}
function Get-PeerRow($Sample, [long]$PeerId) {
    foreach ($p in @($Sample.peers)) { if ([long]$p.id -eq $PeerId) { return $p } }
    return $null
}

try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    # ==========================================================================================
    # PHASE 1 -- the solo round, twice over, on a server launched WITH --solo
    # ==========================================================================================
    Write-Host "[1/3] dedicated server on udp/$Port with --solo, world supermarket..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "solosmoke-server.out.log"
    $server = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket",
        "--solo", "--round-script", $Script) `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "solosmoke-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server

    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "the server never reported listening on udp/$Port; see solosmoke-server.out.log"
    }
    Write-Host "        server up (pid $($server.Id))"

    Write-Host "[2/3] ONE bot for ${BotDurationSec}s..." -ForegroundColor Cyan
    $soloJson = Join-Path $script:LogDir "solosmoke-Solo.jsonl"
    $soloBot = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--bot", "--address", "127.0.0.1:$Port", "--name", "SoloA",
        "--log", $soloJson, "--duration", $BotDurationSec, "--world", "supermarket") `
        -RedirectStandardOutput (Join-Path $script:LogDir "solosmoke-Solo.out.log") `
        -RedirectStandardError (Join-Path $script:LogDir "solosmoke-Solo.err.log") `
        -PassThru -NoNewWindow
    $null = $soloBot.Handle
    $procs += $soloBot

    if (-not (Wait-ForExit $soloBot ($BotDurationSec + 60))) {
        Stop-Proc $soloBot
        Add-Failure "the solo bot did not exit within its own duration + 60 s"
    }
    Stop-Proc $server
    Start-Sleep -Milliseconds 500

    # ==========================================================================================
    # PHASE 2 -- THE CONTROL: the same one bot, the same script, WITHOUT --solo
    #
    # Without this, "one player started a round" is an absence rather than a difference, and a
    # build in which the two-player gate had simply stopped working would pass phase 1 exactly as
    # a correct one does. This is the measured discipline .claude/rules/test-suite.md records for
    # headless probes and for FootstepAudioTests' positive control, applied to a launch flag.
    # ==========================================================================================
    Write-Host "[3/3] the control: the same one bot with NO --solo, which must be refused..." -ForegroundColor Cyan
    $ctrlOut = Join-Path $script:LogDir "solosmoke-control-server.out.log"
    $ctrlServer = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket",
        "--round-script", "object@4,start@6,start@14") `
        -RedirectStandardOutput $ctrlOut `
        -RedirectStandardError (Join-Path $script:LogDir "solosmoke-control-server.err.log") `
        -PassThru -NoNewWindow
    $null = $ctrlServer.Handle
    $procs += $ctrlServer

    if (-not (Wait-ForLogLine $ctrlOut "\[server\] listening" 60)) {
        Stop-Proc $ctrlServer
        Write-Fail "the control server never reported listening on udp/$Port; see solosmoke-control-server.out.log"
    }
    $ctrlBot = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--bot", "--address", "127.0.0.1:$Port", "--name", "CtrlA",
        "--duration", $ControlDurationSec, "--world", "supermarket") `
        -RedirectStandardOutput (Join-Path $script:LogDir "solosmoke-control-bot.out.log") `
        -RedirectStandardError (Join-Path $script:LogDir "solosmoke-control-bot.err.log") `
        -PassThru -NoNewWindow
    $null = $ctrlBot.Handle
    $procs += $ctrlBot
    if (-not (Wait-ForExit $ctrlBot ($ControlDurationSec + 60))) {
        Stop-Proc $ctrlBot
        Add-Failure "the control bot did not exit within its own duration + 60 s"
    }
    Stop-Proc $ctrlServer
    Start-Sleep -Milliseconds 500

    # ==========================================================================================
    # Parsing
    # ==========================================================================================
    if (-not (Test-Path $serverOut)) { Write-Fail "no server log was written at all" }
    $serverLines = @(Get-Content $serverOut)

    $moves = @()
    foreach ($line in $serverLines) {
        if ($line -match '\[round\] peer (\d+) -> (\w+)\[(\d+)\] \(([-0-9.eE]+), ([-0-9.eE]+), ([-0-9.eE]+)\)') {
            $moves += ,@{
                Peer = [long]$matches[1]; Room = $matches[2]
                X = [double]$matches[4]; Y = [double]$matches[5]; Z = [double]$matches[6]
            }
        }
    }
    $phases = @()
    foreach ($line in $serverLines) {
        if ($line -match '\[round\] phase (\w+) -> (\w+)') { $phases += ,@{ From = $matches[1]; To = $matches[2] } }
    }
    $refusals = @()
    foreach ($line in $serverLines) {
        if ($line -match '\[round\] refused: (\w+)') { $refusals += $matches[1] }
    }
    $cardLines = @()
    foreach ($line in $serverLines) {
        if ($line -match '\[round\] card: round (\d+) match (\d+) hider=(\d+) \+(\d+) seeker=(\d+) \+(\d+) totals (\d+)-(\d+) matchOver=(\w+) winner=(-?\d+)') {
            $cardLines += ,@{ Round = [int]$matches[1]; Match = [int]$matches[2]
                              Hider = [long]$matches[3]; HiderGain = [int]$matches[4]
                              Seeker = [long]$matches[5]; SeekerGain = [int]$matches[6]
                              HiderTotal = [int]$matches[7]; SeekerTotal = [int]$matches[8]
                              Over = ($matches[9] -eq "True"); Winner = [long]$matches[10] }
        }
    }
    $samples = @(Read-Samples $soloJson)

    Write-Host ""
    Write-Host "        server phases: $([string]::Join('  ', @($phases | ForEach-Object { "$($_.From)->$($_.To)" })))" -ForegroundColor DarkGray

    # ==========================================================================================
    # 1. THE FLAG REACHED THE LOOP, and the round ran end to end for ONE player
    # ==========================================================================================
    if (@($serverLines | Where-Object { $_ -match '\[round\] DEV --solo armed' }).Count -eq 0) {
        Add-Failure ("the server never logged '[round] DEV --solo armed'. The flag did not reach " +
                     "HideSeekTuning -- read solosmoke-server.out.log for whether --solo landed " +
                     "AFTER the bare '--' (engine flags before it are swallowed silently).")
    }

    foreach ($want in @("Holding->Hiding", "Hiding->Seeking", "Seeking->Together",
                        "Together->Tally", "Tally->Holding")) {
        if (@($phases | ForEach-Object { "$($_.From)->$($_.To)" }) -notcontains $want) {
            Add-Failure ("the server never made the transition $want with one player present -- " +
                         "the solo round did not run end to end")
        }
    }

    # And the round did not simply accept everything: the scripted Start with empty hands is
    # still refused, by name, before the good one.
    if ($refusals -notcontains "HiderMustHoldAnObject") {
        Add-Failure ("the scripted Start with empty hands was not refused under --solo (server " +
                     "logged: $([string]::Join(',', $refusals))). The flag is supposed to relax " +
                     "ONE condition, not the branch.")
    }
    if ($refusals -contains "NeedTwoPlayers") {
        Add-Failure ("the server refused a Start with NeedTwoPlayers while --solo was armed -- " +
                     "the flag reached the log line and not the gate")
    }

    # ==========================================================================================
    # 2. ONE PEER, BOTH ROLES, AND THE TASK ROOM IS NEVER ITS DESTINATION
    # ==========================================================================================
    $selfId = $null
    $firstSample = @($samples) | Select-Object -First 1
    if ($null -ne $firstSample) { $selfId = [long]$firstSample.self }

    if (@($samples).Count -lt 10) {
        Add-Failure "the solo bot logged only $(@($samples).Count) sample(s) -- it never really ran"
    }
    if ($soloBot.ExitCode -ne 0) {
        # A bot that printed its own completion line and then died on the way out is a teardown
        # artifact, not a failing check (.claude/rules/test-suite.md, BASE-1 and INT-1's entries).
        $done = Select-String -Path (Join-Path $script:LogDir "solosmoke-Solo.out.log") `
            -Pattern "\[bot\] SoloA done" -Quiet
        if ($done) {
            Write-Host "        NOTE: the solo bot exited $($soloBot.ExitCode) AFTER printing its own done line -- teardown artifact, not a check" -ForegroundColor Yellow
        } else {
            Add-Failure "the solo bot exited $($soloBot.ExitCode) without finishing its run"
        }
    }

    $syncedRole = @($samples | Where-Object { [bool]$_.roundSynced -and [long]$_.roundHider -ne 0 })
    if ($syncedRole.Count -eq 0) {
        Add-Failure "the solo bot never folded a round message naming a hider"
    } else {
        $bad = @($syncedRole | Where-Object {
            [long]$_.roundHider -ne [long]$_.roundSeeker })
        if ($bad.Count -gt 0) {
            Add-Failure ("$($bad.Count) of $($syncedRole.Count) folded samples name a DIFFERENT " +
                         "hider and seeker in a one-player session (first: hider " +
                         "$($bad[0].roundHider) seeker $($bad[0].roundSeeker)) -- " +
                         "the solo role deal did not reach the wire")
        }
        if ($null -ne $selfId -and [long]$syncedRole[0].roundHider -ne $selfId) {
            Add-Failure ("the round names peer $($syncedRole[0].roundHider) as hider but the " +
                         "only bot is peer $selfId -- the roles were dealt to somebody who is not here")
        }
    }

    $roomsUsed = @{}
    foreach ($m in $moves) { if (-not $roomsUsed.ContainsKey($m.Room)) { $roomsUsed[$m.Room] = $m } }
    Write-Host "        rooms the server sent the solo player to: $([string]::Join(', ', @($roomsUsed.Keys)))" -ForegroundColor DarkGray

    foreach ($want in @("holding", "search", "vestibule")) {
        if (-not $roomsUsed.ContainsKey($want)) {
            Add-Failure ("the solo player was never moved to the '$want' room -- the round did not " +
                         "walk every edge for one player")
        }
    }
    # THE ONE THAT IS ABOUT SOLO SPECIFICALLY. The same peer holds both roles, so if the driver
    # decides the hider's Seeking move as well as the seeker's, the task move lands and the search
    # move is refused inside RoomTeleport's 800 ms cooldown -- the only player in the game spends
    # most of a second in the wrong room and no phase assertion can see it.
    if ($roomsUsed.ContainsKey("task")) {
        Add-Failure ("the server sent the solo player to the TASK room. In solo the one peer seeks, " +
                     "so its Seeking destination is the search room and the task room is the empty " +
                     "one the door bursts onto. Deciding both roles' moves puts it in the task " +
                     "room for a RoomTeleport cooldown (800 ms) before the retry fetches it back.")
    }

    # ==========================================================================================
    # 3. EVERY MOVE THE SERVER DECIDED ACTUALLY LANDED
    # ==========================================================================================
    $checked = 0
    $worst = 0.0
    foreach ($m in $moves) {
        if ($null -eq $selfId -or $m.Peer -ne $selfId) { continue }
        $best = [double]::MaxValue
        foreach ($s in @($samples)) {
            $row = Get-PeerRow $s $m.Peer
            if ($null -eq $row) { continue }
            $d = Dist3 ([double]$row.x) ([double]$row.y) ([double]$row.z) $m.X $m.Y $m.Z
            if ($d -lt $best) { $best = $d }
        }
        if ($best -eq [double]::MaxValue) { continue }
        $checked++
        if ($best -gt $worst) { $worst = $best }
        if ($best -gt $ArrivalRadiusM) {
            Add-Failure ("the solo player never came within $ArrivalRadiusM m of the '$($m.Room)' " +
                         "destination the server sent it to -- closest approach " +
                         "$([math]::Round($best,2)) m. A teleport that silently never happens " +
                         "reads in the TENS of metres here (ROUND-1 measured 38-80 m on a " +
                         "planted fault); a creep from ~1 m to ~2.5 m is a slow machine.")
        }
    }
    if ($checked -eq 0) {
        Add-Failure "not one server-decided move could be checked against the bot's own log -- the suite proved nothing"
    }

    # ==========================================================================================
    # 4. TWO ROUNDS, A MATCH CARD, AND IT IS A DRAW ON A ONE-ROW BOARD
    # ==========================================================================================
    function Get-CardsByRound($Set, [int]$PhaseFilter = -1) {
        $cards = @{}
        foreach ($s in @($Set)) {
            if (-not [bool]$s.roundSynced) { continue }
            if ($null -eq $s.roundTally) { continue }
            if ($PhaseFilter -ge 0 -and [int]$s.roundPhase -ne $PhaseFilter) { continue }
            $cards[[int]$s.roundTally.roundIndex] = $s
        }
        return ,$cards
    }
    $cards = Get-CardsByRound $samples
    $cardsAtTally = Get-CardsByRound $samples 4

    foreach ($round in @(1, 2)) {
        if (-not $cards.ContainsKey($round)) {
            Add-Failure ("the solo bot never folded round $round's card -- read " +
                         "solosmoke-server.out.log for whether that round reached Tally at all")
        }
    }

    if ($cards.ContainsKey(1)) {
        $c1 = $cards[1].roundTally
        Write-Host ("        card round 1: hider $($c1.hiderPeerId) +$($c1.hiderGained), " +
                    "seeker $($c1.seekerPeerId) +$($c1.seekerGained), " +
                    "totals $($c1.hiderTotal)-$($c1.seekerTotal), matchOver=$($c1.matchOver)") -ForegroundColor DarkGray
        if ([bool]$c1.matchOver) {
            Add-Failure "round 1's card says matchOver -- a match is two rounds, in solo as anywhere else"
        }
        if ([long]$c1.hiderPeerId -ne [long]$c1.seekerPeerId) {
            Add-Failure ("round 1's card names two different peers (hider $($c1.hiderPeerId), " +
                         "seeker $($c1.seekerPeerId)) in a one-player session")
        }
        if ([int]$c1.hiderGained -ne 3) {
            Add-Failure ("round 1's card credits the hiding half $($c1.hiderGained) sort(s); the " +
                         "script set 3 before the find, so the freeze at the Found tick did not happen")
        }
        if ([int]$c1.seekerGained -le 0) {
            Add-Failure ("round 1's card credits the seeking half $($c1.seekerGained) -- the seek " +
                         "clock had 180 s on it and the find landed inside 15 s, so this is a find " +
                         "that was read as a timeout")
        }
    }

    if ($cardsAtTally.ContainsKey(2)) {
        $s2 = $cardsAtTally[2]
        $c2 = $s2.roundTally
        Write-Host ("        card round 2: totals $($c2.hiderTotal)-$($c2.seekerTotal), " +
                    "matchOver=$($c2.matchOver), winner=$($c2.winnerPeerId)") -ForegroundColor DarkGray
        if (-not [bool]$c2.matchOver) {
            Add-Failure "round 2's card says matchOver=false -- two rounds is a whole match and the game never said it was over"
        }
        # A DRAW, and both totals equal, because both halves are the same person's row. This is
        # the assertion the packet's "nothing crashes on a one-row board" turns into.
        if ([string]$c2.winnerPeerId -ne "0") {
            Add-Failure ("the solo match card names peer $($c2.winnerPeerId) the winner. Both totals " +
                         "are copied out of ONE peer's row, so they are equal and a draw is the " +
                         "only honest result.")
        }
        if ([int]$c2.hiderTotal -ne [int]$c2.seekerTotal) {
            Add-Failure ("the solo match card's two totals differ ($($c2.hiderTotal) vs " +
                         "$($c2.seekerTotal)) although both are the same peer's cumulative score")
        }
        if ([int]$c2.hiderTotal -le 0) {
            Add-Failure ("the solo match card's totals are $($c2.hiderTotal) -- nothing was scored " +
                         "across two whole rounds, so the draw above proves nothing")
        }
        $rows = @($s2.roundScores.PSObject.Properties.Name)
        if ($rows.Count -ne 1) {
            Add-Failure ("the solo match card arrived with $($rows.Count) score row(s); a solo " +
                         "session has exactly one player and the board has to survive that")
        }
    } else {
        Add-Failure "the solo bot never folded round 2's card during its own Tally"
    }

    # The match card holds LONGER than the round card, read off the server's own two card lines.
    $roundCard = @($cardLines | Where-Object { -not $_.Over } | Select-Object -First 1)
    $matchCard = @($cardLines | Where-Object { $_.Over } | Select-Object -First 1)
    if ($roundCard.Count -eq 0 -or $matchCard.Count -eq 0) {
        Add-Failure ("the server logged $($cardLines.Count) card line(s) but not one of each kind " +
                     "(a round card and a match card); the solo match never completed")
    }

    # And the third Start began match 2 with the score back at zero.
    if (@($serverLines | Where-Object { $_ -match '\[round\] match 2 begins at round 3' }).Count -eq 0) {
        Add-Failure ("the server never logged match 2 beginning at round 3 -- the third Start did " +
                     "not start a new match for the solo player")
    }

    # ==========================================================================================
    # 5. THE CONTROL: without --solo, the same one bot is refused
    # ==========================================================================================
    $ctrlLines = if (Test-Path $ctrlOut) { @(Get-Content $ctrlOut) } else { @() }
    $ctrlRefusals = @()
    foreach ($line in $ctrlLines) {
        if ($line -match '\[round\] refused: (\w+)') { $ctrlRefusals += $matches[1] }
    }
    $ctrlStarted = @($ctrlLines | Where-Object { $_ -match '\[round\] phase Holding -> Hiding' }).Count

    if (@($ctrlLines | Where-Object { $_ -match '\[round\] DEV --solo armed' }).Count -gt 0) {
        Add-Failure ("the CONTROL server logged --solo armed although it was not given the flag -- " +
                     "Solo is leaking from somewhere other than the command line (a static? a " +
                     "default?), and phase 1 proves nothing until it is found")
    }
    if ($ctrlStarted -gt 0) {
        Add-Failure ("the control server started a round with ONE player and NO --solo. The flag is " +
                     "not what is letting the solo round begin -- the two-player gate is simply " +
                     "gone, and every assertion in phase 1 is satisfied by that.")
    }
    if ($ctrlRefusals -notcontains "NeedTwoPlayers") {
        Add-Failure ("the control server never logged a NeedTwoPlayers refusal (it logged: " +
                     "$([string]::Join(',', $ctrlRefusals))). Its script presses Start twice with " +
                     "one player present, so a silent no-op is the defect this names.")
    }

    Write-Host ""
    Write-Host "MEASURED QUANTITIES (compare these across runs, not the verdict):" -ForegroundColor White
    Write-Host "  server-decided moves checked against the bot's own log: $checked"
    Write-Host "  worst closest-approach to a named destination:          $([math]::Round($worst,2)) m (bar $ArrivalRadiusM m)"
    Write-Host "  phase transitions the server made:                      $($phases.Count)"
    Write-Host "  named refusals under --solo:                            $($refusals.Count) [$([string]::Join(',', $refusals))]"
    Write-Host "  cards the server committed:                             $($cardLines.Count) [$(@($cardLines | ForEach-Object { "r$($_.Round) matchOver=$($_.Over)" }) -join ', ')]"
    Write-Host "  rooms used:                                             $([string]::Join(', ', @($roomsUsed.Keys)))"
    Write-Host "  CONTROL (no --solo): rounds started / refusals:          $ctrlStarted / [$([string]::Join(',', $ctrlRefusals))]"
}
finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host ""
if ($script:Failures.Count -gt 0) {
    Write-Host "SOLO-SMOKE FAILED ($($script:Failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "SOLO-SMOKE OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: with --solo, ONE player ran Holding -> Hiding -> Seeking -> Together -> Tally -> Holding on a real server, twice, and then started a third round; it held both roles on the wire, was teleported to the search room and then the vestibule and never to the task room, arrived everywhere the server sent it, and its match card is a draw on a one-row board with both totals equal and non-zero. A Start with empty hands was still refused by name. WITHOUT the flag, the same single bot was refused NeedTwoPlayers and no round began." -ForegroundColor Green
Write-Host ""
Write-Host "SOLO-SMOKE OVERALL: PASS" -ForegroundColor Green
exit 0
