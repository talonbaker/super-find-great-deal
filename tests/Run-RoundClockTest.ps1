<#
.SYNOPSIS
    CLOCK-1: the diegetic countdown, driven once end to end on a real server with three real
    clients. Proves the one thing no pure function can: that three separate processes, each
    holding its own folded view of the round, paint the SAME SECOND on the walls at the same
    instant -- including a client that joined after the round had already started.

.DESCRIPTION
    Everything about the clock that is a RULE is tested engine-free in tests/unit
    (RoundClockTests: ClockLine for every phase, the fit arithmetic, ForEdge for every row of
    the packet's cue table and for the no-op edges, and every new Sfx recipe's headroom). None of
    that is repeated here. What is left is what needs three processes and a wire:

      1. EVERY CLIENT'S CLOCK MATCHES THE SERVER'S, at every sample. The server prints its own
         phase and second at 1 Hz off ServerState -- not off its folded view, so a fold bug
         cannot make both sides of the comparison wrong in the same direction. Each client
         prints one line per clock per second, with the PHASE WORD READ OFF THE LABEL, so a
         clock that never painted logs "-" and fails here rather than passing on a blank panel.
         Every client line is matched to the server line nearest it in wall-clock time (one
         machine, one clock) and must agree on the phase and be within one second.

      2. THE LATE JOINER IS RIGHT FROM THE FIRST MESSAGE. A third bot is launched when the
         server logs Hiding -> Seeking, so it arrives mid-round having witnessed nothing. Its
         FIRST clock line is asserted against the server the same way, on its own -- the
         absolute-wire property, seen from the wall rather than from the wire.

      3. THE CUE SEQUENCE IS THE PACKET'S, IN ORDER. ChimeUp, then ticks, then Note, then
         Triumph, then ResetWhoosh. Read off a client's own --log-clock lines, so it is the
         sequence a player would have HEARD and not the one the server intended.

    WHY --log-clock AND NOT --log-sfx. SFX-1 adds a --log-sfx flag on its own branch for the
    material voices and this branch cannot see it; two lanes each inventing that flag is a merge
    conflict for no gain. The round's cue sequence is a fact about the round, so it rides the
    round's own flag. See docs/agents/handoffs/2026-09-19-CLOCK-1.md.

    THE SCHEDULE IS NOT THE PACKET'S, AND THE REASON IS ARITHMETIC. The packet names
    "start@2,confirm@8,found@20,end@25". Hiding is 30 s, so a Confirm 6 s after Start leaves 24 s
    on the clock and the countdown NEVER REACHES TEN -- that script produces zero ticks, and the
    packet's own expected sequence asks for "Tick x?". start@2 also races the second bot's
    connect (the script clock starts when the FIRST player arrives, and a Godot client takes
    several seconds to boot), which would refuse the Start with NeedTwoPlayers and end the run
    before it began. The schedule below keeps the packet's intervals and moves Confirm to 5 s
    before the buzzer so the last seconds are actually counted. Seeking's 180 s cannot be counted
    down inside a suite, so the Seeking-tick row is covered in xUnit only and that is said in the
    handoff rather than hidden.

    NOT ONE WALL-CLOCK GUESS IN THE ASSERTIONS. Every comparison is anchored on a timestamp both
    sides printed, and the late joiner's launch is anchored on a line the SERVER wrote. The only
    typed numbers are lifetimes -- ceilings, derived from the schedule they must outlive.

    PROVED ABLE TO FAIL. See the handoff's evidence block for the planted fault and what it
    printed.

    Exit 0 = PASS. Headless throughout -- no GPU, no display. One Godot at a time: this suite
    takes the machine-wide suite mutex like every other.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param(
    # 7904. The ladder as CLOCK-1 found it: Run-CarryNetTest 7893/7894/7895, Run-RoundLoopSmoke
    # 7896, Run-FirstPersonTest 7897, Run-PlaceTest 7898, and 7899-7903 claimed by the other
    # lanes of this wave (the orchestrator handed those out; none of them is visible from this
    # branch). INT-0's entry in .claude/rules/test-suite.md is the reason this is stated rather
    # than computed: three lanes off one base all computed "the next free port" as 7896 and all
    # three were wrong. A collision at RUN time here is another lane's process, not a defect --
    # re-run, or take 7905.
    [int]$Port = 7904,
    [int]$MutexTimeoutMinutes = 30,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# ------------------------------------------------------------------------------------------
# The schedule. Seconds from the FIRST PLAYER ARRIVING (the driver's script-clock gate), so
# these are margins over connection time rather than guesses about it.
#
#   8   Start          -> Hiding, 30 s on the clock, hider to the search room.  ChimeUp.
#   28  (t+20)         -> the clock reads 10.  Ticks begin: 10, 9, 8, 7, 6.
#   33  Confirm        -> Seeking with ~5 s left on the hide clock.             Note.
#   45  the bin        -> Together, seeker to the vestibule.                    (DOOR-1's bang)
#   50  End            -> Tally.                                                Triumph.
#   56  (+6 s tally)   -> the reset edge, everyone home.                        ResetWhoosh.
#
# Confirm at 33 rather than at the packet's +6 is the whole reason the tick rows are reachable
# at all -- see the DESCRIPTION.
# ------------------------------------------------------------------------------------------
$Script = "start@8,confirm@33,sorts:3@40,found@45,end@50"

# A CEILING over the schedule above, not an assumption about when anything lands: the last beat
# is the reset at 56 s after the first arrival, and a client takes a few seconds to boot before
# that clock even starts.
$BotDurationSec = 75

# The late joiner's own lifetime. It is launched on a SERVER LOG LINE (Hiding -> Seeking, at
# t = 33), so it needs to outlive what is left: ~23 s of round plus boot plus settle.
$LateBotDurationSec = 45

# How far apart two log lines may be and still be "the same instant". The logs are at 1 Hz from
# three processes on one machine with one wall clock, so the nearest server line to a client
# line is at most half a period away; 600 ms is that with room for a scheduling hiccup, and a
# client sample that cannot find a server line inside it is NOT compared (and is counted, so a
# run that compared nothing cannot pass).
$PairWindowMs = 600

# The packet's bar. Anything within one second is agreement: the wire carries tenths at 10 Hz
# and both sides floor, so a legitimate straddle of a second boundary is exactly 1.
$ToleranceSec = 1

# A client sample this close to a server phase transition is skipped rather than compared. At
# 1 Hz logging and a 10 Hz wire, a client can legitimately log 200 ms after the server changed
# phase and before its own message has landed; that is the wire's latency, not a wrong clock,
# and the assertion it would fail is one the tolerance above is not about.
$PhaseEdgeGuardMs = 1200

Write-Host "=== CLOCK-1: the diegetic countdown, three peers, one second ===" -ForegroundColor White

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
    Write-Host "[1/5] dedicated server on udp/$Port, world supermarket, --round-script, --log-clock..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "roundclock-server.out.log"
    $server = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket",
        "--round-script", $Script, "--log-clock") `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "roundclock-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server

    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "the server never reported listening; see roundclock-server.out.log"
    }
    Write-Host "        server up (pid $($server.Id))"

    # --- the two players ----------------------------------------------------------------------
    Write-Host "[2/5] two bots for ${BotDurationSec}s..." -ForegroundColor Cyan
    $bots = @()
    function Start-ClockBot([string]$Name, [int]$DurationSec) {
        $out = Join-Path $script:LogDir "roundclock-$Name.out.log"
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
            "--headless", "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
            "--duration", $DurationSec, "--world", "supermarket", "--log-clock") `
            -RedirectStandardOutput $out `
            -RedirectStandardError (Join-Path $script:LogDir "roundclock-$Name.err.log") `
            -PassThru -NoNewWindow
        $null = $p.Handle
        return @{ Name = $Name; Proc = $p; Out = $out }
    }

    foreach ($name in @("ClockA", "ClockB")) {
        $b = Start-ClockBot $name $BotDurationSec
        $procs += $b.Proc
        $bots += $b
        # A short stagger so join ORDER is unambiguous -- the first to arrive is this round's
        # hider, and two clients racing to connect makes which-one-hides a coin flip.
        Start-Sleep -Milliseconds 1200
    }

    # --- the late joiner, launched on the SERVER'S OWN LINE ------------------------------------
    # Not at a typed second. Hiding -> Seeking is logged by the driver at script t = 33, and a
    # Godot client boots in a few seconds, so this arrives inside the 12 s of Seeking that
    # follows. If it is slow and arrives in Together or Tally instead, the assertion is unchanged
    # and still meaningful: whatever phase it lands in, its first line must already be right.
    Write-Host "[3/5] waiting for the server to reach Seeking, then joining a third bot late..." -ForegroundColor Cyan
    if (-not (Wait-ForLogLine $serverOut "\[round\] phase Hiding -> Seeking" 120)) {
        Add-Failure "the server never reached Seeking, so the late joiner was never launched"
        $late = $null
    } else {
        $late = Start-ClockBot "ClockLate" $LateBotDurationSec
        $procs += $late.Proc
        $bots += $late
        Write-Host "        late joiner launched (pid $($late.Proc.Id))"
    }

    Write-Host "[4/5] waiting for the round to finish..." -ForegroundColor Cyan
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
    Write-Host "[5/5] reading the server's clock and all three peers' walls..." -ForegroundColor Cyan

    if (-not (Test-Path $serverOut)) { Write-Fail "no server log was written at all" }
    $serverLines = @(Get-Content $serverOut)

    function ConvertTo-Seconds([string]$Mmss) {
        if ($Mmss -notmatch '^(\d+):(\d{2})$') { return $null }
        return [int]$matches[1] * 60 + [int]$matches[2]
    }

    # The server's own reference: "[clock] <unixms> server <PHASE> <M:SS>"
    $serverSamples = @()
    foreach ($line in $serverLines) {
        if ($line -match '\[clock\] (\d+) server (\S+) (\d+:\d{2})') {
            $serverSamples += ,@{ T = [long]$matches[1]; Phase = $matches[2]
                                  Sec = (ConvertTo-Seconds $matches[3]); Text = $matches[3] }
        }
    }

    # The server's phase transitions, for the boundary guard.
    $phaseChangeTimes = @()
    $lastPhase = $null
    foreach ($s in $serverSamples) {
        if ($null -ne $lastPhase -and $s.Phase -ne $lastPhase) { $phaseChangeTimes += $s.T }
        $lastPhase = $s.Phase
    }

    if ($serverSamples.Count -lt 10) {
        Add-Failure ("the server logged only $($serverSamples.Count) clock sample(s) -- " +
                     "--log-clock did not reach the dedicated server, so there is nothing to compare against")
    }

    # Each client: "[clock] <unixms> clock <room> <PHASE> <M:SS> shown=<text>"
    #          and "[clock] <unixms> cue <Name> <layer>"
    foreach ($b in $bots) {
        $b.Samples = @()
        $b.Cues = @()
        if (-not (Test-Path $b.Out)) {
            Add-Failure "$($b.Name) wrote no log at all"
            continue
        }
        foreach ($line in [System.IO.File]::ReadAllLines($b.Out)) {
            if ($line -match '\[clock\] (\d+) clock (\S+) (\S+) (\d+:\d{2}) shown=(\S+)') {
                $b.Samples += ,@{ T = [long]$matches[1]; Room = $matches[2]; Phase = $matches[3]
                                  Sec = (ConvertTo-Seconds $matches[4]); Text = $matches[4]
                                  Shown = $matches[5] }
            } elseif ($line -match '\[clock\] (\d+) cue (\S+) (\S+)') {
                $b.Cues += ,@{ T = [long]$matches[1]; Name = $matches[2]; Layer = $matches[3] }
            }
        }
    }

    # ==========================================================================================
    # 1. Three clocks per client, one per room, and all three were painted
    # ==========================================================================================
    foreach ($b in $bots) {
        $rooms = @($b.Samples | ForEach-Object { $_.Room } | Sort-Object -Unique)
        Write-Host "        $($b.Name): $(@($b.Samples).Count) clock sample(s) across rooms [$([string]::Join(', ', $rooms))]" -ForegroundColor DarkGray
        foreach ($want in @("holding", "search", "task")) {
            if ($rooms -notcontains $want) {
                # No backticks in this string. The first version wrote "its `room` export" and
                # PowerShell ate it: inside a double-quoted string a backtick is the escape
                # character, so `r became a carriage return and the message printed as "its oom
                # export". A failure message that mangles itself is a failure message nobody
                # trusts.
                Add-Failure ("$($b.Name) never logged the '$want' room's clock -- either the room " +
                             "scene does not instance RoundClock.tscn, or RoundClock.ResolveRoom " +
                             "could not find a room node above it")
            }
        }
        # A clock that never painted logs "-" for its phase. That is the whole reason the phase
        # word is read off the LABEL rather than recomputed from the view.
        $blank = @($b.Samples | Where-Object { $_.Phase -eq "-" })
        if ($blank.Count -gt 0) {
            Add-Failure ("$($b.Name) logged $($blank.Count) sample(s) with an unpainted phase " +
                         "label -- the clock is in the tree but its text was never set")
        }
    }

    # ==========================================================================================
    # 2. Every client clock agrees with the server, at every sample
    # ==========================================================================================
    $compared = 0
    $skippedEdge = 0
    $unpaired = 0
    $worstDelta = 0
    foreach ($b in $bots) {
        $bWorst = 0
        $bCompared = 0
        foreach ($s in @($b.Samples)) {
            # The nearest server line in time. Not "the last one before it": the logs are
            # independent 1 Hz timers and either can be first within a period.
            $near = $null
            $bestGap = [long]::MaxValue
            foreach ($ss in $serverSamples) {
                $gap = [math]::Abs($ss.T - $s.T)
                if ($gap -lt $bestGap) { $bestGap = $gap; $near = $ss }
            }
            if ($null -eq $near -or $bestGap -gt $PairWindowMs) { $unpaired++; continue }

            # Within a phase boundary, a client can legitimately still be showing the phase it
            # had when the server had already changed -- that is the wire's latency, not a wrong
            # clock, and it is a different question from the one this suite asks.
            $nearEdge = $false
            foreach ($pc in $phaseChangeTimes) {
                if ([math]::Abs($pc - $s.T) -le $PhaseEdgeGuardMs) { $nearEdge = $true; break }
            }
            if ($nearEdge) { $skippedEdge++; continue }

            $compared++
            $bCompared++
            if ($s.Phase -ne $near.Phase) {
                Add-Failure ("$($b.Name)'s '$($s.Room)' clock read phase $($s.Phase) while the " +
                             "server was in $($near.Phase) ($($bestGap) ms apart, nowhere near a " +
                             "transition)")
                continue
            }
            $delta = [math]::Abs($s.Sec - $near.Sec)
            if ($delta -gt $bWorst) { $bWorst = $delta }
            if ($delta -gt $worstDelta) { $worstDelta = $delta }
            if ($delta -gt $ToleranceSec) {
                Add-Failure ("$($b.Name)'s '$($s.Room)' clock read $($s.Text) while the server " +
                             "read $($near.Text) in $($near.Phase) -- $delta s apart, bar is " +
                             "$ToleranceSec s")
            }
        }
        Write-Host "        $($b.Name): $bCompared sample(s) compared, worst disagreement $bWorst s" -ForegroundColor DarkGray

        # In the two phases that HAVE a clock, the painted second line must be the same M:SS the
        # comparison above used. This is what closes the gap left by logging M:SS off the view:
        # without it, a panel showing last round's tally forever would still pass section 2.
        foreach ($s in @($b.Samples | Where-Object { $_.Phase -eq "HIDING" -or $_.Phase -eq "SEEKING" })) {
            if ($s.Shown -ne $s.Text) {
                Add-Failure ("$($b.Name)'s '$($s.Room)' clock PAINTED '$($s.Shown)' while the " +
                             "round's second was $($s.Text) in $($s.Phase) -- the panel and the " +
                             "view have come apart")
            }
        }
    }

    if ($compared -lt 30) {
        Add-Failure ("only $compared client/server clock pair(s) were compared " +
                     "($unpaired unpaired, $skippedEdge skipped at a phase edge) -- the suite " +
                     "proved nothing about agreement")
    }

    # ==========================================================================================
    # 3. The late joiner's FIRST line is already right
    # ==========================================================================================
    if ($null -ne $late) {
        $first = @($late.Samples) | Select-Object -First 1
        if ($null -eq $first) {
            Add-Failure "the late joiner logged no clock sample at all -- it never synced"
        } else {
            $near = $null
            $bestGap = [long]::MaxValue
            foreach ($ss in $serverSamples) {
                $gap = [math]::Abs($ss.T - $first.T)
                if ($gap -lt $bestGap) { $bestGap = $gap; $near = $ss }
            }
            if ($null -eq $near -or $bestGap -gt $PairWindowMs) {
                Add-Failure ("the late joiner's first clock line has no server line within " +
                             "$PairWindowMs ms, so it cannot be judged")
            } else {
                Write-Host ("        late joiner's FIRST line: $($first.Room) $($first.Phase) " +
                            "$($first.Text)  (server: $($near.Phase) $($near.Text), " +
                            "$bestGap ms apart)") -ForegroundColor DarkGray
                # NOT guarded by the phase-edge window: the point is precisely that a peer which
                # witnessed nothing is complete from one message, so its first line gets the
                # strict comparison.
                if ($first.Phase -ne $near.Phase) {
                    Add-Failure ("the late joiner's FIRST clock line read $($first.Phase) while " +
                                 "the server was in $($near.Phase) -- it is not complete from one message")
                }
                $d = [math]::Abs($first.Sec - $near.Sec)
                if ($d -gt $ToleranceSec) {
                    Add-Failure ("the late joiner's FIRST clock line read $($first.Text) against " +
                                 "the server's $($near.Text) -- $d s out on the first message")
                }
            }
            # ...and it must have joined into a round that was ALREADY RUNNING, or "correct from
            # the first message" is a claim about a peer that saw the start like everyone else.
            if ($first.Phase -eq "HOLDING") {
                Add-Failure ("the late joiner's first sample was in HOLDING -- it did not arrive " +
                             "mid-round, so it proves nothing about a late join")
            }
        }

        # THE SILENCE IS THE ASSERTION. A peer that witnessed nothing must hear nothing it
        # missed: no ChimeUp, no Note. Without this, ForEdge could be chiming at every joiner
        # and every check above would still pass.
        $lateEarly = @($late.Cues | Where-Object { $_.Name -eq "ChimeUp" -or $_.Name -eq "Note" })
        if ($lateEarly.Count -gt 0) {
            Add-Failure ("the late joiner fired [" +
                         [string]::Join(',', @($lateEarly | ForEach-Object { $_.Name })) +
                         "] for edges it never witnessed -- a joiner polls, it does not replay")
        }
    }

    # ==========================================================================================
    # 4. The cue sequence, in order, as a player would have heard it
    # ==========================================================================================
    $witness = $bots[0]
    $heard = @($witness.Cues | ForEach-Object { $_.Name })
    Write-Host "        $($witness.Name) heard: $([string]::Join(', ', $heard))" -ForegroundColor DarkGray

    $wanted = @("ChimeUp", "Tick", "Note", "Triumph", "ResetWhoosh")
    $cursor = 0
    foreach ($w in $wanted) {
        $found = -1
        for ($i = $cursor; $i -lt $heard.Count; $i++) {
            if ($heard[$i] -eq $w) { $found = $i; break }
        }
        if ($found -lt 0) {
            Add-Failure ("the cue '$w' never fired, or fired out of order -- $($witness.Name) heard [" +
                         [string]::Join(',', $heard) + "]")
            break
        }
        $cursor = $found + 1
    }

    $ticks = @($heard | Where-Object { $_ -eq "Tick" })
    Write-Host "        ticks heard in the hide's last seconds: $($ticks.Count)" -ForegroundColor DarkGray
    if ($ticks.Count -lt 3) {
        Add-Failure ("only $($ticks.Count) tick(s) fired -- the schedule is meant to leave the " +
                     "hide clock running from 10 down to about 5, so the countdown row is untested")
    }

    # Both full-length peers must have heard the SAME cues. A cue list derived from the client's
    # own view is only correct if two clients derive the same one.
    if ($bots.Count -ge 2) {
        $a = [string]::Join(',', @($bots[0].Cues | ForEach-Object { $_.Name }))
        $c = [string]::Join(',', @($bots[1].Cues | ForEach-Object { $_.Name }))
        if ($a -ne $c) {
            Add-Failure ("the two peers present throughout heard different cue sequences: " +
                         "[$a] vs [$c]")
        }
    }

    Write-Host ""
    Write-Host "MEASURED QUANTITIES (compare these across runs, not the verdict):" -ForegroundColor White
    Write-Host "  server clock samples logged:                         $($serverSamples.Count)"
    Write-Host "  client/server clock pairs compared:                  $compared"
    Write-Host "  pairs skipped inside a phase-change window:          $skippedEdge"
    Write-Host "  client samples with no server line within ${PairWindowMs}ms:  $unpaired"
    Write-Host "  worst client-vs-server disagreement:                 $worstDelta s (bar $ToleranceSec s)"
    Write-Host "  cues heard by $($witness.Name):                            $([string]::Join(', ', $heard))"
}
finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host ""
if ($script:Failures.Count -gt 0) {
    Write-Host "ROUNDCLOCK-TEST FAILED ($($script:Failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "ROUNDCLOCK-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: three peers' wall clocks tracked the server's phase and second all the way through a round; a bot that joined mid-round was correct on its first message and silent about the edges it missed; the cue sequence fired in the packet's order and both full-length peers derived the same one." -ForegroundColor Green
Write-Host ""
Write-Host "ROUNDCLOCK-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
