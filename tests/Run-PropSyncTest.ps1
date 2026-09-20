<#
.SYNOPSIS
    Networked-object spawn-parity test: prove every peer sees the same networked props, with the
    same server-assigned ids, kinds, and positions.

.DESCRIPTION
    Launches a headless dedicated server in the "propsync" world (the neutral CI field, where the
    server spawns a fixed set of networked test props through the PropSpawner) and N headless bot
    clients. Each bot logs the props it sees (id, kind, holder, position) once per tick. Asserts:

      1. Every bot exited cleanly.
      2. Every bot sees exactly the expected set of props (ids + kinds), proving the custom spawn
         function ran identically on every peer and late-joining bots received the existing props.
      3. Each prop's position matches the server's spawn position within tolerance on every bot.
      4. Every bot agrees with every other bot on each prop's position (cross-view parity).

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [int]$BotCount = 3,
    [double]$DurationSec = 8,
    [int]$Port = 7788,
    [double]$ToleranceMeters = 0.3,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# The server's authoritative spawn set (must match PropManager.SpawnInitialProps).
# id -> @{ Kind; Pos = @(x,y,z) }.  Kind: 0=Crate, 1=Ball.
$expected = @{
    1 = @{ Kind = 0; Pos = @(2.5, 0.5, 2.5) }
    2 = @{ Kind = 1; Pos = @(-2.5, 0.5, -2.5) }
    3 = @{ Kind = 0; Pos = @(2, 0.5, 30.5) }
}

Write-Host "=== net-objects: prop spawn-parity test ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$procs = @()
try {
    Write-Host "[1/3] launching dedicated server (propsync world) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "propsync.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "propsync") "propsync.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    Write-Host "[2/3] launching $BotCount bots for ${DurationSec}s..." -ForegroundColor Cyan
    $bots = @()
    for ($i = 1; $i -le $BotCount; $i++) {
        $jsonLog = Join-Path $script:LogDir "propbot$i.jsonl"
        $bot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "PropBot$i",
            "--log", $jsonLog, "--duration", $DurationSec, "--world", "propsync") "propbot$i"
        $bots += @{ Proc = $bot; Index = $i; JsonLog = $jsonLog }
        $procs += $bot
        Start-Sleep -Milliseconds 300
    }

    $deadline = (Get-Date).AddSeconds($DurationSec + 60)
    foreach ($b in $bots) {
        $remaining = [int]((($deadline - (Get-Date)).TotalSeconds) * 1000)
        if ($remaining -lt 1000) { $remaining = 1000 }
        if (-not $b.Proc.WaitForExit($remaining)) { Write-Fail "propbot$($b.Index) did not exit within timeout" }
    }
    foreach ($b in $bots) {
        if ($b.Proc.ExitCode -ne 0) { Write-Fail "propbot$($b.Index) exited with code $($b.Proc.ExitCode); see $($b.JsonLog)" }
    }
} finally {
    Stop-Procs $procs
}

Write-Host "[3/3] verifying prop spawn parity..." -ForegroundColor Cyan

# view[id] per bot = last-seen @{ Kind; Pos } ; collect each bot's final prop view.
$views = @{}   # botIndex -> @{ id -> @{Kind; Pos=@(x,y,z)} }
foreach ($b in $bots) {
    if (-not (Test-Path $b.JsonLog)) { Write-Fail "propbot$($b.Index) produced no log" }
    $lines = @(Get-Content $b.JsonLog | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -lt 3) { Write-Fail "propbot$($b.Index) log has only $($lines.Count) samples" }

    # Last sample that actually carries the full expected prop roster (spawn replication can land a
    # beat after the first tick); by construction it falls inside the bot's settle window.
    $view = $null
    for ($li = $lines.Count - 1; $li -ge 0; $li--) {
        $sample = $lines[$li] | ConvertFrom-Json
        if (@($sample.props).Count -ge $expected.Count) { $view = $sample.props; break }
    }
    if ($null -eq $view) { Write-Fail "propbot$($b.Index) never saw the full prop roster ($($expected.Count) props)" }

    $m = @{}
    foreach ($p in $view) { $m[[int]$p.id] = @{ Kind = [int]$p.kind; Pos = @([double]$p.x, [double]$p.y, [double]$p.z) } }
    $views[$b.Index] = $m
}

$failures = New-Object System.Collections.Generic.List[string]

function PosDist($a, $b) {
    $dx = $a[0] - $b[0]; $dy = $a[1] - $b[1]; $dz = $a[2] - $b[2]
    return [math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
}

# 2 + 3: every bot sees the expected ids/kinds at the expected positions.
foreach ($bi in $views.Keys) {
    $m = $views[$bi]
    if ($m.Count -ne $expected.Count) {
        $failures.Add("propbot$bi sees $($m.Count) props, expected $($expected.Count)")
    }
    foreach ($id in $expected.Keys) {
        if (-not $m.ContainsKey($id)) { $failures.Add("propbot$bi never saw prop $id"); continue }
        if ($m[$id].Kind -ne $expected[$id].Kind) {
            $failures.Add("propbot$bi sees prop $id kind $($m[$id].Kind), expected $($expected[$id].Kind)")
        }
        $d = PosDist $m[$id].Pos $expected[$id].Pos
        if ($d -gt $ToleranceMeters) {
            $failures.Add(("propbot{0} sees prop {1} off by {2:F3}m from spawn (> {3}m)" -f $bi, $id, $d, $ToleranceMeters))
        }
    }
}

# 4: cross-view parity — every pair of bots agrees on each prop's position.
$maxErr = 0.0
foreach ($id in $expected.Keys) {
    foreach ($bi in $views.Keys) {
        foreach ($bj in $views.Keys) {
            if ($bi -ge $bj) { continue }
            if (-not $views[$bi].ContainsKey($id) -or -not $views[$bj].ContainsKey($id)) { continue }
            $d = PosDist $views[$bi][$id].Pos $views[$bj][$id].Pos
            if ($d -gt $maxErr) { $maxErr = $d }
            if ($d -gt $ToleranceMeters) {
                $failures.Add(("propbot{0} vs propbot{1} disagree on prop {2} by {3:F3}m" -f $bi, $bj, $id, $d))
            }
        }
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "Prop spawn-parity FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}

Write-Host ("PASS: $BotCount bots all saw the same {0} networked props at identical positions (max cross-view {1:F3}m)." `
    -f $expected.Count, $maxErr) -ForegroundColor Green
exit 0
