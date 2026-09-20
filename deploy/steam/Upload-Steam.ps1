<#
.SYNOPSIS
    Builds the SteamPipe app/depot scripts from the checked-in templates and runs steamcmd to
    upload the Windows client build. Real Steamworks IDs are passed as parameters (or env vars),
    never committed to the templates.

.DESCRIPTION
    Requires:
      - build/windows-client/game.exe already exported (run Export-WindowsClient.ps1 first).
      - steamcmd on PATH (or pass -SteamCmd with a full path) - Steamworks SDK Tools install.
      - A Steamworks account with publishing rights on the target AppID. First run per machine
        will prompt interactively for a Steam Guard code; steamcmd caches the session after that.

.EXAMPLE
    powershell -File deploy\steam\Upload-Steam.ps1 -Username myaccount -AppId 1234560 -DepotIdClient 1234561

.EXAMPLE
    # Dry run: renders the SteamPipe scripts and stops - never calls steamcmd, never tags,
    # never touches BUILD-LOG.md. Useful to sanity-check the pipeline without an account/App ID.
    powershell -File deploy\steam\Upload-Steam.ps1 -Username myaccount -AppId 1234560 -DepotIdClient 1234561 -DryRun

.EXAMPLE
    # Two-depot dry run (Windows + macOS). -DepotIdMac is optional; without it the rendered
    # app_build.vdf is byte-identical to the Windows-only pipeline.
    powershell -File deploy\steam\Upload-Steam.ps1 -Username myaccount -AppId 1234560 -DepotIdClient 1234561 -DepotIdMac 1234562 -DryRun
#>
[CmdletBinding()]
param(
    [string]$SteamCmd = "steamcmd.exe",
    [Parameter(Mandatory)] [string]$Username,
    [Parameter(Mandatory)] [string]$AppId,
    [Parameter(Mandatory)] [string]$DepotIdClient,
    # Optional second depot for the macOS client. Omit for a Windows-only upload: the app_build
    # script then contains exactly the one client depot, byte-identical to the pre-Mac pipeline.
    # A LIVE upload with this set is refused from Windows - see the guard below.
    [string]$DepotIdMac = "",
    [string]$Description = "Watis World client build $(Get-Date -Format s)",
    # Renders the SteamPipe scripts but skips steamcmd entirely - and, since tagging/logging
    # only ever runs after a real steamcmd success below, a dry run can never tag or log.
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$steamDir = $PSScriptRoot
$genDir = Join-Path $steamDir "_generated"
$clientExe = Join-Path $root "build\windows-client\game.exe"
$buildInfoPath = Join-Path $root "scripts\BuildInfo.cs"
$buildLogPath = Join-Path $steamDir "BUILD-LOG.md"

function Fail([string]$Message) {
    Write-Host "FAIL: $Message" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $clientExe)) {
    Fail "no client build at $clientExe - run deploy\Export-WindowsClient.ps1 first"
}

# The extra Depots line for the mac depot, injected into app_build.vdf only when -DepotIdMac is
# given. Windows-only uploads render this to empty, leaving the app_build script unchanged.
$macDepotLine = ""
if ($DepotIdMac) {
    $macDepotLine = "`n`t`t`"$DepotIdMac`"`t`"depot_build_mac.vdf`""
}

function Render-Template([string]$TemplateName, [string]$OutName) {
    # -Encoding UTF8 on the READ as well as the write. PowerShell 5.1's Get-Content assumes the
    # host ANSI codepage for a BOM-less file, so the templates' em-dashes came back as three
    # Windows-1252 characters; that used to cancel out against an equally-ANSI Set-Content, and
    # stopped cancelling the moment the write was corrected. Both halves have to agree.
    $text = Get-Content (Join-Path $steamDir $TemplateName) -Raw -Encoding UTF8
    $text = $text.Replace("%APP_ID%", $AppId).Replace("%DEPOT_ID_CLIENT%", $DepotIdClient).Replace("%BUILD_DESC%", $Description)
    $text = $text.Replace("%DEPOT_ID_MAC%", $DepotIdMac).Replace("%DEPOT_MAC_LINE%", $macDepotLine)
    # ABSOLUTE content roots. steamcmd resolves a RELATIVE ContentRoot against its own install
    # directory, NOT against the .vdf -- so the old "..\..\..uild\" only worked when
    # steamcmd.exe happened to sit three levels above the build. With steamcmd installed in the
    # user profile it resolved to C:uild\windows-client\ and failed with "Invalid content
    # root". Measured 2026-09-05 on the first real upload. Absolute paths cannot drift.
    $text = $text.Replace("%CONTENT_ROOT%",        (Join-Path $root "build"))
    $text = $text.Replace("%BUILD_OUTPUT%",        (Join-Path $root "build\steam-output"))
    $text = $text.Replace("%CONTENT_ROOT_CLIENT%", (Join-Path $root "build\windows-client"))
    $text = $text.Replace("%CONTENT_ROOT_MAC%",    (Join-Path $root "build\mac-client\extracted"))
    # Not Set-Content: PowerShell 5.1 writes the host ANSI codepage, which mangles every
    # non-ASCII character on the way through (.claude/rules/imports-and-encoding.md). The
    # templates carry em-dashes in their comments, and -Description is free text a human types,
    # so this round trip is the one place a real Steamworks build description could get
    # corrupted. Write UTF-8 without a BOM - steamcmd's vdf parser chokes on a BOM.
    [System.IO.File]::WriteAllText((Join-Path $genDir $OutName), $text, (New-Object System.Text.UTF8Encoding($false)))
}

Write-Host "[steam] rendering build scripts for AppID $AppId / depot $DepotIdClient$(if ($DepotIdMac) { " + mac depot $DepotIdMac" })..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $genDir | Out-Null
Render-Template "app_build.vdf.template" "app_build.vdf"
Render-Template "depot_build_client.vdf.template" "depot_build_client.vdf"
if ($DepotIdMac) { Render-Template "depot_build_mac.vdf.template" "depot_build_mac.vdf" }

$appBuildPath = Join-Path $genDir "app_build.vdf"

if ($DryRun) {
    Write-Host "[steam] DRY RUN - rendered $appBuildPath; skipping steamcmd, git tag, and BUILD-LOG.md." -ForegroundColor Yellow
    Write-Host "[steam] would run: $SteamCmd +login $Username +run_app_build $appBuildPath +quit" -ForegroundColor Yellow
    Get-Content $appBuildPath | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    exit 0
}

# --- macOS depot: not uploadable from this host ---------------------------------------------
# Contents/MacOS/watis_game is mode 0755 inside deploy\Export-MacClient.ps1's .zip, and NTFS
# has nowhere to keep that bit; a depot built from a Windows-side extraction ships a binary the
# Mac cannot execute, and the failure only shows up on a tester's machine. Whether Windows
# steamcmd can carry the bit some other way is UNVERIFIED (verifying it costs a real upload), so
# this refuses rather than guesses. Extract and push the mac depot from WSL's Linux steamcmd -
# deploy\steam\depot_build_mac.vdf.template carries the commands.
if ($DepotIdMac) {
    Fail @"
-DepotIdMac is set, but a LIVE mac upload from Windows is refused on purpose.

The macOS bundle's executable bit (Contents/MacOS/watis_game, mode 0755) does not survive an
NTFS extraction, and a depot without it installs an app no Mac can launch. Push the mac depot
from WSL with Linux steamcmd instead - deploy\steam\depot_build_mac.vdf.template has the
extraction command and the ContentRoot note.

Re-run with -DryRun to render both depot scripts (that is supported and is how the two-depot
app_build.vdf gets checked), or drop -DepotIdMac to upload the Windows depot only.
"@
}

# PRECONDITION: steamcmd must ALREADY have cached credentials for $Username on this machine.
# The pipeline below (Tee-Object) captures stdout, which SWALLOWS steamcmd's interactive password
# prompt -- it will sit waiting for a password it never visibly asked for, and look like a hang.
# Measured 2026-09-05 on the first real upload. Log in once, directly, outside this script:
#     C:\path\to\steamcmd.exe +login <username>
# enter the password and Steam Guard code, then `quit`. Every later run reads the cache and needs
# no prompt. Do NOT "fix" this by removing the Tee -- the captured output is what the phantom-
# success guard below reads, and that guard is load-bearing.
Write-Host "[steam] running steamcmd (requires cached credentials - see the note above)..." -ForegroundColor Cyan
# Capture the output as well as the exit code: steamcmd's exit codes are famously unreliable
# (0 on some logical run_app_build failures, non-zero on some successes), so the structured
# success line — "successfully finished appID ... build (BuildID ...)" — is the real verdict.
$steamOut = & $SteamCmd +login $Username +run_app_build $appBuildPath +quit 2>&1 | Tee-Object -Variable steamLines | ForEach-Object { Write-Host $_; $_ }
$buildIdMatch = [regex]::Match(($steamLines -join "`n"), '[Bb]uild[Ii][Dd]\s*[:\s]\s*(\d+)')
if ($LASTEXITCODE -ne 0 -and -not $buildIdMatch.Success) { Fail "steamcmd exited with code $LASTEXITCODE and no BuildID in its output" }
if (-not $buildIdMatch.Success) { Fail "steamcmd exited 0 but printed no BuildID - treat as a failed upload (phantom-success guard)" }
$buildId = $buildIdMatch.Groups[1].Value

Write-Host "[steam] OK - BuildID $buildId uploaded to AppID $AppId. Check Steamworks > SteamPipe > Builds to set it live on a branch." -ForegroundColor Green

# --- Tag + build log (only ever reached after steamcmd above has actually succeeded) --------
if (-not (Test-Path $buildInfoPath)) { Fail "cannot tag release - missing $buildInfoPath" }
$versionMatch = Select-String -Path $buildInfoPath -Pattern 'Version\s*=\s*"([^"]+)"' | Select-Object -First 1
if (-not $versionMatch) { Fail "could not parse BuildInfo.Version out of $buildInfoPath" }
$version = $versionMatch.Matches[0].Groups[1].Value
$tag = "v$version"
$sha = (git -C $root rev-parse --short HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sha) { Fail "could not resolve git short SHA for the build log" }
# The tag lands on HEAD, so HEAD must actually be what was built: a dirty tree (or commits
# pulled between export and upload) would tag code that was never in the uploaded binary.
# `git status --porcelain` prints NOTHING on a clean tree, so this pipeline yields $null and
# .Trim() throws "You cannot call a method on a null-valued expression" -- the dirty-tree
# guard crashing on the clean case it exists to bless. Measured 2026-09-05, AFTER a
# successful upload (BuildID 25145262): the build shipped, the tag and log did not.
# Out-String always returns a string, empty or not.
$dirty = (git -C $root status --porcelain | Out-String).Trim()
if ($dirty) { Fail "working tree is dirty - the $tag tag would not describe the uploaded binary. Commit/stash first, re-export, then upload." }
$isoDate = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")

git -C $root tag -a $tag -m "Steam release $tag ($Description) [BuildID $buildId]"
if ($LASTEXITCODE -ne 0) { Fail "git tag $tag failed (already exists? run 'git tag -l `"v*`"' to check)" }
Write-Host "[steam] tagged $tag" -ForegroundColor Green

if (-not (Test-Path $buildLogPath)) {
    Set-Content -Path $buildLogPath -Value "# Steam build log`n`nOne row per successful SteamPipe upload (written by deploy\steam\Upload-Steam.ps1).`n`n| Version | Date | SHA | BuildID | ExeSHA256 | Description |`n|---|---|---|---|---|---|"
}
# The exe hash answers "which binary did testers actually get" after the fact.
$exeHash = (Get-FileHash -Algorithm SHA256 $clientExe).Hash.Substring(0, 16)
Add-Content -Path $buildLogPath -Value "| $version | $isoDate | $sha | $buildId | $exeHash | $Description |"
Write-Host "[steam] appended build log entry to $buildLogPath" -ForegroundColor Green

exit 0
