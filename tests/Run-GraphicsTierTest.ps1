<#
.SYNOPSIS
    The GraphicsQuality writer tripwire (Issue #201) — proves the boot path still assigns
    GraphicsQuality.Current deliberately, and that --graphics still overrides it.

.DESCRIPTION
    GraphicsQuality.Current spent its first months as a public property with NO writer anywhere
    in the repository: every session ever run — every playtest, every perf measurement — silently
    held the compiled-in default, and nothing crashed, logged, or looked wrong. That is the exact
    failure mode this suite exists to make loud. GraphicsSettings.Apply is now the deliberate
    writer, it runs at the top of Boot._Ready before any scene, and it prints its assignment:

        [graphics] tier <Tier> (<source>)

    That line is emitted by the writer itself at the moment of assignment, so grepping it out of
    a real boot is runtime proof the writer still runs. A refactor that deletes or bypasses the
    writer produces a boot with no such line, and run 1 goes red — instead of another silent
    multi-month regression to the initializer.

    THIS IS A PRESENCE CHECK BUILT LIKE THE REPO'S ABSENCE CHECKS: the same method must be able
    to demonstrate both arms, or it proves nothing. So it boots twice, changing exactly one thing:

      run 1 (the case)             --steam-selftest
      run 2 (the positive control) --steam-selftest --graphics high

    Run 1 asserts the settings writer's line with the HEADLESS default (Medium — the baseline
    every historical headless measurement ran; see GraphicsSettings.HeadlessDefault). Run 2
    asserts BOTH lines: the settings writer still ran, and the --graphics override printed
    "forced to High" AFTER it — the precedence tests/Run-FireNodeTest.ps1's parity proof depends
    on. If run 2 cannot find the lines, run 1's grep method is blind and its verdict worthless.

    --steam-selftest is the boot vehicle deliberately: it is the cheapest launch that passes
    through the real Boot._Ready top (where the writer lives), needs no ports, no bots, no world
    build, and exits on its own. What this suite proves is the ASSIGNMENT; the windowed ship
    default (High) and the full resolution rule are pinned engine-free by
    tests/unit/GraphicsSettingsTests.cs, which no headless launch can exercise.

    Prints "GRAPHICS-TIER-TEST OVERALL: PASS" and exits 0 when both runs agree with expectation.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [int]$BootTimeoutSec = 90
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== graphics tier writer tripwire (Issue #201) ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$failures = @()

# Boot once with the given extra args, wait for the process to finish (the selftest exits on
# its own), and return the full output path. Exit code is NOT gated here — the Steam logic
# checks have their own suite; this one only cares what the boot printed on its way in.
function Invoke-BootProbe([string]$Tag, [string[]]$ExtraArgs) {
    $out = Join-Path $script:LogDir "graphics-tier-$Tag.out.log"
    $p = Start-Godot (@("--steam-selftest") + $ExtraArgs) "graphics-tier-$Tag"
    try {
        if (-not (Wait-ForExit $p $BootTimeoutSec)) {
            Stop-Proc $p
            return $null
        }
        return $out
    }
    finally {
        Stop-Proc $p
    }
}

# --- run 1: the case. Default boot; the settings writer must have assigned the tier. ---------
Write-Host "  run 1/2: default headless boot (the writer must assign the tier)..." -ForegroundColor DarkGray
$caseOut = Invoke-BootProbe "default" @()
if ($null -eq $caseOut) {
    $failures += "default boot did not exit within ${BootTimeoutSec}s"
} elseif (-not (Select-String -Path $caseOut -Pattern "^\[graphics\] tier Medium \(headless default\)" -Quiet)) {
    $written = (Select-String -Path $caseOut -Pattern "^\[graphics\]" | Select-Object -First 3 | ForEach-Object { $_.Line }) -join "; "
    if ([string]::IsNullOrEmpty($written)) { $written = "(no [graphics] line at all)" }
    $failures += "GraphicsQuality.Current has no boot writer again (Issue #201 regressed): expected " +
                 "'[graphics] tier Medium (headless default)' in the boot log, got: $written. " +
                 "See $caseOut"
}

# --- run 2: the positive control + precedence. Same grep, a forced tier deliberately present. -
Write-Host "  run 2/2: positive control --graphics high (override must print, after the writer)..." -ForegroundColor DarkGray
$controlOut = Invoke-BootProbe "control" @("--graphics", "high")
if ($null -eq $controlOut) {
    $failures += "positive-control boot did not exit within ${BootTimeoutSec}s"
} else {
    $settingsLine = Select-String -Path $controlOut -Pattern "^\[graphics\] tier Medium \(headless default\)" | Select-Object -First 1
    $forcedLine = Select-String -Path $controlOut -Pattern "^\[graphics\] tier forced to High by --graphics" | Select-Object -First 1
    if ($null -eq $settingsLine) {
        $failures += "POSITIVE CONTROL HALF-FAILED: the settings writer's line vanished when --graphics " +
                     "was passed - the flag must OVERRIDE the writer, never replace it. See $controlOut"
    }
    if ($null -eq $forcedLine) {
        $failures += "POSITIVE CONTROL FAILED: the same grep could not find the --graphics override line, " +
                     "so run 1's presence verdict is worthless. See $controlOut"
    }
    if ($settingsLine -and $forcedLine -and ($forcedLine.LineNumber -le $settingsLine.LineNumber)) {
        $failures += "the --graphics override printed BEFORE the settings writer, so the settings file " +
                     "clobbers a forced tier and Run-FireNodeTest's parity precondition is broken. See $controlOut"
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host "  [graphics-tier] FAIL: $f" -ForegroundColor Red }
    Write-Fail "graphics tier tripwire reported $($failures.Count) failure(s); see graphics-tier-*.out.log"
}

Write-Host "GRAPHICS-TIER-TEST OVERALL: PASS" -ForegroundColor Green
Write-Host "PASS: the boot path assigns GraphicsQuality.Current deliberately, and --graphics " -ForegroundColor Green -NoNewline
Write-Host "still overrides it in the right order." -ForegroundColor Green
exit 0
