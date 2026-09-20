<#
.SYNOPSIS
    The task room's sorting job, server-authoritative: objects settled in the right bin raise the
    count on the wire on EVERY peer, the wrong bin buzzes and costs nothing, an object counts
    once however often it is re-placed, and the count freezes at the Found tick with the hider's
    hands emptied by the burst.

.DESCRIPTION
    TASK-1's gate (packet step 2). Port udp/7907, GIVEN BY THE ORCHESTRATOR rather than computed
    from a snapshot of tests/ -- INT-0's lesson, applied rather than re-learned: 7893-7906 are
    claimed and three lanes off one base have now twice picked the same "next free" number
    because none of them could see the others. The ladder table lives in
    .claude/rules/test-suite.md.

    One headless server on --world supermarket, driven end to end by --round-script, plus two
    headless bots. The FIRST to connect is the hider (HideSeekLoop assigns roles by join order,
    program section 1 item 5), so it is the one carrying --sort-script; the second exists because
    the loop refuses to start a round without exactly two humans, and because every assertion
    below is made on BOTH peers' logs rather than on the server's.

    THE SCHEDULE HAS NO WALL-CLOCK GUESS IN THE BOT'S HALF. --sort-script names (prop, bin) pairs
    and nothing else: each step advances on an OBSERVED hand-off, and the whole loop is gated on
    the round being in Seeking, which is when the hider is in the task room at all. The bots'
    --duration is derived from the round script's own last verb rather than typed beside it
    (.claude/rules/test-suite.md, FIX-1).

    ROUND 1 IS A COLOUR ROUND (SortRule.For: odd -> colour), so bin 0 wants RED, bin 1 BLUE and
    bin 2 YELLOW. The script is five steps:

      1144 (Sort_000_Red)    -> bin 0   RIGHT   count 1
      1147 (Sort_003_Blue)   -> bin 2   WRONG   buzzes, count unchanged
      1150 (Sort_006_Yellow) -> bin 2   RIGHT   count 2
      1144 again             -> bin 0   ALREADY COUNTED -- must not count again
      1153 (Sort_009_Red)    -> held    stays in the hider's hands for the burst

    Ids are 1144..1161 because PropManager.AdoptAuthoredProps assigns by sorted NODE PATH and
    "TaskRoom" sorts AFTER "HoldingRoom" and "SearchRoom", so the sortables take the END of the
    block. RE-DERIVED AT INT-1 (2026-09-19) FROM Run-AuthoredPropTest's adoption log on the
    merged tree, which printed `[props] adopted 162 authored prop(s) ... (ids 1000..1161)`. On
    TASK-1's own base they were 1004..1021, because that base held only CARRY-1's four crates in
    the search room -- no BTN-1 rack, no HOLD-1 practice corner, no SHELF-1 aisle props. An
    authored id is a fact about the WHOLE world and a lane's number is only true on that lane's
    base; SHELF-1's section in .claude/rules/test-suite.md is the rule and this is the third
    suite to pay it.

    Asserted:

      1. THE COUNT AGREES ON EVERY PEER. In each bot's last Together sample -- the instant the
         count is frozen and still live on the wire -- roundSorts is 2. Sampled at Together
         rather than "the last sample" for MATCH-1's measured reason: the reset zeroes the live
         value while the card still describes the round, and a suite that read the wrong instant
         would be comparing two different moments.
      2. THE CARD AGREES WITH IT. The round card's hiderGained is 2 on both peers.
      3. THE WRONG BIN BUZZED, ON EVERY PEER. Each of the three processes logged
         `sort-bad prop=1147 bin=2`, and none logged `sort-good` for it. Asserted per peer
         because TASK-1 puts nothing new on any wire: each peer DERIVES its own verdicts from
         replicated props, so "the server scored it and the clients heard nothing" is exactly
         the failure this has to be able to see.
      4. ONE OBJECT, ONE COUNT. `sort-good prop=1144` appears exactly once per process across
         the whole run, even though 1144 was delivered to bin 0 twice.
      5. THE BURST TOOK WHAT WAS IN THE HAND. Prop 1153 is held by the hider in some sample
         before the found tick and unheld in the last sample, and the server logged
         `burst force-drop: released 1 prop(s)`.
      6. NOTHING MOVED AFTER THE FOUND TICK. No sort-good line on any peer carries a total above
         2, and no `[sort]` line at all appears after the burst.

    Exit 0 = PASS. No human interaction. Headless throughout: nothing here needs a renderer, and
    the two audio cues are asserted through their log lines rather than through a speaker.
#>
[CmdletBinding()]
param(
    # 7907. See the .DESCRIPTION: handed out, not computed. BTN-1 holds 7906 on its own branch.
    [int]$Port = 7907,
    [switch]$SkipBuild,
    [int]$MutexTimeoutMinutes = 120
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# --- the round's schedule, and the bots' lifetime derived FROM it -------------------------------
# start@8    both bots are connected by then (they are launched ~0.5 s apart and connect in ~2 s)
# confirm@14 ends the 30 s hide early; Seeking begins here and the hider lands in the task room
# found@60   46 s of Seeking, against the ~20 s the five-step script measures at (about 3 s a
#            step once the hider is in the room) -- a 2.3x margin for a loaded marathon
# end@66     the End press, so the run exercises Together -> Tally rather than a timeout
$FoundAtSec = 60
$EndAtSec   = 66
$Script     = "start@8,confirm@14,found@$FoundAtSec,end@$EndAtSec"

# Bot lifetime: the End press, plus the match-tally arm (HideSeekTuning.MatchTallySec, 10 s), plus
# a margin for the reset edge to land and be sampled. Derived from $EndAtSec so moving the
# schedule moves this with it.
$DurationSec = $EndAtSec + 22

# The five steps. Prop ids and bin slots only; the bins' coordinates come out of the bot's own
# copy of the authored room (SortBin.Find), so this fixture cannot drift from the level.
$SortScript = "1144:0,1147:2,1150:2,1144:0,1153:-1"

$RightA   = 1144   # Sort_000_Red    -> bin 0 (RED)
$WrongOne = 1147   # Sort_003_Blue   -> bin 2 (YELLOW)
$RightB   = 1150   # Sort_006_Yellow -> bin 2 (YELLOW)
$HeldOne  = 1153   # Sort_009_Red    -> never put down
$ExpectedSorts = 2

# HideSeekPhase ordinals, as they ride the wire.
$PhaseSeeking  = 2
$PhaseTogether = 3

Write-Host "=== sort: right bin counts once, wrong bin buzzes, the burst empties the hand ===" -ForegroundColor White

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    Write-Host "[1/3] launching dedicated server (supermarket, round script '$Script')..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "sort.server.out.log"
    $server = Start-Godot @("--server", "--port", $Port, "--world", "supermarket",
        "--round-script", $Script) "sort.server"
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 30)) {
        Write-Fail "the server never reported listening on udp/$Port; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"

    Write-Host "[2/3] launching the hider (with the sort script) and the seeker..." -ForegroundColor Cyan

    # THE HIDER CONNECTS FIRST AND THAT IS LOAD-BEARING: HideSeekLoop assigns roles by join order
    # and only the hider is ever teleported into the task room, so a script on the second bot
    # would be a bot walking at a wall in the search room for sixty seconds.
    $hiderLog = Join-Path $script:LogDir "sort-hider.jsonl"
    $hiderOut = Join-Path $script:LogDir "sort-hider.out.log"
    $hider = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "SortHider",
        "--log", $hiderLog, "--duration", $DurationSec, "--world", "supermarket",
        "--sort-script", $SortScript) "sort-hider"
    $procs += $hider
    Start-Sleep -Milliseconds 600

    $seekerLog = Join-Path $script:LogDir "sort-seeker.jsonl"
    $seekerOut = Join-Path $script:LogDir "sort-seeker.out.log"
    $seeker = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "SortSeeker",
        "--log", $seekerLog, "--duration", $DurationSec, "--world", "supermarket") "sort-seeker"
    $procs += $seeker

    $bots = @(
        @{ Name = "SortHider";  Proc = $hider;  JsonLog = $hiderLog;  OutLog = $hiderOut }
        @{ Name = "SortSeeker"; Proc = $seeker; JsonLog = $seekerLog; OutLog = $seekerOut }
    )
    $deadline = (Get-Date).AddSeconds($DurationSec + 90)
    foreach ($b in $bots) {
        $remainingMs = [int]((($deadline - (Get-Date)).TotalSeconds) * 1000)
        if ($remainingMs -lt 1000) { $remainingMs = 1000 }
        if (-not $b.Proc.WaitForExit($remainingMs)) { Write-Fail "$($b.Name) did not exit within timeout" }
    }
    foreach ($b in $bots) {
        if ($b.Proc.ExitCode -ne 0) {
            # BASE-1 measured a bot dying with -1073741795 AFTER printing its completion line:
            # a teardown artifact, not a failed run. Say which this is rather than guessing.
            $done = @(Select-String -Path $b.OutLog -Pattern "\[bot\] $($b.Name) done" -ErrorAction SilentlyContinue)
            $note = if ($done.Count -gt 0) { " (it printed its own done line first - see BASE-1's teardown note)" } else { "" }
            Write-Fail "$($b.Name) exited with code $($b.Proc.ExitCode)$note; see $($b.OutLog)"
        }
    }
} finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host "[3/3] verifying the count, the buzz, the once-only rule and the burst..." -ForegroundColor Cyan

function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

function Get-Prop($Sample, [int]$PropId) {
    $p = @($Sample.props) | Where-Object { [int]$_.id -eq $PropId }
    if ($null -eq $p -or @($p).Count -eq 0) { return $null }
    return ($p | Select-Object -First 1)
}

# Every [sort] line a process printed, in order, as objects. One parser, three logs.
#
# A PLAIN ARRAY, not a List[object]: in Windows PowerShell 5.1, `@($list)` on a
# System.Collections.Generic.List[object] throws `Argument types do not match` at the wrapping
# `@(...)` itself, with no useful line in the trace -- measured here, and it read exactly like a
# type error in the assertion that followed. The counts here are single digits, so += costs
# nothing and the quirk is unsayable.
function Get-SortLines([string]$Path) {
    $out = @()
    if (-not (Test-Path $Path)) { return $out }
    foreach ($m in Select-String -Path $Path -Pattern "\[sort\] sort-(good|bad) prop=(\d+) bin=(\d+) \((\w+)\) rule=(\w+) total=(\d+) peer=(\d+)") {
        $g = $m.Matches[0].Groups
        $out += [pscustomobject]@{
            Verdict = $g[1].Value
            Prop    = [int]$g[2].Value
            Bin     = [int]$g[3].Value
            Word    = $g[4].Value
            Rule    = $g[5].Value
            Total   = [int]$g[6].Value
            Peer    = [int]$g[7].Value
        }
    }
    return $out
}

$samplesH = Get-Samples $hiderLog
$samplesS = Get-Samples $seekerLog
$peerH = [int]$samplesH[0].self
$peerS = [int]$samplesS[0].self
Write-Host "        peers: hider=$peerH seeker=$peerS, samples $($samplesH.Count)/$($samplesS.Count)" -ForegroundColor DarkGray

$failures = New-Object System.Collections.Generic.List[string]
function Add-Failure([string]$Text) { $failures.Add($Text) | Out-Null }

# --- the census the room prints on its first poll ----------------------------------------------
# A derivation that silently answered its C# default would be eighteen red cubes that look
# perfectly normal in a diff. This is the line that would say so.
$census = @(Select-String -Path $serverOut -Pattern "\[sort\] task room ready: (.+)$")
if ($census.Count -eq 0) {
    Add-Failure "the server never printed the task room's census -- SortRoom never found its objects"
} else {
    $line = $census[0].Matches[0].Groups[1].Value
    Write-Host "        census: $line" -ForegroundColor DarkGray
    foreach ($want in @("18 sortable\(s\)", "3 bin\(s\)", "red=6", "blue=6", "yellow=6",
                        "cube=6", "ball=6", "can=6", "unresolved=0", "rule=COLOUR")) {
        if ($line -notmatch $want) {
            Add-Failure "the task room census does not say '$want' -- it says: $line"
        }
    }
}

# --- 1 & 2: the count and the card, at the instant they describe the same moment ---------------
foreach ($b in @(@{ N = "hider"; S = $samplesH }, @{ N = "seeker"; S = $samplesS })) {
    $together = @($b.S | Where-Object { [int]$_.roundPhase -eq $PhaseTogether -and $_.roundSynced })
    if ($together.Count -eq 0) {
        Add-Failure "$($b.N) never sampled the Together phase -- the found tick never landed on it"
        continue
    }
    $last = $together[-1]
    $got = [int]$last.roundSorts
    Write-Host "        $($b.N): roundSorts $got at Together over $($together.Count) sample(s)" -ForegroundColor DarkGray
    if ($got -ne $ExpectedSorts) {
        Add-Failure "$($b.N)'s last Together sample says roundSorts=$got, expected $ExpectedSorts"
    }

    $card = @($b.S | Where-Object { $null -ne $_.roundTally })
    if ($card.Count -eq 0) {
        Add-Failure "$($b.N) never received a round card"
    } else {
        $gained = [int]$card[-1].roundTally.hiderGained
        if ($gained -ne $ExpectedSorts) {
            Add-Failure "$($b.N)'s card credits the hider $gained sort(s); the count on the wire was $ExpectedSorts"
        }
    }
}

# --- 3, 4 & 6: the verdicts, per process -------------------------------------------------------
$logs = @(
    @{ Name = "server"; Path = $serverOut }
    @{ Name = "hider";  Path = $hiderOut }
    @{ Name = "seeker"; Path = $seekerOut }
)
$goodTotal = 0; $badTotal = 0; $dupTotal = 0
foreach ($l in $logs) {
    $lines = Get-SortLines $l.Path
    $good = @($lines | Where-Object { $_.Verdict -eq "good" })
    $bad  = @($lines | Where-Object { $_.Verdict -eq "bad" })
    $goodTotal += $good.Count; $badTotal += $bad.Count

    Write-Host ("        {0,-6}: {1} sort-good, {2} sort-bad" -f $l.Name, $good.Count, $bad.Count) -ForegroundColor DarkGray

    # 3: the wrong bin buzzed HERE, and was never scored here.
    $wrongBuzz = @($bad | Where-Object { $_.Prop -eq $WrongOne -and $_.Bin -eq 2 })
    if ($wrongBuzz.Count -lt 1) {
        Add-Failure "$($l.Name) never logged sort-bad for prop $WrongOne in bin 2 -- the wrong bin was silent on this peer"
    }
    if (@($good | Where-Object { $_.Prop -eq $WrongOne }).Count -gt 0) {
        Add-Failure "$($l.Name) SCORED prop $WrongOne -- a blue cube in the YELLOW bin on a colour round"
    }

    # 4: one object, one count -- 1144 went into bin 0 twice.
    $dupes = @($good | Where-Object { $_.Prop -eq $RightA })
    $dupTotal += [Math]::Max($dupes.Count - 1, 0)
    if ($dupes.Count -ne 1) {
        Add-Failure "$($l.Name) logged $($dupes.Count) sort-good line(s) for prop $RightA; it was delivered to bin 0 twice and must count ONCE"
    }
    foreach ($p in @($RightA, $RightB)) {
        if (@($good | Where-Object { $_.Prop -eq $p }).Count -lt 1) {
            Add-Failure "$($l.Name) never scored prop $p, which went into its own colour's bin"
        }
    }

    # 6: nothing counted past two.
    $over = @($lines | Where-Object { $_.Total -gt $ExpectedSorts })
    if ($over.Count -gt 0) {
        Add-Failure "$($l.Name) logged a sort with total=$($over[0].Total), above the expected $ExpectedSorts"
    }
}

# --- 5: the burst took what was in the hand ----------------------------------------------------
$heldEver = @($samplesH | Where-Object {
    $p = Get-Prop $_ $HeldOne
    $null -ne $p -and [int]$p.holder -eq $peerH -and [int]$_.roundPhase -eq $PhaseSeeking
})
if ($heldEver.Count -eq 0) {
    Add-Failure "the hider was never seen holding prop $HeldOne during Seeking -- the burst had nothing to take, so the drop below proves nothing"
} else {
    Write-Host "        hider held prop $HeldOne in $($heldEver.Count) Seeking sample(s)" -ForegroundColor DarkGray
}
$finalHeld = Get-Prop $samplesH[-1] $HeldOne
if ($null -eq $finalHeld) {
    Add-Failure "the hider's final sample has no prop $HeldOne at all"
} elseif ([int]$finalHeld.holder -ne 0) {
    Add-Failure "prop $HeldOne is still held by $($finalHeld.holder) in the hider's final sample -- the burst's forced drop never happened"
}
$forceDrop = @(Select-String -Path $serverOut -Pattern "\[door\] burst force-drop: released (\d+) prop")
$released = if ($forceDrop.Count -gt 0) { [int]$forceDrop[0].Matches[0].Groups[1].Value } else { -1 }
if ($released -lt 1) {
    Add-Failure "the server's burst force-drop released $released prop(s); the hider was holding one"
}

# --- the numbers to compare across runs, not the verdict ---------------------------------------
$sortedAtTally = 0
$tallySamples = @($samplesH | Where-Object { $null -ne $_.roundTally })
if ($tallySamples.Count -gt 0) { $sortedAtTally = [int]$tallySamples[-1].roundTally.hiderGained }

Write-Host ""
Write-Host "MEASURED QUANTITIES (compare these across runs, not the verdict):" -ForegroundColor White
Write-Host ("  right sorts counted on the wire:      {0}" -f $ExpectedSorts)
Write-Host ("  sort-good lines across 3 processes:   {0}" -f $goodTotal)
Write-Host ("  sort-bad lines across 3 processes:    {0}" -f $badTotal)
Write-Host ("  duplicate counts for a re-placed obj: {0}" -f $dupTotal)
Write-Host ("  burst force-dropped:                  {0} prop(s)" -f $released)
Write-Host ("  hider's card at Tally:                {0} sorted" -f $sortedAtTally)

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "SORT-TEST FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "SORT-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: 2 sorted on every peer, the wrong bin buzzed on every peer and cost nothing, a re-placed object counted once, and the burst emptied the hider's hands." -ForegroundColor Green
Write-Host ""
Write-Host "SORT-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
