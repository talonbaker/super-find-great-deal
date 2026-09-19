<#
.SYNOPSIS
    Fully automated multiplayer replication test for Watis World.

.DESCRIPTION
    Builds the project, launches one headless dedicated server and N headless bot
    clients, lets the bots walk a deterministic pattern and log their view of every
    peer's position (one JSON object per line), then asserts that:

      1. Every bot process exited cleanly (exit code 0).
      2. Every bot saw all N players (itself + N-1 others).
      3. Every bot actually moved (its own logged position travelled a minimum
         distance from spawn), proving live data was replicated, not just spawns.
      4. For every observer/subject pair, the observer's final recorded position of
         the subject matches the subject's own final self-reported position within
         a tolerance (bots stand still for the last seconds so views converge).

    Exit code 0 = PASS, non-zero = FAIL. No human interaction required.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tests\Run-MultiplayerTest.ps1
#>
[CmdletBinding()]
param(
    [int]$BotCount = 4,
    [double]$DurationSec = 15,
    [int]$Port = 7777,
    [double]$ToleranceMeters = 0.75,
    [double]$MinTravelMeters = 5.0,
    # Cross-platform Godot resolution: honour SAIL_GODOT/GODOT_BIN (CI points these at the
    # downloaded headless build), else the historical Windows console build, else the Linux
    # headless binary name. This script is standalone (it does not dot-source _Common.ps1), so
    # the fallback chain is inlined here. $IsWindows is $null on Windows PowerShell 5.1, and 5.1
    # is Windows-only, so a null value means Windows - do NOT weaken that branch.
    [string]$GodotExe = $(
        if (-not [string]::IsNullOrWhiteSpace($env:SAIL_GODOT)) { $env:SAIL_GODOT }
        elseif (-not [string]::IsNullOrWhiteSpace($env:GODOT_BIN)) { $env:GODOT_BIN }
        elseif (($null -eq $IsWindows) -or $IsWindows) { "Godot_v4.7-stable_mono_win64_console.exe" }
        else { "Godot_v4.7-stable_mono_linux.x86_64" }
    ),
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$logDir = Join-Path $PSScriptRoot "logs"

function Fail([string]$Message) {
    Write-Host ""
    Write-Host "FAIL: $Message" -ForegroundColor Red
    exit 1
}

# --- Fresh log directory -----------------------------------------------------
# Guarded like every other suite (issue #4): a -SkipBuild re-run must never destroy the
# evidence earlier suites left in tests/logs mid-diagnosis.
if (-not $SkipBuild) {
    if (Test-Path $logDir) { Remove-Item -Recurse -Force $logDir }
}
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

# --- Build & import ----------------------------------------------------------
if (-not $SkipBuild) {
    Write-Host "[1/5] dotnet build..." -ForegroundColor Cyan
    dotnet build (Join-Path $root "WatisWorld.csproj") --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { Fail "dotnet build failed (exit $LASTEXITCODE)" }

    Write-Host "[2/5] godot --import..." -ForegroundColor Cyan
    & $GodotExe --headless --path $root --import | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "godot --import failed (exit $LASTEXITCODE)" }
} else {
    Write-Host "[1/5][2/5] build skipped (-SkipBuild)" -ForegroundColor DarkGray
}

$procs = @()
try {
    # --- Launch dedicated server ---------------------------------------------
    Write-Host "[3/5] launching dedicated server on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $logDir "server.out.log"
    $server = Start-Process -FilePath $GodotExe `
        -ArgumentList @("--headless", "--path", $root, "--", "--server", "--port", $Port, "--world", "open") `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $logDir "server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle  # cache handle so ExitCode is readable after exit (PS 5.1 quirk)
    $procs += $server

    # Wait for the listen line to appear in the server log (assembly load can be slow).
    $listening = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        if ($server.HasExited) { Fail "server exited early (exit $($server.ExitCode)); see $serverOut" }
        if ((Test-Path $serverOut) -and (Select-String -Path $serverOut -Pattern "\[server\] listening" -Quiet)) {
            $listening = $true
            break
        }
    }
    if (-not $listening) { Fail "server never reported listening within 30s; see $serverOut" }
    Write-Host "        server up (pid $($server.Id))"

    # --- Launch bots -----------------------------------------------------------
    Write-Host "[4/5] launching $BotCount bots for ${DurationSec}s..." -ForegroundColor Cyan
    $bots = @()
    for ($i = 1; $i -le $BotCount; $i++) {
        $jsonLog = Join-Path $logDir "bot$i.jsonl"
        $bot = Start-Process -FilePath $GodotExe `
            -ArgumentList @("--headless", "--path", $root, "--",
                "--bot", "--address", "127.0.0.1:$Port", "--name", "Bot$i",
                "--log", $jsonLog, "--duration", $DurationSec, "--world", "open") `
            -RedirectStandardOutput (Join-Path $logDir "bot$i.out.log") `
            -RedirectStandardError (Join-Path $logDir "bot$i.err.log") `
            -PassThru -NoNewWindow
        $null = $bot.Handle  # cache handle so ExitCode is readable after exit (PS 5.1 quirk)
        $bots += @{ Proc = $bot; Index = $i; JsonLog = $jsonLog }
        $procs += $bot
        Start-Sleep -Milliseconds 300
    }

    # --- Wait for bots to finish ------------------------------------------------
    $deadline = (Get-Date).AddSeconds($DurationSec + 60)
    foreach ($b in $bots) {
        $remaining = ($deadline - (Get-Date)).TotalSeconds
        if ($remaining -lt 1) { $remaining = 1 }
        if (-not $b.Proc.WaitForExit([int]($remaining * 1000))) {
            Fail "bot$($b.Index) (pid $($b.Proc.Id)) did not exit within timeout"
        }
    }
    foreach ($b in $bots) {
        if ($b.Proc.ExitCode -ne 0) {
            Fail "bot$($b.Index) exited with code $($b.Proc.ExitCode); see $($b.JsonLog) and bot$($b.Index).out.log"
        }
    }
} finally {
    foreach ($p in $procs) {
        if ($p -and -not $p.HasExited) {
            try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch {}
        }
    }
}

# --- Parse logs & assert convergence ------------------------------------------
Write-Host "[5/5] verifying replication convergence..." -ForegroundColor Cyan

# Bots launch (and therefore exit) staggered, so the literal last sample of a
# long-lived bot may be taken after earlier bots already disconnected and were
# correctly despawned. The comparison sample is therefore the LAST sample in
# which the full roster is present - by construction that falls inside every
# bot's settle window (bots stop moving several seconds before exiting).
$final = @{}   # selfId -> @{ Index; OwnFirst; OwnLast; View = @{ peerId -> pos }; Names = @{ peerId -> name } }
foreach ($b in $bots) {
    if (-not (Test-Path $b.JsonLog)) { Fail "bot$($b.Index) produced no JSON log at $($b.JsonLog)" }
    $lines = @(Get-Content $b.JsonLog | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -lt 5) { Fail "bot$($b.Index) log has only $($lines.Count) samples" }

    $selfId = [long](($lines[0] | ConvertFrom-Json).self)

    # First sample that already contains our own player (the spawn packet from
    # the server can arrive a beat after the first log tick).
    $first = $null
    foreach ($line in $lines) {
        $sample = $line | ConvertFrom-Json
        if (@($sample.peers | Where-Object { [long]$_.id -eq $selfId }).Count -gt 0) { $first = $sample; break }
    }
    if ($null -eq $first) { Fail "bot$($b.Index) never saw its own player spawn" }

    $fullRoster = $null
    for ($li = $lines.Count - 1; $li -ge 0; $li--) {
        $sample = $lines[$li] | ConvertFrom-Json
        if (@($sample.peers).Count -eq $BotCount) { $fullRoster = $sample; break }
    }
    if ($null -eq $fullRoster) {
        Fail "bot$($b.Index) never had a sample containing all $BotCount players (it never saw the full roster)"
    }

    $entry = @{ Index = $b.Index; View = @{}; Names = @{}; OwnFirst = $null; OwnLast = $null }
    foreach ($p in $first.peers) {
        if ([long]$p.id -eq $selfId) { $entry.OwnFirst = $p }
    }
    foreach ($p in $fullRoster.peers) {
        $entry.View[[long]$p.id] = $p
        $entry.Names[[long]$p.id] = $p.name
        if ([long]$p.id -eq $selfId) { $entry.OwnLast = $p }
    }
    if ($null -eq $entry.OwnFirst -or $null -eq $entry.OwnLast) {
        Fail "bot$($b.Index) never logged its own player (self id $selfId)"
    }
    $final[$selfId] = $entry
}

$failures = New-Object System.Collections.Generic.List[string]

# 3. Everyone moved.
foreach ($selfId in $final.Keys) {
    $e = $final[$selfId]
    $dx = $e.OwnLast.x - $e.OwnFirst.x
    $dz = $e.OwnLast.z - $e.OwnFirst.z
    $travel = [math]::Sqrt($dx * $dx + $dz * $dz)
    if ($travel -lt $MinTravelMeters) {
        $failures.Add(("bot{0} only travelled {1:F2}m (< {2}m) - movement/replication suspect" -f $e.Index, $travel, $MinTravelMeters))
    }
}

# 4. Cross-view convergence: observer's view of subject vs subject's own view.
$pairCount = 0
$maxError = 0.0
foreach ($obsId in $final.Keys) {
    $obs = $final[$obsId]
    foreach ($subjId in $final.Keys) {
        $subj = $final[$subjId]
        if (-not $obs.View.ContainsKey($subjId)) {
            $failures.Add("bot$($obs.Index) never saw peer $subjId (bot$($subj.Index))")
            continue
        }
        $seen = $obs.View[$subjId]
        $own = $subj.OwnLast
        $dx = $seen.x - $own.x; $dy = $seen.y - $own.y; $dz = $seen.z - $own.z
        $dist = [math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
        $pairCount++
        if ($dist -gt $maxError) { $maxError = $dist }
        if ($dist -gt $ToleranceMeters) {
            $failures.Add(("bot{0}'s view of bot{1} is off by {2:F3}m (> {3}m tolerance)" -f $obs.Index, $subj.Index, $dist, $ToleranceMeters))
        }
        $ownName = $obs.Names[$subjId]
        if ([string]::IsNullOrEmpty($ownName) -or $ownName -ne "Bot$($subj.Index)") {
            $failures.Add("bot$($obs.Index) sees peer $subjId with name '$ownName', expected 'Bot$($subj.Index)'")
        }
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "Replication test FAILED with $($failures.Count) assertion failure(s):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host "Logs: $logDir"
    exit 1
}

Write-Host ("PASS: {0} bots, {1} observer/subject pairs checked, max position error {2:F3}m (tolerance {3}m)." `
    -f $BotCount, $pairCount, $maxError, $ToleranceMeters) -ForegroundColor Green
Write-Host "Every bot's view of every peer converged to that peer's self-reported position."
exit 0
