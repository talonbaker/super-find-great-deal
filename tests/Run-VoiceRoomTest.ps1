<#
.SYNOPSIS
    VOICE-1: the intercom. Two bots, one round, one real ENet relay — same room is proximity,
    different rooms is the PA, and the server's distance cull does not eat the cross-room voice.

.DESCRIPTION
    This is the half `tests/unit/VoiceRoutingTests.cs` cannot reach. The route TABLE is a pure
    function and is tested there; what needs a live session is the thing that is only true if the
    client and the SERVER agree — `VoiceProximityGate.DefaultForWorld` is on for the supermarket
    (BASE-1 spaced the rooms 40 m apart against a 24 m audibility cutoff precisely so the gate
    would be load-bearing), so a relay that does not know who is on the PA silently mutes the
    intercom and nothing in any pure test can see it.

    ONE SERVER, ONE ROUND, TWO WINDOWS, AND NO WALL-CLOCK GUESSES. `--round-script` walks the
    round from Holding into Seeking; every sample the bots write carries `roundPhase`, so the two
    windows this suite reads are selected by the PHASE THE SERVER WAS IN when the sample was
    taken, never by a typed second:

      SAME ROOM  (roundPhase = Holding)  both bots stand in the holding room.
                 -> each bot's routing verdict must say `proximity` for the other,
                    and both rooms must read `holding`.
      CROSS ROOM (roundPhase = Seeking)  the hider is in the task room, the seeker in the
                 search room, ~42 m apart — past the gate's 40 m exit radius.
                 -> each bot's routing verdict must say `pa` for the other, the two rooms must
                    DIFFER, and the listener must STILL BE RECEIVING at the end of the run.

    WHY BOTH WINDOWS IN ONE RUN: each is the other's positive control. An intercom that "worked"
    by exempting everything would put a reverb on two people standing face to face and fail the
    same-room window; a relay that had simply broken would fail the cross-room one. Neither
    result means anything without the other, and the pair also catches the fail-open path — the
    rooms are asserted alongside the routes, because two UNKNOWN rooms also resolve to `pa`.

    THREE MORE THINGS ARE ASSERTED, each closing a way this could pass for the wrong reason:

      * The server's own counters (`--net-stats`): `paExempt` must be > 0 (the exemption branch
        actually ran) and `gated` must be 0 (nothing was culled). Without the first, the
        cross-room window could pass because the two bots happened to be close enough.
      * The measured separation between the two bodies in the cross-room window must exceed the
        gate's ENTER radius, off the bots' own logged positions — not off the room names.
      * The server's own hook line must read `paPairResolver=wired`. `paResolver=wired` alone is
        the pre-VOICE-1 diagnostic and is NOT sufficient for a room-keyed intercom: a per-talker
        answer applied to every listener is exactly the bug one level in.

    Audio CHARACTER is not provable here and is not attempted — that a PA voice sounds filtered
    and a proximity voice does not is the two-client human check in the handoff. What is proved
    is which route each voice took and that every packet arrived.

    Exit 0 = PASS. Headless throughout. One Godot at a time: takes the machine-wide suite mutex.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param(
    # Unique across tests/. 7893-7898 are the carry/round/first-person ladder (see INT-0's entry
    # in .claude/rules/test-suite.md — three lanes off one base picked 7896 and the marathon then
    # ran them back to back on one socket), and 7901 is claimed by a sibling lane in this wave.
    # 7899 was free on a grep of every port in tests/ at the time this was written.
    [int]$Port = 7899,
    [int]$MutexTimeoutMinutes = 30,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# The round's schedule, in seconds from the FIRST PLAYER ARRIVING (the driver's script clock
# starts there, not at server boot — see ScriptedRoundFactSource). Only two beats are needed:
# Start puts the hider in the search room, Confirm puts the two of them in the task and search
# rooms, which is the widest separation the round produces short of the vestibule.
$RoundScript = "start@8,confirm@14"

# A CEILING on the run, not an assumption about when anything lands: every window below is
# selected by the phase in the samples themselves.
$BotDurationSec = 34

# The window the "still receiving" assertion reads, at the very end of the run. Long enough that
# a 50 packet/second stream deposits hundreds in it.
$SettleWindowSec = 6

# Phase ordinals (HideSeekPhase). Pinned here rather than matched by name because the wire
# carries the ordinal and the bot samples it as an int.
$PhaseHolding = 0
$PhaseSeeking = 2

Write-Host "=== VOICE-1: same room is proximity, cross-room is the intercom ===" -ForegroundColor White

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
$script:Failures = @()
function Add-Failure([string]$m) { $script:Failures += $m }

try {
    if (-not $SkipBuild) {
        Reset-LogDir
        Invoke-BuildAndImport
    }
    if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

    # --- the server ---------------------------------------------------------------------------
    Write-Host "[1/5] dedicated server on udp/$Port, world supermarket, gate at its per-world default..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "voiceroom-server.out.log"
    $statsLog = Join-Path $script:LogDir "voiceroom.netstats.jsonl"
    # Deliberately NO --voice-gate: the point is that the gate is ON here because
    # VoiceProximityGate.DefaultForWorld says so for this world, and the intercom survives it.
    # Forcing it on by flag would hide a regression that turned the per-world default off.
    $server = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket",
        "--round-script", $RoundScript, "--net-stats", $statsLog, "--log-dir", $script:LogDir) `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "voiceroom-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server

    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "the server never reported listening within 60s; see voiceroom-server.out.log"
    }
    Write-Host "        server up (pid $($server.Id))"

    # --- two clients, one of them talking -------------------------------------------------------
    Write-Host "[2/5] two bots for ${BotDurationSec}s, VoiceTalker sending a tone throughout..." -ForegroundColor Cyan
    $bots = @()
    foreach ($spec in @(
        @{ Name = "VoiceTalker"; Extra = @("--voice-send") },
        @{ Name = "VoiceEar";    Extra = @() })) {
        $jsonLog = Join-Path $script:LogDir "voiceroom-$($spec.Name).jsonl"
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList (@(
            "--headless", "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $spec.Name,
            "--log", $jsonLog, "--duration", $BotDurationSec, "--world", "supermarket") + $spec.Extra) `
            -RedirectStandardOutput (Join-Path $script:LogDir "voiceroom-$($spec.Name).out.log") `
            -RedirectStandardError (Join-Path $script:LogDir "voiceroom-$($spec.Name).err.log") `
            -PassThru -NoNewWindow
        $null = $p.Handle
        $procs += $p
        $bots += @{ Name = $spec.Name; Proc = $p; Json = $jsonLog }
        # The same stagger Run-RoundLoopSmoke uses, for the same reason: join order decides who
        # hides, and two clients racing to connect makes that a coin flip. It does not matter to
        # THIS suite who hides — both roles are cross-room in Seeking — but a deterministic
        # ordering makes a failure report readable.
        Start-Sleep -Milliseconds 1200
    }

    Write-Host "[3/5] waiting for the round to reach Seeking and the run to finish..." -ForegroundColor Cyan
    foreach ($b in $bots) {
        if (-not (Wait-ForExit $b.Proc ($BotDurationSec + 60))) {
            Stop-Proc $b.Proc
            Add-Failure "$($b.Name) did not exit within its own duration + 60 s"
        }
    }
    Stop-Proc $server
    Start-Sleep -Milliseconds 500

    # ==========================================================================================
    # Parsing
    # ==========================================================================================
    Write-Host "[4/5] reading both peers' routing verdicts..." -ForegroundColor Cyan

    function Read-Samples([string]$Path) {
        if (-not (Test-Path $Path)) { return @() }
        $out = @()
        foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try { $out += ,(ConvertFrom-Json $line) } catch { }
        }
        return $out
    }
    function Get-PeerRow($Sample, [long]$PeerId) {
        foreach ($p in @($Sample.peers)) { if ([long]$p.id -eq $PeerId) { return $p } }
        return $null
    }
    function Get-VerdictRow($Sample, [long]$PeerId) {
        if ($null -eq $Sample.vroute) { return $null }
        foreach ($p in @($Sample.vroute.peers)) { if ([long]$p.id -eq $PeerId) { return $p } }
        return $null
    }
    function Get-VoiceCount($Sample, [long]$SenderId) {
        if ($null -eq $Sample.voice) { return 0 }
        $prop = $Sample.voice.PSObject.Properties | Where-Object { $_.Name -eq [string]$SenderId }
        if ($null -eq $prop) { return 0 }
        return [long]$prop.Value
    }
    function Dist3($a, $b) {
        $dx = [double]$a.x - [double]$b.x; $dy = [double]$a.y - [double]$b.y; $dz = [double]$a.z - [double]$b.z
        return [math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
    }

    foreach ($b in $bots) { $b.Samples = Read-Samples $b.Json }
    foreach ($b in $bots) {
        if (@($b.Samples).Count -lt 20) {
            Write-Fail "$($b.Name) logged only $(@($b.Samples).Count) sample(s) -- it never really ran"
        }
    }
    $talker = $bots[0]
    $ear = $bots[1]
    $talkerId = [long]($talker.Samples[-1].self)
    $earId = [long]($ear.Samples[-1].self)
    if ($talkerId -le 0 -or $earId -le 0 -or $talkerId -eq $earId) {
        Write-Fail "could not resolve two distinct peer ids (talker=$talkerId ear=$earId)"
    }

    # --- the hooks the server actually wired ---------------------------------------------------
    # Read off the server's OWN line rather than inferred: `paResolver=wired` on its own is the
    # pre-VOICE-1 diagnostic and does not say the relay is listener-relative.
    $hookLine = (Select-String -Path $serverOut -Pattern "\[voice\] gate on," | Select-Object -First 1)
    if ($null -eq $hookLine) {
        Add-Failure "the server never logged its voice-gate hook line -- either the gate is OFF on this world (VoiceProximityGate.DefaultForWorld) or no voice was ever relayed"
    } else {
        $hooks = $hookLine.Line.Trim()
        Write-Host "        server: $hooks"
        if ($hooks -notmatch "paPairResolver=wired") {
            Add-Failure "the server reports paPairResolver NOT-WIRED -- the relay is answering 'is T on the PA' for the whole match at once, which is exactly the hazard VoiceProximityGate's header describes. Line: $hooks"
        }
        if ($hooks -notmatch "paResolver=wired") {
            Add-Failure "the server reports paResolver NOT-WIRED. Line: $hooks"
        }
        if ($hooks -notmatch "roomResolver=wired") {
            Add-Failure "the server reports roomResolver NOT-WIRED -- nothing is feeding the round's room map into voice. Line: $hooks"
        }
    }

    # --- the two windows -------------------------------------------------------------------------
    function Select-Phase($Samples, [int]$Phase) {
        return @($Samples | Where-Object { $_.roundSynced -eq $true -and [int]$_.roundPhase -eq $Phase })
    }

    function Test-Window($Bot, [long]$OtherId, [int]$Phase, [string]$ExpectRoute, [string]$Label) {
        $window = Select-Phase $Bot.Samples $Phase
        if ($window.Count -lt 5) {
            Add-Failure "${Label}: $($Bot.Name) has only $($window.Count) synced sample(s) in that phase -- the round never got there, so nothing was measured"
            return $null
        }
        $routes = @{}
        $selfRooms = @{}
        $otherRooms = @{}
        $missing = 0
        foreach ($s in $window) {
            $row = Get-VerdictRow $s $OtherId
            if ($null -eq $row) { $missing++; continue }
            $routes[[string]$row.route] = $true
            $otherRooms[[string]$row.room] = $true
            $selfRooms[[string]$s.vroute.room] = $true
        }
        if ($missing -eq $window.Count) {
            Add-Failure "${Label}: $($Bot.Name)'s routing verdict never carried the other peer at all"
            return $null
        }
        $routeList = ($routes.Keys | Sort-Object) -join "/"
        $selfList = ($selfRooms.Keys | Sort-Object) -join "/"
        $otherList = ($otherRooms.Keys | Sort-Object) -join "/"
        if ($routeList -ne $ExpectRoute) {
            Add-Failure "${Label}: $($Bot.Name) routed the other peer as '$routeList', expected '$ExpectRoute' (its room: $selfList; the other's: $otherList)"
        }
        return @{ Route = $routeList; SelfRooms = $selfList; OtherRooms = $otherList; Count = $window.Count }
    }

    Write-Host "[5/5] the route table, as two live peers resolved it..." -ForegroundColor Cyan

    # SAME ROOM. Both in the holding room; a reverb on a face-to-face conversation is the failure.
    $sameTalker = Test-Window $talker $earId $PhaseHolding "proximity" "same room"
    $sameEar = Test-Window $ear $talkerId $PhaseHolding "proximity" "same room"
    foreach ($w in @($sameTalker, $sameEar)) {
        if ($null -eq $w) { continue }
        if ($w.SelfRooms -ne "holding" -or $w.OtherRooms -ne "holding") {
            Add-Failure "same room: rooms read self='$($w.SelfRooms)' other='$($w.OtherRooms)', expected both 'holding' -- 'proximity' is only meaningful if the two rooms are the same KNOWN room"
        }
    }

    # CROSS ROOM. Task vs search, and the routes must be pa on BOTH sides.
    $crossTalker = Test-Window $talker $earId $PhaseSeeking "pa" "cross room"
    $crossEar = Test-Window $ear $talkerId $PhaseSeeking "pa" "cross room"
    foreach ($w in @($crossTalker, $crossEar)) {
        if ($null -eq $w) { continue }
        if ([string]::IsNullOrEmpty($w.SelfRooms) -or [string]::IsNullOrEmpty($w.OtherRooms)) {
            Add-Failure "cross room: a room read EMPTY (self='$($w.SelfRooms)' other='$($w.OtherRooms)') -- 'pa' here is the fail-open path, not the intercom working"
        }
        elseif ($w.SelfRooms -eq $w.OtherRooms) {
            Add-Failure "cross room: both peers read room '$($w.SelfRooms)' -- the round never separated them, so 'pa' cannot have come from the room rule"
        }
    }

    # THE BODIES REALLY ARE APART, measured off the logs rather than off the room names.
    $seekSamples = Select-Phase $ear.Samples $PhaseSeeking
    $sep = -1.0
    foreach ($s in $seekSamples) {
        $me = Get-PeerRow $s $earId
        $them = Get-PeerRow $s $talkerId
        if ($null -eq $me -or $null -eq $them) { continue }
        $sep = Dist3 $me $them
    }
    if ($sep -lt 0) {
        Add-Failure "cross room: no Seeking sample ever contained both bodies, so the separation could not be measured"
    } elseif ($sep -le 30.0) {
        Add-Failure "cross room: the two bodies settled only $([math]::Round($sep,1)) m apart -- inside the gate's 30 m ENTER radius, so this window would relay with or without the PA exemption and proves nothing"
    }

    # STILL RECEIVING AT THE END. The whole point: with the gate on and 40 m between them, the
    # relay must keep delivering every frame.
    $last = $ear.Samples[-1]
    $endT = [double]$last.t
    $baseline = $null
    foreach ($s in $ear.Samples) { if ([double]$s.t -le ($endT - $SettleWindowSec * 1000)) { $baseline = $s } }
    $rxTotal = Get-VoiceCount $last $talkerId
    $rxDelta = if ($null -eq $baseline) { -1 } else { $rxTotal - (Get-VoiceCount $baseline $talkerId) }
    if ($rxTotal -lt 300) {
        Add-Failure "the listener received only $rxTotal voice packets in total over ${BotDurationSec}s of a ~50/s stream"
    }
    if ($rxDelta -lt 100) {
        Add-Failure "the listener received only $rxDelta packets in the final ${SettleWindowSec}s at $([math]::Round($sep,1)) m -- the gate is culling the intercom"
    }

    # THE EXEMPTION BRANCH ACTUALLY RAN, off the server's own counters.
    $stats = @()
    if (Test-Path $statsLog) {
        $stats = @(Get-Content $statsLog | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_ | ConvertFrom-Json })
    }
    $paExempt = 0; $gated = 0; $relayed = 0
    if ($stats.Count -gt 0) {
        $paExempt = ($stats | Measure-Object -Property paExempt -Sum).Sum
        $gated = ($stats | Measure-Object -Property gated -Sum).Sum
        $relayed = ($stats | Measure-Object -Property relayed -Sum).Sum
    } else {
        Add-Failure "the server wrote no --net-stats samples, so the relay counters could not be read"
    }
    if ($paExempt -le 0) {
        Add-Failure "the server's paExempt counter read 0 -- the PA exemption branch never ran, so the cross-room window passed for some other reason"
    }
    if ($gated -ne 0) {
        Add-Failure "the server gated $gated packet(s) -- a cross-room voice must never be culled"
    }

    Write-Host ""
    Write-Host ("        same room:  talker->ear {0} (rooms {1}), ear->talker {2} (rooms {3})" -f `
        $(if ($sameTalker) { $sameTalker.Route } else { "-" }), $(if ($sameTalker) { $sameTalker.SelfRooms } else { "-" }),
        $(if ($sameEar) { $sameEar.Route } else { "-" }), $(if ($sameEar) { $sameEar.SelfRooms } else { "-" }))
    Write-Host ("        cross room: talker in {0} -> ear {1}, ear in {2} -> talker {3}, bodies {4} m apart" -f `
        $(if ($crossTalker) { $crossTalker.SelfRooms } else { "-" }), $(if ($crossTalker) { $crossTalker.Route } else { "-" }),
        $(if ($crossEar) { $crossEar.SelfRooms } else { "-" }), $(if ($crossEar) { $crossEar.Route } else { "-" }),
        [math]::Round($sep, 1))
    Write-Host ("        listener received {0} packets total, {1} in the final {2}s" -f $rxTotal, $rxDelta, $SettleWindowSec)
    Write-Host ("        server relay counters: relayed {0}, PA-exempt {1}, gated {2}" -f $relayed, $paExempt, $gated)
    if ($crossTalker) {
        Write-Host ("        routing-verdict (cross-room, talker's view): {{""room"":""{0}"",""peer"":{1},""peerRoom"":""{2}"",""route"":""{3}""}}" -f `
            $crossTalker.SelfRooms, $earId, $crossTalker.OtherRooms, $crossTalker.Route)
    }
    if ($crossEar) {
        Write-Host ("        routing-verdict (cross-room, ear's view):    {{""room"":""{0}"",""peer"":{1},""peerRoom"":""{2}"",""route"":""{3}""}}" -f `
            $crossEar.SelfRooms, $talkerId, $crossEar.OtherRooms, $crossEar.Route)
    }
    if ($sameTalker) {
        Write-Host ("        routing-verdict (same-room,  talker's view): {{""room"":""{0}"",""peer"":{1},""peerRoom"":""{2}"",""route"":""{3}""}}" -f `
            $sameTalker.SelfRooms, $earId, $sameTalker.OtherRooms, $sameTalker.Route)
    }
    if ($sameEar) {
        Write-Host ("        routing-verdict (same-room,  ear's view):    {{""room"":""{0}"",""peer"":{1},""peerRoom"":""{2}"",""route"":""{3}""}}" -f `
            $sameEar.SelfRooms, $talkerId, $sameEar.OtherRooms, $sameEar.Route)
    }
} finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host ""
if ($script:Failures.Count -gt 0) {
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "VOICE-ROOM TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}
Write-Host "VOICE-ROOM TEST OVERALL: PASS" -ForegroundColor Green
exit 0
