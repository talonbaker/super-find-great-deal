<#
.SYNOPSIS
    SHADER-1's headed evidence: the bubble-over-water bug and its neighbours, written to
    docs/qa/SHADER-1/<Label>/. Not a pass/fail suite -- it produces frames a human looks at, and
    it is deliberately NOT registered in Run-AllTests.ps1 for that reason (same argument
    Run-EggCapture.ps1 makes).

.DESCRIPTION
    --capture-cam is mandatory: a bot's own follow camera frames the BOT, and every subject here
    is a place. The bot is never given a --goto-script, so it stands where it spawns (the hub) and
    cannot walk into a bubble and collect it out of the frame.

    THE TWO SHOTS THAT ARE THE PACKET

      lake-east / lake-west -- the SAME ten bubbles over green's lake from opposite banks, both
      high enough that every bubble projects onto water rather than onto the far shore. Green's
      lake is a disc of radius 16.96 m centred on (-95, -0.58, 0) (WaterGeometry, measured from
      the shipped mesh); the bubbles sit on a crossing line through it. Godot sorts the transparent
      queue by DEPTH TO THE SORTING POINT, and a MeshInstance3D's default sorting point is its
      AABB CENTRE -- so the whole 34 m lake sorts as one object at its centre, ~23 m from either
      camera. Bubbles nearer than that composite over the water; bubbles further away are drawn
      FIRST and the water then blends over them.

      That makes the pair a positive control rather than two pictures: bubble Green_08 is on the
      near side from the east and on the far side from the west, so the same bubble must be
      visible in one frame and gone in the other. A bubble that is merely dim cannot do that.

      lake-night -- the same east framing at midnight with the flashlight on, because the film's
      night glow (BubbleOscillation.MaxEmissionEnergy) is the one thing that could make the day
      answer not the night answer.

    THE NEIGHBOURS (scope item 4) -- the same water, and the same transparent queue, seen with
    everything else that shares them:

      moat    -- BluePrecision's drowning moat, EGG-2's framing verbatim.
      sunken  -- green's lake from the south bank at eye height: the murk read, and the sixth
                 television 2 m under it. EGG-2's framing verbatim.
      secret  -- the SecretBubble, a StandardMaterial3D on ALPHA transparency hanging at
                 (-95, -1.35, -10), i.e. UNDER the surface. Midnight, when it is a light.
      cubes   -- the hub's golden cubes.
      tvroom  -- inside television room A, 20 m under the level.

    A missing PNG is reported and does not stop the remaining shots.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Label,
    [int]$Port = 45893,
    [string]$OutDir = "",
    [double]$DurationSec = 26,
    [double]$CaptureAtSec = 18,
    [string[]]$Only = @()
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# NOT a param() default: PowerShell 5.1 does not reliably populate $PSScriptRoot while binding
# param defaults under `powershell -File`, and the failure is silent.
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Split-Path $PSScriptRoot -Parent) "docs\qa\SHADER-1\$Label"
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
    @{ Tag = "lake-se";    Phase = "noon";     Cam = "-83,9,13,-102,0.5,-3" }
    @{ Tag = "lake-nw";    Phase = "noon";     Cam = "-107,9,-13,-88,0.5,3" }
    @{ Tag = "lake-east";  Phase = "noon";     Cam = "-70,14,0,-98,0,0" }
    @{ Tag = "lake-west";  Phase = "noon";     Cam = "-120,14,0,-92,0,0" }
    @{ Tag = "lake-night"; Phase = "midnight"; Cam = "-70,14,0,-98,0,0"; Flash = 2 }
    @{ Tag = "moat";       Phase = "noon";     Cam = "64,22,32,81,-2,-2" }
    @{ Tag = "sunken";     Phase = "noon";     Cam = "-95,0.15,16.5,-95,-0.45,10.65" }
    @{ Tag = "secret";     Phase = "midnight"; Cam = "-95,6,-24,-95,-1.35,-10" }
    @{ Tag = "cubes";      Phase = "noon";     Cam = "26,10,26,0,0.3,6" }
    @{ Tag = "tvroom";     Phase = "noon";     Cam = "0,-18.4,-4.5,0,-18.9,5.2" }
)
# `powershell -File script.ps1 -Only a,b` hands 5.1 ONE string "a,b" rather than a two-element
# array, so split every element again here; a caller who passed a real array is unaffected.
$onlyTags = @()
foreach ($o in $Only) { $onlyTags += ($o -split ',') | Where-Object { $_.Trim().Length -gt 0 } }
if ($onlyTags.Count -gt 0) { $shots = @($shots | Where-Object { $onlyTags -contains $_.Tag }) }
if ($shots.Count -eq 0) { Write-Fail "no shots selected (-Only matched nothing)" }

Write-Host "=== SHADER-1 captures ($Label) -> $OutDir ===" -ForegroundColor White
$missing = @()

foreach ($s in $shots) {
    $shotDir = Join-Path $OutDir $s.Tag
    New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
    Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

    Write-Host ("[{0}] phase {1}, cam {2}..." -f $s.Tag, $s.Phase, $s.Cam) -ForegroundColor Cyan
    $procs = @()
    try {
        $serverOut = Join-Path $script:LogDir "sh1-server-$($s.Tag).out.log"
        $server = Start-Godot @(
            "--server", "--port", $Port, "--world", "bubbletest",
            "--cycle-start-phase", $s.Phase, "--cycle-freeze",
            "--log-dir", $script:LogDir) "sh1-server-$($s.Tag)"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
            Write-Host "      server never listened; skipping $($s.Tag)" -ForegroundColor Yellow
            $missing += $s.Tag
            continue
        }

        $botArgs = @(
            "--bot", "--address", "127.0.0.1:$Port", "--name", "Sh1Cap",
            "--duration", $DurationSec, "--world", "bubbletest",
            "--cycle-start-phase", $s.Phase,
            "--capture-dir", $shotDir, "--capture-at", $CaptureAtSec,
            "--capture-cam", $s.Cam)
        if ($s.ContainsKey("Flash")) { $botArgs += @("--flashlight-at", $s.Flash) }

        $bot = Start-GodotWindowed $botArgs "sh1-bot-$($s.Tag)"
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
        Write-Host "      NO PNG for $($s.Tag) -- see sh1-bot-$($s.Tag).out.log" -ForegroundColor Yellow
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
