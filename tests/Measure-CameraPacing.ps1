<#
.SYNOPSIS
    SICK-1: what the first-person camera actually does to the screen, per frame, per configuration.

.DESCRIPTION
    THIS SCRIPT ASSERTS NOTHING AND IS NOT REGISTERED in Run-AllTests.ps1 -- the same standing
    Measure-ShopFloor.ps1 and Probe-Capacity.ps1 have, for the same reason: a suite that went red
    on a frame time would be gating on a budget nobody has agreed to, and the only budget that
    matters here is Talon's inner ear. It PRINTS A TABLE.

    Talon, riding the MVP for the first time (2026-09-20): "The game is making me motion sick
    already."

    WHAT IT MEASURES, and why each number is in the table:

      dt p50/p95/max   Frame time. A run whose frames are steady is a different experience from
                       one whose average is identical and whose frames alternate.
      over20ms         Frames slower than 50 fps.
      zero_step        THE PHYSICS-STEP TELL, and the headline. The share of render frames on
                       which the eye did not move AT ALL while the body was walking. The avatar
                       is a CharacterBody3D stepped at 60 Hz; before this lane the lens took its
                       world position from parentage, so it moved 60 times a second however fast
                       the screen refreshed. At R Hz that predicts 1 - 60/R: about 58% at 144 Hz.
                       It is the one statistic that is robust to how fast the bot happened to be
                       walking, which is why it is the one the diagnosis turns on.
      step_mean        Mean eye movement per frame. Interpolation must NOT change this -- the
                       same walk, redistributed. If it moves, the two rows are two different
                       walks and the comparison says nothing.
      step_sd          The spread of that. A camera that moves a little every frame has a small
                       one; one that alternates "nothing" and "a whole tick" has one of the same
                       order as its mean.
      yaw_sd           The look's own half, under a constant scripted turn.

    THE STIMULUS. One windowed --first-person-cam bot, patrolling a clear lane of the holding
    room (--goto-patrol, added by this lane because every other scripted brain in the repo either
    stops on arrival or has to be holding something first -- and a held object in the near field
    is one of the things under investigation, so a rig may not smuggle one in), while the probe
    turns the look at a constant rate. Walking AND turning, because they fail differently: the
    walk is the body's 60 Hz step and the turn is the look's per-render-frame one.

    ONE GODOT AT A TIME. The machine-wide suite mutex is taken for the whole run and every
    process is stopped by the pid it was started with, never by image name (.claude/rules and
    the standing instruction: a lane's `taskkill /IM Godot*` killed another lane's marathon on
    2026-09-19). A frame-pacing measurement taken while another Godot is on the machine is not a
    measurement, which is why the mutex is not optional here the way it is for a headless suite.

.PARAMETER Configs
    Which rows to measure. Each is "<label>:<extra game args>". The default pair is the
    before/after the packet asks for.

.PARAMETER SkipBuild
    Reuse the existing build and import.
#>
[CmdletBinding()]
param(
    # 7914, HANDED OUT BY THE ORCHESTRATOR (2026-09-20) rather than computed from the ladder in
    # .claude/rules/test-suite.md. This lane first took 7913 off that table and it was FEEL-1's:
    # the table is a snapshot, and two lanes computing "the next free port" from one snapshot pick
    # the same number every time -- the failure this repo has now paid for four times (7896 x3,
    # 7899 x2, 7912 x2, and this). The bind failure presents as "the server never reported
    # listening within 30s", which is the same sentence a starved machine produces; the
    # discriminator is `Couldn't create an ENet host` in the server's .err.log.
    # Wave assignment: SOLO-1 7912, FEEL-1 7913, SICK-1 7914, ART-1 7915, PHYS-1 7916, HANDS-1 7917.
    [int]$Port = 7914,
    [double]$DurationSec = 30,
    [double]$TurnDegPerSec = 45,
    [string[]]$Configs = @(
        "before:--cam-interp 0",
        "after:--cam-interp 1"
    ),
    [string]$OutDir = "",
    [switch]$SkipBuild,
    [int]$MutexTimeoutMinutes = 120
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== SICK-1: first-person frame pacing ===" -ForegroundColor White

if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:Root "docs\qa\2026-09-20-sick-1"
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$rows = @()
try {
    foreach ($cfg in $Configs) {
        $split = $cfg.IndexOf(":")
        $label = $cfg.Substring(0, $split)
        $extra = @()
        $tail = $cfg.Substring($split + 1).Trim()
        if ($tail.Length -gt 0) { $extra = $tail -split "\s+" }

        Write-Host ""
        Write-Host "--- $label ($tail) ---" -ForegroundColor Cyan
        $procs = @()
        try {
            $serverOut = Join-Path $script:LogDir "pacing-$label-server.out.log"
            $server = Start-Process -FilePath $script:GodotExe `
                -ArgumentList @("--headless", "--path", $script:Root, "--", "--server", "--port", $Port) `
                -RedirectStandardOutput $serverOut `
                -RedirectStandardError (Join-Path $script:LogDir "pacing-$label-server.err.log") `
                -PassThru -NoNewWindow
            $null = $server.Handle
            $procs += $server
            if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
                Write-Fail "the server never reported listening within 30s; see $serverOut"
            }
            Write-Host "        server up (pid $($server.Id))"

            $csv = Join-Path $OutDir "pacing-$label.csv"
            $clientOut = Join-Path $script:LogDir "pacing-$label-client.out.log"
            # A WINDOWED client -- no --headless. Engine flags before the bare --, game flags
            # after; a game flag on the wrong side is swallowed and looks exactly like a hang.
            #
            # THE LANE: x = +2.5, z = -3.0 to +4.2. The holding room's interior is +-5 m and
            # everything in it stands on the -X wall (the practice corner, its table and target
            # box) or the -Z wall (the rack, the board, the START button, the clock), so this
            # column of the room is empty floor and a patrol along it never grazes a collider.
            # A bot that spends the run rubbing along a wall is a measurement of a wall.
            $client = Start-Process -FilePath $script:GodotExe `
                -ArgumentList (@("--path", $script:Root, "--",
                    "--bot", "--address", "127.0.0.1:$Port", "--name", "Pacing",
                    "--windowed", "--first-person-cam",
                    "--goto-patrol", "2.5,-3.0,2.5,4.2",
                    "--pacing-log", $csv,
                    "--pacing-sec", $DurationSec,
                    "--pacing-turn", $TurnDegPerSec,
                    "--pacing-label", $label,
                    "--duration", ($DurationSec + 6)) + $extra) `
                -RedirectStandardOutput $clientOut `
                -RedirectStandardError (Join-Path $script:LogDir "pacing-$label-client.err.log") `
                -PassThru
            $null = $client.Handle
            $procs += $client

            if (-not (Wait-ForExit $client ([int]($DurationSec + 90)))) {
                Stop-Proc $client
                Write-Fail "the pacing client did not exit within its duration + 90s; see $clientOut"
            }
        } finally {
            Stop-Procs $procs
        }

        $summary = Select-String -Path $clientOut -Pattern "^\[pacing\] SUMMARY " | Select-Object -Last 1
        if (-not $summary) {
            Write-Fail ("'$label' never printed its [pacing] SUMMARY line. A missing display or " +
                        "GPU is the first suspect -- this run is not headless. See $clientOut")
        }
        Write-Host "        $($summary.Line.Trim())" -ForegroundColor DarkGray
        $head = Select-String -Path $clientOut -Pattern "^\[pacing\] recording " | Select-Object -Last 1
        if ($head) { Write-Host "        $($head.Line.Trim())" -ForegroundColor DarkGray }
        $lens = Select-String -Path $clientOut -Pattern "^\[fp-lens\] " | Select-Object -Last 1
        if ($lens) { Write-Host "        $($lens.Line.Trim())" -ForegroundColor DarkGray }
        $rows += [pscustomobject]@{ Label = $label; Args = $tail; Summary = $summary.Line.Trim() }
    }
} finally {
    Exit-SuiteMutex $mutex
}

Write-Host ""
Write-Host "=== rows ===" -ForegroundColor White
foreach ($r in $rows) {
    Write-Host ("{0,-10} {1}" -f $r.Label, $r.Summary)
}
Write-Host ""
Write-Host "csv: $OutDir" -ForegroundColor DarkGray
exit 0
