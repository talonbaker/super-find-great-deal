<#
.SYNOPSIS
    FRAME-1's headed evidence: Talon's drawing on the wall of the room behind the sunken lake
    television, written to docs/qa/FRAME-1/.

.DESCRIPTION
    Not a pass/fail suite and deliberately not registered in Run-AllTests.ps1 -- it produces
    frames a human looks at.

    THE ROOM. BubbleTestLayout.TvRoutes' last row is keyed on SunkenTvNodeName and stands at
    SunkenTvPos, which is derived from the lake's own centre; its RoomCentre is (-17, 0, -17) and
    its room is RoomF. ArrivalOf() puts the traveller at world (-17, -19.6, -14), facing -Z, and
    the drawing hangs on the north wall at world (-17, -18.0, -23.0). Both cameras below are those
    numbers and nothing invented.

    --capture-cam is mandatory: a bot's own follow camera frames the BOT, and the subject here is
    a wall 20 m underground that no bot walks to.

    STATES

      shipped -- resources/SecretRoomPicture.png as it ships: Talon's crayon "fwends" on a torn
                 sheet with masking tape, 1388 x 1053 RGBA, 24.1% of it fully transparent. The
                 murk wall must be visible through the surround and around the torn edge; if
                 alpha were broken it would be a solid rectangle instead.

      empty   -- the same build with the PNG moved aside. The wall must read as a deliberate,
                 lit, empty mount rather than as a bug, and the launch log must have no ERROR
                 lines. This is what a build with the art removed or renamed looks like.

      opaque  -- THE CONTROL PAIR, and the reason the shipped shot is evidence rather than an
                 assertion. Two checkerboards of identical geometry authored in
                 docs/qa/FRAME-1/make_test_images.py: picture_alpha.png with half its cells fully
                 transparent, picture_opaque.png with those same cells filled solid grey. Side by
                 side they separate "the material honours alpha" from "that part of the image was
                 dark anyway".

    CAMERAS

      arrival -- exactly the arrival point, at the shipped avatar's measured eye height
                 (AvatarProportions.PlayerEyeHeightM, 0.995 m above the room floor at y = -20).
                 This is what a player sees the instant the room resolves, and it is the shot that
                 answers Talon's "large enough to at least the player can see it".

      close   -- 3 m out from the same wall at the same height.

    THE TRANSIENT WRITES, DISCLOSED. For the empty and control states this script moves Talon's
    PNG aside (and puts it back in a finally block) and copies a test PNG in its place. The
    shipped file is never modified and never re-exported.

.EXAMPLE
    powershell -File tools/dev/frame1_capture.ps1
    powershell -File tools/dev/frame1_capture.ps1 -Only shipped

    The absence check's POSITIVE CONTROL is not a switch here, because a switch that fakes an
    error proves nothing about the real guard. It is a scratch source edit, recorded step by step
    in docs/qa/FRAME-1/README.md: invert the ResourceLoader.Exists test in
    BubbleTestWorld.SetUpSecretPicture so GD.Load runs on a path that genuinely is not there, and
    watch the same log scan that reports 0 ERROR lines for the shipped build light up.
#>
[CmdletBinding()]
param(
    [int]$Port = 45993,
    [string]$OutDir = "",
    [double]$DurationSec = 22,
    [double]$CaptureAtSec = 14,
    [string]$Only = ""
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\..\..\tests\_Common.ps1"

if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot "..\..\docs\qa\FRAME-1" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

# Where the drawing lives. Mirrored from BubbleTestLayout.SecretPictureImagePath; the C# side is
# the authority.
$PicturePath = Join-Path $script:Root "resources\SecretRoomPicture.png"
$Stash       = Join-Path $script:Root "resources\SecretRoomPicture.png.frame1-stash"
$StashImport = "$Stash.import"

function Start-GodotWindowed([string[]]$UserArgs, [string]$Tag) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $err = Join-Path $script:LogDir "$Tag.err.log"
    $a = @("--path", $script:Root, "--resolution", "1920x1080", "--") + $UserArgs
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList $a `
        -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

function Hide-Shipped {
    if (Test-Path $PicturePath) { Move-Item $PicturePath $Stash -Force }
    if (Test-Path "$PicturePath.import") { Move-Item "$PicturePath.import" $StashImport -Force }
}
function Restore-Shipped {
    if (Test-Path $Stash) { Move-Item $Stash $PicturePath -Force }
    if (Test-Path $StashImport) { Move-Item $StashImport "$PicturePath.import" -Force }
}

# world x,y,z, tx,ty,tz. Derived in the .DESCRIPTION above, not chosen by eye.
$cams = @(
    @{ Tag = "arrival"; Cam = "-17,-19.005,-14,-17,-18.0,-23.0" }
    @{ Tag = "close";   Cam = "-17,-19.005,-20,-17,-18.0,-23.0" }
)

# Image = $null means "the shipped file, untouched". "" means "no file at all".
$states = @(
    @{ Tag = "shipped";         Image = $null; Cams = @("arrival", "close") }
    @{ Tag = "empty";           Image = "";    Cams = @("arrival", "close") }
    @{ Tag = "control-alpha";   Image = (Join-Path $OutDir "picture_alpha.png");  Cams = @("close") }
    @{ Tag = "control-opaque";  Image = (Join-Path $OutDir "picture_opaque.png"); Cams = @("close") }
)
if ($Only) { $states = $states | Where-Object { $_.Tag -eq $Only } }

Write-Host "=== FRAME-1 captures -> $OutDir ===" -ForegroundColor White
Restore-Shipped

$missing = @()
try {
    foreach ($st in $states) {
        Restore-Shipped
        if ($null -ne $st.Image) {
            Hide-Shipped
            if ($st.Image) {
                if (-not (Test-Path $st.Image)) {
                    Write-Host "  missing test image $($st.Image) -- run docs/qa/FRAME-1/make_test_images.py" -ForegroundColor Yellow
                    $missing += $st.Tag
                    continue
                }
                Copy-Item $st.Image $PicturePath -Force
            }
        }

        # AFTER the swap: the import is what turns a loose PNG into something ResourceLoader.Exists
        # can see, and it is also exactly the step a replacement drawing needs (open the project
        # once, or run this).
        Invoke-BuildAndImport

        foreach ($cName in $st.Cams) {
            $c = $cams | Where-Object { $_.Tag -eq $cName }
            $shotDir = Join-Path $OutDir "$($st.Tag)-$($c.Tag)"
            New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
            Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

            Write-Host ("[{0}/{1}] cam {2}..." -f $st.Tag, $c.Tag, $c.Cam) -ForegroundColor Cyan
            $procs = @()
            try {
                $tag = "frame1-$($st.Tag)-$($c.Tag)"
                $serverOut = Join-Path $script:LogDir "$tag-server.out.log"
                $server = Start-Godot @(
                    "--server", "--port", $Port, "--world", "bubbletest",
                    "--cycle-start-phase", "noon", "--cycle-freeze",
                    "--log-dir", $script:LogDir) "$tag-server"
                $procs += $server
                if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 90)) {
                    Write-Host "      server never listened; skipping" -ForegroundColor Yellow
                    $missing += "$($st.Tag)/$($c.Tag)"
                    continue
                }

                $bot = Start-GodotWindowed @(
                    "--bot", "--address", "127.0.0.1:$Port", "--name", "Frame1",
                    "--duration", $DurationSec, "--world", "bubbletest",
                    "--cycle-start-phase", "noon",
                    "--capture-dir", $shotDir, "--capture-at", $CaptureAtSec,
                    "--capture-cam", $c.Cam) "$tag-bot"
                $procs += $bot
                if (-not (Wait-ForExit $bot ([int]$DurationSec + 90))) {
                    Write-Host "      the capture bot never exited" -ForegroundColor Yellow
                }
            }
            finally {
                foreach ($p in $procs) { if ($p -and -not $p.HasExited) { Stop-Proc $p } }
            }

            $pngs = Get-ChildItem -Path $shotDir -Filter *.png -ErrorAction SilentlyContinue
            if (-not $pngs) {
                Write-Host "      NO PNG -- see $tag-bot.out.log" -ForegroundColor Yellow
                $missing += "$($st.Tag)/$($c.Tag)"
            } else {
                foreach ($p in $pngs) { Write-Host ("        {0}  ({1:N0} bytes)" -f $p.Name, $p.Length) -ForegroundColor Gray }
            }
        }
    }
}
finally {
    if (Test-Path $PicturePath) {
        # Only remove a stand-in, never the shipped drawing.
        if (Test-Path $Stash) { Remove-Item $PicturePath -Force }
    }
    if (Test-Path "$PicturePath.import") {
        if (Test-Path $StashImport) { Remove-Item "$PicturePath.import" -Force }
    }
    Restore-Shipped
    Invoke-BuildAndImport
    Write-Host "shipped drawing restored at resources\SecretRoomPicture.png." -ForegroundColor DarkGray
}

Write-Host ""
if ($missing.Count -gt 0) {
    Write-Host ("shots with no frame: {0}" -f ($missing -join ", ")) -ForegroundColor Yellow
} else {
    Write-Host "every requested shot produced a frame." -ForegroundColor Green
}
exit 0
