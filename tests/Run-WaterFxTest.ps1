<#
.SYNOPSIS
    Headless test of the lake's splash VFX and audio (W4) — the pool, the routing, the
    submersion filter, the distance cull and the voice budget.

.DESCRIPTION
    Launches the WaterFxSelfTest scene headless. It drives the REAL WaterFx.Dispatch into
    the REAL SplashParticlePool against the REAL AudioServer bus graph, and measures the
    result off the live nodes rather than off the tuning functions' own opinion:

      - all five synthesized clips are real audio (peak/mean/length), 48 kHz, never looping
        (a looping one-shot would hold an SfxLab pool slot open forever)
      - the submersion lowpass lands on Sfx and on the Ambient group handle, does NOT land
        on Voice, and does not disturb the bed lane's duck compressor
      - Ensure() is idempotent — four extra calls stack no effects
      - the filter engages and disengages only at the wide-open end, so the switch itself
        is inaudible; Release hands both buses back
      - 30 back-to-back bursts produce at most 6 emitter nodes (never one per splash)
      - Amount stays pinned at 64 on every slot while AmountRatio is what moves — writing
        Amount reallocates the buffer and restarts the system
      - a splash 100 m out is not emitted and IS counted as culled
      - the night weight reaches the live emitter (count and lifetime down, droplet size
        unchanged) and the emission cap reaches the live draw material
      - 12 simultaneous splashes produce at most 4 water voices and refuse the rest

    Every "X is denied/absent" check is paired with a positive control proving the harness
    can observe X being granted — a diagnostic that only ever reports "absent" proves
    nothing. What no headless run can say is whether any of it LOOKS right; that is judged
    from the locked-camera capture sequences under
    docs/superpowers/status/2026-08-08-lake-w4/ and from nowhere else.

    The scene prints one line per check and "WATERFX-TEST OVERALL: PASS" with exit 0 when
    everything is green. The budget arithmetic underneath is proved more thoroughly in the
    engine-free tier: dotnet test, WaterFxTuningTests.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== lake splash VFX + audio (W4) ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$out = Join-Path $script:LogDir "waterfx.out.log"
$err = Join-Path $script:LogDir "waterfx.err.log"
$p = Start-Process -FilePath $script:GodotExe `
    -ArgumentList @("--headless", "--path", $script:Root, "res://tests/scenes/WaterFxSelfTest.tscn") `
    -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
$null = $p.Handle

if (-not (Wait-ForExit $p 120)) {
    Stop-Proc $p
    Write-Fail "waterfx self-test timed out"
}

foreach ($log in @($out, $err)) {
    if (Test-Path $log) {
        Get-Content $log | Where-Object { $_ -match "^\[waterfx-selftest\] FAIL|^WATERFX-TEST" } | ForEach-Object {
            $color = if ($_ -match "FAIL") { "Red" } else { "Gray" }
            Write-Host "  $_" -ForegroundColor $color
        }
    }
}

if ($p.ExitCode -ne 0) { Write-Fail "waterfx self-test reported failures (exit $($p.ExitCode)); see waterfx.out.log" }
if (-not (Select-String -Path $out -Pattern "WATERFX-TEST OVERALL: PASS" -Quiet)) {
    Write-Fail "waterfx self-test did not print OVERALL: PASS"
}

Write-Host ""
Write-Host "PASS: lake splash VFX + audio green." -ForegroundColor Green
exit 0
