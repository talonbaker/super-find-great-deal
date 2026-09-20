<#
.SYNOPSIS
    CARRY-1: the networked-carry proof. Two real clients and a real dedicated server, across the
    three carry cases no existing suite covers -- a contested grab, a prop re-grabbed after its
    holder disconnected, and a prop carried through a TV portal. Every detector in this file
    carries a MUTATION CONTROL that proves it can go red.

.DESCRIPTION
    Talon, 2026-08-29 (note 12): *"the ability for users to pick up objects and move them around
    in a shared network kind of space... there should be a kind of test nonetheless to make sure
    that this is completely working completely flawless... this is already created. You don't need
    to do this again."*

    So this suite BUILDS NOTHING. Carry, throw, E and Q all exist and are untouched. The only
    non-test change behind it is `--seed-test-props`, a server-side launch flag that puts an
    ordinary Crate in a world that has none (see LaunchOptions.SeedTestProps).

    WHAT WAS ALREADY COVERED, AND IS NOT REPEATED HERE:
      Run-CarryTest      grab converges on two peers; a LATER contested grab is rejected; drop
                         converges; a disconnecting holder releases; a late joiner converges.
      Run-ThrowTest      the thrown prop travels, streams, and settles to the SAME resting place
                         on both peers; an out-of-bounds prop recovers home.
      Run-RegrabTest     a prop grabbed a second time WHILE STILL LOOSE tracks its holder.
      Run-CarryDriftTest a long hold-and-walk does not accumulate drift.
    Those four are the answer to the packet's cases 1 and 3. This suite is the other three.

    PHASE 1 -- CONTENTION (world propsync, prop 1 = Crate at (2.5, 0.5, 2.5)).
    Two bots walk to the SAME crate and are given the SAME earliest-grab clock, launched
    back-to-back with no stagger. Exactly one may end up holding it; the loser must be TOLD
    (PropManager.DenyGrab -> "grab denied ... reason=Taken"), and the prop must never duplicate,
    vanish, or read as held by two peers on any peer's view.

    HONESTY ABOUT "THE SAME FRAME". Two OS processes cannot be made to press in one physics tick,
    and this script does not pretend otherwise -- it MEASURES the skew it actually achieved and
    prints it. That is not a weakened test: Godot dispatches reliable RPCs serially on the server's
    main thread, and PropManager's arbitration is a single `_registry.SetHolder` -- an atomic
    check-and-set in the same branch (MECHANICS-BIBLE 2). There is no interleaving window for a
    tighter race to find. What a tighter race would exercise, and what this DOES exercise, is the
    ordering rule PropManager.RequestGrab documents at length: the target is claimed BEFORE the
    winner's own item is displaced, so the loser never puts something down for a crate it never
    gets. The measured skew is reported so a future reader can judge the staging rather than
    trust it.

    PHASE 2 -- AUTHORITY THROUGH A DISCONNECT (world propsync, prop 2 = Ball at (-2.5,0.5,-2.5)).
    Bot C grabs the ball and disconnects while still holding it. Run-CarryTest already proves a
    witness sees the release. THE HALF NOBODY CHECKS is what the packet actually asks: the ball
    must not be left FROZEN AND UN-GRABBABLE, or parented to a dead peer. So bot D picks it up
    afterwards, and an independent witness W -- who never grabs anything -- has to see the whole
    sequence: held by C, released, held by D, and tracking D as D walks away.

    NOT ONE WALL-CLOCK GUESS IN THE STAGING, and that is a correction, not a flourish. The first
    version gave C `--duration 6` because ~5 m at ~3.6 m/s is under two seconds. Measured, twice,
    on a verified-idle machine: the SERVER moved C 2.4 m in 5.8 s (~0.42 m/s) while its client
    predicted the full walk, prediction error spiking to 1.71 m nine times. C's lifetime expired
    with the ball never touched, and D -- whose grab was "safely" scheduled to lose a race C never
    entered -- won it instead, then fired the throw that only existed to arm its chase and hurled
    the ball 12 m away. Two faults, one root: a step whose correctness depends on WHEN an earlier
    step landed. Now C exits on an observed event (--exit-when-holding), C presses on a cadence
    rather than once (--carry-grab-retry: a single press decides on a PREDICTED position), and D
    is not launched until the server itself reports C gone. The remaining numbers are ceilings,
    and a ceiling being reached is a real failure rather than a slow machine.

    PHASE 3 -- A CARRIED PROP SURVIVING ITS HOLDER'S SERVER TELEPORT (world supermarket, one
    seeded Crate in the holding room). It was the carry-through-a-TV-portal proof, removed at the
    fork with the level and the portal host it drove, and restored here by ROUND-1 (2026-09-19)
    because the PROPERTY never went anywhere: a server teleport moves the AVATAR
    (SandboxAvatar.ServerTeleportTo, reached through RoomTeleport) and says nothing at all about
    the thing in its hands -- the prop follows only because NetworkedProp.BindToHolder re-derives
    its transform from the holder's carry anchor every frame. The failures it guards are specific:
    the prop left standing in the room the player just left, or eaten by Carryable.KillPlaneY on
    the way.

    The trigger is now the round. Bot P connects first, so it is the first round's HIDER; it picks
    the crate up and is still holding it when --round-script presses Start, and the Holding ->
    Hiding transition teleports it 40 m into the search room. The witness W connects second, never
    grabs anything, and is the only view that can tell a real teleport from a local prediction.

    EVERY DETECTOR IS PROVEN TO FIRE, IN THE SAME RUN.
    "A test that has never once failed has not been shown to work" -- and this repo has paid for a
    detector that silently never fired. Rather than promise an out-of-band experiment, each
    analyser here is a pure function over parsed samples, and after it passes on the real logs it
    is re-run against a MUTATED COPY of those same logs with the specific defect injected: the
    holder rewritten to the losing peer, the held prop shoved 5 m out of the holder's hands, the
    carried crate left behind in the hub. If the analyser does not go red on the mutant, the suite
    FAILS -- because at that point its green on the real data means nothing. The mutations are
    applied to freshly re-parsed copies, so they cannot leak into the real assertions.

    Exit 0 = PASS. No human interaction. Headless throughout -- no GPU, no display.
#>
[CmdletBinding()]
param(
    # Distinct per phase and unique across tests/ (7893-7895 were unused; 7818 is Run-RegrabTest's
    # and is hardcoded AND shared -- see .claude/rules/test-suite.md, "a SECOND, non-flake cause").
    # Sequential phases could reuse one port, and deliberately do not: a server whose clients fail
    # to attach holds its socket, and reusing the port would cascade this suite's first failure
    # into its own later phases.
    [int]$ContentionPort = 7893,
    [int]$AuthorityPort = 7894,
    # 7895 was PHASE 3's, was left unclaimed at the fork for exactly this, and is claimed back
    # here: the phase is the same property (a carried prop surviving its holder's server teleport)
    # driven by the round's phase change instead of by walking into a screen.
    [int]$TeleportPort = 7895,
    [ValidateSet("all", "contention", "authority", "teleport")]
    [string]$Phase = "all",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# propsync's authoritative roster (PropManager.SpawnInitialProps).
$CrateId = 1   # Crate at (2.5, 0.5, 2.5)
$BallId = 2    # Ball  at (-2.5, 0.5, -2.5)

# The carry anchor sits ~0.88 m from the avatar's rendered body (CarryAnchorRest; see
# Run-CarryDriftTest's tolerance table, which measures the steady state at 0.88 m and caps the
# peak at 1.45 m). 1.6 m is Run-RegrabTest's radius and is reused unchanged so a number measured
# by this suite is directly comparable with the one that suite prints.
$HoldRadius = 1.6

# SandboxAvatar.PickupRadius. The CLIENT's reach, and it is the discriminator between "the prop
# refused to be picked up" and "nobody ever asked": inside this radius the client sends a request
# and the server answers (accept, or a logged refusal); outside it, nothing is sent at all and the
# server log is silent. PropManager.GrabRange (this + 0.75 m of latency tolerance) is the SERVER's
# check and is deliberately the wider of the two.
$PickupRadius = 1.5

$script:Failures = New-Object System.Collections.Generic.List[string]
$script:Measured = New-Object System.Collections.Generic.List[string]

function Add-Failure([string]$Message) { $script:Failures.Add($Message) | Out-Null }

# Drains an analyser's return value. A PowerShell function that returns an empty array returns
# NOTHING, and `foreach ($x in $null)` is a trap that differs between hosts -- so every analyser
# result goes through here, where an empty result is unambiguously zero failures.
function Add-Failures($Result) {
    foreach ($m in @($Result)) {
        if ($null -ne $m -and "$m".Length -gt 0) { Add-Failure "$m" }
    }
}

function Get-FailureCount($Result) {
    $n = 0
    foreach ($m in @($Result)) { if ($null -ne $m -and "$m".Length -gt 0) { $n++ } }
    return $n
}
function Add-Measured([string]$Message) {
    $script:Measured.Add($Message) | Out-Null
    Write-Host ("        " + $Message) -ForegroundColor DarkGray
}

# ==================================================================================================
# Parsing
# ==================================================================================================

# Parses a bot's JSONL log and normalises each sample to seconds since THAT bot's own first sample.
# Separate OS processes do not share an engine clock, so every cross-bot comparison below is made
# either in each bot's own local-elapsed terms or on the `wall` field (Unix ms, which IS comparable
# across processes on one machine -- BotHarness writes it for exactly that reason).
#
# Re-parses from disk on every call, and that is load-bearing: the mutation controls mutate the
# object graph they are handed, so they must never be handed the same graph the real assertions
# ran on.
function Get-Samples([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $lines = @(Get-Content $Path | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0) { Write-Fail "log has no samples: $Path" }
    $parsed = @($lines | ForEach-Object { $_ | ConvertFrom-Json })
    $t0 = [double]$parsed[0].t
    foreach ($s in $parsed) {
        $s | Add-Member -NotePropertyName Sec -NotePropertyValue (([double]$s.t - $t0) / 1000.0) -Force
    }
    return $parsed
}

function Get-Prop($Sample, [int]$PropId) {
    $p = @($Sample.props) | Where-Object { [int]$_.id -eq $PropId }
    if ($null -eq $p -or @($p).Count -eq 0) { return $null }
    return @($p)[0]
}

function Get-PeerRow($Sample, [long]$PeerId) {
    $p = @($Sample.peers) | Where-Object { [long]$_.id -eq $PeerId }
    if ($null -eq $p -or @($p).Count -eq 0) { return $null }
    return @($p)[0]
}

function Dist3($ax, $ay, $az, $bx, $by, $bz) {
    $dx = [double]$ax - [double]$bx
    $dy = [double]$ay - [double]$by
    $dz = [double]$az - [double]$bz
    return [math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
}

# The nearest prop to a point, read from the EARLIEST sample that has any props at all. Written
# for the removed phase 3, and kept because the reasoning outlives it: a suite that assumes "the
# only prop in this world is mine" breaks the moment a level gains authored props, and SHELF-1 is
# about to put a hundred of them in the search room. Identifying a fixture by where it was SEEDED
# survives any number of props appearing beside it.
#
# Not literally the first sample: the spawner's replication and the bot's first tick are two
# different events, and a bot that logs sample 0 before the dump lands would make this report
# "no prop at the seed point" for a fixture that is perfectly fine. It still reads an EARLY sample
# rather than a late one -- by the time the crate is in someone's hands it is no longer at the
# seed point, and matching it there is the whole identification.
function Find-PropNear($Samples, [double]$X, [double]$Y, [double]$Z) {
    $first = $null
    foreach ($s in $Samples) {
        if (@($s.props).Count -gt 0) { $first = $s; break }
    }
    if ($null -eq $first) { return $null }
    $best = $null
    $bestD = [double]::MaxValue
    foreach ($p in @($first.props)) {
        $d = Dist3 $p.x $p.y $p.z $X $Y $Z
        if ($d -lt $bestD) { $bestD = $d; $best = $p }
    }
    if ($null -eq $best) { return $null }
    return @{ Id = [int]$best.id; Dist = $bestD }
}

# The server's own refusal trace: "<iso> INFO [server] grab denied peer=N reason=R".
# Returns @{ Peer; Reason; WallMs } rows. This is the ONLY direct evidence that a contested grab
# was actually attempted and actually refused; without it, a run in which the losing bot never
# pressed at all would satisfy every "the loser never held it" assertion below.
function Get-GrabDenials([string]$ServerLogPath) {
    $rows = @()
    if (-not (Test-Path $ServerLogPath)) { return $rows }
    foreach ($m in @(Select-String -Path $ServerLogPath -Pattern 'grab denied peer=(\d+) reason=(\w+)')) {
        $peer = [int]$m.Matches[0].Groups[1].Value
        $reason = $m.Matches[0].Groups[2].Value
        $wall = -1.0
        if ($m.Line -match '^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3})Z') {
            $dt = [datetime]::ParseExact($matches[1], "yyyy-MM-ddTHH:mm:ss.fff",
                [System.Globalization.CultureInfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::AssumeUniversal -bor
                [System.Globalization.DateTimeStyles]::AdjustToUniversal)
            $wall = [double]([System.DateTimeOffset]::new($dt, [timespan]::Zero).ToUnixTimeMilliseconds())
        }
        $rows += ,@{ Peer = $peer; Reason = $reason; WallMs = $wall }
    }
    return $rows
}

# ==================================================================================================
# Analysers -- pure functions over parsed samples, each returning a list of failure strings.
#
# Pure on purpose. An analyser that reached out to $script:Failures, or to a file, could not be
# re-run against a mutated copy of its own input, and re-running it against a mutant is the only
# way this suite can show that it is capable of failing at all.
# ==================================================================================================

# The prop must never, on this peer's view, be held by $ForbiddenPeer.
function Test-NeverHeldBy($Samples, [int]$PropId, [long]$ForbiddenPeer, [string]$ViewName) {
    $f = @()
    foreach ($s in $Samples) {
        $p = Get-Prop $s $PropId
        if ($null -eq $p) { continue }
        if ([long]$p.holder -eq $ForbiddenPeer) {
            $f += "$ViewName @ $([math]::Round($s.Sec,2))s: prop $PropId holder=$($p.holder), which is the peer whose grab must have been refused"
            break
        }
    }
    return $f
}

# The set of holders this peer ever observed for the prop must be exactly {0, $Expected}.
function Test-HolderSet($Samples, [int]$PropId, [long]$Expected, [string]$ViewName) {
    $f = @()
    $seen = @{}
    foreach ($s in $Samples) {
        $p = Get-Prop $s $PropId
        if ($null -eq $p) { continue }
        $seen[[long]$p.holder] = $true
    }
    foreach ($h in $seen.Keys) {
        if ($h -ne 0 -and $h -ne $Expected) {
            $f += "${ViewName}: prop $PropId was observed held by peer $h; only 0 (loose) and $Expected are legal in this phase"
        }
    }
    if (-not $seen.ContainsKey([long]$Expected)) {
        $f += "${ViewName}: prop $PropId was NEVER observed held by peer $Expected -- this view did not converge on the hold at all"
    }
    return $f
}

# While $HolderId holds the prop (from $FromSec onward on this log's own clock), the prop must sit
# within $Radius of that holder's avatar ON THIS PEER'S VIEW. Returns failures plus the measured
# quantity, because the quantity is what discriminates a real defect from this machine's noise --
# the corpus rule for the whole carry family.
function Measure-HoldTracking($Samples, [int]$PropId, [long]$HolderId, [double]$FromSec,
                              [double]$Radius, [int]$MinSamples, [string]$ViewName) {
    $f = @()
    $checked = 0
    $worst = 0.0
    foreach ($s in $Samples) {
        if ([double]$s.Sec -lt $FromSec) { continue }
        $p = Get-Prop $s $PropId
        if ($null -eq $p -or [long]$p.holder -ne $HolderId) { continue }
        $peer = Get-PeerRow $s $HolderId
        if ($null -eq $peer) { continue }
        $d = Dist3 $p.x $p.y $p.z $peer.x $peer.y $peer.z
        $checked++
        if ($d -gt $worst) { $worst = $d }
        if ($d -gt $Radius) {
            $f += ("{0} @ {1:F2}s: held prop {2} sat {3:F2} m from its holder (> {4} m)" -f `
                $ViewName, [double]$s.Sec, $PropId, $d, $Radius)
        }
    }
    if ($checked -lt $MinSamples) {
        $f += "${ViewName}: only $checked held-by-$HolderId samples after ${FromSec}s (need $MinSamples) -- not enough signal; retune staging"
    }
    return @{ Failures = $f; Checked = $checked; Worst = $worst }
}

# The prop must be observed, on this peer's view, within $Radius of a point the SERVER named --
# it travelled with its holder rather than being left standing in the room they left.
#
# A point rather than a plane, and the point comes out of the server's own log, because the rooms
# are laid out along +X and a y-threshold (the shape this analyser had when a TV portal dropped
# people through a floor) says nothing about a 40 m sideways move.
function Test-PropReached($Samples, [int]$PropId, [double]$X, [double]$Y, [double]$Z,
                          [double]$Radius, [int]$MinSamples, [string]$ViewName) {
    $f = @()
    $near = 0
    $closest = [double]::MaxValue
    foreach ($s in $Samples) {
        $p = Get-Prop $s $PropId
        if ($null -eq $p) { continue }
        $d = Dist3 ([double]$p.x) ([double]$p.y) ([double]$p.z) $X $Y $Z
        if ($d -lt $closest) { $closest = $d }
        if ($d -le $Radius) { $near++ }
    }
    if ($near -lt $MinSamples) {
        $f += ("{0}: prop {1} came within {2} m of the destination its holder was teleported to in only {3} sample(s) (need {4}); closest approach {5:F2} m -- the carried prop was left behind" -f `
            $ViewName, $PropId, $Radius, $near, $MinSamples, $closest)
    }
    return $f
}

# The first second, ON THIS LOG'S OWN CLOCK, at which this log saw $PeerId within $Radius of a
# point. -1 if never. Per log, for the reason Get-FirstSecPeerBelow below states at length:
# two bot processes do not share a clock.
function Get-FirstSecPeerNear($Samples, [long]$PeerId, [double]$X, [double]$Y, [double]$Z, [double]$Radius) {
    foreach ($s in $Samples) {
        $r = Get-PeerRow $s $PeerId
        if ($null -eq $r) { continue }
        if ((Dist3 ([double]$r.x) ([double]$r.y) ([double]$r.z) $X $Y $Z) -le $Radius) { return [double]$s.Sec }
    }
    return -1.0
}

# The first second, ON THIS LOG'S OWN CLOCK, at which this log saw $PeerId below $Y. -1 if never.
#
# PER LOG, and that is the whole point. Two bot processes do not share a clock, so anchoring one
# log's window with another log's elapsed seconds is a category error -- it silently samples the
# wrong stretch of the wrong process. CARRY-1's first green run did exactly that and it was the
# phase-3 mutation control, not the assertions, that caught it: the control mutated only the
# witness samples after PortalP's 8.8 s and left the witness's own earlier descent samples intact,
# so the analyser still found plenty below the floor and stayed green on a mutant that was supposed
# to be broken. Run-RegrabTest reached the same conclusion the same way (see its Get-RegrabTime:
# "separate processes, separate clocks").
function Get-FirstSecPeerBelow($Samples, [long]$PeerId, [double]$Y) {
    foreach ($s in $Samples) {
        $r = Get-PeerRow $s $PeerId
        if ($null -ne $r -and [double]$r.y -lt $Y) { return [double]$s.Sec }
    }
    return -1.0
}

# Walks the holder timeline and reports the observed transitions, in order, as "0>5>0>7" style.
function Get-HolderTransitions($Samples, [int]$PropId) {
    $seq = @()
    $last = $null
    foreach ($s in $Samples) {
        $p = Get-Prop $s $PropId
        if ($null -eq $p) { continue }
        $h = [long]$p.holder
        if ($null -eq $last -or $h -ne $last) {
            $seq += ,@{ Holder = $h; Sec = [double]$s.Sec; Wall = [double]$s.wall }
            $last = $h
        }
    }
    return $seq
}

# ==================================================================================================
# The mutation controls.
#
# Each takes a FRESHLY re-parsed copy of a real log, injects one specific defect, re-runs the
# analyser that is supposed to catch it, and insists it goes red. A control that does not fire is
# itself a suite failure: at that point the analyser's green on the real data proves nothing.
# ==================================================================================================

function Assert-DetectorFires([string]$Name, $Failures) {
    if ((Get-FailureCount $Failures) -eq 0) {
        Add-Failure "MUTATION CONTROL FAILED -- '$Name': the injected defect was NOT detected, so this analyser's PASS on the real logs is worthless. Fix the analyser before believing any green from this suite."
        return $false
    }
    Write-Host ("        control ok: '{0}' fires ({1} failure(s) on the mutant)" -f $Name, (Get-FailureCount $Failures)) -ForegroundColor DarkGray
    return $true
}

# ==================================================================================================
Write-Host "=== carry: the networked-carry proof (CARRY-1, Talon note 12) ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

# --------------------------------------------------------------------------------------------------
# PHASE 1 -- CONTENTION
# --------------------------------------------------------------------------------------------------
if ($Phase -eq "all" -or $Phase -eq "contention") {
    Write-Host "[1/3] contention: two peers reach for the same crate..." -ForegroundColor Cyan
    $dur = 15
    $aLog = Join-Path $script:LogDir "carrynet.contendA.jsonl"
    $bLog = Join-Path $script:LogDir "carrynet.contendB.jsonl"
    $serverOut = Join-Path $script:LogDir "carrynet.contend.server.out.log"
    $procs = @()
    try {
        $server = Start-Godot @("--server", "--port", $ContentionPort, "--world", "propsync") "carrynet.contend.server"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
            Write-Fail "contention server never reported listening; see $serverOut"
        }
        # NO stagger between these two, unlike every other carry suite. The point is to press as
        # close together as two processes allow; the achieved skew is measured below rather than
        # asserted, because it is a property of the machine and not of the code under test.
        #
        # --carry-grab-retry is on for BOTH, and it strengthens the contention rather than diluting
        # it. The winner latches the moment it is confirmed holding and stops pressing; the loser
        # keeps pressing and keeps being refused, so the refusal this phase is built on becomes a
        # certainty instead of a single press that a 1.7 m prediction error could waste.
        $botA = Start-Godot @("--bot", "--address", "127.0.0.1:$ContentionPort", "--name", "ContendA",
            "--log", $aLog, "--duration", $dur, "--world", "propsync",
            "--carry-script", "2.5,0.5,2.5,5.0,-1",
            "--carry-grab-retry", "0.5") "carrynet.contendA"
        $procs += $botA
        $botB = Start-Godot @("--bot", "--address", "127.0.0.1:$ContentionPort", "--name", "ContendB",
            "--log", $bLog, "--duration", $dur, "--world", "propsync",
            "--carry-script", "2.5,0.5,2.5,5.0,-1",
            "--carry-grab-retry", "0.5") "carrynet.contendB"
        $procs += $botB
        foreach ($p in @($botA, $botB)) {
            if (-not (Wait-ForExit $p ($dur + 90))) { Write-Fail "a contention bot never exited" }
        }
        foreach ($p in @($botA, $botB)) {
            if ($p.ExitCode -ne 0) { Write-Fail "a contention bot exited $($p.ExitCode); see carrynet.contend*.out.log" }
        }
    } finally {
        Stop-Procs $procs
    }

    $sa = Get-Samples $aLog
    $sb = Get-Samples $bLog
    $peerA = [long]$sa[0].self
    $peerB = [long]$sb[0].self
    Write-Host "        peers: ContendA=$peerA ContendB=$peerB" -ForegroundColor DarkGray

    $aHeld = @($sa | Where-Object { [int]$_.heldPropId -eq $CrateId }).Count
    $bHeld = @($sb | Where-Object { [int]$_.heldPropId -eq $CrateId }).Count

    # --- positive control 1: somebody actually got it -------------------------------------------
    if ($aHeld -eq 0 -and $bHeld -eq 0) {
        Write-Fail "STAGING: neither bot ever held crate $CrateId. Nothing was contested, so nothing below would mean anything. Check the bots reached the crate at all (carrynet.contend*.out.log)."
    }
    if ($aHeld -gt 0 -and $bHeld -gt 0) {
        Add-Failure "BOTH peers report holding crate $CrateId in their own loadout (A in $aHeld samples, B in $bHeld) -- the prop DUPLICATED across the contested grab"
    }
    if ($aHeld -gt 0) { $winner = $peerA; $loser = $peerB; $winName = "ContendA"; $loseName = "ContendB" }
    else { $winner = $peerB; $loser = $peerA; $winName = "ContendB"; $loseName = "ContendA" }
    Add-Measured "contention winner: $winName (peer $winner); loser: $loseName (peer $loser)"

    # --- positive control 2: the loser actually pressed, and was actually refused -----------------
    $denials = Get-GrabDenials $serverOut
    $loserDenials = @($denials | Where-Object { $_.Peer -eq $loser })
    $taken = @($loserDenials | Where-Object { $_.Reason -eq "Taken" -or $_.Reason -eq "AlreadyHeld" })
    if ($loserDenials.Count -eq 0) {
        Add-Failure "POSITIVE CONTROL FAILED: the server never refused a grab from peer $loser. Either the losing bot never pressed, or its request never arrived -- so 'the loser never held it' below is satisfied by a bot that never tried, and this run did NOT test contention."
    } elseif ($taken.Count -eq 0) {
        $reasons = ($loserDenials | ForEach-Object { $_.Reason }) -join ","
        Add-Failure "STAGING: peer $loser's grab was refused for [$reasons], not Taken/AlreadyHeld. OutOfRange means the bot never got within PropManager.GrabRange (2.25 m) of the crate -- that is a staging fault, not a contention result."
    } else {
        Add-Measured "server refused peer $loser's grab: reason=$($taken[0].Reason) ($($taken.Count) refusal(s) total)"
        # The achieved skew, stated honestly: the gap between the refusal landing on the server and
        # the winning hold becoming visible on the winner's own client. Not the raw press-to-press
        # gap (nothing logs a press), and it includes one bot tick plus the ApplyPropState round
        # trip -- so it is an UPPER bound on how far apart the two requests were.
        $winSamples = if ($winner -eq $peerA) { $sa } else { $sb }
        $firstHeld = @($winSamples | Where-Object { [int]$_.heldPropId -eq $CrateId })
        if ($firstHeld.Count -gt 0 -and $taken[0].WallMs -gt 0) {
            $gap = $taken[0].WallMs - [double]$firstHeld[0].wall
            Add-Measured ("contention skew (upper bound): refusal landed {0:N0} ms from the winning hold becoming visible on the winner's client" -f $gap)
        }
    }

    # --- the invariants, on both peers' independent views ---------------------------------------
    foreach ($set in @(@{ N = "ContendA (own view)"; S = $sa }, @{ N = "ContendB (own view)"; S = $sb })) {
        Add-Failures (Test-NeverHeldBy $set.S $CrateId $loser $set.N)
        Add-Failures (Test-HolderSet $set.S $CrateId $winner $set.N)
    }

    # The loser's own slots must be empty of it -- the holder field and the loadout are two
    # independent replicated facts, and a bug that agreed on one while disagreeing on the other is
    # exactly the shape PropManager's "loadout-before-props" ordering exists to prevent.
    $loserSamples = if ($loser -eq $peerA) { $sa } else { $sb }
    $slotHit = @($loserSamples | Where-Object {
        [int]$_.slot0 -eq $CrateId -or [int]$_.slot1 -eq $CrateId -or [int]$_.arms -eq $CrateId
    })
    if ($slotHit.Count -gt 0) {
        Add-Failure "the refused peer $loser had crate $CrateId in a loadout slot in $($slotHit.Count) sample(s) -- the loadout and the holder state disagree"
    }

    # No duplication and no loss: every peer sees the same prop count, for the whole run.
    $counts = @(@(@($sa | ForEach-Object { [int]$_.propCount }) + @($sb | ForEach-Object { [int]$_.propCount })) | Sort-Object -Unique)
    if ($counts.Count -ne 1) {
        Add-Failure "prop count varied across the contention run: [$($counts -join ', ')] -- a networked prop was duplicated or lost"
    } else {
        Add-Measured "prop count stable at $($counts[0]) on both peers for the whole contention run"
    }
    $dups = @(@($sa) + @($sb) | Where-Object { @($_.dupNames).Count -gt 0 })
    if ($dups.Count -gt 0) { Add-Failure "duplicate node names reported in $($dups.Count) sample(s) -- identity collision" }

    # --- mutation control: rewrite one sample's holder to the LOSER ------------------------------
    $mut = Get-Samples $aLog
    $mutated = $false
    foreach ($s in $mut) {
        $p = Get-Prop $s $CrateId
        if ($null -ne $p -and [long]$p.holder -eq $winner) { $p.holder = $loser; $mutated = $true; break }
    }
    if (-not $mutated) {
        Add-Failure "MUTATION CONTROL could not be staged: no sample in ContendA's log shows the crate held by the winner, so the 'never held by the loser' detector cannot be exercised"
    } else {
        $null = Assert-DetectorFires "contention: holder rewritten to the refused peer" `
            (Test-NeverHeldBy $mut $CrateId $loser "mutant")
    }
}

# --------------------------------------------------------------------------------------------------
# PHASE 2 -- AUTHORITY THROUGH A DISCONNECT, AND THE RE-GRAB AFTERWARDS
# --------------------------------------------------------------------------------------------------
if ($Phase -eq "all" -or $Phase -eq "authority") {
    Write-Host "[2/3] authority: the holder disconnects, and somebody else picks it up..." -ForegroundColor Cyan
    # BUDGETS, NOT PREDICTIONS. The first version of this phase gave DropC `--duration 6` on the
    # reasoning that ~5 m at ~3.6 m/s is under two seconds. It measured otherwise, twice, on a
    # verified-idle machine: the SERVER moved DropC 2.4 m in 5.8 s (~0.42 m/s) while its client
    # predicted the full walk speed, prediction error spiking to 1.71 m nine times. The lifetime
    # expired with the ball never picked up, and the case this phase exists for silently never ran.
    #
    # Every clock guess is now gone. C exits on an OBSERVED event (--exit-when-holding), C presses
    # on a cadence instead of once (--carry-grab-retry, because a single press decides on a
    # PREDICTED position), and D is not launched until the server has actually reported C gone.
    # These numbers are ceilings: on a healthy run none of them is reached.
    $cBudget = 30      # C: how long the grab-and-hold gets to happen at all
    $dBudget = 20      # D: how long the re-grab-and-carry gets, measured from ITS launch
    $wBudget = 45      # W: outlives both, and is the phase's outer bound
    $holdBeforeExitSec = 1.5
    $cLog = Join-Path $script:LogDir "carrynet.dropC.jsonl"
    $dLog = Join-Path $script:LogDir "carrynet.grabD.jsonl"
    $wLog = Join-Path $script:LogDir "carrynet.witnessW.jsonl"
    $cOut = Join-Path $script:LogDir "carrynet.dropC.out.log"
    $serverOut = Join-Path $script:LogDir "carrynet.authority.server.out.log"
    $procs = @()
    try {
        $server = Start-Godot @("--server", "--port", $AuthorityPort, "--world", "propsync") "carrynet.authority.server"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
            Write-Fail "authority server never reported listening; see $serverOut"
        }

        # W first, so the witness is connected before anything happens to the ball.
        $botW = Start-Godot @("--bot", "--address", "127.0.0.1:$AuthorityPort", "--name", "WitnessW",
            "--log", $wLog, "--duration", $wBudget, "--world", "propsync",
            "--carry-script", "1.5,0.5,1.5,9999,-1") "carrynet.witnessW"
        $procs += $botW
        Start-Sleep -Milliseconds 300

        # C: walks to the ball, presses until it is genuinely holding, holds a beat, then quits
        # cleanly WHILE STILL HOLDING. Nothing here is timed off a clock the machine could miss.
        $botC = Start-Godot @("--bot", "--address", "127.0.0.1:$AuthorityPort", "--name", "DropC",
            "--log", $cLog, "--duration", $cBudget, "--world", "propsync",
            "--carry-script", "-2.5,0.5,-2.5,1.0,-1",
            "--carry-grab-retry", "0.5",
            "--exit-when-holding", "$holdBeforeExitSec") "carrynet.dropC"
        $procs += $botC

        # D IS NOT LAUNCHED UNTIL THE SERVER SAYS C IS GONE, and that ordering is the fix for the
        # second fault the first run exposed. D used to start alongside C and press at a guessed
        # 3.0 s "which C will have beaten"; when C ran late, D's grab SUCCEEDED, and the throw that
        # was only there to arm D's chase -- harmless with empty hands -- fired for real and hurled
        # the ball 12 m across the field. A staging step whose harmlessness depends on the OUTCOME
        # of the step before it is not staging, it is a second race. Sequencing on the server's own
        # "peer left" line removes it: when D presses, the ball is loose and uncontested, so D needs
        # no throw and no chase at all.
        #
        # This also strengthens the case rather than weakening it: D is now a peer that never
        # witnessed the hold, so it proves a prop released by a disconnect is grabbable by someone
        # who only ever saw it lying there.
        Write-Host "        waiting for DropC to grab, hold, and disconnect..." -ForegroundColor DarkGray
        if (-not (Wait-ForLogLine $serverOut "peer left peer=" ($cBudget + 30))) {
            Write-Fail "STAGING: no peer ever left the authority server. DropC neither grabbed nor timed out, which means the run never reached the disconnect at all; see $cOut"
        }

        $botD = Start-Godot @("--bot", "--address", "127.0.0.1:$AuthorityPort", "--name", "GrabD",
            "--log", $dLog, "--duration", $dBudget, "--world", "propsync",
            "--carry-script", "-2.5,0.5,-2.5,1.0,-1",
            # WALK TO THE BALL, NOT TO WHERE THE BALL SPAWNED. A prop released by a disconnecting
            # holder rests where THAT player was standing: measured, 1.1 m off the spawn, which put
            # this bot 1.99 m from it -- outside SandboxAvatar.PickupRadius (1.5 m), so its client
            # never even sent a request, and it stood there for 16 s while the suite reported a
            # carry defect. The coordinate above stays as the fallback for the ticks before the
            # ball has replicated.
            "--carry-target-prop", "$BallId",
            "--carry-grab-retry", "0.5",
            "--carry-walk-to", "3,-8") "carrynet.grabD"
        $procs += $botD

        foreach ($p in @($botC, $botD, $botW)) {
            if (-not (Wait-ForExit $p ($wBudget + 90))) { Write-Fail "an authority-phase bot never exited" }
        }
        foreach ($p in @($botC, $botD, $botW)) {
            if ($p.ExitCode -ne 0) { Write-Fail "an authority-phase bot exited $($p.ExitCode); see carrynet.*.out.log" }
        }
    } finally {
        Stop-Procs $procs
    }

    # THE DISCONNECT'S OWN POSITIVE CONTROL, read from C's process rather than inferred. "A peer
    # left" is not the same fact as "a peer left while holding something", and only the second one
    # stages this phase. BotHarness prints the line exactly when it quits in that state.
    if (-not (Select-String -Path $cOut -Pattern "disconnecting at .* WHILE STILL HOLDING" -Quiet)) {
        $reached = Select-String -Path $cOut -Pattern "is holding at .* exiting in"
        $why = if ($reached) { "it reached the hold but never got to the exit" } else { "it never held the ball at all" }
        Write-Fail "STAGING: DropC did not disconnect while holding -- $why. Its --duration ($cBudget s) is a budget, so this means the grab genuinely failed rather than that the machine was slow; see $cOut"
    }

    $sc = Get-Samples $cLog
    $sd = Get-Samples $dLog
    $sw = Get-Samples $wLog
    $peerC = [long]$sc[0].self
    $peerD = [long]$sd[0].self
    Write-Host "        peers: DropC=$peerC GrabD=$peerD Witness=$([long]$sw[0].self)" -ForegroundColor DarkGray

    # Read the whole story off the INDEPENDENT witness. C's own log and D's own log could each be
    # a local prediction; W never touches the ball, so its view is the shared one.
    $seq = Get-HolderTransitions $sw $BallId
    $seqText = ($seq | ForEach-Object { "$($_.Holder)@$([math]::Round($_.Sec,1))s" }) -join " > "
    Add-Measured "witness saw ball $BallId holder timeline: $seqText"

    $iHeldByC = -1; $iReleased = -1; $iHeldByD = -1
    for ($i = 0; $i -lt $seq.Count; $i++) {
        if ($iHeldByC -lt 0) { if ($seq[$i].Holder -eq $peerC) { $iHeldByC = $i }; continue }
        if ($iReleased -lt 0) { if ($seq[$i].Holder -eq 0) { $iReleased = $i }; continue }
        if ($iHeldByD -lt 0 -and $seq[$i].Holder -eq $peerD) { $iHeldByD = $i }
    }

    # --- positive control: the disconnect case was actually staged -------------------------------
    if ($iHeldByC -lt 0) {
        Write-Fail "STAGING: the witness never saw the ball held by DropC (peer $peerC). The disconnect-while-holding case was never set up, so nothing below is a result. Check DropC reached the ball (carrynet.dropC.out.log)."
    }
    if ($iReleased -lt 0) {
        Add-Failure "DEFECT: the witness saw the ball held by DropC and NEVER saw it released. A disconnecting holder must not take the prop with it -- the ball is parented to a dead peer."
    }
    if ($iHeldByD -lt 0) {
        # STAGING OR DEFECT, decided by a measurement rather than by which is more interesting.
        # A bot that never got within its own client's PickupRadius never sent a request at all,
        # so the prop was never asked for and "it could not be grabbed" is not a finding about the
        # prop. Print the closest approach either way: it is the number that separates the two.
        $closest = [double]::MaxValue
        foreach ($x in $sd) {
            $p = Get-Prop $x $BallId
            $me = Get-PeerRow $x $peerD
            if ($null -eq $p -or $null -eq $me) { continue }
            $dd = Dist3 $p.x $p.y $p.z $me.x $me.y $me.z
            if ($dd -lt $closest) { $closest = $dd }
        }
        Add-Measured ("GrabD's closest approach to the ball: {0:F2} m (client PickupRadius is {1} m)" -f $closest, $PickupRadius)
        if ($closest -gt $PickupRadius) {
            Add-Failure ("STAGING: GrabD never got within reach of the ball -- closest approach {0:F2} m against a {1} m client PickupRadius, so its client never sent a grab request and the prop was never actually asked for. This is the bot failing to walk to where the ball came to rest, NOT a prop that could not be picked up." -f $closest, $PickupRadius)
        } else {
            Add-Failure ("DEFECT: after DropC disconnected, GrabD (peer $peerD) got to within {0:F2} m of the ball -- inside the {1} m reach -- and still never held it on the witness's view. A prop released by a disconnect must be grabbable again, not left frozen and inert; this is the half Run-CarryTest does not check." -f $closest, $PickupRadius)
        }
    } else {
        $latency = $seq[$iHeldByD].Sec - $seq[$iReleased].Sec
        # Labelled for what it actually contains. GrabD is launched AFTER the disconnect, so this
        # spans a whole process start, connect and walk -- it is NOT a netcode latency and must not
        # be read as one. What it is good for is a regression signal on the phase's own shape.
        Add-Measured ("release -> re-grab on the witness: {0:F2} s (includes GrabD's process start, connect and walk)" -f $latency)
    }

    # The ball must never be attributed back to the dead peer once it is gone.
    if ($iReleased -ge 0) {
        $afterSec = $seq[$iReleased].Sec
        foreach ($s in $sw) {
            if ([double]$s.Sec -lt $afterSec) { continue }
            $p = Get-Prop $s $BallId
            if ($null -ne $p -and [long]$p.holder -eq $peerC) {
                Add-Failure "witness @ $([math]::Round($s.Sec,2))s: ball $BallId is attributed to peer $peerC, who disconnected at $([math]::Round($afterSec,2))s -- a ghost holder"
                break
            }
        }
    }

    # And once D has it, it must TRACK D -- on D's own view and on the witness's, while D walks
    # away. The witness view is the load-bearing one: it is the peer where a broken re-bind shows.
    if ($iHeldByD -ge 0) {
        $from = $seq[$iHeldByD].Sec + 1.0   # one settle beat for the snap-to-hand lerp
        foreach ($set in @(@{ N = "GrabD (own view)"; S = $sd }, @{ N = "Witness (independent)"; S = $sw })) {
            $r = Measure-HoldTracking $set.S $BallId $peerD $from $HoldRadius 5 $set.N
            Add-Failures $r.Failures
            if ($r.Checked -gt 0) {
                Add-Measured ("{0}: {1} held samples, worst hold distance {2:F2} m" -f $set.N, $r.Checked, $r.Worst)
            }
        }

        # --- mutation control: shove the held ball 5 m out of D's hands in one sample ------------
        $mut = Get-Samples $wLog
        $moved = $false
        foreach ($s in $mut) {
            if ([double]$s.Sec -lt $from) { continue }
            $p = Get-Prop $s $BallId
            if ($null -ne $p -and [long]$p.holder -eq $peerD) { $p.x = [double]$p.x + 5.0; $moved = $true; break }
        }
        if (-not $moved) {
            Add-Failure "MUTATION CONTROL could not be staged: no witness sample shows the ball held by GrabD after the settle beat"
        } else {
            $mr = Measure-HoldTracking $mut $BallId $peerD $from $HoldRadius 5 "mutant"
            $null = Assert-DetectorFires "authority: held prop displaced 5 m from its holder" $mr.Failures
        }
    }
}

# --------------------------------------------------------------------------------------------------
# PHASE 3 -- A CARRIED PROP SURVIVING ITS HOLDER'S SERVER TELEPORT
#
# Restored by ROUND-1 (2026-09-19) on the port the fork left unclaimed for it. Same property,
# different trigger: the round's Holding -> Hiding transition moves the hider 40 m into the search
# room, and the crate in its hands has to come too.
# --------------------------------------------------------------------------------------------------
if ($Phase -eq "all" -or $Phase -eq "teleport") {
    Write-Host "[3/3] teleport: the hider is moved 40 m to another room while holding a crate..." -ForegroundColor Cyan

    # The crate goes in the holding room, inside reach of a spawn marker (BASE-1's holding markers
    # are at +/-2.5 on both axes; PropManager.GrabRange is 2.25 m and the bot walks to it anyway).
    $SeedX = -1.0; $SeedY = 0.6; $SeedZ = -1.0

    # BUDGETS, NOT PREDICTIONS -- phase 2's lesson, applied. P presses on a CADENCE
    # (--carry-grab-retry) rather than once, because a single press decides on a predicted
    # position; its grab window is 3 s..Start, and the only number that has to be right is that
    # the round's Start comes after it. 12 s of grabbing against a ~2 m walk is a ceiling.
    $StartAtSec = 12
    $dur = 34

    $pLog = Join-Path $script:LogDir "carrynet.portP.jsonl"
    $wLog = Join-Path $script:LogDir "carrynet.portW.jsonl"
    $serverOut = Join-Path $script:LogDir "carrynet.port.server.out.log"
    $procs = @()
    try {
        $server = Start-Godot @("--server", "--port", $TeleportPort, "--world", "supermarket",
            "--seed-test-props", "$SeedX,$SeedY,$SeedZ",
            # Start only. Confirm is deliberately never pressed: this phase is about ONE teleport,
            # and a second one (Hiding -> Seeking, 30 s later) would move the holder again inside
            # the window the assertions sample.
            "--round-script", "start@$StartAtSec") "carrynet.port.server"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
            Write-Fail "teleport server never reported listening; see $serverOut"
        }

        # P FIRST, and the stagger is load-bearing rather than politeness: the round's roster is in
        # JOIN order and the first joiner is the first round's hider, which is the peer that gets
        # teleported. A race here would make which bot is the subject a coin flip.
        $botP = Start-Godot @("--bot", "--address", "127.0.0.1:$TeleportPort", "--name", "PortalP",
            "--log", $pLog, "--duration", $dur, "--world", "supermarket",
            "--carry-script", "$SeedX,$SeedY,$SeedZ,3.0,-1",
            "--carry-grab-retry", "0.5") "carrynet.portP"
        $procs += $botP
        Start-Sleep -Milliseconds 2000
        $botW = Start-Godot @("--bot", "--address", "127.0.0.1:$TeleportPort", "--name", "PortalW",
            "--log", $wLog, "--duration", $dur, "--world", "supermarket") "carrynet.portW"
        $procs += $botW

        foreach ($proc in @($botP, $botW)) {
            if (-not (Wait-ForExit $proc ($dur + 90))) { Write-Fail "a teleport bot never exited" }
        }
        foreach ($pair in @(@{ P = $botP; N = "PortalP" }, @{ P = $botW; N = "PortalW" })) {
            if ($pair.P.ExitCode -ne 0) {
                # A bot that printed its own done line and then died on the way out is a teardown
                # artifact, not a check (.claude/rules/test-suite.md, BASE-1's -1073741795 entry).
                Write-Host "        NOTE: $($pair.N) exited $($pair.P.ExitCode); read its .out.log for its own done line before calling that a red" -ForegroundColor Yellow
            }
        }
    } finally {
        Stop-Procs $procs
    }

    $sp = Get-Samples $pLog
    $sw = Get-Samples $wLog
    $peerP = [long]$sp[0].self
    $peerW = [long]$sw[0].self
    Write-Host "        peers: PortalP=$peerP (the hider) PortalW=$peerW (witness)" -ForegroundColor DarkGray

    # The crate, identified by where it was SEEDED rather than by an id typed here -- the level is
    # about to gain a hundred authored props (SHELF-1) and "the only prop in this world is mine"
    # stops being true the day it does.
    $found = Find-PropNear $sw $SeedX $SeedY $SeedZ
    if ($null -eq $found -or $found.Dist -gt 2.0) {
        Write-Fail "STAGING: the witness never saw a prop near the seed point ($SeedX,$SeedY,$SeedZ) -- --seed-test-props did not land"
    }
    $CarriedId = $found.Id
    Add-Measured "teleport: carried prop id $CarriedId, first seen $([math]::Round($found.Dist,2)) m from its seed point"

    # Where the server actually sent the hider. Read from the driver's own line rather than typed,
    # so a room moved in the editor moves this assertion with it.
    $destX = $null; $destY = $null; $destZ = $null
    foreach ($line in @(Get-Content $serverOut)) {
        if ($line -match '\[round\] peer (\d+) -> search\[\d+\] \(([-0-9.eE]+), ([-0-9.eE]+), ([-0-9.eE]+)\)') {
            if ([long]$matches[1] -eq $peerP) {
                $destX = [double]$matches[2]; $destY = [double]$matches[3]; $destZ = [double]$matches[4]
                break
            }
        }
    }
    if ($null -eq $destX) {
        Write-Fail ("STAGING: the server never logged a move of peer $peerP into the search room -- the round " +
                    "never started, so no teleport happened. Check $serverOut for a [round] refused: line.")
    }
    Add-Measured "teleport: the server sent peer $peerP to ($destX, $destY, $destZ)"

    # --- positive control: P was actually HOLDING it, and the witness agreed ----------------------
    $pHeld = @($sp | Where-Object { [int]$_.heldPropId -eq $CarriedId }).Count
    if ($pHeld -eq 0) {
        Write-Fail "STAGING: PortalP never held prop $CarriedId -- the grab never landed, so nothing below is about a teleport"
    }
    Add-Failures (Test-HolderSet $sw $CarriedId $peerP "PortalW (witness)")

    # --- the move itself, on the WITNESS's own clock ---------------------------------------------
    # Per log, always. Two bot processes do not share a clock, and anchoring one log's window with
    # another log's elapsed seconds is the category error that made this very phase's mutation
    # control pass on a mutant the first time it was written (see Get-FirstSecPeerBelow's note).
    $arrivedSec = Get-FirstSecPeerNear $sw $peerP $destX $destY $destZ 8.0
    if ($arrivedSec -lt 0) {
        Add-Failure "PortalW never saw peer $peerP anywhere near the search room -- the teleport did not replicate to the witness at all"
    } else {
        Add-Measured "teleport: the witness first saw the hider in the search room at $([math]::Round($arrivedSec,2))s on its own clock"
    }

    # --- THE ASSERTION: the crate came too --------------------------------------------------------
    Add-Failures (Test-PropReached $sw $CarriedId $destX $destY $destZ 3.0 5 "PortalW (witness)")

    # ...and kept tracking its holder on the far side, rather than arriving and then detaching.
    if ($arrivedSec -ge 0) {
        $track = Measure-HoldTracking $sw $CarriedId $peerP ($arrivedSec + 0.5) $HoldRadius 5 "PortalW (witness)"
        Add-Failures $track.Failures
        Add-Measured ("teleport: after the move the witness measured the held crate at worst {0:F2} m from its holder over {1} sample(s) (bar {2} m)" -f $track.Worst, $track.Checked, $HoldRadius)
    }

    # The prop must not have been eaten on the way: the count never moves on either peer.
    $counts = @(@(@($sp | ForEach-Object { [int]$_.propCount }) + @($sw | ForEach-Object { [int]$_.propCount })) | Sort-Object -Unique)
    if ($counts.Count -ne 1) {
        Add-Failure "prop count varied across the teleport run: [$($counts -join ', ')] -- a networked prop was duplicated or lost across the move"
    } else {
        Add-Measured "teleport: prop count stable at $($counts[0]) on both peers for the whole run"
    }

    # --- mutation control: leave the crate behind -------------------------------------------------
    # Applied to a freshly re-parsed copy so it cannot leak into the assertions above.
    $mut = Get-Samples $wLog
    $moved = $false
    foreach ($s in $mut) {
        $pr = Get-Prop $s $CarriedId
        if ($null -eq $pr) { continue }
        # Put it back where it was seeded -- exactly the defect this phase exists to catch.
        $pr.x = $SeedX; $pr.y = $SeedY; $pr.z = $SeedZ
        $moved = $true
    }
    if (-not $moved) {
        Add-Failure "MUTATION CONTROL could not be staged: the witness's log has no samples carrying prop $CarriedId at all"
    } else {
        $null = Assert-DetectorFires "teleport: the carried crate left behind in the room the holder left" (Test-PropReached $mut $CarriedId $destX $destY $destZ 3.0 5 "mutant")
    }
}

# ==================================================================================================
Write-Host ""
Write-Host "MEASURED QUANTITIES (compare these across runs, NOT the verdict --" -ForegroundColor White
Write-Host "  see .claude/rules/test-suite.md on the carry family: a pass/fail from a" -ForegroundColor White
Write-Host "  load-sensitive suite on this machine is close to worthless on its own):" -ForegroundColor White
foreach ($m in $script:Measured) { Write-Host "  $m" }

Write-Host ""
if ($script:Failures.Count -gt 0) {
    Write-Host "CARRYNET-TEST FAILED ($($script:Failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in ($script:Failures | Select-Object -First 20)) { Write-Host "  - $f" -ForegroundColor Red }
    if ($script:Failures.Count -gt 20) { Write-Host "  ... and $($script:Failures.Count - 20) more" -ForegroundColor Red }
    Write-Host ""
    Write-Host "CARRYNET-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: a contested grab produces exactly one holder and a told loser; a disconnecting holder releases its prop and the next player can pick it up; a crate held through a 40 m server teleport arrives with its holder and keeps tracking it. Every detector was re-run against a mutated copy of its own logs and went red." -ForegroundColor Green
Write-Host ""
Write-Host "CARRYNET-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
