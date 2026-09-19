<#
.SYNOPSIS
    DOOR-1: the burst door, on a real server with two real clients. Proves the four things only a
    live session can prove -- that EVERY PEER opened the door at the same instant, that the
    blocker really stopped blocking, that the shove really moved something, and that the reset
    really put the wall back.

.DESCRIPTION
    Everything about the staging that is a RULE is tested engine-free in tests/unit
    (StartleTimelineTests: the beats and their order, the leaf's throw, overshoot, settle and
    close, the camera kick's shape, the linear impulse falloff, and every malformed overlay).
    None of that is repeated here. This suite exists for the half a pure function cannot reach:

      1. EVERY PEER BURST AT THE SAME INSTANT. The door is round state, not a node with RPCs: each
         peer plays its own copy of the timeline from the one reliable message that carries the
         server's Found tick. So the thing to measure is the SPREAD of the burst across three
         processes, and each peer's own tell->burst gap, both off the wall-clock stamps the door
         prints. A door that opened only on the server would look perfect in the server's log.
      2. THE BLOCKER IS PASSABLE, AND WAS NOT BEFORE. The seeker bot walks at the task room from
         the moment it lands in the vestibule. During the tell it must be held on the vestibule
         side of the door plane; after the burst it must be through it. The two halves are each
         other's control -- "it walked through" alone stays green on a door whose blocker was
         never solid in the first place.
      3. THE SHOVE MOVED SOMETHING. Three crates are seeded inside the burst radius and left to
         settle to Resting for the whole round, so what is measured is the Resting->Loose WAKE
         (the hard half: a Resting prop has no stream behind it, so a server that shoved its own
         copy without waking it would move nothing on any client). Measured on a CLIENT's log,
         never the server's.
      4. THE RESET PUT THE WALL BACK. Every peer logs the close and the blocker going solid, after
         the burst, on the Tally -> Holding edge.

    IT ALSO EXERCISES --startle-file END TO END, and that is not decoration: the overlay is
    handed to all three processes with a deliberately long TellSec, which is what buys assertion 2
    a blocked window long enough to sample, and what makes "the knob Talon edits actually reaches
    the door" a measured fact rather than a promise. Each process loads its OWN copy -- see the
    handoff on why the tuning is not authoritative and what that costs.

    NOT ONE WALL-CLOCK GUESS IN THE ASSERTIONS. Every window is derived from a log line: the
    door's own [door] armed / burst / closed stamps (Unix ms, comparable across processes on one
    machine -- the same field BotHarness's `wall` uses) and the round driver's phase lines. A
    bot's position is judged strictly against the burst that was meant to free it.

    PROVED ABLE TO FAIL. See the EVIDENCE block in
    docs/agents/handoffs/2026-09-19-DOOR-1.md for the planted fault and what it printed.

    Exit 0 = PASS. Headless throughout -- no GPU, no display. One Godot at a time: this suite
    takes the machine-wide suite mutex like every other.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone. NOTE (the trap
    .claude/rules/test-suite.md records under STEAM-1): -SkipBuild skips Reset-LogDir, which is
    what creates tests/logs, so run once WITHOUT it in a fresh worktree.
#>
[CmdletBinding()]
param(
    # Unique across tests/. The ladder as of this lane: Run-CarryNetTest 7893/7894/7895,
    # Run-RoundLoopSmoke 7896, Run-FirstPersonTest 7897, Run-PlaceTest 7898,
    # Run-VoiceRoomTest 7899 (VOICE-1), 7901 claimed by another lane of this wave, and SFX-1
    # claiming upward from 7902. 7900 is the one gap left.
    #
    # THE REASON THIS COMMENT NAMES THE WHOLE LADDER (INT-0, 2026-09-19): three lanes off one base
    # each computed "the next free port" from the same snapshot of tests/ and all three picked
    # 7896, which is a red that looks exactly like ordinary port contention and is not. A port
    # claimed in a comment is a port the next lane can see.
    #
    # AND IT HAPPENED AGAIN INSIDE THIS WAVE, which is worth the extra line. This suite was
    # written on 7899 against a ladder that was correct when it was read; VOICE-1 landed
    # Run-VoiceRoomTest on 7899 first, from its own worktree, and neither lane could see the
    # other. The orchestrator settled it. **A port is not claimed until it is on origin**, and
    # two concurrent lanes reading the same tests/ will pick the same "next free" one every time.
    [int]$Port = 7900,
    [int]$MutexTimeoutMinutes = 30,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# --- the tuning this run uses -----------------------------------------------------------------
#
# TellSec is deliberately 1.2 s rather than the shipped 0.4. Two reasons, both about what can be
# MEASURED rather than about how the door should feel:
#   * a bot samples at about 5 Hz, so a 0.4 s tell is two samples of "still blocked" and 1.2 s is
#     six -- and the blocked window is the control for the passability assertion;
#   * a value that is NOT the default is the only way to prove the overlay was read at all. With
#     0.4 s in the file, a --startle-file that silently did nothing would be indistinguishable
#     from one that worked.
# Every other knob is left at its shipped value, so the sparse file has exactly one line in it.
$TellSec = 1.2

# How far a peer's own burst may sit from its own arm + TellSec. The snapshot interval is
# 2 / 60 s = 33.3 ms (NetProfile.SnapshotIntervalTicks / TickRate) and the packet's bar is "within
# one snapshot interval". The door advances on the RENDER frame, so a headless peer running at its
# own frame rate can only ever land on a frame boundary -- this is that bar plus one 60 Hz frame
# of quantisation, and the RAW worst value is printed so the bar is never the thing anyone reads.
$SnapshotIntervalMs = 1000.0 * 2.0 / 60.0
$BurstGapToleranceMs = $SnapshotIntervalMs + (1000.0 / 60.0)

# How far apart three processes' bursts may land. Wider than the per-peer gap on purpose and for a
# different reason: this one contains the reliable message's own delivery time to two clients plus
# three OS schedulers, none of which the per-peer gap contains. Still small enough that a door
# that opened a frame-and-a-half late on one client would be a red.
$BurstSpreadToleranceMs = 150.0

# The door plane, in world Z. TaskRoom is instanced at x = 80 and the door is its local +Z wall at
# z = 5.2, 0.4 m thick -- so 5.0 is the task-room FACE and 5.4 the vestibule face.
#
# BOTH BARS ARE THE WALL'S OWN FACES, and the first draft of this suite got the blocked one wrong
# in a way worth writing down. It asked for the body's centre to stay past 5.6, reasoning from a
# capsule radius nobody had measured; the first green run measured the blocked seeker's centre at
# 5.55, hard against the face with about 15 cm of clearance, and the suite called a perfectly
# blocked door a failure. The faces need no guess about a body's width: a centre past 5.4 is a
# centre INSIDE the wall slab, and a centre under 5.0 is a centre in the room. What is left
# between them is the wall itself, which neither assertion claims.
#
# Neither bar is delicate. Measured: blocked 5.55 against 5.4, and through 1.12 against 5.0 --
# the second is the length of the room away from the bar, not a hair over it.
$DoorWorldZ = 5.2
$ThroughZ = 5.0
$BlockedZ = 5.4
$TaskRoomWorldX = 80.0

# The doorway's centre in world space: the room's origin, the wall's own z, and the opening's
# centre height (BurstDoor.DoorwayCentreHeightM).
$DoorwayX = $TaskRoomWorldX
$DoorwayY = 1.1
$DoorwayZ = $DoorWorldZ

# How far a prop within the burst radius must move to count. The packet's number.
$PropMovedBarM = 0.3

# The three crates, seeded in the task room inside the 3 m burst radius and left to settle for the
# whole round. Distances from the doorway centre: 1.24 m, 1.51 m and 1.96 m, so the linear falloff
# gives them 3.5, 3.0 and 2.1 N.s -- three different pushes rather than three copies of one.
$SeedProps = "80.0,0.3,4.0;79.2,0.3,4.2;80.8,0.3,3.6"

# The schedule, in seconds from the FIRST PLAYER ARRIVING (the driver's script clock starts there,
# not at server boot -- ROUND-1's measured trap).
#    6  Start        -> Hiding,  hider to the search room
#   12  Confirm      -> Seeking, hider to the task room, seeker to the search room
#   20  the find     -> Together, seeker to the vestibule; the door stages from this instant
#   30  End          -> Tally
#  +6 s of Tally     -> the reset edge: the leaf closes and the blocker comes back
$Script = "start@6,confirm@12,found@20,end@30"

# A CEILING, not an assumption about when anything lands: the schedule's last beat (30) plus the
# tally (6) plus the close (0.6) plus room for three processes to finish. Every assertion below is
# anchored on a log line, never on a typed second.
$BotDurationSec = 50

Write-Host "=== DOOR-1: the burst door, on a real server with two real clients ===" -ForegroundColor White

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

    # --- the startle overlay ------------------------------------------------------------------
    # Written with .NET rather than Set-Content: PowerShell 5.1 mangles UTF-8 on a round trip
    # (.claude/rules/imports-and-encoding.md), and while this document happens to be ASCII, a
    # suite that writes a file the game parses should not be the place that rule gets tested.
    $startleFile = Join-Path $script:LogDir "burstdoor-startle.json"
    $startleJson = "{`n  `"version`": 1,`n  `"values`": {`n    `"TellSec`": $TellSec`n  }`n}`n"
    [System.IO.File]::WriteAllText($startleFile, $startleJson, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "[0/5] startle overlay: TellSec $TellSec s -> $startleFile" -ForegroundColor Cyan

    # --- the server ---------------------------------------------------------------------------
    Write-Host "[1/5] dedicated server on udp/$Port, world supermarket, 3 seeded crates..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "burstdoor-server.out.log"
    $server = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket",
        "--round-script", $Script, "--seed-test-props", $SeedProps,
        "--startle-file", $startleFile) `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "burstdoor-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server

    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "the server never reported listening; see burstdoor-server.out.log"
    }
    Write-Host "        server up (pid $($server.Id))"

    # --- two clients --------------------------------------------------------------------------
    #
    # JOIN ORDER IS THE ROLE. The first to arrive is round 1's hider, so the stagger is not
    # politeness -- without it which bot hides is a coin flip and every hider-only assertion below
    # would have to be vague about which log it was reading.
    #
    # The seeker gets --goto-script at the task room's centre. It spends the first twenty seconds
    # pressing uselessly against whichever wall is between it and that point (harmless, and it can
    # never arrive: the nearest it gets is 35 m away behind a wall), and then it is teleported into
    # the vestibule 1.5 m from the door and walks straight at it. That is the passability probe,
    # and it needs no new flag: the bot is simply trying to get somewhere the door is in the way of.
    Write-Host "[2/5] two bots for ${BotDurationSec}s (hider first -- join order is the role)..." -ForegroundColor Cyan
    $bots = @()
    foreach ($spec in @(
        @{ Name = "DoorHider"; Goto = $null },
        @{ Name = "DoorSeeker"; Goto = "$TaskRoomWorldX,0" })) {
        $name = $spec.Name
        $jsonLog = Join-Path $script:LogDir "burstdoor-$name.jsonl"
        $botArgs = @(
            "--headless", "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $name,
            "--log", $jsonLog, "--duration", $BotDurationSec, "--world", "supermarket",
            "--startle-file", $startleFile)
        if ($null -ne $spec.Goto) { $botArgs += @("--goto-script", $spec.Goto) }
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList $botArgs `
            -RedirectStandardOutput (Join-Path $script:LogDir "burstdoor-$name.out.log") `
            -RedirectStandardError (Join-Path $script:LogDir "burstdoor-$name.err.log") `
            -PassThru -NoNewWindow
        $null = $p.Handle
        $procs += $p
        $bots += @{ Name = $name; Proc = $p; Json = $jsonLog
                    Out = (Join-Path $script:LogDir "burstdoor-$name.out.log") }
        Start-Sleep -Milliseconds 1200
    }

    Write-Host "[3/5] waiting for the round to run and the door to go..." -ForegroundColor Cyan
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
    Write-Host "[4/5] reading three peers' door logs..." -ForegroundColor Cyan

    if (-not (Test-Path $serverOut)) { Write-Fail "no server log was written at all" }

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
    function Dist3([double]$ax, [double]$ay, [double]$az, [double]$bx, [double]$by, [double]$bz) {
        $dx = $ax - $bx; $dy = $ay - $by; $dz = $az - $bz
        return [math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
    }

    # One peer's door timeline, off its own stdout. `wall=` is Unix milliseconds, which is the one
    # field comparable across three processes on one machine (.claude/rules/test-suite.md, FIX-1).
    function Read-DoorLog([string]$Label, [string]$Path) {
        $view = @{ Label = $Label; Armed = $null; Burst = $null; Closed = $null; Closing = $null
                   Tell = $false; Shoved = -1; ForceDropped = -1; Hider = $null; Tell0 = $false
                   SeekerAimed = $false; OpenOnArrival = $null; StartleTell = $null }
        if (-not (Test-Path $Path)) { return $view }
        foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
            if ($line -match '\[door\] armed foundTick=(-?\d+) tell=([0-9.]+)s burstAt=([0-9.]+)s peer=(\d+) wall=(\d+)') {
                if ($null -eq $view.Armed) {
                    $view.Armed = [double]$matches[5]
                    $view.StartleTell = [double]$matches[2]
                }
            }
            elseif ($line -match '\[door\] burst leaf->\S+ blocker=off hider=(\w+) peer=(\d+) wall=(\d+)') {
                if ($null -eq $view.Burst) {
                    $view.Hider = ($matches[1] -eq 'True')
                    $view.Burst = [double]$matches[3]
                }
            }
            elseif ($line -match '\[door\] closed, blocker solid peer=(\d+) wall=(\d+)') {
                if ($null -eq $view.Closed) { $view.Closed = [double]$matches[2] }
            }
            elseif ($line -match '\[door\] closing from .* wall=(\d+)') {
                if ($null -eq $view.Closing) { $view.Closing = [double]$matches[1] }
            }
            elseif ($line -match '\[door\] open on arrival .* wall=(\d+)') {
                $view.OpenOnArrival = [double]$matches[1]
            }
            elseif ($line -match '\[door\] tell: light dipped') { $view.Tell = $true }
            elseif ($line -match '\[door\] burst shoved (\d+) prop') { $view.Shoved = [int]$matches[1] }
            elseif ($line -match '\[door\] burst force-drop: released (\d+) prop') { $view.ForceDropped = [int]$matches[1] }
            elseif ($line -match '\[door\] seeker arrival: look aimed at the doorway') { $view.SeekerAimed = $true }
        }
        return $view
    }

    $doorLogs = @()
    $doorLogs += ,(Read-DoorLog "server" $serverOut)
    foreach ($b in $bots) { $doorLogs += ,(Read-DoorLog $b.Name $b.Out) }

    foreach ($b in $bots) { $b.Samples = Read-Samples $b.Json }

    foreach ($b in $bots) {
        if (@($b.Samples).Count -lt 10) {
            Add-Failure "$($b.Name) logged only $(@($b.Samples).Count) sample(s) -- it never really ran"
        }
        if ($b.Proc.ExitCode -ne 0) {
            # A bot that printed its own completion line and then died on the way out is a
            # teardown artifact, not a failing check (.claude/rules/test-suite.md, BASE-1's
            # -1073741795 entry). Read the log before calling it.
            $done = Select-String -Path $b.Out -Pattern "\[bot\] $($b.Name) done" -Quiet
            if ($done) {
                Write-Host "        NOTE: $($b.Name) exited $($b.Proc.ExitCode) AFTER printing its own done line -- teardown artifact, not a check" -ForegroundColor Yellow
            } else {
                Add-Failure "$($b.Name) exited $($b.Proc.ExitCode) without finishing its run"
            }
        }
    }

    # ==========================================================================================
    # 0. The overlay reached all three processes
    # ==========================================================================================
    foreach ($d in $doorLogs) {
        if ($null -eq $d.StartleTell) {
            Add-Failure "$($d.Label) never armed the door at all -- no [door] armed line in its log"
        } elseif ([math]::Abs($d.StartleTell - $TellSec) -gt 0.001) {
            Add-Failure ("$($d.Label) armed the door with tell=$($d.StartleTell)s; --startle-file " +
                         "asked for ${TellSec}s -- the overlay did not reach this process")
        }
    }

    # ==========================================================================================
    # 1. Every peer burst, and burst at the same instant
    # ==========================================================================================
    $burstTimes = @()
    $worstGapMs = 0.0
    foreach ($d in $doorLogs) {
        if ($null -eq $d.Burst) {
            Add-Failure ("$($d.Label) never logged a burst -- the door did not open on this peer. " +
                         "The door is round state: every peer stages it from the same message, so " +
                         "one peer missing it is the whole failure this suite exists for.")
            continue
        }
        $burstTimes += $d.Burst
        if ($null -ne $d.Armed) {
            $gap = $d.Burst - $d.Armed
            $err = [math]::Abs($gap - $TellSec * 1000.0)
            if ($err -gt $worstGapMs) { $worstGapMs = $err }
            Write-Host ("        $($d.Label): armed -> burst $([math]::Round($gap,1)) ms " +
                        "(asked $([math]::Round($TellSec*1000,0)) ms, off by $([math]::Round($err,1)) ms)") -ForegroundColor DarkGray
            if ($err -gt $BurstGapToleranceMs) {
                Add-Failure ("$($d.Label) burst $([math]::Round($gap,1)) ms after its own arm; " +
                             "TellSec is ${TellSec}s, so the bar is $([math]::Round($BurstGapToleranceMs,1)) ms of error")
            }
        }
    }
    $burstSpreadMs = 0.0
    if ($burstTimes.Count -ge 2) {
        $burstSpreadMs = ([double]($burstTimes | Measure-Object -Maximum).Maximum) -
                         ([double]($burstTimes | Measure-Object -Minimum).Minimum)
        Write-Host "        burst spread across $($burstTimes.Count) peers: $([math]::Round($burstSpreadMs,1)) ms" -ForegroundColor DarkGray
        if ($burstSpreadMs -gt $BurstSpreadToleranceMs) {
            Add-Failure ("the three peers' bursts are $([math]::Round($burstSpreadMs,1)) ms apart " +
                         "(bar $BurstSpreadToleranceMs ms) -- they are not playing one instant")
        }
    } else {
        Add-Failure "fewer than two peers burst at all; there is no spread to measure"
    }

    # The tell is HIDER-SIDE. Exactly one peer may have dipped its light and clicked its intercom,
    # and it must be the one that also reported hider=True on its own burst line. A tell on the
    # seeker's client would tell the seeker something they already know; a tell on the SERVER
    # would be a headless process running presentation.
    $telling = @($doorLogs | Where-Object { $_.Tell })
    $claimHider = @($doorLogs | Where-Object { $_.Hider -eq $true })
    Write-Host "        tell fired on: $(if ($telling.Count -gt 0) { [string]::Join(', ', @($telling | ForEach-Object { $_.Label })) } else { '(nobody)' })" -ForegroundColor DarkGray
    if ($telling.Count -ne 1) {
        Add-Failure ("the tell fired on $($telling.Count) peer(s); §5 puts it on the hiders' " +
                     "clients only, and there is exactly one hider")
    }
    if ($claimHider.Count -ne 1) {
        Add-Failure "$($claimHider.Count) peer(s) reported hider=True at the burst; exactly one is a hider"
    }
    if ($telling.Count -eq 1 -and $claimHider.Count -eq 1 -and $telling[0].Label -ne $claimHider[0].Label) {
        Add-Failure ("the tell fired on '$($telling[0].Label)' but '$($claimHider[0].Label)' is the " +
                     "hider -- the hider-only gate is reading something other than the round's own roles")
    }

    # ==========================================================================================
    # 2. The blocker: held before the burst, passable after it
    #
    # Judged on the SEEKER's own samples of itself, in world Z, restricted to the task-room
    # complex so the search room's geometry cannot be mistaken for it.
    # ==========================================================================================
    $seeker = $bots | Where-Object { $_.Name -eq "DoorSeeker" } | Select-Object -First 1
    $seekerBurst = ($doorLogs | Where-Object { $_.Label -eq "DoorSeeker" } | Select-Object -First 1).Burst
    $seekerArmed = ($doorLogs | Where-Object { $_.Label -eq "DoorSeeker" } | Select-Object -First 1).Armed
    $blockedSamples = 0
    $throughSamples = 0
    $deepestBeforeZ = [double]::MaxValue
    $deepestAfterZ = [double]::MaxValue
    if ($null -ne $seeker -and $null -ne $seekerBurst -and $null -ne $seekerArmed) {
        $selfId = $null
        $first = @($seeker.Samples) | Select-Object -First 1
        if ($null -ne $first) { $selfId = [long]$first.self }
        foreach ($s in @($seeker.Samples)) {
            if ($null -eq $selfId) { break }
            $row = Get-PeerRow $s $selfId
            if ($null -eq $row) { continue }
            if ([math]::Abs([double]$row.x - $TaskRoomWorldX) -gt 15.0) { continue }  # not in this room
            $wall = [double]$s.wall
            $z = [double]$row.z
            if ($wall -ge $seekerArmed -and $wall -lt $seekerBurst) {
                $blockedSamples++
                if ($z -lt $deepestBeforeZ) { $deepestBeforeZ = $z }
            } elseif ($wall -ge $seekerBurst) {
                if ($z -lt $deepestAfterZ) { $deepestAfterZ = $z }
                if ($z -le $ThroughZ) { $throughSamples++ }
            }
        }
    } else {
        Add-Failure "the seeker's own door log has no arm or no burst; the blocker check cannot be staged"
    }

    Write-Host ("        seeker in the task-room complex: $blockedSamples sample(s) during the tell " +
                "(deepest z $([math]::Round($deepestBeforeZ,2))), after the burst deepest z " +
                "$([math]::Round($deepestAfterZ,2)) (door plane z $DoorWorldZ)") -ForegroundColor DarkGray

    if ($blockedSamples -lt 3) {
        # Not a statement about the door: a control that never ran cannot be read as a control
        # that passed. TellSec is what buys this window, so if it is thin the overlay is the
        # first suspect.
        Add-Failure ("only $blockedSamples seeker sample(s) landed inside the tell window, so " +
                     "'the blocker held' was never actually measured -- raise TellSec or check " +
                     "that --startle-file reached the bots")
    } elseif ($deepestBeforeZ -lt $BlockedZ) {
        Add-Failure ("during the tell the seeker reached z $([math]::Round($deepestBeforeZ,2)), " +
                     "past the vestibule face at $BlockedZ -- the blocker was not blocking before " +
                     "the burst, so 'it walked through afterwards' proves nothing")
    }
    if ($throughSamples -lt 1) {
        Add-Failure ("after the burst the seeker never got past z $ThroughZ (deepest " +
                     "$([math]::Round($deepestAfterZ,2))) -- it could not walk from the vestibule " +
                     "into the task room, so the blocker never stopped blocking")
    }

    # ==========================================================================================
    # 3. The shove: a prop inside the burst radius moved, seen on a CLIENT
    #
    # On a client's log, never the server's: the hard half of the shove is the Resting -> Loose
    # WAKE, and a server that pushed its own copy of a Resting prop without waking it would move
    # the prop in its own log and nowhere else.
    # ==========================================================================================
    $hider = $bots | Where-Object { $_.Name -eq "DoorHider" } | Select-Object -First 1
    $hiderDoor = $doorLogs | Where-Object { $_.Label -eq "DoorHider" } | Select-Object -First 1
    $hiderBurst = $hiderDoor.Burst
    # THE WINDOW ENDS AT THE RESET, AND THAT IS NOT TIDINESS -- it is the defect the first run of
    # this suite reported as "the best a prop managed was 0 m". The reset edge calls
    # PropManager.ResetForNewPlaythrough, which puts every authored and seeded prop back at its
    # spawn transform; the bots outlive that by a dozen seconds, so a last-sample-versus-
    # first-sample comparison was measuring a crate that had been shoved across the room and then
    # tidied back to the millimetre. Reading it as "the shove did not fire" would have been a
    # whole evening on a working feature.
    $resetWall = if ($null -ne $hiderDoor.Closing) { $hiderDoor.Closing } else { [double]::MaxValue }
    $bestPropMoveM = 0.0
    $propsInRadius = 0
    if ($null -ne $hider -and $null -ne $hiderBurst) {
        # Where each prop was on the last sample before the burst...
        $before = @{}
        foreach ($s in @($hider.Samples)) {
            if ([double]$s.wall -ge $hiderBurst) { continue }
            foreach ($p in @($s.props)) {
                $before[[string]$p.id] = @{ X = [double]$p.x; Y = [double]$p.y; Z = [double]$p.z }
            }
        }
        # ...and the FURTHEST it ever got from there between the burst and the reset. The maximum
        # rather than a final position, because a crate that tumbles and rolls back near its start
        # still moved, and the assertion is about the shove rather than about where it ends up.
        $peak = @{}
        foreach ($s in @($hider.Samples)) {
            $wall = [double]$s.wall
            if ($wall -lt $hiderBurst -or $wall -ge $resetWall) { continue }
            foreach ($p in @($s.props)) {
                $key = [string]$p.id
                if (-not $before.ContainsKey($key)) { continue }
                $b0 = $before[$key]
                $d = Dist3 $b0.X $b0.Y $b0.Z ([double]$p.x) ([double]$p.y) ([double]$p.z)
                if (-not $peak.ContainsKey($key) -or $d -gt $peak[$key]) { $peak[$key] = $d }
            }
        }
        foreach ($key in @($before.Keys)) {
            $b0 = $before[$key]
            $d0 = Dist3 $b0.X $b0.Y $b0.Z $DoorwayX $DoorwayY $DoorwayZ
            if ($d0 -gt 3.0) { continue }   # outside the burst radius; not this assertion's business
            $propsInRadius++
            $moved = if ($peak.ContainsKey($key)) { [double]$peak[$key] } else { 0.0 }
            Write-Host ("        prop $key was $([math]::Round($d0,2)) m from the doorway and moved " +
                        "$([math]::Round($moved,3)) m at its furthest before the reset") -ForegroundColor DarkGray
            if ($moved -gt $bestPropMoveM) { $bestPropMoveM = $moved }
        }
        if ($resetWall -eq [double]::MaxValue) {
            Add-Failure ("the hider never logged the door closing, so the prop window had no end " +
                         "and the reset's own restore is inside the measurement")
        }
    } else {
        Add-Failure "the hider's own door log has no burst; the prop-shove check cannot be staged"
    }
    if ($propsInRadius -lt 1) {
        Add-Failure ("not one prop was inside the ${PropMovedBarM}-metre-bar's burst radius before " +
                     "the burst -- --seed-test-props did not stage this check")
    } elseif ($bestPropMoveM -le $PropMovedBarM) {
        Add-Failure ("the best a prop inside the burst radius managed was " +
                     "$([math]::Round($bestPropMoveM,3)) m (bar $PropMovedBarM m), measured on a " +
                     "CLIENT's log -- either the shove did not fire or it did not reach the peers")
    }

    # The flinch fired as a decision even though there was nothing in the hand: --round-script has
    # nobody holding anything, so the honest assertion is that the server RAN the release with a
    # count, not that a prop left a hand. A missing line means ForceDropOnBurst never executed.
    $serverDoor = $doorLogs | Where-Object { $_.Label -eq "server" } | Select-Object -First 1
    if ($serverDoor.ForceDropped -lt 0) {
        Add-Failure "the server never logged the force-drop; ForceDropOnBurst defaults on and did not run"
    }
    if ($serverDoor.Shoved -lt 1) {
        Add-Failure "the server logged $($serverDoor.Shoved) prop(s) shoved by the burst"
    }

    # ==========================================================================================
    # 4. The reset put the wall back, on every peer, after the burst
    # ==========================================================================================
    foreach ($d in $doorLogs) {
        if ($null -eq $d.Closed) {
            Add-Failure ("$($d.Label) never logged the close -- on the reset edge the leaf must " +
                         "shut and the blocker must come back, on every peer")
        } elseif ($null -ne $d.Burst -and $d.Closed -le $d.Burst) {
            Add-Failure "$($d.Label) closed the door at or before its own burst, which is not a reset"
        }
    }

    # And the round really did reach its reset edge, so "closed" is the reset rather than a door
    # that never opened.
    $serverLines = @(Get-Content $serverOut)
    $phases = @()
    foreach ($line in $serverLines) {
        if ($line -match '\[round\] phase (\w+) -> (\w+)') { $phases += "$($matches[1])->$($matches[2])" }
    }
    Write-Host "        server phases: $([string]::Join('  ', $phases))" -ForegroundColor DarkGray
    foreach ($want in @("Seeking->Together", "Tally->Holding")) {
        if ($phases -notcontains $want) {
            Add-Failure "the server never made the transition $want -- the door's own trigger never happened"
        }
    }

    Write-Host ""
    Write-Host "[5/5] MEASURED QUANTITIES (compare these across runs, not the verdict):" -ForegroundColor White
    Write-Host "  peers that burst:                               $($burstTimes.Count) of $($doorLogs.Count)"
    Write-Host "  worst |own burst - (own arm + TellSec)|:         $([math]::Round($worstGapMs,1)) ms (bar $([math]::Round($BurstGapToleranceMs,1)) ms)"
    Write-Host "  burst spread across peers:                      $([math]::Round($burstSpreadMs,1)) ms (bar $BurstSpreadToleranceMs ms)"
    Write-Host "  seeker samples inside the tell (blocked):       $blockedSamples, deepest z $([math]::Round($deepestBeforeZ,2)) (face $BlockedZ)"
    Write-Host "  seeker samples past the door after the burst:   $throughSamples, deepest z $([math]::Round($deepestAfterZ,2)) (bar $ThroughZ)"
    Write-Host "  props inside the burst radius:                  $propsInRadius, best move $([math]::Round($bestPropMoveM,3)) m (bar $PropMovedBarM m)"
    Write-Host "  props the server said it shoved:                $($serverDoor.Shoved)"
    Write-Host "  props the force-drop released:                  $($serverDoor.ForceDropped)"
}
finally {
    Stop-Procs $procs
    Exit-SuiteMutex $mutex
}

Write-Host ""
if ($script:Failures.Count -gt 0) {
    Write-Host "BURSTDOOR-TEST FAILED ($($script:Failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "BURSTDOOR-TEST OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: --startle-file reached all three processes; every peer burst the door within one snapshot interval of its own tell; the tell fired on the hider alone; the seeker was held on the vestibule side while the tell ran and walked into the task room after the burst; a Resting crate inside the burst radius moved on a CLIENT's view; and the reset closed the leaf and made the blocker solid again on every peer." -ForegroundColor Green
Write-Host ""
Write-Host "BURSTDOOR-TEST OVERALL: PASS" -ForegroundColor Green
exit 0
