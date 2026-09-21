<#
.SYNOPSIS
    Regrab-while-loose drift regression test: prove a prop grabbed a SECOND time — while
    still Loose from a throw, before the server's settle latch — tracks its holder on
    every peer instead of drifting toward the stale loose-stream target.

.DESCRIPTION
    The bug this pins down (weekend playtest report): first pickup carried perfectly;
    after a throw and a quick re-grab, the held item drifted slowly away from the player
    as they walked. Root cause: NetworkedProp.BindToHolder didn't clear _looseFollowing,
    so on every non-server peer the loose-stream lerp (toward the last, never-again-
    updated streamed transform) fought the carry anchor chase every physics tick.

    Staging, in the "propsync" world (server test props: prop 2 = Ball at -2.5,0.5,-2.5):

      - RegrabBotA: walks to the ball, grabs (clock >= 1.0s), throws 2.0s after the grab
        lands, then (--carry-regrab 2) chases the ball's live replicated position and
        re-grabs it — the retry cadence means this lands while the ball is still rolling
        whenever physics allows — then carries it on a long walk to (8, 8).
      - WitnessBotB: walks to mid-field and stands. Never grabs. Its view proves the
        held prop tracks A's avatar on an INDEPENDENT peer, where the drift was worst.

    Assertions (both bots' JSONL logs):
      1. The regrab actually happened: held-by-A, then loose, then held-by-A again.
      2. The loose window was genuinely exercised: the ball was still moving shortly
         before the re-grab (else the settle latch already cleaned up and this run
         proved nothing — fail loudly so the staging gets retuned, never silently pass).
      3. No drift: from shortly after the regrab to the end of the run, in every sample
         where A holds the ball, the ball sits within HoldRadius of A's avatar — on A's
         own peer AND on the witness. Pre-fix this fails by metres as A walks away.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [int]$Port = 7818,
    [double]$DurationSec = 26,
    # Softens A's scripted throw so the ball's roll stays well inside the field. At full strength
    # (1.0) the throw sends the Ball rolling ~46m, and the propsync ground slab is 64m across
    # (GameWorld: Vector3(64,1,64), so +/-32): from the ball's spawn at (-2.5,-2.5) the longest line
    # available is the ~48.8m diagonal, which the throw very nearly uses up. A measured run parked
    # the ball at z=31.81 -- 19cm short of the edge. Tip it over and PropManager's kill-plane recovers
    # it to its home transform in the OPPOSITE corner, stranding this bot ~44m away with no time to
    # chase it down: "never completed held -> loose -> held-again". That was the whole flake -- a coin
    # flip decided by 19cm, which is why it only ever showed up under a loaded marathon. See issue #4.
    # This is staging, not a gameplay change: LaunchOptions.CarryThrowScale defaults to 1.0, and only
    # this test fixture ever passes it.
    #
    # 0.35 is chosen so the bot is DECISIVELY faster than the ball, which is what makes the chase a
    # certainty instead of a race. ThrowForwardSpeed is 7.5 m/s and the bot walks ~3.6 m/s, so:
    #   1.00 -> ball leaves at 7.5 m/s, outruns the bot for ~8s, caught only as it decays at ~14s,
    #           by which point it has crossed ~45m and is metres from the void. Measured: 29.0/32.
    #   0.50 -> 3.75 m/s, still marginally faster than the bot; regrab drifts to 5.8s. Still a race.
    #   0.35 -> ~2.6 m/s, slower than the bot from the first frame; the gap only ever closes.
    #           Measured across runs: regrab at 5.03/5.02/5.03s, ball never past 7.7 of 32. The
    #           ~10ms spread is the point -- the staging is deterministic again.
    # Don't raise this without re-reading that table: above ~0.48 the ball outruns the bot and the
    # race comes back. Don't drop it far below either, or the ball settles before the bot arrives and
    # the Loose->Held path this suite exists to prove goes untested ("ball had already settled").
    #
    # RE-DERIVED BY FEEL-1 (2026-09-20), 0.35 -> 0.23, because the table above is a RATIO and its
    # denominator moved. Every line of it is written against "the bot walks ~3.6 m/s"; Talon's
    # ruling of 2026-09-20 brought the walk down to 2.4 m/s (MpFoundation.Game.BrowsePace) and
    # deleted sprint, so 0.35 x 7.5 = 2.6 m/s -- which that table calls "slower than the bot from
    # the first frame" -- became FASTER than the bot, and the race the table exists to abolish
    # came straight back. Measured, three standalone runs on an idle machine, all three red with
    # the documented staging string and the bot stranded at (7.28, 7.17) while the ball rolled on
    # to x = 10.59 and was still rolling when the run ended.
    #
    # 0.23 keeps the ratio the table chose rather than picking a new number: 0.35 x (2.4 / 3.6).
    # The ball leaves at ~1.7 m/s against a 2.4 m/s walk, so the gap only ever closes, which is
    # the property the whole table is about. <b>Read the ratio, not the constant</b>, if the walk
    # speed moves again.
    [double]$ThrowScale = 0.23,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

$BallId = 2 # Ball at (-2.5, 0.5, -2.5) in the propsync roster (PropManager.SpawnInitialProps)
$HoldRadius = 1.6 # carry anchor sits ~0.5m from avatar centre; generous for lerp lag

Write-Host "=== regrab: loose->held second-pickup drift regression test ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$procs = @()
try {
    Write-Host "[1/3] launching dedicated server (propsync world) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "regrab.server.out.log"
    # --carry-throw-scale goes to the SERVER, not the bot: the server owns the throw impulse
    # (PropManager.RequestThrow), and the client deliberately never predicts it. See $ThrowScale above.
    $server = Start-Godot @("--server", "--port", $Port, "--world", "propsync",
        "--carry-throw-scale", "$ThrowScale") "regrab.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    Write-Host "[2/3] launching regrab bot + witness..." -ForegroundColor Cyan

    $aLog = Join-Path $script:LogDir "regrabA.jsonl"
    $botA = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "RegrabBotA",
        "--log", $aLog, "--duration", $DurationSec, "--world", "propsync",
        "--carry-script", "-2.5,0.5,-2.5,1.0,-1,2.0",
        "--carry-regrab", "$BallId",
        "--carry-walk-to", "8,8") "regrabA"
    $procs += $botA
    Start-Sleep -Milliseconds 300

    $bLog = Join-Path $script:LogDir "regrabB.jsonl"
    $botB = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "WitnessBotB",
        "--log", $bLog, "--duration", $DurationSec, "--world", "propsync",
        "--carry-script", "1.5,0.5,1.5,9999,-1") "regrabB"
    $procs += $botB

    $bots = @(
        @{ Name = "RegrabBotA"; Proc = $botA; JsonLog = $aLog }
        @{ Name = "WitnessBotB"; Proc = $botB; JsonLog = $bLog }
    )
    $deadline = (Get-Date).AddSeconds($DurationSec + 80)
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

Write-Host "[3/3] verifying regrab tracking..." -ForegroundColor Cyan

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

function Get-Peer($Sample, [long]$PeerId) {
    $p = @($Sample.peers) | Where-Object { [long]$_.id -eq $PeerId }
    if ($null -eq $p -or @($p).Count -eq 0) { return $null }
    return ($p | Select-Object -First 1)
}

function Dist3($ax, $ay, $az, $bx, $by, $bz) {
    $dx = $ax - $bx; $dy = $ay - $by; $dz = $az - $bz
    return [math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
}

$samplesA = Get-Samples $aLog
$samplesB = Get-Samples $bLog
$aPeerId = [long]$samplesA[0].self

$failures = New-Object System.Collections.Generic.List[string]

# --- 1: the held -> loose -> held-again sequence happened (on A's own log) --------------------
$phase = 0 # 0 = awaiting first hold, 1 = awaiting loose, 2 = awaiting regrab, 3 = done
$regrabAtSec = -1.0
$lastLoose = $null
$loosePosNearRegrab = @()
foreach ($s in $samplesA) {
    $p = Get-Prop $s $BallId
    if ($null -eq $p) { continue }
    $h = [int]$p.holder
    switch ($phase) {
        0 { if ($h -eq $aPeerId) { $phase = 1 } }
        1 { if ($h -eq 0) { $phase = 2 } }
        2 {
            if ($h -eq 0) {
                # Remember the tail of the loose window for the still-moving check.
                $loosePosNearRegrab += ,@([double]$s.LocalElapsedSec, [double]$p.x, [double]$p.y, [double]$p.z)
            } elseif ($h -eq $aPeerId) {
                $phase = 3
                $regrabAtSec = [double]$s.LocalElapsedSec
            }
        }
    }
    if ($phase -eq 3) { break }
}
if ($phase -lt 3) {
    $failures.Add("bot A never completed held -> loose -> held-again for prop $BallId (reached phase $phase) - regrab staging broken")
}

# --- 2: the loose window was genuinely exercised (ball still moving near the regrab) ----------
if ($regrabAtSec -ge 0) {
    $tail = @($loosePosNearRegrab | Where-Object { $_[0] -ge ($regrabAtSec - 0.8) })
    if ($tail.Count -ge 2) {
        $first = $tail[0]; $last = $tail[$tail.Count - 1]
        $moved = Dist3 $first[1] $first[2] $first[3] $last[1] $last[2] $last[3]
        if ($moved -lt 0.02) {
            $failures.Add(("ball had already settled before the regrab (moved {0:F3}m in the final loose window) - this run never exercised the Loose->Held path; retune staging" -f $moved))
        }
    } else {
        $failures.Add("too few loose samples before the regrab to prove the window was exercised - retune staging")
    }
}

# --- 3: no drift after the regrab, on the holder's peer AND the independent witness -----------
# Each log gets the held->loose->held-again phases walked on ITS OWN samples (separate
# processes, separate clocks), and the drift gate applies only after that log's own
# observed regrab plus a settle beat (the snap-to-hand lerp needs a moment).
function Get-RegrabTime($Samples, [int]$PropId, [long]$HolderId) {
    $ph = 0
    foreach ($s in $Samples) {
        $p = Get-Prop $s $PropId
        if ($null -eq $p) { continue }
        $h = [int]$p.holder
        switch ($ph) {
            0 { if ($h -eq $HolderId) { $ph = 1 } }
            1 { if ($h -eq 0) { $ph = 2 } }
            2 { if ($h -eq $HolderId) { return [double]$s.LocalElapsedSec } }
        }
    }
    return -1.0
}

if ($regrabAtSec -ge 0) {
    foreach ($set in @(
        @{ Name = "A (holder's own view)"; Samples = $samplesA },
        @{ Name = "B (independent witness)"; Samples = $samplesB })) {
        $ownRegrab = Get-RegrabTime $set.Samples $BallId $aPeerId
        if ($ownRegrab -lt 0) {
            $failures.Add("$($set.Name): never observed the held -> loose -> held-again sequence")
            continue
        }
        $checked = 0
        $worst = 0.0
        foreach ($s in $set.Samples) {
            if ($s.LocalElapsedSec -lt ($ownRegrab + 1.0)) { continue }
            $p = Get-Prop $s $BallId
            if ($null -eq $p -or [int]$p.holder -ne $aPeerId) { continue }
            $peer = Get-Peer $s $aPeerId
            if ($null -eq $peer) { continue }
            $d = Dist3 ([double]$p.x) ([double]$p.y) ([double]$p.z) ([double]$peer.x) ([double]$peer.y) ([double]$peer.z)
            $checked++
            if ($d -gt $worst) { $worst = $d }
            if ($d -gt $HoldRadius) {
                $failures.Add(("{0} @ {1:F1}s: held ball {2:F2}m from holder (> {3}m) - drift" -f $set.Name, $s.LocalElapsedSec, $d, $HoldRadius))
            }
        }
        if ($checked -lt 5) {
            $failures.Add("$($set.Name): only $checked held-by-A samples after the regrab - not enough signal; retune staging")
        } else {
            Write-Host ("        {0}: {1} held samples checked, worst hold distance {2:F2}m" -f $set.Name, $checked, $worst)
        }
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "REGRAB-TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in ($failures | Select-Object -First 12)) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "REGRAB-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}
Write-Host "REGRAB-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
