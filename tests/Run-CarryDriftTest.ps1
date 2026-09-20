<#
.SYNOPSIS
    Carry drift regression: prove the held-item-to-anchor offset stays BOUNDED and does NOT
    grow across a long (18s+) sustained hold-and-walk episode. Guards the exact gap that let
    a suspected "drift that grows the longer you hold and walk" symptom go unmeasured: the
    existing carry test only samples grab/hold/drop convergence with the holder standing (or
    walking to one point and stopping), never a continuous walk long enough for slow, per-tick
    accumulation to show up.

.DESCRIPTION
    Launches a headless "propsync" server (prop 1 = Crate at (2.5,0.5,2.5)) and ONE scripted
    carry bot that:
      - walks to prop 1 and grabs it (local clock >= 1.0s),
      - never voluntarily drops (holdSec = -1),
      - PATROLS a ~21m north-south lane at x=2.5 (inside propsync's safe x in [-4,4] corridor)
        for the whole run - continuous walking, reversing direction a few times, never standing
        still (--carry-patrol).

    Each tick the bot logs (BotHarness.PropSample) the held prop's distance from ITS OWN rendered
    body (SandboxAvatar.RenderGlobalPosition = the corrected on-screen position - what a player
    actually sees), measured directly against the avatar's own accessor, never reconstructed from
    position + rest offset + bob (the bob oscillation would make that fragile enough to manufacture
    fake drift). This "Bd" scalar sums the WHOLE carry chain (body -> anchor -> item), so a drifting
    anchor shows up here even if the item still chases its anchor perfectly. Correct behaviour holds
    it near |CarryAnchorRest| (~0.88m); a real per-tick accumulation makes it climb without settling.
    (The log also carries "Off" = the item's distance from its anchor, the isolated chase lag, for
    diagnosis when a failure needs localising to chase-vs-anchor.)

    Over the sustained-walk window (local elapsed >= 3s, after the grab and first leg settle)
    it asserts:
      1. Held throughout   - prop 1's holder is this bot on every walk-window sample (a real
                             measurement, not a no-op: if the grab silently failed the distance
                             would be a meaningless 0 and this catches it).
      2. Bounded           - mean body-distance <= MeanMax and peak <= PeakMax. Steady is ~1.01m
                             (the rest offset), swinging with facing/turns; the caps sit above
                             that with margin but far below any accumulating drift.
                             RE-MEASURED 2026-08-30 (W7-8): 0.88m -> 1.010m mean / 1.063m peak,
                             growth 0.000m over 83 samples. The step is the ARMFUL LOAD LIFT, not
                             drift - a crate is now posed as an armful and rides 0.70 of its own
                             half-height above the carry mount so the hands end up under it
                             (Carryable.ArmfulLoadLiftFraction). Prop 1 is a crate, so this suite
                             sees the whole of it. The caps are untouched: growth, not the
                             absolute distance, is the discriminating check (item 3), and a lift
                             is flat over time by construction.
      3. Does not grow     - mean(last third) - mean(first third) <= GrowthMax. This is the
                             discriminating check: the by-design offset is flat over time; a real
                             per-tick accumulation trends steadily upward.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [int]$Port = 7817,
    [double]$DurationSec = 20,
    # Tolerances on the held-item-to-rendered-body distance (Bd). Correct behaviour holds it near
    # |CarryAnchorRest| (~0.88m), swinging with facing and turn-arounds; growth over an 18s hold is
    # ~0m. Caps sit above the by-design behaviour and far below any real accumulation (even
    # 0.05 m/s of drift blows past the growth and peak caps within the run).
    [double]$MeanMax = 1.15,
    [double]$PeakMax = 1.45,
    [double]$GrowthMax = 0.12,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

$Prop1Id = 1 # Crate at (2.5, 0.5, 2.5) in the propsync world (PropManager.SpawnInitialProps)

Write-Host "=== carry-drift: sustained hold+walk offset stays bounded ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$procs = @()
try {
    Write-Host "[1/3] launching dedicated server (propsync world) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "carrydrift.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "propsync") "carrydrift.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    Write-Host "[2/3] launching patrolling carry bot (grab, never drop, walk ${DurationSec}s)..." -ForegroundColor Cyan
    $log = Join-Path $script:LogDir "carrydrift.jsonl"
    $bot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "DriftBot",
        "--log", $log, "--duration", $DurationSec, "--world", "propsync",
        "--carry-script", "2.5,0.5,2.5,1.0,-1", "--carry-patrol", "2.5,-3,2.5,18") "carrydrift"
    $procs += $bot

    $deadline = (Get-Date).AddSeconds($DurationSec + 60)
    $remainingMs = [int]((($deadline - (Get-Date)).TotalSeconds) * 1000)
    if (-not $bot.WaitForExit($remainingMs)) { Write-Fail "DriftBot did not exit within timeout" }
    if ($bot.ExitCode -ne 0) { Write-Fail "DriftBot exited with code $($bot.ExitCode); see $log" }
} finally {
    Stop-Procs $procs
}

Write-Host "[3/3] verifying held-item-to-body distance is bounded and non-growing..." -ForegroundColor Cyan

if (-not (Test-Path $log)) { Write-Fail "log not found: $log" }
$lines = @(Get-Content $log | Where-Object { $_.Trim().Length -gt 0 })
if ($lines.Count -eq 0) { Write-Fail "log has no samples: $log" }
$parsed = @($lines | ForEach-Object { $_ | ConvertFrom-Json })
$self = [int]$parsed[0].self
$t0 = [double]$parsed[0].t

# Sustained-walk window: local elapsed >= 3s (grab ~1s + first leg settle), through end of run.
$walk = @($parsed | Where-Object { ((([double]$_.t - $t0) / 1000.0) -ge 3.0) })
if ($walk.Count -lt 10) { Write-Fail "too few walk-window samples ($($walk.Count)); bot may not have run" }

$failures = New-Object System.Collections.Generic.List[string]

# Pull prop 1's holder + body-distance (Bd) for each walk-window sample.
$rows = @($walk | ForEach-Object {
    $p = @($_.props | Where-Object { [int]$_.id -eq $Prop1Id }) | Select-Object -First 1
    [pscustomobject]@{
        T = [math]::Round((([double]$_.t - $t0) / 1000.0), 2)
        Holder = if ($p) { [int]$p.holder } else { 0 }
        Bd = if ($p) { [double]$p.bd } else { 0.0 }
    }
})

# 1. Held throughout: prop 1's holder is this bot on every walk-window sample.
$notHeld = @($rows | Where-Object { $_.Holder -ne $self })
if ($notHeld.Count -gt 0) {
    $first = $notHeld[0]
    $failures.Add("prop $Prop1Id not held by bot ($self) during walk window (e.g. @ $($first.T)s holder=$($first.Holder)); distance measurement is not a live hold")
}

# 2 & 3. Bounded + non-growing, over the samples where the bot genuinely holds it.
$dists = @($rows | Where-Object { $_.Holder -eq $self } | ForEach-Object { $_.Bd })
if ($dists.Count -lt 9) {
    $failures.Add("too few held-while-walking distance samples ($($dists.Count)) to judge drift")
} else {
    $mean = ($dists | Measure-Object -Average).Average
    $peak = ($dists | Measure-Object -Maximum).Maximum
    $third = [int]($dists.Count / 3)
    $earlyMean = ($dists[0..($third - 1)] | Measure-Object -Average).Average
    $lateMean = ($dists[($dists.Count - $third)..($dists.Count - 1)] | Measure-Object -Average).Average
    $growth = $lateMean - $earlyMean

    Write-Host ("        body-distance: mean={0:F3}m peak={1:F3}m  early-third={2:F3}m late-third={3:F3}m growth={4:F3}m  (n={5})" -f `
        $mean, $peak, $earlyMean, $lateMean, $growth, $dists.Count) -ForegroundColor DarkGray

    if ($mean -gt $MeanMax) { $failures.Add("mean body-distance $([math]::Round($mean,3))m exceeds $MeanMax m - held item sits too far from the body on average") }
    if ($peak -gt $PeakMax) { $failures.Add("peak body-distance $([math]::Round($peak,3))m exceeds $PeakMax m - held item detached from the body") }
    if ($growth -gt $GrowthMax) { $failures.Add("body-distance GREW by $([math]::Round($growth,3))m (early $([math]::Round($earlyMean,3)) -> late $([math]::Round($lateMean,3))) over the hold; exceeds $GrowthMax m - this is accumulating drift, not by-design lag") }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "CARRY-DRIFT-TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "CARRY-DRIFT-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: held-item-to-body distance stayed bounded and did not grow across the sustained hold+walk." -ForegroundColor Green
Write-Host ""
Write-Host "CARRY-DRIFT-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
