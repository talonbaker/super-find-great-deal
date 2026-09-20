<#
.SYNOPSIS
    Exports the headless Linux match-server build (build/linux-server/). Installs the
    Godot 4.7 mono export templates automatically if missing.

.DESCRIPTION
    Fully automated - no editor interaction. The one-time template install downloads
    ~1.1 GB from the official Godot GitHub release, so the first run is slow; every
    subsequent run only re-exports (~seconds).

    NOTE (Steam-native era): nothing deploys this build today — match servers are
    spawned client-locally by the hosting player (see docs/STEAM.md), and the old
    phonebook Docker image is retired. Kept for a possible future Linux dedicated-server
    host; that host would also need the Linux Steamworks native library shipped beside
    the binary (thirdparty/steamworks/linux-x64) for --transport steam to work.
#>
[CmdletBinding()]
param(
    [string]$GodotExe = "Godot_v4.7-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$templatesDir = Join-Path $env:APPDATA "Godot\export_templates\4.7.stable.mono"
$templatesUrl = "https://github.com/godotengine/godot/releases/download/4.7-stable/Godot_v4.7-stable_mono_export_templates.tpz"
$exportDir = Join-Path $root "build\linux-server"
$exportBin = Join-Path $exportDir "watis_game_server.x86_64"

function Fail([string]$Message) {
    Write-Host "FAIL: $Message" -ForegroundColor Red
    exit 1
}

# --- Export templates (one-time install) ---------------------------------------
if (-not (Test-Path (Join-Path $templatesDir "linux_release.x86_64"))) {
    Write-Host "[export] Godot 4.7 mono export templates not installed - downloading (~1.1 GB, one time)..." -ForegroundColor Yellow
    $tpz = Join-Path $env:TEMP "godot-4.7-mono-templates.tpz"
    if (-not (Test-Path $tpz)) {
        # Download to a .partial name and rename on success (same guard as the client
        # export): a truncated cached .tpz otherwise wedges every later run.
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
    if (-not (Test-Path (Join-Path $templatesDir "linux_release.x86_64"))) { Fail "template install did not produce linux_release.x86_64" }
}

# --- Export ---------------------------------------------------------------------
Write-Host "[export] building Linux server export..." -ForegroundColor Cyan
# Clean first + freshness check, same rationale as the client export: stale files must not
# ride along, and a leftover binary must not pass for today's build.
if (Test-Path $exportDir) { Remove-Item -Recurse -Force $exportDir }
New-Item -ItemType Directory -Force $exportDir | Out-Null
$exportStart = Get-Date
& $GodotExe --headless --path $root --export-release "LinuxServer" "build/linux-server/watis_game_server.x86_64" | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "godot --export-release failed (exit $LASTEXITCODE)" }
if (-not (Test-Path $exportBin)) { Fail "export produced no binary at $exportBin" }
if ((Get-Item $exportBin).LastWriteTime -lt $exportStart) { Fail "binary at $exportBin predates this export run - the export silently produced nothing" }

$dataDir = Get-ChildItem $exportDir -Directory | Where-Object Name -like "data_*"
if (-not $dataDir) { Fail "export produced no data_* directory (mono runtime payload missing)" }

# --- Steam native library --------------------------------------------------------
# Mirror of the Windows export's post-export copy: SteamService.ResolveNative probes beside
# the executable in an exported build, so a future Linux dedicated host running with
# --transport steam would fail AT RUNTIME, not at export time, without this. Cheap now,
# silent-ship failure later.
$steamSoSrc = Join-Path $root "thirdparty\steamworks\linux-x64\libsteam_api.so"
$steamSoDst = Join-Path $exportDir "libsteam_api.so"
if (-not (Test-Path $steamSoSrc)) { Fail "missing Steam native library source at $steamSoSrc" }
Copy-Item -Path $steamSoSrc -Destination $steamSoDst -Force
if (-not (Test-Path $steamSoDst)) { Fail "libsteam_api.so did not land beside $exportBin after copy" }
Write-Host "[export] libsteam_api.so present beside $exportBin" -ForegroundColor Green

$sizeMb = [math]::Round((Get-ChildItem $exportDir -Recurse | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "[export] OK - $exportBin ($sizeMb MB total)" -ForegroundColor Green
exit 0
