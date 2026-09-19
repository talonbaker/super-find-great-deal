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
#   4  a Start with empty hands -> refused, named, round does not begin
#   8  Start properly            -> Hiding, hider to the search room
#   14 Confirm                   -> Seeking, hider to the task room, seeker to the search room
#   22 the object is in the bin  -> Together, seeker to the vestibule
#   28 End                       -> Tally
#   +6 s of Tally                -> the reset edge, everyone back to the holding room
$Script = "noobject@3,start@4,object@6,start@8,confirm@14,towers:3@18,found@22,end@28"

# Bot lifetime: the schedule's last beat (28) plus the tally (6) plus a settle. A CEILING, not an
# assumption about when anything lands -- every assertion below is anchored on a server log line.
$BotDurationSec = 46

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
    # 4. Both peers held the SAME round state at Tally
    # ==========================================================================================
    $tallyViews = @()
    foreach ($b in $bots) {
        # Phase ordinal 4 = Tally (HideSeekPhase). The LAST Tally sample this peer logged: by then
        # every message of that phase has landed on both.
        $t = $null
        foreach ($s in @($b.Samples)) {
            if ([bool]$s.roundSynced -and [int]$s.roundPhase -eq 4) { $t = $s }
        }
        if ($null -eq $t) {
            Add-Failure "$($b.Name) never logged a synced sample in the Tally phase"
        } else {
            $tallyViews += ,@{ Name = $b.Name; S = $t }
        }
    }

    if ($tallyViews.Count -eq 2) {
        $a = $tallyViews[0].S; $c = $tallyViews[1].S
        foreach ($field in @("roundIndex", "roundHider", "roundSeeker")) {
            if ([string]$a.$field -ne [string]$c.$field) {
                Add-Failure ("at Tally the two peers disagree on $field " +
                             "($($tallyViews[0].Name)=$($a.$field), $($tallyViews[1].Name)=$($c.$field))")
            }
        }
        if ($null -eq $a.roundTally -or $null -eq $c.roundTally) {
            Add-Failure "one or both peers reached Tally with no card folded at all"
        } else {
            foreach ($field in @("roundIndex", "hiderPeerId", "hiderGained", "seekerPeerId", "seekerGained")) {
                if ([string]$a.roundTally.$field -ne [string]$c.roundTally.$field) {
                    Add-Failure ("at Tally the two peers' cards disagree on $field " +
                                 "($($a.roundTally.$field) vs $($c.roundTally.$field))")
                }
            }
            Write-Host ("        card: round $($a.roundTally.roundIndex), hider $($a.roundTally.hiderPeerId) " +
                        "+$($a.roundTally.hiderGained), seeker $($a.roundTally.seekerPeerId) " +
                        "+$($a.roundTally.seekerGained)") -ForegroundColor DarkGray
        }
        # The score maps, compared key by key. This is the absolute-wire property: one message,
        # every peer that applied it holding the same view.
        $aKeys = @($a.roundScores.PSObject.Properties.Name | Sort-Object)
        $cKeys = @($c.roundScores.PSObject.Properties.Name | Sort-Object)
        if ([string]::Join(',', $aKeys) -ne [string]::Join(',', $cKeys)) {
            Add-Failure "at Tally the two peers' score maps name different peers ($([string]::Join(',',$aKeys)) vs $([string]::Join(',',$cKeys)))"
        } else {
            foreach ($k in $aKeys) {
                if ([int]$a.roundScores.$k -ne [int]$c.roundScores.$k) {
                    Add-Failure "at Tally the two peers disagree on peer $k's score ($($a.roundScores.$k) vs $($c.roundScores.$k))"
                }
            }
            Write-Host "        scores at Tally: $([string]::Join(', ', @($aKeys | ForEach-Object { "$_=$($a.roundScores.$_)" })))" -ForegroundColor DarkGray
        }
        # The hider's score IS the frozen tower count the script set, which is what makes this a
        # check on the round rather than on two peers agreeing about nothing.
        if ($null -ne $a.roundTally -and [int]$a.roundTally.hiderGained -ne 3) {
            Add-Failure ("the card credits the hider $($a.roundTally.hiderGained) tower(s); the script " +
                         "set 3 before the find, so the freeze at the Found tick did not happen")
        }
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

Write-Host "PASS: the round ran Holding -> Hiding -> Seeking -> Together -> Tally -> Holding on a real server; a Start with empty hands was refused with a named reason and did not begin the round; every peer arrived in the room the server sent it to, and the rooms are genuinely separate places; both peers held the same phase, roles, scores and card at Tally." -ForegroundColor Green
Write-Host ""
Write-Host "ROUNDLOOP-SMOKE OVERALL: PASS" -ForegroundColor Green
exit 0
