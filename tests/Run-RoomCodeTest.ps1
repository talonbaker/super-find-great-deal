<#
.SYNOPSIS
    Room-code join capability, end to end: a code-gated server admits only joiners that
    present the matching code, and rejects everyone else.

.DESCRIPTION
    The room-code gate (Handshake v4, NetworkManager.ExpectedRoomCode) closes the audit's one
    HIGH finding — room codes and host ids are enumerable Steam lobby metadata, so a scraped
    host id could previously be joined uninvited. Before this suite the fix had NO automated
    coverage: only the CODEC was unit-tested (RoomCodeTests), and the enforcement itself was
    left to a manual Steam session.

    It does not need one. The gate is enforced inside the handshake, which is transport-
    agnostic — the same check runs over ENet — so the capability is fully exercisable headless.
    What still genuinely needs a live Steam login is the LOBBY DIRECTORY half (publishing the
    code as lobby metadata and resolving it back to a host id), which this suite does not claim
    to cover.

    Staging: one dedicated ENet server started with --room-code, then three bots in sequence:

      1. correct code   -> ADMITTED  (proves the gate doesn't lock out legitimate joiners,
                                      i.e. that a passing result isn't just "rejects everyone")
      2. wrong code     -> REJECTED
      3. no code at all -> REJECTED  (the scraped-host-id attack: knowing the address is not
                                      enough without the capability)

    Asserted from the SERVER's own log, which is the authority on who was accepted, plus the
    admitted bot's JSONL as independent confirmation it actually reached the session.

    Exit 0 = PASS. No human interaction, no Steam account.
#>
[CmdletBinding()]
param(
    [int]$Port = 7822,
    [double]$DurationSec = 6,
    [string]$Code = "ABCDEF",
    [string]$WrongCode = "WXYZQR",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== room-code join capability (code-gated server over ENet) ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
} else {
    Write-Host "  (build skipped)" -ForegroundColor DarkGray
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$failures = New-Object System.Collections.Generic.List[string]
$procs = @()
try {
    Write-Host "[1/3] launching server gated on room code '$Code' (udp/$Port)..." -ForegroundColor Cyan
    $server = Start-Godot @("--server", "--port", $Port, "--world", "open",
        "--room-code", $Code, "--log-dir", $script:LogDir) "roomcode-server"
    $procs += $server
    if (-not (Wait-ForLogLine (Join-Path $script:LogDir "roomcode-server.out.log") "\[server\] listening" 30)) {
        Write-Fail "server never reported listening"
    }
    Write-Host "        server up (pid $($server.Id))" -ForegroundColor DarkGray

    # Each bot runs to completion before the next starts, so a rejection can never be
    # misattributed to whichever bot happened to be connecting at the time.
    $cases = @(
        @{ Tag = "correct"; Args = @("--join-room-code", $Code);      Admit = $true  },
        @{ Tag = "wrong";   Args = @("--join-room-code", $WrongCode); Admit = $false },
        @{ Tag = "nocode";  Args = @();                               Admit = $false }
    )

    Write-Host "[2/3] joining with correct code, wrong code, and no code..." -ForegroundColor Cyan
    foreach ($case in $cases) {
        $jsonLog = Join-Path $script:LogDir "roomcode-$($case.Tag).jsonl"
        $botArgs = @("--bot", "--address", "127.0.0.1:$Port", "--name", "Bot-$($case.Tag)",
            "--log", $jsonLog, "--duration", $DurationSec, "--world", "open") + $case.Args
        $bot = Start-Godot $botArgs "roomcode-bot-$($case.Tag)"
        $procs += $bot
        if (-not $bot.WaitForExit([int](($DurationSec + 45) * 1000))) {
            Stop-Proc $bot
            $failures.Add("bot '$($case.Tag)' did not exit within timeout")
            continue
        }

        # A bot that got in logs samples containing its own avatar; a rejected one never does.
        # This is the client-side half of the proof, independent of the server's log.
        $sawSelf = $false
        if (Test-Path $jsonLog) {
            $lines = @(Get-Content $jsonLog | Where-Object { $_.Trim().Length -gt 0 })
            foreach ($line in $lines) {
                $s = $line | ConvertFrom-Json
                if (@($s.peers | Where-Object { [long]$_.id -eq [long]$s.self }).Count -gt 0) {
                    $sawSelf = $true; break
                }
            }
        }
        $verdict = if ($sawSelf) { "ADMITTED" } else { "REJECTED" }
        $expected = if ($case.Admit) { "ADMITTED" } else { "REJECTED" }
        Write-Host ("        {0,-8} code -> {1} (expected {2})" -f $case.Tag, $verdict, $expected) `
            -ForegroundColor $(if ($verdict -eq $expected) { "DarkGray" } else { "Red" })
        if ($verdict -ne $expected) {
            $failures.Add("bot '$($case.Tag)': expected $expected but was $verdict")
        }
    }
} finally {
    Stop-Procs $procs
}

Write-Host "[3/3] verifying the server logged each rejection..." -ForegroundColor Cyan
$serverLog = Join-Path $script:LogDir "matchserver.log"
if (-not (Test-Path $serverLog)) {
    Write-Fail "server produced no matchserver.log"
}
# Exactly the two bad joiners, and no more: a gate that also rejected the legitimate joiner
# would still "reject attackers" while being useless, so the COUNT is the assertion, not
# merely the presence of the line.
$rejects = @(Select-String -Path $serverLog -Pattern "room code mismatch rejected")
if ($rejects.Count -ne 2) {
    $failures.Add("expected exactly 2 'room code mismatch rejected' lines in matchserver.log, found $($rejects.Count)")
}
# The reject line must not echo the presented code back into the log — it is a capability,
# and a log is a lower-trust artifact than the session it protects.
foreach ($r in $rejects) {
    if ($r.Line -match "presented=$WrongCode") {
        $failures.Add("reject log leaks the presented room code verbatim: $($r.Line)")
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "ROOMCODE-TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "ROOMCODE-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: the code-gated server admitted the matching joiner and rejected both the wrong-code and no-code joiners (2 rejections logged, no code leaked)." -ForegroundColor Green
Write-Host "ROOMCODE-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
