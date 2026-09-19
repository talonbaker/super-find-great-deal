<#
.SYNOPSIS
    W6-3 (A): a scripted --goto-script bot must not strand itself by latching "arrived" on a
    position the server never confirmed.

.DESCRIPTION
    THE DEFECT THIS SUITE EXISTS FOR (recorded in .claude/rules/test-suite.md, measured twice on
    2026-08-30 by LEVER-2 and CARRY-1, and unfixed until this suite went green):

      ScriptedGotoIntentSource latches `_arrived` the first physics tick its OWN body is within
      ArriveRadius of the target, and that latch is permanent by design (a door's onArrive may
      teleport the body, so re-testing the distance afterwards would re-fire it forever). On a
      networked client that body is a PREDICTION. When the server's authoritative body has not
      actually got there -- because inputs were lost, or the box was loaded -- the next
      reconciliation yanks the prediction back to where the server really had it and the bot is
      left short of its target holding a brain that will never ask to move again. LEVER-2
      measured it in 2 of 3 runs (stranded at z=-5.53 against a clean z=-7.33, 1.8 m short);
      CARRY-1 measured the same client's prediction error peaking at 1.71 m on an IDLE machine.

      The rule, from .claude/rules/test-suite.md: a scripted bot may compute its next MOVE from a
      predicted position, but must not take an irreversible action on one.

    HOW THIS SUITE FORCES THE DEFECT, WITHOUT WAITING FOR MACHINE LOAD.

      Machine load is not a test input. Adverse network conditions are: --net-sim
      <latency>,<loss>,<jitter> routes this client's input sends AND its snapshot deliveries
      through an in-process simulator (see NetSim), so the divergence is manufactured inside this
      one bot's process and no other suite on the box is disturbed by it.

      WHAT DOES NOT WORK, MEASURED, BECAUSE THE NEXT READER WILL TRY IT. Packet loss alone does
      NOT reproduce this. At --net-sim 40,55,0 the bot's peak prediction-vs-authority correction
      was 0.10 m and it arrived clean. That is the netcode working as designed and it is worth
      knowing: NetCodec.InputRedundancy is 4, so one sequence is only lost when four consecutive
      datagrams are; ServerInputQueue then bridges the hole by repeating the last real intent
      once, and drains the resulting backlog at MaxCatchUpSteps per tick. Pure latency is likewise
      useless here -- the server replays the same input stream and simply arrives late, in the
      same place.

      WHAT DOES WORK is jitter WIDE ENOUGH TO OUTLAST THE REORDER GRACE, on top of loss.
      ServerInputQueue.GapGraceTicks is 6 (~100 ms): an input that arrives later than that has
      already had its hole bridged, and is then discarded as a stale duplicate. Those steps are
      never simulated by anyone but the client, so the divergence they create is PERMANENT rather
      than a lag, which is precisely the shape of the defect. Sprinting multiplies the metres each
      dropped step is worth.

      THE JITTER WIDTH IS THE WHOLE MARGIN, so do not trim it. At 400 ms the unmodified base
      stranded a bot by 2.87 m on one run and only 1.36 m on the next -- straddling ArriveRadius,
      a 2-of-3 detector, which is a flaky suite rather than a test. At 800 ms the same three runs
      gave 5.81 / 4.81 / 7.67 m while the fixed code gave 1.33 / 1.41 / 1.50 m: a clean factor of
      three, with the fixed side pinned under ArriveRadius by construction. The permanently-lost
      displacement is what strands a bot, it accumulates over the walk, and jitter is the dial
      that controls it.

    WHAT IT ASSERTS.

      (1) ControlBot -- IDENTICAL brain, IDENTICAL world, NO --net-sim -- reaches its own target,
          and rests exactly where it got closest. This is the staging control AND the instrument
          check, and it is read FIRST: if it fails, the world/spawn/route/measurement is broken
          and NOTHING below is evidence about the latch.
      (2) LatchBot -- the same brain under loss+jitter -- COMES TO REST inside ArriveRadius (plus
          settle) of its target. This is the regression. A final sampled position is the SERVER's,
          so this asserts that the authority actually got the body there rather than that the bot
          believed it had. Measured, three runs each: fixed 1.33 / 1.41 / 1.50 m; unfixed
          5.81 / 4.81 / 7.67 m. The fixed figures are bounded by ArriveRadius as a matter of
          construction, not of tuning -- the bot does not stop asking until the authority has it
          inside, and nothing moves it afterwards.
      (3) The raw final distance, closest approach, give-back and peak prediction error are
          printed on every run, pass or fail, because the quantity is the evidence and the verdict
          is not (.claude/rules/test-suite.md, "compare the RAW measured quantity"). Give-back is
          printed but NOT asserted on LatchBot -- see Measure-Arrival for why that check was
          removed after it was shown to fail correct code.

    Exit code 0 = PASS. Headless; no GPU, no display, no human.
#>
[CmdletBinding()]
param(
    [int]$Port = 7821,
    [double]$LatchDurationSec = 40,
    [double]$ControlDurationSec = 22,
    # 60 ms one-way, 80% loss, 800 ms jitter. The JITTER is the load-bearing term -- it must
    # comfortably exceed ServerInputQueue.GapGraceTicks (~100 ms), which is what turns a late
    # input into a PERMANENTLY dropped one. Loss on its own measured 0.10 m of correction and
    # proved nothing. See the DESCRIPTION.
    [string]$NetSim = "60,80,800",
    # ScriptedGotoIntentSource.ArriveRadius (1.5 m) plus 0.5 m of settle slack. THIS IS THE
    # REGRESSION'S GATE, and it is the contract rather than a tuned number: a bot that only
    # latches on a server-confirmed arrival cannot rest outside ArriveRadius, because it does not
    # stop asking until the authority has it inside, and nothing moves it afterwards. Measured
    # over three runs each: fixed 1.33 / 1.41 / 1.50 m (bounded, as the construction requires),
    # unfixed 5.81 / 4.81 / 7.67 m (unbounded -- it stops wherever its guess happened to be).
    [double]$ArriveToleranceM = 2.0,
    # Instrument-integrity only, and it applies to the CLEAN control bot alone. A bot that has
    # stopped on a connection with nothing to reconcile must rest exactly where it got closest;
    # six runs measured give-back 0.00 m, every time, to two decimals. If that ever moves,
    # something is pushing bodies after they stop and NO distance in this suite means anything.
    [double]$ControlDriftToleranceM = 0.25,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# open: a flat 64x64 code-built ground slab with a spawn ring at the origin and exactly
# two interactables, both well clear of the routes below. Chosen over the camp world on purpose --
# this suite must measure the LATCH, and a route through a tree field can strand a bot for a
# completely different (and already-documented) reason.
$LatchTargetXZ = @(0.0, 28.0)      # straight +Z across the open world's 64 m slab from the spawn ring
$ControlTargetXZ = @(22.0, 22.0)   # +X/+Z diagonal, well inside the slab (|x|,|z| < 32)

Write-Host "=== scripted goto: arrival is latched on server truth, not on a prediction (W6-3) ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$procs = @()
try {
    Write-Host "[1/3] launching dedicated server (open world) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "arrivelatch.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "open") "arrivelatch.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
        Write-Fail "server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    # LAUNCH ORDER IS LOAD-BEARING, same reason Run-CampDoorTest.ps1 spells out at length:
    # Gameplay.OnPeerConnected hands out spawn markers in JOIN order and never frees a slot. The
    # two target coordinates above are each chosen for ONE spawn. LatchBot must be first
    # (SpawnPoints[0]) and ControlBot second (SpawnPoints[1]).
    Write-Host "[2/3] launching LatchBot (--net-sim $NetSim) and ControlBot (clean)..." -ForegroundColor Cyan
    $latchLog = Join-Path $script:LogDir "arrivelatch.latch.jsonl"
    $botLatch = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "LatchBot",
        "--log", $latchLog, "--duration", $LatchDurationSec, "--world", "open",
        "--goto-script", "$($LatchTargetXZ[0]),$($LatchTargetXZ[1])", "--goto-sprint",
        "--net-sim", $NetSim) "arrivelatch.latch"
    $procs += $botLatch
    Start-Sleep -Milliseconds 600

    $ctrlLog = Join-Path $script:LogDir "arrivelatch.control.jsonl"
    $botCtrl = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "ControlBot",
        "--log", $ctrlLog, "--duration", $ControlDurationSec, "--world", "open",
        "--goto-script", "$($ControlTargetXZ[0]),$($ControlTargetXZ[1])", "--goto-sprint") "arrivelatch.control"
    $procs += $botCtrl

    $bots = @(
        @{ Name = "LatchBot"; Proc = $botLatch; JsonLog = $latchLog; DurationSec = $LatchDurationSec }
        @{ Name = "ControlBot"; Proc = $botCtrl; JsonLog = $ctrlLog; DurationSec = $ControlDurationSec }
    )
    $deadline = (Get-Date).AddSeconds($LatchDurationSec + 60)
    foreach ($b in $bots) {
        $remainingMs = [int]((($deadline - (Get-Date)).TotalSeconds) * 1000)
        if ($remainingMs -lt 1000) { $remainingMs = 1000 }
        if (-not $b.Proc.WaitForExit($remainingMs)) { Write-Fail "$($b.Name) did not exit within timeout" }
        if ($b.Proc.ExitCode -ne 0) { Write-Fail "$($b.Name) exited with code $($b.Proc.ExitCode); see $($b.JsonLog)" }
    }
} finally {
    Stop-Procs $procs
}

Write-Host "[3/3] verifying arrival against each bot's own final position..." -ForegroundColor Cyan

function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

function Get-SelfPeer($Sample) {
    $selfId = [int64]$Sample.self
    return @($Sample.peers) | Where-Object { [int64]$_.id -eq $selfId } | Select-Object -First 1
}

# Every raw quantity this suite is allowed to reason from, in one place.
#
# WHERE THE BOT COMES TO REST IS THE ASSERTION. Everything else here is printed evidence.
#
# A bot's FINAL sampled position is the SERVER's, not its own guess: by the end of the run its
# prediction has been reconciled onto the authority and nothing is moving it. So the final
# distance to target answers exactly the question the defect is about -- "did the authority
# actually get this body there, or did the bot quit on a guess?" -- and it answers it against a
# number that is already in the code (ScriptedGotoIntentSource.ArriveRadius) rather than one
# tuned to make a run pass.
#
# GIVE-BACK (final minus closest) IS REPORTED AND DELIBERATELY NOT ASSERTED ON THE IMPAIRED BOT,
# and this is worth reading before anyone re-adds the check. It was the original discriminator
# here and it was WRONG on the merits: it measures how far this client's PREDICTION ran ahead of
# the authority before being pulled back, and under adverse conditions a correct client is
# entitled to do that. Measured on the FIXED code: give-back 0.92 / 0.55 / 0.59 m while every one
# of those runs came to rest inside ArriveRadius. Gating on it would have failed a bot that
# arrived exactly as designed, for the crime of predicting -- which is what client prediction is.
# It stays printed because it is the clearest single number for the SHAPE of the old defect
# (unfixed: 5.23 / 4.16 / 6.88 m), and it is still asserted on the CLEAN control bot, where any
# non-zero value means the instrument itself is unsound.
function Measure-Arrival($Samples, $TargetXZ) {
    $dists = New-Object System.Collections.Generic.List[double]
    $peaks = 0.0
    foreach ($s in $Samples) {
        $p = Get-SelfPeer $s
        if ($null -eq $p) { continue }
        $dx = [double]$p.x - $TargetXZ[0]
        $dz = [double]$p.z - $TargetXZ[1]
        $dists.Add([math]::Sqrt($dx * $dx + $dz * $dz))
        $pe = [double]$p.pe
        if ($pe -gt $peaks) { $peaks = $pe }
    }
    if ($dists.Count -eq 0) { return $null }
    $final = $dists[$dists.Count - 1]
    $closest = ($dists | Measure-Object -Minimum).Minimum
    return @{
        Final = $final
        Closest = $closest
        GiveBack = $final - $closest
        PeakCorrectionM = $peaks
        SampleCount = $dists.Count
    }
}

$failures = New-Object System.Collections.Generic.List[string]

# --- (1) STAGING CONTROL, read before anything below --------------------------------------------
$ctrl = Measure-Arrival (Get-Samples $ctrlLog) $ControlTargetXZ
if ($null -eq $ctrl) {
    $failures.Add("(1) ControlBot: no self-position samples at all - this suite measured nothing")
} else {
    Write-Host ("        (1) ControlBot  (no net-sim): final {0:F2} m, closest {1:F2} m, give-back {2:F2} m, peak correction {3:F2} m, {4} samples" -f `
        $ctrl.Final, $ctrl.Closest, $ctrl.GiveBack, $ctrl.PeakCorrectionM, $ctrl.SampleCount) -ForegroundColor DarkGray
    if ($ctrl.Final -gt $ArriveToleranceM) {
        $failures.Add(("(1) ControlBot did not reach its target on a CLEAN connection - final {0:F2} m out (tolerance {1:F2} m). " -f $ctrl.Final, $ArriveToleranceM) +
            "This is the staging control, not the regression: with no --net-sim there is no prediction error to latch on, so the arrive latch is NOT implicated. " +
            "Look at the route (LoopLabWorld's layout), the spawn marker this bot was handed (join order), or whether it was blocked. " +
            "Do not read the LatchBot lines below as evidence about the latch while this line is red.")
    }
    if ($ctrl.GiveBack -gt $ControlDriftToleranceM) {
        $failures.Add(("(1) ControlBot gave back {0:F2} m (closest {1:F2} m, final {2:F2} m) with NO packet loss at all. " -f $ctrl.GiveBack, $ctrl.Closest, $ctrl.Final) +
            "A stopped body on a clean connection has nothing to reconcile and must rest exactly where it got closest - six runs measured 0.00 m. " +
            "Something is moving bodies after they stop (another peer, a collider, a spawn), so NO distance this suite prints can be trusted this run, check (2) included.")
    }
}

# --- (2) THE REGRESSION: it arrived, and then it was pushed back out and never walked again ------
$latch = Measure-Arrival (Get-Samples $latchLog) $LatchTargetXZ
if ($null -eq $latch) {
    $failures.Add("(2) LatchBot: no self-position samples at all - this suite measured nothing")
} else {
    Write-Host ("        (2) LatchBot (net-sim $NetSim): final {0:F2} m, closest {1:F2} m, give-back {2:F2} m, peak correction {3:F2} m, {4} samples" -f `
        $latch.Final, $latch.Closest, $latch.GiveBack, $latch.PeakCorrectionM, $latch.SampleCount) -ForegroundColor DarkGray
    if ($latch.Final -gt $ArriveToleranceM) {
        $failures.Add(("(2) LatchBot came to rest {0:F2} m from its --goto-script target, outside the {1:F2} m gate " -f $latch.Final, $ArriveToleranceM) +
            ("(ScriptedGotoIntentSource.ArriveRadius 1.50 m plus settle). It got as close as {0:F2} m, so it gave back {1:F2} m, " -f $latch.Closest, $latch.GiveBack) +
            ("with a peak prediction-vs-authority correction of {0:F2} m. " -f $latch.PeakCorrectionM) +
            "A final position IS the server's, so this says the AUTHORITY never got this body to the target and the bot stopped asking anyway - " +
            "the arrive latch firing on a position the server never confirmed. A bot that only latches on a confirmed arrival cannot rest outside " +
            "ArriveRadius, because it does not stop asking until the authority has it inside and nothing moves it afterwards. " +
            "ControlBot above walked the identical brain to the same kind of target with no packet loss; if that line is red too, read it first - " +
            "the world or the route is the cause and this line is only a consequence.")
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Fail "arrive-latch suite failed ($($failures.Count) check(s))"
}
Write-Host "PASS: the scripted goto brain comes to rest where the SERVER put it, inside ArriveRadius." -ForegroundColor Green
exit 0
