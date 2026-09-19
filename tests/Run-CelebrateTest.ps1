<#
.SYNOPSIS
    CELEBRATE-1: the last bubble goes, every peer is told, exactly once — and no automated run
    celebrates anything.

.DESCRIPTION
    Talon, wrapping the 2026-09-04 playtest: "when the player collects all the bubbles, when they
    get 100 out of 100, I would like a small sound to play like a triumph sound ... and maybe do
    something also special to let them know they're special."

    Completion is a GROUP event: BubbleCounter holds one shared, server-adjudicated tally, so the
    triumph is not the last popper's — it is everyone's. That makes three things worth measuring,
    and this suite runs TWO sessions to measure them, because two of the three are contradictory
    demands on the same gate.

    ---------------------------------------------------------------------------------------------
    RUN 1 - THE SHIPPED SHAPE. Three scripted bots, no overrides.
    ---------------------------------------------------------------------------------------------
      1. Announced once   - the server logs exactly ONE completion line per completion, and TWO
                            across the whole run (a completion, a lever reset, a recompletion).
                            The fixture keeps ticking after each one; a latch that re-armed per
                            frame would show up here as hundreds.
      2. Every peer told  - A and B each RECEIVE both completion broadcasts, from two separate
                            processes' logs.
      3. Nothing sounded  - and not one of the three bots CELEBRATES. This is the bot gate, and
                            the received/celebrated PAIR is what makes it an observation: a peer
                            with received > 0 and celebrated == 0 proves the gate culled it, where
                            received == 0 alone would equally be a broken wire.
      4. Late join quiet  - bot C joins AFTER the first completion has landed. It must arrive at a
                            finished level (its first sample reads a full tally) and must NOT be
                            greeted by a triumph it did not earn: received stays 0 until the
                            RECOMPLETION, which it is present for.
      5. Recomplete       - after the lever reset, popping everything again announces again. A
                            completion is a repeatable event, not a one-per-session one.
      6. Protocol         - no peer logged a handshake/version complaint, i.e. nothing about this
                            feature needed ProtocolVersion to move.

    ---------------------------------------------------------------------------------------------
    RUN 2 - THE POSITIVE CONTROL. The same session with --celebrate-force on all three bots.
    ---------------------------------------------------------------------------------------------
      7. The probe sees   - all three bots now CELEBRATE, celebrated == received, from three
                            separate processes' logs. Without this, run 1's assertion 3 is
                            indistinguishable from a broadcast that never arrives and a counter
                            that never moves - i.e. from nothing at all.
      8. Input is free    - the walker keeps MOVING through the celebration window, and its honk
                            presses keep being granted by the server and answered back to it across
                            the same window. The packet's standing constraint ("never take control
                            away from the player") made checkable on a live session rather than
                            argued from the source.
      9. No blocker       - and the source of the celebration path touches none of the input,
                            pause, focus or camera APIs that could block a player. Carries its own
                            positive control: the same patterns are shown to match a file that
                            really does use them.

    NUMBERS ARE DERIVED, NOT TYPED (the FIX-1 lesson, inherited from Run-BubbleSyncTest). Both
    completion marks come off the server's own "[bubbletest] celebrate schedule:" line, which the
    fixture computes from --bubble-pop-all-at, --bubble-reset-at and BubbleResetConfirm's two
    constants. Move any of those and the windows move with them.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [int]$Port = 7893,
    # The first completion. Late enough that all three peers of run 1 are up and synced; early
    # enough to leave the reset and the recompletion inside a ~45 s session.
    [double]$PopAllAtSec = 6,
    # The lever reset. Must be after the first completion (asserted below against the server's own
    # announced schedule, never assumed).
    [double]$ResetAtSec = 20,
    # How long after the RECOMPLETION every bot keeps sampling. A margin, not a mark: at the ~4.9 Hz
    # these bots sample, 6 s is ~29 post-event samples where the assertions need a handful.
    [double]$PostSettleSec = 6,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# The fixture's own constants (scripts/game/bubble/BubbleSelfTest.cs). Restated rather than
# derived, so a change on either side shows up as a failing assertion instead of as two files
# quietly agreeing about the wrong thing.
$ExpectedBubbles = 6
# BubbleCounter.CompletionLogPrefix, and the anchored reset line ServerReset prints.
$CompletionPattern = "^\[bubbletest\] all bubbles popped:"
$ResetPattern = "^\[bubbletest\] reset\s*$"
# BubbleCelebration.TotalSeconds, in milliseconds — the window assertion 8 looks inside.
$CelebrationWindowMs = 1600
# The walker's route and the motor's jog speed (MotorTuning.MoveSpeed, 3.8 m/s): together these say
# how long bot A is in motion, which is the window the first completion has to land inside for
# assertion 8 to be about anything. Restated from the tuning rather than derived, so a retune that
# invalidates the schedule shows up as a loud harness failure instead of a silently vacuous green.
$WalkMetres = 30.0
$WalkSpeedMps = 3.8

$inv = [System.Globalization.CultureInfo]::InvariantCulture
function Inv([double]$v) { return $v.ToString("F2", $inv) }

function Get-BotDurationArg([datetime]$Zero, [double]$EndSec) {
    $remaining = $EndSec - ((Get-Date) - $Zero).TotalSeconds
    if ($remaining -lt 5) { $remaining = 5 }
    return $remaining.ToString("F2", $inv)
}

function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $null -ne $_.bubble })
}

$failures = New-Object System.Collections.Generic.List[string]

# =================================================================================================
# One session, parameterised by whether the bots carry --celebrate-force. Returns the three bots'
# sample sets plus the server's log lines, so both runs are read by the same code and any
# difference between them is the FLAG rather than the reader.
# =================================================================================================
function Invoke-CelebrateSession {
    param(
        [string]$Tag,
        [int]$SessionPort,
        [switch]$Force,
        [string[]]$HonkMarks = @()
    )

    $procs = @()
    $forceArgs = if ($Force) { @("--celebrate-force") } else { @() }
    $honkArgs = @()
    foreach ($m in $HonkMarks) { $honkArgs += @("--honk-at", $m) }

    $aLog = Join-Path $script:LogDir "celebrate$Tag-A.jsonl"
    $bLog = Join-Path $script:LogDir "celebrate$Tag-B.jsonl"
    $cLog = Join-Path $script:LogDir "celebrate$Tag-C.jsonl"
    $serverOut = Join-Path $script:LogDir "celebrate$Tag.server.out.log"

    try {
        Write-Host "  [$Tag] launching dedicated server on udp/$SessionPort (force=$([bool]$Force))..." -ForegroundColor Cyan
        $server = Start-Godot @("--server", "--port", $SessionPort, "--world", "open",
            "--bubble-selftest", "--bubble-pop-all-at", (Inv $PopAllAtSec),
            "--bubble-reset-at", (Inv $ResetAtSec)) "celebrate$Tag.server"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
            Write-Fail "[$Tag] server never reported listening; see $serverOut"
        }
        if (-not (Wait-ForLogLine $serverOut "\[bubble\] adopted $ExpectedBubbles bubble" 20)) {
            Write-Fail "[$Tag] server never adopted $ExpectedBubbles bubbles; see $serverOut"
        }
        if (-not (Wait-ForLogLine $serverOut "\[bubbletest\] celebrate schedule:" 20)) {
            Write-Fail ("[$Tag] server never announced its celebrate schedule; see $serverOut. " +
                "Every window below is derived from that line - without it this suite would be " +
                "timing bots against typed constants, which is the FIX-1 defect.")
        }
        $sessionZero = Get-Date
        $schedHit = Select-String -Path $serverOut -Pattern "\[bubbletest\] celebrate schedule:" | Select-Object -First 1
        $schedLine = if ($null -ne $schedHit) { $schedHit.Line } else { "" }
        $m = [regex]::Match($schedLine, "popAllAt=([0-9.]+)s recompleteAt=([0-9.]+)s")
        if (-not $m.Success) { Write-Fail "[$Tag] could not parse the celebrate schedule line: $schedLine" }
        $announcedPopAll = [double]::Parse($m.Groups[1].Value, $inv)
        $recompleteAt = [double]::Parse($m.Groups[2].Value, $inv)
        if ([math]::Abs($announcedPopAll - $PopAllAtSec) -gt 0.01) {
            Write-Fail ("[$Tag] the server scheduled its first completion at ${announcedPopAll}s but " +
                "this script asked for ${PopAllAtSec}s - --bubble-pop-all-at is not reaching the fixture")
        }
        if ($recompleteAt -le $announcedPopAll) {
            Write-Fail ("[$Tag] the recompletion mark (${recompleteAt}s) is not after the first " +
                "completion (${announcedPopAll}s); the reset schedule and the completion schedule " +
                "have been given colliding marks and nothing below would mean what it says")
        }
        $sessionEndSec = $recompleteAt + $PostSettleSec
        Write-Host ("  [$Tag] schedule: complete {0}s, recomplete {1}s, bots home {2}s" -f `
            $announcedPopAll, $recompleteAt, $sessionEndSec)

        Write-Host "  [$Tag] launching the walker and the witness..." -ForegroundColor Cyan
        $aOut = Join-Path $script:LogDir "celebrate$Tag-A.out.log"
        $botA = Start-Godot (@("--bot", "--address", "127.0.0.1:$SessionPort", "--name", "CelebA",
            "--log", $aLog, "--duration", (Get-BotDurationArg $sessionZero $sessionEndSec),
            "--world", "open", "--bubble-selftest", "--goto-script", "4,30") + $forceArgs + $honkArgs) "celebrate$Tag-A"
        $procs += $botA
        if (-not (Wait-ForLogLine $aOut "\[bot\] CelebA running as peer" 40)) {
            Write-Fail "[$Tag] bot A never connected; see $aOut"
        }
        # THE WALKER'S WINDOW, measured rather than assumed (the FIX-1 discipline). A walks the
        # clear x = 4 corridor from the spawn ring to z = 30 — about 30 m at MotorTuning.MoveSpeed
        # (3.8 m/s), so it is in motion for roughly WalkSec from the moment it spawns. Assertion 8
        # needs the first completion to land INSIDE that motion; if it does not, the assertion is
        # vacuous, and a vacuous assertion that reports green is the defect FIX-1 exists to close.
        # Reported here every run so the margin is visible, and failed loudly NAMING THE HARNESS.
        $aConnectedAtSec = ((Get-Date) - $sessionZero).TotalSeconds
        $walkSec = $WalkMetres / $WalkSpeedMps
        $motionFrom = $aConnectedAtSec + 1.0
        $motionTo = $aConnectedAtSec + $walkSec - 1.0
        Write-Host ("  [$Tag] walker connected at {0:F2}s; in motion ~{1:F2}s..{2:F2}s; completion at {3:F2}s" -f `
            $aConnectedAtSec, $motionFrom, $motionTo, $announcedPopAll)
        if ($Force -and ($announcedPopAll -le $motionFrom -or $announcedPopAll -ge $motionTo)) {
            $msg = ("[$Tag] HARNESS: the first completion ({0:F2}s) does not land inside the walker's motion " -f $announcedPopAll) +
                ("window ({0:F2}s..{1:F2}s), so 'the player kept moving through the celebration' cannot be " -f $motionFrom, $motionTo) +
                "measured and assertion 8 would pass or fail for the wrong reason. Move --bubble-pop-all-at, " +
                "not the assertion."
            Write-Fail $msg
        }
        $botB = Start-Godot (@("--bot", "--address", "127.0.0.1:$SessionPort", "--name", "CelebB",
            "--log", $bLog, "--duration", (Get-BotDurationArg $sessionZero $sessionEndSec),
            "--world", "open", "--bubble-selftest", "--goto-script", "-3,3") + $forceArgs + $honkArgs) "celebrate$Tag-B"
        $procs += $botB

        Write-Host "  [$Tag] waiting for the first completion, then joining a third peer late..." -ForegroundColor Cyan
        if (-not (Wait-ForLogLine $serverOut $CompletionPattern 90)) {
            Write-Fail "[$Tag] the server never announced a completion; see $serverOut"
        }
        $lateArg = Get-BotDurationArg $sessionZero $sessionEndSec
        $botC = Start-Godot (@("--bot", "--address", "127.0.0.1:$SessionPort", "--name", "CelebC",
            "--log", $cLog, "--duration", $lateArg,
            "--world", "open", "--bubble-selftest", "--goto-script", "0,-4") + $forceArgs) "celebrate$Tag-C"
        $procs += $botC

        # Gate on the SERVER'S OWN LOG before any bot is allowed to exit (NET-1 §11.2 rule 1): a
        # recompletion that never lands fails here, naming the server, instead of surfacing later
        # as "nobody saw the second completion" and accusing replication.
        $gateSec = [int][math]::Ceiling($sessionEndSec) + 60
        if (-not (Wait-ForLogLine $serverOut $ResetPattern $gateSec)) {
            Write-Fail "[$Tag] the server never broadcast a reset within ${gateSec}s; see $serverOut"
        }
        $bots = @(
            @{ Name = "A (walker)"; Proc = $botA; Log = $aLog; Late = $false }
            @{ Name = "B (witness)"; Proc = $botB; Log = $bLog; Late = $false }
            @{ Name = "C (late)"; Proc = $botC; Log = $cLog; Late = $true }
        )
        $deadline = $sessionZero.AddSeconds($sessionEndSec + 120)
        foreach ($b in $bots) {
            $remainingMs = [int]((($deadline - (Get-Date)).TotalSeconds) * 1000)
            if ($remainingMs -lt 1000) { $remainingMs = 1000 }
            if (-not $b.Proc.WaitForExit($remainingMs)) { Write-Fail "[$Tag] $($b.Name) did not exit within timeout" }
        }
        foreach ($b in $bots) {
            if ($b.Proc.ExitCode -ne 0) { Write-Fail "[$Tag] $($b.Name) exited with code $($b.Proc.ExitCode); see $($b.Log)" }
        }
    } finally {
        Stop-Procs $procs
    }

    foreach ($b in $bots) { $b.Samples = Get-Samples $b.Log }
    return @{
        Tag = $Tag
        Bots = $bots
        ServerLines = @(Get-Content $serverOut)
        ServerOut = $serverOut
    }
}

Write-Host "=== celebrate: the last bubble, every peer, exactly once, and nothing on a bot ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

# =================================================================================================
# RUN 1 - the shipped shape.
# =================================================================================================
Write-Host ""
Write-Host "[1/3] RUN 1: the shipped shape (no overrides)" -ForegroundColor Cyan
$run1 = Invoke-CelebrateSession -Tag "1" -SessionPort $Port

$completions = @($run1.ServerLines | Where-Object { $_ -match $CompletionPattern })
$resets = @($run1.ServerLines | Where-Object { $_ -match $ResetPattern })
Write-Host ("        server: completions announced={0} resets broadcast={1}" -f $completions.Count, $resets.Count)

# --- 1: announced once per completion, and the latch re-arms on a reset -------------------------
if ($completions.Count -ne 2) {
    $failures.Add(("the server announced $($completions.Count) completion(s), expected exactly 2 " +
        "(one at the first pop-all, one after the reset). More than 2 means the latch re-arms per " +
        "tick - the exact idempotency defect MECHANICS-BIBLE section 4 is about. Fewer means the " +
        "reset did not clear it, and a level can only be completed once per session."))
}
if ($resets.Count -ne 1) {
    $failures.Add("the server broadcast $($resets.Count) reset(s), expected exactly 1 - the recompletion half of assertion 1 is not about what it says it is")
}

# --- 6: nothing needed a protocol bump ----------------------------------------------------------
$versionComplaints = @($run1.ServerLines | Where-Object { $_ -match "protocol|version mismatch|handshake refused" -and $_ -match "reject|mismatch|refus" })
if ($versionComplaints.Count -gt 0) {
    $failures.Add("the server logged $($versionComplaints.Count) protocol/handshake complaint(s): $($versionComplaints[0])")
}

foreach ($b in $run1.Bots) {
    $samples = $b.Samples
    $maxReceived = ($samples | ForEach-Object { [long]$_.bubble.celebrateReceived } | Measure-Object -Maximum).Maximum
    $maxPlayed = ($samples | ForEach-Object { [long]$_.bubble.celebratePlayed } | Measure-Object -Maximum).Maximum
    $expectedReceived = if ($b.Late) { 1 } else { 2 }
    Write-Host ("        {0,-12} samples={1,-4} celebrateReceived={2} celebratePlayed={3} (expect received={4} played=0)" -f `
        $b.Name, $samples.Count, $maxReceived, $maxPlayed, $expectedReceived)

    # --- 2/5: every peer present was told, both times ------------------------------------------
    if ($maxReceived -ne $expectedReceived) {
        $failures.Add("run 1: $($b.Name) received $maxReceived completion broadcast(s), expected $expectedReceived")
    }

    # --- 3: THE BOT GATE. Nothing celebrated, and received > 0 is what makes that mean something.
    if ($maxPlayed -ne 0) {
        $failures.Add(("run 1: $($b.Name) CELEBRATED $maxPlayed time(s) on a scripted run. This is " +
            "the defect the packet exists to prevent: a suite that pops every bubble must sound " +
            "nothing and celebrate nothing."))
    }
    if ($maxReceived -eq 0) {
        $failures.Add("run 1: $($b.Name) never received a completion broadcast at all, so 'it celebrated nothing' is a claim about the wire rather than about the gate")
    }

    # --- monotone: the counter never goes backwards, and never runs away -------------------------
    $prev = 0
    foreach ($s in $samples) {
        $r = [long]$s.bubble.celebrateReceived
        if ($r -lt $prev) { $failures.Add("run 1: $($b.Name) saw celebrateReceived go backwards ($prev -> $r)"); break }
        $prev = $r
    }

    # --- 4: the late joiner arrived at a finished level and was NOT greeted ----------------------
    if ($b.Late) {
        $first = $samples[0]
        if ([int]$first.bubble.count -ne $ExpectedBubbles) {
            $failures.Add(("the late joiner's FIRST sample reads count=$([int]$first.bubble.count), " +
                "expected $ExpectedBubbles - it did not in fact arrive at a finished level, so " +
                "'it was not greeted by a triumph' was never at stake"))
        }
        if (-not $first.bubble.synced) {
            $failures.Add("the late joiner's FIRST sample is not synced; it had not yet heard from the server at all")
        }
        # Every sample taken while the board was still full (i.e. before the reset) must carry a
        # zero receive count: those are the frames in which a triumph would have been a greeting.
        foreach ($s in $samples) {
            if ([int]$s.bubble.count -lt $ExpectedBubbles) { break }
            if ([long]$s.bubble.celebrateReceived -ne 0) {
                $failures.Add(("the late joiner was GREETED by a completion it did not earn: it read " +
                    "celebrateReceived=$([long]$s.bubble.celebrateReceived) while still on the finished " +
                    "board it joined. A completion is an event, not a state a late sync may replay."))
                break
            }
        }
    }
}

# =================================================================================================
# RUN 2 - the positive control, and the input-is-free proof.
# =================================================================================================
Write-Host ""
Write-Host "[2/3] RUN 2: --celebrate-force (the probe's positive control)" -ForegroundColor Cyan
# Honk marks straddling the first completion on the WITNESS, spaced past HonkConfig.CooldownSec
# (0.6 s) so each is a genuine grant rather than a throttled press. The bot's own elapsed clock and
# the server's differ by its launch offset, so the marks cover a window rather than a point.
$honkMarks = @()
for ($t = 1.0; $t -le 24.0; $t += 0.8) { $honkMarks += (Inv $t) }
$run2 = Invoke-CelebrateSession -Tag "2" -SessionPort ($Port + 1) -Force -HonkMarks $honkMarks

$completions2 = @($run2.ServerLines | Where-Object { $_ -match $CompletionPattern })
if ($completions2.Count -lt 1) {
    $failures.Add("run 2: the server never announced a completion, so the positive control had nothing to see")
}

foreach ($b in $run2.Bots) {
    $samples = $b.Samples
    $maxReceived = ($samples | ForEach-Object { [long]$_.bubble.celebrateReceived } | Measure-Object -Maximum).Maximum
    $maxPlayed = ($samples | ForEach-Object { [long]$_.bubble.celebratePlayed } | Measure-Object -Maximum).Maximum
    Write-Host ("        {0,-12} samples={1,-4} celebrateReceived={2} celebratePlayed={3} (expect played == received > 0)" -f `
        $b.Name, $samples.Count, $maxReceived, $maxPlayed)

    # --- 7: the probe can see a celebration ------------------------------------------------------
    if ($maxPlayed -lt 1) {
        $failures.Add(("run 2: $($b.Name) celebrated $maxPlayed time(s) even with --celebrate-force. " +
            "The probe cannot see a celebration at all, which makes run 1's absence proof VACUOUS - " +
            "it would report the same numbers for a broadcast that never fires."))
    }
    if ($maxPlayed -ne $maxReceived) {
        $failures.Add("run 2: $($b.Name) received $maxReceived and celebrated $maxPlayed - with the gate forced open the two must be equal")
    }
}

# --- 8: the player kept playing through the celebration ------------------------------------------
# The window is BubbleCelebration.TotalSeconds after the first sample in which the walker's own
# celebration fired. Wall is Unix milliseconds, logged so three processes can be compared on one
# machine.
$walker = @($run2.Bots | Where-Object { -not $_.Late })[0]
$fired = $null
foreach ($s in $walker.Samples) {
    if ([long]$s.bubble.celebratePlayed -ge 1) { $fired = $s; break }
}
if ($null -eq $fired) {
    $failures.Add("run 2: the walker never celebrated, so the input-is-free window has no start and assertion 8 is vacuous")
} else {
    $t0 = [long]$fired.wall
    $window = @($walker.Samples | Where-Object { [long]$_.wall -gt $t0 -and [long]$_.wall -le $t0 + $CelebrationWindowMs })
    if ($window.Count -lt 2) {
        $failures.Add("run 2: only $($window.Count) sample(s) landed inside the ${CelebrationWindowMs}ms celebration window; the walker's movement through it cannot be measured")
    } else {
        function Self($Sample) { return @($Sample.peers | Where-Object { [long]$_.id -eq [long]$Sample.self })[0] }
        $p0 = Self $fired
        $moved = 0.0
        foreach ($s in $window) {
            $p = Self $s
            if ($null -eq $p0 -or $null -eq $p) { continue }
            $d = [math]::Sqrt([math]::Pow([double]$p.x - [double]$p0.x, 2) + [math]::Pow([double]$p.z - [double]$p0.z, 2))
            if ($d -gt $moved) { $moved = $d }
        }
        Write-Host ("        input: walker moved {0:F3} m inside the {1} ms celebration window ({2} samples)" -f `
            $moved, $CelebrationWindowMs, $window.Count)
        if ($moved -lt 0.10) {
            $failures.Add(("run 2: the walker moved only {0:F3} m during the celebration - the beat is " -f $moved) +
                "taking control away from the player, which is the packet's standing constraint broken")
        }
        # THE HONK VERB, all the way round the loop, DURING the celebration. The walker presses H
        # on a schedule of its own; a press that is served travels to the server, passes the
        # authoritative cooldown, is broadcast back and lands in this peer's own received counter.
        # So a rise in that counter inside the window is not "a sound played" — it is a player
        # input accepted, adjudicated and answered while the celebration was on screen.
        # RECEIVED rather than HEARD deliberately: heard is culled by the earshot rule
        # (HonkGate.InEarshot), which is a fact about distance and would make this assertion depend
        # on where two bots happened to be standing.
        function ReceivedTotal($Sample) {
            $sum = 0
            if ($null -ne $Sample.honkReceived) {
                foreach ($p in $Sample.honkReceived.PSObject.Properties) { $sum += [long]$p.Value }
            }
            return $sum
        }
        $honkStart = ReceivedTotal $fired
        $honkEnd = ReceivedTotal $window[-1]
        Write-Host ("        input: walker had {0} honk(s) answered at the celebration and {1} by the end of its window" -f $honkStart, $honkEnd)
        if ($honkEnd -le $honkStart) {
            $failures.Add(("run 2: the walker had no NEW honk answered during the celebration window " +
                "($honkStart -> $honkEnd). Either the honk verb stopped being served while the " +
                "celebration ran - the packet's standing constraint broken - or the press schedule " +
                "missed the window, which makes this assertion vacuous either way."))
        }
    }
}

# =================================================================================================
# 9 - the ABSENCE check: the celebration path touches no input, pause, focus or camera API.
# =================================================================================================
Write-Host ""
Write-Host "[3/3] source: the celebration blocks nothing" -ForegroundColor Cyan
$blockers = @("SceneTree.*Paused", "Input\.MouseMode", "SetProcessInput", "SetProcessUnhandledInput",
    "AcceptEvent", "GrabFocus", "MouseFilter", "GetViewport\(\)\.SetInputAsHandled")
$celebrationSources = @(
    (Join-Path $script:Root "scripts/game/bubble/BubbleCelebration.cs")
)
foreach ($src in $celebrationSources) {
    if (-not (Test-Path $src)) { $failures.Add("celebration source not found: $src"); continue }
    $text = Get-Content $src -Raw
    foreach ($pattern in $blockers) {
        if ($text -match $pattern) {
            $failures.Add("the celebration source $([System.IO.Path]::GetFileName($src)) uses '$pattern' - it can block a player's input")
        }
    }
}
# The positive control. An absence proof about a pattern set is worth nothing unless the patterns
# can match anything: Gameplay.cs really does normalise Input.MouseMode on teardown.
$controlFile = Join-Path $script:Root "scripts/game/Gameplay.cs"
$controlText = if (Test-Path $controlFile) { Get-Content $controlFile -Raw } else { "" }
$controlHits = @($blockers | Where-Object { $controlText -match $_ })
Write-Host ("        blockers: 0 in the celebration path; the same patterns match {0} time(s) in Gameplay.cs (control)" -f $controlHits.Count)
if ($controlHits.Count -eq 0) {
    $failures.Add(("the input-blocker patterns match NOTHING in Gameplay.cs either, so the absence " +
        "check above cannot detect a blocker and proves nothing"))
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "CELEBRATE TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "CELEBRATE TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: one completion announcement per completion; every peer present was told, twice;" -ForegroundColor Green
Write-Host "      a late joiner was not greeted; no scripted peer celebrated, and the forced control" -ForegroundColor Green
Write-Host "      proves the probe could have seen it; the player kept moving and honking throughout." -ForegroundColor Green
Write-Host ""
Write-Host "CELEBRATE TEST OVERALL: PASS" -ForegroundColor Green
exit 0
