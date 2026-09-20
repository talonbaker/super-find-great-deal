<#
.SYNOPSIS
    MEASURES host upstream with the voice proximity gate off and on, at 2 and 6 real players.
    Not a pass/fail suite and deliberately NOT in Run-AllTests.ps1 — it launches up to seven
    headless Godot processes per arm and takes several minutes. Run it when you want numbers.

.DESCRIPTION
    Every arm is a fresh dedicated server with --net-stats, and every bot holds --voice-send for
    the whole run, which is the perf audit's own stated assumption (§2.2(d): "all six talking —
    for proximity voice in co-op horror this is the normal case, not the worst").

    Arms:
      2p-spread-off / 2p-spread-on   two players 48 m apart (out of earshot of each other)
      6p-pairs-off  / 6p-pairs-on    six players as THREE PAIRS ~50 m apart — each player has
                                     exactly one audible partner. This is the realistic night
                                     layout, not a contrived worst case, and it is the only
                                     six-way spread the 64 m "open" world can hold (six mutually
                                     >40 m points do not fit in a 64 m square).
      6p-cluster-on                  six players all within a few metres, gate ON. The control
                                     that stops the headline being a lie by omission: when the
                                     group is together the gate must cost nothing, and this arm
                                     is what shows it does not silently cull a huddle.

    Numbers come from ENetConnection's own transport counters (see NetStatsLogger), averaged over
    the settled window after every bot has finished walking. The counter's ability to report
    "traffic present" — not just "traffic absent" — is what the cluster arm and the gate-off arms
    demonstrate: if it were stuck, all five arms would read alike.

    CAVEAT, stated because it changes the absolute numbers: --voice-send encodes a 220 Hz SINE,
    which Opus compresses far below what real speech costs at the same nominal bitrate. The
    RATIOS between arms are sound (identical payload on both sides of every comparison); the
    absolute Mbps is a floor, not a prediction. The script prints measured bytes-per-relayed-
    packet so the correction to a nominal ~60 B Opus frame can be made explicitly.
#>
[CmdletBinding()]
param([switch]$SkipBuild, [int]$DurationSec = 26, [int]$SettleAfterSec = 12)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

# Three pairs, each pair 4 m apart internally and ~50 m from either other pair, all inside the
# "open" world's 64 m slab (+/-32).
$PairLayout = @("-28,-20", "-24,-20", "28,-20", "24,-20", "0,28", "-4,28")
$ClusterLayout = @("-6,0", "-2,0", "2,0", "6,0", "0,4", "0,-4")
$TwoSpread = @("-24,0", "24,0")

$allProcs = @()

function Invoke-Arm([string]$Tag, [int]$Port, [string]$Gate, [string[]]$Layout) {
    $statsLog = Join-Path $script:LogDir "bw-$Tag.jsonl"
    $srv = Start-Godot @("--server", "--port", $Port, "--world", "open", "--log-dir", $script:LogDir,
        "--voice-gate", $Gate, "--net-stats", $statsLog) "bw-$Tag.server"
    $script:allProcs += $srv
    if (-not (Wait-ForLogLine (Join-Path $script:LogDir "bw-$Tag.server.out.log") "\[server\] listening" 40)) {
        Write-Fail "server for arm '$Tag' never came up"
    }

    $bots = @()
    for ($i = 0; $i -lt $Layout.Count; $i++) {
        $b = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--world", "open",
            "--duration", $DurationSec, "--voice-send", "--name", "P$i",
            "--goto-script", $Layout[$i]) "bw-$Tag.p$i"
        $bots += $b
        $script:allProcs += $b
    }
    foreach ($b in $bots) {
        if (-not (Wait-ForExit $b 180)) { Write-Fail "arm '$Tag': a bot did not exit" }
        if ($b.ExitCode -ne 0) { Write-Fail "arm '$Tag': a bot exited $($b.ExitCode)" }
    }
    Stop-Proc $srv
    Start-Sleep -Milliseconds 500

    $samples = @(Get-Content $statsLog | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_ | ConvertFrom-Json })
    # The settled window only: the first seconds are the join handshakes, the scene dump and the
    # walk-out, none of which is steady state.
    $settled = @($samples | Where-Object { [double]$_.t -ge ($SettleAfterSec * 1000) })
    if ($settled.Count -lt 5) { Write-Fail "arm '$Tag': only $($settled.Count) settled samples" }

    $peers = ($settled | Measure-Object -Property peers -Maximum).Maximum
    if ($peers -ne $Layout.Count) {
        Write-Fail "arm '$Tag': server saw $peers peers, expected $($Layout.Count) - the arm is not measuring what it claims"
    }
    $upBps = ($settled | Measure-Object -Property upBps -Average).Average
    $sentBytes = ($settled | Measure-Object -Property sent -Sum).Sum
    $relayed = ($settled | Measure-Object -Property relayed -Sum).Sum
    $gated = ($settled | Measure-Object -Property gated -Sum).Sum
    $secs = ($settled | Measure-Object -Property intervalSec -Sum).Sum

    return [pscustomobject]@{
        Arm            = $Tag
        Players        = $Layout.Count
        Gate           = $Gate
        UpMbps         = [math]::Round($upBps / 1e6, 3)
        UpKBs          = [math]::Round($sentBytes / $secs / 1024, 1)
        RelayedPerSec  = [math]::Round($relayed / $secs, 0)
        GatedPerSec    = [math]::Round($gated / $secs, 0)
        BytesPerRelay  = if ($relayed -gt 0) { [math]::Round($sentBytes / $relayed, 1) } else { 0 }
        SettledSamples = $settled.Count
    }
}

Write-Host "=== host-upstream measurement: voice proximity gate off vs on ===" -ForegroundColor White
$results = @()
try {
    $arms = @(
        @{ Tag = "2p-spread-off"; Port = 7871; Gate = "off"; Layout = $TwoSpread },
        @{ Tag = "2p-spread-on";  Port = 7872; Gate = "on";  Layout = $TwoSpread },
        @{ Tag = "6p-pairs-off";  Port = 7873; Gate = "off"; Layout = $PairLayout },
        @{ Tag = "6p-pairs-on";   Port = 7874; Gate = "on";  Layout = $PairLayout },
        @{ Tag = "6p-cluster-on"; Port = 7875; Gate = "on";  Layout = $ClusterLayout }
    )
    foreach ($arm in $arms) {
        Write-Host ("  running {0} ({1} players, gate {2})..." -f $arm.Tag, $arm.Layout.Count, $arm.Gate) -ForegroundColor Cyan
        $r = Invoke-Arm $arm.Tag $arm.Port $arm.Gate $arm.Layout
        $results += $r
        Write-Host ("    {0,-14} up {1,6} Mbps  ({2,7} KB/s)  relays/s {3,5}  gated/s {4,5}  B/relay {5}" -f `
            $r.Arm, $r.UpMbps, $r.UpKBs, $r.RelayedPerSec, $r.GatedPerSec, $r.BytesPerRelay) -ForegroundColor DarkGreen
    }
} finally {
    Stop-Procs $allProcs
}

Write-Host ""
Write-Host "=== results ===" -ForegroundColor White
$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$results | ConvertTo-Json -Depth 3 | Set-Content -Path (Join-Path $script:LogDir "voice-gate-bandwidth.json") -Encoding utf8
Write-Host ("wrote {0}" -f (Join-Path $script:LogDir "voice-gate-bandwidth.json")) -ForegroundColor DarkGray
exit 0
