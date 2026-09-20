<#
.SYNOPSIS
    Exports the macOS client (build/mac-client/watis_game.zip) from this Windows machine.
    Shaped like Export-WindowsClient.ps1: fully automated, no editor interaction, installs the
    Godot 4.7 mono export templates if missing (one .tpz carries every platform's templates).

.DESCRIPTION
    Measured facts this script is built on (2026-08-28, STEAM-MAC-1):

      - The stock Godot 4.7 mono macOS template ships ONE binary,
        macos_template.app/Contents/MacOS/godot_macos_release.universal. There is no
        godot_macos_release.x86_64, so `binary_format/architecture` MUST be "universal";
        an x86_64-only preset fails with "Requested template binary ... not found".
      - Godot refuses a universal (or arm64) macOS export unless the PROJECT setting
        rendering/textures/vram_compression/import_etc2_astc is true. That setting is
        deliberately false on the trunk with an ART-BIBLE §4.4 guard comment beside it, so
        this script does not flip it - it checks it and tells you exactly what to change.
        See docs/agents/roles/programming/outbox/2026-08-28-STEAM-MAC-1-report.md.
      - Signing is Godot's BUILT-IN ad-hoc signer (codesign/codesign=1), which runs on a
        Windows host with no external tool. rcodesign is NOT an option here: Godot itself
        refuses it for .NET/GDExtension exports ("'rcodesign' doesn't support signing
        applications with embedded dynamic libraries"). No Apple Developer account exists,
        so notarisation is out and the signature stays ad-hoc.
#>
[CmdletBinding()]
param(
    [string]$GodotExe = "Godot_v4.7-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$templatesDir = Join-Path $env:APPDATA "Godot\export_templates\4.7.stable.mono"
$templatesUrl = "https://github.com/godotengine/godot/releases/download/4.7-stable/Godot_v4.7-stable_mono_export_templates.tpz"
$exportDir = Join-Path $root "build\mac-client"
$exportZip = Join-Path $exportDir "watis_game.zip"
$buildInfoPath = Join-Path $root "scripts\BuildInfo.cs"
$presetsPath = Join-Path $root "export_presets.cfg"
$projectPath = Join-Path $root "project.godot"

function Fail([string]$Message) {
    Write-Host "FAIL: $Message" -ForegroundColor Red
    exit 1
}

# --- Precondition: the ETC2/ASTC project setting the universal export requires ---------------
# Checked rather than flipped: project.godot carries an explicit ART-BIBLE §4.4 guard comment
# on this line, and changing it is Talon's call, not this script's. Failing here with the exact
# remediation beats Godot's own error, which names an editor menu path that does not exist on a
# headless run.
if (-not (Test-Path $projectPath)) { Fail "missing $projectPath" }
$projectText = Get-Content $projectPath -Raw
if ($projectText -notmatch 'textures/vram_compression/import_etc2_astc\s*=\s*true') {
    Fail @"
macOS export is blocked by a project setting, not by anything in this script.

  project.godot: rendering/textures/vram_compression/import_etc2_astc = false

Godot 4.7's only macOS export template is a UNIVERSAL binary, and it refuses to export
universal/arm64 while that setting is false. Setting it to true makes Godot import an
additional ASTC variant for the 17 textures in this tree that use VRAM compression
(compress/mode=2); the desktop path still selects s3tc/bptc, which is what ART-BIBLE §4.4
is protecting. The line carries a guard comment, so flipping it is a design call - see
docs/agents/roles/programming/outbox/2026-08-28-STEAM-MAC-1-report.md, "Open questions".
"@
}

# --- Version: BuildInfo.cs is the single source of truth ------------------------------------
# CFBundleShortVersionString / CFBundleVersion want dotted integers, so the CalVer date maps
# the same way the Windows quad does ("2026.07.16" -> "2026.7.16"). A same-day "-N" suffix has
# no representable slot in a CFBundle version and is deliberately dropped - Steam versions a
# playtest build by BuildID, not by CFBundleVersion.
function Get-BuildVersion {
    if (-not (Test-Path $buildInfoPath)) { Fail "cannot stamp version - missing $buildInfoPath" }
    $match = Select-String -Path $buildInfoPath -Pattern 'Version\s*=\s*"([^"]+)"' | Select-Object -First 1
    if (-not $match) { Fail "could not parse BuildInfo.Version out of $buildInfoPath" }
    return $match.Matches[0].Groups[1].Value
}

function ConvertTo-MacVersion([string]$Version) {
    $date = $Version
    if ($Version -match '^(.*)-(\d+)$') { $date = $Matches[1] }
    $parts = $date.Split(".")
    if ($parts.Count -ne 3) { Fail "BuildInfo.Version '$Version' is not date-based CalVer YYYY.MM.DD[-N]" }
    $ints = foreach ($p in $parts) {
        if ($p -notmatch '^\d+$') { Fail "BuildInfo.Version '$Version' has a non-numeric part ('$p')" }
        [string][int]$p   # strip leading zeros: "07" -> "7"
    }
    return ($ints -join ".")
}

$buildVersion = Get-BuildVersion
$macVersion = ConvertTo-MacVersion $buildVersion
Write-Host "[export] BuildInfo.Version=$buildVersion -> CFBundleShortVersionString $macVersion" -ForegroundColor Cyan

if (-not (Test-Path $presetsPath)) { Fail "missing $presetsPath" }

# Windows PowerShell 5.1 mangles UTF-8 on a Get-Content/Set-Content round trip (see
# .claude/rules/imports-and-encoding.md). export_presets.cfg is deliberately pure ASCII - the
# macOS preset's copyright string is "(c) 2026 Talon", not the glyph - so the round trip below
# is safe. Assert it rather than trust it: a single smart quote pasted into the .cfg would
# otherwise be silently corrupted by this script.
$presetBytes = [System.IO.File]::ReadAllBytes($presetsPath)
$nonAscii = $presetBytes | Where-Object { $_ -gt 127 } | Select-Object -First 1
if ($null -ne $nonAscii) {
    Fail "export_presets.cfg contains non-ASCII bytes; PowerShell 5.1 would corrupt them on the version stamp below. Keep the file ASCII-only (write '(c)' rather than the copyright glyph)."
}

$presetsText = Get-Content $presetsPath -Raw
# "application/version" and "application/short_version" exist only in the macOS preset;
# "application/file_version"/"application/product_version" (Windows) do not contain either
# string as a substring, so these two replacements cannot cross-hit the Windows preset.
$stampedText = $presetsText `
    -replace 'application/short_version="[^"]*"', "application/short_version=`"$macVersion`"" `
    -replace 'application/version="[^"]*"', "application/version=`"$macVersion`""
[System.IO.File]::WriteAllText($presetsPath, $stampedText, (New-Object System.Text.UTF8Encoding($false)))

$verifyText = Get-Content $presetsPath -Raw
foreach ($key in @("application/short_version", "application/version")) {
    $m = [regex]::Match($verifyText, [regex]::Escape($key) + '="([^"]*)"')
    if (-not $m.Success -or $m.Groups[1].Value -ne $macVersion) {
        Fail "version drift: export_presets.cfg $key is '$($m.Groups[1].Value)', expected '$macVersion' (from BuildInfo.Version=$buildVersion)"
    }
}
Write-Host "[export] version stamp verified - export_presets.cfg matches BuildInfo.Version" -ForegroundColor Green

# --- Export templates (one-time install, shared with the Windows/Linux exports) --------------
if (-not (Test-Path (Join-Path $templatesDir "macos.zip"))) {
    Write-Host "[export] Godot 4.7 mono export templates not installed - downloading (~1.1 GB, one time)..." -ForegroundColor Yellow
    $tpz = Join-Path $env:TEMP "godot-4.7-mono-templates.tpz"
    if (-not (Test-Path $tpz)) {
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
    if (-not (Test-Path (Join-Path $templatesDir "macos.zip"))) { Fail "template install did not produce macos.zip" }
}

# --- Export ---------------------------------------------------------------------------------
Write-Host "[export] building macOS client export (universal, built-in ad-hoc signature)..." -ForegroundColor Cyan
# Clean first, for the same reason Export-WindowsClient.ps1 does: a leftover .zip from a
# previous run would pass the Test-Path below even if today's export silently produced nothing.
if (Test-Path $exportDir) { Remove-Item -Recurse -Force $exportDir }
New-Item -ItemType Directory -Force $exportDir | Out-Null
$exportStart = Get-Date
& $GodotExe --headless --path $root --export-release "MacOSClient" "build/mac-client/watis_game.zip" | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "godot --export-release failed (exit $LASTEXITCODE)" }
if (-not (Test-Path $exportZip)) { Fail "export produced no archive at $exportZip" }
if ((Get-Item $exportZip).LastWriteTime -lt $exportStart) { Fail "archive at $exportZip predates this export run - the export silently produced nothing" }

# --- Verify the bundle inside the archive ----------------------------------------------------
# The Windows script checks a directory; here the whole bundle is one .zip, and the .zip is the
# shipping form precisely because it is the only container that carries the POSIX executable bit
# off an NTFS host. So the checks read the central directory instead of the filesystem.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($exportZip)
try {
    $names = $zip.Entries | ForEach-Object { $_.FullName }
    $binEntry = $zip.Entries | Where-Object { $_.FullName -eq "watis_game.app/Contents/MacOS/watis_game" }
    if (-not $binEntry) { Fail "no watis_game.app/Contents/MacOS/watis_game in $exportZip" }

    # Executable bit: stored in the high 16 bits of the zip external attributes. Godot writes
    # 0755 here; if it ever stops, every Mac tester gets "you do not have permission" and the
    # Steam depot ships a file nobody can run - worth failing the build over.
    try {
        $mode = ($binEntry.ExternalAttributes -shr 16) -band 0xFFF
        if (($mode -band 0x49) -ne 0x49) { Fail ("main binary is mode 0{0} in the archive - the POSIX executable bit is not set" -f [Convert]::ToString($mode, 8)) }
        Write-Host ("[export] main binary mode 0{0} (executable bit set)" -f [Convert]::ToString($mode, 8)) -ForegroundColor Green
    } catch [System.Management.Automation.PropertyNotFoundException] {
        Write-Host "[export] WARN - this PowerShell cannot read zip external attributes; verify the executable bit under WSL (see the packet report)." -ForegroundColor Yellow
    }

    if ($names -notcontains "watis_game.app/Contents/_CodeSignature/CodeResources") {
        Fail "no _CodeSignature/CodeResources in the bundle - the ad-hoc signature did not happen (check codesign/codesign=1 in the MacOSClient preset)"
    }
    if (-not ($names | Where-Object { $_ -like "watis_game.app/Contents/Resources/data_*_macos_*/*" })) {
        Fail "no data_*_macos_* directory in the bundle (the .NET runtime payload is missing)"
    }
    # The Steam native library. On Windows this is copied beside the exe post-export; on macOS
    # that is not possible without invalidating the bundle's code signature, and it turns out
    # not to be needed: `dotnet publish` already places libsteam_api.dylib beside the managed
    # assemblies in each data_*_macos_* directory, which is inside the signed bundle. Fail if it
    # ever stops landing there - a Steam-less client must never ship.
    $dylibs = $names | Where-Object { $_ -like "*/data_*_macos_*/libsteam_api.dylib" }
    if (-not $dylibs) { Fail "libsteam_api.dylib is not in the bundle - a Steam-less macOS client must not ship" }
    Write-Host "[export] libsteam_api.dylib present in $($dylibs.Count) runtime payload(s)" -ForegroundColor Green

    $plist = $zip.Entries | Where-Object { $_.FullName -eq "watis_game.app/Contents/Info.plist" }
    if (-not $plist) { Fail "no Info.plist in the bundle" }
    $reader = New-Object System.IO.StreamReader($plist.Open())
    $plistText = $reader.ReadToEnd()
    $reader.Dispose()
    if ($plistText -notmatch [regex]::Escape("<string>$macVersion</string>")) {
        Fail "Info.plist does not carry version $macVersion - the export used a stale preset"
    }
    # audio/driver/enable_input is true in project.godot, so the bundle opens the microphone.
    # Without NSMicrophoneUsageDescription macOS terminates the process at that moment rather
    # than prompting - a crash that only ever reproduces on a Mac.
    if ($plistText -notmatch "NSMicrophoneUsageDescription") {
        Fail "Info.plist has no NSMicrophoneUsageDescription, but the project enables audio input - macOS would kill the app when voice chat opens the mic"
    }
    Write-Host "[export] Info.plist OK (version $macVersion, microphone usage description present)" -ForegroundColor Green
} finally {
    $zip.Dispose()
}

$sizeMb = [math]::Round((Get-Item $exportZip).Length / 1MB)
Write-Host "[export] OK - $exportZip ($sizeMb MB)" -ForegroundColor Green
Write-Host "[export] Signature: ad-hoc (no Apple Developer account). Steam-delivered content is not" -ForegroundColor Yellow
Write-Host "[export] quarantined, so testers who install through Steam launch it normally; a tester who" -ForegroundColor Yellow
Write-Host "[export] downloads this .zip from a browser or chat instead gets one Gatekeeper block and" -ForegroundColor Yellow
Write-Host "[export] must right-click the app > Open > Open to run it." -ForegroundColor Yellow
exit 0
