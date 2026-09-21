<#
.SYNOPSIS
    HANDS-1's gate: the hand is where the grab point is, a crate takes two hands and a can takes
    one, and pressing a button moves a hand.

.DESCRIPTION
    Talon, 2026-09-20: "I think they should have hands. Follow the convention of R.E.P.O. and
    Lethal Company but in a slightly different way, because this game is about manipulating
    objects." The design that answers it (PROGRAM-2026-09-20-RIDE-1 section 2) is that the hand
    shows the GRAB POINT -- after FEEL-1 the object is held where you took hold of it, and that is
    the one piece of information this game runs on.

    udp/7917, ASSIGNED by the orchestrator in the dispatch and never computed from a snapshot of
    tests/. That rule is now written in .claude/rules/test-suite.md under FEEL-1's own section, and
    it is there because this repo has paid for the other way four times (three lanes on 7896, two
    on 7899, INT-1's 7910 taken while three sentences said it was free, and FEEL-1's 7912 held by
    a live SOLO-1 lane in another worktree).

    THIS IS THE SECOND SUITE IN THE REGISTRY THAT NEEDS A RENDERER, and for FP-1's own reason one
    system over: the subject is what the player's own lens draws in front of them. The hands hang
    off that lens and exist only where it exists, so there is no headless way to measure them. A
    window opens, runs for about twenty seconds and closes itself; if the client never prints its
    SUMMARY line, a missing display/GPU is the first thing to check.

    STAGING. One headless server in the search room with two props seeded in the -X end walkway --
    a Can and a Crate, chosen because they sit either side of the two-hand threshold with room to
    spare (longest axis 0.12 m against 0.44 m) -- plus one windowed --hands-selftest bot walked to
    a standing spot 1.40 m from the ConfirmButton by its own brain, so the can, the crate and a
    pressable are all inside SandboxAvatar.PickupRadius / RoundButton.PressRadius from one place
    and no beat in the run depends on a walk that can go wrong. HOLD-1's rule ("walk a bot along
    its own walkway") is why the walk leaves the central walkway into the open -X end rather than
    crossing an aisle, and HandsProbeIntentSource exists because a 1.5 m waypoint latch cannot
    stand a bot inside a 1.6 m press radius round a button 1.15 m up a wall.

    WHAT IS ASSERTED, and why each one is not vacuous:

      1. THE HAND IS AT THE GRAB POINT, every frame, to 1 cm -- measured ACROSS the line of sight.
         Across rather than through, and that is the measurement's honesty rather than a
         weakening: FEEL-1 records the grab point as the foot of the perpendicular from the prop's
         centre onto the view ray, so for any well-aimed grab it is INSIDE the prop (at the centre
         of a crate, in the ordinary case) and the hand is deliberately pushed out along the same
         line of sight to the surface, because a hand drawn inside a crate is a hand nobody can
         see. A raw 3-D distance would therefore report the prop's own radius and measure nothing.
         Sideways is where an attach bug shows up, and sideways is what the plant moves.

      2. AND ON THE OBJECT: no hand more than 5 cm outside the prop's own bounding sphere.
         The other half of "the hand is on the thing" -- assertion 1 alone is satisfied by a hand
         floating a metre in front of the crate.

      3. THE TWO-HAND RULE, from the numbers that decided it. The probe logs one HOLDROW per hold
         carrying the prop's bounding radius, its mass and how many hands went on it; this runner
         asserts that the SMALLER prop got one and the LARGER got two, rather than trusting the
         count the code itself produced.

      4. A PRESS MOVES A HAND. The poke is hooked at RoundControls.ClientRequestPress -- the one
         client-side point a human's click on a RoundButton and a suite's --press both go through
         -- so a press that moved no hand means the hook is gone. The round REFUSES this Confirm
         (wrong phase) and that is fine: the poke is the hand reaching, not the round's answer.

      5. ENOUGH SAMPLES TO MEAN ANYTHING. Under 60 held frames, or fewer than two distinct holds,
         is a STAGING failure and says so -- a green bar over a run where nothing was ever held
         proves nothing, which is this repo's oldest lesson about absence.

    PROVED ABLE TO FAIL, wired in rather than done by hand: -ProveItCanFail runs the same session
    with --hands-plant-offset, which pushes a deliberate sideways error into every attached hand.
    Assertion 1 must go red and the run must still stage (the holds still happen), or the bar is
    not measuring what it claims. Not run by the marathon: it is a second windowed session.

    GATED ON THE SUMMARY LINE, THEN ON THE EXIT CODE. Same reasoning Run-FirstPersonTest.ps1 and
    Run-SupermarketWorldTest.ps1 spell out: a process that dies before it reports exits non-zero
    for reasons that have nothing to do with the subject, and "never reported" is a different
    problem from "reported a failure".

        [hands-selftest] SUMMARY failures=<n> result=PASS|FAIL

    Exit 0 = PASS. No human interaction; a window opens and closes on its own.

.PARAMETER ProveItCanFail
    Run the positive control instead of the gate: plant the attach offset wrong and require the
    1 cm bar to go RED. Exits 0 when the planted run failed for the right reason.

.PARAMETER SkipBuild
    Reuse the existing build and import. For re-running a red standalone.
#>
[CmdletBinding()]
param(
    # ASSIGNED, never computed. See the header.
    [int]$Port = 7917,
    # 30 s, not 22. The probe's fourteen-second script runs from the frame it SETTLES at its
    # standing spot, and its settle deadline is 12 s -- measured, after a loaded run caught the
    # bot still walking at 1.56 m/s on an absolute 4.5 s mark and reported it as a failure of the
    # fixture's props. A duration derived from the schedule rather than typed beside it is FIX-1's
    # rule in this file's own rules directory.
    [double]$DurationSec = 30,
    [double]$PlantOffsetM = 0.08,
    [switch]$ProveItCanFail,
    [string]$CaptureDir = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# The search room is instanced at world x = +40, so its local (-6.94, 1.15, -3) ConfirmButton is
# at world (33.06, 1.15, -3) and SearchSpawn_0 at (35.50, 1.10, 0).
#
# The standing spot: (33.85, -3.0), walked to by HandsProbeIntentSource, which stops within 15 cm
# of it rather than at ScriptedGotoIntentSource's 1.5 m waypoint latch (that latch cannot put a
# bot inside a wall button's press radius -- see the brain's own doc). It is 0.79 m from the
# button in x and 1.40 m from it in 3D, inside RoundButton.PressRadius (1.6 m) and well inside
# the server's 2.35 m; 0.73 m clear of the -X wall's inner face against a 0.36 m capsule; and in
# the OPEN -X end walkway, where the aisles (world x in [35.4, 44.6]) cannot get between the bot
# and anything it wants (HOLD-1's rule: walk a bot along its own walkway, never diagonally
# through a room whose only cross-aisles are at its ends).
$StandX = 33.85
$StandZ = -3.0

# The two props, both inside SandboxAvatar.PickupRadius (1.5 m) of that spot, resting on the
# floor at their own half-heights: 0.75 m and 0.98 m away in 3D, so REVIEW-1's client-side 1.5 m
# bar -- the smaller of the two reach bars, and the one that actually binds -- has half a metre
# of slack. They are a Can and a Crate because those sit either side of the two-hand threshold
# with room to spare: longest axis 0.12 m against 0.44 m, against a 0.20 m bar.
# ONE definition of each spot: the server seeds there and the probe is told to grab whatever is
# nearest there. Two copies of a coordinate is how a fixture silently stops being about the thing
# it was written for.
$CanAt = "34.6,0.06,-3.0"
$CrateAt = "34.5,0.22,-3.7"
$CanSeed = "$CanAt,Can"
$CrateSeed = "$CrateAt,Crate"

$title = if ($ProveItCanFail) { "HANDS-1: the positive control (planted attach offset)" }
         else { "HANDS-1: the hand is at the grab point" }
Write-Host "=== $title ===" -ForegroundColor White

if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

if ([string]::IsNullOrWhiteSpace($CaptureDir)) {
    $CaptureDir = Join-Path $script:LogDir "hands-capture"
}
if (Test-Path $CaptureDir) { Remove-Item -Recurse -Force $CaptureDir }
New-Item -ItemType Directory -Path $CaptureDir -Force | Out-Null

$procs = @()
$clientOut = Join-Path $script:LogDir "hands-client.out.log"
try {
    Write-Host "[1/3] launching dedicated server (supermarket, search room) on udp/$Port..." -ForegroundColor Cyan
    $serverOut = Join-Path $script:LogDir "hands-server.out.log"
    $server = Start-Process -FilePath $script:GodotExe `
        -ArgumentList @("--headless", "--path", $script:Root, "--",
            "--server", "--port", $Port, "--world", "supermarket",
            "--spawn-room", "search", "--spawn-index", "HandsProbe=0",
            "--seed-test-props", "$CanSeed;$CrateSeed") `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "hands-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle   # cache the handle so ExitCode is readable after exit (PS 5.1 quirk)
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 40)) {
        Write-Fail "the server never reported listening on udp/$Port within 40s; see $serverOut"
    }
    Write-Host "        server up (pid $($server.Id))"
    if (-not (Select-String -Path $serverOut -Pattern "seeded 2 test prop" -Quiet)) {
        Write-Fail "STAGING: --seed-test-props did not seed both props; see $serverOut"
    }

    # A WINDOWED client -- no --headless. Engine flags before the bare --, game flags after; a
    # game flag on the wrong side is swallowed and looks exactly like a hang.
    Write-Host "[2/3] launching the windowed hands probe for ${DurationSec}s..." -ForegroundColor Cyan
    $clientArgs = @("--path", $script:Root, "--",
        "--bot", "--address", "127.0.0.1:$Port", "--name", "HandsProbe",
        "--duration", $DurationSec, "--world", "supermarket", "--windowed",
        "--hands-selftest", "--hands-capture-dir", $CaptureDir,
        "--hands-stand", "$StandX,$StandZ")
    # The two objects this fixture is ABOUT, named by position. Not "the smallest and largest
    # free carryable in reach": measured on this suite's first real run, that picked up
    # Stock/Bin_0 -- a 6 kg floor bin, a perfectly legal carryable -- at 1.37 m instead of the
    # crate seeded at 0.98 m.
    $clientArgs += @("--hands-props", "$CanAt;$CrateAt")
    if ($ProveItCanFail) { $clientArgs += @("--hands-plant-offset", $PlantOffsetM) }
    $client = Start-Process -FilePath $script:GodotExe -ArgumentList $clientArgs `
        -RedirectStandardOutput $clientOut `
        -RedirectStandardError (Join-Path $script:LogDir "hands-client.err.log") `
        -PassThru -NoNewWindow
    $null = $client.Handle
    $procs += $client

    if (-not $client.WaitForExit([int](($DurationSec + 60) * 1000))) {
        Write-Fail "the hands probe did not exit within $([int]($DurationSec + 60))s; see $clientOut"
    }
} finally {
    Stop-Procs $procs
}

Write-Host "[3/3] reading the probe's own log..." -ForegroundColor Cyan
if (-not (Test-Path $clientOut)) { Write-Fail "no client log at $clientOut" }

$summary = Select-String -Path $clientOut -Pattern "\[hands-selftest\] SUMMARY failures=(\d+) result=(PASS|FAIL)" |
    Select-Object -Last 1
if (-not $summary) {
    Write-Fail "the probe never printed its SUMMARY line. A missing display/GPU is the first thing to check (this suite renders). See $clientOut"
}
$probeFailures = [int]$summary.Matches[0].Groups[1].Value
$probeResult = $summary.Matches[0].Groups[2].Value

$reading = Select-String -Path $clientOut -Pattern "\[hands-selftest\] samples=(\d+) worstLateralM=([\d\.]+) worstOffSurfaceM=([\-\d\.]+) holds=(\d+) pokes=(\d+) noHandFrames=(\d+) reachFrames=(\d+)" |
    Select-Object -Last 1
if (-not $reading) { Write-Fail "the probe printed a SUMMARY but no reading line; see $clientOut" }
$g = $reading.Matches[0].Groups
$samples = [int]$g[1].Value
$worstLateral = [double]$g[2].Value
$worstOffSurface = [double]$g[3].Value
$holdCount = [int]$g[4].Value
$pokes = [int]$g[5].Value
$noHand = [int]$g[6].Value
$reachFrames = [int]$g[7].Value

Write-Host ("        hand-to-grab-point: worst {0:F4} m across the view over {1} held frame(s) (bar 0.0100)" -f $worstLateral, $samples) -ForegroundColor DarkGray
Write-Host ("        off the surface:    worst {0:F4} m outside the prop's bounding sphere (bar 0.0500)" -f $worstOffSurface) -ForegroundColor DarkGray
Write-Host ("        holds: $holdCount   pokes: $pokes   frames holding with no hand on it: $noHand   frames still reaching (not judged): $reachFrames") -ForegroundColor DarkGray
Write-Host "HANDSEVIDENCE worstLateralM=$worstLateral worstOffSurfaceM=$worstOffSurface samples=$samples holds=$holdCount pokes=$pokes"

# The HOLDROW lines: one per distinct hold, carrying the numbers the two-hand rule was asked
# about. Parsed here rather than asserted in the probe, so the expectation ("the smaller prop gets
# one hand, the larger gets two") is stated by something other than the code under test.
$rows = @()
foreach ($m in (Select-String -Path $clientOut -Pattern "\[hands-selftest\] HOLDROW prop=(\d+) r=([\d\.]+) mass=([\d\.]+) hands=(\d+)")) {
    $rows += [pscustomobject]@{
        Prop  = [int]$m.Matches[0].Groups[1].Value
        R     = [double]$m.Matches[0].Groups[2].Value
        Mass  = [double]$m.Matches[0].Groups[3].Value
        Hands = [int]$m.Matches[0].Groups[4].Value
    }
}
foreach ($r in $rows) {
    Write-Host ("        held prop {0}: bounding radius {1:F3} m, {2:F2} kg -> {3} hand(s)" -f $r.Prop, $r.R, $r.Mass, $r.Hands) -ForegroundColor DarkGray
}

$failures = New-Object System.Collections.Generic.List[string]

if ($rows.Count -lt 2) {
    $failures.Add("STAGING: only $($rows.Count) distinct hold(s) were logged; the one-hand and two-hand beats were not both staged, so the two-hand rule has nothing to be judged on")
} else {
    # In FIXTURE ORDER: the probe grabs the can first and the crate second, and --hands-props
    # named both spots, so the rows come out in that order. Sorting by radius here would let a
    # run that grabbed the wrong two objects still line up and pass.
    $small = $rows[0]
    $large = $rows[1]
    if ($small.Hands -ne 1) {
        $failures.Add(("THE TWO-HAND RULE: the SMALLER prop ({0}, bounding radius {1:F3} m, {2:F2} kg) was held in {3} hand(s); a can is one-handed" -f $small.Prop, $small.R, $small.Mass, $small.Hands))
    }
    if ($large.Hands -ne 2) {
        $failures.Add(("THE TWO-HAND RULE: the LARGER prop ({0}, bounding radius {1:F3} m, {2:F2} kg) was held in {3} hand(s); a crate takes two" -f $large.Prop, $large.R, $large.Mass, $large.Hands))
    }
    if ($small.R -ge $large.R) {
        $failures.Add("STAGING: the two holds were the same size ($($small.R) m), so the two-hand rule was never actually asked a question")
    }
}

if ($pokes -lt 1) {
    $failures.Add("A PRESS MOVED NO HAND: the probe pressed Confirm and FirstPersonHands.Poke never fired. The hook is RoundControls.ClientRequestPress -- the one client-side point every press goes through.")
}

# The captures, which are what Talon judges the concept from. A frame that is a few hundred bytes
# is a photograph of a flat clear colour (CELEBRATE-1's entry in the rules file), so size is
# checked as well as existence.
$wanted = @("idle", "reaching", "one-hand-can", "break-hold-return", "two-hand-crate", "poke")
foreach ($name in $wanted) {
    $png = Join-Path $CaptureDir "hands-$name.png"
    if (-not (Test-Path $png)) {
        $failures.Add("CAPTURE: '$name' was never written to $CaptureDir")
        continue
    }
    $kb = [int]((Get-Item $png).Length / 1024)
    Write-Host ("        capture {0,-18} {1} KB" -f $name, $kb) -ForegroundColor DarkGray
    if ($kb -lt 8) {
        $failures.Add("CAPTURE: '$name' is only $kb KB -- that is a photograph of a flat clear colour, not of a room")
    }
}

# --- the two modes ----------------------------------------------------------------------------
if ($ProveItCanFail) {
    Write-Host ""
    Write-Host "POSITIVE CONTROL: --hands-plant-offset $PlantOffsetM m was planted." -ForegroundColor Yellow
    # The plant must be CAUGHT, and the run must still have staged -- a control that failed because
    # nothing was ever held proves the opposite of what it is for.
    if ($rows.Count -lt 2 -or $samples -lt 60) {
        Write-Host "CONTROL INCONCLUSIVE: the planted run did not stage ($($rows.Count) hold(s), $samples sample(s)). Re-run; a control that fails at staging says nothing about the bar." -ForegroundColor Red
        Write-Host "HANDS-SMOKE OVERALL: FAIL" -ForegroundColor Red
        exit 1
    }
    if ($probeResult -eq "FAIL" -and $worstLateral -gt 0.01) {
        Write-Host ("PASS: the planted {0} m offset was caught -- worst hand-to-grab-point {1:F4} m against the 0.0100 m bar, {2} probe failure(s), and the run still staged {3} holds over {4} frames." -f $PlantOffsetM, $worstLateral, $probeFailures, $rows.Count, $samples) -ForegroundColor Green
        Write-Host "HANDS-SMOKE OVERALL: PASS" -ForegroundColor Green
        exit 0
    }
    Write-Host ("CONTROL FAILED: a planted {0} m offset left the bar GREEN (worst {1:F4} m, probe result {2}). The 1 cm assertion is not measuring what it claims." -f $PlantOffsetM, $worstLateral, $probeResult) -ForegroundColor Red
    Write-Host "HANDS-SMOKE OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

if ($probeResult -ne "PASS") {
    foreach ($m in (Select-String -Path $clientOut -Pattern "\[hands-selftest\] FAILURE (.+)$")) {
        $failures.Add("PROBE: " + $m.Matches[0].Groups[1].Value)
    }
    if ($probeFailures -gt 0 -and $failures.Count -eq 0) {
        $failures.Add("PROBE: reported $probeFailures failure(s) but printed no FAILURE line; see $clientOut")
    }
}

if ($client.ExitCode -ne 0) {
    # BASE-1/INT-1's teardown rule: a bot that printed its completion line and then died on the way
    # out is a finalizer artifact, not a failure of the subject. Say which this was.
    $done = Select-String -Path $clientOut -Pattern "\[bot\] HandsProbe done" -Quiet
    if ($done) {
        Write-Host "NOTE: the probe exited $($client.ExitCode) AFTER printing its completion line -- a teardown artifact (see .claude/rules/test-suite.md). Not treated as a failure of the hands." -ForegroundColor Yellow
    } else {
        $failures.Add("the probe exited $($client.ExitCode) WITHOUT printing its completion line -- a real crash, not teardown; see $clientOut and the .err.log beside it")
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "HANDS-SMOKE FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "HANDS-SMOKE OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: the hand tracked the grab point to $([math]::Round($worstLateral * 100, 2)) cm across $samples frames, the crate took two hands and the can took one, and the press moved a hand." -ForegroundColor Green
Write-Host "        captures in $CaptureDir"
Write-Host ""
Write-Host "HANDS-SMOKE OVERALL: PASS" -ForegroundColor Green
exit 0
