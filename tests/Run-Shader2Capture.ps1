<#
.SYNOPSIS
    SHADER-2's headed evidence: the boxy shape under the sunken television, written to
    docs/qa/SHADER-2/<Label>/. Not a pass/fail suite -- it produces frames a human looks at, and
    it is deliberately NOT registered in Run-AllTests.ps1 for that reason (the same argument
    Run-EggCapture.ps1 and Run-Shader1Capture.ps1 both make).

.DESCRIPTION
    THE SUBJECT. GreenHills' "SunkenTvPlinth": a Cap (1.8 x 0.15 x 1.8, green_neutral) at
    y -2.20..-2.05 and a Column (1.4 x 2.4 x 1.4) at y -4.525..-2.125, under the sixth television
    at (-95, -2.05, 10.65). The bed at that radius is -2.71 .. -3.27, so roughly 0.6-1.2 m of the
    Column stands in open water and is what the player sees.

    --capture-cam is mandatory: a bot's own follow camera frames the BOT, and the subject here is
    a place. The bot is given no --goto-script, so it stands where it spawns (the hub) and cannot
    walk into frame. BotHarness builds a bare Camera3D, so FOV is Godot's default 75 vertical with
    KeepHeight -- which is what the screen-space arithmetic in the SHADER-2 report assumes.

    THE SHOTS

      bank-eye     -- Run-Shader1Capture.ps1's "sunken" framing VERBATIM, so the before/after pair
                      can be diffed against SHADER-1's own already-committed frame. Standing on
                      the south bank at eye height, 5.9 m from the television. This is the repro.
      bank-night   -- the same camera at midnight with the flashlight, because a dark artifact on
                      dark water is the one thing that could be day-only.
      bank-oblique -- the same distance from the south-east, to answer "does it depend on angle".
      bank-far     -- 15.4 m back and 1.5 m up, to answer "does it depend on distance".
      over-water   -- 3 m above the surface looking down, i.e. NOT through a grazing water plane.
      under-water  -- the camera BELOW the waterline beside the plinth, so no water surface stands
                      between it and the subject. Separates "the box is wrong" from "the water
                      over the box is wrong". Its Y (-1.20) and radius (13.5) are chosen against
                      the bake's own bed profile: under WaterGeometry.WaterY (-0.58) and clear of
                      the bed, which crosses the submerged contour (-1.73) at r = 13.888. The
                      first attempt at -1.90/14.6 put the camera INSIDE the bank and rendered the
                      terrain's backfaces; if this shot ever comes back with a flat grey lower
                      third, that is what happened.
      tv-murk      -- tight on the television itself, 3 m out and 0.48 m above the surface, so the
                      whole frame is the murk read Talon asked NOT to be changed. This is the
                      regression shot: it must be identical before and after.

    A missing PNG is reported and does not stop the remaining shots.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Label,
    [int]$Port = 45897,
    [string]$OutDir = "",
    [double]$DurationSec = 26,
    [double]$CaptureAtSec = 18,
    [string[]]$Only = @()
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# NOT a param() default: PowerShell 5.1 does not reliably populate $PSScriptRoot while binding
# param defaults under `powershell -File`, and the failure is silent. (SHADER-1's note.)
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Split-Path $PSScriptRoot -Parent) "docs\qa\SHADER-2\$Label"
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

# x,y,z, tx,ty,tz -- all world space. Flash = seconds at which the bot toggles its flashlight.
$shots = @(
    @{ Tag = "bank-eye";     Phase = "noon";     Cam = "-95,0.15,16.5,-95,-0.45,10.65" }
    @{ Tag = "bank-night";   Phase = "midnight"; Cam = "-95,0.15,16.5,-95,-0.45,10.65"; Flash = 2 }
    @{ Tag = "bank-oblique"; Phase = "noon";     Cam = "-90.8,0.15,15.0,-95,-0.45,10.65" }
    @{ Tag = "bank-far";     Phase = "noon";     Cam = "-95,1.5,26.0,-95,-1.2,10.65" }
    @{ Tag = "over-water";   Phase = "noon";     Cam = "-95,3.0,16.0,-95,-2.4,10.65" }
    @{ Tag = "under-water";  Phase = "noon";     Cam = "-95,-1.2,13.5,-95,-2.6,10.65" }
    @{ Tag = "tv-murk";      Phase = "noon";     Cam = "-95,-0.10,13.5,-95,-1.35,10.75" }
)
# `powershell -File script.ps1 -Only a,b` hands 5.1 ONE string "a,b" rather than a two-element
# array, so split every element again here; a caller who passed a real array is unaffected.
$onlyTags = @()
foreach ($o in $Only) { $onlyTags += ($o -split ',') | Where-Object { $_.Trim().Length -gt 0 } }
if ($onlyTags.Count -gt 0) { $shots = @($shots | Where-Object { $onlyTags -contains $_.Tag }) }
if ($shots.Count -eq 0) { Write-Fail "no shots selected (-Only matched nothing)" }

Write-Host "=== SHADER-2 captures ($Label) -> $OutDir ===" -ForegroundColor White
$missing = @()

foreach ($s in $shots) {
    $shotDir = Join-Path $OutDir $s.Tag
    New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
    Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

    Write-Host ("[{0}] phase {1}, cam {2}..." -f $s.Tag, $s.Phase, $s.Cam) -ForegroundColor Cyan
    $procs = @()
    try {
        $serverOut = Join-Path $script:LogDir "sh2-server-$($s.Tag).out.log"
        $server = Start-Godot @(
            "--server", "--port", $Port, "--world", "bubbletest",
            "--cycle-start-phase", $s.Phase, "--cycle-freeze",
            "--log-dir", $script:LogDir) "sh2-server-$($s.Tag)"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
            Write-Host "      server never listened; skipping $($s.Tag)" -ForegroundColor Yellow
            $missing += $s.Tag
            continue
        }

        $botArgs = @(
            "--bot", "--address", "127.0.0.1:$Port", "--name", "Sh2Cap",
            "--duration", $DurationSec, "--world", "bubbletest",
            "--cycle-start-phase", $s.Phase,
            "--capture-dir", $shotDir, "--capture-at", $CaptureAtSec,
            "--capture-cam", $s.Cam)
        if ($s.ContainsKey("Flash")) { $botArgs += @("--flashlight-at", $s.Flash) }

        $bot = Start-GodotWindowed $botArgs "sh2-bot-$($s.Tag)"
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
        Write-Host "      NO PNG for $($s.Tag) -- see sh2-bot-$($s.Tag).out.log" -ForegroundColor Yellow
        $missing += $s.Tag
    } else {
        foreach ($p in $pngs) { Write-Host ("        {0}  ({1:N0} bytes)" -f $p.Name, $p.Length) -ForegroundColor Gray }
    }
}

Write-Host ""
if ($missing.Count -gt 0) {
    Write-Host ("shots with no frame: {0}" -f ($missing -join ", ")) -ForegroundColor Yellow
} else {
    Write-Host "every shot produced a frame." -ForegroundColor Green
}
exit 0
