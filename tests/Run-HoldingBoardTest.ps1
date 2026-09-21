<#
.SYNOPSIS
    HOLD-1: the holding room's board, driven on a real server with two real clients, one of
    which joins after the round has already started. Proves the one thing no pure function can:
    that a peer which witnessed nothing paints the SAME BOARD as a peer that was there from the
    beginning, off ONE wire message.

.DESCRIPTION
    Everything about the board that is a RULE is tested engine-free in tests/unit
    (HoldingBoardTests: the row set, the ordering, the role cell in every phase, the header, the
    footer's card-vs-live-scores split, and the panel's width bounds). None of that is repeated
    here. What is left is what needs two processes and a wire.

      1. THE LATE JOINER'S FIRST BOARD IS ALREADY COMPLETE. Bots A and B play the round; bot C
         is launched on a SERVER LOG LINE -- the Holding -> Hiding transition -- so it arrives
         having seen nothing of it. Its FIRST [board] line is compared against the line A
         painted NEAREST IT IN TIME, and the rows, the header and the footer must agree. This
         is ROUND-1's absolute fold seen from the wall rather than from the wire: the message
         is a whole view, not a delta, so there is no state to catch up on.

         THREE BOTS AND NOT TWO, and the first cut of this suite got that wrong in a way worth
         recording. The late joiner was originally the SECOND peer, gated on Holding -> Hiding.
         That is a deadlock: HideSeekLoop refuses a Start with fewer than two humans, so the
         transition the bot was waiting for could never happen. The run failed with "the server
         never left Holding", which reads exactly like a broken round script and is nothing of
         the sort. A round needs its two players before a late joiner can be late.

      2. THE ROWS ARE READ OFF THE LABELS, NOT RECOMPUTED. HoldingBoard.CurrentText returns what
         was last pushed into each Label3D, so a board that never painted logs rows=0 and fails
         here rather than passing on a blank panel. CLOCK-1 made exactly this point about the
         clock's phase word and it is the reason either suite means anything.

      3. EVERY PAIR, NOT JUST THE FIRST. Once C is up, every one of its lines is paired with
         A's nearest and compared. A late joiner that was right once and then drifted would
         pass an assertion about its first line alone.

      4. THE THIRD PLAYER IS TOLD THEY ARE WAITING, AND CANNOT RUN THE ROUND (SOLO-1,
         2026-09-20). C was already a third body in this suite; what changed is that a third body
         is no longer a refusal. Its own row must read WAITING -- not the em dash HOLD-1 printed,
         which says nothing about whether it is in this game -- and its START press must come back
         refused with NotInThisMatch. Both are asserted on C's own evidence: the row off the
         label it painted, the refusal off the server's own [buttons] line.

         WHY IT PRESSES FROM A PARKED SPOT. RoundControls checks REACH before it checks the gate
         (BTN-1's ordering, deliberate), so a bot that wandered off would be refused TooFarAway
         and this assertion would be about the walk rather than about the rule. C is sent to the
         START button with --goto-script, exactly as Run-ButtonsTest's own wrong-phase presser is,
         and presses twice; a run in which every press came back TooFarAway says so in its own
         words rather than reading as a broken gate.

    THE PRONOUN IS THE ONE THING THAT MAY DIFFER, and the comparison normalises it rather than
    ignoring the column. Each client paints its own copy of this wall, so during Holding your own
    row says "NEXT: YOU HIDE" and the other player's copy of your row says "NEXT: HIDES". That is
    deliberate (see HoldingBoardModel.RoleCell). Everything else -- the names, the scores, the
    order, the header, the footer -- must be identical, and a test that dropped the role column
    to avoid the pronoun would stop noticing a role that was wrong.

    PAIRING IS BY TIMESTAMP, NOT BY ORDERING, which is CLOCK-1's measured rule: two independent
    1 Hz timers drift into and out of phase within a period, so "the last line before this one"
    is between 0 and 1000 ms stale at random. Every [board] line carries a Unix millisecond
    stamp and each of B's lines is matched to A's NEAREST IN TIME.

    WHY --log-clock AND NOT A FLAG OF ITS OWN. The board and the wall clock are painted by ONE
    poll from ONE view (RoundAudio), so a suite that wants to see what the walls say wants both.
    A second flag would be a second thing to remember and could not be off when the other was on.

    NOT ONE WALL-CLOCK GUESS IN THE ASSERTIONS. The late joiner's launch is anchored on a line
    the SERVER wrote; the only typed numbers are lifetimes, which are ceilings derived from the
    schedule they must outlive.

    Exit 0 = PASS. Headless throughout -- no GPU, no display. One Godot at a time: this suite
    takes the machine-wide suite mutex like every other.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param(
    # 7909. HANDED OUT BY THE ORCHESTRATOR, not computed from a snapshot of tests/ -- INT-0's
    # measured lesson (three lanes off one base all picked 7896) applied rather than re-learned,
    # and SHELF-1's note in .claude/rules/test-suite.md already recorded 7909 as the next free
    # number. The one ladder table lives in that file; this suite is on it.
    [int]$Port = 7909,
    [int]$MutexTimeoutMinutes = 120,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# ------------------------------------------------------------------------------------------
# The schedule, in seconds from the FIRST PLAYER ARRIVING (the driver's script-clock gate), so
# these are margins over connection time rather than guesses about it.
#
#   10  Start    -> Hiding. C is launched HERE, on the server's own transition line.
#   34  Confirm  -> Seeking.
#   46  found    -> Together.
#   50  End      -> Tally, and the card lands. The footer has something to say from here on.
#   56  (+6 s)   -> the reset edge, back to Holding with the card still up.
#
# Start at 10 rather than earlier because the script clock starts when the FIRST player arrives
# and a Godot client takes several seconds to boot -- a Start that races B's connect would be
# refused NeedTwoPlayers, which is CLOCK-1's measured trap with the same shape.
# ------------------------------------------------------------------------------------------
$Script = "start@10,confirm@34,towers:3@40,found@46,end@50"

# A CEILING over the schedule, not an assumption about when anything lands: the last beat is the
# reset at 56 s after the first arrival, plus boot and settle.
$BotDurationSec = 80

# The late joiner's own lifetime. Launched on the Holding -> Hiding line at t = 10, so it must
# outlive the ~46 s of round that is left, plus its own boot. A CEILING derived from the
# schedule above, not a number typed beside it (FIX-1's rule).
$LateBotDurationSec = 62

# How far apart two [board] lines may be and still be "the same instant". The logs are at 1 Hz
# from two processes on one machine with one wall clock, so the nearest A line to a B line is at
# most half a period away; 600 ms is that with room for a scheduling hiccup. A B line that
# cannot find an A line inside it is NOT compared -- and is counted, so a run that compared
# nothing cannot read as green.
$PairWindowMs = 600

# A B line this close to a server phase transition is skipped rather than compared. At 1 Hz
# logging over a 10 Hz wire the two clients can legitimately be one message apart across an
# edge; that is the wire's latency, not two different boards. CLOCK-1 measured the same thing
# and chose the same guard.
$PhaseEdgeGuardMs = 1200

# The floor on how much was actually compared. A guard that swallowed everything must not read
# as green -- CLOCK-1's rule, and the reason its own suite counts its skips.
$MinPairs = 12

Write-Host "=== HOLD-1: the holding room's board, and a peer that saw none of it ===" -ForegroundColor White

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
$script:Failures = @()
function Add-Failure([string]$m) { $script:Failures += $m }

# "[board] wall=<ms> header="..." rows=N [r0 | r1] overflow="..." footer="..."" -> a hashtable.
# Parsed with one regex rather than by splitting, because every field but rows= contains spaces.
function ConvertTo-BoardSample([string]$Line) {
    if ($Line -notmatch '^\[board\] wall=(\d+) header="([^"]*)" rows=(\d+) \[(.*)\] overflow="([^"]*)" footer="([^"]*)"$') {
        return $null
    }
    $rowsText = $matches[4]
    $rows = if ($rowsText -eq "(none)") { @() } else { @($rowsText -split ' \| ') }
    return @{
        T = [long]$matches[1]; Header = $matches[2]; Count = [int]$matches[3]
        Rows = $rows; Overflow = $matches[5]; Footer = $matches[6]; Raw = $Line
    }
}

# The pronoun is the ONE thing two peers' copies of this wall may disagree about, by design.
# Normalised rather than dropped: a comparison that deleted the role column would stop noticing
# a role that was actually wrong.
function Normalize-Row([string]$Row) {
    return ($Row -replace 'NEXT: YOU HIDE', 'NEXT: HIDES') -replace 'NEXT: YOU SEEK', 'NEXT: SEEKS'
}

function Compare-Boards($Label, $Mine, $Theirs) {
    $bad = @()
    if ($Mine.Header -ne $Theirs.Header) {
        $bad += "header '$($Mine.Header)' vs '$($Theirs.Header)'"
    }
    if ($Mine.Count -ne $Theirs.Count) {
        $bad += "row count $($Mine.Count) vs $($Theirs.Count)"
    } else {
        for ($i = 0; $i -lt $Mine.Count; $i++) {
            $a = Normalize-Row $Mine.Rows[$i]
            $b = Normalize-Row $Theirs.Rows[$i]
            if ($a -ne $b) { $bad += "row $i '$a' vs '$b'" }
        }
    }
    if ($Mine.Footer -ne $Theirs.Footer) {
        $bad += "footer '$($Mine.Footer)' vs '$($Theirs.Footer)'"
    }
    if ($Mine.Overflow -ne $Theirs.Overflow) {
        $bad += "overflow '$($Mine.Overflow)' vs '$($Theirs.Overflow)'"
    }
    if ($bad.Count -gt 0) {
        Add-Failure ("$Label - the two peers painted different boards at the same instant: " +
                     ($bad -join "; ") + ". The board is a pure function of one folded view, so " +
                     "this is either a fold that differs between peers or a renderer that is " +
                     "not reading the view it was handed.")
    }
    return $bad.Count -eq 0
}

try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    # --- the server ---------------------------------------------------------------------------
    Write-Host "[1/4] dedicated server on udp/$Port, world supermarket, --round-script, --log-clock..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "holdboard-server.out.log"
    $server = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket",
        "--round-script", $Script, "--log-clock") `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "holdboard-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server

    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "the server never reported listening on udp/$Port; see holdboard-server.out.log"
    }
    Write-Host "        server up (pid $($server.Id))"

    function Start-BoardBot([string]$Name, [int]$DurationSec, [string[]]$Extra = @()) {
        $out = Join-Path $script:LogDir "holdboard-$Name.out.log"
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList (@(
            "--headless", "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
            "--duration", $DurationSec, "--world", "supermarket", "--log-clock") + $Extra) `
            -RedirectStandardOutput $out `
            -RedirectStandardError (Join-Path $script:LogDir "holdboard-$Name.err.log") `
            -PassThru -NoNewWindow
        $null = $p.Handle
        return @{ Name = $Name; Proc = $p; Out = $out }
    }

    # --- A and B, the two who play the round ----------------------------------------------------
    Write-Host "[2/4] bots A and B (present throughout) for ${BotDurationSec}s..." -ForegroundColor Cyan
    $botA = Start-BoardBot "BoardA" $BotDurationSec
    $procs += $botA.Proc
    # A short stagger so join ORDER is unambiguous: the first to arrive is this round's hider,
    # and two clients racing to connect makes which-one-hides a coin flip (CLOCK-1's note).
    Start-Sleep -Milliseconds 1200
    $botB = Start-BoardBot "BoardB" $BotDurationSec
    $procs += $botB.Proc

    # --- C, launched on the SERVER'S OWN LINE -----------------------------------------------------
    # Not at a typed second. The round leaves Holding at script t = 10 and a Godot client boots in
    # a few seconds, so C arrives inside Hiding having witnessed nothing. If it is slow and lands
    # in Seeking instead, the assertion is unchanged and still meaningful: whatever phase it
    # arrives in, its first board must already be right.
    Write-Host "[3/4] waiting for the round to leave Holding, then joining C late..." -ForegroundColor Cyan
    if (-not (Wait-ForLogLine $serverOut "\[round\] phase Holding -> Hiding" 120)) {
        Add-Failure ("the server never left Holding, so the late joiner was never launched. " +
                     "Read holdboard-server.out.log for the refusal - a Start refused " +
                     "NeedTwoPlayers means A and B had not both connected when the script fired.")
        $botC = $null
    } else {
        # --goto-script parks C at the START button (the same spot Run-ButtonsTest's wrong-phase
        # presser uses) and --press fires the SHIPPED verb twice on C's own elapsed clock. Both
        # numbers are ceilings over C's boot and walk rather than marks it has to hit: the
        # assertion is that AT LEAST ONE press came back NotInThisMatch, so a slow walk costs a
        # press and not the run. Neither flag can touch the board -- a refused press changes no
        # round state at all, which is the point of it.
        $botC = Start-BoardBot "BoardC" $LateBotDurationSec @(
            "--goto-script", "1.2,-4.5",
            "--press", "start@14,start@26")
        $procs += $botC.Proc
        Write-Host "        late joiner launched (pid $($botC.Proc.Id))"
    }

    foreach ($b in @($botA, $botB, $botC)) {
        if ($null -eq $b) { continue }
        if (-not (Wait-ForExit $b.Proc ($BotDurationSec + 60))) {
            Stop-Proc $b.Proc
            Add-Failure "$($b.Name) did not exit within its own duration + 60 s"
        }
    }
    Stop-Proc $server
    Start-Sleep -Milliseconds 500

    # ==========================================================================================
    # Parsing and assertions
    # ==========================================================================================
    Write-Host "[4/4] reading both peers' walls..." -ForegroundColor Cyan

    $samples = @{}
    foreach ($b in @($botA, $botB, $botC)) {
        if ($null -eq $b) { continue }
        $parsed = @()
        if (Test-Path $b.Out) {
            foreach ($line in (Get-Content $b.Out)) {
                $s = ConvertTo-BoardSample $line
                if ($null -ne $s) { $parsed += ,$s }
            }
        }
        $samples[$b.Name] = $parsed
        Write-Host ("        $($b.Name): $($parsed.Count) board sample(s)")
    }

    $a = @($samples["BoardA"])
    $bs = if ($samples.ContainsKey("BoardC")) { @($samples["BoardC"]) } else { @() }

    if ($a.Count -eq 0) {
        Add-Failure ("bot A logged no [board] lines at all. Either --log-clock did not reach the " +
                     "client, or the board never painted - HoldingBoard is hidden until the " +
                     "round is synced, so a peer that never synced has a blank wall by design.")
    }
    if ($bs.Count -eq 0) {
        Add-Failure "the late joiner logged no [board] lines at all"
    }

    # The server's phase transitions, for the edge guard.
    $phaseChangeTimes = @()
    $lastPhase = $null
    foreach ($line in (Get-Content $serverOut)) {
        if ($line -match '\[clock\] (\d+) server (\S+) ') {
            if ($null -ne $lastPhase -and $matches[2] -ne $lastPhase) {
                $phaseChangeTimes += [long]$matches[1]
            }
            $lastPhase = $matches[2]
        }
    }

    function Get-Nearest($Set, [long]$T) {
        $best = $null; $bestGap = [long]::MaxValue
        foreach ($s in $Set) {
            $gap = [math]::Abs($s.T - $T)
            if ($gap -lt $bestGap) { $bestGap = $gap; $best = $s }
        }
        if ($bestGap -gt $PairWindowMs) { return $null }
        return $best
    }

    # --- 1. the late joiner's FIRST board -----------------------------------------------------
    $firstB = if ($bs.Count -gt 0) { $bs[0] } else { $null }
    if ($null -ne $firstB) {
        Write-Host ""
        Write-Host "THE LATE JOINER'S FIRST BOARD (this is the line to quote across runs):" -ForegroundColor White
        Write-Host "        $($firstB.Raw)" -ForegroundColor DarkGray
        $peer = Get-Nearest $a $firstB.T
        if ($null -eq $peer) {
            Add-Failure ("the late joiner's first board line has no line from A within " +
                         "${PairWindowMs} ms, so there is nothing to compare it against")
        } else {
            Write-Host "        A at the same instant:" -ForegroundColor DarkGray
            Write-Host "        $($peer.Raw)" -ForegroundColor DarkGray
            [void](Compare-Boards "the late joiner's FIRST board" $firstB $peer)
        }
        if ($firstB.Count -lt 1) {
            Add-Failure ("the late joiner's first board line has rows=0. The board is read off " +
                         "its Label3Ds, so this is a blank panel and not merely an empty view - " +
                         "ROUND-1's message is an absolute fold and one message is supposed to " +
                         "be enough.")
        }
    }

    # --- 2. every pair, not just the first ----------------------------------------------------
    $compared = 0; $skippedEdge = 0; $unpaired = 0; $agreed = 0
    foreach ($s in $bs) {
        $nearEdge = $false
        foreach ($t in $phaseChangeTimes) {
            if ([math]::Abs($s.T - $t) -le $PhaseEdgeGuardMs) { $nearEdge = $true; break }
        }
        if ($nearEdge) { $skippedEdge++; continue }
        $peer = Get-Nearest $a $s.T
        if ($null -eq $peer) { $unpaired++; continue }
        $compared++
        if (Compare-Boards "at wall=$($s.T)" $s $peer) { $agreed++ }
    }

    if ($compared -lt $MinPairs) {
        Add-Failure ("only $compared board pair(s) were actually compared (skipped $skippedEdge " +
                     "at a phase edge, $unpaired unpaired) against a floor of $MinPairs. A guard " +
                     "that swallowed everything must not read as green.")
    }

    # --- 3. the board eventually shows BOTH humans and a footer -------------------------------
    $twoRow = @($bs | Where-Object { $_.Count -ge 2 })
    if ($twoRow.Count -eq 0) {
        Add-Failure ("the late joiner never saw a board with two rows on it, so the row model " +
                     "was never exercised with more than one human - the whole session was one " +
                     "peer as far as the wire was concerned")
    }
    $withFooter = @($bs | Where-Object { $_.Footer.Length -gt 0 })
    if ($withFooter.Count -eq 0) {
        Add-Failure ("the footer was empty on every one of the late joiner's samples. The round " +
                     "script ends at t=50, so a card should have landed and stayed up - read " +
                     "holdboard-server.out.log for whether the round reached Tally at all.")
    }

    # --- 4. THE THIRD PLAYER IS WAITING, AND SAYS SO ON ITS OWN WALL (SOLO-1) -----------------
    # Read off the ROW C painted for ITSELF. A row is "NAME . ROLE . SCORE" and C's display name
    # is its bot name, so this finds its own line without needing its peer id.
    $cWaiting = 0
    $cRowsSeen = 0
    foreach ($s in $bs) {
        foreach ($row in @($s.Rows)) {
            if ($row -notmatch '^BoardC') { continue }
            $cRowsSeen++
            if ($row -match 'WAITING') { $cWaiting++ }
        }
    }
    if ($cRowsSeen -eq 0) {
        Add-Failure ("the late joiner never painted a row for itself. Every human on the wire owns " +
                     "a score row (HideSeekLoop folds one in at zero), so a board with no BoardC " +
                     "row means the third peer is not on the round message at all.")
    } elseif ($cWaiting -eq 0) {
        Add-Failure ("the late joiner painted $cRowsSeen row(s) for itself and not one of them says " +
                     "WAITING. A third player holds neither role and the cell is the only place " +
                     "the board answers 'am I in this game'. Sample row: " +
                     "$(@($bs | ForEach-Object { @($_.Rows) | Where-Object { $_ -match '^BoardC' } }) | Select-Object -First 1)")
    }
    # And the two who ARE playing must not read WAITING, or the word means nothing.
    $playerWaiting = 0
    foreach ($s in $bs) {
        foreach ($row in @($s.Rows)) {
            if ($row -match '^Board[AB]' -and $row -match 'WAITING') { $playerWaiting++ }
        }
    }
    if ($playerWaiting -gt 0) {
        Add-Failure ("$playerWaiting row(s) belonging to BoardA/BoardB read WAITING -- those two " +
                     "hold the roles, so the cell is not distinguishing anything")
    }

    # --- 5. AND IT CANNOT RUN THE ROUND (SOLO-1) -----------------------------------------------
    $buttonLines = @()
    foreach ($line in (Get-Content $serverOut)) {
        if ($line -match '^\[buttons\] press ') { $buttonLines += $line }
    }
    foreach ($line in $buttonLines) { Write-Host "        $line" -ForegroundColor DarkGray }

    $notInMatch = @($buttonLines | Where-Object { $_ -match 'refused reason=NotInThisMatch' }).Count
    $tooFar = @($buttonLines | Where-Object { $_ -match 'refused reason=TooFarAway' }).Count
    if ($buttonLines.Count -eq 0) {
        Add-Failure ("the late joiner's START presses never reached the server at all. A press that " +
                     "is never made is never refused (TASK-1 section 3.3) -- read " +
                     "holdboard-BoardC.out.log for whether --press fired, before concluding " +
                     "anything about the gate.")
    } elseif ($notInMatch -eq 0) {
        Add-Failure ("none of the late joiner's $($buttonLines.Count) press(es) was refused with " +
                     "NotInThisMatch ($tooFar came back TooFarAway). TooFarAway on every one of " +
                     "them is a STAGING failure -- the reach check runs BEFORE the gate, so a bot " +
                     "that did not finish its walk is answered by the wrong layer; NotInThisMatch " +
                     "missing with nothing else present is the gate itself.")
    }
    if (@($buttonLines | Where-Object { $_ -match 'accepted' }).Count -gt 0) {
        Add-Failure ("a press from the late joiner was ACCEPTED. A third player is in the room and " +
                     "not in the match; it must not be able to start, confirm or end anybody " +
                     "else's round.")
    }

    Write-Host ""
    Write-Host "MEASURED QUANTITIES (compare these across runs, not the verdict):" -ForegroundColor White
    Write-Host "  late joiner rows reading WAITING: $cWaiting of $cRowsSeen"
    Write-Host "  its presses refused NotInThisMatch / TooFarAway: $notInMatch / $tooFar"
    Write-Host "  A board samples:                  $($a.Count)"
    Write-Host "  late joiner board samples:        $($bs.Count)"
    Write-Host "  pairs compared / agreed:          $compared / $agreed"
    Write-Host "  skipped at a phase edge:          $skippedEdge"
    Write-Host "  unpaired (no A line within ${PairWindowMs} ms): $unpaired"
    Write-Host "  samples showing two humans:       $($twoRow.Count)"
    Write-Host "  samples carrying a footer:        $($withFooter.Count)"
}
finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

if ($script:Failures.Count -gt 0) {
    Write-Host ""
    Write-Host "HOLDING-BOARD TEST FAILED ($($script:Failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host "HOLDING-BOARD TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "HOLDING-BOARD TEST OVERALL: PASS" -ForegroundColor Green
exit 0
