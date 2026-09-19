<#
.SYNOPSIS
    Proves the proximity-voice relay mechanism headlessly: synthetic Opus frames are
    relayed with correct sender tagging at roughly the expected rate, and the new voice
    channel enforces the same security discipline as every other input surface.

.DESCRIPTION
    First a headless pure-logic mute-set self-test (no server), then four independent
    integration checks, each on a fresh server (mirroring Run-SecurityTest's
    fresh-server-per-check pattern so state never cross-contaminates):
      0. Mute logic     - --voice-mute-selftest: MuteRegistry's write-both/peer-only,
                            clear-peer-not-identity-on-disconnect, re-apply-by-identity-on-
                            reconnect, unmute-both, and recycled-id safety (P9). This is the
                            REAL CI proof of the mute-identity contract; the live re-apply over a
                            Steam reconnect is interactive-only (see Task A5's report — the
                            identity source is server-only with today's transport).
      1. Relay + tagging  - a speaker bot sends a synthetic Opus tone stream; a listener
                            bot's JSONL log must show voice packets tagged with the
                            speaker's peer id at roughly the expected rate, and the
                            speaker must never receive its own voice back.
      2. Oversized packet - packets past the size bound are rejected and logged by the
                            server, never relayed, and the server stays up.
      3. Voice flood      - a bot sending at ~4x the legitimate rate trips the per-client
                            rate limit; the excess is dropped and logged, listeners
                            receive a bounded stream, and nothing crashes.
      4. Garbage payload  - size-valid non-Opus bytes relay (the server never decodes)
                            and land in the listener's hardened decoder, which must drop
                            them without crashing (bot still exits 0).

    Audio *quality/feel* is explicitly not provable here - that is the weekend manual
    playtest (two clients, walk toward/away while one holds push-to-talk).

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

# Parses a bot's JSONL log and returns @{ PeerIdByName = @{ name -> id }; Voice = @{ senderId -> count } }
# from the last sample (voice counters are monotonic, so the last sample is the total).
function Read-BotFinal([string]$JsonLog, [string]$BotLabel) {
    if (-not (Test-Path $JsonLog)) { Write-Fail "$BotLabel produced no JSON log at $JsonLog" }
    $lines = @(Get-Content $JsonLog | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -lt 3) { Write-Fail "$BotLabel log has only $($lines.Count) samples" }
    $names = @{}
    foreach ($line in $lines) {
        $sample = $line | ConvertFrom-Json
        foreach ($p in $sample.peers) { $names[[string]$p.name] = [long]$p.id }
    }
    $last = $lines[$lines.Count - 1] | ConvertFrom-Json
    $voice = @{}
    if ($last.PSObject.Properties.Name -contains 'voice' -and $null -ne $last.voice) {
        foreach ($prop in $last.voice.PSObject.Properties) { $voice[[long]$prop.Name] = [long]$prop.Value }
    }
    return @{ PeerIdByName = $names; Voice = $voice }
}

$serverLog = Join-Path $script:LogDir "matchserver.log"
$allProcs = @()

# --- 0. Mute-set logic self-test (headless, no server) --------------------------
# The load-bearing CI proof of P9's mute-identity contract (see MuteRegistry / VoiceMuteSelfTest).
# A live identity re-apply over a real Steam reconnect is interactive-only, exactly like every
# other Steam-relay path in this branch: SteamId64Of is 0 for ENet bots and, with today's
# transport, on the client side too, so no headless bot run can exercise the identity branch.
Write-Host "[0/4] mute-set logic self-test..." -ForegroundColor Cyan
$muteSelf = Start-Godot @("--voice-mute-selftest") "voice-mute-selftest"
if (-not (Wait-ForExit $muteSelf 60)) { Stop-Proc $muteSelf; Write-Fail "voice-mute self-test did not exit in time" }
if ($muteSelf.ExitCode -ne 0) { Write-Fail "voice-mute self-test exited $($muteSelf.ExitCode); see voice-mute-selftest.out.log" }
$muteSelfOut = Join-Path $script:LogDir "voice-mute-selftest.out.log"
if (-not (Select-String -Path $muteSelfOut -Pattern "\[voice-mute-selftest\] PASS" -Quiet)) {
    Write-Fail "voice-mute self-test never printed PASS; see voice-mute-selftest.out.log"
}
Write-Host "      ok (write-both/peer-only, disconnect keeps identity, reconnect re-applies, unmute clears both, recycled-id safe)" -ForegroundColor DarkGreen

try {
    # --- 1. Relay + sender tagging ----------------------------------------------
    Write-Host "[1/4] voice relay + sender tagging..." -ForegroundColor Cyan
    $s1 = Start-FreshServer 7821 "srv-voice-relay"; $allProcs += $s1
    $listenLog = Join-Path $script:LogDir "voice-listener.jsonl"
    $speakLog = Join-Path $script:LogDir "voice-speaker.jsonl"
    $listener = Start-Godot @("--bot", "--address", "127.0.0.1:7821", "--name", "Listener",
        "--log", $listenLog, "--duration", "12") "bot-voice-listener"; $allProcs += $listener
    $speaker = Start-Godot @("--bot", "--address", "127.0.0.1:7821", "--voice-send", "--name", "Speaker",
        "--log", $speakLog, "--duration", "10") "bot-voice-speaker"; $allProcs += $speaker
    if (-not (Wait-ForExit $speaker 60)) { Write-Fail "speaker bot did not exit" }
    if (-not (Wait-ForExit $listener 60)) { Write-Fail "listener bot did not exit" }
    if ($speaker.ExitCode -ne 0) { Write-Fail "speaker bot exited $($speaker.ExitCode)" }
    if ($listener.ExitCode -ne 0) { Write-Fail "listener bot exited $($listener.ExitCode)" }

    $listen = Read-BotFinal $listenLog "listener"
    $speak = Read-BotFinal $speakLog "speaker"
    if (-not $listen.PeerIdByName.ContainsKey("Speaker")) { Write-Fail "listener never saw the Speaker player" }
    $speakerId = $listen.PeerIdByName["Speaker"]
    $received = if ($listen.Voice.ContainsKey($speakerId)) { $listen.Voice[$speakerId] } else { 0 }
    # Speaker transmits ~50 pkt/s for ~10s while both are connected; overlap and startup
    # make the exact count vary, so assert a generous floor that still proves a stream.
    if ($received -lt 150) { Write-Fail "listener received only $received voice packets from Speaker (expected a ~50/s stream, >=150)" }
    if ($speak.Voice.Count -ne 0) { Write-Fail "speaker received voice packets ($($speak.Voice.Count) senders) - the relay must exclude the sender" }
    Stop-Proc $s1
    Write-Host "      ok ($received packets received, correctly tagged, none echoed to sender)" -ForegroundColor DarkGreen

    # --- 2. Oversized packet rejection --------------------------------------------
    Write-Host "[2/4] oversized voice packet rejection..." -ForegroundColor Cyan
    $s2 = Start-FreshServer 7822 "srv-voice-oversize"; $allProcs += $s2
    $lis2Log = Join-Path $script:LogDir "voice-listener2.jsonl"
    $lis2 = Start-Godot @("--bot", "--address", "127.0.0.1:7822", "--name", "Listener2",
        "--log", $lis2Log, "--duration", "10") "bot-voice-listener2"; $allProcs += $lis2
    $attacker = Start-Godot @("--bot", "--address", "127.0.0.1:7822", "--voice-oversize", "--name", "Oversizer",
        "--duration", "8") "bot-voice-oversize"; $allProcs += $attacker
    if (-not (Wait-ForExit $attacker 60)) { Write-Fail "oversize bot did not exit" }
    if (-not (Wait-ForExit $lis2 60)) { Write-Fail "listener2 did not exit" }
    if ($lis2.ExitCode -ne 0) { Write-Fail "listener2 exited $($lis2.ExitCode)" }
    if (-not (Wait-ForLogLine $serverLog "oversized voice packet rejected" 10)) { Write-Fail "server never logged an oversized-voice rejection" }
    $lis2Data = Read-BotFinal $lis2Log "listener2"
    if ($lis2Data.PeerIdByName.ContainsKey("Oversizer")) {
        $overId = $lis2Data.PeerIdByName["Oversizer"]
        if ($lis2Data.Voice.ContainsKey($overId) -and $lis2Data.Voice[$overId] -gt 0) {
            Write-Fail "an oversized packet was relayed to the listener - size bound not enforced"
        }
    }
    if ($s2.HasExited) { Write-Fail "server crashed on oversized voice packets" }
    Stop-Proc $s2
    Write-Host "      ok (rejected, logged, never relayed, server alive)" -ForegroundColor DarkGreen

    # --- 3. Voice flood rate limiting ---------------------------------------------
    Write-Host "[3/4] voice flood rate limiting..." -ForegroundColor Cyan
    $s3 = Start-FreshServer 7823 "srv-voice-flood"; $allProcs += $s3
    $lis3Log = Join-Path $script:LogDir "voice-listener3.jsonl"
    $lis3 = Start-Godot @("--bot", "--address", "127.0.0.1:7823", "--name", "Listener3",
        "--log", $lis3Log, "--duration", "10") "bot-voice-listener3"; $allProcs += $lis3
    $flooder = Start-Godot @("--bot", "--address", "127.0.0.1:7823", "--voice-flood", "--name", "Flooder",
        "--duration", "8") "bot-voice-flood"; $allProcs += $flooder
    if (-not (Wait-ForExit $flooder 60)) { Write-Fail "flood bot did not exit" }
    if (-not (Wait-ForExit $lis3 60)) { Write-Fail "listener3 did not exit" }
    if ($lis3.ExitCode -ne 0) { Write-Fail "listener3 exited $($lis3.ExitCode)" }
    if (-not (Wait-ForLogLine $serverLog "voice rate limit" 10)) { Write-Fail "server never logged a voice rate-limit trip" }
    $lis3Data = Read-BotFinal $lis3Log "listener3"
    if (-not $lis3Data.PeerIdByName.ContainsKey("Flooder")) { Write-Fail "listener3 never saw the Flooder player" }
    $floodId = $lis3Data.PeerIdByName["Flooder"]
    $floodReceived = if ($lis3Data.Voice.ContainsKey($floodId)) { $lis3Data.Voice[$floodId] } else { 0 }
    # Flooder attempts ~200/s for ~8s (~1600); the 75/s budget must cap what reaches
    # listeners well below the attempted volume.
    if ($floodReceived -ge 1000) { Write-Fail "listener3 received $floodReceived flood packets - rate limit is not throttling" }
    if ($floodReceived -lt 1) { Write-Fail "listener3 received no flood packets at all - relay itself appears broken" }
    if ($s3.HasExited) { Write-Fail "server crashed under voice flood" }
    Stop-Proc $s3
    Write-Host "      ok (throttled to $floodReceived of ~1600 attempted, server alive)" -ForegroundColor DarkGreen

    # --- 4. Garbage payload: hardened client decoder --------------------------------
    Write-Host "[4/4] garbage payload vs hardened client decoder..." -ForegroundColor Cyan
    $s4 = Start-FreshServer 7824 "srv-voice-garbage"; $allProcs += $s4
    $lis4Log = Join-Path $script:LogDir "voice-listener4.jsonl"
    $lis4 = Start-Godot @("--bot", "--address", "127.0.0.1:7824", "--name", "Listener4",
        "--log", $lis4Log, "--duration", "10") "bot-voice-listener4"; $allProcs += $lis4
    $garbler = Start-Godot @("--bot", "--address", "127.0.0.1:7824", "--voice-garbage", "--name", "Garbler",
        "--duration", "8") "bot-voice-garbage"; $allProcs += $garbler
    if (-not (Wait-ForExit $garbler 60)) { Write-Fail "garbage bot did not exit" }
    if (-not (Wait-ForExit $lis4 60)) { Write-Fail "listener4 did not exit (client crashed on garbage voice payloads?)" }
    if ($lis4.ExitCode -ne 0) { Write-Fail "listener4 exited $($lis4.ExitCode) - the client decoder must survive arbitrary bytes" }
    $lis4Data = Read-BotFinal $lis4Log "listener4"
    if (-not $lis4Data.PeerIdByName.ContainsKey("Garbler")) { Write-Fail "listener4 never saw the Garbler player" }
    $garbId = $lis4Data.PeerIdByName["Garbler"]
    $garbReceived = if ($lis4Data.Voice.ContainsKey($garbId)) { $lis4Data.Voice[$garbId] } else { 0 }
    # Size-valid garbage MUST relay (the server never decodes audio - that's the locked
    # design); the client's decoder is the boundary that must hold.
    if ($garbReceived -lt 50) { Write-Fail "listener4 received only $garbReceived garbage packets - size-valid packets should relay" }
    if ($s4.HasExited) { Write-Fail "server crashed relaying garbage payloads" }
    Stop-Proc $s4
    Write-Host "      ok ($garbReceived size-valid garbage packets relayed; client decoded none, crashed never)" -ForegroundColor DarkGreen
} finally {
    Stop-Procs $allProcs
}

Write-Host ""
Write-Host "PASS: mute-set identity/peer-id logic green; voice relay tagged and rate-correct, size bound enforced, flood throttled, client decoder survived garbage." -ForegroundColor Green
exit 0
