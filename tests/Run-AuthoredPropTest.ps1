<#
.SYNOPSIS
    SHELF-1: the authored-prop adoption proof. Every prop AUTHORED into the search room is
    adopted with the id its node path says it has, identically on a dedicated server, on a
    client that was there from the start, and on one that joins late -- and the id names the
    same object on all three.

.DESCRIPTION
    THE DEBT THIS PAYS. docs/PRUNE-BACKLOG.md listed "the authored-prop adoption proof": BASE-1
    deleted the fork's Run-AuthoredPropTest.ps1 because the supermarket had no authored props to
    adopt, and wrote that it "belongs with SHELF-1's hundred props". There are now 144 of them --
    130 in the search room, BTN-1's three on the holding room's rack, and HOLD-1's eleven in the
    practice corner.

    WHAT IS ACTUALLY AT RISK, and it is not whether AdoptAuthoredProps runs. It is the ID
    ASSIGNMENT RULE: ids start at 1000 and are handed out in ORDINAL NODE-PATH SORT ORDER, which
    means the id of every prop in the world is a function of the NAME of every other one. Nothing
    about that is visible in a diff. Rename one product node, or drop one in beside CARRY-1's
    crates instead of under Stock/, and every id above it shifts by one -- silently, on every
    peer identically, so no desync check anywhere would notice. What breaks is everything that
    NAMES an id: Run-PlaceTest's four crates, BTN-1's drop-off bin, REACH-1's target, a hider's
    hidden object surviving a reconnect.

    So this suite asserts the BLOCK, not just the mechanism:

        1000         ObjectRack/Deal_0        the rack's near-miss can      PropKind.Can
        1001         ObjectRack/Deal_1        the rack's near-miss produce  PropKind.Produce
        1002         ObjectRack/Deal_2        the rack's near-miss box      PropKind.Box
        1003..1010   PracticeCorner/Cube_00.. the eight practice crates     PropKind.Crate
        1011         PracticeCorner/Near_00   practice near-miss can        PropKind.Can
        1012         PracticeCorner/Near_01   practice near-miss produce    PropKind.Produce
        1013         PracticeCorner/Near_02   practice near-miss box        PropKind.Box
        1014..1017   SearchRoom/Prop_0..3     CARRY-1's four crates         PropKind.Crate
        1018..1023   Stock/Bin_0..5           the six floor bins            PropKind.Box
        1024..1071   Stock/Box_000..047       cereal boxes                  PropKind.Box
        1072..1119   Stock/Can_000..047       cans                          PropKind.Can
        1120..1143   Stock/Produce_*          produce                       PropKind.Produce

    RE-DERIVED TWICE BY HOLD-1 (2026-09-19), and the shift is this suite's own thesis arriving on
    schedule -- twice in one lane. First when BTN-1 was merged in: its rack took 1000..1002 and
    every search-room id moved up by three. Then again when this lane's own practice corner
    landed: eleven more props in the holding room, and the search room moved up by eleven more.
    The rule underneath both is one sentence -- "HoldingRoom" sorts before "SearchRoom" and a
    room name is a PREFIX of every path under it, so SHELF-1's Stock/ trick, which protects the
    crates from the products in the SAME room, cannot protect a room from a room that sorts
    before it. No number here was re-typed; every one was re-READ off the server's own per-prop
    adoption lines in tests/logs/authoredprop.server.out.log.

    THIS SUITE KEEPS ITS LITERALS ON PURPOSE, while Run-PlaceTest and Run-ButtonsTest now DERIVE
    theirs (Get-AuthoredPropId, _Common.ps1). The division is the point. Those two use an id to
    stage something and do not care what it is, so a literal there is a dependency nobody can see
    in a diff and the fix is to stop typing it. This suite exists to ASSERT the block, so the
    numbers ARE its subject -- a version of it that derived them would pass whatever the world
    did, which is the one thing it must not do.

    FIVE PHASES OF EVIDENCE, each answering something the others cannot:

      1. THE SERVER'S OWN COUNT. PropManager prints "[props] adopted N authored prop(s) in T ms
         (ids A..B)" on every peer since SHELF-1. The server's line must say 144 and 1000..1143.
         T is REPORTED, never gated -- adopt time is a number SHELF-1's packet asks for, and a
         budget nobody has agreed to is not a test.

      2. THE SAME LINE FROM BOTH CLIENTS, byte-equal on the count and the range. Adoption is
         LOCAL -- every peer walks its own copy of the same .tscn -- so two peers printing
         different ranges is the failure mode that produces two players holding "prop 1042" and
         meaning different objects. Nothing on the wire would catch it.

      3. COMPLETE IN THE LATE JOINER'S FIRST SAMPLE. Bot B connects ~8 s after bot A, and all 144
         ids must be in the FIRST line it ever writes. That is the difference between "adopted"
         and "streamed": an authored prop is never spawned and never sent, so a late joiner that
         had to wait for a dump would be showing an empty shop for a frame or two.

      4. THE KIND OF EVERY ID, against the block table above, on both peers. This is what makes a
         rename a red rather than a rumour.

      5. ONE POSITION PER BLOCK, and cross-peer agreement on ALL of them. The eight landmark ids
         are the FIRST of each block, checked against the pose authored in SearchRoom.tscn -- a
         rename INSIDE a block keeps every kind right and moves exactly these. Then every one of
         the 144 is compared between A's and B's final samples: same id, same place, or the two
         peers do not agree about what the number means.

         BE HONEST ABOUT WHAT THE CROSS-PEER HALF PROVES HERE. Nothing moves in this run, and an
         authored prop at rest is at its authored transform on every peer by construction, so a
         green reads 0.0000 m and would read 0.0000 m even if the stream were dead. It is kept
         because it is free and because it DOES catch a peer that built a different world -- but
         the suite that proves a MOVING prop converges across peers is Run-PlaceTest (on these
         same authored crates) and Run-CarryNetTest, not this one.

    PROVED ABLE TO FAIL. See docs/agents/handoffs/2026-09-19-SHELF-1.md for the planted rename
    (Stock/Can_000 -> Stock/Can_900) and exactly which lines went red.

    THE SUITE GATES ON THE LINES, not on the exit code, for the reason
    Run-SupermarketWorldTest.ps1 does: a process that dies before its own summary exits non-zero
    for reasons that have nothing to do with the subject. Bot exit codes are checked too, after.

    Exit 0 = PASS. Headless throughout. Takes the machine-wide suite mutex like every other.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param(
    # 7908. HANDED OUT BY THE ORCHESTRATOR, not computed from a snapshot of tests/ -- which is
    # INT-0's measured lesson (three lanes off one base all picked 7896) applied rather than
    # re-learned. The ladder as of this lane: 7893/7894/7895 Run-CarryNetTest, 7896
    # Run-RoundLoopSmoke, 7897 Run-FirstPersonTest, 7898 Run-PlaceTest, 7899 Run-VoiceRoomTest,
    # 7900 Run-BurstDoorTest, 7901 BTN-1 (reserved), 7902 Run-MaterialSfxTest, 7903 Run-ReachTest,
    # 7904 Run-RoundClockTest, 7905 INT-0B's unregistered match-end probe, 7906 BTN-1, 7907
    # TASK-1. The one table is in .claude/rules/test-suite.md.
    [int]$Port = 7908,
    [int]$MutexTimeoutMinutes = 120,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# --- the block table, which is the subject of this suite ---------------------------------------
# PropKind ordinals (scripts/net/PropState.cs): Crate 0, Ball 1, Can 2, Box 3, Produce 4.
$AuthoredFirst = 1000
$AuthoredLast  = 1161
$AuthoredCount = $AuthoredLast - $AuthoredFirst + 1     # 162
#
# RE-DERIVED AT INT-1 (2026-09-19) FROM THIS SUITE'S OWN ADOPTION LOG on the fully merged tree,
# not re-typed and not predicted. The run that produced these numbers printed:
#
#   [props] adopted 162 authored prop(s) in 169.96 ms (ids 1000..1161)
#
# 144 (HOLD-1's rack + practice corner + search room) + TASK-1's 18 sortables. "TaskRoom" sorts
# AFTER "SearchRoom", so TASK-1's block went to the END and NOTHING BELOW 1144 MOVED -- which is
# the one happy case of SHELF-1's rule, and worth stating for that reason: TASK-1's own branch
# numbered these 1004..1021, because on ITS base the search room held only four crates. An id is
# a fact about the WHOLE world, so a lane's own number is only ever true on that lane's base.
#
# THIS SUITE KEEPS ITS LITERALS ON PURPOSE. SHELF-1 gave Run-PlaceTest and Run-ButtonsTest
# Get-AuthoredPropId so they DERIVE the ids they stage with; this one exists to ASSERT the
# block, and a version that derived them would pass whatever the world did. That division is
# the point, and it is why this table had to be updated by hand and those two did not.
$Blocks = @(
    # The rack is three objects of three DIFFERENT kinds, so it is three one-id blocks rather
    # than one. That is not a workaround: the rack's whole design is one of each shape, and a
    # "block" here is a run of ids that share a kind. HOLD-1's three practice near-misses are
    # the same three shapes for the same reason.
    @{ Name = "rack: near-miss can";         First = 1000; Last = 1000; Kind = 2 }
    @{ Name = "rack: near-miss produce";     First = 1001; Last = 1001; Kind = 4 }
    @{ Name = "rack: near-miss box";         First = 1002; Last = 1002; Kind = 3 }
    @{ Name = "practice crates";             First = 1003; Last = 1010; Kind = 0 }
    @{ Name = "practice: near-miss can";     First = 1011; Last = 1011; Kind = 2 }
    @{ Name = "practice: near-miss produce"; First = 1012; Last = 1012; Kind = 4 }
    @{ Name = "practice: near-miss box";     First = 1013; Last = 1013; Kind = 3 }
    @{ Name = "CARRY-1 crates";              First = 1014; Last = 1017; Kind = 0 }
    @{ Name = "floor bins";                  First = 1018; Last = 1023; Kind = 3 }
    @{ Name = "cereal boxes";                First = 1024; Last = 1071; Kind = 3 }
    @{ Name = "cans";                        First = 1072; Last = 1119; Kind = 2 }
    @{ Name = "produce";                     First = 1120; Last = 1143; Kind = 4 }
    # TASK-1's eighteen sortables, arriving at INT-1. EIGHTEEN ONE-ID BLOCKS AND NOT THREE RUNS,
    # and that is the crate's design rather than an awkward table: the supply crate is
    # INTERLEAVED cube / ball / can, deliberately, so no row and no column of its 6 x 3 grid is
    # a group under either sort rule -- a pre-sorted crate would hand the player round 1's
    # answer. A "block" here is a run of ids sharing a kind, and in an interleaved crate every
    # run is length one. Kinds come from the COLLIDER via Carryable.ShapeFromCollider, the same
    # table that decides the sound: SortCube is a cubic BoxShape3D -> Crate (0), SortBall is a
    # SphereShape3D r 0.08 <= 0.17 -> Produce (4), SortCan is a CylinderShape3D -> Can (2).
    # If this cycle ever breaks, TaskRoom.tscn was reordered.
) + (0..17 | ForEach-Object {
    $id = 1144 + $_
    $shape = @(
        @{ Word = "cube"; Kind = 0 }
        @{ Word = "ball"; Kind = 4 }
        @{ Word = "can";  Kind = 2 }
    )[$_ % 3]
    @{ Name = ("sortable {0:d3} ({1})" -f $_, $shape.Word); First = $id; Last = $id; Kind = $shape.Kind }
})

# The first id of each block, at its AUTHORED world pose. SearchRoom.tscn is instanced at x = +40
# by Supermarket.tscn, so these are the scene's local numbers plus 40 on x. Tolerances are
# per-axis-free (a 3D distance) and generous on the two that physics can legitimately move:
# a body resting on a shelf settles a millimetre or two, and a bin's reported position is its
# base. A rename shifts a landmark by at least 0.16 m (the tightest product spacing in the room),
# so 0.08 m separates "settled" from "renumbered" with room to spare.
$LandmarkToleranceM = 0.08
#
# The rack landmark is the exception to the "+40 on x" sentence above: HoldingRoom.tscn is
# instanced at the ORIGIN, so its local numbers are already world numbers.
$Landmarks = @(
    @{ Id = 1000; Name = "ObjectRack/Deal_0";      At = @(-0.60, 1.090, -4.60) }
    @{ Id = 1003; Name = "PracticeCorner/Cube_00"; At = @(-3.55, 0.220, -0.90) }
    @{ Id = 1011; Name = "PracticeCorner/Near_00"; At = @(-4.55, 0.760, -1.10) }
    @{ Id = 1014; Name = "Prop_0 (CARRY-1 crate)"; At = @(36.00, 0.220, 0.00) }
    @{ Id = 1018; Name = "Stock/Bin_0";            At = @(33.60, 0.000, -4.30) }
    @{ Id = 1024; Name = "Stock/Box_000";          At = @(36.19, 0.565, -1.16) }
    @{ Id = 1072; Name = "Stock/Can_000";          At = @(36.31, 0.485, -3.26) }
    @{ Id = 1120; Name = "Stock/Produce_000";      At = @(44.85, 0.505, -1.40) }
    # TASK-1's first sortable (INT-1). TaskRoom.tscn is instanced at x = +80 by Supermarket.tscn
    # and Sort_000_Red is authored at local (-2.55, 0.695, 0.68), so world x is 77.45. ONE
    # landmark for the whole crate rather than eighteen: the blocks above already assert every
    # id's kind, and what a landmark adds is "the block did not SHIFT" -- which one id at the
    # boundary answers, exactly as the seven above do for theirs. The sortables sit 0.22 m
    # apart, so the 0.08 m bar separates a settle from a renumbering here too.
    @{ Id = 1144; Name = "Sort_000_Red (TASK-1)";  At = @(77.45, 0.695, 0.68) }
)

# Cross-peer agreement. A client mirrors a Resting prop from the server's broadcast, so the two
# should be identical rather than merely close; 0.10 m is the same bar Run-PlaceTest uses for
# "the two peers agree where it is" and leaves room for a prop that is still settling when the
# logs stop.
$PeerAgreementM = 0.10

$BotADurationSec = 26
$BotBDurationSec = 16      # joins ~8 s in and stops with A
$LateJoinDelaySec = 8

Write-Host "=== SHELF-1: the authored-prop adoption proof (162 props, server + late joiner) ===" -ForegroundColor White

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
$script:Failures = @()
function Add-Failure([string]$m) { $script:Failures += $m }

function Start-AuthoredBot([string]$Tag, [string]$Name, [int]$DurationSec) {
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

# "[props] adopted 144 authored prop(s) in 4.21 ms (ids 1000..1143)" -> @{Count;First;Last;Ms}
function Get-AdoptLine([string]$Path, [string]$Who) {
    if (-not (Test-Path $Path)) { Add-Failure "$Who wrote no log at all ($Path)"; return $null }
    $line = @(Select-String -Path $Path -Pattern '^\[props\] adopted ' | ForEach-Object { $_.Line }) |
        Select-Object -Last 1
    if (-not $line) {
        Add-Failure "$Who never printed a '[props] adopted' line -- AdoptAuthoredProps did not run on that peer"
        return $null
    }
    if ($line -notmatch 'adopted (\d+) authored prop\(s\) in ([0-9.]+) ms \(ids (\d+)\.\.(\d+)\)') {
        Add-Failure "$Who printed an adoption line this suite cannot parse: '$line'"
        return $null
    }
    return @{ Count = [int]$Matches[1]; Ms = [double]$Matches[2]
              First = [int]$Matches[3]; Last = [int]$Matches[4]; Line = $line.Trim() }
}

function Get-Samples([string]$Path, [string]$Who) {
    if (-not (Test-Path $Path)) { Add-Failure "$Who wrote no JSONL at all ($Path)"; return @() }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Add-Failure "$Who wrote an empty JSONL"; return @() }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

# id -> the prop object, for one sample. Authored ids only (>= 1000).
function Get-AuthoredMap($Sample) {
    $map = @{}
    foreach ($p in @($Sample.props)) {
        $id = [int]$p.id
        if ($id -ge $AuthoredFirst) { $map[$id] = $p }
    }
    return $map
}

function Dist3($a, $b) {
    $dx = $a[0] - $b[0]; $dy = $a[1] - $b[1]; $dz = $a[2] - $b[2]
    return [math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
}

try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    Write-Host "[1/3] dedicated server, supermarket world, search room..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "authoredprop.server.out.log"
    $server = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search") `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "authoredprop.server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Stop-Proc $server
        Write-Fail "the server never reported listening on udp/$Port; see authoredprop.server.out.log"
    }

    Write-Host "[2/3] bot A joins now; bot B joins $LateJoinDelaySec s later (the late joiner)..." -ForegroundColor Cyan
    $botA = Start-AuthoredBot "authoredpropA" "AuthoredPropA" $BotADurationSec
    $procs += $botA
    Start-Sleep -Seconds $LateJoinDelaySec
    $botB = Start-AuthoredBot "authoredpropB" "AuthoredPropB" $BotBDurationSec
    $procs += $botB

    foreach ($b in @(@{ N = "AuthoredPropA"; P = $botA; D = $BotADurationSec },
                     @{ N = "AuthoredPropB"; P = $botB; D = $BotBDurationSec })) {
        if (-not (Wait-ForExit $b.P ($b.D + 60))) {
            Stop-Proc $b.P
            Add-Failure "$($b.N) did not exit within its own duration + 60 s"
        } elseif ($b.P.ExitCode -ne 0) {
            Add-Failure "$($b.N) exited with code $($b.P.ExitCode); see tests/logs/"
        }
    }
    Stop-Proc $server
    Start-Sleep -Milliseconds 500
} finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host "[3/3] verifying the adoption block on all three peers..." -ForegroundColor Cyan

# --- 1 + 2: the adoption line, on the server and on both clients ------------------------------
$peers = @(
    @{ Who = "server"; Path = (Join-Path $script:LogDir "authoredprop.server.out.log") }
    @{ Who = "bot A (present from the start)"; Path = (Join-Path $script:LogDir "authoredpropA.out.log") }
    @{ Who = "bot B (late joiner)"; Path = (Join-Path $script:LogDir "authoredpropB.out.log") }
)
$adopts = @{}
foreach ($p in $peers) {
    $a = Get-AdoptLine $p.Path $p.Who
    if ($null -eq $a) { continue }
    $adopts[$p.Who] = $a
    Write-Host "        $($p.Who): $($a.Line)" -ForegroundColor DarkGray
    if ($a.Count -ne $AuthoredCount) {
        Add-Failure ("$($p.Who) adopted $($a.Count) prop(s); the world authors $AuthoredCount. " +
            "Either a prop was added/removed without updating this suite, or a peer built a different world.")
    }
    if ($a.First -ne $AuthoredFirst -or $a.Last -ne $AuthoredLast) {
        Add-Failure ("$($p.Who) adopted ids $($a.First)..$($a.Last); expected $AuthoredFirst..$AuthoredLast.")
    }
}
if ($adopts.Count -eq 3) {
    $ranges = @($adopts.Values | ForEach-Object { "$($_.First)..$($_.Last)/$($_.Count)" } | Select-Object -Unique)
    if ($ranges.Count -ne 1) {
        Add-Failure ("the three peers disagree about the adoption block: $($ranges -join ' vs '). " +
            "Adoption is local -- two peers with different blocks mean the same id names different objects.")
    } else {
        Write-Host "        all three peers agree on the block: $($ranges[0])" -ForegroundColor DarkGray
    }
    # ADOPT TIME IS REPORTED, NOT GATED. See the .DESCRIPTION.
    Write-Host ("        adopt time: server {0:F2} ms, bot A {1:F2} ms, bot B {2:F2} ms (reported, not gated)" -f
        $adopts["server"].Ms, $adopts["bot A (present from the start)"].Ms,
        $adopts["bot B (late joiner)"].Ms) -ForegroundColor DarkGray
}

# --- 3: complete in each client's FIRST sample ------------------------------------------------
$samplesA = Get-Samples (Join-Path $script:LogDir "authoredpropA.jsonl") "bot A"
$samplesB = Get-Samples (Join-Path $script:LogDir "authoredpropB.jsonl") "bot B"

foreach ($set in @(@{ N = "bot A"; S = $samplesA }, @{ N = "bot B (late joiner)"; S = $samplesB })) {
    if ($set.S.Count -eq 0) { continue }
    $first = Get-AuthoredMap $set.S[0]
    $missing = @()
    for ($id = $AuthoredFirst; $id -le $AuthoredLast; $id++) {
        if (-not $first.ContainsKey($id)) { $missing += $id }
    }
    if ($missing.Count -gt 0) {
        Add-Failure ("$($set.N): $($missing.Count) authored id(s) absent from its FIRST sample " +
            "(e.g. $($missing[0..([math]::Min(4, $missing.Count - 1))] -join ', ')). An authored prop is " +
            "adopted locally, never spawned and never streamed -- a peer that has to wait for it is a bug.")
    } else {
        Write-Host "        $($set.N): all $AuthoredCount authored ids present in its first sample" -ForegroundColor DarkGray
    }
}

# --- 4: the kind of every id, per block, on both peers ----------------------------------------
foreach ($set in @(@{ N = "bot A"; S = $samplesA }, @{ N = "bot B (late joiner)"; S = $samplesB })) {
    if ($set.S.Count -eq 0) { continue }
    $map = Get-AuthoredMap $set.S[-1]
    foreach ($b in $Blocks) {
        $wrong = @()
        for ($id = $b.First; $id -le $b.Last; $id++) {
            if (-not $map.ContainsKey($id)) { $wrong += "$id absent"; continue }
            $k = [int]$map[$id].kind
            if ($k -ne $b.Kind) { $wrong += "$id is kind $k" }
        }
        if ($wrong.Count -gt 0) {
            Add-Failure ("$($set.N): the '$($b.Name)' block $($b.First)..$($b.Last) should be all " +
                "PropKind $($b.Kind); $($wrong.Count) wrong (e.g. $($wrong[0..([math]::Min(3, $wrong.Count - 1))] -join '; ')). " +
                "An id block that has moved means something under the search room was renamed, added or removed.")
        }
    }
}
if ($script:Failures.Count -eq 0) {
    Write-Host "        the five id blocks carry the kinds SearchRoom.tscn authors, on both peers" -ForegroundColor DarkGray
}

# --- 5: landmark poses, and cross-peer agreement on all 144 -----------------------------------
if ($samplesA.Count -gt 0) {
    $lastA = Get-AuthoredMap $samplesA[-1]
    foreach ($lm in $Landmarks) {
        if (-not $lastA.ContainsKey($lm.Id)) {
            Add-Failure "landmark id $($lm.Id) ($($lm.Name)) is not in bot A's last sample at all"
            continue
        }
        $p = $lastA[$lm.Id]
        $d = Dist3 @([double]$p.x, [double]$p.y, [double]$p.z) $lm.At
        if ($d -gt $LandmarkToleranceM) {
            Add-Failure (("id {0} should be {1} at ({2:F2}, {3:F2}, {4:F2}); it is at " +
                "({5:F2}, {6:F2}, {7:F2}), {8:F3} m away (bar {9:F2} m). The id block has shifted -- " +
                "something under the search room was renamed, added or removed.") -f
                $lm.Id, $lm.Name, $lm.At[0], $lm.At[1], $lm.At[2],
                [double]$p.x, [double]$p.y, [double]$p.z, $d, $LandmarkToleranceM)
        } else {
            Write-Host ("        id {0,-5} {1,-26} at ({2,6:F2},{3,5:F2},{4,6:F2}), {5:F3} m from authored" -f
                $lm.Id, $lm.Name, [double]$p.x, [double]$p.y, [double]$p.z, $d) -ForegroundColor DarkGray
        }
    }
}

if ($samplesA.Count -gt 0 -and $samplesB.Count -gt 0) {
    $lastA = Get-AuthoredMap $samplesA[-1]
    $lastB = Get-AuthoredMap $samplesB[-1]
    $worst = -1.0; $worstId = -1; $disagreements = 0; $holderMismatch = 0
    for ($id = $AuthoredFirst; $id -le $AuthoredLast; $id++) {
        if (-not ($lastA.ContainsKey($id) -and $lastB.ContainsKey($id))) { continue }
        $pa = $lastA[$id]; $pb = $lastB[$id]
        $d = Dist3 @([double]$pa.x, [double]$pa.y, [double]$pa.z) @([double]$pb.x, [double]$pb.y, [double]$pb.z)
        # -1 as the seed rather than 0, so an all-exact run still names an id: every authored
        # prop is at its authored transform on BOTH peers while nothing has moved, and "worst
        # 0.0000 m (id -1)" reads like the loop never ran.
        if ($d -gt $worst) { $worst = $d; $worstId = $id }
        if ($d -gt $PeerAgreementM) { $disagreements++ }
        if ([int]$pa.holder -ne [int]$pb.holder) { $holderMismatch++ }
    }
    if ($disagreements -gt 0) {
        Add-Failure (("{0} of {1} authored props are in different places on the two peers " +
            "(worst: id {2}, {3:F3} m, bar {4:F2} m). The same id must name the same object in the " +
            "same place on every peer or the seeker and the hider are not in one room.") -f
            $disagreements, $AuthoredCount, $worstId, $worst, $PeerAgreementM)
    } else {
        Write-Host ("        A vs B: all $AuthoredCount authored props agree, worst {0:F4} m (id {1}, bar {2:F2} m)" -f
            $worst, $worstId, $PeerAgreementM) -ForegroundColor DarkGray
    }
    if ($holderMismatch -gt 0) {
        Add-Failure "$holderMismatch authored prop(s) have different holders on A and B"
    }
}

Write-Host ""
if ($script:Failures.Count -gt 0) {
    Write-Host "AUTHORED-PROP TEST FAILED ($($script:Failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "AUTHORED-PROP TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

# Built before it is printed: `-f` binds tighter than `+`, so a format string assembled with
# `+` inside the same parentheses formats only its LAST fragment and prints the rest of the
# placeholders literally. Measured on this suite's first green run.
$passLine = "PASS: $AuthoredCount authored props adopted as ids $AuthoredFirst..$AuthoredLast on the " +
    "server, on a client that was there from the start and on one that joined $LateJoinDelaySec s late; " +
    "the five id blocks carry the kinds the scene authors; the first id of every block is at its " +
    "authored pose; and all $AuthoredCount agree across peers."
Write-Host $passLine -ForegroundColor Green
Write-Host ""
Write-Host "AUTHORED-PROP TEST OVERALL: PASS" -ForegroundColor Green
exit 0
