<#
.SYNOPSIS
    STOCK-1: the baked shop floor, and whether the object the round is about can be hidden in it.

.DESCRIPTION
    PHASE 1 -- the offline compliance test (--stock-selftest, headless, no server, no bots).
    Reads the bays and SHELF-1's 120 carryable facings out of the LIVE TREE, re-derives the whole
    bulk layout from ShelfStock's arithmetic, checks the loaded MultiMesh instance counts and the
    collision-run count against it, then poses a real near-miss prefab in EVERY authored hole and
    runs the shipped PlacementIntegrity.Check on each. Every hole, not a sample: the failure this
    guards against is the one that only shows up in the hole nobody sampled.

    PHASE 2 -- the live three-layer verdict. A prop is seeded AT one of the holes phase 1 printed
    (never at a coordinate typed into this file -- the holes are seeded from the bay names, so a
    literal here would be right until the first re-seed and wrong silently afterwards), the round
    is driven to a Confirm on it, and the server's own layer-2 and layer-3 lines are read. The
    packet's assertion is that InsideStatic never fires for a prop in a hole; this is where that
    is proved against a real PropManager rather than against a physics query.

    PHASE 3 -- the ids did not move. SHELF-1's block is 1000..1129 and STOCK-1 adds no
    NetworkedProp, so the adoption line must be byte-identical. Run-PlaceTest.ps1 names four of
    those numbers in its own source.

    THE SUITE GATES ON THE LINES, NOT THE EXIT CODE, for the reason Run-SupermarketWorldTest.ps1
    does: a process that dies before its own summary exits non-zero for reasons that have nothing
    to do with the subject.

    PROVED ABLE TO FAIL. See docs/agents/handoffs/2026-09-20-STOCK-1.md.

    Exit 0 = PASS. Headless throughout. One Godot at a time: this suite takes the machine-wide
    suite mutex like every other.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param(
    # 7911. HANDED OUT by the orchestrator rather than computed from a snapshot of tests/ --
    # INT-0 measured three lanes off one base all picking 7896, and the rule that came out of it
    # is that a port is not claimed until it is on origin. The one ladder table in
    # .claude/rules/test-suite.md said 7910 was next; the REVIEW-1 fix lane took it in the same
    # wave, so this is 7911 and the table carries the row.
    [int]$Port = 7911,
    [int]$MutexTimeoutMinutes = 120,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

Write-Host "=== STOCK-1: the baked shop floor, and the holes in it ===" -ForegroundColor White

$RoundScript = "start@6,confirm@14"
$BotDurationSec = 26
$SeededPropId = 1          # PropRegistry.Register starts at 1; authored props are 1000+
$AuthoredCount = 130       # SHELF-1: 4 crates + 6 bins + 120 products
$AuthoredFirst = 1000
$AuthoredLast = 1129

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
$script:Failures = @()
function Add-Failure([string]$m) { $script:Failures += $m }

# NB: GameArgs, not Args. $Args is a PowerShell AUTOMATIC variable and a function parameter of
# that name is silently not what the caller passed -- REACH-1 launched three servers with no game
# flags at all and they sat there doing nothing, which reads exactly like a failed bind.
function Start-StockServer([string]$Tag, [string[]]$GameArgs) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList (
        @("--headless", "--path", $script:Root, "--") + $GameArgs) `
        -RedirectStandardOutput $out `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

function Start-StockBot([string]$Tag, [string]$Name, [int]$DurationSec) {
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
        "--log", (Join-Path $script:LogDir "$Tag.jsonl"), "--duration", $DurationSec,
        "--world", "supermarket") `
        -RedirectStandardOutput (Join-Path $script:LogDir "$Tag.out.log") `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

$script:PlacePose = $null
$script:PlaceWhere = ""

try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    # ---- PHASE 1: every hole, offline ---------------------------------------------------------
    Write-Host "[1/3] the baked stock and every authored hole (offline)..." -ForegroundColor Cyan
    $selfOut = Join-Path $script:LogDir "stock-selftest.out.log"
    $selftest = Start-StockServer "stock-selftest" @("--stock-selftest")
    $procs += $selftest
    if (-not (Wait-ForExit $selftest 180)) {
        Stop-Proc $selftest
        Add-Failure "the stock self-test did not finish within 180 s; see stock-selftest.out.log"
    }
    Start-Sleep -Milliseconds 300

    $lines = @()
    if (Test-Path $selfOut) { $lines = @(Get-Content $selfOut) }
    foreach ($l in @($lines | Where-Object { $_ -match '^\[stock-selftest\]   ' })) {
        Write-Host "        $l" -ForegroundColor DarkGray
    }
    $places = @($lines | Where-Object { $_ -match '^\[stock-selftest\] PLACE ' })
    foreach ($l in $places) { Write-Host "        $l" -ForegroundColor DarkGray }

    $summary = @($lines | Where-Object { $_ -match '^\[stock-selftest\] SUMMARY ' })
    if ($summary.Count -eq 0) {
        Add-Failure "the stock self-test printed no SUMMARY line at all -- it died before it finished; see stock-selftest.out.log"
    } else {
        Write-Host "        $($summary[-1])" -ForegroundColor DarkGray
        if ($summary[-1] -notmatch 'result=PASS') {
            Add-Failure "stock self-test: $($summary[-1])"
        }
        # A run that judged nothing is not a pass. The room authors well over a hundred holes;
        # anything in single figures means the layout collapsed rather than that it is tidy.
        if ($summary[-1] -match 'holes=(\d+)') {
            $holes = [int]$matches[1]
            if ($holes -lt 40) {
                Add-Failure "the self-test judged only $holes hole(s); a sixteen-bay room authors well over a hundred. The fill collapsed, or the bays were not found."
            }
        }
        if ($summary[-1] -match 'poses=(\d+)') {
            if ([int]$matches[1] -lt 40) {
                Add-Failure "the self-test posed a prop in fewer than 40 hole/prefab combinations; it was not actually checking placements."
            }
        }
    }

    # The phase-2 seed comes from phase 1's own output. Prefer a CAN hole: cans are the tightest
    # of the three grids (0.09 m pitch), so it is the hole with the least room in it.
    #
    # THE KIND IS PASSED EXPLICITLY AND THAT IS NOT OPTIONAL. --seed-test-props defaults to a
    # CRATE -- 0.44 m on every axis -- and no hole on any shelf in this room is a third of that.
    # A run that forgot the kind would seed a crate half inside a bay, report StaticOverlap, and
    # look exactly like the defect this suite exists to catch.
    foreach ($m in @("Can", "Box", "Produce")) {
        $hit = @($places | Where-Object { $_ -match "material=$m\b" }) | Select-Object -First 1
        if ($hit -and $hit -match 'at=([-\d.]+),([-\d.]+),([-\d.]+)') {
            $script:PlacePose = "$($matches[1]),$($matches[2]),$($matches[3]),$($m.ToLower())"
            $script:PlaceWhere = $hit
            break
        }
    }
    if (-not $script:PlacePose) {
        Add-Failure "phase 1 printed no PLACE candidate, so phase 2 has no hole to seed into"
    }

    # ---- PHASE 2: the live three-layer verdict on a prop sitting in a hole ---------------------
    if ($script:PlacePose) {
        Write-Host "[2/3] a prop seeded in an authored hole -> Confirm accepted..." -ForegroundColor Cyan
        Write-Host "        seeding at $($script:PlacePose)" -ForegroundColor DarkGray
        $serverOut = Join-Path $script:LogDir "stock.server.out.log"
        $server = Start-StockServer "stock.server" @(
            "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
            "--seed-test-props", $script:PlacePose,
            "--reach-target", $SeededPropId,
            "--round-script", $RoundScript)
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 90)) {
            Stop-Proc $server
            Write-Fail "the server never reported listening on udp/$Port; see stock.server.out.log"
        }

        $bots = @()
        foreach ($name in @("StockA", "StockB")) {
            $b = Start-StockBot "stock-$name" $name $BotDurationSec
            $procs += $b
            $bots += $b
            # A short stagger so join ORDER is unambiguous -- the first to arrive is the hider.
            Start-Sleep -Milliseconds 1200
        }
        foreach ($b in $bots) {
            if (-not (Wait-ForExit $b ($BotDurationSec + 60))) {
                Stop-Proc $b
                Add-Failure "a stock bot did not exit within its own duration + 60 s"
            } elseif ($b.ExitCode -ne 0) {
                Add-Failure "a stock bot exited with code $($b.ExitCode); see tests/logs/ (read its .out.log for a completion line and its .err.log for a Finalize frame before calling this a crash)"
            }
        }
        Stop-Proc $server
        Start-Sleep -Milliseconds 500

        $slog = @()
        if (Test-Path $serverOut) { $slog = @(Get-Content $serverOut) }

        # LAYER 2. Every audit prints one line. A prop standing in a hole must come out
        # None/Good: StaticOverlap means bulk collision is IN the hole, and Stuck is the outcome
        # REACH-1 turns into InsideStatic.
        $layer2 = @($slog | Where-Object { $_ -match "^\[reach\] layer2 " -and $_ -match "prop=$SeededPropId\b" })
        foreach ($l in $layer2 | Select-Object -First 3) { Write-Host "        $l" -ForegroundColor DarkGray }
        if ($layer2.Count -eq 0) {
            Add-Failure "the seeded prop never latched Resting at all, so layer 2 never audited it -- the run did not stage. Read stock.server.out.log."
        }
        $bad = @($layer2 | Where-Object { $_ -match 'StaticOverlap' })
        if ($bad.Count -gt 0) {
            Add-Failure "layer 2 reported StaticOverlap on a prop standing in an AUTHORED HOLE ($($bad.Count) of $($layer2.Count) audit(s)): $($bad[0]). Bulk collision is inside the hole; a hider who used it would be refused the Confirm."
        }
        $stuck = @($slog | Where-Object { $_ -match '^\[reach\] STUCK ' })
        if ($stuck.Count -gt 0) {
            Add-Failure "the rest audit latched STUCK $($stuck.Count) time(s): $($stuck[0])"
        }

        # LAYER 3. The packet's own sentence: InsideStatic must never fire for a prop in a hole.
        $layer3 = @($slog | Where-Object { $_ -match '^\[reach\] layer3 ' })
        foreach ($l in $layer3 | Select-Object -First 3) { Write-Host "        $l" -ForegroundColor DarkGray }
        if ($layer3.Count -eq 0) {
            Add-Failure "layer 3 never evaluated the target -- no [reach] layer3 line in the server log. The Confirm was never reached."
        }
        $inside = @($layer3 | Where-Object { $_ -match 'InsideStatic' })
        if ($inside.Count -gt 0) {
            Add-Failure "layer 3 answered InsideStatic for a prop in an authored hole: $($inside[0]). That is the exact refusal STOCK-1 exists to make impossible."
        }
        $occluded = @($layer3 | Where-Object { $_ -match 'OccludedByStatic' })
        if ($occluded.Count -gt 0) {
            Add-Failure "layer 3 answered OccludedByStatic for a prop in a FULL-DEPTH hole: $($occluded[0]). The hole does not reach a face, so bulk is standing in front of the object."
        }

        # THE CONFIRM-TIME AUDIT SPECIFICALLY. REACH-1's IConfirmTimeAudit re-measures on the
        # press, and that is the verdict the round actually refuses on -- a layer3 line from an
        # earlier settle proves nothing about the instant the hider committed.
        $confirmAudit = @($slog | Where-Object { $_ -match '^\[reach\] layer3 \[confirm\]' })
        if ($confirmAudit.Count -eq 0) {
            Add-Failure "no '[reach] layer3 [confirm]' line -- the Confirm-time audit never ran, so an acceptance here would prove nothing about layer 3"
        } else {
            Write-Host "        $($confirmAudit[-1])" -ForegroundColor DarkGray
            if ($confirmAudit[-1] -notmatch 'reachable \(Reachable\)') {
                Add-Failure "the Confirm-time audit on a prop in an authored hole said '$($confirmAudit[-1])'; expected 'reachable (Reachable)'"
            }
        }

        # And the round accepted it, which is the whole point of the three layers agreeing.
        $refusals = @($slog | Where-Object { $_ -match '\[round\] refused: (\w+)' } |
            ForEach-Object { if ($_ -match '\[round\] refused: (\w+)') { $matches[1] } })
        if ($refusals -contains "NobodyCouldReachThat") {
            Add-Failure "the server refused the Confirm with NobodyCouldReachThat on a prop standing in an AUTHORED HOLE. That is the exact refusal STOCK-1 exists to make impossible."
        }
        $seeking = @($slog | Where-Object { $_ -match '\[round\] phase Hiding -> Seeking' })
        if ($seeking.Count -eq 0) {
            Add-Failure "the round never entered Seeking (refusals logged: $($refusals -join ', ')). Read stock.server.out.log before treating this as a STOCK-1 defect -- a round script that did not run is a staging failure."
        } else {
            Write-Host "        round entered Seeking on the Confirm press" -ForegroundColor DarkGray
        }

        # ---- PHASE 3: the ids did not move ----------------------------------------------------
        Write-Host "[3/3] SHELF-1's id block, unmoved..." -ForegroundColor Cyan
        $adopt = @($slog | Where-Object { $_ -match '^\[props\] adopted ' }) | Select-Object -First 1
        if (-not $adopt) {
            Add-Failure "the server printed no [props] adopted line; adoption did not run"
        } else {
            Write-Host "        $adopt" -ForegroundColor DarkGray
            $want = "adopted $AuthoredCount authored prop"
            if ($adopt -notmatch [regex]::Escape($want)) {
                Add-Failure "the adoption line reads '$adopt'; STOCK-1 adds no NetworkedProp, so it must still say '$want'. Something under the search room was added, removed or renamed, and every id above it has moved -- Run-PlaceTest.ps1 names four of them in its own source."
            }
            if ($adopt -notmatch "ids $AuthoredFirst\.\.$AuthoredLast") {
                Add-Failure "the adoption line does not report ids $AuthoredFirst..$AuthoredLast: '$adopt'"
            }
        }
    }
} finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host ""
if ($script:Failures.Count -gt 0) {
    Write-Host "STOCK-TEST FAILED ($($script:Failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "STOCK-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

$passLine = "PASS: the baked stock matches the arithmetic that baked it; every authored hole " +
    "accepts a near-miss prop at the pose a hider leaves it; a prop seeded in a hole audits " +
    "None/Good and reads reachable (never InsideStatic, never OccludedByStatic); the Confirm " +
    "was accepted; and the id block is still $AuthoredFirst..$AuthoredLast."
Write-Host $passLine -ForegroundColor Green
Write-Host ""
Write-Host "STOCK-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
