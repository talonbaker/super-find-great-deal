<#
.SYNOPSIS
    Proves the Phase 2 security hardening headlessly: every hostile/malformed input is
    rejected and logged, and the match server never crashes or degrades for legitimate
    clients.

.DESCRIPTION
    Five checks. 1-4 each assert the process SURVIVES and LOGS the rejection; 5 then asks
    what those logs are left holding about the people who connected:
      1. Version mismatch  - a wrong-protocol client is refused; a good client still joins.
      2. Malformed auth    - an oversized/garbage auth payload is refused; server stays up.
      3. Connection flood  - a burst of raw connections is rate-limited, not accepted unbounded.
      4. Match full        - the 7th client to a 6-cap server is refused with "match full".
      5. Log hygiene       - after all of the above, matchserver.log carries per-session
                             LogIdentity tokens and no raw peer address or display name.
                             When a player hosts, this file is other people's data on their
                             disk; it rotates by size only and survives uninstall.

    (The old checks 5-7 — malformed HTTP, HTTP rate limiting, heartbeat/unregister secret
    enforcement — were properties of the retired multi-tenant matchmaking phonebook. Room
    codes now live in Steam's own lobby directory, where Valve owns that attack surface;
    there is no HTTP service of ours left to harden.)

    Each ENet check uses a fresh server so per-IP rate-limit windows never cross-contaminate.
    Exit 0 = PASS.
#>
[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# Guarded: the marathon runs every child with -SkipBuild and owns the one reset at its start.
# An unguarded reset here would delete EVERY earlier suite's logs mid-run (this suite is not first),
# destroying the evidence for any failure that already happened -- which is exactly why the
# Carry-family flake survived five marathons undiagnosed. See issue #4.
if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

function Start-FreshServer([int]$Port, [string]$Tag) {
    $server = Start-Godot @("--server", "--port", $Port, "--log-dir", $script:LogDir) $Tag
    # Register for cleanup BEFORE the readiness wait: Write-Fail exits the script, so a
    # timeout here would otherwise orphan the server (holding its UDP port) because the
    # caller never got the chance to append it to $allProcs.
    $script:allProcs += $server
    if (-not (Wait-ForLogLine (Join-Path $script:LogDir "$Tag.out.log") "\[server\] listening" 30)) {
        Write-Fail "server '$Tag' never came up; see $Tag.out.log"
    }
    return $server
}

$serverLog = Join-Path $script:LogDir "matchserver.log"
$allProcs = @()

try {
    # --- 1. Version mismatch ---------------------------------------------------
    Write-Host "[1/4] version mismatch rejection..." -ForegroundColor Cyan
    $s1 = Start-FreshServer 7811 "srv-version"; $allProcs += $s1
    $bad = Start-Godot @("--bot", "--address", "127.0.0.1:7811", "--protocol", "999",
        "--name", "OldClient", "--duration", "8") "bot-badversion"; $allProcs += $bad
    if (-not (Wait-ForExit $bad 40)) { Write-Fail "wrong-version bot did not exit" }
    if ($bad.ExitCode -eq 0) { Write-Fail "wrong-version bot connected - it should have been refused" }
    if (-not (Wait-ForLogLine $serverLog "version mismatch rejected" 10)) { Write-Fail "server did not log a version-mismatch rejection" }
    # server must still accept a correct client
    $good = Start-Godot @("--bot", "--address", "127.0.0.1:7811", "--name", "GoodClient",
        "--log", (Join-Path $script:LogDir "good1.jsonl"), "--duration", "6") "bot-good1"; $allProcs += $good
    if (-not (Wait-ForExit $good 40)) { Write-Fail "good client did not exit after a rejected peer" }
    if ($good.ExitCode -ne 0) { Write-Fail "good client failed to join after a rejected peer - server degraded" }
    Stop-Proc $s1
    Write-Host "      ok" -ForegroundColor DarkGreen

    # --- 2. Malformed auth -----------------------------------------------------
    Write-Host "[2/4] malformed auth rejection..." -ForegroundColor Cyan
    $s2 = Start-FreshServer 7812 "srv-badauth"; $allProcs += $s2
    $garbage = Start-Godot @("--bot", "--address", "127.0.0.1:7812", "--bad-auth",
        "--name", "Garbage", "--duration", "8") "bot-badauth"; $allProcs += $garbage
    if (-not (Wait-ForExit $garbage 40)) { Write-Fail "bad-auth bot did not exit" }
    if ($garbage.ExitCode -eq 0) { Write-Fail "bad-auth bot connected - malformed payload should have been refused" }
    if (-not (Wait-ForLogLine $serverLog "malformed auth rejected" 10)) { Write-Fail "server did not log a malformed-auth rejection" }
    $good2 = Start-Godot @("--bot", "--address", "127.0.0.1:7812", "--name", "GoodClient2",
        "--log", (Join-Path $script:LogDir "good2.jsonl"), "--duration", "6") "bot-good2"; $allProcs += $good2
    if (-not (Wait-ForExit $good2 40)) { Write-Fail "good client did not exit after malformed peer" }
    if ($good2.ExitCode -ne 0) { Write-Fail "good client failed to join after malformed peer - server degraded" }
    Stop-Proc $s2
    Write-Host "      ok" -ForegroundColor DarkGreen

    # --- 3. Connection flood ---------------------------------------------------
    # Fresh server with a deliberately low per-IP connection budget (--conn-limit 6),
    # then a burst of real clients from 127.0.0.1: the excess must be rate-limited, not
    # accepted, and the server must stay up. (The shipping default budget of 40/10s
    # comfortably clears the legitimate harness, which never exceeds ~11 rapid connects.)
    Write-Host "[3/4] connection-flood rate limiting..." -ForegroundColor Cyan
    $s3 = Start-Godot @("--server", "--port", 7813, "--conn-limit", 6, "--log-dir", $script:LogDir) "srv-flood"
    $allProcs += $s3
    if (-not (Wait-ForLogLine (Join-Path $script:LogDir "srv-flood.out.log") "\[server\] listening" 30)) { Write-Fail "flood server never came up" }
    $floodBots = @()
    for ($i = 1; $i -le 12; $i++) {
        $b = Start-Godot @("--bot", "--address", "127.0.0.1:7813", "--name", "Flood$i", "--duration", "4") "floodbot$i"
        $floodBots += $b; $allProcs += $b
        Start-Sleep -Milliseconds 100
    }
    foreach ($b in $floodBots) { if (-not (Wait-ForExit $b 40)) { Write-Fail "a flood bot did not exit" } }
    if (-not (Wait-ForLogLine $serverLog "rate limit: connection rejected" 5)) { Write-Fail "server did not rate-limit a burst of 12 connections against a budget of 6" }
    if ($s3.HasExited) { Write-Fail "server crashed under connection flood" }
    Stop-Proc $s3
    Write-Host "      ok" -ForegroundColor DarkGreen

    # --- 4. Match full ---------------------------------------------------------
    # Cap is Protocol.MaxPlayers = 6 (Performance Bible §1); one client over must be refused.
    Write-Host "[4/4] match-full rejection (7 clients, cap 6)..." -ForegroundColor Cyan
    $s4 = Start-FreshServer 7814 "srv-full"; $allProcs += $s4
    $fullBots = @()
    for ($i = 1; $i -le 7; $i++) {
        $b = Start-Godot @("--bot", "--address", "127.0.0.1:7814", "--name", "Full$i", "--duration", "6") "full$i"
        $fullBots += $b; $allProcs += $b
        Start-Sleep -Milliseconds 150
    }
    foreach ($b in $fullBots) { if (-not (Wait-ForExit $b 60)) { Write-Fail "a match-full bot did not exit" } }
    $rejected = @($fullBots | Where-Object { $_.ExitCode -ne 0 }).Count
    $accepted = @($fullBots | Where-Object { $_.ExitCode -eq 0 }).Count
    if (-not (Wait-ForLogLine $serverLog "match full: connection rejected" 5)) { Write-Fail "server did not log a match-full rejection" }
    if (-not (Select-String -Path $serverLog -Pattern "accepted=6" -Quiet)) { Write-Fail "server never reached the 6-player cap" }
    if ($rejected -lt 1) { Write-Fail "no client was rejected despite exceeding the cap" }
    if ($accepted -ne 6) { Write-Fail "expected exactly 6 clients to join, got $accepted" }
    if ($s4.HasExited) { Write-Fail "server crashed at the player cap" }
    Stop-Proc $s4
    Write-Host "      ok ($accepted joined, $rejected refused)" -ForegroundColor DarkGreen

    # --- 5. The log the host is left holding -----------------------------------
    # Checks 1-4 above have, by this point, driven every ServerLog identity call site there
    # is: rate limit, slot exhaustion, malformed auth, match full, version mismatch, and a
    # successful authenticate. So this is the moment the accumulated log is at its richest,
    # and the right moment to ask what it now contains about the people who connected.
    #
    # Asserted end to end rather than in a unit test on purpose: LogIdentity being correct
    # proves nothing if a call site passes the raw address past it. The wiring is the thing
    # that can silently regress, and only a real session exercises the wiring.
    Write-Host "[5/5] host log carries no raw peer addresses..." -ForegroundColor Cyan
    if (-not (Test-Path $serverLog)) { Write-Fail "no matchserver.log to inspect" }

    # Positive control FIRST: if the connect lines are missing entirely, the checks below
    # would pass vacuously — which is exactly how a guard like this rots into a no-op.
    $fromLines = @(Select-String -Path $serverLog -Pattern "from=[0-9a-f]{8}\b" -AllMatches)
    if ($fromLines.Count -lt 1) {
        # Print the evidence rather than just the verdict. "No matching lines" has at least three
        # very different causes -- a stale binary still emitting ip=, an empty peer address
        # rendering as from=none, or the log having been rotated out from under this check -- and
        # they are indistinguishable without seeing what is actually in the file.
        Write-Host "      --- peer lines actually in matchserver.log ---" -ForegroundColor Yellow
        @(Select-String -Path $serverLog -Pattern "peer=" -AllMatches | Select-Object -Last 8) |
            ForEach-Object { Write-Host "      $($_.Line)" -ForegroundColor Yellow }
        Write-Host "      --- end ---" -ForegroundColor Yellow
        Write-Fail "no 'from=<token>' lines in matchserver.log - the identity wiring is absent, not clean"
    }

    $rawAddr = @(Select-String -Path $serverLog -Pattern "\bip=" -AllMatches)
    if ($rawAddr.Count -gt 0) {
        Write-Fail "matchserver.log still writes raw peer addresses (ip=): $($rawAddr[0].Line)"
    }
    # No bare IPv4 anywhere, regardless of the field name it hides behind — this is what
    # actually catches a NEW call site that logs an address without going through LogIdentity.
    #
    # One deliberate exemption: the server's own "listening ... address=" announcement. That is
    # the host's own bind address, not a peer's — it is the operator's own machine describing
    # itself, it is what the harness and a human both need to find the session, and no third
    # party's data is in it. Every other line is fair game.
    $rawIpv4 = @(Select-String -Path $serverLog -Pattern "\b(?:\d{1,3}\.){3}\d{1,3}\b" -AllMatches |
        Where-Object { $_.Line -notmatch "\[server\] listening " })
    if ($rawIpv4.Count -gt 0) {
        Write-Fail "matchserver.log still writes peer IPs verbatim: $($rawIpv4[0].Line)"
    }
    # The status heartbeat used to append every player's typed display name. Bots above are
    # named GoodClient/Full1..7, so a surviving names=[...] would show up right here.
    $names = @(Select-String -Path $serverLog -Pattern "names=\[" -AllMatches)
    if ($names.Count -gt 0) {
        Write-Fail "status heartbeat still logs player display names: $($names[0].Line)"
    }
    Write-Host "      ok ($($fromLines.Count) pseudonymous peer lines, 0 raw addresses, 0 names)" -ForegroundColor DarkGreen

} finally {
    Stop-Procs $allProcs
}

Write-Host ""
Write-Host "PASS: all five security checks held - every hostile input was rejected and logged, the server never crashed or degraded, and the host's log names nobody." -ForegroundColor Green
exit 0
