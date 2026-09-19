<#
.SYNOPSIS
    EGG-2's headed evidence: four shots of the Bubble Test's new surprises, written to
    docs/qa/EGG-2/. Not a pass/fail suite -- it produces frames a human looks at, and it is not
    registered in Run-AllTests.ps1 for that reason.

.DESCRIPTION
    --capture-cam is mandatory here for the same reason Run-BubbleTestCapture.ps1 gives: a bot's
    own follow camera frames the bot, and three of these four subjects are places rather than
    bodies. The fourth (the creature) deliberately frames the bot's neighbourhood rather than the
    bot, because the whole point of that shot is what is standing 21 m away from it.

    SHOTS

      moat    -- the blue tower's climb over its new water, midday, from the south-west and high
                 enough to hold the whole 50 x 31 m basin and the spiral above it. Talon's
                 addendum §7 made this floor lethal; this is what a player looks down at from the
                 balance beam now.

      sunken  -- green's lake from the south bank at eye height, looking north at the plinth the
                 sixth television stands on. It is 2 m under the surface through the murk shader,
                 so the honest answer to "can you see it" is a frame, not an assurance.

      cyan    -- the cyan run's east plate, looking down the second ruler. Directed 2026-09-02 at
                 absence altitude (THRILL-BIBLE §12, "the room where you were measured"); the shot
                 exists so the arrangement can be judged as an arrangement.

      night   -- midnight, frozen, framing the bot's neighbourhood rather than the bot. The
                 creature stands 18..62 m from whoever it is watching and never inside their view
                 cone, so a shot aimed AT the bot would systematically miss it. The pass/fail
                 evidence for the night gate is Run-WatcherNightGateTest.ps1, which asserts on the
                 server log in both directions; this is the look at it.

    A missing PNG is reported and does not stop the remaining shots -- this is evidence
    gathering, not a gate, and a machine with a busy GPU should still produce the three it can.
#>
[CmdletBinding()]
param(
    [int]$Port = 45881,
    [string]$OutDir = "",
    [double]$DurationSec = 30,
    [double]$CaptureAtSec = 22
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# NOT a param() default: PowerShell 5.1 does not reliably populate $PSScriptRoot while binding
# param defaults under `powershell -File`, and the failure is silent. Same workaround every other
# *Capture script in this directory uses.
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Split-Path $PSScriptRoot -Parent) "docs\qa\EGG-2"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

function Start-GodotWindowed([string[]]$UserArgs, [string]$Tag) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $err = Join-Path $script:LogDir "$Tag.err.log"
    $a = @("--path", $script:Root, "--") + $UserArgs
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList $a `
        -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
    $null = $p.Handle
    return $p
}

Reset-LogDir
Invoke-BuildAndImport

# x,y,z, tx,ty,tz -- all world space.
$shots = @(
    @{ Tag = "moat";   Phase = "noon";     Cam = "64,22,32,81,-2,-2";    Walk = "40,0" }
    # Eye height on the south bank, looking flat across the water at the plinth 5 m out. The point
    # of the low angle is the ANTENNA: TvPortal builds two rabbit ears whose tops sit at y -0.325,
    # which is 0.255 m ABOVE WaterY (-0.58), so the sixth television's own shipped geometry breaks
    # the surface and is the discovery cue. A high shot cannot show that and a murky lake will not
    # show the set itself from any distance -- see the report.
    @{ Tag = "sunken"; Phase = "noon";     Cam = "-95,0.15,16.5,-95,-0.45,10.65"; Walk = "-70,18" }
    @{ Tag = "cyan";   Phase = "noon";     Cam = "34,28,86,56,0,80";      Walk = "0,70" }
    # Eye height and CLOSE, so anything standing off from the bot is silhouetted against the sky
    # rather than lost against unlit ground: this level's night sight range is 28 m and a high
    # downward shot at 40 m returns a black frame that proves nothing either way.
    @{ Tag = "night";  Phase = "midnight"; Cam = "30,1.6,86,30,1.6,60";   Walk = "30,70" }
)

Write-Host "=== EGG-2 captures -> $OutDir ===" -ForegroundColor White
$missing = @()

foreach ($s in $shots) {
    $shotDir = Join-Path $OutDir $s.Tag
    New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
    Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

    Write-Host ("[{0}] phase {1}, cam {2}..." -f $s.Tag, $s.Phase, $s.Cam) -ForegroundColor Cyan
    $procs = @()
    try {
        $serverOut = Join-Path $script:LogDir "eggcap-server-$($s.Tag).out.log"
        $server = Start-Godot @(
            "--server", "--port", $Port, "--world", "bubbletest",
            "--cycle-start-phase", $s.Phase, "--cycle-freeze",
            "--log-dir", $script:LogDir) "eggcap-server-$($s.Tag)"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
            Write-Host "      server never listened; skipping $($s.Tag)" -ForegroundColor Yellow
            $missing += $s.Tag
            continue
        }

        $bot = Start-GodotWindowed @(
            "--bot", "--address", "127.0.0.1:$Port", "--name", "EggCap",
            "--duration", $DurationSec, "--world", "bubbletest",
            "--cycle-start-phase", $s.Phase,
            "--goto-script", $s.Walk,
            "--capture-dir", $shotDir, "--capture-at", $CaptureAtSec,
            "--capture-cam", $s.Cam) "eggcap-bot-$($s.Tag)"
        $procs += $bot
        if (-not (Wait-ForExit $bot ([int]$DurationSec + 90))) {
            Write-Host "      the $($s.Tag) capture bot never exited" -ForegroundColor Yellow
        }
    }
    finally {
        foreach ($p in $procs) { if ($p -and -not $p.HasExited) { Stop-Proc $p } }
    }

    $pngs = Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue
    if (-not $pngs) {
        Write-Host "      NO PNG for $($s.Tag) -- see eggcap-bot-$($s.Tag).out.log" -ForegroundColor Yellow
        $missing += $s.Tag
    } else {
        foreach ($p in $pngs) { Write-Host ("        {0}  ({1:N0} bytes)" -f $p.Name, $p.Length) -ForegroundColor Gray }
    }
}

Write-Host ""
if ($missing.Count -gt 0) {
    Write-Host ("shots with no frame: {0}" -f ($missing -join ", ")) -ForegroundColor Yellow
} else {
    Write-Host "all four shots produced frames." -ForegroundColor Green
}
exit 0
