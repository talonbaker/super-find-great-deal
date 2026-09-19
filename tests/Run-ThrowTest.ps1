<#
.SYNOPSIS
    Throw + loose-physics streaming convergence test: prove a thrown/dropped prop is
    server-simulated, its motion streams to every peer, it settles to the same resting
    position everywhere, and a prop that goes out of bounds always recovers home.

.DESCRIPTION
    Launches a headless dedicated server in the "propsync" world (server-spawned test props:
    prop 1 = Crate at (2.5, 0.5, 2.5), prop 2 = Ball at (-2.5, 0.5, -2.5), prop 3 = Crate at
    (2, 0.5, 30.5) — deliberately close to the open field's finite 64x64 ground slab edge, so a
    throw easily carries it off the slab and past the kill-plane) and three scripted headless
    bots:

      - ThrowBotA: walks to prop 1, grabs it (earliest local clock >= 1.0s), then throws it
        ~2.5s after the grab lands. Never auto-drops.
      - ThrowBotB: walks to (and idles near) prop 1 too, but its earliest-grab clock is set so
        far in the future it never actually grabs — a pure independent witness to A's throw,
        proving convergence across two separate peers rather than just one bot's own view.
      - OOBBotC: walks the long way out to prop 3, grabs it, then throws it ~2.5s after the
        grab lands. Because prop 3 sits right at the ground slab's edge, the throw carries it
        off into the void; the server's kill-plane recovery should return it to its exact spawn
        transform (HomeTransform) rather than letting it fall forever.

    Each bot logs one JSONL sample per tick (BotHarness), including every networked prop's
    current holder and position. The script normalizes each bot's own log to its own local
    elapsed clock (its first sample's timestamp), then anchors every assertion to an OBSERVED
    state transition in that log — the grab, the release, the fall past the slab edge — rather
    than to an assumed wall-clock window. See the anchoring note above assertion 1: the bots'
    scripted timeline slips under load, so any constant saying WHEN an event should have happened
    is a guess about machine speed, and that guess was this suite's long-standing flake.

      1. Holder clears     - from A's observed release onward, both A's and B's own views show
                               prop 1 unheld (0), and it stays that way.
      2. Prop travels       - prop 1's position genuinely changes in the seconds after the release
                               (it's being simulated and streamed, not stuck).
      3. Settle convergence - prop 1 comes to rest at the SAME position on both A's and B's peers
                               (server-authoritative settle, broadcast reliably to everyone).
      4. OOB recovery        - prop 3, thrown off the slab's edge, is OBSERVED falling into the void
                               and then ends up back at its exact home transform (2, 0.5, 30.5) —
                               never lost, never NaN.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [int]$Port = 7816,
    [double]$ABDurationSec = 22,
    # Bot lifetimes are COMPLETION BUDGETS -- how long the scenario gets to finish -- not
    # assumptions about when any step lands, which is the distinction the assertions below turn on.
    # Raised from 16/24 on evidence, not superstition: a marathon slipped bot C's out-of-bounds
    # crossing from ~12.8s to 18.17s, leaving a 24s log too short to contain the recovery that
    # followed. These tolerate ~11s of slip against a worst observed ~5.4s.
    [double]$CDurationSec = 32,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# Server's authoritative test-prop roster for the "propsync" world (PropManager.SpawnInitialProps).
$Prop1Id = 1 # Crate at (2.5, 0.5, 2.5)
$Prop3Id = 3 # Crate at (2, 0.5, 30.5) -- deliberately near the slab's edge
$Prop3Home = @(2.0, 0.5, 30.5)

Write-Host "=== throw: server-simulated loose-physics streaming convergence test ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$procs = @()
try {
    Write-Host "[1/3] launching dedicated server (propsync world) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "throw.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "propsync") "throw.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    Write-Host "[2/3] launching scripted throw bots..." -ForegroundColor Cyan

    $aLog = Join-Path $script:LogDir "throwA.jsonl"
    $botA = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "ThrowBotA",
        "--log", $aLog, "--duration", $ABDurationSec, "--world", "propsync",
        "--carry-script", "2.5,0.5,2.5,1.0,-1,2.5") "throwA"
    $procs += $botA
    Start-Sleep -Milliseconds 300

    $bLog = Join-Path $script:LogDir "throwB.jsonl"
    $botB = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "ThrowBotB",
        "--log", $bLog, "--duration", $ABDurationSec, "--world", "propsync",
        "--carry-script", "2.5,0.5,2.5,9999,-1") "throwB"
    $procs += $botB
    Start-Sleep -Milliseconds 300

    $cLog = Join-Path $script:LogDir "throwC.jsonl"
    $botC = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "OOBBotC",
        "--log", $cLog, "--duration", $CDurationSec, "--world", "propsync",
        "--carry-script", "2,0.5,30.5,1.0,-1,2.5") "throwC"
    $procs += $botC

    $bots = @(
        @{ Name = "ThrowBotA"; Proc = $botA; JsonLog = $aLog }
        @{ Name = "ThrowBotB"; Proc = $botB; JsonLog = $bLog }
        @{ Name = "OOBBotC";   Proc = $botC; JsonLog = $cLog }
    )

    $maxDuration = [math]::Max($ABDurationSec, $CDurationSec)
    $deadline = (Get-Date).AddSeconds($maxDuration + 80)
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

Write-Host "[3/3] verifying throw/loose-physics convergence..." -ForegroundColor Cyan

# Loads a bot's JSONL log and normalizes each sample's timestamp to seconds since that bot's own
# first sample (separate OS processes don't share a wall clock).
function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    $parsed = @($lines | ForEach-Object { $_ | ConvertFrom-Json })
    $t0 = [double]$parsed[0].t
    foreach ($s in $parsed) {
        $s | Add-Member -NotePropertyName LocalElapsedSec -NotePropertyValue (([double]$s.t - $t0) / 1000.0) -Force
    }
    return $parsed
}

function Get-Prop($Sample, [int]$PropId) {
    $p = @($Sample.props) | Where-Object { [int]$_.id -eq $PropId }
    if ($null -eq $p -or @($p).Count -eq 0) { return $null }
    return ($p | Select-Object -First 1)
}

function Get-Holder($Sample, [int]$PropId) {
    $p = Get-Prop $Sample $PropId
    if ($null -eq $p) { return $null }
    return [int]$p.holder
}

function Get-Pos($Sample, [int]$PropId) {
    $p = Get-Prop $Sample $PropId
    if ($null -eq $p) { return $null }
    return @([double]$p.x, [double]$p.y, [double]$p.z)
}

function Dist3($a, $b) {
    $dx = $a[0] - $b[0]; $dy = $a[1] - $b[1]; $dz = $a[2] - $b[2]
    return [math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
}

# Walks $Samples once and reports when $PropId was first picked up, and first released after that.
# GrabbedAt/ReleasedAt are -1 when that transition never appears in this log — callers MUST treat
# that as a failure, never as "nothing to check" (see the anchoring note below).
function Get-HoldSpan($Samples, [int]$PropId) {
    $grabbedAt = -1.0; $releasedAt = -1.0; $holder = 0
    foreach ($s in $Samples) {
        $h = Get-Holder $s $PropId
        if ($null -eq $h) { continue }
        if ($grabbedAt -lt 0) {
            if ($h -ne 0) { $grabbedAt = [double]$s.LocalElapsedSec; $holder = $h }
        } elseif ($releasedAt -lt 0 -and $h -eq 0) {
            $releasedAt = [double]$s.LocalElapsedSec
        }
    }
    return @{ GrabbedAt = $grabbedAt; ReleasedAt = $releasedAt; Holder = $holder }
}

$samplesA = Get-Samples $aLog
$samplesB = Get-Samples $bLog
$samplesC = Get-Samples $cLog

$failures = New-Object System.Collections.Generic.List[string]

# --- event anchoring ---------------------------------------------------------------------------
# Every window below is derived from an OBSERVED transition in the bot's own samples, never from an
# assumed wall clock.
#
# Why: the bots run a scripted wall-clock timeline, and under CPU contention (the 22-suite marathon
# runs these back-to-back) that timeline slips by seconds — while a constant like "the throw has
# landed by 7.0s" does not. Measured on an unloaded run, A's throw lands at ~3.4s against a window
# opening at 7.0s: 3.6s of cushion, versus an observed ~5s slip under load. When the slip outruns
# the cushion the assertion fails on a prop that behaved perfectly. That was this suite's long-
# standing "load-margin flake" — not environmental, just a guess about machine speed baked into a
# constant. Anchoring to the transition removes the guess, and TIGHTENS the test besides: a throw
# that never fired can no longer pass merely because the prop sat loose when the clock said to look.
#
# Constants below are durations of SIMULATION measured AFTER an observed event — never predictions
# of when that event occurs. That distinction is the whole fix; keep it if you retune them.
#
# A missing anchor is a HARD FAILURE, never a skip. An anchor helper that quietly reported "nothing
# to check" would turn a real regression (throw never fires) into a green run — strictly worse than
# the flake it replaced.
$FlightSpanSec = 3.0        # window after release in which a real throw must visibly move
$SettleAfterThrowSec = 5.5  # physics needed after leaving the hand before rest is stable
$RecoverWithinSec = 6.0     # server kill-plane recovery budget once the prop is off the slab
                            # (measured ~2s unloaded) -- a limit on the RECOVERY, not a guess about
                            # when the fall happens. Exceeding it is a real bug, not a slow machine.
$OobBelowY = -5.0           # slab sits at y~0.5; KillPlaneY is -30, but samples are ~200ms apart so
                            # the crossing itself is rarely sampled. -5 proves it left and is falling.

# --- 1: holder clears at the throw, and STAYS clear -------------------------------------------
$spanA = Get-HoldSpan $samplesA $Prop1Id
$spanB = Get-HoldSpan $samplesB $Prop1Id
foreach ($set in @(@{ Name = "A"; Samples = $samplesA; Span = $spanA },
                   @{ Name = "B"; Samples = $samplesB; Span = $spanB })) {
    $span = $set.Span
    if ($span.GrabbedAt -lt 0) {
        $failures.Add("bot $($set.Name): prop $Prop1Id was never held in this log - the grab never happened, so no throw was staged")
        continue
    }
    if ($span.ReleasedAt -lt 0) {
        $failures.Add(("bot {0}: prop {1} was grabbed at {2:F2}s but never released before the log ended - the throw never fired" -f `
            $set.Name, $Prop1Id, $span.GrabbedAt))
        continue
    }
    foreach ($s in $set.Samples) {
        if ($s.LocalElapsedSec -lt $span.ReleasedAt) { continue }
        $h = Get-Holder $s $Prop1Id
        if ($null -eq $h) { $failures.Add("bot $($set.Name) @ $($s.LocalElapsedSec)s: prop $Prop1Id missing from sample"); continue }
        if ($h -ne 0) {
            $failures.Add(("bot {0} @ {1:F2}s: prop {2} holder={3}, expected 0 (loose/resting) after the throw at {4:F2}s" -f `
                $set.Name, $s.LocalElapsedSec, $Prop1Id, $h, $span.ReleasedAt))
        }
    }
}

# --- 2: prop 1 genuinely travels once it leaves the hand --------------------------------------
# Anchored to A's OWN observed release, so it captures the real flight rather than whenever the
# clock guessed the flight would be: diff the extremes — real motion, not a stuck/duplicated sample.
$flightWindow = @()
if ($spanA.ReleasedAt -ge 0) {
    $flightWindow = @($samplesA | Where-Object {
        $_.LocalElapsedSec -ge $spanA.ReleasedAt -and $_.LocalElapsedSec -le ($spanA.ReleasedAt + $FlightSpanSec) })
}
if ($spanA.ReleasedAt -lt 0) {
    # Already reported by assertion 1; no anchor means nothing here can be checked honestly.
} elseif ($flightWindow.Count -lt 2) {
    $failures.Add(("bot A: fewer than 2 samples in the {0}s after the throw at {1:F2}s - cannot prove motion" -f `
        $FlightSpanSec, $spanA.ReleasedAt))
} else {
    $positions = @($flightWindow | ForEach-Object { Get-Pos $_ $Prop1Id } | Where-Object { $null -ne $_ })
    $maxSpread = 0.0
    for ($i = 0; $i -lt $positions.Count; $i++) {
        for ($j = $i + 1; $j -lt $positions.Count; $j++) {
            $d = Dist3 $positions[$i] $positions[$j]
            if ($d -gt $maxSpread) { $maxSpread = $d }
        }
    }
    if ($maxSpread -lt 0.5) {
        $failures.Add(("bot A: prop {0} barely moved during the flight window (max spread {1:F3}m) - throw may not be simulating" -f $Prop1Id, $maxSpread))
    }
}

# --- 3: settle convergence -------------------------------------------------------------------
# Well after the throw + settle (SettleTicks ~0.3s of low speed after it stops moving), both A
# and B should agree on prop 1's exact resting position (server-broadcast reliably to everyone).
# Each log is anchored to ITS OWN observed release: separate processes, separate clocks, and A and
# B genuinely see the release a few hundred ms apart.
$aSettle = @(); $bSettle = @()
if ($spanA.ReleasedAt -ge 0) {
    $aSettle = @($samplesA | Where-Object { $_.LocalElapsedSec -ge ($spanA.ReleasedAt + $SettleAfterThrowSec) })
    if ($aSettle.Count -eq 0) {
        $failures.Add(("bot A: no samples {0}s after its throw at {1:F2}s - the log ended first, so the settle was never observed (--duration {2}s left no room after a late throw)" -f `
            $SettleAfterThrowSec, $spanA.ReleasedAt, $ABDurationSec))
    }
}
if ($spanB.ReleasedAt -ge 0) {
    $bSettle = @($samplesB | Where-Object { $_.LocalElapsedSec -ge ($spanB.ReleasedAt + $SettleAfterThrowSec) })
    if ($bSettle.Count -eq 0) {
        $failures.Add(("bot B: no samples {0}s after its throw at {1:F2}s - the log ended first, so the settle was never observed (--duration {2}s left no room after a late throw)" -f `
            $SettleAfterThrowSec, $spanB.ReleasedAt, $ABDurationSec))
    }
}
if ($aSettle.Count -gt 0 -and $bSettle.Count -gt 0) {
    $aPos = Get-Pos ($aSettle | Select-Object -Last 1) $Prop1Id
    $bPos = Get-Pos ($bSettle | Select-Object -Last 1) $Prop1Id
    if ($null -eq $aPos -or $null -eq $bPos) {
        $failures.Add("prop $Prop1Id missing from A or B's settle-window sample")
    } else {
        $d = Dist3 $aPos $bPos
        if ($d -gt 0.15) {
            $failures.Add(("bot A vs bot B disagree on prop {0}'s settled position by {1:F3}m (> 0.15m)" -f $Prop1Id, $d))
        }
    }
    # Also confirm it actually stopped: every sample in the window should be close to the last one.
    foreach ($s in $aSettle) {
        $p = Get-Pos $s $Prop1Id
        if ($null -eq $p) { continue }
        $d = Dist3 $p $aPos
        if ($d -gt 0.3) {
            $failures.Add(("bot A @ $($s.LocalElapsedSec)s: prop {0} still moving in the settle window (off by {1:F3}m from the window's last sample)" -f $Prop1Id, $d))
        }
    }
}

# --- 4: OOB recovery -----------------------------------------------------------------------
# OOBBotC's throw carries prop 3 off the slab's edge. Anchored to the fall it actually took, not to
# a guess about when it would take it — this is the assertion whose constant demonstrably lost the
# race under load ("bot C pipeline ran ~5s late; prop hadn't crossed KillPlaneY when window closed").
# Requiring the fall to be OBSERVED also closes a hole in the old version: a prop that never left the
# slab trivially satisfied "is at home", so a broken throw could pass as a successful recovery.
$spanC = Get-HoldSpan $samplesC $Prop3Id
$cWindow = @()
if ($spanC.GrabbedAt -lt 0) {
    $failures.Add("bot C: prop $Prop3Id was never held - the OOB throw was never staged")
} elseif ($spanC.ReleasedAt -lt 0) {
    $failures.Add(("bot C: prop {0} was grabbed at {1:F2}s but never released - the OOB throw never fired" -f `
        $Prop3Id, $spanC.GrabbedAt))
} else {
    $crossedAt = -1.0
    foreach ($s in $samplesC) {
        if ($s.LocalElapsedSec -lt $spanC.ReleasedAt) { continue }
        $p = Get-Pos $s $Prop3Id
        if ($null -eq $p) { continue }
        if ($p[1] -lt $OobBelowY) { $crossedAt = [double]$s.LocalElapsedSec; break }
    }
    if ($crossedAt -lt 0) {
        $failures.Add(("bot C: prop {0} never fell below y={1} after the throw at {2:F2}s - it never left the slab, so this run never exercised OOB recovery" -f `
            $Prop3Id, $OobBelowY, $spanC.ReleasedAt))
    } else {
        # Search for the recovery ITSELF rather than waiting a fixed beat and assuming it has
        # already happened by then. Waiting needs the log to outlive crossedAt + budget; searching
        # needs only enough log to contain the event, which lands ~2s after the prop leaves the
        # slab. That difference is not academic: a marathon put this crossing at 18.17s (vs ~12.8s
        # unloaded, a 5.4s slip) and a fixed 6s tail ran off the end of a 24s log, failing a run
        # whose prop had recovered perfectly well.
        $recoveredAt = -1.0
        foreach ($s in $samplesC) {
            if ($s.LocalElapsedSec -lt $crossedAt) { continue }
            $p = Get-Pos $s $Prop3Id
            if ($null -eq $p) { continue }
            if ((Dist3 $p $Prop3Home) -le 0.2 -and (Get-Holder $s $Prop3Id) -eq 0) {
                $recoveredAt = [double]$s.LocalElapsedSec
                break
            }
        }
        $logEndC = [double]($samplesC[$samplesC.Count - 1].LocalElapsedSec)
        if ($recoveredAt -lt 0) {
            # Distinguish "the scenario ran out of log" from "recovery is broken" -- conflating them
            # is what sent five marathons chasing a phantom.
            if (($logEndC - $crossedAt) -lt $RecoverWithinSec) {
                $failures.Add(("bot C: prop {0} left the slab at {1:F2}s but the log ends at {2:F2}s - only {3:F2}s later, inside the {4}s recovery budget, so recovery was never observable. Scenario cut short, NOT a recovery bug: --duration {5}s left no room after a late throw (this crossing lands ~12.8s unloaded)." -f `
                    $Prop3Id, $crossedAt, $logEndC, ($logEndC - $crossedAt), $RecoverWithinSec, $CDurationSec))
            } else {
                $failures.Add(("bot C: prop {0} went out of bounds at {1:F2}s and NEVER returned to home ({2}) in the {3:F2}s of log that followed - kill-plane recovery is broken" -f `
                    $Prop3Id, $crossedAt, ($Prop3Home -join ","), ($logEndC - $crossedAt)))
            }
        } elseif (($recoveredAt - $crossedAt) -gt $RecoverWithinSec) {
            $failures.Add(("bot C: prop {0} took {1:F2}s to recover home after going out of bounds at {2:F2}s (budget {3}s)" -f `
                $Prop3Id, ($recoveredAt - $crossedAt), $crossedAt, $RecoverWithinSec))
        } else {
            # Recovered -- now prove it STAYS home and unheld for the rest of the log.
            $cWindow = @($samplesC | Where-Object { $_.LocalElapsedSec -ge $recoveredAt })
        }
    }
}
if ($cWindow.Count -gt 0) {
    foreach ($s in $cWindow) {
        $p = Get-Pos $s $Prop3Id
        if ($null -eq $p) { $failures.Add("bot C @ $($s.LocalElapsedSec)s: prop $Prop3Id missing from sample"); continue }
        $d = Dist3 $p $Prop3Home
        if ($d -gt 0.2) {
            $failures.Add(("bot C @ $($s.LocalElapsedSec)s: prop {0} at ({1:F2},{2:F2},{3:F2}), {4:F3}m from home {5} - not recovered" -f `
                $Prop3Id, $p[0], $p[1], $p[2], $d, ($Prop3Home -join ",")))
        }
        $h = Get-Holder $s $Prop3Id
        if ($h -ne 0) {
            $failures.Add("bot C @ $($s.LocalElapsedSec)s: prop $Prop3Id holder=$h, expected 0 after OOB recovery")
        }
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "THROW-TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "THROW-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: holder cleared, prop travelled, settle converged across peers, OOB prop recovered home." -ForegroundColor Green
Write-Host ""
Write-Host "THROW-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
