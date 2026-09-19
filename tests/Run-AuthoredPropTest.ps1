<#
.SYNOPSIS
    Proves an AUTHORED prop (placed as a real node in BubbleTest.tscn, adopted into the
    netcode by PropManager.AdoptAuthoredProps — never spawned) is carryable and syncs
    across peers exactly like a runtime-spawned one: grab converges, it genuinely travels
    on a throw, and both holder-clearing and the settled resting position agree across two
    independent peers.

.DESCRIPTION
    Launches a headless dedicated server in the "bubbletest" world (the authored level;
    no runtime spawning happens for it — see PropManager.AdoptAuthoredProps) and two scripted
    headless bots:

      - AuthoredBotA: walks to GoldCube_Hub_0's authored position (10, 0.22, 10) — deterministically
        adopted id 1006 (see AuthoredPropId below; ids are assigned by sorting adopted
        NetworkedProp descendants by node path), grabs it, then throws it ~2.5s after the grab lands.
      - AuthoredBotB: walks to (and idles near) the same spot, but its earliest-grab clock is
        set so far in the future it never actually grabs — a pure independent witness to A's
        throw, proving convergence across two separate peers rather than just one bot's own
        view (mirrors Run-ThrowTest.ps1's ThrowBotA/B pattern).

    Each bot logs one JSONL sample per tick (BotHarness), including every prop's current
    holder and position via PropManager.AllProps() — which walks BOTH the runtime-spawn root
    and the adopted-authored dictionary, so an authored prop shows up in the log exactly like
    a spawned one. Asserts, within generously-buffered windows:

      1. Holder clears  - after A's throw, both A's and B's own views show the prop unheld (0).
      2. Prop travels    - the prop's position genuinely changes across the flight window.
      3. Settle converges - the prop comes to rest at the SAME position on both A's and B's
                             peers (server-authoritative settle, broadcast reliably to everyone).

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [int]$Port = 7819,
    [double]$DurationSec = 16,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# GoldCube_Hub_0's authored position in BubbleTest.tscn; its deterministically-adopted id (see
# PropManager.AdoptAuthoredProps: ids start at AuthoredIdBase=1000, assigned in ordinal
# node-path sort order — the three GoldCube_Blue_* and three GoldCube_Cyan_* bodies sort before
# GoldCube_Hub_0, so it is 1006). Re-derive both if the level's cube set changes.
$AuthoredPropId = 1006
$AuthoredPropPos = @(10.0, 0.22, 10.0)

Write-Host "=== authored-props: adopted (never-spawned) prop carry/throw convergence test ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$procs = @()
try {
    Write-Host "[1/3] launching dedicated server (bubbletest world) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "authoredprop.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "bubbletest") "authoredprop.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    Write-Host "[2/3] launching scripted bots against the authored prop..." -ForegroundColor Cyan

    $target = "$($AuthoredPropPos[0]),$($AuthoredPropPos[1]),$($AuthoredPropPos[2])"

    $aLog = Join-Path $script:LogDir "authoredpropA.jsonl"
    $botA = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "AuthoredBotA",
        "--log", $aLog, "--duration", $DurationSec, "--world", "bubbletest",
        "--carry-script", "$target,1.0,-1,2.5") "authoredpropA"
    $procs += $botA
    Start-Sleep -Milliseconds 300

    $bLog = Join-Path $script:LogDir "authoredpropB.jsonl"
    $botB = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "AuthoredBotB",
        "--log", $bLog, "--duration", $DurationSec, "--world", "bubbletest",
        "--carry-script", "$target,9999,-1") "authoredpropB"
    $procs += $botB

    $bots = @(
        @{ Name = "AuthoredBotA"; Proc = $botA; JsonLog = $aLog }
        @{ Name = "AuthoredBotB"; Proc = $botB; JsonLog = $bLog }
    )

    $deadline = (Get-Date).AddSeconds($DurationSec + 60)
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

Write-Host "[3/3] verifying authored-prop carry/throw convergence..." -ForegroundColor Cyan

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

function In-Window($Sample, [double]$FromSec, [double]$ToSec) {
    return ($Sample.LocalElapsedSec -ge $FromSec) -and ($Sample.LocalElapsedSec -le $ToSec)
}

$samplesA = Get-Samples $aLog
$samplesB = Get-Samples $bLog

$failures = New-Object System.Collections.Generic.List[string]

# --- 0: the authored prop is actually visible at all (proves adoption, not just presence) -----
$firstA = $samplesA | Select-Object -First 1
if ($null -eq (Get-Prop $firstA $AuthoredPropId)) {
    $failures.Add("bot A never sees adopted prop $AuthoredPropId at all - AdoptAuthoredProps may not have run")
}

# --- 1: holder clears after the throw ----------------------------------------------------------
$postThrowFrom = 7.0; $postThrowTo = [math]::Min(13.0, $DurationSec - 1.0)
foreach ($set in @(@{ Name = "A"; Samples = $samplesA }, @{ Name = "B"; Samples = $samplesB })) {
    $window = @($set.Samples | Where-Object { In-Window $_ $postThrowFrom $postThrowTo })
    if ($window.Count -eq 0) { $failures.Add("bot $($set.Name): no samples in post-throw window [$postThrowFrom,$postThrowTo]s"); continue }
    foreach ($s in $window) {
        $h = Get-Holder $s $AuthoredPropId
        if ($null -eq $h) { $failures.Add("bot $($set.Name) @ $($s.LocalElapsedSec)s: prop $AuthoredPropId missing from sample"); continue }
        if ($h -ne 0) {
            $failures.Add("bot $($set.Name) @ $($s.LocalElapsedSec)s: prop $AuthoredPropId holder=$h, expected 0 (loose/resting) after throw")
        }
    }
}

# --- 2: the prop genuinely travels during the flight window ------------------------------------
$flightFrom = 4.0; $flightTo = 7.0
$flightWindow = @($samplesA | Where-Object { In-Window $_ $flightFrom $flightTo })
if ($flightWindow.Count -lt 2) {
    $failures.Add("bot A: fewer than 2 samples in flight window [$flightFrom,$flightTo]s to prove motion")
} else {
    $positions = @($flightWindow | ForEach-Object { Get-Pos $_ $AuthoredPropId } | Where-Object { $null -ne $_ })
    $maxSpread = 0.0
    for ($i = 0; $i -lt $positions.Count; $i++) {
        for ($j = $i + 1; $j -lt $positions.Count; $j++) {
            $d = Dist3 $positions[$i] $positions[$j]
            if ($d -gt $maxSpread) { $maxSpread = $d }
        }
    }
    if ($maxSpread -lt 0.5) {
        $failures.Add(("bot A: prop {0} barely moved during the flight window (max spread {1:F3}m) - throw may not be simulating" -f $AuthoredPropId, $maxSpread))
    }
}

# --- 3: settle convergence ----------------------------------------------------------------------
$settleFrom = 9.0; $settleTo = [math]::Min(14.0, $DurationSec - 0.5)
$aSettle = @($samplesA | Where-Object { In-Window $_ $settleFrom $settleTo })
$bSettle = @($samplesB | Where-Object { In-Window $_ $settleFrom $settleTo })
if ($aSettle.Count -eq 0) { $failures.Add("bot A: no samples in settle window [$settleFrom,$settleTo]s") }
if ($bSettle.Count -eq 0) { $failures.Add("bot B: no samples in settle window [$settleFrom,$settleTo]s") }
if ($aSettle.Count -gt 0 -and $bSettle.Count -gt 0) {
    $aPos = Get-Pos ($aSettle | Select-Object -Last 1) $AuthoredPropId
    $bPos = Get-Pos ($bSettle | Select-Object -Last 1) $AuthoredPropId
    if ($null -eq $aPos -or $null -eq $bPos) {
        $failures.Add("prop $AuthoredPropId missing from A or B's settle-window sample")
    } else {
        $d = Dist3 $aPos $bPos
        if ($d -gt 0.15) {
            $failures.Add(("bot A vs bot B disagree on prop {0}'s settled position by {1:F3}m (> 0.15m)" -f $AuthoredPropId, $d))
        }
    }
    foreach ($s in $aSettle) {
        $p = Get-Pos $s $AuthoredPropId
        if ($null -eq $p) { continue }
        $d = Dist3 $p $aPos
        if ($d -gt 0.3) {
            $failures.Add(("bot A @ $($s.LocalElapsedSec)s: prop {0} still moving in the settle window (off by {1:F3}m from the window's last sample)" -f $AuthoredPropId, $d))
        }
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "AUTHORED-PROP TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "AUTHORED-PROP TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: authored (never-spawned) prop was grabbed, thrown, travelled, and settled to the same position across peers." -ForegroundColor Green
Write-Host ""
Write-Host "AUTHORED-PROP TEST OVERALL: PASS" -ForegroundColor Green
exit 0
