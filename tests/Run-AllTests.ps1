<#
.SYNOPSIS
    Runs the full Super Find Great Deal headless scene-test suite and reports a single pass/fail.
    Zero human interaction; exit 0 only if every suite passes.

.DESCRIPTION
    Builds the game once (dotnet build + godot --import), then runs every registered suite
    below in order and prints a summary block. Each Run-*.ps1 also runs on its own; pass
    -SkipBuild to skip the rebuild. Logs land in tests/logs/.

    THIS IS HALF OF "THE FULL SUITE". The other half is the Godot-free xUnit project:
        dotnet test tests/unit/SailNet.Tests.csproj
    Run both before every commit and report the raw counts of each, never a verdict.
    See .claude/rules/test-suite.md for the discipline (foreground, redirect never pipe,
    stage by path, one suite per machine at a time).

    Suite order matters: bot suites deal spawn points by join index, so a new BOT suite is
    appended at the END of the registry; a no-bot self-test may sit anywhere.

.PARAMETER MutexTimeoutMinutes
    How long to wait for the machine-wide full-suite lock (port 7818 and the log directory
    are shared). Default 30; use 120+ while several worktrees are live.
#>
[CmdletBinding()]
param(
    # Backstop for a holder that is simply still running (not crashed - the OS already recovers
    # that case via mutex abandonment) or wedged. 30 minutes comfortably exceeds an unloaded
    # full-suite run's observed wall time with room for a second run's worth of marathon load.
    [int]$MutexTimeoutMinutes = 30
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== Super Find Great Deal full test suite ===" -ForegroundColor White
Write-Host "acquiring machine-wide full-suite lock..." -ForegroundColor DarkGray
$suiteMutexWaitStart = Get-Date
$suiteMutex = Enter-SuiteMutex -WaitTimeoutMinutes $MutexTimeoutMinutes
$suiteMutexWaitedSec = [int]((Get-Date) - $suiteMutexWaitStart).TotalSeconds
if ($suiteMutexWaitedSec -gt 1) {
    Write-Host "  lock acquired after ${suiteMutexWaitedSec}s wait." -ForegroundColor DarkGray
} else {
    Write-Host "  lock acquired (uncontended)." -ForegroundColor DarkGray
}

$exitCode = 1
try {
    Reset-LogDir
    Invoke-BuildAndImport

    $suites = @(
        # Order matters: bot suites deal spawn points by join index, so a new BOT suite is
        # appended at the END; a no-bot self-test may sit anywhere. See tests/README.md.
        @{ Name = "Netcode: replication";                Script = "Run-MultiplayerTest.ps1" }
        @{ Name = "Practice: spawn/join/reap";           Script = "Run-PracticeTest.ps1" }
        @{ Name = "Security: hardening";                 Script = "Run-SecurityTest.ps1" }
        @{ Name = "Voice: proximity relay";              Script = "Run-VoiceTest.ps1" }
        @{ Name = "Hosting: local spawn";                Script = "Run-HostingTest.ps1" }
        @{ Name = "Sandbox: mechanics";                  Script = "Run-SandboxTest.ps1" }
        @{ Name = "Interaction: feel system";            Script = "Run-FeelTest.ps1" }
        @{ Name = "Net-objects: prop store";             Script = "Run-PropRegistryTest.ps1" }
        @{ Name = "Net-objects: spawn parity";           Script = "Run-PropSyncTest.ps1" }
        @{ Name = "Carry: server-authoritative";         Script = "Run-CarryTest.ps1" }
        @{ Name = "Carry: drift (hold+walk)";            Script = "Run-CarryDriftTest.ps1" }
        @{ Name = "Carry: regrab-while-loose";           Script = "Run-RegrabTest.ps1" }
        @{ Name = "Carry: throw + loose";                Script = "Run-ThrowTest.ps1" }
        @{ Name = "Carry: place + integrity";            Script = "Run-PlaceTest.ps1" }
        @{ Name = "Carry: networked proof";              Script = "Run-CarryNetTest.ps1" }
        @{ Name = "Netcode: step determinism";           Script = "Run-NetStepTest.ps1" }
        @{ Name = "Netcode: net-sim";                    Script = "Run-NetSimTest.ps1" }
        @{ Name = "Netcode: anti-cheat";                 Script = "Run-CheatTest.ps1" }
        @{ Name = "Netcode: arrive latch";               Script = "Run-ArriveLatchTest.ps1" }
        @{ Name = "Steam: transport logic";              Script = "Run-SteamLogicTest.ps1" }
        @{ Name = "Reconnect: grace window";             Script = "Run-ReconnectTest.ps1" }
        @{ Name = "Telemetry: module logic";             Script = "Run-TelemetryTest.ps1" }
        @{ Name = "World: tidal-cycle phase";            Script = "Run-CycleTest.ps1" }
        @{ Name = "Avatar identity: replication";        Script = "Run-AvatarIdentityTest.ps1" }
        @{ Name = "Session: room-code gate";             Script = "Run-RoomCodeTest.ps1" }
        @{ Name = "World: night-cycle bands";            Script = "Run-NightCycleTest.ps1" }
        @{ Name = "World: run driver";                   Script = "Run-RunDriverTest.ps1" }
        @{ Name = "Aim substrate: stance replication";   Script = "Run-AimTest.ps1" }
        @{ Name = "Voice: proximity gate";               Script = "Run-VoiceGateTest.ps1" }
        @{ Name = "Host: failure routing";               Script = "Run-HostFailureTest.ps1" }
        @{ Name = "Graphics: tier writer";               Script = "Run-GraphicsTierTest.ps1" }

        # --- BASE-1 (2026-09-19): the supermarket world ---
        # Appended, per the standing rule that a new suite goes LAST rather than into the middle
        # of the order. It takes no part in the join-index spawn dealing above -- no server, no
        # bots, no network at all -- so its position is free and the rule is what decides it.
        @{ Name = "World: supermarket (BASE-1)";         Script = "Run-SupermarketWorldTest.ps1" }

        # Appended after it, same rule. This one DOES run a server and two bots, on its own port
        # (7896), so it takes its place at the end of the network suites rather than anywhere
        # convenient.
        @{ Name = "Round: hide-seek loop (ROUND-1)";     Script = "Run-RoundLoopSmoke.ps1" }

        # --- FP-1 (2026-09-19): the first-person rig ---
        # THE ONE SUITE HERE THAT NEEDS A RENDERER. Every other entry launches --headless; this one
        # cannot, because what it tests is what a camera renders and what a camera culls, and a
        # headless process has neither. It opens a small window, runs for twelve seconds and closes
        # itself -- still zero human interaction, but it does need a desktop session, so a marathon
        # driven from a detached/headless shell will report this one red with "a missing display or
        # GPU is the first suspect" in its own words. CI does not run this file at all -- and as of
        # INT-0 (2026-09-19) there is no CI: .github/workflows/build.yml is deleted, so there is no
        # .github to be affected. FP-1's sentence is kept rather than rewritten because it records
        # what was true when the suite was written.
        # Last, per the standing rule. It takes a server and one bot client, so it DOES take part
        # in the join-index spawn dealing -- which is the other reason it goes at the end.
        @{ Name = "First person: rig + cull (FP-1)";     Script = "Run-FirstPersonTest.ps1" }

        # SFX-1 (2026-09-19), on udp/7902, and it goes AFTER the first-person suite for the same
        # reason that one goes last: it too opens a window, and for the same underlying cause
        # rather than a similar one. ActorFx.Fire early-outs on a headless instance, so a
        # headless observer hears nothing and can assert nothing -- "this suite needs a desktop
        # session" is now true of two entries, not one. It also runs two sequential phases on one
        # port and seeds forty props in the second, so it is the heaviest thing in the registry
        # and belongs where a marathon has nothing queued behind it.
        @{ Name = "Sfx: material voices (SFX-1)";        Script = "Run-MaterialSfxTest.ps1" }

        # WHAT WAS REMOVED HERE AT THE FORK (BASE-1, 2026-09-19), so a reader of an old handoff
        # can tell "deleted" from "lost": the bubble-test world, the shared bubble counter and the
        # last-bubble celebration, the TV portal, the Puffin Lab, the watcher's night gate, the
        # water contract and its splash FX, the failure states, the honk, the playthrough-flow
        # spine and its screens, the HUD layout law, the wire-order probes, the authored-prop
        # adoption proof, the movement lab and both bike suites, and the affordance-kit course.
        # Every one of them tested something this repo no longer ships.
        #
        # TWO OF THOSE ARE OWED BACK RATHER THAN GONE, and they are named because a following lane
        # will need them: the AUTHORED-PROP adoption proof belongs with SHELF-1's hundred props,
        # and Run-CarryNetTest's removed PHASE 3 -- a prop surviving its holder's teleport --
        # belongs with ROUND-1's phase-driven room changes. See the note in that script.
    )

    $results = @()
    foreach ($suite in $suites) {
        Write-Host ""
        Write-Host ">>> $($suite.Name)" -ForegroundColor White
        # try/catch: a terminating error inside a child (truncated JSONL mid-parse, a file
        # still held by a dying Godot) must count as that suite FAILING, not abort the whole
        # marathon before the remaining suites run or the summary prints.
        try {
            # Optional per-suite NAMED switches (MOVE-1), splatted from a hashtable. Absent on
            # every suite that needs none, so the common case is exactly the call it always was.
            # Assigned rather than taken from an `if` expression: an empty collection returned out
            # of a script block collapses to $null, and splatting $null passes a positional $null
            # that every one of these scripts rejects.
            $extra = @{}
            if ($suite.ContainsKey("Args")) { $extra = $suite.Args }
            & (Join-Path $PSScriptRoot $suite.Script) -SkipBuild @extra
            $ok = $LASTEXITCODE -eq 0
        } catch {
            Write-Host "    suite threw: $_" -ForegroundColor Red
            $ok = $false
        }
        $results += [pscustomobject]@{ Name = $suite.Name; Ok = $ok }
        if (-not $ok) { Write-Host "    suite FAILED" -ForegroundColor Red }
    }

    Write-Host ""
    Write-Host "=== summary ===" -ForegroundColor White
    foreach ($r in $results) {
        $tag = if ($r.Ok) { "PASS" } else { "FAIL" }
        $color = if ($r.Ok) { "Green" } else { "Red" }
        Write-Host ("  {0,-26} {1}" -f $r.Name, $tag) -ForegroundColor $color
    }

    if ($results | Where-Object { -not $_.Ok }) {
        Write-Host ""
        Write-Host "OVERALL: FAIL" -ForegroundColor Red
        $exitCode = 1
    } else {
        Write-Host ""
        Write-Host "OVERALL: PASS - all suites green." -ForegroundColor Green
        $exitCode = 0
    }
} finally {
    # Always releases, including on Write-Fail's exit-from-a-nested-function path (PowerShell
    # unwinds try/finally on `exit` even when it's called from a dot-sourced helper) - a build
    # failure or a hard-fail inside any suite must not leave the mutex held forever.
    Exit-SuiteMutex $suiteMutex
}

exit $exitCode
