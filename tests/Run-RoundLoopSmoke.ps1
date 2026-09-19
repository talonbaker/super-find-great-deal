<#
.SYNOPSIS
    ROUND-1: the round, driven once end to end on a real server with two real clients. Proves the
    three things only a live session can prove -- that each peer is TELEPORTED INTO THE RIGHT ROOM
    on every phase change, that the round reaches its own reset edge, and that both peers hold the
    SAME round state at Tally.

.DESCRIPTION
    Everything about the loop that is a RULE is tested engine-free in tests/unit
    (HideSeekLoopTests: every transition, every named refusal, both timer-expiry paths, the grace
    extension, a role holder disconnecting, the role swap, score accumulation, the wire's fold).
    None of that is repeated here. This suite exists for the half a pure function cannot reach:

      1. THE TELEPORTS LAND. The server decides a destination on each phase change and calls
         RoomTeleport, which bumps the owner's prediction epoch so the body snaps rather than
         rubber-banding 40 m. This asserts each bot's OWN LOGGED POSITION converges on the room
         the server named -- not that the call was made.
      2. THE ROOMS ARE ACTUALLY DIFFERENT PLACES. Every destination the server logged is checked
         against every other: no two of the three rooms may come within RoomSeparationM, and each
         arrival must be NEARER its own destination than any other. Without that pair, "the bot is
         near its destination" would stay green in a world whose rooms sat on top of each other.
      3. BOTH PEERS AGREE AT TALLY. Two independent JSONL logs, compared field by field on the
         round columns BotHarness samples -- phase, round index, both roles, the score map and the
         frozen card. That is the absolute-wire property in a live session: one message, and every
         peer that applied it holds the same view.
      4. A MATCH IS TWO ROUNDS AND THEN SOMEBODY HAS WON (MATCH-1). The script runs TWO whole
         rounds and then a third Start. Asserted from both bots' OWN logs rather than from the
         server's: matchOver FALSE on round 1's card and TRUE on round 2's, the winner id equal
         to whichever peer actually holds the higher total on the wire, the match tally armed
         longer than the round tally, and every score row back at zero after the third Start.
         The negative half is the point -- an implementation that ended a match every round
         passes "round 2 ended one" on its own, and so does one that never reset a score.

    HOW IT IS DRIVEN. --round-script (a dev flag, server-side, additive-only -- see
    ScriptedRoundFactSource) feeds the facts BTN-1's buttons, CARRY-1's bin and TASK-1's towers
    will feed later. The script's clock starts when the first player arrives rather than at server
    boot, so the schedule is not racing the clients' own launch -- the "derive a lifetime from the
    schedule it must outlive" rule from .claude/rules/test-suite.md, applied to the schedule
    itself.

    NOT ONE WALL-CLOCK GUESS IN THE ASSERTIONS. Every window this suite samples in is derived from
    the SERVER'S OWN LOG: the driver prints one line per decided move
    ("[round] peer N -> search[0] (35.5, 1.1, 0)") and one per phase change, each with the server's
    wall clock beside it in the bot logs' comparable Unix-ms field. A bot's position is judged
    strictly AFTER the move that was meant to put it there, never at a typed second.

    THE REFUSALS ARE EXERCISED TOO. The script presses Start with the hider's hands empty before it
    presses it properly, so the server must log a named refusal and the round must NOT begin on it.
    That is also what puts a sentence on the HUD strip (the packet asks for the strip's refusal
    line to be triggered from the dev script to prove it renders); the strip is client-side chrome
    a headless bot never draws, so what is asserted here is the round state behind it -- the
    refusal reaching both peers on the wire.

    PROVED ABLE TO FAIL. See the DEVIATIONS / EVIDENCE block in
    docs/agents/handoffs/2026-09-19-ROUND-1.md for the planted fault and what it printed.

    Exit 0 = PASS. Headless throughout -- no GPU, no display. One Godot at a time: this suite takes
    the machine-wide suite mutex like every other.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param(
    # Unique across tests/. 7893/7894 are Run-CarryNetTest's two phases and 7895 is reserved for
    # the carry-through-a-teleport phase that suite is owed back (see Run-CarryNetTest's header);
    # this is the next one along.
    [int]$Port = 7896,
    [int]$MutexTimeoutMinutes = 30,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# How close a body has to be to the destination the server named to count as ARRIVED. A teleport
# lands in one frame, so this is not a tolerance on the move -- it is a bound on how far the bot's
# own deterministic walk carries it away before the next sample. Measured on the first green run:
# worst closest-approach 1.10 m across all six moves, so 3 m is three times the observed figure
# and still an order of magnitude inside the nearest ambiguity (see $VestibuleClearanceM).
$ArrivalRadiusM = 3.0

# No two of the three ROOMS may come within this. BASE-1's own level self-test asserts a 30 m floor
# between them for the voice-cutoff reason; this is the same property re-derived from the
# destinations the ROUND driver actually used, so a room moved in the editor fails here too.
#
# THE VESTIBULE IS DELIBERATELY NOT IN THAT SET. It is a 3 x 3 m alcove behind the task room's +Z
# wall -- the far side of the door DOOR-1 will burst -- so it is SUPPOSED to be adjacent, and the
# first run of this suite measured it 9.73 m from the task room's marker. What matters for it is a
# different property: that "the seeker reached the vestibule" is distinguishable from "the seeker
# is still in the task room", which needs clearance against the arrival radius rather than room
# separation.
$RoomSeparationM = 20.0
$VestibuleClearanceM = 2.0 * $ArrivalRadiusM

# The schedule. Seconds from the FIRST PLAYER ARRIVING (see the driver's script-clock gate), so
# these are margins over connection time, not guesses about it.
#
# ROUND 1 -- the round, and the named refusal in front of it
#   4  a Start with empty hands -> refused, named, round does not begin
#   8  Start properly            -> Hiding, hider to the search room
#   14 Confirm                   -> Seeking, hider to the task room, seeker to the search room
#   18 towers:3                  -> the hider's score for this round
#   22 the object is in the bin  -> Together, seeker to the vestibule
#   28 End                       -> Tally, card for round 1, matchOver=FALSE, 6 s of tally
#   +6 s                         -> the reset edge, everyone home, ROLES SWAP
#
# ROUND 2 -- the other half of match 1 (MATCH-1)
#   36 lost                      -> THE BIN IS EMPTIED. Mandatory, and it is the one thing the
#                                   packet's own two-round script is missing: `found` is a LEVEL,
#                                   not a pulse (a bin holding the target keeps holding it), so a
#                                   second round started with it still set walks Hiding -> Seeking
#                                   -> Together in one tick and never tests anything.
#   40 Start                     -> Hiding, round 2
#   44 towers:1                  -> a DIFFERENT tower count, so the two cards cannot be confused
#                                   for each other and the two totals cannot come out equal
#   46 Confirm                   -> Seeking
#   58 the object is in the bin  -> Together
#   63 End                       -> Tally, card for round 2, matchOver=TRUE, 10 s of MATCH tally
#   +10 s                        -> the reset edge
#
# ROUND 3 -- the Start that begins match 2
#   78 Start                     -> Hiding, round 3, AND THE SCORES GO BACK TO ZERO. This is the
#                                   only observable difference between the Start that begins a
#                                   match and the Start that begins a round, which is why the
#                                   suite has to run a third one to see it at all.
#
# The expected arithmetic, at the shipping tuning (seek 180 s):
#   round 1: hider +3, seeker +172 (180 - 8)   -> A=3,   B=172
#   round 2: hider +1, seeker +168 (180 - 12)  -> B=173, A=171   (roles swapped)
#   match 1: B WINS 173-171
# Both totals stay under the wire's 255 byte clamp on purpose.
$Script = ("noobject@3,start@4,object@6,start@8,confirm@14,towers:3@18,found@22,end@28," +
           "lost@36,start@40,towers:1@44,confirm@46,found@58,end@63,start@78")

# Bot lifetime: the schedule's last beat (78) plus a settle long enough to log a dozen samples of
# round 3's Hiding with the scores already zeroed. A CEILING, not an assumption about when
# anything lands -- every assertion below is anchored on a server log line or on a card.
$BotDurationSec = 95

Write-Host "=== ROUND-1: the hide-seek round, driven once end to end ===" -ForegroundColor White

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
$script:Failures = @()
function Add-Failure([string]$m) { $script:Failures += $m }

try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    # --- the server -------------------------------------------------------------------------
    Write-Host "[1/4] dedicated server on udp/$Port, world supermarket, --round-script..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "roundsmoke-server.out.log"
    $server = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket", "--round-script", $Script) `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "roundsmoke-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server

    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "the server never reported listening; see roundsmoke-server.out.log"
    }
    Write-Host "        server up (pid $($server.Id))"

    # --- two clients ------------------------------------------------------------------------
    Write-Host "[2/4] two bots for ${BotDurationSec}s..." -ForegroundColor Cyan
    $bots = @()
    foreach ($name in @("RoundA", "RoundB")) {
        $jsonLog = Join-Path $script:LogDir "roundsmoke-$name.jsonl"
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
            "--headless", "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $name,
            "--log", $jsonLog, "--duration", $BotDurationSec, "--world", "supermarket") `
            -RedirectStandardOutput (Join-Path $script:LogDir "roundsmoke-$name.out.log") `
            -RedirectStandardError (Join-Path $script:LogDir "roundsmoke-$name.err.log") `
            -PassThru -NoNewWindow
        $null = $p.Handle
        $procs += $p
        $bots += @{ Name = $name; Proc = $p; Json = $jsonLog }
        # A short stagger so join ORDER is unambiguous: the first to arrive is the first round's
        # hider, and two clients racing to connect would make which-one-hides a coin flip that the
        # assertions below would then have to be vague about.
        Start-Sleep -Milliseconds 1200
    }

    Write-Host "[3/4] waiting for the round to run..." -ForegroundColor Cyan
    foreach ($b in $bots) {
        if (-not (Wait-ForExit $b.Proc ($BotDurationSec + 60))) {
            Stop-Proc $b.Proc
            Add-Failure "$($b.Name) did not exit within its own duration + 60 s"
        }
    }
    Stop-Proc $server
    Start-Sleep -Milliseconds 500

    # ==========================================================================================
    # Parsing
    # ==========================================================================================
    Write-Host "[4/4] reading the server's decisions and both peers' views..." -ForegroundColor Cyan

    if (-not (Test-Path $serverOut)) { Write-Fail "no server log was written at all" }
    $serverLines = @(Get-Content $serverOut)

    # The driver's own move decisions: "[round] peer <id> -> <room>[<i>] (x, y, z)".
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

    foreach ($b in $bots) { $b.Samples = Read-Samples $b.Json }

    foreach ($b in $bots) {
        if (@($b.Samples).Count -lt 10) {
            Add-Failure "$($b.Name) logged only $(@($b.Samples).Count) sample(s) -- it never really ran"
        }
        if ($b.Proc.ExitCode -ne 0) {
            # A bot that printed its own completion line and then died on the way out is a
            # teardown artifact, not a failing check (.claude/rules/test-suite.md, BASE-1's
            # -1073741795 entry). Read the log before calling it.
            $done = Select-String -Path (Join-Path $script:LogDir "roundsmoke-$($b.Name).out.log") `
                -Pattern "\[bot\] $($b.Name) done" -Quiet
            if ($done) {
                Write-Host "        NOTE: $($b.Name) exited $($b.Proc.ExitCode) AFTER printing its own done line -- teardown artifact, not a check" -ForegroundColor Yellow
            } else {
                Add-Failure "$($b.Name) exited $($b.Proc.ExitCode) without finishing its run"
            }
        }
    }

    # ==========================================================================================
    # 1. The round ran, in order, and a refusal was named rather than silent
    # ==========================================================================================
    $seen = @($phases | ForEach-Object { "$($_.From)->$($_.To)" })
    Write-Host "        server phases: $([string]::Join('  ', $seen))" -ForegroundColor DarkGray

    foreach ($want in @("Holding->Hiding", "Hiding->Seeking", "Seeking->Together", "Together->Tally", "Tally->Holding")) {
        if ($seen -notcontains $want) {
            Add-Failure "the server never made the transition $want -- the round did not run end to end"
        }
    }

    if ($refusals -notcontains "HiderMustHoldAnObject") {
        Add-Failure ("the scripted Start with empty hands was not refused with a NAMED reason " +
                     "(server logged: $([string]::Join(',', $refusals))) -- a silent no-op is the defect")
    }
    # And the refusal must not have started the round: the FIRST Holding->Hiding must come after
    # the refusal, which the server's own line order settles.
    $refusalIndex = -1; $startIndex = -1; $i = 0
    foreach ($line in $serverLines) {
        if ($refusalIndex -lt 0 -and $line -match '\[round\] refused: HiderMustHoldAnObject') { $refusalIndex = $i }
        if ($startIndex -lt 0 -and $line -match '\[round\] phase Holding -> Hiding') { $startIndex = $i }
        $i++
    }
    if ($refusalIndex -ge 0 -and $startIndex -ge 0 -and $refusalIndex -gt $startIndex) {
        Add-Failure "the round began BEFORE the refused Start -- the refusal did not refuse anything"
    }

    # ==========================================================================================
    # 2. Every room the server named is a different place
    # ==========================================================================================
    $rooms = @{}
    foreach ($m in $moves) { if (-not $rooms.ContainsKey($m.Room)) { $rooms[$m.Room] = $m } }
    Write-Host "        rooms used: $([string]::Join(', ', @($rooms.Keys)))" -ForegroundColor DarkGray
    foreach ($want in @("holding", "search", "task", "vestibule")) {
        if (-not $rooms.ContainsKey($want)) { Add-Failure "no peer was ever moved to the '$want' room" }
    }
    $roomNames = @($rooms.Keys)
    $properRooms = @($roomNames | Where-Object { $_ -ne "vestibule" })
    for ($a = 0; $a -lt $properRooms.Count; $a++) {
        for ($c = $a + 1; $c -lt $properRooms.Count; $c++) {
            $ra = $rooms[$properRooms[$a]]; $rb = $rooms[$properRooms[$c]]
            $d = Dist3 $ra.X $ra.Y $ra.Z $rb.X $rb.Y $rb.Z
            Write-Host "        $($properRooms[$a]) <-> $($properRooms[$c]): $([math]::Round($d,2)) m" -ForegroundColor DarkGray
            if ($d -lt $RoomSeparationM) {
                Add-Failure ("'$($properRooms[$a])' and '$($properRooms[$c])' are only $([math]::Round($d,2)) m apart " +
                             "(need $RoomSeparationM m) -- 'the bot reached its room' would prove nothing")
            }
        }
    }
    # The vestibule's own property: close to the task room by design, far enough from it that
    # arriving there is not the same measurement as standing in the task room.
    if ($rooms.ContainsKey("vestibule") -and $rooms.ContainsKey("task")) {
        $rv = $rooms["vestibule"]; $rt = $rooms["task"]
        $d = Dist3 $rv.X $rv.Y $rv.Z $rt.X $rt.Y $rt.Z
        Write-Host "        task <-> vestibule: $([math]::Round($d,2)) m (alcove, by design)" -ForegroundColor DarkGray
        if ($d -lt $VestibuleClearanceM) {
            Add-Failure ("the vestibule is only $([math]::Round($d,2)) m from the task room's marker " +
                         "(need $VestibuleClearanceM m, twice the arrival radius) -- 'the seeker reached " +
                         "the vestibule' could not be told from 'the seeker never left the task room'")
        }
    }

    # ==========================================================================================
    # 3. Every peer actually ARRIVED where the server sent it
    #
    # Judged on the bot's own converged view of itself, in the LAST sample before the next move
    # for that same peer -- so each move is checked over the window it owns, and a body that was
    # teleported away again before it ever arrived cannot hide behind a later sample.
    # ==========================================================================================
    $checked = 0
    $worst = 0.0
    foreach ($m in $moves) {
        # Which bot is this peer? Its own samples carry self = its peer id.
        $owner = $null
        foreach ($b in $bots) {
            $first = @($b.Samples) | Select-Object -First 1
            if ($null -ne $first -and [long]$first.self -eq $m.Peer) { $owner = $b; break }
        }
        if ($null -eq $owner) { continue }   # the server moved a peer whose log we do not have

        # The CLOSEST APPROACH to this destination anywhere in the owner's own log, and how far
        # the nearest OTHER named destination was at that same sample. A teleport lands in one
        # frame and the bot then walks its deterministic pattern away from wherever it was put, so
        # the closest approach is the honest measurement of "did it arrive"; requiring this
        # destination to be the nearest one at that instant is what makes it "did it arrive HERE"
        # rather than "is it somewhere in the building".
        $best = [double]::MaxValue
        $bestRival = [double]::MaxValue
        $sampleCount = 0
        foreach ($s in @($owner.Samples)) {
            $row = Get-PeerRow $s $m.Peer
            if ($null -eq $row) { continue }
            $sampleCount++
            $d = Dist3 ([double]$row.x) ([double]$row.y) ([double]$row.z) $m.X $m.Y $m.Z
            if ($d -lt $best) {
                $best = $d
                $bestRival = [double]::MaxValue
                foreach ($rn in @($rooms.Keys)) {
                    $r = $rooms[$rn]
                    if ((Dist3 $r.X $r.Y $r.Z $m.X $m.Y $m.Z) -lt 0.01) { continue }  # the same place
                    $rd = Dist3 ([double]$row.x) ([double]$row.y) ([double]$row.z) $r.X $r.Y $r.Z
                    if ($rd -lt $bestRival) { $bestRival = $rd }
                }
            }
        }
        if ($sampleCount -eq 0) { continue }
        $checked++
        if ($best -gt $worst) { $worst = $best }
        if ($best -gt $ArrivalRadiusM) {
            Add-Failure ("$($owner.Name) (peer $($m.Peer)) never came within $ArrivalRadiusM m of the " +
                         "'$($m.Room)' destination the server sent it to -- closest approach " +
                         "$([math]::Round($best,2)) m")
        } elseif ($bestRival -le $best) {
            Add-Failure ("$($owner.Name) (peer $($m.Peer)) was no nearer the '$($m.Room)' destination " +
                         "($([math]::Round($best,2)) m) than to another named room " +
                         "($([math]::Round($bestRival,2)) m) -- which room it is in cannot be told apart")
        }
    }
    if ($checked -eq 0) {
        Add-Failure "not one server-decided move could be checked against a bot's own log -- the suite proved nothing"
    }

    # Every bot must have been in at least two DISTINCT rooms over the run, or "the teleport
    # worked" would be satisfied by a body that never moved out of the room it spawned in.
    foreach ($b in $bots) {
        $selfId = $null
        $firstSample = @($b.Samples) | Select-Object -First 1
        if ($null -ne $firstSample) { $selfId = [long]$firstSample.self }
        if ($null -eq $selfId) { continue }
        $roomsVisited = @{}
        foreach ($s in @($b.Samples)) {
            $row = Get-PeerRow $s $selfId
            if ($null -eq $row) { continue }
            foreach ($rn in $roomNames) {
                $r = $rooms[$rn]
                if ((Dist3 ([double]$row.x) ([double]$row.y) ([double]$row.z) $r.X $r.Y $r.Z) -le $ArrivalRadiusM) {
                    $roomsVisited[$rn] = $true
                }
            }
        }
        $visited = @($roomsVisited.Keys)
        Write-Host "        $($b.Name) (peer $selfId) was in: $([string]::Join(', ', $visited))" -ForegroundColor DarkGray
        if ($visited.Count -lt 2) {
            Add-Failure "$($b.Name) was only ever in $($visited.Count) room(s) -- it was never actually moved"
        }
    }

    # ==========================================================================================
    # 4. Both peers held the SAME round state at Tally, for EVERY round the match ran
    #
    # Keyed by the round each card is ABOUT (HideSeekTally carries its own RoundIndex, because the
    # live index has already advanced by the time the card exists). A later sample of the same
    # round replaces an earlier one, so the map holds the settled version of each card.
    # ==========================================================================================
    #
    # TWO maps, and the difference between them is a real distinction rather than bookkeeping.
    # A card OUTLIVES the numbers it describes: it is still the peer's LastTally long after the
    # Tally phase ended, and the Start that begins the next match zeroes the live score map while
    # the card still reads 173-171. So:
    #   Cards         -- the last sample carrying each card. Use it for the card's OWN frozen
    #                    fields, and to prove they survived the reset.
    #   CardsAtTally  -- the last sample carrying each card WHILE ITS TALLY WAS ON SCREEN
    #                    (phase ordinal 4). The only instant at which the live score map and the
    #                    card are describing the same moment, so every live-vs-card comparison
    #                    belongs here.
    # The first version of this suite used one map for both and failed with "the totals on the
    # wire are level at 0 but the card names peer N the winner" -- the suite reading the live map
    # one round too late, which is precisely the bug the frozen totals exist to prevent, caught
    # against the suite instead of against the game.
    function Get-CardsByRound($Samples, [int]$PhaseFilter = -1) {
        $cards = @{}
        foreach ($s in @($Samples)) {
            if (-not [bool]$s.roundSynced) { continue }
            if ($null -eq $s.roundTally) { continue }
            if ($PhaseFilter -ge 0 -and [int]$s.roundPhase -ne $PhaseFilter) { continue }
            $cards[[int]$s.roundTally.roundIndex] = $s
        }
        return ,$cards
    }
    foreach ($b in $bots) {
        $b.Cards = Get-CardsByRound $b.Samples
        $b.CardsAtTally = Get-CardsByRound $b.Samples 4
    }

    # Phase ordinal 4 = Tally (HideSeekPhase): every peer must have SEEN the phase, not merely
    # folded a card at some point.
    foreach ($b in $bots) {
        $sawTally = $false
        foreach ($s in @($b.Samples)) {
            if ([bool]$s.roundSynced -and [int]$s.roundPhase -eq 4) { $sawTally = $true }
        }
        if (-not $sawTally) { Add-Failure "$($b.Name) never logged a synced sample in the Tally phase" }
    }

    # Every field of every card, compared between the two independent logs. This is the
    # absolute-wire property applied to MATCH-1's five new fields as well as ROUND-1's five.
    $cardFields = @("roundIndex", "hiderPeerId", "hiderGained", "seekerPeerId", "seekerGained",
                    "endedByDisconnect", "matchOver", "matchIndex", "winnerPeerId",
                    "hiderTotal", "seekerTotal")
    foreach ($round in @(1, 2)) {
        $rows = @()
        foreach ($b in $bots) {
            if (-not $b.CardsAtTally.ContainsKey($round)) {
                Add-Failure "$($b.Name) never folded round $round's card during its own Tally -- that round never finished on this peer"
            } else {
                $rows += ,@{ Name = $b.Name; S = $b.CardsAtTally[$round] }
            }
        }
        if ($rows.Count -ne 2) { continue }
        $a = $rows[0].S; $c = $rows[1].S
        foreach ($field in $cardFields) {
            if ([string]$a.roundTally.$field -ne [string]$c.roundTally.$field) {
                Add-Failure ("round $round's card disagrees on $field between the two peers " +
                             "($($rows[0].Name)=$($a.roundTally.$field), $($rows[1].Name)=$($c.roundTally.$field))")
            }
        }
        Write-Host ("        card round $($a.roundTally.roundIndex) (match $($a.roundTally.matchIndex)): " +
                    "hider $($a.roundTally.hiderPeerId) +$($a.roundTally.hiderGained), " +
                    "seeker $($a.roundTally.seekerPeerId) +$($a.roundTally.seekerGained), " +
                    "totals $($a.roundTally.hiderTotal)-$($a.roundTally.seekerTotal), " +
                    "matchOver=$($a.roundTally.matchOver) winner=$($a.roundTally.winnerPeerId)") -ForegroundColor DarkGray

        # The roles, and the score map key by key, at the sample that carries this card.
        $aKeys = @($a.roundScores.PSObject.Properties.Name | Sort-Object)
        $cKeys = @($c.roundScores.PSObject.Properties.Name | Sort-Object)
        if ([string]::Join(',', $aKeys) -ne [string]::Join(',', $cKeys)) {
            Add-Failure "at round $round's card the two peers' score maps name different peers ($([string]::Join(',',$aKeys)) vs $([string]::Join(',',$cKeys)))"
        } else {
            foreach ($k in $aKeys) {
                if ([int]$a.roundScores.$k -ne [int]$c.roundScores.$k) {
                    Add-Failure "at round $round's card the two peers disagree on peer $k's score ($($a.roundScores.$k) vs $($c.roundScores.$k))"
                }
            }
            Write-Host "        scores at round $round's card: $([string]::Join(', ', @($aKeys | ForEach-Object { "$_=$($a.roundScores.$_)" })))" -ForegroundColor DarkGray
        }
    }

    # The hider's score IS the frozen tower count the script set for THAT round, which is what
    # makes this a check on the round rather than on two peers agreeing about nothing. Two
    # different counts, so the two cards cannot be confused for each other.
    foreach ($want in @(@{ Round = 1; Towers = 3 }, @{ Round = 2; Towers = 1 })) {
        foreach ($b in $bots) {
            if (-not $b.Cards.ContainsKey($want.Round)) { continue }
            $got = [int]$b.Cards[$want.Round].roundTally.hiderGained
            if ($got -ne $want.Towers) {
                Add-Failure ("$($b.Name): round $($want.Round)'s card credits the hider $got tower(s); the " +
                             "script set $($want.Towers) before the find, so the freeze at the Found tick did not happen")
            }
        }
    }

    # And the roles really did swap between the two rounds -- without that, "each of them hides
    # once" is not what the match measured.
    foreach ($b in $bots) {
        if (-not ($b.Cards.ContainsKey(1) -and $b.Cards.ContainsKey(2))) { continue }
        $h1 = [string]$b.Cards[1].roundTally.hiderPeerId
        $h2 = [string]$b.Cards[2].roundTally.hiderPeerId
        $s1 = [string]$b.Cards[1].roundTally.seekerPeerId
        if ($h1 -eq $h2) {
            Add-Failure ("$($b.Name): peer $h1 hid in BOTH rounds of the match -- the roles did not swap, " +
                         "so neither player got a turn at the other side")
        } elseif ($h2 -ne $s1) {
            Add-Failure "$($b.Name): round 2's hider ($h2) is neither round 1's hider nor its seeker ($s1)"
        }
    }

    # ==========================================================================================
    # 5. A MATCH IS TWO ROUNDS AND THEN SOMEBODY HAS WON (MATCH-1)
    #
    # All of it off the BOTS' own folded cards, never off the server's state: what this suite can
    # prove that a unit test cannot is that the result crossed the wire intact and that both
    # peers hold the same one.
    # ==========================================================================================
    $matchWinner = "?"
    $matchTotals = "?"
    foreach ($b in $bots) {
        # FALSE then TRUE. The false half is what an implementation that ends a match every round
        # fails, and it is the half a one-round script could never have asked about.
        if ($b.Cards.ContainsKey(1) -and [bool]$b.Cards[1].roundTally.matchOver) {
            Add-Failure ("$($b.Name) folded round 1's card with matchOver=true -- round 1 is the first " +
                         "half of a two-round match, not the end of one")
        }
        if ($b.Cards.ContainsKey(2) -and -not [bool]$b.Cards[2].roundTally.matchOver) {
            Add-Failure ("$($b.Name) folded round 2's card with matchOver=false -- two rounds is a whole " +
                         "match and the game never said who won")
        }
        if (-not $b.CardsAtTally.ContainsKey(2)) { continue }

        # The winner id equals whichever peer actually holds the higher total ON THE WIRE, read
        # at the one instant the live map and the card describe the same moment: while the match
        # card is on screen. The card decides the winner server-side; this asserts the two agree
        # after a round trip, which is the failure a client-side recomputation would produce.
        $s = $b.CardsAtTally[2]
        $keys = @($s.roundScores.PSObject.Properties.Name)
        $best = ""; $bestVal = -1; $tie = $false
        foreach ($k in $keys) {
            $v = [int]$s.roundScores.$k
            if ($v -gt $bestVal) { $bestVal = $v; $best = $k; $tie = $false }
            elseif ($v -eq $bestVal) { $tie = $true }
        }
        $winner = [string]$s.roundTally.winnerPeerId
        if ($keys.Count -lt 2) {
            Add-Failure "$($b.Name): the match card arrived with $($keys.Count) score row(s); a match is between two players"
        } elseif ($tie) {
            if ($winner -ne "0") {
                Add-Failure "$($b.Name): the totals on the wire are level at $bestVal but the card names peer $winner the winner"
            }
        } elseif ($winner -ne $best) {
            Add-Failure ("$($b.Name): the match card names peer $winner the winner, but peer $best holds the " +
                         "higher total ($bestVal) on the wire")
        }
        # This schedule is built to produce a real winner (173-171). A draw here means the two
        # rounds scored identically, which would make the assertion above vacuous.
        if ($winner -eq "0") {
            Add-Failure ("$($b.Name): the match ended in a draw; this schedule scores the two rounds " +
                         "differently on purpose, so a draw means one of them did not score what it should")
        }
        # A positive control for the zero check below: the totals at the match card must NOT be
        # zero, or "every score is zero after the third Start" is satisfied by a session in which
        # nobody ever scored at all.
        if ($bestVal -le 0) {
            Add-Failure "$($b.Name): every total at the match card is zero -- nothing was ever scored, so the reset proves nothing"
        }
        $matchWinner = $winner
        $matchTotals = [string]::Join(', ', @($keys | Sort-Object | ForEach-Object { "$_=$($s.roundScores.$_)" }))
    }

    # THE SCORES GO BACK TO ZERO ON THE START THAT BEGINS MATCH 2, and not before. Sampled in
    # round 3's Hiding -- phase ordinal 1, live round index 3 -- rather than at a typed second.
    foreach ($b in $bots) {
        $afterStart = $null
        foreach ($s in @($b.Samples)) {
            if ([bool]$s.roundSynced -and [int]$s.roundPhase -eq 1 -and [int]$s.roundIndex -eq 3) { $afterStart = $s }
        }
        if ($null -eq $afterStart) {
            Add-Failure ("$($b.Name) never logged round 3's Hiding -- the third Start never ran on this peer, " +
                         "so the score reset that begins a new match was never observed")
            continue
        }
        $rows = @($afterStart.roundScores.PSObject.Properties.Name)
        if ($rows.Count -lt 2) {
            Add-Failure ("$($b.Name): after the Start that begins match 2 the score map has $($rows.Count) row(s); " +
                         "the reset is ZERO ROWS, not an empty map -- a board needs a row to show a zero")
        }
        $nonZero = @()
        foreach ($k in $rows) { if ([int]$afterStart.roundScores.$k -ne 0) { $nonZero += "$k=$($afterStart.roundScores.$k)" } }
        if ($nonZero.Count -gt 0) {
            Add-Failure ("$($b.Name): after the Start that begins match 2 the scores still read " +
                         "$([string]::Join(', ', $nonZero)) -- the new match inherited the old one's score")
        }

        # AND THE CARD STILL READS THE OLD RESULT. This is the other half, and it is the whole
        # argument for freezing the totals onto the card instead of reading them off the live
        # map: at this exact sample the map says 0-0 and the card must still say who won match 1.
        # HOLD-1's board shows the last card during the next round, so a board built on the live
        # map would print "X WON 0-0" from here on.
        if ($null -eq $afterStart.roundTally) {
            Add-Failure "$($b.Name): the card was cleared by the Start that began match 2 -- the last result is gone from the board"
        } elseif ([int]$afterStart.roundTally.roundIndex -ne 2) {
            Add-Failure ("$($b.Name): after the third Start the card is round $($afterStart.roundTally.roundIndex)'s, " +
                         "not round 2's -- a new match should not produce a card")
        } elseif (-not [bool]$afterStart.roundTally.matchOver) {
            # Already reported above as "round 2's card with matchOver=false". Saying it a second
            # time here as "the result is not frozen" would be a wrong diagnosis of the same
            # fault -- there is no result to freeze on a card that is not a match end.
            Write-Host "        $($b.Name) after the third Start: $($rows.Count) score row(s); card is not a match end (reported above)" -ForegroundColor DarkGray
        } else {
            $ht = [int]$afterStart.roundTally.hiderTotal
            $st = [int]$afterStart.roundTally.seekerTotal
            if ($ht -eq 0 -and $st -eq 0) {
                Add-Failure ("$($b.Name): the match card's own totals went to 0-0 when the scores reset -- the " +
                             "result is not frozen, so the board loses it the moment the next match begins")
            }
            if ([string]$afterStart.roundTally.winnerPeerId -eq "0") {
                Add-Failure "$($b.Name): the match card's winner went to 0 when the scores reset -- the result is not frozen"
            }
            Write-Host ("        $($b.Name) after the third Start: $($rows.Count) score row(s), all zero; " +
                        "match card still reads $ht-$st, winner $($afterStart.roundTally.winnerPeerId)") -ForegroundColor DarkGray
        }
    }

    # The match card holds LONGER than the round card, read off the server's own two card lines.
    $cardLines = @()
    foreach ($line in $serverLines) {
        if ($line -match '\[round\] card: round (\d+) match (\d+) .*matchOver=(\w+) winner=(-?\d+) tally=([0-9.]+)s') {
            $cardLines += ,@{ Round = [int]$matches[1]; Match = [int]$matches[2]
                              Over = ($matches[3] -eq "True"); Winner = [long]$matches[4]
                              Tally = [double]$matches[5] }
        }
    }
    $roundCard = @($cardLines | Where-Object { -not $_.Over } | Select-Object -First 1)
    $matchCard = @($cardLines | Where-Object { $_.Over } | Select-Object -First 1)
    if ($roundCard.Count -eq 0 -or $matchCard.Count -eq 0) {
        Add-Failure ("the server logged $($cardLines.Count) card line(s) but not one of each kind " +
                     "(a round card and a match card); the match never completed on the server")
    } else {
        $rt = $roundCard[0].Tally; $mt = $matchCard[0].Tally
        Write-Host "        tally armed: round card $($rt)s, match card $($mt)s" -ForegroundColor DarkGray
        if ($mt -le $rt) {
            Add-Failure ("the match card was armed for $($mt)s against the round card's $($rt)s -- " +
                         "MatchTallySec was not applied, so the only moment the game says who won is as " +
                         "short as an ordinary round's")
        }
    }

    # And the server said, in its own words, that a new match began at round 3.
    $matchBegins = @()
    foreach ($line in $serverLines) {
        if ($line -match '\[round\] match (\d+) begins at round (\d+) -- scores zeroed: (.+)$') {
            $matchBegins += ,@{ Match = [int]$matches[1]; Round = [int]$matches[2]; Scores = $matches[3] }
        }
    }
    Write-Host "        match starts the server logged: $(@($matchBegins | ForEach-Object { "match $($_.Match) at round $($_.Round) [$($_.Scores)]" }) -join '; ')" -ForegroundColor DarkGray
    if (@($matchBegins | Where-Object { $_.Match -eq 2 -and $_.Round -eq 3 }).Count -eq 0) {
        Add-Failure "the server never logged match 2 beginning at round 3 -- the third Start did not start a new match"
    }
    if (@($matchBegins | Where-Object { $_.Round -eq 2 }).Count -gt 0) {
        Add-Failure "the server logged a new match beginning at round 2 -- round 2 is the second half of match 1, not a new one"
    }

    # And the reset edge really did put everyone back in the holding room.
    $backHome = @($moves | Where-Object { $_.Room -eq "holding" })
    if ($backHome.Count -lt 2) {
        Add-Failure "the reset edge moved $($backHome.Count) peer(s) to the holding room; both were owed a trip home"
    }

    Write-Host ""
    Write-Host "MEASURED QUANTITIES (compare these across runs, not the verdict):" -ForegroundColor White
    Write-Host "  server-decided moves checked against a bot's own log: $checked"
    Write-Host "  worst closest-approach to a named destination:        $([math]::Round($worst,2)) m (bar $ArrivalRadiusM m)"
    Write-Host "  phase transitions the server made:                    $($phases.Count)"
    Write-Host "  named refusals the server logged:                     $($refusals.Count) [$([string]::Join(',', $refusals))]"
    Write-Host "  cards the server committed:                           $($cardLines.Count) [$(@($cardLines | ForEach-Object { "r$($_.Round) matchOver=$($_.Over)" }) -join ', ')]"
    Write-Host "  match 1's winner / totals on the wire:                peer $matchWinner / $matchTotals"
}
finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host ""
if ($script:Failures.Count -gt 0) {
    Write-Host "ROUNDLOOP-SMOKE FAILED ($($script:Failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "ROUNDLOOP-SMOKE OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: the round ran Holding -> Hiding -> Seeking -> Together -> Tally -> Holding on a real server; a Start with empty hands was refused with a named reason and did not begin the round; every peer arrived in the room the server sent it to, and the rooms are genuinely separate places; both peers held the same phase, roles, scores and card at Tally. A MATCH ran too: two rounds with the roles swapped between them, matchOver false on the first card and true on the second, the winner on the wire equal to the higher total, a longer card at the match end, and every score row back at zero on the Start that began match 2." -ForegroundColor Green
Write-Host ""
Write-Host "ROUNDLOOP-SMOKE OVERALL: PASS" -ForegroundColor Green
exit 0
