<#
.SYNOPSIS
    PROBE-1: how many real items a room can hold, on the server and on the client. Measures and
    photographs; asserts nothing and can never go red. NOT a registered suite.

.DESCRIPTION
    Talon, 2026-09-20: "How many items in total for one player? Could we stock hundreds and
    hundreds?" Under the SYNC model the search room is simulated by the hider's client during
    Hiding and by the server during Seeking (program S1), so the answer has two halves and both
    are MEASURED. The bars are the ones the program already carries: server p95 under 8 ms at
    rest (SHELF-1's), client physics p50/p95 under 5 ms, and a whole frame under 16.6 ms.

    THIS SCRIPT ASSERTS NOTHING, for REACH-1's and SHELF-1's reason: a suite that went red on a
    frame time would be gating on a budget nobody has agreed to. It prints a table; the
    orchestrator decides from it.

    PASSES

      server  -- for each N: a headless dedicated server with --probe-props N, two headless bots
                 walking, ReachCostProbe at rest (--cost-shove-every 0) and then under a FIXED
                 100-prop wave (--cost-shove-every 3 --cost-shove-count 100). Two runs of each,
                 because SHELF-1's rule is that one p95 is not a measurement. Collects server
                 p50/p95/peak, the physics server's active-body/collision-pair/island counts, GC
                 over the window, rest audits and corrections, and bandwidth from ENet's own
                 counters.

      client  -- the same populations with a WINDOWED first-person bot standing in the central
                 walkway looking down an aisle (STOCK-1's capture pose), which is the only peer
                 that renders anything. Collects the physics reading SHELF-1 added (`pms`) AND
                 the render reading this packet added (`prs`, `fms`, `draws`, `objs`), plus one
                 frame per population.

      ablate  -- THE ANOMALY. The same at-rest staging at one N, run twice on ONE build with
                 --probe-ablate flipped, so what the at-rest fixes saved is a difference rather
                 than an argument. See scripts/game/props/PropCostSwitches.cs.

    ENGINE FLAGS BEFORE THE BARE --, GAME FLAGS AFTER. Wrong side is swallowed and looks exactly
    like a hang (CLAUDE.md).

    ONE GODOT AT A TIME. This takes the machine-wide suite mutex like every registered suite and
    stops every process by the pid it started it with -- never by image name, which has already
    killed another lane's marathon on this machine (2026-09-19).

.PARAMETER Pass
    server | client | ablate | all
.PARAMETER Populations
    The N values to measure. Default 130, 500, 1000, 2000 (the packet's four).
#>
[CmdletBinding()]
param(
    [ValidateSet("server", "client", "ablate", "all")][string]$Pass = "all",
    [int[]]$Populations = @(130, 500, 1000, 2000),
    # 7908, the port Measure-ShopFloor and Run-AuthoredPropTest share. Safe for their reason:
    # everything here takes the machine-wide mutex, so two can never be alive at one moment.
    [int]$Port = 7908,
    [int]$MutexTimeoutMinutes = 180,
    [double]$WindowSec = 20,
    [int]$Runs = 2,
    [int]$AblateAt = 2000,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

$CaptureDir = Join-Path $script:Root "docs/qa/2026-09-20-probe-1"
New-Item -ItemType Directory -Path $CaptureDir -Force | Out-Null

# The search room is instanced at x = +40 and its interior is x in [33,47], z in [-5,5]. The
# CENTRAL walkway (z = 0) is the one ProbeSeedLayout deliberately leaves empty, so it is where a
# bot can stand and walk. 41.5 is mid-room along it.
$LaneX = 41.5
$LaneZ = 0

$script:Rows = @()

function Start-ProbeServer([string]$Tag, [int]$N, [double]$ShoveEvery, [int]$ShoveCount, [string]$Ablate) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $game = @("--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
              "--probe-props", $N,
              "--reach-cost", $WindowSec, "--cost-shove-every", $ShoveEvery,
              "--cost-shove-count", $ShoveCount,
              "--net-stats", (Join-Path $script:LogDir "$Tag.netstats.jsonl"))
    if ($Ablate) { $game += @("--probe-ablate", $Ablate) }
    $p = Start-Process -FilePath $script:GodotExe `
        -ArgumentList (@("--headless", "--path", $script:Root, "--") + $game) `
        -RedirectStandardOutput $out `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

function Start-WalkerBot([string]$Tag, [string]$Name, [int]$DurationSec, [string]$Goto, [string]$Ablate) {
    $game = @("--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
              "--world", "supermarket", "--duration", $DurationSec,
              "--goto-script", $Goto,
              "--log", (Join-Path $script:LogDir "$Tag.jsonl"))
    if ($Ablate) { $game += @("--probe-ablate", $Ablate) }
    $p = Start-Process -FilePath $script:GodotExe `
        -ArgumentList (@("--headless", "--path", $script:Root, "--") + $game) `
        -RedirectStandardOutput (Join-Path $script:LogDir "$Tag.out.log") `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

# A WINDOWED first-person bot: the only peer that renders, and therefore the only peer the render
# columns mean anything on. CELEBRATE-1's rule -- a capture bot without --capture-cam renders a
# flat grey world -- is why --first-person-cam is not optional here even when no frame is wanted.
function Start-LensBot([string]$Tag, [string]$Name, [int]$DurationSec, [string]$Marks) {
    $game = @("--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
              "--windowed", "--first-person-cam",
              "--goto-script", "$LaneX,$LaneZ", "--fp-look", "-90,0",
              "--world", "supermarket", "--duration", $DurationSec,
              "--log", (Join-Path $script:LogDir "$Tag.jsonl"))
    if ($Marks) { $game += @("--capture-dir", $CaptureDir, "--capture-at", $Marks) }
    $p = Start-Process -FilePath $script:GodotExe `
        -ArgumentList (@("--path", $script:Root, "--") + $game) `
        -RedirectStandardOutput (Join-Path $script:LogDir "$Tag.out.log") `
        -RedirectStandardError (Join-Path $script:LogDir "$Tag.err.log") `
        -PassThru
    $null = $p.Handle
    return $p
}

function Get-Percentiles([double[]]$Values) {
    if ($Values.Count -lt 3) { return $null }
    $s = @($Values | Sort-Object)
    return [pscustomobject]@{
        N    = $s.Count
        P50  = $s[[int][math]::Floor(0.50 * ($s.Count - 1))]
        P95  = $s[[int][math]::Floor(0.95 * ($s.Count - 1))]
        Peak = $s[-1]
    }
}

# Every number this script reports about a bot comes from its own JSONL, by FIELD NAME read off a
# line rather than guessed -- Measure-ShopFloor's "mean sent 0 B/s" bug was a reporter that could
# not find its field and printed a confident zero instead of saying so.
function Get-BotSamples([string]$Tag) {
    $jl = Join-Path $script:LogDir "$Tag.jsonl"
    if (-not (Test-Path $jl)) { return $null }
    $rows = @(Get-Content $jl | Where-Object { $_.Trim().Length -gt 0 } |
              ForEach-Object { try { $_ | ConvertFrom-Json } catch { } })
    if ($rows.Count -lt 5) { return $null }
    $have = $rows[0].PSObject.Properties.Name
    foreach ($f in @("pms", "prs", "fms", "draws")) {
        if ($have -notcontains $f) {
            Write-Host "  [client] $Tag : the log has no '$f' field -- this build predates the render instrument" -ForegroundColor Yellow
            return $null
        }
    }
    return [pscustomobject]@{
        Samples = $rows.Count
        Pms     = Get-Percentiles @($rows | ForEach-Object { [double]$_.pms })
        Prs     = Get-Percentiles @($rows | ForEach-Object { [double]$_.prs })
        Fms     = Get-Percentiles @($rows | Where-Object { [double]$_.fms -gt 0 } | ForEach-Object { [double]$_.fms })
        Draws   = (@($rows | ForEach-Object { [int]$_.draws }) | Measure-Object -Maximum).Maximum
        Objs    = (@($rows | ForEach-Object { [int]$_.objs }) | Measure-Object -Maximum).Maximum
    }
}

function Get-ServerSummary([string]$Tag) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    if (-not (Test-Path $out)) { return $null }
    $line = @(Get-Content $out | Where-Object { $_ -match '^\[reach-cost\] SUMMARY ' }) | Select-Object -Last 1
    if (-not $line) { return $null }
    $o = @{}
    foreach ($m in [regex]::Matches($line, '(\w+)=([^\s]+)')) { $o[$m.Groups[1].Value] = $m.Groups[2].Value }
    $seed = @(Get-Content $out | Where-Object { $_ -match '^\[probe-props\] SUMMARY ' }) | Select-Object -Last 1
    if ($seed) {
        foreach ($m in [regex]::Matches($seed, '(\w+)=([^\s]+)')) { $o["seed_" + $m.Groups[1].Value] = $m.Groups[2].Value }
    }
    $adopt = @(Get-Content $out | Where-Object { $_ -match '^\[props\] adopted ' }) | Select-Object -Last 1
    if ($adopt) { $o["adopted"] = $adopt }
    return [pscustomobject]$o
}

function Get-NetStats([string]$Tag) {
    $ns = Join-Path $script:LogDir "$Tag.netstats.jsonl"
    if (-not (Test-Path $ns)) { return $null }
    $rows = @(Get-Content $ns | Where-Object { $_.Trim().Length -gt 0 } |
              ForEach-Object { try { $_ | ConvertFrom-Json } catch { } })
    $live = @($rows | Where-Object { $_.PSObject.Properties.Name -contains "peers" -and [int]$_.peers -ge 2 })
    if ($live.Count -eq 0) { $live = $rows }
    if ($live.Count -eq 0) { return $null }
    return [pscustomobject]@{
        Seconds = $live.Count
        SentKbs = (($live | ForEach-Object { [double]$_.sent / [double]$_.intervalSec } | Measure-Object -Average).Average) / 1024.0
        Pkts    = ($live | ForEach-Object { [double]$_.sentPkts / [double]$_.intervalSec } | Measure-Object -Average).Average
    }
}

# ONE SERVER-SIDE STAGING: a headless server at population N with two headless bots walking.
function Invoke-ServerRun([string]$Tag, [int]$N, [double]$ShoveEvery, [int]$ShoveCount, [string]$Ablate) {
    Write-Host ">>> $Tag  (N=$N, shove every ${ShoveEvery}s capped at $ShoveCount$(if ($Ablate) { ", ablate=$Ablate" }))" -ForegroundColor Cyan
    $procs = @()
    try {
        $server = Start-ProbeServer $Tag $N $ShoveEvery $ShoveCount $Ablate
        $procs += $server
        # 240 s, not 90: seeding two thousand props and auditing every one of them with the
        # shipped layer 2 happens BEFORE the server listens, and that burst is itself one of the
        # numbers this packet owes (program R6).
        if (-not (Wait-ForLogLine (Join-Path $script:LogDir "$Tag.out.log") "\[server\] listening" 240)) {
            Stop-Proc $server
            Write-Host "  the server never reported listening on udp/$Port -- skipping this row" -ForegroundColor Red
            return $null
        }
        $botDur = [int]($WindowSec - 2)
        $b1 = Start-WalkerBot "${Tag}A" "ProbeA" $botDur "$LaneX,$LaneZ" $Ablate
        $procs += $b1
        Start-Sleep -Milliseconds 800
        $b2 = Start-WalkerBot "${Tag}B" "ProbeB" $botDur "44,$LaneZ" $Ablate
        $procs += $b2
        foreach ($b in @($b1, $b2)) { if (-not (Wait-ForExit $b ($botDur + 90))) { Stop-Proc $b } }
        if (-not (Wait-ForExit $server 180)) { Stop-Proc $server }
    } finally {
        Stop-Procs $procs
    }
    Start-Sleep -Milliseconds 600
    return Get-ServerSummary $Tag
}

# ONE CLIENT-SIDE STAGING: the same server, one WINDOWED first-person bot in the clear lane.
function Invoke-ClientRun([string]$Tag, [int]$N, [double]$ShoveEvery, [int]$ShoveCount, [string]$Marks) {
    Write-Host ">>> $Tag  (N=$N, windowed first-person client)" -ForegroundColor Cyan
    $procs = @()
    try {
        # The host outlives the client, always: Measure-ShopFloor measured what happens when it
        # does not -- the clients lose the server, never reach BotHarness.Finish, and write no
        # final sample at all.
        $server = Start-ProbeServer $Tag $N $ShoveEvery $ShoveCount ""
        $procs += $server
        if (-not (Wait-ForLogLine (Join-Path $script:LogDir "$Tag.out.log") "\[server\] listening" 240)) {
            Stop-Proc $server
            Write-Host "  the server never reported listening on udp/$Port -- skipping this row" -ForegroundColor Red
            return $null
        }
        $capDur = [int]($WindowSec - 4)
        $lens = Start-LensBot "${Tag}L" "ProbeLens" $capDur $Marks
        $procs += $lens
        if (-not (Wait-ForExit $lens ($capDur + 120))) { Stop-Proc $lens }
        Stop-Proc $server
    } finally {
        Stop-Procs $procs
    }
    Start-Sleep -Milliseconds 600
    return Get-BotSamples "${Tag}L"
}

function Show-ServerRow([string]$Label, $S, $Net) {
    if ($null -eq $S) { Write-Host ("  {0,-26} (no summary line)" -f $Label) -ForegroundColor Red; return }
    Write-Host ("  {0,-26} props={1,-5} p50 {2,7} p95 {3,7} peak {4,7}  active {5,-7} pairs {6,-7} islands {7,-5} gc0 {8,-4} alloc {9} MB  audits {10} ({11} corr){12}" -f
        $Label, $S.props, $S.frameP50Ms, $S.frameP95Ms, $S.framePeakMs,
        $S.activeBodies, $S.collisionPairs, $S.islands, $S.gc0, $S.allocMb,
        $S.audits, $S.corrections,
        $(if ($Net) { "  wire {0:N1} kB/s ({1:N0} pkt/s)" -f $Net.SentKbs, $Net.Pkts } else { "" })
    ) -ForegroundColor DarkGray
    if ($S.PSObject.Properties.Name -contains "seed_seeded") {
        Write-Host ("  {0,-26} seeded {1} in {2} ms; ONE-TICK AUDIT BURST {3} ms, {4} correction(s), {5} stuck" -f
            "", $S.seed_seeded, $S.seed_spawnMs, $S.seed_auditBurstMs, $S.seed_corrections, $S.seed_stuck) -ForegroundColor DarkGray
    }
}

function Show-ClientRow([string]$Label, $C) {
    if ($null -eq $C) { Write-Host ("  {0,-26} (no samples)" -f $Label) -ForegroundColor Red; return }
    Write-Host ("  {0,-26} {1,3} sample(s)  physics p50 {2,6:F2} p95 {3,6:F2}  process p50 {4,6:F2} p95 {5,6:F2}  FRAME p50 {6,6:F2} p95 {7,6:F2} peak {8,6:F2}  draws {9}  objs {10}" -f
        $Label, $C.Samples,
        $C.Pms.P50, $C.Pms.P95, $C.Prs.P50, $C.Prs.P95,
        $(if ($C.Fms) { $C.Fms.P50 } else { 0 }), $(if ($C.Fms) { $C.Fms.P95 } else { 0 }),
        $(if ($C.Fms) { $C.Fms.Peak } else { 0 }),
        $C.Draws, $C.Objs) -ForegroundColor DarkGray
}

Write-Host "=== PROBE-1: how many real items a room can hold (pass: $Pass) ===" -ForegroundColor White
Write-Host "Populations: $($Populations -join ', ')   runs per cell: $Runs   window: ${WindowSec}s" -ForegroundColor White

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    if ($Pass -in @("server", "all")) {
        Write-Host ""
        Write-Host "--- SERVER ---" -ForegroundColor White
        foreach ($n in $Populations) {
            for ($r = 1; $r -le $Runs; $r++) {
                $tag = "probe-rest-$n-$r"
                $s = Invoke-ServerRun $tag $n 0 0 ""
                Show-ServerRow "N=$n at rest, run $r" $s (Get-NetStats $tag)
            }
            for ($r = 1; $r -le $Runs; $r++) {
                $tag = "probe-wave-$n-$r"
                # A FIXED 100-prop wave at every population: the disturbance is held constant so
                # the column compares row to row (LaunchOptions.CostShoveCount).
                $s = Invoke-ServerRun $tag $n 3 100 ""
                Show-ServerRow "N=$n under a 100 wave, $r" $s (Get-NetStats $tag)
            }
        }
    }

    if ($Pass -in @("client", "all")) {
        Write-Host ""
        Write-Host "--- CLIENT (windowed, first person, mid-aisle) ---" -ForegroundColor White
        foreach ($n in $Populations) {
            for ($r = 1; $r -le $Runs; $r++) {
                # One frame per population on the first run only: frames are free but a second
                # copy of the same shot is not evidence of anything.
                $marks = if ($r -eq 1) { "8,12" } else { "" }
                $c = Invoke-ClientRun "probe-client-$n-$r" $n 0 0 $marks
                Show-ClientRow "N=$n at rest, run $r" $c
            }
        }
    }

    if ($Pass -in @("ablate", "all")) {
        Write-Host ""
        Write-Host "--- THE ANOMALY: one build, one machine, the fixes off and on (N=$AblateAt) ---" -ForegroundColor White
        foreach ($mode in @(@{ Tag = "base"; Flag = "all" },
                            @{ Tag = "loop"; Flag = "carryable" },
                            @{ Tag = "carryable"; Flag = "loop" },
                            @{ Tag = "both"; Flag = "" })) {
            for ($r = 1; $r -le $Runs; $r++) {
                $tag = "probe-ablate-$($mode.Tag)-$r"
                $s = Invoke-ServerRun $tag $AblateAt 0 0 $mode.Flag
                Show-ServerRow "fixes on: $($mode.Tag), run $r" $s (Get-NetStats $tag)
            }
        }
        Write-Host "  (row 'base' = both fixes ablated = the tree at integration/2026-09-19-mvp;" -ForegroundColor DarkGray
        Write-Host "   'both' = both fixes live. The two middle rows attribute the saving.)" -ForegroundColor DarkGray
    }
} finally {
    Exit-SuiteMutex $mutex
}

if ($Pass -in @("client", "all")) {
    Write-Host ""
    Write-Host "Frames in $CaptureDir :" -ForegroundColor White
    foreach ($shot in @(Get-ChildItem -Path $CaptureDir -Filter "*.png" -ErrorAction SilentlyContinue | Sort-Object Name)) {
        # FP-1's lesson: compare a capture's SIZE to one known good before believing it. A few
        # hundred bytes is a photograph of nothing.
        Write-Host ("  {0,-40} {1,9:N0} bytes" -f $shot.Name, $shot.Length)
    }
}

Write-Host ""
Write-Host "PROBE-CAPACITY: done (this script asserts nothing)." -ForegroundColor Green
exit 0
