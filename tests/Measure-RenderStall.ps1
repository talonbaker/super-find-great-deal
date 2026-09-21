<#
.SYNOPSIS
    STALL-1: the ~535 ms windowed render stall, one variable at a time.

.DESCRIPTION
    THIS SCRIPT ASSERTS NOTHING AND IS NOT REGISTERED in Run-AllTests.ps1 -- the standing
    Measure-CameraPacing.ps1, Measure-ShopFloor.ps1 and Probe-Capacity.ps1 have, for the same
    reason. It PRINTS A TABLE.

    It is SICK-1's rig, on SICK-1's port (7914), with SICK-1's probe, its bot, its patrol lane and
    its 45 deg/s turn -- so a row printed here is directly comparable to the rows in
    docs/agents/handoffs/2026-09-20-SICK-1.md S3 and S5. Three things it adds, each of which
    Measure-CameraPacing.ps1 structurally cannot do, which is why this is a second driver rather
    than an edit to that one (SICK-1's script stays byte-for-byte what its handoff measured):

      -EngineArgs    Godot ENGINE flags, which go BEFORE the bare `--`. Measure-CameraPacing.ps1
                     appends every extra argument AFTER it, where the engine never sees them.
                     --gpu-index, --rendering-driver and --rendering-method are all engine flags
                     and all three are rows the packet asks for, so without this the table
                     cannot be built at all. A game flag on the wrong side is swallowed and looks
                     exactly like a hang (.claude/rules/test-suite.md).

      -Foreground    Brings the client's window to the foreground and keeps checking it is still
                     there. SICK-1's capture client was an UNFOCUSED background window, and
                     Windows throttles presentation for occluded and unfocused windows. Talon
                     plays focused. Until this switch existed, every windowed number this repo
                     has ever printed was taken in the state Talon never plays in.

      -Repeat        The same row n times. Two runs of one configuration on this machine differ
                     by more than some of the effects being compared (STOCK-1 S5.5 measured
                     0.58 ms of run-to-run noise against a 0.5 ms signal), so a one-run row is
                     not evidence about a small difference. The stall is not a small difference
                     -- it is 500 ms against 16 -- but the rows AROUND it are.

    ONE GODOT AT A TIME. The machine-wide suite mutex is held for the whole table and every
    process is stopped by the pid it was started with, never by image name.

.PARAMETER Configs
    Rows to measure, each "<label>:<game args>|<engine args>". The `|` is optional; with no `|`
    the whole tail is game args, so a Measure-CameraPacing.ps1 config string works unchanged.

.EXAMPLE
    powershell -Command "& tests/Measure-RenderStall.ps1 -SkipBuild -Configs @('base:','intel:|--gpu-index 1')"
#>
[CmdletBinding()]
param(
    # 7914 -- SICK-1's port, handed out by the orchestrator. Reused deliberately: this lane is a
    # follow-up to that measurement and must not introduce a sixth port collision into this wave.
    [int]$Port = 7914,
    [double]$DurationSec = 36,
    [double]$TurnDegPerSec = 45,
    [string[]]$Configs = @("base:"),
    [string]$OutDir = "",
    [int]$Repeat = 1,
    [switch]$Foreground,
    [switch]$SkipBuild,
    [int]$MutexTimeoutMinutes = 180
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== STALL-1: the windowed render stall ===" -ForegroundColor White

if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:Root "docs\qa\2026-09-21-stall-1"
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# SetForegroundWindow, for -Foreground. Declared once; Add-Type is a no-op on a re-run in the
# same process but throws on a redefinition, so it is guarded.
if (-not ("StallWin" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class StallWin {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int max);
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
  // THE WINDOW IS NOT THE PROCESS WE STARTED. tests/_Common.ps1 resolves Godot to the _console
  // wrapper, which spawns the real engine as a CHILD (the wrapper plus its child are ONE Godot
  // launch, not two). So Process.MainWindowHandle on the object Start-Process returns is always
  // zero, which is exactly how the first attempt at -Foreground silently measured three
  // UNFOCUSED rows and labelled them focused. Enumerate every visible top-level window instead
  // and take the one whose owning pid is in the given set.
  public static IntPtr FindWindowForPids(int[] pids) {
    IntPtr found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr p) {
      if (!IsWindowVisible(h)) return true;
      int wpid; GetWindowThreadProcessId(h, out wpid);
      foreach (int want in pids) {
        if (wpid == want) {
          var sb = new System.Text.StringBuilder(256);
          GetClassName(h, sb, sb.Capacity);
          // Godot's game window class is "Engine"; the wrapper's console is "ConsoleWindowClass".
          if (sb.ToString() != "ConsoleWindowClass") { found = h; return false; }
        }
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@
}

# Every pid in the launched process's tree, so the wrapper's engine child is reachable.
function Get-ProcessTreePids([int]$RootPid) {
    $all = @($RootPid)
    $frontier = @($RootPid)
    while ($frontier.Count -gt 0) {
        $kids = @(Get-CimInstance Win32_Process -Filter ("ParentProcessId=" + ($frontier -join " OR ParentProcessId=")) -ErrorAction SilentlyContinue |
                  ForEach-Object { [int]$_.ProcessId })
        $kids = @($kids | Where-Object { $all -notcontains $_ })
        if ($kids.Count -eq 0) { break }
        $all += $kids
        $frontier = $kids
    }
    return $all
}

function Get-ForegroundPid {
    $h = [StallWin]::GetForegroundWindow()
    $out = 0
    [void][StallWin]::GetWindowThreadProcessId($h, [ref]$out)
    return $out
}

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$rows = @()
try {
    foreach ($cfg in $Configs) {
        $split = $cfg.IndexOf(":")
        $baseLabel = $cfg.Substring(0, $split)
        $tail = $cfg.Substring($split + 1).Trim()
        $gameTail = $tail
        $engineTail = ""
        $bar = $tail.IndexOf("|")
        if ($bar -ge 0) {
            $gameTail = $tail.Substring(0, $bar).Trim()
            $engineTail = $tail.Substring($bar + 1).Trim()
        }
        $gameExtra = @()
        if ($gameTail.Length -gt 0) { $gameExtra = $gameTail -split "\s+" }
        $engineExtra = @()
        if ($engineTail.Length -gt 0) { $engineExtra = $engineTail -split "\s+" }

        for ($rep = 1; $rep -le $Repeat; $rep++) {
            $label = if ($Repeat -gt 1) { "$baseLabel-r$rep" } else { $baseLabel }
            Write-Host ""
            Write-Host "--- $label (game: '$gameTail') (engine: '$engineTail') ---" -ForegroundColor Cyan
            $procs = @()
            $clientOut = Join-Path $script:LogDir "stall-$label-client.out.log"
            try {
                $serverOut = Join-Path $script:LogDir "stall-$label-server.out.log"
                $server = Start-Process -FilePath $script:GodotExe `
                    -ArgumentList @("--headless", "--path", $script:Root, "--", "--server", "--port", $Port) `
                    -RedirectStandardOutput $serverOut `
                    -RedirectStandardError (Join-Path $script:LogDir "stall-$label-server.err.log") `
                    -PassThru -NoNewWindow
                $null = $server.Handle
                $procs += $server
                if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
                    Write-Fail "the server never reported listening within 30s; see $serverOut"
                }
                Write-Host "        server up (pid $($server.Id))"

                $csv = Join-Path $OutDir "stall-$label.csv"
                # A WINDOWED client -- no --headless: a headless run cannot see a render stall.
                # SICK-1's patrol lane (x = +2.5, z = -3.0 .. +4.2), unchanged, so this table's
                # rows and that handoff's rows measure the same walk.
                $client = Start-Process -FilePath $script:GodotExe `
                    -ArgumentList ($engineExtra + @("--path", $script:Root, "--",
                        "--bot", "--address", "127.0.0.1:$Port", "--name", "Stall",
                        "--windowed", "--first-person-cam",
                        "--goto-patrol", "2.5,-3.0,2.5,4.2",
                        "--pacing-log", $csv,
                        "--pacing-sec", $DurationSec,
                        "--pacing-turn", $TurnDegPerSec,
                        "--pacing-label", $label,
                        "--duration", ($DurationSec + 8)) + $gameExtra) `
                    -RedirectStandardOutput $clientOut `
                    -RedirectStandardError (Join-Path $script:LogDir "stall-$label-client.err.log") `
                    -PassThru
                $null = $client.Handle
                $procs += $client
                Write-Host "        client up (pid $($client.Id))"

                if ($Foreground) {
                    # The window does not exist the instant the process does; wait for one to
                    # appear anywhere in the launched tree, then raise it. SW_RESTORE first,
                    # because a window that opened minimised cannot be brought forward.
                    $hwnd = [IntPtr]::Zero
                    $treePids = @()
                    $deadline = (Get-Date).AddSeconds(30)
                    while ((Get-Date) -lt $deadline) {
                        $treePids = Get-ProcessTreePids $client.Id
                        $hwnd = [StallWin]::FindWindowForPids([int[]]$treePids)
                        if ($hwnd -ne [IntPtr]::Zero) { break }
                        Start-Sleep -Milliseconds 300
                    }
                    if ($hwnd -ne [IntPtr]::Zero) {
                        [void][StallWin]::ShowWindow($hwnd, 9)
                        [void][StallWin]::SetForegroundWindow($hwnd)
                        Start-Sleep -Milliseconds 700
                        $fg = Get-ForegroundPid
                        $ok = $treePids -contains $fg
                        # THE READ-BACK, NOT THE INTENT (Boot.cs's own rule for window mode).
                        # "We called SetForegroundWindow" and "the window has focus" are
                        # different claims and only the second one is evidence: Windows refuses
                        # the call outright when the caller does not own the foreground.
                        Write-Host ("        foreground pid now {0}, launched tree {1} -- {2}" -f `
                            $fg, ($treePids -join ","), $(if ($ok) { "FOCUSED" } else { "NOT FOCUSED" })) `
                            -ForegroundColor $(if ($ok) { "Green" } else { "Red" })
                    } else {
                        Write-Host "        no game window in the launched tree within 30s -- row is UNFOCUSED" -ForegroundColor Red
                    }
                }

                if (-not (Wait-ForExit $client ([int]($DurationSec + 120)))) {
                    Stop-Proc $client
                    Write-Fail "the stall client did not exit within its duration + 120s; see $clientOut"
                }
            } finally {
                Stop-Procs $procs
            }

            $summary = Select-String -Path $clientOut -Pattern "^\[pacing\] SUMMARY " | Select-Object -Last 1
            if (-not $summary) {
                Write-Fail ("'$label' never printed its [pacing] SUMMARY line. A missing display " +
                            "or GPU is the first suspect -- this run is not headless. See $clientOut")
            }
            $stall = Select-String -Path $clientOut -Pattern "^\[stall\] " | Select-Object -Last 1
            foreach ($pat in @("^\[adapter\] ", "^\[pacing\] recording ")) {
                foreach ($line in (Select-String -Path $clientOut -Pattern $pat)) {
                    Write-Host "        $($line.Line.Trim())" -ForegroundColor DarkGray
                }
            }
            Write-Host "        $($summary.Line.Trim())" -ForegroundColor DarkGray
            if ($stall) { Write-Host "        $($stall.Line.Trim())" -ForegroundColor White }
            $rows += [pscustomobject]@{
                Label   = $label
                Game    = $gameTail
                Engine  = $engineTail
                Summary = $summary.Line.Trim()
                Stall   = $(if ($stall) { $stall.Line.Trim() } else { "(no [stall] line)" })
            }
        }
    }
} finally {
    Exit-SuiteMutex $mutex
}

Write-Host ""
Write-Host "=== rows ===" -ForegroundColor White
foreach ($r in $rows) {
    Write-Host ("{0,-22} {1}" -f $r.Label, $r.Summary)
    Write-Host ("{0,-22} {1}" -f "", $r.Stall) -ForegroundColor DarkGray
}
Write-Host ""
Write-Host "csv: $OutDir" -ForegroundColor DarkGray
exit 0
