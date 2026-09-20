<#
.SYNOPSIS
    Runs the headless telemetry-module logic checks (no Firestore, no network).

.DESCRIPTION
    Launches the game with --telemetry-self-test: pure-logic assertions over TelemetryStore
    (consent persistence, stable device id), SessionMonitor (session-active marker lifecycle,
    crash classification, managed-exception pending-crash file), the PendingQueue flush
    (sends+deletes on success, leaves on failure), and payload construction for all three
    report types — with TelemetryClient's real HTTP send stubbed. This is the CI-safe half of
    the telemetry feature's test split (see docs/superpowers/specs/2026-07-13-analytics-crash-
    feedback-design.md); the real Firestore round-trip, prompt visuals, and a genuine native
    crash stay a weekend manual protocol, never mocked here. Exit 0 = PASS.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# Guarded: the marathon runs every child with -SkipBuild and owns the one reset at its start.
# This suite runs LAST, so an unguarded reset here wipes every preceding suite's logs at the
# very end of the run -- destroying the whole marathon's evidence right before anyone reads it.
# See issue #4.
if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

Write-Host "[1/1] running telemetry logic self-test..." -ForegroundColor Cyan
$proc = Start-Godot @("--telemetry-self-test") "telemetry-selftest"
if (-not (Wait-ForExit $proc 60)) { Stop-Proc $proc; Write-Fail "telemetry self-test did not exit in time" }
if ($proc.ExitCode -ne 0) { Write-Fail "telemetry self-test exited $($proc.ExitCode); see telemetry-selftest.out.log" }

$out = Join-Path $script:LogDir "telemetry-selftest.out.log"
if (-not (Select-String -Path $out -Pattern "\[telemetry-selftest\] PASS" -Quiet)) {
    Write-Fail "telemetry self-test never printed PASS; see telemetry-selftest.out.log"
}

Write-Host ""
Write-Host "PASS: telemetry logic checks all green." -ForegroundColor Green
exit 0
