<#
.SYNOPSIS
    Anti-cheat containment: a client that lies about its movement on the wire cannot
    exceed legitimate movement in the server's authoritative view.

.DESCRIPTION
    Three scenarios, each its own dedicated server + bot(s), all driven by the same
    --cheat-move wire-lying bot brain (a doctored movement direction inflated 25x, a bogus
    SpeedFactor claim, and periodic NaN bursts — see SendInputs's cheat hook).

    [1] Direction/speed containment (the "open" world, no props): a cheat bot walks the
        normal deterministic pattern and predicts honestly, but every SENT packet lies.
        Because the server simulates movement itself from sanitized inputs, the doctored
        packets must change nothing: the observer's replicated view of the cheater — which
        is exactly the server's truth — must stay inside legitimate speed and distance
        bounds for the entire run.

    [2] Carry-encumbrance containment (finding 4.1, RISK-AUDIT-2026-07-12.md), ONE run instead
        of two (issue #6 fix — see below for why this replaced the prior two-run design):
          CheatCarrier walks to prop 1 (propsync world's Crate), grabs it (earliest local
          clock >= 1.0s), then holds+patrols a lane for the rest of the run (genuinely
          encumbered — a real measurement, not a no-op; never auto-drops, so there is no
          held->unheld transition to get wrong). The SAME server/tick/log also carries
          Observer — already present in every run of this file, unmodified, doing its
          ordinary default straight-line walk (DeterministicWalkIntentSource, no --carry-*
          flags) — which never holds anything and so IS the unheld control, moving on the
          same simulated clock, same machine-load window, same process, as CheatCarrier's
          held phase.
        Before the fix, ServerTick trusted the wire-claimed SpeedFactor outright (clamped only
        into AvatarMotor's generic [0.55,1] sanity range, which the claim always saturates to
        1.0 regardless of who's actually holding what), so CheatCarrier held would cruise at
        statistically the SAME speed as Observer unheld. After the fix, ServerTick derives
        CheatCarrier's speed factor from its own <see cref="Props.FindHeldBy"/> state — held
        must cruise measurably SLOWER than Observer's unheld baseline.

        Why one run instead of two (issue #6): the prior design ran CheatCarrier (held) and
        CheatControl (unheld) as two SEPARATE dedicated-server processes and compared their
        means. The encumbrance signal is only ~0.14-0.19 m/s on a ~3.3-3.6 m/s walk (~4-5%),
        and run-to-run variance from machine load turned out to be large enough to swamp and
        even INVERT it under a 23-suite marathon (recorded: +0.138 m/s isolated, -0.061 m/s
        loaded). Comparing two peers inside ONE run instead of two independently-launched runs
        removes that entire noise source: both readings ride the exact same tick jitter,
        scheduler contention, and GC pauses, so only the encumbrance itself can move the delta.

        An earlier version of this fix tried a single bot that grabbed, held, auto-dropped,
        then kept patrolling unheld for the second half of the same run. That auto-drop-mid-
        patrol transition turned out to hard-freeze the bot's replicated movement outright
        (reproduced with and without --cheat-move; a genuine pre-existing bug, never exercised
        by any test before now since every prior --carry-patrol run used holdSec=-1/"never
        drop"). Fixing that bug is a game-code change outside this dispatch's scope (test-
        harness only), so this version never asks any bot to drop while patrolling — Observer's
        already-running, already-proven walk supplies the unheld side instead, and the held
        side simply never releases.

        Event-anchored, not clock-windowed (issue #17): the held window opens at the OBSERVED
        holder-field transition (grab lands) and Observer's unheld window is the OBSERVED span
        of its own motion (first and last sample it visibly moved) — never an assumed wall-
        clock offset, the same anchoring philosophy Run-ThrowTest.ps1 uses (Refs #4). #17's
        flake was a near-zero delta from sampling that could start before the grab necessarily
        landed; a missing anchor is a hard failure ("the grab never landed"/"Observer never
        moved"), never a silent skip.

    Asserts from scenario 1's observer log:
      1. The cheater's horizontal speed, averaged over any rolling $SpeedWindowSec-second
         window (not a single consecutive-sample delta — see issue #199's fix note above the
         computation), never exceeds the legitimate maximum (walk speed + interpolation slack).
      2. The cheater's total displacement stays within what an honest walk covers.
      3. The cheater is alive and moving (containment, not a kick/freeze) and both
         bots exit cleanly.

    Asserts from scenario 2's observer log:
      4. CheatCarrier is actually observed holding prop 1 (a real measurement, not a no-op —
         if the grab silently failed there would be nothing encumbering it, and this catches
         that).
      5. CheatCarrier's mean cruising speed during its observed held window is at least
         EncumbranceDeltaMin m/s SLOWER than Observer's mean cruising speed during its own
         observed (unheld) motion span, in the SAME run — proving the server derived
         CheatCarrier's speed from its own holder state (real ~5.3% slowdown for prop 1's 1 kg
         default mass) rather than honoring its identical, unencumbered wire claim.
      6. CheatCarrier's mean held speed still clears EncumbranceFloor — contained, not
         frozen or over-rejected.

    Exit code 0 = PASS.
#>
[CmdletBinding()]
param(
    [double]$DurationSec = 12,
    [int]$Port = 7809,
    [double]$MaxLegitSpeed = 7.0,      # walk 5.2 m/s + interpolation/sampling slack
    [double]$MaxDisplacement = 30.0,   # honest deterministic walk covers ~15 m
    [double]$MinCheaterTravel = 3.0,   # contained, not frozen: real inputs still move it
    [int]$CarryCheatPortA = 7810,
    # CheatCarrier grabs at ~1-2s and holds (never auto-drops) for the rest of the run; Observer
    # covers its own ~15m straight walk (DeterministicWalkIntentSource: WalkDistance=15,
    # MoveSpeed=3.6 => ~4.7s including its 0.5s start delay) well inside this window. Long
    # enough for both phases to clear the >=9-speed-sample floor below with comfortable margin
    # (measured basis in the PR body, not guessed).
    [double]$CarryCheatDurationSec = 14,
    # Samples this close to an observed motion boundary (the grab landing; Observer's own
    # walk starting/stopping) are excluded from that phase's speed measurement: an
    # engage/start/stop edge can produce one non-cruise sample, and excluding it from both
    # phases equally cannot bias the comparison either way.
    [double]$SettleBufferSec = 0.4,
    # A scripted bot occasionally goes still for its last second or so as the process approaches
    # its own --duration expiry (observed empirically, the same artifact Run-CarryDriftTest.ps1
    # and the pre-fix version of this file both trim) — excluded from the tail of CheatCarrier's
    # held window (Observer's own window is independently event-anchored to its real motion and
    # needs no tail trim; it stops on its own well before this matters).
    [double]$TailTrimSec = 2.0,
    # CheatCarrier's held-phase mean must be at least this much SLOWER than Observer's unheld-
    # phase mean in the SAME run. Theoretical gap is ~0.19 m/s (MoveSpeed 3.6 * (1 -
    # ComputeSpeedFactor(1f)) =~ 3.6 - 3.41 m/s); measured within-run delta across 4 isolated
    # runs of this exact scenario was 0.265-0.283 m/s (Observer's clean straight walk cruises
    # very close to the full 3.6 m/s while CheatCarrier's --cheat-move noise profile costs it a
    # little more on top of pure encumbrance). 0.15 sits with ~0.11 m/s (>40%) of margin below
    # every observed run, tighter than the old cross-run design's 0.10 floor while comparing
    # within one run (issue #6's fix) removes run-to-run variance as a noise source entirely —
    # this floor only needs margin against genuine within-run measurement noise, not against a
    # second process's independent load.
    [double]$EncumbranceDeltaMin = 0.15,
    [double]$EncumbranceFloor = 2.0,  # contained, not frozen (well above the 0.55-factor floor of ~1.98 m/s)
    # Issue #199: scenario 1's containment check reads speed as displacement over a rolling
    # window of this length, not over a single consecutive-log-sample delta. 1.0s is 5x
    # Observer's own 0.2s log-write interval, big enough to absorb a replication-delivery burst
    # landing inside one short log-write pair (see the computation site for the mechanism and
    # measured evidence), while still catching a sustained real speed-hack well inside one
    # second (a genuine breach does not hide for a full second).
    [double]$SpeedWindowSec = 1.0,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== anti-cheat containment test ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$failures = New-Object System.Collections.Generic.List[string]

# --- Scenario 1: direction/speed containment (no props involved) --------------------------
$observerLog = Join-Path $script:LogDir "cheat-observer.jsonl"
$procs = @()
try {
    $server = Start-Godot @("--server", "--port", $Port, "--world", "open", "--log-dir", $script:LogDir) "cheat-server"
    $procs += $server
    if (-not (Wait-ForLogLine (Join-Path $script:LogDir "cheat-server.out.log") "\[server\] listening" 30)) {
        Write-Fail "server never reported listening"
    }

    $observer = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "Observer",
        "--log", $observerLog, "--duration", $DurationSec, "--world", "open") "cheat-observer"
    $procs += $observer
    Start-Sleep -Milliseconds 300
    $cheater = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "Cheater",
        "--duration", $DurationSec, "--world", "open", "--cheat-move") "cheat-cheater"
    $procs += $cheater

    if (-not (Wait-ForExit $observer ([int]($DurationSec + 60)))) { Write-Fail "observer did not exit in time" }
    if (-not (Wait-ForExit $cheater ([int]($DurationSec + 60)))) { Write-Fail "cheat bot did not exit in time" }
    if ($observer.ExitCode -ne 0) { Write-Fail "observer exited with code $($observer.ExitCode)" }
    if ($cheater.ExitCode -ne 0) { Write-Fail "cheat bot exited with code $($cheater.ExitCode)" }
} finally {
    Stop-Procs $procs
}

Write-Host "verifying the server contained the cheater..." -ForegroundColor Cyan
if (-not (Test-Path $observerLog)) { Write-Fail "observer produced no JSON log" }
$samples = @(Get-Content $observerLog | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_ | ConvertFrom-Json })
if ($samples.Count -lt 10) { Write-Fail "observer log has only $($samples.Count) samples" }

# The observer's replicated view of the cheater IS the server's authoritative state.
$track = @()   # @{ T; X; Z }
foreach ($s in $samples) {
    foreach ($p in $s.peers) {
        if ($p.name -eq "Cheater") {
            $track += @{ T = [double]$s.t; X = [double]$p.x; Z = [double]$p.z }
        }
    }
}
if ($track.Count -lt 10) { Write-Fail "observer only saw the cheater in $($track.Count) samples" }

# Per-consecutive-sample speed (informational only — see issue #199). Both X and T already come
# from Observer's own single WriteSample() call, so dt here IS a real measured interval, not an
# assumed nominal one. That is not enough on its own: Observer's ~0.2s log-write cadence and the
# network layer's delivery of Cheater's replicated position are two INDEPENDENT clocks. Under
# heavy machine contention a delivery burst can land several real seconds of legitimate travel
# into the log at once; if that burst happens to fall between two log-writes whose OWN dt stayed
# small, dividing a real dx by that real-but-tiny dt produces a real arithmetic result that is
# not a real rate of travel over any meaningful span. Measured proof (issue #199): the SAME
# tree, same test, same 15.2m/58-sample cheater track read peak 3.74 m/s idle vs peak 17.98 m/s
# loaded — identical distance and sample count, ~5x apart on this metric alone. Kept only for the
# diagnostic log line below; the gate is the windowed check underneath.
$maxSpeed = 0.0
$t0 = $track[0].T
for ($i = 1; $i -lt $track.Count; $i++) {
    $dt = ($track[$i].T - $track[$i - 1].T) / 1000.0
    if ($dt -le 0.01) { continue }
    if (($track[$i].T - $t0) -lt 1500) { continue }  # skip the join/interp warm-up window
    $dx = $track[$i].X - $track[$i - 1].X
    $dz = $track[$i].Z - $track[$i - 1].Z
    $speed = [math]::Sqrt($dx * $dx + $dz * $dz) / $dt
    if ($speed -gt $maxSpeed) { $maxSpeed = $speed }
}

# Windowed speed containment (issue #199's actual gate): for each sample past the warm-up
# window, look forward to the first later sample at least $SpeedWindowSec away on Observer's own
# clock, and read speed as displacement over THAT real span. A minimum-1s window can't be fooled
# by a single burst the way a single log-sample delta can (see the comment above) — any one
# delivery artifact gets averaged across a full second of real travel instead of dominating a
# possibly-tiny dt — while a genuine sustained 25x-speed breach still blows straight through
# $MaxLegitSpeed inside any one-second span, so this still catches a real breach. Slid one sample
# at a time (not a disjoint window walk) so no second of the run goes unchecked.
$maxWindowedSpeed = 0.0
$windowedSampleCount = 0
for ($i = 0; $i -lt $track.Count; $i++) {
    if (($track[$i].T - $t0) -lt 1500) { continue }  # skip the join/interp warm-up window
    $targetT = $track[$i].T + ($SpeedWindowSec * 1000.0)
    $j = -1
    for ($k = $i + 1; $k -lt $track.Count; $k++) {
        if ($track[$k].T -ge $targetT) { $j = $k; break }
    }
    if ($j -lt 0) { continue }  # no sample far enough ahead yet (tail of the run) - not a gap
    $wdt = ($track[$j].T - $track[$i].T) / 1000.0
    if ($wdt -le 0.01) { continue }
    $wdx = $track[$j].X - $track[$i].X
    $wdz = $track[$j].Z - $track[$i].Z
    $wspeed = [math]::Sqrt($wdx * $wdx + $wdz * $wdz) / $wdt
    $windowedSampleCount++
    if ($wspeed -gt $maxWindowedSpeed) { $maxWindowedSpeed = $wspeed }
    if ($wspeed -gt $MaxLegitSpeed) {
        $failures.Add(("scenario 1: cheater averaged {0:F2} m/s over a {1:F2}s window in the server view (> {2} m/s legit bound) ending at t+{3:F1}s" `
            -f $wspeed, $wdt, $MaxLegitSpeed, (($track[$j].T - $t0) / 1000.0)))
    }
}
if ($windowedSampleCount -lt 3) {
    $failures.Add(("scenario 1: only {0} {1}s-window speed reading(s) - run too short or too sparsely sampled to judge sustained speed" -f $windowedSampleCount, $SpeedWindowSec))
}

$dx = $track[$track.Count - 1].X - $track[0].X
$dz = $track[$track.Count - 1].Z - $track[0].Z
$displacement = [math]::Sqrt($dx * $dx + $dz * $dz)
if ($displacement -gt $MaxDisplacement) {
    $failures.Add(("scenario 1: cheater displaced {0:F1}m (> {1}m) - inflated inputs leaked into the authority" -f $displacement, $MaxDisplacement))
}
if ($displacement -lt $MinCheaterTravel) {
    $failures.Add(("scenario 1: cheater only displaced {0:F1}m - contained but frozen; sanitation is over-rejecting" -f $displacement))
}

Write-Host ("  [1] cheater in server view: peak {0:F2}s-window speed {1:F2} m/s (raw per-sample peak {2:F2} m/s, informational only), total displacement {3:F1}m over {4} samples" `
    -f $SpeedWindowSec, $maxWindowedSpeed, $maxSpeed, $displacement, $track.Count) -ForegroundColor Gray

# --- Scenario 2: carry-encumbrance containment (finding 4.1), within-run (issue #6) --------
# ONE dedicated server + one Observer + ONE scripted --cheat-move bot (CheatCarrier). Observer
# needs no special flags (it already runs this way in every suite): its default
# DeterministicWalkIntentSource walks a fixed ~15m heading and stops, never holding anything —
# the unheld control. CheatCarrier grabs prop 1 and holds it (never auto-drops) for the rest of
# the run — the held case. Both readings come from the SAME server/tick/log, so the comparison
# below cancels run-to-run variance BY CONSTRUCTION instead of hoping it stays under the signal.
$Prop1Id = 1 # Crate at (2.5, 0.5, 2.5) in the "propsync" world (PropManager.SpawnInitialProps)

Write-Host "[2] launching within-run CheatCarrier encumbrance run..." -ForegroundColor Cyan
$obsLog = Join-Path $script:LogDir "cheat-carry-observer.jsonl"
$carryProcs = @()
try {
    $srv = Start-Godot @("--server", "--port", $CarryCheatPortA, "--world", "propsync", "--log-dir", $script:LogDir) "cheat-carry-server"
    $carryProcs += $srv
    if (-not (Wait-ForLogLine (Join-Path $script:LogDir "cheat-carry-server.out.log") "\[server\] listening" 30)) {
        Write-Fail "cheat-carry server never reported listening"
    }
    $obs = Start-Godot @("--bot", "--address", "127.0.0.1:$CarryCheatPortA", "--name", "Observer",
        "--log", $obsLog, "--duration", $CarryCheatDurationSec, "--world", "propsync") "cheat-carry-observer"
    $carryProcs += $obs
    Start-Sleep -Milliseconds 300
    # Walk to prop 1, grab it (earliest local clock >= 1.0s), then hold+patrol the same lane
    # Run-CarryDriftTest.ps1 uses for the REST of the run (holdSec -1 = never auto-drop — no
    # held->unheld transition to trigger the patrol-drop freeze described above). --cheat-move
    # lies on the wire the whole time (inflated direction, bogus SpeedFactor claim, periodic
    # NaN), exactly as scenario 1 — the point is the server's authoritative view ignores it.
    $bot = Start-Godot @("--bot", "--address", "127.0.0.1:$CarryCheatPortA", "--name", "CheatCarrier",
        "--duration", $CarryCheatDurationSec, "--world", "propsync",
        "--carry-script", "2.5,0.5,2.5,1.0,-1",
        "--carry-patrol", "2.5,-3,2.5,18", "--cheat-move") "cheat-carry-bot"
    $carryProcs += $bot

    if (-not (Wait-ForExit $obs ([int]($CarryCheatDurationSec + 60)))) { Write-Fail "cheat-carry observer did not exit in time" }
    if (-not (Wait-ForExit $bot ([int]($CarryCheatDurationSec + 60)))) { Write-Fail "cheat-carry bot did not exit in time" }
    if ($obs.ExitCode -ne 0) { Write-Fail "cheat-carry observer exited with code $($obs.ExitCode)" }
    if ($bot.ExitCode -ne 0) { Write-Fail "cheat-carry bot exited with code $($bot.ExitCode)" }
} finally {
    Stop-Procs $carryProcs
}

if (-not (Test-Path $obsLog)) { Write-Fail "cheat-carry observer produced no JSON log" }
$carrySamples = @(Get-Content $obsLog | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_ | ConvertFrom-Json })
if ($carrySamples.Count -lt 10) { Write-Fail "cheat-carry observer log has only $($carrySamples.Count) samples" }
$carryT0 = [double]$carrySamples[0].t
foreach ($s in $carrySamples) {
    $s | Add-Member -NotePropertyName LocalElapsedSec -NotePropertyValue (([double]$s.t - $carryT0) / 1000.0) -Force
}

$carrierPeerId = $null
$observerPeerId = $null
foreach ($s in $carrySamples) {
    if ($null -eq $carrierPeerId) {
        $p = @($s.peers) | Where-Object { $_.name -eq "CheatCarrier" } | Select-Object -First 1
        if ($p) { $carrierPeerId = [int]$p.id }
    }
    if ($null -eq $observerPeerId) {
        $p = @($s.peers) | Where-Object { $_.name -eq "Observer" } | Select-Object -First 1
        if ($p) { $observerPeerId = [int]$p.id }
    }
    if ($carrierPeerId -and $observerPeerId) { break }
}
if ($null -eq $carrierPeerId) { Write-Fail "cheat-carry observer never saw a peer named CheatCarrier" }
if ($null -eq $observerPeerId) { Write-Fail "cheat-carry log never saw its own Observer peer" }

# Returns @{ T; X; Z } for every sample where $PeerId's position is present.
function Get-PositionTrack([int]$PeerId) {
    $track = @()
    foreach ($s in $carrySamples) {
        $peer = @($s.peers) | Where-Object { [int]$_.id -eq $PeerId } | Select-Object -First 1
        if ($peer) { $track += @{ T = [double]$s.LocalElapsedSec; X = [double]$peer.x; Z = [double]$peer.z } }
    }
    return $track
}

# Mean cruising speed from position deltas between consecutive samples inside [WindowStart, WindowEnd].
function Get-CruiseSpeeds($Track, [double]$WindowStart, [double]$WindowEnd) {
    $windowed = @($Track | Where-Object { $_.T -ge $WindowStart -and $_.T -le $WindowEnd })
    $speeds = New-Object System.Collections.Generic.List[double]
    for ($i = 1; $i -lt $windowed.Count; $i++) {
        $dt = $windowed[$i].T - $windowed[$i - 1].T
        if ($dt -le 0.01) { continue }
        $dx = $windowed[$i].X - $windowed[$i - 1].X
        $dz = $windowed[$i].Z - $windowed[$i - 1].Z
        $speeds.Add([math]::Sqrt($dx * $dx + $dz * $dz) / $dt)
    }
    return $speeds
}

# --- held window: event-anchored to the OBSERVED grab (holder 0 -> CheatCarrier) -----------
# A missing anchor is a hard failure, never a silent skip (issue #17: a flake that reads as
# "encumbrance isn't working" is exactly the failure mode that gets dismissed and then hides a
# real regression).
$grabbedAt = -1.0
foreach ($s in $carrySamples) {
    $prop = @($s.props) | Where-Object { [int]$_.id -eq $Prop1Id } | Select-Object -First 1
    $holder = if ($prop) { [int]$prop.holder } else { 0 }
    if ($holder -eq $carrierPeerId) { $grabbedAt = [double]$s.LocalElapsedSec; break }
}
if ($grabbedAt -lt 0) {
    $failures.Add("scenario 2: prop $Prop1Id was never observed held by CheatCarrier - the grab never landed")
}

# --- unheld window: event-anchored to Observer's OWN observed motion span ------------------
# Observer never holds anything (no --carry-* flags), so its own moving span is a clean unheld
# reference, on the same server/tick/log as CheatCarrier's held window. Detected purely from
# observed position (first sample it left its spawn point, last sample before it settled at its
# final resting point) rather than DeterministicWalkIntentSource's timing constants, so this
# stays correct even if that brain's constants change.
$MotionEpsilonM = 0.2
$observerTrack = Get-PositionTrack $observerPeerId
$obsMoveStart = -1.0
$obsMoveEnd = -1.0
if ($observerTrack.Count -ge 2) {
    $spawn = $observerTrack[0]
    $rest = $observerTrack[$observerTrack.Count - 1]
    foreach ($t in $observerTrack) {
        $d = [math]::Sqrt([math]::Pow($t.X - $spawn.X, 2) + [math]::Pow($t.Z - $spawn.Z, 2))
        if ($d -gt $MotionEpsilonM) { $obsMoveStart = $t.T; break }
    }
    for ($i = $observerTrack.Count - 1; $i -ge 0; $i--) {
        $t = $observerTrack[$i]
        $d = [math]::Sqrt([math]::Pow($t.X - $rest.X, 2) + [math]::Pow($t.Z - $rest.Z, 2))
        if ($d -gt $MotionEpsilonM) { $obsMoveEnd = $t.T; break }
    }
}
if ($obsMoveStart -lt 0 -or $obsMoveEnd -le $obsMoveStart) {
    $failures.Add("scenario 2: Observer was never seen moving in this run - no unheld baseline to compare against")
}

if ($failures.Count -eq 0) {
    $tailCutoff = $CarryCheatDurationSec - $TailTrimSec
    $heldStart = $grabbedAt + $SettleBufferSec
    $heldEnd = $tailCutoff
    $unheldStart = $obsMoveStart + $SettleBufferSec
    $unheldEnd = $obsMoveEnd - $SettleBufferSec

    $heldSpeeds = Get-CruiseSpeeds (Get-PositionTrack $carrierPeerId) $heldStart $heldEnd
    $unheldSpeeds = Get-CruiseSpeeds $observerTrack $unheldStart $unheldEnd

    Write-Host ("  [2] grab observed at {0:F2}s (held window [{1:F2}s,{2:F2}s]); Observer moved [{3:F2}s,{4:F2}s] (unheld window [{5:F2}s,{6:F2}s])" `
        -f $grabbedAt, $heldStart, $heldEnd, $obsMoveStart, $obsMoveEnd, $unheldStart, $unheldEnd) -ForegroundColor Gray

    if ($heldSpeeds.Count -lt 9 -or $unheldSpeeds.Count -lt 9) {
        $failures.Add(("scenario 2: too few speed samples to judge encumbrance within one run (held {0}, unheld {1}) - raise CarryCheatDurationSec" `
            -f $heldSpeeds.Count, $unheldSpeeds.Count))
    } else {
        $carrierMean = ($heldSpeeds | Measure-Object -Average).Average
        $unheldMean = ($unheldSpeeds | Measure-Object -Average).Average
        $delta = $unheldMean - $carrierMean
        Write-Host ("  [2] CheatCarrier held mean {0:F3} m/s over {1} samples vs Observer's unheld mean {2:F3} m/s over {3} samples (same run) - delta {4:F3} m/s" `
            -f $carrierMean, $heldSpeeds.Count, $unheldMean, $unheldSpeeds.Count, $delta) -ForegroundColor Gray
        if ($delta -lt $EncumbranceDeltaMin) {
            $msg = "scenario 2: CheatCarrier's held speed ({0:F3} m/s) is not measurably slower than Observer's unheld speed ({1:F3} m/s, same run; delta {2:F3} m/s < {3} m/s floor) - the server appears to have honored the wire-claimed SpeedFactor instead of deriving it from real holder state (finding 4.1 regression)" `
                -f $carrierMean, $unheldMean, $delta, $EncumbranceDeltaMin
            $failures.Add($msg)
        }
        if ($carrierMean -lt $EncumbranceFloor) {
            $msg2 = "scenario 2: CheatCarrier's mean held speed {0:F3} m/s is below {1} m/s - contained but frozen; encumbrance is over-applied" -f $carrierMean, $EncumbranceFloor
            $failures.Add($msg2)
        }
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "Anti-cheat test FAILED with $($failures.Count) assertion failure(s):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}

Write-Host "PASS: doctored movement packets (25x direction, bogus speed factor, NaN bursts) were fully contained by server authority, both in the open (no props) and while genuinely holding a networked prop (real mass-derived encumbrance, not the claimed value)." -ForegroundColor Green
exit 0
