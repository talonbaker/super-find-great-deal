<#
.SYNOPSIS
    STALL-1: the per-frame dt histogram, and what each stall frame WAS.

.DESCRIPTION
    Reads one or more pacing CSVs (the ones CameraPacingProbe writes) and prints, per file, a
    log-spaced frame-time histogram plus the stall rows in full. It asserts nothing.

    THE HISTOGRAM IS THE CAPTURE. The packet asks for "the per-frame dt histogram before/after",
    and a histogram is the right picture for this question rather than a screenshot: the defect
    is not something you can see in one frame, it is a second mode in a distribution. A run with
    no stall is unimodal at the refresh interval; a run with one has a second clump three orders
    of magnitude to its right and nothing in between, and that gap is what says "a discrete event
    with a mechanism" rather than "the machine is slow".

    The four discriminating columns (gc0/gc1/gc2, phys_ticks, proc_ms, phys_proc_ms) are printed
    for every stall frame, because a stall that stepped 8 physics ticks with no GC and 2 ms of
    proc time is a PRESENT stall, and one that stepped 1 tick with a gen2 collection on it is a
    managed pause. Those are different packets.

.PARAMETER Csv
    One or more CSV paths (wildcards allowed).

.PARAMETER OutFile
    Optional. Also write the report to this file, in UTF-8 without a BOM.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string[]]$Csv,
    [string]$OutFile = "",
    [double]$StallMs = 100
)

$ErrorActionPreference = "Stop"
$lines = New-Object System.Collections.Generic.List[string]
function Emit([string]$s) { Write-Host $s; $lines.Add($s) }

# Log-spaced edges: the interesting structure is at 16 ms and at 500 ms, and a linear histogram
# with a bin wide enough to hold the second puts the entire first mode in one bar.
$edges = @(0, 1, 2, 4, 8, 12, 14, 16, 18, 20, 25, 33, 50, 100, 200, 400, 500, 600, 800, 1000, [double]::PositiveInfinity)

$files = @()
foreach ($c in $Csv) { $files += (Get-ChildItem -Path $c -File | ForEach-Object { $_.FullName }) }
if ($files.Count -eq 0) { throw "no CSV matched: $($Csv -join ', ')" }

foreach ($f in $files) {
    $rows = Import-Csv -Path $f
    if ($rows.Count -eq 0) { Emit "$([IO.Path]::GetFileName($f)): EMPTY"; continue }
    $dt = @($rows | ForEach-Object { [double]$_.frame_ms })
    $sorted = @($dt | Sort-Object)
    function Pct([double]$p) { return $sorted[[Math]::Min($sorted.Count - 1, [Math]::Max(0, [int][Math]::Ceiling($p / 100.0 * $sorted.Count) - 1))] }
    $wall = ($dt | Measure-Object -Sum).Sum / 1000.0
    $stalls = @($rows | Where-Object { [double]$_.frame_ms -ge $StallMs })

    Emit ""
    Emit ("=" * 78)
    Emit ("FILE     {0}" -f [IO.Path]::GetFileName($f))
    Emit ("frames   {0} over {1:F1} s     p50 {2:F3} ms   p95 {3:F3} ms   p99 {4:F3} ms   max {5:F3} ms" -f `
        $dt.Count, $wall, (Pct 50), (Pct 95), (Pct 99), ($sorted[-1]))
    $lostMs = 0.0
    foreach ($s in $stalls) { $lostMs += [double]$s.frame_ms }
    Emit ("stalls   {0} frames over {1:F0} ms; {2:F2} s of the {3:F1} s window ({4:F1} %) is inside them" -f `
        $stalls.Count, $StallMs, ($lostMs / 1000.0), $wall, ($lostMs / 10.0 / $wall))
    Emit ""
    Emit "  frame-time histogram (log-spaced bins)"
    for ($i = 0; $i -lt $edges.Count - 1; $i++) {
        $lo = $edges[$i]; $hi = $edges[$i + 1]
        $n = @($dt | Where-Object { $_ -ge $lo -and $_ -lt $hi }).Count
        if ($n -eq 0) { continue }
        $bar = "#" * [Math]::Max(1, [int][Math]::Round(60.0 * $n / $dt.Count))
        $hiTxt = if ([double]::IsInfinity($hi)) { "inf" } else { ("{0:F0}" -f $hi) }
        Emit ("  {0,6:F0} - {1,-6} ms | {2,6} {3,5:F2}%  {4}" -f $lo, $hiTxt, $n, (100.0 * $n / $dt.Count), $bar)
    }

    if ($stalls.Count -gt 0 -and ($rows[0].PSObject.Properties.Name -contains "gc0")) {
        Emit ""
        Emit "  every stall frame, and what it was"
        Emit ("  {0,7} {1,10} {2,9} {3,7} {4,10} {5,10} {6,6}" -f "frame", "frame_ms", "step_m", "ticks", "proc_ms", "phys_ms", "gc")
        $prevFrame = $null
        $periods = @()
        foreach ($s in $stalls) {
            $idx = [int]$s.frame
            $gcNote = "-"
            if ($null -ne $prevFrame) {
                $before = $rows[$idx - 1]
                if ([int]$s.gc0 -gt [int]$before.gc0 -or [int]$s.gc1 -gt [int]$before.gc1 -or [int]$s.gc2 -gt [int]$before.gc2) {
                    $gcNote = "{0}/{1}/{2}" -f ([int]$s.gc0 - [int]$before.gc0), ([int]$s.gc1 - [int]$before.gc1), ([int]$s.gc2 - [int]$before.gc2)
                }
                $span = 0.0
                for ($k = $prevFrame + 1; $k -le $idx; $k++) { $span += [double]$rows[$k].frame_ms }
                $periods += ($span / 1000.0)
            } elseif ($idx -gt 0) {
                $before = $rows[$idx - 1]
                if ([int]$s.gc0 -gt [int]$before.gc0 -or [int]$s.gc1 -gt [int]$before.gc1 -or [int]$s.gc2 -gt [int]$before.gc2) {
                    $gcNote = "{0}/{1}/{2}" -f ([int]$s.gc0 - [int]$before.gc0), ([int]$s.gc1 - [int]$before.gc1), ([int]$s.gc2 - [int]$before.gc2)
                }
            }
            Emit ("  {0,7} {1,10:F1} {2,9:F4} {3,7} {4,10:F3} {5,10:F3} {6,6}" -f `
                $idx, [double]$s.frame_ms, [double]$s.step_m, [int]$s.phys_ticks, [double]$s.proc_ms, [double]$s.phys_proc_ms, $gcNote)
            $prevFrame = $idx
        }
        if ($periods.Count -gt 0) {
            $pm = ($periods | Measure-Object -Average).Average
            Emit ("  period between stalls: mean {0:F2} s over {1} gaps (min {2:F2}, max {3:F2})" -f `
                $pm, $periods.Count, ($periods | Measure-Object -Minimum).Minimum, ($periods | Measure-Object -Maximum).Maximum)
        }
        $gcTotal = "{0}/{1}/{2}" -f ([int]$rows[-1].gc0 - [int]$rows[0].gc0), ([int]$rows[-1].gc1 - [int]$rows[0].gc1), ([int]$rows[-1].gc2 - [int]$rows[0].gc2)
        Emit ("  GC collections in the whole window (gen0/1/2): {0}" -f $gcTotal)
    }
}

if ($OutFile) {
    $dir = Split-Path -Parent $OutFile
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    # UTF-8 without a BOM, written through .NET rather than Set-Content: Windows PowerShell 5.1
    # mangles UTF-8 on a Get-Content/Set-Content round trip (.claude/rules/imports-and-encoding.md).
    [IO.File]::WriteAllLines($OutFile, $lines, (New-Object Text.UTF8Encoding($false)))
    Write-Host ""
    Write-Host "wrote $OutFile" -ForegroundColor DarkGray
}
