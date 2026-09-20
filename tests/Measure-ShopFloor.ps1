<#
.SYNOPSIS
    SHELF-1: what the dressed search room COSTS, and what it LOOKS like. Three passes, no
    assertions. NOT a registered suite -- it measures and photographs; nothing here can go red.

.DESCRIPTION
    The packet asks for numbers a suite cannot produce, because a suite that gated on them would
    be gating on a budget nobody has agreed to (REACH-1 made the same point about its own cost
    phase). This script is where those numbers come from, so they can be re-taken rather than
    quoted from a handoff forever.

    PASS "rest" -- the room doing nothing.
      A headless dedicated server in the supermarket's search room, two headless bots walking,
      and ReachCostProbe with --cost-shove-every 0 (never shove). Reports server physics frame
      time p50/p95/peak over the window, the prop population, and -- via --net-stats -- the bytes
      actually on the wire per second. This is the state a player spends most of a round in.

    PASS "shove" -- every prop in the room knocked loose every three seconds.
      The same staging with the probe's default shove. 130 authored props plus two bots, all
      latching Resting in a wave, which is a strictly harsher load than two players can apply
      (ReachCostProbe's own note). Read it as an upper bound, and read it against pass "rest"
      rather than against a budget.

    PASS "capture" -- the photographs, and the voice budget.
      A WINDOWED host (the only peer that SIMULATES the props, which SFX-1 measured is the only
      peer where two moving bodies can both report a contact) plus two WINDOWED first-person
      clients standing in a walkway looking at each other down the aisle, with the shove running.
      That single staging produces all three shots the packet asks for -- two clients in the
      search room, an aisle view, and a bin going over with its produce spilling -- because in
      first person each client's frame contains the other player AND the aisle AND, after the
      first shove, the spill.

      THE VOICE-BUDGET LINE COMES FROM A CLIENT, NOT THE HOST, and that is a fact about where
      the hook is rather than a choice: ActorFx.BudgetSummaryLine is printed by BotHarness.Finish
      and a server process never reaches it. SFX-1 recorded the same limitation. The host's own
      [sfx] fire lines are counted here as well, so the client's summary can be read as the floor
      it is.

    ENGINE FLAGS BEFORE THE BARE --, GAME FLAGS AFTER. Wrong side is swallowed and looks exactly
    like a hang (CLAUDE.md).

    One Godot at a time: this takes the machine-wide suite mutex like every registered suite, and
    stops every process by the pid it started it with.

.PARAMETER Pass
    rest | shove | capture | all
#>
[CmdletBinding()]
param(
    [ValidateSet("rest", "shove", "capture", "all")][string]$Pass = "all",
    # 7908, the same port Run-AuthoredPropTest.ps1 claims. Safe for the reason
    # Capture-RoundClock.ps1 shares 7904 with its own suite: both take the machine-wide mutex, so
    # the two can never be alive at the same moment.
    [int]$Port = 7908,
    [int]$MutexTimeoutMinutes = 120,
    [double]$WindowSec = 20,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

$CaptureDir = Join-Path $script:Root "docs/qa/2026-09-19-shelf-1"
New-Item -ItemType Directory -Path $CaptureDir -Force | Out-Null

# Search room world space: SearchRoom.tscn is instanced at x = +40, so the interior is
# x in [33, 47], z in [-5, 5]. The walkway between aisles 1 and 2 is the z-band [-2.9, -1.3],
# centre z = -2.1; the central walkway is z = 0.
$WalkwayZ = -2.1

Write-Host "=== SHELF-1: what the shop floor costs, and what it looks like (pass: $Pass) ===" -ForegroundColor White

function Start-Probe([string]$Tag, [double]$ShoveEvery) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
        "--reach-cost", $WindowSec, "--cost-shove-every", $ShoveEvery,
        "--net-stats", (Join-Path $script:LogDir "$Tag.netstats.jsonl")) `
        -RedirectStandardOutput $out `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

function Start-WalkerBot([string]$Tag, [string]$Name, [int]$DurationSec, [string]$Goto) {
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
        "--world", "supermarket", "--duration", $DurationSec,
        "--goto-script", $Goto,
        "--log", (Join-Path $script:LogDir "$Tag.jsonl")) `
        -RedirectStandardOutput (Join-Path $script:LogDir "$Tag.out.log") `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

function Report-Probe([string]$Tag, [string]$Title) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    Write-Host ""
    Write-Host "--- $Title ---" -ForegroundColor White
    if (-not (Test-Path $out)) { Write-Host "  (no log)" -ForegroundColor Red; return }
    foreach ($l in @(Get-Content $out | Where-Object { $_ -match '^\[reach-cost\] ' })) {
        Write-Host "  $l" -ForegroundColor DarkGray
    }
    foreach ($l in @(Get-Content $out | Where-Object { $_ -match '^\[props\] adopted ' })) {
        Write-Host "  $l" -ForegroundColor DarkGray
    }
    # THE CLIENT'S OWN FRAME TIME (SHELF-1). A client does not simulate a loose prop -- it lerps
    # a frozen kinematic body toward a streamed transform -- so the server's number says nothing
    # about what 130 props cost the player watching them. BotHarness samples `pms` at its 5 Hz
    # logging cadence, so these percentiles are coarser than the probe's 60 Hz ones and are
    # labelled as what they are.
    foreach ($tag in @("${Tag}A", "${Tag}B")) {
        $jl = Join-Path $script:LogDir "$tag.jsonl"
        if (-not (Test-Path $jl)) { continue }
        $ms = @(Get-Content $jl | Where-Object { $_.Trim().Length -gt 0 } |
                ForEach-Object { ($_ | ConvertFrom-Json).pms } |
                Where-Object { $null -ne $_ } | Sort-Object)
        if ($ms.Count -lt 10) { continue }
        $p50 = $ms[[int][math]::Floor(0.50 * ($ms.Count - 1))]
        $p95 = $ms[[int][math]::Floor(0.95 * ($ms.Count - 1))]
        Write-Host ("  [client] {0}: physics frame time over {1} sample(s) at 5 Hz: p50 {2:F3} ms, p95 {3:F3} ms, peak {4:F3} ms" -f
            $tag, $ms.Count, $p50, $p95, $ms[-1]) -ForegroundColor DarkGray
    }
    # Bandwidth: ENet's own counters, one JSON object per second (NetStatsLogger). Reported as
    # the mean over the seconds that had two peers connected, which is the only interval the
    # number means anything for.
    $ns = Join-Path $script:LogDir "$Tag.netstats.jsonl"
    if (-not (Test-Path $ns)) {
        Write-Host "  [net-stats] no log at $ns" -ForegroundColor Yellow
    } else {
        $rows = @(Get-Content $ns | Where-Object { $_.Trim().Length -gt 0 } |
                  ForEach-Object { try { $_ | ConvertFrom-Json } catch { } })
        $live = @($rows | Where-Object { $_.PSObject.Properties.Name -contains "peers" -and [int]$_.peers -ge 2 })
        if ($live.Count -eq 0) { $live = $rows }
        if ($live.Count -eq 0) {
            # SAY SO RATHER THAN SAY NOTHING. Measured: one run printed no bandwidth line at all
            # and the file on disk was 4 KB of perfectly good JSON -- the server had just been
            # stopped by pid and the log was still held when this read it. A reporter that goes
            # quiet is indistinguishable from a run with no traffic, which is the failure mode
            # the "mean sent 0 B/s" bug above already cost once.
            Write-Host "  [net-stats] $ns parsed to 0 row(s) -- it may still have been held by the dying server; read it directly" -ForegroundColor Yellow
        } else {
            # NetStatsLogger's own field names, read off a line rather than guessed: sent/recv are
            # BYTES in the interval and sentPkts/recvPkts the packet counts (ENet's PopStatistic
            # deltas). The first version of this reporter looked for sentBytes/recvBytes, found
            # neither, and printed a confident "mean sent 0 B/s" -- a reporter that cannot find
            # its field should say so, not report a zero.
            $sent = ($live | ForEach-Object { [double]$_.sent / [double]$_.intervalSec } | Measure-Object -Average).Average
            $recv = ($live | ForEach-Object { [double]$_.recv / [double]$_.intervalSec } | Measure-Object -Average).Average
            $spk  = ($live | ForEach-Object { [double]$_.sentPkts / [double]$_.intervalSec } | Measure-Object -Average).Average
            Write-Host ("  [net-stats] {0} two-peer second(s): mean sent {1:N0} B/s ({2:N0} pkt/s), mean recv {3:N0} B/s" -f
                $live.Count, $sent, $spk, $recv) -ForegroundColor DarkGray
        }
    }
}

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    foreach ($p in @(@{ Tag = "shelf-rest"; Shove = 0; Run = ($Pass -in @("rest", "all")) },
                     @{ Tag = "shelf-shove"; Shove = 3; Run = ($Pass -in @("shove", "all")) })) {
        if (-not $p.Run) { continue }
        Write-Host ""
        Write-Host ">>> pass '$($p.Tag)' ($WindowSec s window, shove every $($p.Shove) s)" -ForegroundColor Cyan
        $server = Start-Probe $p.Tag $p.Shove
        $procs += $server
        if (-not (Wait-ForLogLine (Join-Path $script:LogDir "$($p.Tag).out.log") "\[server\] listening" 90)) {
            Stop-Proc $server
            Write-Fail "the server never reported listening on udp/$Port for $($p.Tag)"
        }
        $botDur = [int]($WindowSec - 2)
        $b1 = Start-WalkerBot "$($p.Tag)A" "ShelfA" $botDur "40,$WalkwayZ"
        $procs += $b1
        Start-Sleep -Milliseconds 800
        $b2 = Start-WalkerBot "$($p.Tag)B" "ShelfB" $botDur "44,0"
        $procs += $b2
        foreach ($b in @($b1, $b2)) { if (-not (Wait-ForExit $b ($botDur + 60))) { Stop-Proc $b } }
        # The probe quits the server itself when its window closes.
        if (-not (Wait-ForExit $server 120)) { Stop-Proc $server }
        Start-Sleep -Milliseconds 400
    }

    if ($Pass -in @("capture", "all")) {
        Write-Host ""
        Write-Host ">>> pass 'capture' (windowed host + two windowed first-person clients)" -ForegroundColor Cyan
        # THE HOST MUST OUTLIVE THE CLIENTS. Measured: with the probe's window at 26 s and the
        # clients at 34, the host quit first, both clients lost the server and exited WITHOUT
        # reaching BotHarness.Finish -- so neither printed its [sfx] SUMMARY and the voice budget
        # came back blank while 768 [sfx] fire lines sat in each client's own log. The probe's
        # window is therefore longer than the capture, not shorter.
        $capDur = 34
        $hostOut = Join-Path $script:LogDir "shelf-cap.host.out.log"
        # WINDOWED, no --headless: ActorFx early-outs on a headless display (SFX-1), and the host
        # is the only peer that simulates the props, so it is the only one where a prop-on-prop
        # contact exists at all. The shove starts at t = 3 s of the probe's window.
        $chost = Start-Process -FilePath $script:GodotExe -ArgumentList @(
            "--path", $script:Root, "--",
            "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
            "--reach-cost", 55, "--cost-shove-every", 14, "--log-sfx", "--windowed") `
            -RedirectStandardOutput $hostOut `
            -RedirectStandardError (Join-Path $script:LogDir "shelf-cap.host.err.log") `
            -PassThru
        $null = $chost.Handle
        $procs += $chost
        if (-not (Wait-ForLogLine $hostOut "\[server\] listening" 90)) {
            Stop-Proc $chost
            Write-Fail "the windowed host never reported listening on udp/$Port"
        }

        # Two first-person clients in the SAME walkway, facing each other along it. Yaw 0 is -Z
        # (FirstPersonCamera's convention), so yaw -90 faces +X and yaw +90 faces -X: each client
        # has the other player, the length of two aisles, and the bins at the far wall in one
        # frame. Marks are taken generously and the good ones are kept -- --capture-at counts
        # from the CLIENT's own launch and the shove counts from the SERVER's, and calibrating
        # that gap is not worth doing when frames are free (Capture-RoundClock.ps1's lesson).
        function Start-Shot([string]$Name, [string]$Goto, [string]$Look, [string]$Marks) {
            $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
                "--path", $script:Root, "--",
                "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
                "--windowed", "--first-person-cam", "--log-sfx",
                "--goto-script", $Goto, "--fp-look", $Look,
                "--capture-dir", $CaptureDir, "--capture-at", $Marks,
                "--world", "supermarket", "--duration", $capDur,
                "--log", (Join-Path $script:LogDir "shelf-cap.$Name.jsonl")) `
                -RedirectStandardOutput (Join-Path $script:LogDir "shelf-cap.$Name.out.log") `
                -RedirectStandardError (Join-Path $script:LogDir "shelf-cap.$Name.err.log") `
                -PassThru
            $null = $p.Handle
            return $p
        }
        # THE SHOVE IS PUSHED OUT TO 14 s SO THERE ARE PRE-SHOVE FRAMES AT ALL. Measured: with it
        # at 6 s, the client's own 4 s and 6 s marks already showed cereal boxes in mid-air,
        # because --capture-at counts from the CLIENT's launch and the probe's cadence from the
        # HOST's, and the host is up several seconds first. The stocked aisles are half of what
        # the brief asks a capture to show ("the visual draw is the density of items covering
        # every surface"), so they get their own marks rather than being raced for.
        #
        # ShopEast looks DOWN the walkway: two clients in the search room, the length of two
        # aisles, and after a wave the spill. ShopWest stands 0.8 m off a can bay's face and
        # looks straight INTO it, which is the only framing that shows a shelf as stocked --
        # from a 1.6 m walkway every aisle is seen edge-on and the products hide behind the
        # uprights.
        $shotA = Start-Shot "ShopEast" "36,$WalkwayZ" "-90,0" "5,9,18,23,28,32"
        $procs += $shotA
        Start-Sleep -Milliseconds 1200
        $shotB = Start-Shot "ShopWest" "38.85,$WalkwayZ" "0,3" "5,9,18,23,28,32"
        $procs += $shotB

        foreach ($p in @($shotA, $shotB)) { if (-not (Wait-ForExit $p ($capDur + 60))) { Stop-Proc $p } }
        Stop-Proc $chost
        Start-Sleep -Milliseconds 400
    }
} finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

if ($Pass -in @("rest", "all"))  { Report-Probe "shelf-rest"  "AT REST (no shove)" }
if ($Pass -in @("shove", "all")) { Report-Probe "shelf-shove" "UNDER THE SHOVE (every 3 s)" }

if ($Pass -in @("capture", "all")) {
    Write-Host ""
    Write-Host "--- THE VOICE BUDGET (windowed host simulating, client summary) ---" -ForegroundColor White
    $hostSfx = @(Get-Content (Join-Path $script:LogDir "shelf-cap.host.out.log") -ErrorAction SilentlyContinue |
                 Where-Object { $_ -match '^\[sfx\] sfx ' })
    Write-Host "  host [sfx] fire lines: $($hostSfx.Count)" -ForegroundColor DarkGray
    foreach ($n in @("ShopEast", "ShopWest")) {
        $log = Join-Path $script:LogDir "shelf-cap.$n.out.log"
        if (-not (Test-Path $log)) { continue }
        $s = @(Get-Content $log | Where-Object { $_ -match '^\[sfx\] SUMMARY ' }) | Select-Object -Last 1
        Write-Host "  $n : $(if ($s) { $s } else { '(no [sfx] SUMMARY line)' })" -ForegroundColor DarkGray
    }
    Write-Host ""
    Write-Host "Frames in $CaptureDir :" -ForegroundColor White
    foreach ($shot in @(Get-ChildItem -Path $CaptureDir -Filter "*.png" -ErrorAction SilentlyContinue | Sort-Object Name)) {
        # FP-1's lesson (.claude/rules/test-suite.md): compare a capture's SIZE to one known good
        # before believing it. A few hundred bytes is a photograph of nothing.
        Write-Host ("  {0,-34} {1,9:N0} bytes" -f $shot.Name, $shot.Length)
    }
}

Write-Host ""
Write-Host "MEASURE-SHOPFLOOR: done (this script asserts nothing)." -ForegroundColor Green
exit 0
