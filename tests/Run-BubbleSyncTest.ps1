<#
.SYNOPSIS
    BT-6: one shared, server-adjudicated bubble tally converges across three peers — including
    one that joined after the pops — and a reset puts every bubble back on all of them.

.DESCRIPTION
    Launches a headless dedicated server in the "open" world with --bubble-selftest, which
    installs six bubbles at known points plus a BubbleCounter. Every bot gets the same flag and
    builds the same six bubbles locally; only the tally travels (program D6).

    The fixture's layout is what makes the walk deterministic rather than lucky. Bot A connects
    first, so Gameplay.OnPeerConnected deals it GameWorld.SpawnPoints[0] = (4, 1.1, 0), and
    --goto-script 4,24 walks it straight up the x = 4 corridor. Bubbles 2 and 4 sit ON that
    corridor at z = 8 and z = 16; bubbles 0, 1, 3 and 5 sit 8 m to either side — twenty times the
    0.35 m collider radius — so "A pops exactly two bubbles, and they are 2 and 4" is geometry.
    Bots B and C are handed a goto target inside their own arrive radius, so they stand still and
    witness; B is present throughout, C joins only after the server's log confirms both pops.

      1. Adoption      - every peer adopted the same six bubbles (an id space that disagrees
                         makes every later assertion meaningless, so it is checked first).
      2. Convergence   - the first sample in which any peer sees the tally reach 2 has popped
                         = {2,4}, and so does every sample after it until the reset, on all
                         three peers, from three separate processes' logs.
      3. Late join     - bot C's VERY FIRST sample is already synced, already reads 2, and
                         already has bubbles 2 and 4 hidden. It never renders a frame with a
                         bubble that is not there.
      4. No client pop - every client locally hides bubble 0 (BubbleSelfTest's forge). The
                         hidden set proves the forge really ran; no peer's POPPED set ever
                         contains 0 and no tally moves. Acceptance criterion 5's control.
      5. Stray press   - (LEVER-1) at the scheduled mark two DIFFERENT peers each pull the reset
                         lever ONCE and neither follows through. Two warnings are raised, ZERO
                         resets land, and the tally the fixture prints either side of the pulls
                         is unchanged. This is Talon's own sentence made a live assertion - "I
                         would be upset if I spent hours collecting all the bubbles and then
                         someone reset my progress" - and the two-arms count is its positive
                         control: an absence proof about the board surviving is worth nothing
                         unless the presses demonstrably happened.
      6. Reset         - a beat later both peers CONFIRM, in the same frame. Exactly one pull is
                         accepted and exactly one refused, one reset broadcast goes out, and
                         every peer reads 0, has an empty popped set, and shows every bubble
                         visible again. Two things at once: a deliberate confirmed press does
                         still reset (the companion bug to 5, and the easier one to ship), and
                         BT-8's acceptance criterion 4 - two confirms inside the 1.2 s cooldown
                         produce ONE broadcast - survives the new warning in front of it.
      7. Bob bound     - the worst distance any bubble sat from its authored position, on any
                         peer, over the whole run, stays inside 0.15 m (criterion 6).

      8. Window        - (FIX-1) assertion 6's own positive control, and the reason this file has
                         a history. The late joiner's LAST sample must land strictly AFTER the
                         sample in which the reset first shows up on the two peers that were
                         present throughout. Without it, "C never saw the reset" is ambiguous
                         between a replication defect and a bot that had already gone home, and
                         this suite spent its whole life reporting the second as the first.

    Assertions are keyed to the OBSERVED tally rather than to wall-clock windows: the three peers
    start their clocks seconds apart by construction, and a fixed window would be measuring boot
    time rather than convergence.

    BOT LIFETIMES ARE DERIVED, NOT TYPED (FIX-1, 2026-09-04). Every bot exits at ONE absolute
    moment: $PostResetSettleSec after the reset lands. That moment is read off the server's own
    "[bubbletest] reset schedule:" line, which the fixture prints from $ResetAtSec and from
    BubbleSelfTest.ConfirmDelaySec (itself derived from BubbleResetConfirm.MinDwellSec and
    ConfirmWindowSec). So --bubble-reset-at or either lever constant moving MOVES THE WINDOWS
    WITH IT. Before FIX-1 the late joiner had a typed 26 s lifetime against a 30 s reset mark
    measured from a different clock; it exited 141 ms before the reset's effect was observable on
    any peer, every run, and assertion 6's late-joiner half had therefore never executed once.
    A number typed here beside the reset mark is a third copy of a timing that already has two.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [int]$Port = 7891,
    [double]$ResetAtSec = 30,
    # How long after the reset lands every bot keeps sampling. The only free number in the
    # schedule, and it is a MARGIN, not a mark: at the ~4.9 Hz these bots sample, 6 s is ~29
    # post-reset samples on every peer, where assertion 8 needs one. Chosen (FIX-1) large enough
    # that boot skew, a slow flush of the server's schedule line and a late broadcast together
    # cannot eat it, and small enough to keep the suite near its historical ~40 s.
    [double]$PostResetSettleSec = 6,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# The fixture's own constants (scripts/game/bubble/BubbleSelfTest.cs). Restated here rather than
# derived, so a change on either side shows up as a failing assertion instead of as two files
# quietly agreeing about the wrong thing.
$ExpectedBubbles = 6
$CorridorPops = @(2, 4)
$ForgeSubject = 0
$MaxOffsetM = 0.15

# FIX-1: one absolute exit moment for all three bots, expressed as a --duration each because that
# is the only lifetime knob BotHarness has. $Zero is when the server's fixture clock started (the
# frame it printed its schedule); $EndSec is that clock's reading at which everyone goes home.
# Floored at 5 s so a pathologically slow launch still produces a bot rather than a parse error,
# and formatted invariant because LaunchOptions parses --duration with InvariantCulture.
function Get-BotDurationArg([datetime]$Zero, [double]$EndSec) {
    $remaining = $EndSec - ((Get-Date) - $Zero).TotalSeconds
    if ($remaining -lt 5) { $remaining = 5 }
    return $remaining.ToString("F2", [System.Globalization.CultureInfo]::InvariantCulture)
}

Write-Host "=== bubbles: server-adjudicated pop, one shared tally, late join, reset ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$aLog = Join-Path $script:LogDir "bubbleA.jsonl"
$bLog = Join-Path $script:LogDir "bubbleB.jsonl"
$cLog = Join-Path $script:LogDir "bubbleC.jsonl"

$procs = @()
try {
    Write-Host "[1/4] launching dedicated server (open world + bubble fixture) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "bubble.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "open",
        "--bubble-selftest", "--bubble-reset-at", $ResetAtSec) "bubble.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "server never reported listening; see $serverOut"
    }
    if (-not (Wait-ForLogLine $serverOut "\[bubble\] adopted $ExpectedBubbles bubble" 20)) {
        Write-Fail "server never adopted $ExpectedBubbles bubbles; see $serverOut"
    }
    # FIX-1: the reset schedule, from the only peer that owns it, at the frame its fixture clock
    # started. Everything about bot lifetimes below is derived from this line rather than typed
    # beside it, so --bubble-reset-at and both BubbleResetConfirm constants move the windows.
    if (-not (Wait-ForLogLine $serverOut "\[bubbletest\] reset schedule:" 20)) {
        Write-Fail ("server never announced its reset schedule; see $serverOut. This line is what " +
            "the bot lifetimes are derived from - without it the suite would be timing bots " +
            "against a typed constant again, which is the FIX-1 defect.")
    }
    $sessionZero = Get-Date
    $scheduleHit = Select-String -Path $serverOut -Pattern "\[bubbletest\] reset schedule:" | Select-Object -First 1
    $scheduleLine = if ($null -ne $scheduleHit) { $scheduleHit.Line } else { "" }
    $schedMatch = [regex]::Match($scheduleLine, "resetAt=([0-9.]+)s confirmDelay=([0-9.]+)s resetLandsAt=([0-9.]+)s")
    if (-not $schedMatch.Success) {
        Write-Fail "could not parse the server's reset schedule line: $scheduleLine"
    }
    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    $announcedResetAt = [double]::Parse($schedMatch.Groups[1].Value, $inv)
    $confirmDelaySec  = [double]::Parse($schedMatch.Groups[2].Value, $inv)
    $resetLandsAtSec  = [double]::Parse($schedMatch.Groups[3].Value, $inv)
    # The server is meant to be running the mark this script asked for. If it is not, every
    # derivation below is off by the difference and the whole schedule is a guess again.
    if ([math]::Abs($announcedResetAt - $ResetAtSec) -gt 0.01) {
        Write-Fail ("the server scheduled its reset at ${announcedResetAt}s but this script asked " +
            "for ${ResetAtSec}s - --bubble-reset-at is not reaching the fixture")
    }
    $sessionEndSec = $resetLandsAtSec + $PostResetSettleSec
    Write-Host "        server up (pid $($server.Id))"
    Write-Host ("        schedule: reset at {0}s + {1}s lever confirm = lands {2}s; every bot exits at {3}s (+{4}s settle)" -f `
        $announcedResetAt, $confirmDelaySec, $resetLandsAtSec, $sessionEndSec, $PostResetSettleSec)

    Write-Host "[2/4] launching the walker and the witness..." -ForegroundColor Cyan
    $aOut = Join-Path $script:LogDir "bubbleA.out.log"
    $botA = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "BubbleBotA",
        "--log", $aLog, "--duration", (Get-BotDurationArg $sessionZero $sessionEndSec), "--world", "open",
        "--bubble-selftest", "--goto-script", "4,24") "bubbleA"
    $procs += $botA
    # Gate on A actually being connected before B is launched, rather than on a sleep: the spawn
    # point is dealt by join order (Gameplay._spawnIndex++), and A has to be peer index 0 for the
    # corridor walk above to be the walk this test describes.
    if (-not (Wait-ForLogLine $aOut "\[bot\] BubbleBotA running as peer" 40)) {
        Write-Fail "bot A never connected; see $aOut"
    }

    $botB = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "BubbleBotB",
        "--log", $bLog, "--duration", (Get-BotDurationArg $sessionZero $sessionEndSec), "--world", "open",
        "--bubble-selftest", "--goto-script", "-3,3") "bubbleB"
    $procs += $botB

    Write-Host "[3/4] waiting for both corridor pops, then joining a third peer late..." -ForegroundColor Cyan
    if (-not (Wait-ForLogLine $serverOut "\[bubbletest\] pop id=$($CorridorPops[0]) " 60)) {
        Write-Fail "bot A never popped bubble $($CorridorPops[0]); see $serverOut"
    }
    if (-not (Wait-ForLogLine $serverOut "\[bubbletest\] pop id=$($CorridorPops[1]) " 60)) {
        Write-Fail "bot A never popped bubble $($CorridorPops[1]); see $serverOut"
    }

    # FIX-1: C's lifetime is what is left of the SHARED session, not a constant of its own. It
    # launches a few seconds in (measured ~5 s, once both corridor pops are on the server's log)
    # and therefore gets a shorter --duration than A and B did - but all three numbers name the
    # SAME absolute exit moment, on the far side of the reset, instead of three independent launch
    # clocks racing it.
    $lateDurationArg = Get-BotDurationArg $sessionZero $sessionEndSec
    Write-Host ("        late joiner gets --duration $lateDurationArg (= {0}s session end minus its launch offset)" -f $sessionEndSec)
    $botC = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "BubbleBotC",
        "--log", $cLog, "--duration", $lateDurationArg, "--world", "open",
        "--bubble-selftest", "--goto-script", "0,-4") "bubbleC"
    $procs += $botC

    # FIX-1 / NET-1 §11.2 rule 1: the scheduled server-side event is gated on the SERVER'S OWN LOG
    # before any bot is allowed to exit. The script already did this to LAUNCH C (the pop gates
    # above); it must equally do it to RETAIN C. A reset that never lands fails here, naming the
    # server, instead of surfacing 30 s later as "C never saw the reset" and accusing replication.
    $resetGateSec = [int][math]::Ceiling($sessionEndSec) + 60
    if (-not (Wait-ForLogLine $serverOut "^\[bubbletest\] reset\s*$" $resetGateSec)) {
        Write-Fail ("the server never broadcast a reset within ${resetGateSec}s; see $serverOut. " +
            "Nothing downstream of this can discriminate a replication defect from a lever that " +
            "never fired.")
    }
    Write-Host ("        server broadcast the reset at {0} (bots still alive)" -f (Get-Date).ToString("HH:mm:ss.fff"))

    $bots = @(
        @{ Name = "BubbleBotA"; Proc = $botA; JsonLog = $aLog }
        @{ Name = "BubbleBotB"; Proc = $botB; JsonLog = $bLog }
        @{ Name = "BubbleBotC"; Proc = $botC; JsonLog = $cLog }
    )
    $deadline = $sessionZero.AddSeconds($sessionEndSec + 120)
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

Write-Host "[4/4] verifying the shared tally across three peers..." -ForegroundColor Cyan

# --- BT-8: the reset came from the lever, and the double pull produced ONE reset ---------------
# Read from the SERVER's log rather than from the bots', because the gate is server-side and the
# clients cannot see a refusal that never became a broadcast.
$serverLog = Join-Path $script:LogDir "bubble.server.out.log"
$leverFailures = New-Object System.Collections.Generic.List[string]
if (-not (Test-Path $serverLog)) {
    $leverFailures.Add("server log not found: $serverLog")
} else {
    $serverLines = @(Get-Content $serverLog)
    $pulled  = @($serverLines | Where-Object { $_ -match "\[bubbletest\] reset lever pulled byPeer=" })
    $refused = @($serverLines | Where-Object { $_ -match "\[bubbletest\] reset refused \(cooldown" })
    # Anchored, so it matches BubbleCounter.ServerReset's own bare line and neither of the two
    # lever lines above ("reset lever pulled ...", "reset refused ...").
    $resets  = @($serverLines | Where-Object { $_ -match "^\[bubbletest\] reset\s*$" })
    Write-Host ("        lever: accepted={0} refused={1} resets broadcast={2}" -f `
        $pulled.Count, $refused.Count, $resets.Count)
    if ($pulled.Count -ne 1) {
        $leverFailures.Add("the lever accepted $($pulled.Count) pull(s), expected exactly 1 - the reset did not come from the lever, or the cooldown let a second through")
    }
    if ($refused.Count -ne 1) {
        # The positive control. Without an observed refusal, "only one reset" could equally mean
        # the second pull never happened, which proves nothing about the cooldown.
        $leverFailures.Add("the lever refused $($refused.Count) pull(s), expected exactly 1 - the second press of the double pull was never observed, so the cooldown was not exercised")
    }
    if ($resets.Count -ne 1) {
        $leverFailures.Add("BubbleCounter broadcast $($resets.Count) reset(s), expected exactly 1")
    }

    # --- LEVER-1: the stray press, and the board that survived it ------------------------------
    # Talon's sentence, as an assertion on a real session. Two peers each pulled the lever once and
    # neither followed through; what must be true is that the board did not move.
    $armed = @($serverLines | Where-Object { $_ -match "\[bubbletest\] reset ARMED byPeer=" })
    $stray = @($serverLines | Where-Object { $_ -match "\[bubbletest\] lever fixture: two stray pulls, armed=(\d+) accepted=(\d+) tally (\d+) -> (\d+)" })
    Write-Host ("        lever: warnings armed={0}" -f $armed.Count)
    if ($armed.Count -ne 2) {
        # 2 = the two stray pulls, one per peer. The two presses a beat later are CONFIRMS of
        # those same warnings, not new ones -- a third ARMED line would mean a confirm was not
        # recognised as one, which is the lever being unusable rather than merely noisy.
        $leverFailures.Add("the lever raised $($armed.Count) warning(s), expected exactly 2 (one per peer, each then confirmed) - the two-stage press is not being exercised as designed")
    }
    if ($stray.Count -ne 1) {
        $leverFailures.Add("the fixture never printed its stray-pull line; the LEVER-1 case was not exercised at all")
    } else {
        $m = [regex]::Match($stray[0], "armed=(\d+) accepted=(\d+) tally (\d+) -> (\d+)")
        $strayArmed = [int]$m.Groups[1].Value
        $strayAccepted = [int]$m.Groups[2].Value
        $before = [int]$m.Groups[3].Value
        $after  = [int]$m.Groups[4].Value
        Write-Host ("        lever: stray pulls armed={0} accepted={1} tally {2} -> {3}" -f `
            $strayArmed, $strayAccepted, $before, $after)
        if ($strayArmed -ne 2) {
            # The positive control: "the board survived" proves nothing unless the presses landed.
            $leverFailures.Add("the two stray pulls raised $strayArmed warning(s), expected 2 - the presses were not observed, so the survival below is vacuous")
        }
        if ($strayAccepted -ne 0) {
            $leverFailures.Add("a stray pull RESET THE BOARD ($strayAccepted accepted) - this is exactly the defect LEVER-1 exists to close")
        }
        if ($before -ne $after) {
            $leverFailures.Add("the tally moved from $before to $after across two stray pulls that nobody confirmed")
        }
        if ($before -eq 0) {
            $leverFailures.Add("the stray pulls landed on an EMPTY board ($before), so 'the tally still stands' was never actually at stake")
        }
    }
}

function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

function Ids($Value) {
    if ($null -eq $Value) { return @() }
    return @($Value | ForEach-Object { [int]$_ } | Sort-Object)
}

function Join-Ids($Value) { return (Ids $Value) -join "," }

$peers = @(
    @{ Name = "A (walker)";  Samples = Get-Samples $aLog; Late = $false }
    @{ Name = "B (witness)"; Samples = Get-Samples $bLog; Late = $false }
    @{ Name = "C (late)";    Samples = Get-Samples $cLog; Late = $true  }
)

$failures = New-Object System.Collections.Generic.List[string]
foreach ($f in $leverFailures) { $failures.Add($f) }
$expectedPopped = (Ids $CorridorPops) -join ","
$forgeObserved = $false

# --- 8 (FIX-1): the late joiner's window provably SPANS the reset ------------------------------
# Assertion 6's positive control, and the one this suite was missing. "C never saw the reset" is
# two completely different findings depending on whether C was still sampling when the reset
# landed, and until FIX-1 nothing here could tell them apart: C's window ended 141 ms before the
# reset's effect was observable on any peer, so the late-joiner half of 6 had never once run.
#
# Wall is Unix milliseconds, logged by BotHarness precisely so three processes can be compared on
# one machine (see its Sample record). The mark is the EARLIEST sample, on either peer present
# throughout, whose tally has returned to 0 after reading 2 - i.e. the first moment the broadcast's
# effect was observable anywhere. C must still have been sampling strictly after it.
function Get-BubbleSamples($Peer) { return @($Peer.Samples | Where-Object { $null -ne $_.bubble }) }

$resetWallMs = $null
foreach ($p in @($peers | Where-Object { -not $_.Late })) {
    $sawTwo = $false
    foreach ($s in (Get-BubbleSamples $p)) {
        $c = [int]$s.bubble.count
        if ($c -eq $CorridorPops.Count) { $sawTwo = $true; continue }
        if ($sawTwo -and $c -eq 0) {
            $w = [long]$s.wall
            if ($null -eq $resetWallMs -or $w -lt $resetWallMs) { $resetWallMs = $w }
            break
        }
    }
}
$latePeer = @($peers | Where-Object { $_.Late })[0]
$lateSamples = Get-BubbleSamples $latePeer
$lateLastWallMs = if ($lateSamples.Count -gt 0) { [long]$lateSamples[-1].wall } else { $null }
if ($null -eq $resetWallMs) {
    $failures.Add("no peer present throughout ever saw the tally return to 0, so the reset's wall-clock mark is unknown and the late joiner's window cannot be discriminated at all")
} elseif ($null -eq $lateLastWallMs) {
    $failures.Add("the late joiner logged no bubble samples at all")
} else {
    $resetAt = [DateTimeOffset]::FromUnixTimeMilliseconds($resetWallMs).UtcDateTime
    $lateEnd = [DateTimeOffset]::FromUnixTimeMilliseconds($lateLastWallMs).UtcDateTime
    $marginSec = ($lateLastWallMs - $resetWallMs) / 1000.0
    Write-Host ("        window: reset first observed {0}Z, late joiner's last sample {1}Z, margin {2:F3}s" -f `
        $resetAt.ToString("HH:mm:ss.fff"), $lateEnd.ToString("HH:mm:ss.fff"), $marginSec)
    if ($lateLastWallMs -le $resetWallMs) {
        # Names the HARNESS, deliberately. This failure is never evidence about replication.
        $msg = ("the late joiner's sampling window CLOSED {0:F3}s BEFORE the reset was " -f (-$marginSec)) +
            "observable on any peer (last sample $($lateEnd.ToString('HH:mm:ss.fff'))Z, reset " +
            "$($resetAt.ToString('HH:mm:ss.fff'))Z) - this is a HARNESS defect, not a replication " +
            "one, and every late-joiner reset assertion below it is VACUOUS. Bot lifetimes must " +
            "be derived from the server's announced reset schedule, never typed."
        $failures.Add($msg)
    }
}

foreach ($p in $peers) {
    $name = $p.Name
    $samples = $p.Samples

    # --- 0: the instrumentation is present at all -------------------------------------------
    $withBubble = @($samples | Where-Object { $null -ne $_.bubble })
    if ($withBubble.Count -eq 0) {
        $failures.Add("$name never reported a bubble sample - BubbleCounter.Instance was null all run")
        continue
    }

    # --- 1: the same id space on every peer --------------------------------------------------
    foreach ($s in $withBubble) {
        if ([int]$s.bubble.adopted -ne $ExpectedBubbles) {
            $failures.Add("$name adopted $([int]$s.bubble.adopted) bubbles, expected $ExpectedBubbles")
            break
        }
    }

    # --- 6: the bob never drifts the collider ------------------------------------------------
    $worst = ($withBubble | ForEach-Object { [double]$_.bubble.maxOffset } | Measure-Object -Maximum).Maximum
    if ($worst -gt $MaxOffsetM + 0.0005) {
        $failures.Add(("$name saw a bubble {0:F4} m from its authored position (bound {1} m)" -f $worst, $MaxOffsetM))
    }
    Write-Host ("        {0,-12} samples={1,-4} worst bob offset={2:F4} m" -f $name, $withBubble.Count, $worst)

    # --- 2/3: convergence, and the late joiner's first sample --------------------------------
    $firstAtTwo = $withBubble | Where-Object { [int]$_.bubble.count -ge $CorridorPops.Count } | Select-Object -First 1
    if ($null -eq $firstAtTwo) {
        $failures.Add("$name never saw the tally reach $($CorridorPops.Count)")
        continue
    }
    if ((Join-Ids $firstAtTwo.bubble.popped) -ne $expectedPopped) {
        $failures.Add("$name reached the tally with popped={$(Join-Ids $firstAtTwo.bubble.popped)}, expected {$expectedPopped}")
    }
    if (-not $firstAtTwo.bubble.synced) {
        $failures.Add("$name reported a tally it had not received from the server (synced=false)")
    }

    if ($p.Late) {
        $first = $withBubble | Select-Object -First 1
        if (-not $first.bubble.synced) {
            $failures.Add("late joiner's FIRST sample is not synced - it rendered before it knew the state")
        }
        if ([int]$first.bubble.count -ne $CorridorPops.Count) {
            $failures.Add("late joiner's FIRST sample reads count=$([int]$first.bubble.count), expected $($CorridorPops.Count)")
        }
        if ((Join-Ids $first.bubble.popped) -ne $expectedPopped) {
            $failures.Add("late joiner's FIRST sample has popped={$(Join-Ids $first.bubble.popped)}, expected {$expectedPopped}")
        }
        $hiddenFirst = Ids $first.bubble.hidden
        foreach ($id in $CorridorPops) {
            if ($hiddenFirst -notcontains $id) {
                $failures.Add("late joiner's FIRST sample still renders bubble $id")
            }
        }
    }

    # --- the whole timeline, in order -------------------------------------------------------
    $seenReset = $false
    $sawTwo = $false
    foreach ($s in $withBubble) {
        $count = [int]$s.bubble.count
        $popped = Ids $s.bubble.popped
        $hidden = Ids $s.bubble.hidden

        # 4: a client cannot move the shared tally, ever.
        if ($popped -contains $ForgeSubject) {
            $failures.Add("$name has bubble $ForgeSubject in its POPPED set - a client-side action moved the tally")
        }
        if ($count -gt $CorridorPops.Count) {
            $failures.Add("$name read count=$count, which is more than the two corridor bubbles")
        }
        if ($hidden -contains $ForgeSubject) { $forgeObserved = $true }

        if ($count -eq $CorridorPops.Count) {
            $sawTwo = $true
            foreach ($id in $CorridorPops) {
                if ($hidden -notcontains $id) {
                    $failures.Add("$name reads the full tally but still renders bubble $id")
                }
            }
        }
        # 5: after the reset, everything is back.
        if ($sawTwo -and $count -eq 0) {
            $seenReset = $true
            if ($popped.Count -ne 0) {
                $failures.Add("$name reset to 0 but still lists popped={$($popped -join ',')}")
            }
            if ($hidden.Count -ne 0) {
                $failures.Add("$name reset to 0 but is still hiding {$($hidden -join ',')}")
            }
        }
    }
    if (-not $seenReset) {
        $failures.Add("$name never saw the reset return the tally to 0 and every bubble to visible")
    }
}

# The forge's positive control: an absence proof about client pops is worth nothing unless a
# client actually tried something. BubbleSelfTest hides bubble 0 locally on every client; if that
# never shows up in a hidden set, assertion 4 above was vacuous.
if (-not $forgeObserved) {
    $failures.Add("no peer ever hid bubble $ForgeSubject locally - the client-forge control never ran, so 'no client path pops' was not actually exercised")
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "BUBBLE SYNC TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "BUBBLE SYNC TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: one server-adjudicated tally, identical on three peers including a late joiner; " -ForegroundColor Green
Write-Host "      no client-side action moved it; a stray pull left it standing, and a confirmed one put every bubble back." -ForegroundColor Green
Write-Host ""
Write-Host "BUBBLE SYNC TEST OVERALL: PASS" -ForegroundColor Green
exit 0
