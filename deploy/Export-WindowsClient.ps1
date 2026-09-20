<#
.SYNOPSIS
    Exports the interactive Windows client build (build/windows-client/) that will eventually
    ship through SteamPipe. Installs the Godot 4.7 mono export templates automatically if missing.

.DESCRIPTION
    Fully automated - no editor interaction. Shares the same export-templates archive as
    Export-LinuxServer.ps1 (one .tpz contains every platform's templates), so if the server
    export has already installed them, this is just an export - no re-download.
#>
[CmdletBinding()]
param(
    [string]$GodotExe = "Godot_v4.7-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$templatesDir = Join-Path $env:APPDATA "Godot\export_templates\4.7.stable.mono"
$templatesUrl = "https://github.com/godotengine/godot/releases/download/4.7-stable/Godot_v4.7-stable_mono_export_templates.tpz"
$exportDir = Join-Path $root "build\windows-client"
$exportBin = Join-Path $exportDir "game.exe"
$buildInfoPath = Join-Path $root "scripts\BuildInfo.cs"
$presetsPath = Join-Path $root "export_presets.cfg"
$steamDllSrc = Join-Path $root "thirdparty\steamworks\win-x64\steam_api64.dll"

function Fail([string]$Message) {
    Write-Host "FAIL: $Message" -ForegroundColor Red
    exit 1
}

# --- Version: BuildInfo.cs is the single source of truth. Parse it and map the CalVer string
#     to the numeric quad Windows requires for file_version/product_version. Date-based CalVer
#     "2026.07.16" -> "2026.7.16.0"; a same-day rebuild "2026.07.16-2" -> "2026.7.16.2".
function Get-BuildVersion {
    if (-not (Test-Path $buildInfoPath)) { Fail "cannot stamp version - missing $buildInfoPath" }
    $match = Select-String -Path $buildInfoPath -Pattern 'Version\s*=\s*"([^"]+)"' | Select-Object -First 1
    if (-not $match) { Fail "could not parse BuildInfo.Version out of $buildInfoPath" }
    return $match.Matches[0].Groups[1].Value
}

function ConvertTo-WindowsVersionQuad([string]$Version) {
    # Split an optional "-N" same-day suffix off the date; N becomes the 4th (build) field.
    $build = "0"
    $date = $Version
    if ($Version -match '^(.*)-(\d+)$') { $date = $Matches[1]; $build = $Matches[2] }
    $parts = $date.Split(".")
    if ($parts.Count -ne 3) { Fail "BuildInfo.Version '$Version' is not date-based CalVer YYYY.MM.DD[-N]" }
    $ints = foreach ($p in $parts) {
        if ($p -notmatch '^\d+$') { Fail "BuildInfo.Version '$Version' has a non-numeric part ('$p')" }
        [string][int]$p   # strip leading zeros so Windows reads integers: "07" -> "7"
    }
    return (($ints + $build) -join ".")
}

# --- Stamp export_presets.cfg from BuildInfo.cs (the .cfg is a generated mirror) -------------
$buildVersion = Get-BuildVersion
$versionQuad = ConvertTo-WindowsVersionQuad $buildVersion
Write-Host "[export] BuildInfo.Version=$buildVersion -> Windows version quad $versionQuad" -ForegroundColor Cyan

if (-not (Test-Path $presetsPath)) { Fail "missing $presetsPath" }
$presetsText = Get-Content $presetsPath -Raw
$stampedText = $presetsText `
    -replace 'application/file_version="[^"]*"', "application/file_version=`"$versionQuad`"" `
    -replace 'application/product_version="[^"]*"', "application/product_version=`"$versionQuad`""
Set-Content -Path $presetsPath -Value $stampedText -NoNewline

# --- Drift check: fail loudly if the stamp didn't take, so the .cfg can never silently
#     disagree with BuildInfo.cs (the headless guard the Story asks for) --------------------
$verifyText = Get-Content $presetsPath -Raw
$fileVerMatch = [regex]::Match($verifyText, 'application/file_version="([^"]*)"')
$productVerMatch = [regex]::Match($verifyText, 'application/product_version="([^"]*)"')
if (-not $fileVerMatch.Success -or $fileVerMatch.Groups[1].Value -ne $versionQuad) {
    Fail "version drift: export_presets.cfg application/file_version is '$($fileVerMatch.Groups[1].Value)', expected '$versionQuad' (from BuildInfo.Version=$buildVersion)"
}
if (-not $productVerMatch.Success -or $productVerMatch.Groups[1].Value -ne $versionQuad) {
    Fail "version drift: export_presets.cfg application/product_version is '$($productVerMatch.Groups[1].Value)', expected '$versionQuad' (from BuildInfo.Version=$buildVersion)"
}
Write-Host "[export] version stamp verified - export_presets.cfg matches BuildInfo.Version" -ForegroundColor Green

# --- Export templates (one-time install, shared with the Linux server export) ---------------
if (-not (Test-Path (Join-Path $templatesDir "windows_release_x86_64.exe"))) {
    Write-Host "[export] Godot 4.7 mono export templates not installed - downloading (~1.1 GB, one time)..." -ForegroundColor Yellow
    $tpz = Join-Path $env:TEMP "godot-4.7-mono-templates.tpz"
    if (-not (Test-Path $tpz)) {
        # Download to a .partial name and rename on success: an interrupted download
        # otherwise leaves a truncated .tpz that every later run trusts and fails to
        # extract, with no path back short of manually deleting the temp file.
        $partial = "$tpz.partial"
        if (Test-Path $partial) { Remove-Item -Force $partial }
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        (New-Object System.Net.WebClient).DownloadFile($templatesUrl, $partial)
        if ((Get-Item $partial).Length -lt 500MB) { Fail "templates download looks truncated ($([math]::Round((Get-Item $partial).Length / 1MB)) MB) - delete $partial and retry" }
        Move-Item -Force $partial $tpz
    }
    Write-Host "[export] installing templates..." -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $tmp = Join-Path $env:TEMP "godot-tpl-extract"
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
    try {
        [System.IO.Compression.ZipFile]::ExtractToDirectory($tpz, $tmp)
    } catch {
        Remove-Item -Force $tpz -ErrorAction SilentlyContinue
        Fail "templates archive failed to extract (deleted the cached copy - re-run to re-download): $_"
    }
    New-Item -ItemType Directory -Force (Split-Path $templatesDir) | Out-Null
    if (Test-Path $templatesDir) { Remove-Item -Recurse -Force $templatesDir }
    Move-Item (Join-Path $tmp "templates") $templatesDir
    if (-not (Test-Path (Join-Path $templatesDir "windows_release_x86_64.exe"))) { Fail "template install did not produce windows_release_x86_64.exe" }
}

# --- Export ---------------------------------------------------------------------
Write-Host "[export] building Windows client export..." -ForegroundColor Cyan
# Clean first: the Steam depot maps this directory recursively with LocalPath "*", so any
# stale file here (old data_* payload, a renamed exe, a stray log) ships to players; and a
# leftover exe from a previous export would pass the Test-Path below even if today's export
# silently produced nothing.
if (Test-Path $exportDir) { Remove-Item -Recurse -Force $exportDir }
New-Item -ItemType Directory -Force $exportDir | Out-Null
$exportStart = Get-Date
& $GodotExe --headless --path $root --export-release "WindowsClient" "build/windows-client/game.exe" | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "godot --export-release failed (exit $LASTEXITCODE)" }
if (-not (Test-Path $exportBin)) { Fail "export produced no binary at $exportBin" }
if ((Get-Item $exportBin).LastWriteTime -lt $exportStart) { Fail "binary at $exportBin predates this export run - the export silently produced nothing" }

$dataDir = Get-ChildItem $exportDir -Directory | Where-Object Name -like "data_*"
if (-not $dataDir) { Fail "export produced no data_* directory (mono runtime payload missing)" }

# --- Steam native library --------------------------------------------------------
# SteamService.ResolveNative probes beside the running executable in an exported build (the
# res:// copy globalizes into the pack and File.Exists is false there - see
# scripts/net/steam/SteamService.cs). The Godot export's include_filter does not reliably
# carry a raw DLL that must sit beside the exe, so copy it explicitly post-export and fail the
# build if it's missing - a Steam-less client must never ship (and upload) silently.
$steamDllDst = Join-Path $exportDir "steam_api64.dll"
if (-not (Test-Path $steamDllSrc)) { Fail "missing Steam native library source at $steamDllSrc" }
Copy-Item -Path $steamDllSrc -Destination $steamDllDst -Force
if (-not (Test-Path $steamDllDst)) { Fail "steam_api64.dll did not land beside $exportBin after copy" }
Write-Host "[export] steam_api64.dll present beside $exportBin" -ForegroundColor Green

# --- steam_appid.txt must NOT ship in the depot ----------------------------------
# STEAM-1 ruling (2026-09-04). steam_appid.txt is a DEVELOPMENT convenience: it lets a build
# started outside Steam identify itself (SteamService.TryAppIdFile probes beside the exe). It
# is gitignored and must stay out of the depot, for two reasons:
#   1. It OVERRIDES Valve's own SteamAppId/SteamGameId hand-off env for anything that reads the
#      file first, so a stale or wrong number shipped beside the exe outlives every later App ID
#      change - and the failure surfaces on a tester's machine, not here.
#   2. Valve's own guidance is that the file is for development and is not distributed.
# A local NON-Steam run is unaffected: drop your own steam_appid.txt beside the exported exe
# (gitignored, never committed), or pass `--steam-app-id <id>`, or set SteamAppId in the shell.
# This directory is wiped and re-exported above, so a hit here means the export itself carried
# the file in - which is exactly what this guard exists to catch.
$strayAppIdFiles = Get-ChildItem $exportDir -Recurse -File -Filter "steam_appid.txt"
if ($strayAppIdFiles) {
    Fail "steam_appid.txt is in the export at $($strayAppIdFiles[0].FullName) - it is a dev-only override and must never ship in a Steam depot (see the comment above this check)"
}
Write-Host "[export] no steam_appid.txt in the export - dev-only override correctly excluded" -ForegroundColor Green

$sizeMb = [math]::Round((Get-ChildItem $exportDir -Recurse | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "[export] OK - $exportBin ($sizeMb MB total)" -ForegroundColor Green
exit 0
