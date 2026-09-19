<#
Shared helpers for the Sail foundation's headless test scripts. Dot-source with:
    . "$PSScriptRoot\_Common.ps1"
#>

$script:Root = Split-Path $PSScriptRoot -Parent
$script:LogDir = Join-Path $PSScriptRoot "logs"

# Are we on Windows? $IsWindows is an automatic variable in PowerShell 6+ (pwsh). Under
# Windows PowerShell 5.1 it does not exist and evaluates to $null - and 5.1 only ships on
# Windows, so a $null value unambiguously means "Windows". This is the single branch point
# for every OS-specific path below; the Windows behaviour must stay byte-for-byte identical.
$script:OnWindows = ($null -eq $IsWindows) -or $IsWindows

# Godot binary resolution. Honour an explicit override first so CI (and any non-standard local
# install) can point at a downloaded headless build; then fall back to the historical Windows
# console build, or the Linux headless binary name the CI workflow downloads. The two env vars
# are both accepted because SAIL_* is this repo's convention and GODOT_BIN is a common one.
function Resolve-GodotExe {
    if (-not [string]::IsNullOrWhiteSpace($env:SAIL_GODOT)) { return $env:SAIL_GODOT }
    if (-not [string]::IsNullOrWhiteSpace($env:GODOT_BIN))  { return $env:GODOT_BIN }
    if ($script:OnWindows) { return "Godot_v4.7-stable_mono_win64_console.exe" }
    # Linux mono build's binary name (inside Godot_v4.7-stable_mono_linux_x86_64/). CI sets
    # SAIL_GODOT to the absolute path, so this bare name is only a last-resort PATH/CWD fallback.
    return "Godot_v4.7-stable_mono_linux.x86_64"
}
$script:GodotExe = Resolve-GodotExe

function Write-Fail([string]$Message) {
    Write-Host ""
    Write-Host "FAIL: $Message" -ForegroundColor Red
    exit 1
}

function Reset-LogDir {
    if (Test-Path $script:LogDir) { Remove-Item -Recurse -Force $script:LogDir }
    New-Item -ItemType Directory -Path $script:LogDir | Out-Null
}

function Invoke-BuildAndImport {
    Write-Host "  building godot project..." -ForegroundColor DarkGray
    dotnet build (Join-Path $script:Root "SuperFindGreatDeal.csproj") --nologo -v quiet | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet build (game) failed" }
    Write-Host "  godot import..." -ForegroundColor DarkGray
    & $script:GodotExe --headless --path $script:Root --import | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Fail "godot --import failed" }
}

# Start a Godot process with the given user args (everything after --). Caches the
# process Handle so ExitCode is readable after exit (PowerShell 5.1 quirk).
function Start-Godot([string[]]$UserArgs, [string]$Tag) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $err = Join-Path $script:LogDir "$Tag.err.log"
    $args = @("--headless", "--path", $script:Root, "--") + $UserArgs
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList $args `
        -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

function Wait-ForLogLine([string]$File, [string]$Pattern, [int]$TimeoutSec = 30) {
    for ($i = 0; $i -lt ($TimeoutSec * 2); $i++) {
        Start-Sleep -Milliseconds 500
        if ((Test-Path $File) -and (Select-String -Path $File -Pattern $Pattern -Quiet)) { return $true }
    }
    return $false
}

function Wait-ForExit($Process, [int]$TimeoutSec) {
    return $Process.WaitForExit($TimeoutSec * 1000)
}

function Stop-Proc($Process) {
    if ($Process -and -not $Process.HasExited) {
        try { Stop-Process -Id $Process.Id -Force -ErrorAction Stop } catch {}
    }
}

function Stop-Procs($Processes) {
    foreach ($p in $Processes) { Stop-Proc $p }
}

# --- Machine-wide full-suite mutex (issue #44) --------------------------------------------
# Concurrent Run-AllTests.ps1 invocations share this repo's tests/logs directory (Reset-LogDir
# wipes it out from under a sibling run) AND every suite's hardcoded UDP ports, so two full
# suites racing on the same machine — including across separate git worktrees, which is exactly
# how dispatched agents run — corrupt each other's logs and fight over ports/CPU. That
# contention is the prime suspect for #44's one-off net-sim/anti-cheat failure (parallel-agent
# Godot instances on the same machine; passed clean on immediate re-run).
#
# A Windows named Mutex ("Global\...") is process- and worktree-agnostic by construction (a
# kernel object namespace, not a filesystem path) — every worktree opens the SAME mutex by
# name, unlike a lock file which would need one shared path everyone agrees on. The OS itself
# reclaims an abandoned mutex if the holder crashes (Mutex.WaitOne throws
# AbandonedMutexException but grants ownership to the caller), so no stale-lock-file heuristic
# is needed for crash safety; a generous WaitTimeoutMinutes remains as a backstop for the
# non-crash case (a holder that is simply still running, or wedged).
#
# On non-Windows there is no cross-process named-mutex namespace we can rely on identically
# (.NET named mutexes on Unix are backed by the temp dir and reject the "Global\" prefix), so
# the pwsh-Linux path below uses an exclusive lock FILE on a fixed machine-wide temp path
# instead - same machine-wide, worktree-agnostic guarantee (every worktree opens the SAME
# absolute path), and the OS closing a crashed holder's file handle gives the same crash
# recovery the Windows abandoned-mutex behaviour provides. The one reduced guarantee versus
# Windows: the lock is machine-local only (no network-share semantics), which is irrelevant
# here since all contending worktrees live on one machine. In CI there is a single runner and
# a single worktree, so the lock is uncontended regardless.
$script:SuiteMutexName = "Global\Sail_FullTestSuite_Mutex"
# GetTempPath() is cross-platform (%TEMP% on Windows, /tmp on Linux); $env:TEMP is null on Linux.
$script:SuiteMutexHolderFile = Join-Path ([System.IO.Path]::GetTempPath()) "sail-fulltest-suite-holder.json"
$script:SuiteLockFile = Join-Path ([System.IO.Path]::GetTempPath()) "sail-fulltest-suite.lock"

# Best-effort human-readable description of whoever currently holds (or last held) the suite
# mutex, for the wait-progress log line and the timeout error. Never throws: holder info is a
# courtesy, not a correctness dependency.
function Get-SuiteMutexHolderDescription {
    if (-not (Test-Path $script:SuiteMutexHolderFile)) { return "(unknown - no holder info recorded)" }
    try {
        $h = Get-Content $script:SuiteMutexHolderFile -Raw | ConvertFrom-Json
        $startedUtc = [datetime]::Parse($h.StartedAtUtc, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
        $elapsedSec = [int]((Get-Date).ToUniversalTime() - $startedUtc).TotalSeconds
        return "pid $($h.Pid) on $($h.Machine) ($($h.Worktree)), running ${elapsedSec}s"
    } catch {
        return "(unknown - holder info unreadable)"
    }
}

# Records best-effort holder info for the next waiter's progress line. Never throws: the info
# is a courtesy, not a correctness dependency - the lock is already held regardless of whether
# this write lands. $env:COMPUTERNAME is null on Linux, so fall back to the resolved host name.
function Write-SuiteMutexHolderInfo {
    try {
        $machine = if (-not [string]::IsNullOrWhiteSpace($env:COMPUTERNAME)) { $env:COMPUTERNAME } else { [System.Net.Dns]::GetHostName() }
        $info = [pscustomobject]@{
            Pid          = $PID
            Machine      = $machine
            Worktree     = $script:Root
            StartedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        }
        $info | ConvertTo-Json -Compress | Set-Content -Path $script:SuiteMutexHolderFile -Encoding utf8
    } catch { }
}

# Blocks until this process holds the machine-wide full-suite lock, polling every PollSec and
# logging progress (with the current holder, if known) every ReportEverySec. Returns a handle
# (a Windows Mutex, or a Linux FileStream) to pass to Exit-SuiteMutex when done. Fails loud
# (Write-Fail) if WaitTimeoutMinutes elapses first — a genuinely stuck holder should be
# investigated, not silently forced through. Dispatches to the platform-appropriate mechanism.
function Enter-SuiteMutex([int]$WaitTimeoutMinutes = 30) {
    if ($script:OnWindows) { return Enter-SuiteMutexWindows $WaitTimeoutMinutes }
    return Enter-SuiteMutexFile $WaitTimeoutMinutes
}

# Windows path: a named kernel Mutex (unchanged from the original single-platform implementation).
function Enter-SuiteMutexWindows([int]$WaitTimeoutMinutes = 30) {
    $mutex = New-Object System.Threading.Mutex($false, $script:SuiteMutexName)
    $pollSec = 5
    $reportEverySec = 30
    $waitedSec = 0
    $sinceReport = 0
    $deadline = (Get-Date).AddMinutes($WaitTimeoutMinutes)
    $acquired = $false
    while (-not $acquired) {
        try {
            $acquired = $mutex.WaitOne($pollSec * 1000)
        } catch [System.Threading.AbandonedMutexException] {
            # A prior holder crashed while owning the mutex; the OS has already transferred
            # ownership to this thread. Treat as a clean acquire, but say so - an abandoned
            # mutex is evidence a suite died mid-run somewhere, worth knowing about.
            Write-Host "  (suite mutex: previous holder crashed/abandoned it - acquiring cleanly)" -ForegroundColor Yellow
            $acquired = $true
        }
        if ($acquired) { break }
        $waitedSec += $pollSec
        $sinceReport += $pollSec
        if ($sinceReport -ge $reportEverySec) {
            $sinceReport = 0
            Write-Host ("  waiting for machine-wide full-suite lock ({0}s so far) - held by: {1}" `
                -f $waitedSec, (Get-SuiteMutexHolderDescription)) -ForegroundColor Yellow
        }
        if ((Get-Date) -ge $deadline) {
            $mutex.Dispose()
            Write-Fail ("timed out after {0} minute(s) waiting for the machine-wide full-suite mutex (held by: {1}) - another full-suite run appears stuck; investigate before retrying" `
                -f $WaitTimeoutMinutes, (Get-SuiteMutexHolderDescription))
        }
    }
    Write-SuiteMutexHolderInfo
    return $mutex
}

# Non-Windows path: an exclusive lock FILE on a fixed machine-wide temp path. Opening it with
# FileShare.None means a second full-suite run's Open() throws IOException until the holder's
# stream closes; the OS closes that stream (and releases the lock) if the holder crashes, giving
# the same crash-recovery guarantee the Windows abandoned-mutex behaviour provides. Reduced
# guarantee vs Windows: machine-local only (see the $script:SuiteLockFile note above).
function Enter-SuiteMutexFile([int]$WaitTimeoutMinutes = 30) {
    $pollSec = 5
    $reportEverySec = 30
    $waitedSec = 0
    $sinceReport = 0
    $deadline = (Get-Date).AddMinutes($WaitTimeoutMinutes)
    $stream = $null
    while ($null -eq $stream) {
        try {
            $stream = [System.IO.File]::Open($script:SuiteLockFile, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        } catch [System.IO.IOException] {
            # Held by another full-suite run on this machine; wait and retry (same reporting and
            # timeout policy as the Windows path).
            $waitedSec += $pollSec
            $sinceReport += $pollSec
            if ($sinceReport -ge $reportEverySec) {
                $sinceReport = 0
                Write-Host ("  waiting for machine-wide full-suite lock ({0}s so far) - held by: {1}" `
                    -f $waitedSec, (Get-SuiteMutexHolderDescription)) -ForegroundColor Yellow
            }
            if ((Get-Date) -ge $deadline) {
                Write-Fail ("timed out after {0} minute(s) waiting for the machine-wide full-suite lock (held by: {1}) - another full-suite run appears stuck; investigate before retrying" `
                    -f $WaitTimeoutMinutes, (Get-SuiteMutexHolderDescription))
            }
            Start-Sleep -Seconds $pollSec
        }
    }
    Write-SuiteMutexHolderInfo
    return $stream
}

# Releases a lock acquired via Enter-SuiteMutex. Safe to call with $null (no-op) so a caller's
# finally block doesn't need its own null check. Handles both handle types (Windows Mutex and
# Linux FileStream) so the release path is platform-agnostic.
function Exit-SuiteMutex($Handle) {
    if ($null -eq $Handle) { return }
    try { Remove-Item -Path $script:SuiteMutexHolderFile -Force -ErrorAction SilentlyContinue } catch {}
    if ($Handle -is [System.Threading.Mutex]) {
        try { $Handle.ReleaseMutex() } catch {}
        $Handle.Dispose()
    } elseif ($Handle -is [System.IO.FileStream]) {
        try { $Handle.Dispose() } catch {}
        # Best-effort removal of the lock file itself; harmless if a racing waiter recreates it.
        try { Remove-Item -Path $script:SuiteLockFile -Force -ErrorAction SilentlyContinue } catch {}
    }
}
