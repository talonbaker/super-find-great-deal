<#
.SYNOPSIS
    Runs the full Watis World headless scene-test suite and reports a single pass/fail.
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

Write-Host "=== Watis World full test suite ===" -ForegroundColor White
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
        @{ Name = "Carry: networked proof";              Script = "Run-CarryNetTest.ps1" }
        @{ Name = "Netcode: step determinism";           Script = "Run-NetStepTest.ps1" }
        @{ Name = "Netcode: net-sim";                    Script = "Run-NetSimTest.ps1" }
        @{ Name = "Netcode: anti-cheat";                 Script = "Run-CheatTest.ps1" }
        @{ Name = "Netcode: arrive latch";               Script = "Run-ArriveLatchTest.ps1" }
        @{ Name = "Steam: transport logic";              Script = "Run-SteamLogicTest.ps1" }
        @{ Name = "Authored props: adopted";             Script = "Run-AuthoredPropTest.ps1" }
        @{ Name = "Reconnect: grace window";             Script = "Run-ReconnectTest.ps1" }
        @{ Name = "Telemetry: module logic";             Script = "Run-TelemetryTest.ps1" }
        @{ Name = "World: tidal-cycle phase";            Script = "Run-CycleTest.ps1" }
        @{ Name = "Avatar identity: replication";        Script = "Run-AvatarIdentityTest.ps1" }
        @{ Name = "Session: room-code gate";             Script = "Run-RoomCodeTest.ps1" }
        @{ Name = "World: night-cycle bands";            Script = "Run-NightCycleTest.ps1" }
        @{ Name = "World: run driver";                   Script = "Run-RunDriverTest.ps1" }
        @{ Name = "Net: wire-order probes";              Script = "Run-NetProbeTest.ps1" }
        @{ Name = "Flow: playthrough spine";             Script = "Run-FlowTest.ps1" }
        @{ Name = "Aim substrate: stance replication";   Script = "Run-AimTest.ps1" }
        @{ Name = "World: bubble test (BT-0)";           Script = "Run-BubbleTestWorldTest.ps1" }
        @{ Name = "Water: lake contract (W2)";           Script = "Run-WaterTest.ps1" }
        @{ Name = "Water: splash VFX + audio (W4)";      Script = "Run-WaterFxTest.ps1" }
        @{ Name = "Voice: proximity gate";               Script = "Run-VoiceGateTest.ps1" }
        @{ Name = "Host: failure routing";               Script = "Run-HostFailureTest.ps1" }
        @{ Name = "Failure states (1c)";                 Script = "Run-IncapacityTest.ps1" }
        @{ Name = "Graphics: tier writer";               Script = "Run-GraphicsTierTest.ps1" }
        @{ Name = "UI: flow screens (CORE-B1)";          Script = "Run-ScreenFlowTest.ps1" }
        @{ Name = "UI: HUD layout law (PLAY-1)";         Script = "Run-HudLayoutTest.ps1" }
        @{ Name = "Bubbles: shared counter";             Script = "Run-BubbleSyncTest.ps1" }
        @{ Name = "World: TV portal (BT-10)";            Script = "Run-TvPortalTest.ps1" }
        @{ Name = "Honk: proximity + anti-spam";         Script = "Run-HonkTest.ps1" }
        @{ Name = "Bubbles: the last one";               Script = "Run-CelebrateTest.ps1" }
        # MOVE-1 (2026-09-04): the movement lab and the bike came over from Sail. In Sail these
        # three carried headers saying they were "deliberately NOT in Run-AllTests.ps1 - a lab
        # suite for a prototype that ships nowhere". The bike is this repo's core hook and Talon
        # feel-tests it live, so that reason expired: a bike regression turns the marathon red.
        # Run-MovementPlayground takes -Headless, which is its scripted capture with no rasterizer
        # and no PNG assertion, so the marathon keeps its headless zero-interaction promise.
        @{ Name = "Movement lab: scripted run";          Script = "Run-MovementPlayground.ps1"; Args = @{ Headless = $true } }
        @{ Name = "Bike: state machine";                 Script = "Run-BikeSelfTest.ps1" }
        @{ Name = "Bike: handling model";                Script = "Run-BikeHandlingSelfTest.ps1" }

        # --- LEVEL-1 (2026-09-04): three suites ported from Sail with the level content they
        # prove. APPENDED, per the standing rule. None of them takes part in the join-index
        # spawn dealing EXCEPT the night gate, which drives bots and is therefore last of all.

        # --- LD-6 (2026-09-02): the affordance kit's scene self-test ---
        # No ports, no bots, no dedicated server, no world build - two short python runs and two
        # headless `--script` boots that instantiate scenes/dev/KitCourse.tscn and quit
        # themselves.
        #
        # The second boot is a POSITIVE CONTROL and is EXPECTED to exit 1: the suite feeds the
        # self-test a scene the generator emitted with one Wedge deliberately mirrored, and fails
        # if the self-test accepts it. That control exists because the first KitCourse.tscn
        # shipped with all 22 of its rotated pieces mirrored and the generator's own validator
        # passed it - a checker built the way its subject was built agrees with it, wrong sign
        # and all. See the script header and docs/levels/KIT.md.
        @{ Name = "Level: the kit (LD-6)";               Script = "Run-KitCourseTest.ps1" }

        # --- EGG-1 (2026-09-02): the Puffin Lab throwback ---
        # A single headless boot that instantiates scenes/game/world/puffinlab/PuffinLab.tscn,
        # runs two absence checks (each with its own positive control), measures the live
        # avatar's capsule against every station of the route, and quits itself.
        #
        # It re-measures the fit against the LIVE player model every run, which is the reason it
        # is in the marathon rather than run once by hand: the ported rooms are scaled to admit a
        # 1.20 m avatar through a 1.05 m door, and the day the player model changes height this
        # goes red instead of the easter egg going quietly impassable.
        @{ Name = "Egg: puffin lab (EGG-1)";             Script = "Run-PuffinLabTest.ps1" }

        # --- EGG-2 (2026-09-02): the night-only creature in the Bubble Test ---
        # It drives bots, so it belongs LAST among the bot suites. TWO dedicated servers on
        # 45871/45873 (both outside the 78xx block everything else here uses), one after the
        # other, never at the same time.
        #
        # IT IS A PAIR AND THAT IS THE POINT. The day half is an ABSENCE check -- no sighting at
        # noon -- and an absence proves nothing without a control, because a broken build, a bot
        # that never connected and a correctly-gated day all produce the identical empty log. The
        # midnight half is that control, in the same script, on the same tree, one flag apart, and
        # the script fails if EITHER half is missing.
        @{ Name = "Watcher: night gate";                 Script = "Run-WatcherNightGateTest.ps1" }
        # --- end LEVEL-1 ---
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
