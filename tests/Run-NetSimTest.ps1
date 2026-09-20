<#
.SYNOPSIS
    Server-authoritative netcode under adverse network conditions: reconciliation
    correctness for owned avatars and render smoothness for remote avatars.

.DESCRIPTION
    Launches a headless dedicated server and N headless bots with the in-process
    network-condition simulator active on every bot (--net-sim latency,loss,jitter —
    default 80 ms one-way, 5% loss, 15 ms jitter). Bots walk the deterministic pattern
    while predicting locally, streaming inputs, and reconciling against server
    snapshots; remote peers snapshot-interpolate. Asserts, from the bots' JSONL logs:

      1. Every bot exits 0, sees the full roster, and travels (live inputs made it
         through loss to the authority).
      2. Reconciliation: each bot's predicted-vs-authoritative correction ("pe") stays
         bounded all run and settles to ~zero at the end — prediction reconciles to the
         server's truth with no permanent drift; the visual offset ("ve") drains too.
      3. Remote smoothness: no remote avatar's rendered position ever moves more than
         a small threshold in a single frame ("mrs") — interpolation never teleports
         under latency + loss + jitter.
      4. Cross-view convergence: every observer's final view of every subject matches
         the subject's own position within tolerance (server truth reached everyone).

    Exit code 0 = PASS. No human interaction required.
#>
[CmdletBinding()]
param(
    [int]$BotCount = 3,
    [double]$DurationSec = 15,
    [int]$Port = 7807,
    [string]$NetSim = "80,5,15",
    [double]$ToleranceMeters = 0.75,
    [double]$MinTravelMeters = 5.0,
    [double]$MaxCorrectionMeters = 2.0,
    [double]$SettledCorrectionMeters = 0.25,
    [double]$MaxRenderStepMeters = 0.35,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== netcode under simulated conditions (net-sim $NetSim) ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$procs = @()
$bots = @()
try {
    $server = Start-Godot @("--server", "--port", $Port, "--world", "open", "--log-dir", $script:LogDir) "netsim-server"
    $procs += $server
    if (-not (Wait-ForLogLine (Join-Path $script:LogDir "netsim-server.out.log") "\[server\] listening" 30)) {
        Write-Fail "server never reported listening"
    }

    for ($i = 1; $i -le $BotCount; $i++) {
        $jsonLog = Join-Path $script:LogDir "netsim-bot$i.jsonl"
        $bot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "Bot$i",
            "--log", $jsonLog, "--duration", $DurationSec, "--world", "open", "--net-sim", $NetSim) "netsim-bot$i"
        $bots += @{ Proc = $bot; Index = $i; JsonLog = $jsonLog }
        $procs += $bot
        Start-Sleep -Milliseconds 300
    }

    $deadline = (Get-Date).AddSeconds($DurationSec + 60)
    foreach ($b in $bots) {
        $remaining = ($deadline - (Get-Date)).TotalSeconds
        if ($remaining -lt 1) { $remaining = 1 }
        if (-not $b.Proc.WaitForExit([int]($remaining * 1000))) {
            Write-Fail "bot$($b.Index) did not exit within timeout"
        }
    }
    foreach ($b in $bots) {
        if ($b.Proc.ExitCode -ne 0) {
            Write-Fail "bot$($b.Index) exited with code $($b.Proc.ExitCode)"
        }
    }
} finally {
    Stop-Procs $procs
}

Write-Host "verifying reconciliation, smoothness, and convergence..." -ForegroundColor Cyan
$failures = New-Object System.Collections.Generic.List[string]
$final = @{}
$statsLines = @()

foreach ($b in $bots) {
    if (-not (Test-Path $b.JsonLog)) { Write-Fail "bot$($b.Index) produced no JSON log" }
    $lines = @(Get-Content $b.JsonLog | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -lt 10) { Write-Fail "bot$($b.Index) log has only $($lines.Count) samples" }
    $samples = @($lines | ForEach-Object { $_ | ConvertFrom-Json })
    $selfId = [long]$samples[0].self
    $t0 = [double]$samples[0].t

    # --- 2. Reconciliation: bounded corrections, settled to ~zero, no drift ---------
    $maxPe = 0.0; $sumPe = 0.0; $peCount = 0
    $maxMrs = 0.0
    foreach ($s in $samples) {
        $warm = ([double]$s.t - $t0) -ge 2000  # skip the join/spawn window
        foreach ($p in $s.peers) {
            if ([long]$p.id -eq $selfId) {
                if ($warm) {
                    $pe = [double]$p.pe
                    $sumPe += $pe; $peCount++
                    if ($pe -gt $maxPe) { $maxPe = $pe }
                }
            } elseif ($warm) {
                # --- 3. Remote smoothness: peak per-frame rendered movement -----------
                $mrs = [double]$p.mrs
                if ($mrs -gt $maxMrs) { $maxMrs = $mrs }
            }
        }
    }
    if ($peCount -eq 0) { $failures.Add("bot$($b.Index) logged no post-warmup samples of itself") }
    if ($maxPe -gt $MaxCorrectionMeters) {
        $failures.Add(("bot{0} peak prediction correction {1:F3}m exceeds {2}m" -f $b.Index, $maxPe, $MaxCorrectionMeters))
    }
    if ($maxMrs -gt $MaxRenderStepMeters) {
        $failures.Add(("bot{0} saw a remote render step of {1:F3}m (> {2}m = visible jump)" -f $b.Index, $maxMrs, $MaxRenderStepMeters))
    }

    $last = $samples[$samples.Count - 1]
    $ownLast = @($last.peers | Where-Object { [long]$_.id -eq $selfId })[0]
    if ($null -eq $ownLast) { Write-Fail "bot$($b.Index) final sample lacks its own avatar" }
    if ([double]$ownLast.pe -gt $SettledCorrectionMeters) {
        $failures.Add(("bot{0} settled prediction error {1:F3}m (> {2}m) - permanent drift" -f $b.Index, [double]$ownLast.pe, $SettledCorrectionMeters))
    }
    if ([double]$ownLast.ve -gt 0.1) {
        $failures.Add(("bot{0} settled visual offset {1:F3}m never drained" -f $b.Index, [double]$ownLast.ve))
    }

    # --- 1 & 4. Travel + final full-roster view for cross-convergence ----------------
    $first = $null
    foreach ($s in $samples) {
        if (@($s.peers | Where-Object { [long]$_.id -eq $selfId }).Count -gt 0) { $first = $s; break }
    }
    $fullRoster = $null
    for ($li = $samples.Count - 1; $li -ge 0; $li--) {
        if (@($samples[$li].peers).Count -eq $BotCount) { $fullRoster = $samples[$li]; break }
    }
    if ($null -eq $first -or $null -eq $fullRoster) {
        Write-Fail "bot$($b.Index) never saw itself or the full roster"
    }
    $entry = @{ Index = $b.Index; View = @{}; OwnFirst = $null; OwnLast = $null }
    foreach ($p in $first.peers) { if ([long]$p.id -eq $selfId) { $entry.OwnFirst = $p } }
    foreach ($p in $fullRoster.peers) {
        $entry.View[[long]$p.id] = $p
        if ([long]$p.id -eq $selfId) { $entry.OwnLast = $p }
    }
    $final[$selfId] = $entry

    $meanPe = if ($peCount -gt 0) { $sumPe / $peCount } else { 0 }
    $statsLines += ("  bot{0}: peak correction {1:F3}m, mean {2:F4}m, settled {3:F4}m, peak remote step {4:F3}m" `
        -f $b.Index, $maxPe, $meanPe, [double]$ownLast.pe, $maxMrs)
}

foreach ($selfId in $final.Keys) {
    $e = $final[$selfId]
    $dx = $e.OwnLast.x - $e.OwnFirst.x
    $dz = $e.OwnLast.z - $e.OwnFirst.z
    $travel = [math]::Sqrt($dx * $dx + $dz * $dz)
    if ($travel -lt $MinTravelMeters) {
        $failures.Add(("bot{0} only travelled {1:F2}m under net-sim - inputs not reaching the authority" -f $e.Index, $travel))
    }
}

$maxError = 0.0
foreach ($obsId in $final.Keys) {
    $obs = $final[$obsId]
    foreach ($subjId in $final.Keys) {
        $subj = $final[$subjId]
        if (-not $obs.View.ContainsKey($subjId)) {
            $failures.Add("bot$($obs.Index) never saw peer $subjId (bot$($subj.Index))")
            continue
        }
        $seen = $obs.View[$subjId]; $own = $subj.OwnLast
        $dx = $seen.x - $own.x; $dy = $seen.y - $own.y; $dz = $seen.z - $own.z
        $dist = [math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
        if ($dist -gt $maxError) { $maxError = $dist }
        if ($dist -gt $ToleranceMeters) {
            $failures.Add(("bot{0}'s view of bot{1} is off by {2:F3}m under net-sim" -f $obs.Index, $subj.Index, $dist))
        }
    }
}

Write-Host ""
foreach ($line in $statsLines) { Write-Host $line -ForegroundColor Gray }
if ($failures.Count -gt 0) {
    Write-Host "Net-sim test FAILED with $($failures.Count) assertion failure(s):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}

Write-Host ("PASS: {0} bots under {1} (one-way ms, loss %, jitter ms): predictions reconciled, remotes stayed smooth, views converged (max {2:F3}m)." `
    -f $BotCount, $NetSim, $maxError) -ForegroundColor Green
exit 0
