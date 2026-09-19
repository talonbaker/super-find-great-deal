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

    PHASE 3 -- CARRIED THROUGH A TV PORTAL (world bubbletest). The packet: *"teleport-while-holding
    is a classic way to lose an object. Check it."* A crate is seeded on the approach line to the
    hub TV. Bot P grabs it and walks into the screen. TvPortalHost.OnBodyAtScreen moves the AVATAR
    through SandboxAvatar.ServerTeleportTo and says nothing at all about the thing in its hands --
    the prop follows only because NetworkedProp.BindToHolder derives its transform from the
    holder's carry anchor every frame. That is a property worth proving rather than assuming, and
    the failure it guards is specific: a crate left standing in the hub, or eaten by
    Carryable.KillPlaneY on the way down. The witness bot proves it on an INDEPENDENT peer, which
    is the only view that can tell a real teleport from a local prediction.

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
    [int]$PortalPort = 7895,
    [ValidateSet("all", "contention", "authority", "portal")]
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

# The nearest prop to a point, read from the EARLIEST sample that has any props at all. Phase 3
# uses this instead of a hardcoded prop id: bubbletest seeds nothing today, but LEVEL-4 is adding
# golden cubes to it, and a suite that assumed "the only prop is mine" would break the moment they
# land. Identifying the fixture by where it was SEEDED survives any number of props appearing
# beside it.
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

# The prop must END this log below $MaxY -- it went through the portal with its holder rather than
# being left standing in the hub.
function Test-PropWentDown($Samples, [int]$PropId, [double]$MaxY, [int]$MinSamples, [string]$ViewName) {
    $f = @()
    $below = @($Samples | ForEach-Object {
        $p = Get-Prop $_ $PropId
        if ($null -ne $p -and [double]$p.y -lt $MaxY) { $p }
    })
    if ($below.Count -lt $MinSamples) {
        $f += "${ViewName}: prop $PropId was below y=$MaxY in only $($below.Count) sample(s) (need $MinSamples) -- the carried prop did not go through the portal with its holder"
    }
    return $f
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
# PHASE 3 -- CARRIED THROUGH A TV PORTAL
# --------------------------------------------------------------------------------------------------
if ($Phase -eq "all" -or $Phase -eq "portal") {
    Write-Host "[3/3] portal: a crate carried through the TV..." -ForegroundColor Cyan

    # The hub TV's walk-in trigger in world space, and the 1.3 m overshoot that makes a bot with a
    # 1.2 m ArriveRadius actually TOUCH it. Both are Run-TvPortalTest's, copied with their reason
    # rather than re-derived -- see that script's comment block; aiming at the trigger itself parks
    # the bot a metre short of a screen it never enters, which looks exactly like a broken portal.
    $HubTvTrigger = @(-22.93, -22.51)
    # The crate: 4 m back along the same approach line, so the bot walks to it, grabs it, and then
    # continues on the SAME heading into the screen. The approach unit vector from the origin is
    # about (-0.714, -0.701), so 4 m back from the trigger is (-22.93 + 2.856, -22.51 + 2.804).
    $SeedX = [math]::Round($HubTvTrigger[0] + 2.856, 3)
    $SeedZ = [math]::Round($HubTvTrigger[1] + 2.804, 3)
    $SeedY = 0.5
    $seedArg = "$SeedX,$SeedY,$SeedZ"
    Write-Host "        seeding one test crate at ($seedArg)" -ForegroundColor DarkGray

    # The room floor is PARSED from BubbleTestLayout, never retyped -- BT-10 lost an afternoon to a
    # second copy of this constant when Talon moved the room from -40 to -20.
    $roomFloorY = $(
        $layoutSrc = "$PSScriptRoot/../scripts/game/world/bubbletest/BubbleTestLayout.cs"
        $src = Get-Content -Raw -LiteralPath $layoutSrc
        if ($src -match 'TvRoomAnchor\s*=\s*new\(\s*[-0-9.f]+\s*,\s*(-?[0-9.]+)f') { [double]$matches[1] }
        else { throw "could not parse TvRoomAnchor from BubbleTestLayout.cs" }
    )
    # Halfway between the hub floor (0) and the room floor: unambiguously "downstairs", with no
    # dependence on where in the room anything settles.
    $downY = $roomFloorY / 2.0
    Write-Host "        room floor y=$roomFloorY (parsed); 'through the portal' means y < $downY" -ForegroundColor DarkGray

    $dur = 24
    $pLog = Join-Path $script:LogDir "carrynet.portalP.jsonl"
    $wLog = Join-Path $script:LogDir "carrynet.portalW.jsonl"
    $serverOut = Join-Path $script:LogDir "carrynet.portal.server.out.log"
    $procs = @()
    try {
        $server = Start-Godot @("--server", "--port", $PortalPort, "--world", "bubbletest",
            "--seed-test-props", $seedArg) "carrynet.portal.server"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
            Write-Fail "portal server never reported listening; see $serverOut"
        }
        if (-not (Select-String -Path $serverOut -Pattern "--seed-test-props: seeded 1 test crate" -Quiet)) {
            Write-Fail "the test crate was never seeded; --seed-test-props did not take. See $serverOut"
        }

        $botP = Start-Godot @("--bot", "--address", "127.0.0.1:$PortalPort", "--name", "PortalP",
            "--log", $pLog, "--duration", $dur, "--world", "bubbletest",
            "--carry-script", "$seedArg,1.0,-1",
            # Same one-press-on-a-predicted-position hazard as phase 2. P only starts walking
            # toward the screen once its grab has landed, so a wasted press here does not fail
            # loudly as "the crate was never carried" -- it fails as "the server never teleported
            # PortalP", which points at the portal rather than at the pickup.
            "--carry-grab-retry", "0.5",
            "--carry-walk-to", "$($HubTvTrigger[0]),$($HubTvTrigger[1])") "carrynet.portalP"
        $procs += $botP
        Start-Sleep -Milliseconds 400

        # The witness stands in the hub's opposite corner, nowhere near a screen, and never grabs.
        $botW = Start-Godot @("--bot", "--address", "127.0.0.1:$PortalPort", "--name", "PortalW",
            "--log", $wLog, "--duration", $dur, "--world", "bubbletest",
            "--carry-script", "20,0.5,20,9999,-1") "carrynet.portalW"
        $procs += $botW

        foreach ($p in @($botP, $botW)) {
            if (-not (Wait-ForExit $p ($dur + 90))) { Write-Fail "a portal-phase bot never exited" }
        }
        foreach ($p in @($botP, $botW)) {
            if ($p.ExitCode -ne 0) { Write-Fail "a portal-phase bot exited $($p.ExitCode); see carrynet.portal*.out.log" }
        }
    } finally {
        Stop-Procs $procs
    }

    $sp = Get-Samples $pLog
    $sw = Get-Samples $wLog
    $peerP = [long]$sp[0].self
    Write-Host "        PortalP is peer $peerP" -ForegroundColor DarkGray

    # Identify the fixture by WHERE IT WAS SEEDED, not by id. LEVEL-4 is adding golden cubes to
    # this world; a suite that assumed "the only prop is mine" would break the day they land.
    $found = Find-PropNear $sp $SeedX $SeedY $SeedZ
    if ($null -eq $found -or $found.Dist -gt 0.75) {
        $d = if ($null -eq $found) { "none at all" } else { ("{0:F2} m away" -f $found.Dist) }
        Write-Fail "STAGING: no prop found at the seed point ($seedArg) in PortalP's first sample -- nearest was $d. The fixture did not replicate to the client."
    }
    $crate = $found.Id
    Add-Measured "seeded crate is prop $crate (found $([math]::Round($found.Dist,3)) m from the seed point)"

    # --- positive control 1: the bot actually picked it up ---------------------------------------
    $heldSamples = @($sp | Where-Object { [int]$_.heldPropId -eq $crate })
    if ($heldSamples.Count -eq 0) {
        Write-Fail "STAGING: PortalP never picked the crate up, so nothing about carrying it through a portal was tested. Check the bot reached the seed point (carrynet.portalP.out.log)."
    }

    # --- positive control 2: the teleport actually fired, and converged on BOTH peers -------------
    $teleportLines = @(Select-String -Path $serverOut -Pattern "\[bubbletest\.tv\] peer=$peerP ")
    if ($teleportLines.Count -eq 0) {
        Write-Fail "STAGING: the server never teleported PortalP -- it did not walk into the screen. The portal-carry assertions below are unproven, not passed."
    }
    Add-Measured "server teleported PortalP $($teleportLines.Count) time(s)"

    foreach ($set in @(@{ N = "PortalP (own view)"; S = $sp }, @{ N = "PortalW (independent)"; S = $sw })) {
        $down = @($set.S | ForEach-Object { $r = Get-PeerRow $_ $peerP; if ($null -ne $r -and [double]$r.y -lt $downY) { $r } })
        if ($down.Count -lt 5) {
            Add-Failure "$($set.N): PortalP was below y=$downY in only $($down.Count) sample(s) -- the teleport did not converge on this peer, so the crate's behaviour through it cannot be judged from this view"
        }
    }

    # --- the actual question: did the crate go with him? -----------------------------------------
    foreach ($set in @(@{ N = "PortalP (own view)"; S = $sp }, @{ N = "PortalW (independent)"; S = $sw })) {
        Add-Failures (Test-PropWentDown $set.S $crate $downY 5 $set.N)
    }

    # Still HELD, and still tracking, after the teleport. Anchored to the first sample in which
    # PortalP's own view has him downstairs, plus a settle beat -- never to a guessed clock, which
    # is the constant this repo's carry suites kept flaking on.
    $firstDown = Get-FirstSecPeerBelow $sp $peerP $downY
    if ($firstDown -lt 0) {
        Add-Failure "PortalP's own view never put him below y=$downY, so the post-teleport window cannot be anchored"
    } else {
        $from = $firstDown + 1.0
        Add-Measured ("PortalP arrived downstairs at {0:F2} s on its own clock" -f $firstDown)
        foreach ($set in @(@{ N = "PortalP (own view)"; S = $sp }, @{ N = "PortalW (independent)"; S = $sw })) {
            # EACH view anchors on ITS OWN observation of the arrival, plus a settle beat. Reusing
            # PortalP's 7.8 s against the witness's log would be reading one process's clock in
            # another process's samples -- see Get-FirstSecPeerBelow.
            $ownDown = Get-FirstSecPeerBelow $set.S $peerP $downY
            if ($ownDown -lt 0) {
                Add-Failure "$($set.N): never observed PortalP below y=$downY, so this view cannot judge what happened to the crate afterwards"
                continue
            }
            $ownFrom = $ownDown + 1.0
            $stillHeld = @($set.S | Where-Object {
                [double]$_.Sec -ge $ownFrom -and (Get-Prop $_ $crate) -ne $null -and [long](Get-Prop $_ $crate).holder -eq $peerP
            })
            if ($stillHeld.Count -lt 5) {
                Add-Failure "$($set.N): the crate was held by PortalP in only $($stillHeld.Count) sample(s) after the teleport -- teleport-while-holding dropped the object"
            }
            $r = Measure-HoldTracking $set.S $crate $peerP $ownFrom $HoldRadius 5 $set.N
            Add-Failures $r.Failures
            if ($r.Checked -gt 0) {
                Add-Measured ("{0}: arrived downstairs at {1:F2}s on this log's clock; {2} post-teleport held samples, worst hold distance {3:F2} m" -f $set.N, $ownDown, $r.Checked, $r.Worst)
            }
        }

        # The OOB recovery must not have eaten it on the way down. PropManager's loose loop
        # recovers a prop below Carryable.KillPlaneY to its HomeTransform -- which for this crate
        # is the hub seed point. A crate that is "back where it started" after the trip is the
        # exact failure mode, and it would otherwise look like a perfectly healthy resting prop.
        $backHome = @($sp | Where-Object {
            [double]$_.Sec -ge $from -and (Get-Prop $_ $crate) -ne $null -and
            (Dist3 (Get-Prop $_ $crate).x (Get-Prop $_ $crate).y (Get-Prop $_ $crate).z $SeedX $SeedY $SeedZ) -lt 1.0
        })
        if ($backHome.Count -gt 0) {
            Add-Failure "the crate returned to its hub seed point in $($backHome.Count) sample(s) after the teleport -- the kill-plane recovery (PropManager's loose loop, Carryable.KillPlaneY) reclaimed it instead of it travelling with its holder"
        }

        # --- mutation control: leave the crate behind in the hub --------------------------------
        # EVERY crate sample is pinned at hub height, not just the ones after some anchor: the
        # defect being modelled is "the crate never went through at all". The first version pinned
        # only the samples after PortalP's clock reading, left the witness's own earlier descent
        # samples untouched, and the analyser rightly stayed green on a mutant that was not
        # actually broken -- caught by this control failing, which is what it is for.
        $mut = Get-Samples $wLog
        $left = $false
        foreach ($s in $mut) {
            $p = Get-Prop $s $crate
            if ($null -ne $p) { $p.y = 0.5; $left = $true }
        }
        if (-not $left) {
            Add-Failure "MUTATION CONTROL could not be staged: the witness log has no crate samples at all"
        } else {
            $null = Assert-DetectorFires "portal: crate left behind in the hub" `
                (Test-PropWentDown $mut $crate $downY 5 "mutant")
        }
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

Write-Host "PASS: a contested grab produces exactly one holder and a told loser; a disconnecting holder releases its prop and the next player can pick it up; a carried crate goes through the TV portal with its holder and keeps tracking on an independent peer. Every detector was re-run against a mutated copy of its own logs and went red." -ForegroundColor Green
Write-Host ""
Write-Host "CARRYNET-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
