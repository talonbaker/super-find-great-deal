<#
.SYNOPSIS
    CELEBRATE-1's headed evidence: what the all-bubbles celebration actually looks like, written to
    docs/qa/CELEBRATE-1/<Label>/. Not a pass/fail suite -- it produces frames a human looks at, and
    it is deliberately NOT registered in Run-AllTests.ps1 for that reason (the same argument
    Run-EggCapture.ps1 and Run-Shader1Capture.ps1 make).

.DESCRIPTION
    A dedicated server runs the REAL bubbletest level with --bubble-pop-all-at, so the tally the
    frame shows is the level's own hundred-odd authored bubbles going to full, not a six-bubble
    test fixture. A WINDOWED bot joins it and captures its own viewport across the celebration.

    TWO FLAGS DO THE WORK, and both are worth understanding before reading a frame:

      --celebrate-force   The celebration is gated on a person driving a body
                          (IIntentSource.IsHumanInput), so a capture bot correctly refuses it --
                          which is exactly the behaviour Run-CelebrateTest.ps1 asserts. This flag
                          opens that gate for THIS process only. It reaches one boolean in
                          BubbleCelebration and cannot unlock an achievement: AchievementRuntime
                          is built (or not) in SandboxAvatar.ConfigureAsNetworked, which never
                          reads it, so nothing here can write into the shared user:// profile.

      --capture-at        A LIST of marks, not one. The whole beat is 1.6 s
                          (BubbleCelebration.TotalSeconds) and the bot's clock and the server's
                          differ by however long the windowed client took to boot, so a single
                          mark would be a coin flip. The marks straddle the completion densely
                          enough that several frames land inside it; a human picks the good ones.

    WHAT TO LOOK FOR IN THE FRAMES, in the order they appear:
      * the HUD tally reading full;
      * a burst of pale-gold sparkles rising off every body in view, the capture bot's included;
      * one line of text near the top of the screen, "That's every bubble in the world.";
      * and, across all of it, the world still running -- no dimming, no letterbox, no frozen
        camera, nothing covering the play area. The restraint IS the deliverable.

    The sound cannot be captured in a PNG. Run-CelebrateTest.ps1 proves it fires on every peer;
    tests/unit/CelebrateTests.cs pins its length, headroom and shape.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Label,
    [int]$Port = 45894,
    [string]$OutDir = "",
    [double]$PopAllAtSec = 14,
    [double]$DurationSec = 26,
    # x,y,z,tx,ty,tz -- world space. MANDATORY in practice, not optional: a bot builds no
    # SandboxCamera of its own, so a capture run without one renders the engine's flat clear
    # colour behind a perfectly correct HUD. (Measured on the first run of this script: the tally
    # and the toast line were both right and the world was a grey field.) Run-Shader1Capture's
    # header says the same thing from the other end. This default frames the bubbletest hub spawn
    # (0, 0, 12) from behind and slightly above, so the capture bot's own body -- and the sparkles
    # coming off it -- are in shot.
    [string]$CaptureCam = "1.4,1.5,29.2,0,1.0,27.4",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# NOT a param() default: PowerShell 5.1 does not reliably populate $PSScriptRoot while binding
# param defaults under `powershell -File`, and the failure is silent. (Run-Shader1Capture's note.)
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Split-Path $PSScriptRoot -Parent) "docs\qa\CELEBRATE-1\$Label"
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

if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }
Get-ChildItem -Path $OutDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

$inv = [System.Globalization.CultureInfo]::InvariantCulture
# Dense marks either side of the server's completion. The bot's clock starts when it boots, some
# seconds after the server's, so this covers a WINDOW rather than betting on a point.
$marks = @()
for ($t = $PopAllAtSec - 6.0; $t -le $PopAllAtSec + 3.0; $t += 0.4) { $marks += $t.ToString("F2", $inv) }
$markArg = $marks -join ","

Write-Host "=== CELEBRATE-1 capture ($Label) -> $OutDir ===" -ForegroundColor White
$procs = @()
try {
    $serverOut = Join-Path $script:LogDir "celebcap-server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "bubbletest",
        "--cycle-start-phase", "noon", "--cycle-freeze",
        "--bubble-pop-all-at", $PopAllAtSec.ToString("F2", $inv),
        "--log-dir", $script:LogDir) "celebcap-server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "server never listened; see $serverOut"
    }
    if (-not (Wait-ForLogLine $serverOut "\[bubble\] adopted \d+ bubble" 60)) {
        Write-Fail "server never adopted the level's bubbles; see $serverOut"
    }
    $adopted = (Select-String -Path $serverOut -Pattern "\[bubble\] adopted (\d+) bubble" |
        Select-Object -First 1).Matches[0].Groups[1].Value
    Write-Host "        the level adopted $adopted bubbles; completing them at ${PopAllAtSec}s" -ForegroundColor Gray

    $bot = Start-GodotWindowed @(
        "--bot", "--address", "127.0.0.1:$Port", "--name", "CelebCap",
        "--duration", $DurationSec.ToString("F2", $inv), "--world", "bubbletest",
        "--cycle-start-phase", "noon",
        "--celebrate-force",
        "--capture-dir", $OutDir, "--capture-at", $markArg,
        "--capture-cam", $CaptureCam) "celebcap-bot"
    $procs += $bot
    if (-not (Wait-ForExit $bot ([int]$DurationSec + 90))) {
        Write-Host "      the capture bot never exited" -ForegroundColor Yellow
    }
} finally {
    foreach ($p in $procs) { if ($p -and -not $p.HasExited) { Stop-Proc $p } }
}

$completions = @(Get-Content (Join-Path $script:LogDir "celebcap-server.out.log") |
    Where-Object { $_ -match "^\[bubbletest\] all bubbles popped:" })
Write-Host ("        server announced {0} completion(s)" -f $completions.Count)
$botOut = Join-Path $script:LogDir "celebcap-bot.out.log"
$celebrated = @(Get-Content $botOut -ErrorAction SilentlyContinue |
    Where-Object { $_ -match "^\[bubbletest\] celebrate:" })
foreach ($line in $celebrated) { Write-Host "        bot: $line" -ForegroundColor Gray }

$pngs = @(Get-ChildItem -Path $OutDir -Filter *.png -ErrorAction SilentlyContinue)
Write-Host ""
if ($pngs.Count -eq 0) {
    Write-Host "NO FRAMES were captured -- see $botOut" -ForegroundColor Yellow
    exit 1
}
foreach ($p in $pngs) { Write-Host ("        {0}  ({1:N0} bytes)" -f $p.Name, $p.Length) -ForegroundColor Gray }
Write-Host ""
Write-Host ("{0} frame(s) captured." -f $pngs.Count) -ForegroundColor Green
exit 0
