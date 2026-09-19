<#
.SYNOPSIS
    CORE-PROG-A1 probe obligation (docs/design/2026-08-13-core-spine-spec.md §1.6/§5.4):
    the two wire mechanisms the playthrough verdict design depends on, asserted by
    measurement BEFORE any code is allowed to depend on them.

.DESCRIPTION
    One live scenario: a dedicated ENet server in the code-built "open" world with
    --net-probe-at, plus one bot. At the probe time the server emits, in ONE physics tick:

      Probe 1 (CallLocal synchronicity, authority path): mutate a field, fire a CallLocal
      RPC, and assert on the line after Rpc() returns that the local handler already ran
      AND read the post-mutation value — for the emitting node and for a SECOND node
      (cross-node local ordering). The server self-asserts and prints
      "[net-probe] callLocal PASS" plus a detail line this script re-checks.

      Probe 2 (cross-node same-channel reliable ordering, remote path): two distinct
      nodes interleave a 20-marker burst on NetCodec.RunChannel (reliable+ordered+
      CallLocal — RunDriver's exact attribute set). Every peer prints one line per
      applied marker; this script asserts the bot applied all 22 marks strictly in send
      order with the correct sending-node tags, and that the authority's own application
      order matches too.

      The 2026-08-09 lesson, measured: the same handler reads the mutated field. The
      authority must see the post-mutation values (111 then 222); the remote must see its
      own untouched 0 — the divergence that makes "handler compares incoming value
      against pre-mutated server state" a per-peer behavior difference.

    Exit 0 = PASS. No human interaction.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [int]$Port = 7881, # unique across tests/ — 7841 is Run-LoopUiTest/Run-WalletPayoutTest's
    [double]$ProbeAtSec = 6,
    [double]$BotDurationSec = 16
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

Write-Host "=== net-probe: CallLocal synchronicity + cross-node RunChannel ordering ===" -ForegroundColor White

$procs = @()
try {
    $serverOut = Join-Path $script:LogDir "net-probe-server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "open",
        "--net-probe-at", $ProbeAtSec) "net-probe-server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "net-probe server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id)), probeAt=${ProbeAtSec}s"

    $botOut = Join-Path $script:LogDir "net-probe-bot.out.log"
    $bot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "ProbeBot",
        "--duration", $BotDurationSec, "--world", "open", "--net-probe-at", $ProbeAtSec) "net-probe-bot"
    $procs += $bot

    if (-not $bot.WaitForExit([int](($BotDurationSec + 60) * 1000))) { Write-Fail "ProbeBot did not exit within timeout" }
    if ($bot.ExitCode -ne 0) { Write-Fail "ProbeBot exited with code $($bot.ExitCode); see $botOut" }
} finally {
    Stop-Procs $procs
}

Write-Host "[verify] parsing probe output..." -ForegroundColor Cyan

$failures = New-Object System.Collections.Generic.List[string]

# ---- Probe 1: the authority's own self-assertion, re-checked field by field. ---------------
$serverLines = Get-Content (Join-Path $script:LogDir "net-probe-server.out.log")
$detail = $serverLines | Where-Object { $_ -match "\[net-probe\] callLocal sync=" } | Select-Object -First 1
if (-not $detail) {
    $failures.Add("probe1: server never printed the callLocal detail line - the emit never fired (no peer connected in time?)")
} else {
    Write-Host "        $detail" -ForegroundColor DarkGray
    foreach ($want in @("sync=True", "postMutation=True", "crossNodeSync=True", "siblingPostMutation=True")) {
        if ($detail -notmatch [regex]::Escape($want)) { $failures.Add("probe1: detail line lacks '$want': $detail") }
    }
}
if (-not ($serverLines | Where-Object { $_ -match "\[net-probe\] callLocal PASS" })) {
    $failures.Add("probe1: server never printed '[net-probe] callLocal PASS'")
}

# ---- Probe 2: strict send-order application on BOTH peers. --------------------------------
# Send order: mark 1 from A, mark 2 from B (probe 1's two CallLocal marks), then the burst -
# marks 3,5,7,... from A and 4,6,8,... from B (NetOrderProbe.EmitProbes).
$totalMarks = 22
function Test-RecvOrder([string]$Who, [string[]]$Lines) {
    $recv = @($Lines | Where-Object { $_ -match "\[net-probe\] recv (NetOrderProbe[AB]) (\d+) sawState=(-?\d+)" } |
        ForEach-Object {
            if ($_ -match "\[net-probe\] recv (NetOrderProbe[AB]) (\d+) sawState=(-?\d+)") {
                [pscustomobject]@{ Node = $Matches[1]; Mark = [int]$Matches[2]; SawState = [int]$Matches[3] }
            }
        })
    if ($recv.Count -ne $totalMarks) {
        $script:failures.Add("probe2: $Who applied $($recv.Count) marks, expected $totalMarks")
        return $recv
    }
    for ($i = 0; $i -lt $recv.Count; $i++) {
        $expectMark = $i + 1
        $expectNode = if ($expectMark -le 2) { if ($expectMark -eq 1) { "NetOrderProbeA" } else { "NetOrderProbeB" } }
                      elseif (($expectMark - 3) % 2 -eq 0) { "NetOrderProbeA" } else { "NetOrderProbeB" }
        if ($recv[$i].Mark -ne $expectMark) {
            $script:failures.Add("probe2: $Who applied mark $($recv[$i].Mark) at position $i, expected $expectMark - NOT send order")
        }
        if ($recv[$i].Node -ne $expectNode) {
            $script:failures.Add("probe2: $Who mark $expectMark arrived via $($recv[$i].Node), expected $expectNode")
        }
    }
    return $recv
}

$serverRecv = Test-RecvOrder "server(authority)" $serverLines
$botLines = Get-Content (Join-Path $script:LogDir "net-probe-bot.out.log")
$botRecv = Test-RecvOrder "bot(remote)" $botLines

# ---- The 2026-08-09 lesson as a measured divergence, not an assumption. --------------------
if ($serverRecv.Count -eq $totalMarks -and $botRecv.Count -eq $totalMarks) {
    if ($serverRecv[0].SawState -ne 111) { $failures.Add("state-visibility: authority handler at mark 1 saw $($serverRecv[0].SawState), expected the post-mutation 111") }
    if ($serverRecv[1].SawState -ne 222) { $failures.Add("state-visibility: authority handler at mark 2 saw $($serverRecv[1].SawState), expected the post-mutation 222") }
    if ($botRecv[0].SawState -ne 0) { $failures.Add("state-visibility: remote handler at mark 1 saw $($botRecv[0].SawState), expected its own untouched 0") }
    if ($botRecv[1].SawState -ne 0) { $failures.Add("state-visibility: remote handler at mark 2 saw $($botRecv[1].SawState), expected its own untouched 0") }
    Write-Host "        authority saw post-mutation state (111/222); remote saw its own 0 - divergence measured" -ForegroundColor DarkGray
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "NET-PROBE TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "NET-PROBE TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: CallLocal applied synchronously with post-mutation state on the authority (both nodes); $totalMarks cross-node RunChannel marks applied in exact send order on authority AND remote; authority/remote state-visibility divergence measured (111/222 vs 0/0)." -ForegroundColor Green
Write-Host ""
Write-Host "NET-PROBE TEST OVERALL: PASS" -ForegroundColor Green
exit 0
