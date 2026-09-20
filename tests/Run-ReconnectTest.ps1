<#
.SYNOPSIS
    Runs the headless reconnect-grace-window logic checks (no Steam client required), then a
    live client-teardown scenario proving audit P1 (Task A1: a client that drops and resumes
    tears down its stale replicated nodes instead of accumulating ghosts/duplicates), extended
    (Task A2 / P2) to also carry a held prop through that same reconnect, and (Task A3 / P11)
    to check palette-color stability/non-collision alongside it.

.DESCRIPTION
    [1/2] --reconnect-selftest: pure-logic assertions over ReconnectRegistry, the server-side
    bookkeeping behind the Steam-transport client-reconnection grace window (see
    docs/superpowers/specs/2026-07-12-client-reconnection-design.md) — resume within the 60s
    window, expiry past it, one-shot consume, the periodic sweep, per-identity isolation,
    (Task A2) the captured HeldPropIds/ColorIndex round-tripping through Capture/TryConsume
    unmodified, and (Task A3) the resumed-vs-fresh color-index decision itself
    (Gameplay.ResolveColorIndex). This is the REAL, load-bearing proof of P2/P11's data-layer
    and decision-layer contracts — see the IMPORTANT note below on why the live scenario below
    cannot also be that proof.

    [2/2] Live teardown scenario (audit P1, docs/superpowers/2026-07-19-state-transition-
    audit.md): one dedicated ENet server, BotB (stays connected throughout), and BotA launched
    with --force-reconnect-at, which — partway through its run — deliberately closes its own
    connection and redials the same address, standing in for the real Steam-transport
    reconnect retry loop (Gameplay.OnServerDisconnected -> ConnectSteam) that headless CI
    cannot exercise (no live Steam relay/account, and that path is gated off for bot/practice
    clients by design — see Gameplay.cs). This drives the SAME production teardown code
    (Gameplay.TeardownReplicatedNodes, called before every redial and defensively again on the
    resumed OnConnectedToServer) that a real Steam-transport resume would run, just over the
    transport CI actually has — see LaunchOptions.ForceReconnectAtSec's doc comment and Task
    A1's report for the full CI-scope rationale. The "propsync" world seeds 3 networked test
    props specifically so a stale/duplicated PROP count is just as observable as a stale
    avatar. BotA also runs --carry-script (Task A2) to grab the seeded Crate (propId 1, the
    same "prop 1" convention other prop-carry suites already rely on) before the
    forced reconnect.

    Asserts on BotA's own JSONL view, post-resume: avatarCount == 2 (itself under its new peer
    id + BotB — no leftover ghost of its own old body, no un-freed duplicate of BotB replayed
    on top of the still-present original), propCount == 3 (no duplicated runtime props),
    dupNames empty (no Godot "@"-renamed collision — the direct symptom of a spawner replay
    colliding with a node this client never freed), heldPropId == -1 IMMEDIATELY post-resume
    (Task A2 regression check, see IMPORTANT below), and zero ObjectDisposedException in any
    client log (the live playtest crash Task A1 fixes — though note bots never instantiate a
    SandboxCamera at all, see SandboxAvatar's local-spawn branch, so this specific check cannot
    exercise the camera guard; that half of the fix is interactive-only, see Task A1's report).

    IMPORTANT (Task A2 finding, not a guess — empirically confirmed): P2's server-side capture/
    restore (Gameplay.OnPeerDisconnected's ReconnectRegistry.Capture call, OnPeerConnected's
    TryConsume + PropManager.TryRestoreHeldProp loop) is gated on a resolvable SteamID64
    (NetworkManager.SteamId64Of, non-zero only for a real SteamPeer connection — see
    Gameplay.cs). Bots here connect over plain ENet, so SteamId64Of is ALWAYS 0 for them: the
    capture/restore path structurally never fires in this harness, toggling it on/off in
    Gameplay.cs produces IDENTICAL observed behavior (verified directly — no "peer reconnected"
    server-log line ever appears in this scenario, with or without the fix). This is the SAME
    class of Steam-vs-ENet fidelity gap Task A1's report already documented for position-resume
    (and is, in fact, pre-existing and already noted in Gameplay.cs's own comments above
    _reconnects's declaration, written before this task started) — P2 inherits it because it is
    bolted onto the exact same SteamID-gated Capture/TryConsume calls, per the plan's own design.
    So: heldPropId == -1 post-resume here is the CORRECT, honest expectation (props still drop
    immediately on disconnect exactly as before — the design's explicit "today's behavior"
    bullet — this part IS transport-agnostic and true whether or not P2 exists at all). What
    this scenario actually regression-tests is that A1's teardown (specifically
    PropManager.ClientResetHeldState, which TeardownReplicatedNodes calls) still correctly
    clears BotA's client-side held-view across a reconnect even when it WAS holding something at
    drop time — a real, newly-relevant edge case now that a reconnecting bot can be a carrier.
    The actual proof of P2's capture/restore round trip (position AND held-prop-ids AND the
    first-grab-wins fairness guard, which routes through PropRegistry.SetHolder's ALREADY-
    TESTED "second_holder_rejected_while_held" invariant — see PropRegistrySelfTest.cs) is
    [1/2]'s pure-logic self-test above for the data layer, code-review + the doc comments on
    PropManager.TryRestoreHeldProp/HeldPropIdsFor and Gameplay.OnPeerDisconnected/
    OnPeerConnected for the wiring, and — like every other Steam-relay-dependent path in this
    plan — Talon's next live Steam session for the full end-to-end proof. See Task A2's report
    for the empirical evidence (RED-toggle logs) behind this finding.

    Camera IsInstanceValid guard + rebind-on-resume are NOT bot-testable (bots have no
    camera) and stay Talon's next interactive session's job, same as every other Steam-relay-
    dependent path in this plan. This is the CI-safe half of the reconnection feature's test
    split, matching Run-SteamLogicTest.ps1's precedent: real disconnect/reconnect over the
    live Steam relay needs live Steam accounts and stays a weekend manual protocol, never
    mocked here.

    P11 / Task A3 (palette-color restore on resume) inherits the EXACT same SteamID-gate
    finding as P2 above, empirically re-confirmed: BotA's own colorIdx (added to the JSONL
    this task) goes 1 -> 2 across its forced reconnect in a real run of this scenario - it
    gets a freshly dealt color, NOT its pre-drop one back, because Gameplay.ResolveColorIndex's
    resumed=true branch never fires over ENet for the same reason P2's restore never fires (see
    the IMPORTANT note above). So this scenario deliberately does NOT assert BotA's colorIdx is
    identical pre-drop/post-resume - that would be false today and would stay false until a real
    Steam-transport session exercises the resumed branch. What IS asserted, honestly: (1) BotB
    (never disconnects) keeps the exact same colorIdx for its entire session - "color stability
    across a session for a non-resumed peer", per the plan's pivot; (2) BotA's post-resume color
    never collides with BotB's - "no color double-assignment" while both are live simultaneously.
    Neither assertion proves P11 restore; both are real, valuable regression checks on their own
    terms. The actual proof of the resumed-vs-fresh decision itself is [1/2]'s pure-logic
    self-test (Gameplay.ResolveColorIndex, the one seam that decision has without a live Steam
    session - see ReconnectSelfTest.cs) plus code review of the wiring in Gameplay.OnPeerConnected.
    Exit 0 = PASS.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [int]$Port = 7778,
    [double]$DurationSec = 14,
    [double]$ForceReconnectAtSec = 5
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# Guarded: the marathon runs every child with -SkipBuild and owns the one reset at its start.
# This suite is 18th; an unguarded reset here deletes suites 1-17's logs mid-run, destroying the
# evidence for any failure that already happened. See issue #4.
if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

Write-Host "[1/2] running reconnect-grace-window logic self-test..." -ForegroundColor Cyan
$proc = Start-Godot @("--reconnect-selftest") "reconnect-selftest"
if (-not (Wait-ForExit $proc 60)) { Stop-Proc $proc; Write-Fail "reconnect self-test did not exit in time" }
if ($proc.ExitCode -ne 0) { Write-Fail "reconnect self-test exited $($proc.ExitCode); see reconnect-selftest.out.log" }

$out = Join-Path $script:LogDir "reconnect-selftest.out.log"
if (-not (Select-String -Path $out -Pattern "\[reconnect-selftest\] PASS" -Quiet)) {
    Write-Fail "reconnect self-test never printed PASS; see reconnect-selftest.out.log"
}

Write-Host ""
Write-Host "[2/2] live client-teardown scenario (audit P1: force-reconnect + BotB present; Task A2: BotA carries a crate through it)..." -ForegroundColor Cyan

$procs = @()
try {
    $serverOut = Join-Path $script:LogDir "reconnect-server.out.log"
    $server = Start-Process -FilePath $script:GodotExe `
        -ArgumentList @("--headless", "--path", $script:Root, "--", "--server", "--port", $Port, "--world", "propsync") `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "reconnect-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server

    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "reconnect-test server never reported listening; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    $botAJson = Join-Path $script:LogDir "reconnect-botA.jsonl"
    $botA = Start-Process -FilePath $script:GodotExe `
        -ArgumentList @("--headless", "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", "BotA",
            "--log", $botAJson, "--duration", $DurationSec, "--world", "propsync",
            "--carry-script", "2.5,0.5,2.5,1.0,-1",
            "--force-reconnect-at", $ForceReconnectAtSec) `
        -RedirectStandardOutput (Join-Path $script:LogDir "reconnect-botA.out.log") `
        -RedirectStandardError (Join-Path $script:LogDir "reconnect-botA.err.log") `
        -PassThru -NoNewWindow
    $null = $botA.Handle
    $procs += $botA

    $botBJson = Join-Path $script:LogDir "reconnect-botB.jsonl"
    $botB = Start-Process -FilePath $script:GodotExe `
        -ArgumentList @("--headless", "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", "BotB",
            "--log", $botBJson, "--duration", $DurationSec, "--world", "propsync") `
        -RedirectStandardOutput (Join-Path $script:LogDir "reconnect-botB.out.log") `
        -RedirectStandardError (Join-Path $script:LogDir "reconnect-botB.err.log") `
        -PassThru -NoNewWindow
    $null = $botB.Handle
    $procs += $botB

    $deadline = $DurationSec + 60
    if (-not (Wait-ForExit $botA $deadline)) { Write-Fail "BotA did not exit within ${deadline}s" }
    if (-not (Wait-ForExit $botB $deadline)) { Write-Fail "BotB did not exit within ${deadline}s" }

    if ($botA.ExitCode -ne 0) { Write-Fail "BotA exited $($botA.ExitCode); see reconnect-botA.out.log" }
    if ($botB.ExitCode -ne 0) { Write-Fail "BotB exited $($botB.ExitCode); see reconnect-botB.out.log" }
} finally {
    Stop-Procs $procs
}

# --- Assertions on BotA's post-resume view --------------------------------------------------
$botAJson = Join-Path $script:LogDir "reconnect-botA.jsonl"
if (-not (Test-Path $botAJson)) { Write-Fail "BotA produced no JSON log at $botAJson" }
$lines = @(Get-Content $botAJson | Where-Object { $_.Trim().Length -gt 0 })
if ($lines.Count -lt 3) { Write-Fail "BotA log has only $($lines.Count) samples" }

# Last sample = fully settled, well after the forced reconnect at $ForceReconnectAtSec.
$last = $lines[-1] | ConvertFrom-Json

# BotB never disconnects in this scenario - its own JSONL is what "color stability across a
# session for a non-resumed peer" (Task A3 / P11's pivot pattern) is asserted against below.
$botBJson = Join-Path $script:LogDir "reconnect-botB.jsonl"
if (-not (Test-Path $botBJson)) { Write-Fail "BotB produced no JSON log at $botBJson" }
$botBLines = @(Get-Content $botBJson | Where-Object { $_.Trim().Length -gt 0 })
if ($botBLines.Count -lt 2) { Write-Fail "BotB log has only $($botBLines.Count) samples" }
$botBFirst = $botBLines[0] | ConvertFrom-Json
$botBLast = $botBLines[-1] | ConvertFrom-Json

$failures = New-Object System.Collections.Generic.List[string]

if ([int]$botBFirst.colorIdx -ne [int]$botBLast.colorIdx) {
    $failures.Add("BotB colorIdx changed across its own session ($($botBFirst.colorIdx) -> $($botBLast.colorIdx)) - BotB never disconnects, so its dealt palette color must never change ('color stability across a session for a non-resumed peer')")
}

if ([int]$last.avatarCount -ne 2) {
    $failures.Add("post-resume avatarCount = $($last.avatarCount), expected 2 (BotA + BotB) - stale/duplicate avatar node(s) left over from the pre-drop session")
}
if ([int]$last.propCount -ne 3) {
    $failures.Add("post-resume propCount = $($last.propCount), expected 3 (the propsync world's seeded props) - stale/duplicate runtime prop node(s)")
}
$dupNames = @($last.dupNames)
if ($dupNames.Count -gt 0) {
    $failures.Add("post-resume dupNames = [$($dupNames -join ',')] - Godot '@'-renamed a spawner replay that collided with a node this client never freed")
}

# Task A2 regression check (see the .DESCRIPTION's IMPORTANT note for why this is -1, not the
# held prop id): first find the sample where BotA's OWN peer id changes - the resumed ENet
# connection is assigned a brand-new id (Godot's connection handshake, unrelated to anything
# this task touches), a discrete, permanent state change unlike avatarCount, which only briefly
# dips to a non-2 value WHILE torn down. A fast localhost redial can complete inside a single
# BotHarness sampling tick (0.2s) - confirmed empirically two different ways: a marathon run of
# this suite recorded self flipping ids at the very same sample avatarCount read 2 throughout
# (the transient dip was never sampled even though the resume genuinely happened), while a
# standalone run recorded self flipping ids at a sample where avatarCount briefly read 0 (self
# updates one tick before the fresh spawn/replication catches up). So: use the self-id change as
# the robust, un-missable "redial happened" anchor, then scan FORWARD from there for the first
# sample where avatarCount has actually settled back to 2 - the true "resume complete, view
# converged" point, whether that's the same sample as the self-id change or a tick later.
$firstSelf = ($lines[0] | ConvertFrom-Json).self
$resumeIdx = -1
for ($i = 1; $i -lt $lines.Count; $i++) {
    $s = $lines[$i] | ConvertFrom-Json
    if ($s.self -ne $firstSelf) { $resumeIdx = $i; break }
}
if ($resumeIdx -lt 0) {
    $failures.Add("BotA's own peer id (self) never changed across the whole run - the forced reconnect never actually redialed, so this run proves nothing")
} else {
    $settledIdx = -1
    for ($i = $resumeIdx; $i -lt $lines.Count; $i++) {
        $s = $lines[$i] | ConvertFrom-Json
        if ([int]$s.avatarCount -eq 2) { $settledIdx = $i; break }
    }
    if ($settledIdx -lt 0) {
        $failures.Add("avatarCount never recovered to 2 after the peer-id change at sample $resumeIdx - resume never actually completed")
    } else {
        $resumeSample = $lines[$settledIdx] | ConvertFrom-Json
        # heldPropId must be -1 at this first-settled sample: the crate genuinely dropped
        # (OnPeerLeft, unconditional) and P2's SteamID-gated restore cannot fire over ENet, so
        # BotA's client-side held-view (PropManager.ClientResetHeldState, cleared by A1's
        # TeardownReplicatedNodes) must show nothing, not a stale leftover of the pre-drop hold.
        if ([int]$resumeSample.heldPropId -ne -1) {
            $failures.Add("first settled post-resume sample (index $settledIdx, self changed at index $resumeIdx) had heldPropId = $($resumeSample.heldPropId), expected -1 - the client-side held-view must not carry a stale entry across the teardown")
        }
        # Task A3 / P11: "no color double-assignment" - BotA's freshly-dealt post-resume color
        # (restore can't fire over ENet - see the .DESCRIPTION's P11 note - so this IS a fresh
        # deal, not a restore) must never collide with BotB's, while both are connected at once.
        # NOT a check that BotA got its OWN pre-drop color back (that would be false today).
        if ([int]$resumeSample.colorIdx -eq [int]$botBLast.colorIdx) {
            $failures.Add("post-resume colorIdx collision: BotA's colorIdx ($($resumeSample.colorIdx)) matches BotB's ($($botBLast.colorIdx)) while both are connected simultaneously - two live peers must never be dealt the same palette color")
        }
    }
}

foreach ($logFile in @(
    (Join-Path $script:LogDir "reconnect-botA.out.log"), (Join-Path $script:LogDir "reconnect-botA.err.log"),
    (Join-Path $script:LogDir "reconnect-botB.out.log"), (Join-Path $script:LogDir "reconnect-botB.err.log"))) {
    if ((Test-Path $logFile) -and (Select-String -Path $logFile -Pattern "ObjectDisposedException" -Quiet)) {
        $failures.Add("ObjectDisposedException found in $logFile")
    }
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "FAIL: live client-teardown scenario failed $($failures.Count) assertion(s):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host "Logs: $script:LogDir"
    exit 1
}

Write-Host ""
Write-Host "PASS: reconnect-grace-window logic checks all green (incl. P2's HeldPropIds/ColorIndex round trip and P11's resumed-vs-fresh color decision); post-resume client view is clean (avatarCount=2, propCount=3, no dup names, no stale held-view, no ObjectDisposedException, no colorIdx collision, BotB colorIdx stable across its session)." -ForegroundColor Green
exit 0
