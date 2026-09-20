<#
.SYNOPSIS
    F1 of the 2026-08-30 master review: a failed host-side connect must return the HOST to
    the Host screen, tell them hosting ended and why, and leave a working re-host button —
    while still reaping the server child and destroying the lobby.

.DESCRIPTION
    Boots res://tests/scenes/HostFailureSelfTest.tscn headless. Four phases:

      A. The routing decision as a table (Net.SessionFailureRoute) — host to Host, joiner to
         Join, the joiner's message passed through verbatim, no blank error label ever.
      B. HostMenu.tscn is built under the real engine with an error pending and asserted to
         render it, consume it, and come back idle with a wired re-host button. Its positive
         control builds the same screen with NO error pending and asserts an empty label.
      C. The whole production path, end to end and unmocked: a REAL dedicated-server child is
         spawned via LocalServerHost, adopted, and the client is pointed at a dead port. The
         real Gameplay.Fail -> ResetToOffline -> ChangeSceneToFile chain then has to land on
         the Host screen, reap the child (positive control: the pid is asserted ALIVE first)
         and leave no lobby behind.
      D. The control for C: the identical failure with IsHostFlow false must still land on the
         JOIN screen. Without it, "the host lands on Host" is equally consistent with
         "everything lands on Host", which would be a new bug wearing the fix's clothes.

    The scene prints one line per check and "HOSTFAIL-TEST OVERALL: PASS" with exit 0 when
    everything is green.

    NOT YET REGISTERED IN Run-AllTests.ps1: that file was owned by another agent during the
    session this was written (2026-08-30, packet MRF-A). It rides the marathon meanwhile as
    phase 2 of Run-ScreenFlowTest.ps1; give it its own entry when the file frees up.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== session: host-flow failure routing (F1) ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$out = Join-Path $script:LogDir "hostfail.out.log"
$err = Join-Path $script:LogDir "hostfail.err.log"
$p = Start-Process -FilePath $script:GodotExe `
    -ArgumentList @("--headless", "--path", $script:Root, "res://tests/scenes/HostFailureSelfTest.tscn") `
    -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
$null = $p.Handle

# Generous: phase C spawns a real server child and then waits out an 8 s connect watchdog,
# phase D waits out a second one, and this machine routinely runs several agents' suites at once.
if (-not (Wait-ForExit $p 300)) {
    Stop-Proc $p
    Write-Fail "host-failure self-test timed out; see hostfail.out.log"
}

if (Test-Path $out) {
    Get-Content $out | Where-Object { $_ -match "^HOSTFAIL-TEST" } | ForEach-Object {
        $color = if ($_ -match "FAIL") { "Red" } else { "Gray" }
        Write-Host "  $_" -ForegroundColor $color
    }
}

if ($p.ExitCode -ne 0) { Write-Fail "host-failure self-test reported failures (exit $($p.ExitCode))" }
if (-not (Select-String -Path $out -Pattern "HOSTFAIL-TEST OVERALL: PASS" -Quiet)) {
    Write-Fail "host-failure self-test did not print OVERALL: PASS"
}

Write-Host ""
Write-Host "PASS: a failed host connect returns the host to the Host screen, says why, offers a re-host, and still tears the session down." -ForegroundColor Green
exit 0
