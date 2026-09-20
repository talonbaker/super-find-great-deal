<#
.SYNOPSIS
    Compares the RAW MEASURED QUANTITIES two runs of the same suite printed, and says whether they
    moved. An absence check's instrument.

.DESCRIPTION
    `.claude/rules/test-suite.md`: "compare the RAW measured quantity across runs" -- the verdict
    is not the signal. And `~/.claude/CLAUDE.md`'s standing rule for any claim that something did
    NOT change: an absence is worthless until the instrument has been shown to detect a presence.
    That is what this script is for, and it is why it has a -PositiveControl switch built in
    rather than left to whoever is holding it.

    It takes two captured stdout files from the SAME suite, pulls every indented raw-quantity line
    out of each (the DarkGray/Yellow measurement lines every suite in tests/ prints with an
    eight-space indent), pairs them by their non-numeric skeleton, and reports the delta of every
    number. Lines whose text differs entirely are reported as unpaired, which is itself a change.

    Volatile-by-construction fields are dropped, not compared: process ids ("pid NNNNN") change
    on every launch by definition and comparing them would make every run differ.

    EXIT CODES: 0 = every paired quantity within tolerance and nothing unpaired. 1 = something
    moved (or is missing), which is a FINDING to be read, not automatically a regression.

.PARAMETER PositiveControl
    Runs the comparison a second time against a copy of -After with ONE number nudged by
    -ControlNudge, and fails if that copy does NOT come back as different. This is the proof that
    a "nothing moved" result from this script means anything at all.

.EXAMPLE
    powershell -File tests/Compare-SuiteQuantities.ps1 -Before base.txt -After branch.txt `
        -Tolerances "m off the marker=0.05;Evidence_e{N} in=1.5" -PositiveControl
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Before,
    [Parameter(Mandatory = $true)][string]$After,
    # Default tolerance for any quantity not named in -Tolerances.
    [double]$Tolerance = 0.05,
    # Per-quantity tolerances as "substring=tolerance;substring=tolerance". THE SUBSTRING IS
    # MATCHED AGAINST THE SKELETON, i.e. against the line with every number already replaced by
    # {N} -- so key on "Evidence_e{N} in", never on "Evidence_e11 in", or the entry silently never
    # matches and the default tolerance is used instead. A wall-clock duration
    # needs a looser one than a settled position, and saying which is which BY NAME is more honest
    # than one number that has to cover both. A string rather than a hashtable on purpose:
    # `powershell -File` (this repo's only sanctioned invocation - see .claude/rules/test-suite.md)
    # binds every argument as a string, so a hashtable literal never survives the call.
    [string]$Tolerances = "",
    [switch]$PositiveControl,
    [double]$ControlNudge = 0.5
)

$ErrorActionPreference = "Stop"

function Get-QuantityLines([string]$Path) {
    if (-not (Test-Path $Path)) { throw "capture not found: $Path" }
    return @(Get-Content $Path |
        Where-Object { $_ -match '^\s{8}\S' } |
        Where-Object { $_ -notmatch 'pid \d+' } |
        ForEach-Object { $_.TrimEnd() })
}

# The line with every number replaced by a placeholder: two runs of the same suite print the same
# skeletons and different numbers, so this is what pairs them.
function Get-Skeleton([string]$Line) {
    return ($Line -replace '-?\d+(\.\d+)?', '{N}')
}

function Get-Numbers([string]$Line) {
    return @([regex]::Matches($Line, '-?\d+(\.\d+)?') | ForEach-Object { [double]$_.Value })
}

$script:ToleranceMap = @{}
foreach ($pair in ($Tolerances -split ';')) {
    if ($pair.Trim().Length -eq 0) { continue }
    $kv = $pair -split '=', 2
    if ($kv.Count -ne 2) { throw "malformed -Tolerances entry '$pair' (want substring=number)" }
    $script:ToleranceMap[$kv[0].Trim()] = [double]$kv[1].Trim()
}

function Resolve-Tolerance([string]$Skeleton) {
    foreach ($key in $script:ToleranceMap.Keys) {
        if ($Skeleton -like "*$key*") { return [double]$script:ToleranceMap[$key] }
    }
    return $Tolerance
}

function Compare-Captures([string[]]$BeforeLines, [string[]]$AfterLines, [bool]$Quiet) {
    $diffs = New-Object System.Collections.Generic.List[string]
    $beforeBySkeleton = @{}
    foreach ($l in $BeforeLines) {
        $k = Get-Skeleton $l
        if (-not $beforeBySkeleton.ContainsKey($k)) { $beforeBySkeleton[$k] = New-Object System.Collections.Generic.List[string] }
        $beforeBySkeleton[$k].Add($l)
    }

    foreach ($l in $AfterLines) {
        $k = Get-Skeleton $l
        if (-not $beforeBySkeleton.ContainsKey($k) -or $beforeBySkeleton[$k].Count -eq 0) {
            $diffs.Add("UNPAIRED (only in After): $l")
            continue
        }
        $match = $beforeBySkeleton[$k][0]
        $beforeBySkeleton[$k].RemoveAt(0)
        $tol = Resolve-Tolerance $k
        $bn = Get-Numbers $match
        $an = Get-Numbers $l
        for ($i = 0; $i -lt $an.Count; $i++) {
            $d = $an[$i] - $bn[$i]
            $flag = if ([math]::Abs($d) -gt $tol) { "MOVED" } else { "same " }
            if (-not $Quiet) {
                Write-Host ("  {0}  {1,10:F3} -> {2,10:F3}  delta {3,8:F3}  (tol {4:F2})  {5}" -f `
                    $flag, $bn[$i], $an[$i], $d, $tol, $k) -ForegroundColor $(if ($flag -eq "MOVED") { "Yellow" } else { "DarkGray" })
            }
            if ([math]::Abs($d) -gt $tol) {
                $diffs.Add(("MOVED by {0:F3} (tol {1:F2}) in: {2}" -f $d, $tol, $k))
            }
        }
    }
    foreach ($k in $beforeBySkeleton.Keys) {
        foreach ($leftover in $beforeBySkeleton[$k]) { $diffs.Add("UNPAIRED (only in Before): $leftover") }
    }
    return $diffs
}

$beforeLines = Get-QuantityLines $Before
$afterLines = Get-QuantityLines $After
if ($beforeLines.Count -eq 0 -or $afterLines.Count -eq 0) {
    Write-Host "FAIL: one of the captures has no raw-quantity lines at all - this comparison would be vacuous." -ForegroundColor Red
    Write-Host ("  before: {0} line(s), after: {1} line(s)" -f $beforeLines.Count, $afterLines.Count)
    exit 1
}

Write-Host ("=== raw-quantity comparison: {0} vs {1} ===" -f (Split-Path $Before -Leaf), (Split-Path $After -Leaf)) -ForegroundColor White
$diffs = Compare-Captures $beforeLines $afterLines $false

if ($PositiveControl) {
    # Nudge exactly one number in the After capture and require the comparison to notice. If it
    # does not, the "nothing moved" result above is an instrument reading zero because it is
    # broken, and it must not be reported as evidence.
    $mutated = @($afterLines)
    $idx = -1
    for ($i = 0; $i -lt $mutated.Count; $i++) {
        if ((Get-Numbers $mutated[$i]).Count -gt 0) { $idx = $i; break }
    }
    if ($idx -lt 0) {
        Write-Host "FAIL: positive control impossible - no captured line carries a number." -ForegroundColor Red
        exit 1
    }
    $one = [regex]::Match($mutated[$idx], '-?\d+(\.\d+)?')
    $nudged = [double]$one.Value + $ControlNudge
    $mutated[$idx] = $mutated[$idx].Remove($one.Index, $one.Length).Insert($one.Index, ("{0:F3}" -f $nudged))
    Write-Host ""
    Write-Host "  [positive control] one number nudged by $ControlNudge in the After capture:" -ForegroundColor Cyan
    Write-Host ("    {0}" -f $mutated[$idx]) -ForegroundColor Cyan
    $controlDiffs = Compare-Captures $afterLines $mutated $true
    if ($controlDiffs.Count -eq 0) {
        Write-Host "  [positive control] FAILED - the comparison did not see a difference it was GIVEN one to find." -ForegroundColor Red
        Write-Host "  Every 'nothing moved' result from this script is therefore worthless. Fix the comparison before reporting anything." -ForegroundColor Red
        exit 1
    }
    Write-Host ("  [positive control] passed - the comparison reported {0} difference(s) on the nudged copy." -f $controlDiffs.Count) -ForegroundColor Green
}

Write-Host ""
if ($diffs.Count -eq 0) {
    Write-Host "NOTHING MOVED: every paired raw quantity is within tolerance, and nothing is unpaired." -ForegroundColor Green
    exit 0
}
foreach ($d in $diffs) { Write-Host "  - $d" -ForegroundColor Yellow }
Write-Host ""
Write-Host "$($diffs.Count) quantity/quantities moved. That is a FINDING to read, not automatically a regression." -ForegroundColor Yellow
exit 1
