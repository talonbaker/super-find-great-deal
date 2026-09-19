<#
.SYNOPSIS
    EGG-2 / Talon's easter-egg addendum §4: the night-only creature in the Bubble Test level.
    Two server runs at two frozen clock phases -- midnight and noon -- and the assertion is a
    PAIR, not a single observation.

.DESCRIPTION
    The creature is the shipped Watcher (scripts/game/watcher/), reused as-is. Nothing in this
    packet retunes it and nothing adds a third state to WatcherBrain; the night gate is a filter
    on the peer list BubbleTestWatcher hands the brain, so "day" is expressed as "no candidates"
    in the creature's own vocabulary.

    PHASE 1, midnight (--cycle-start-phase midnight --cycle-freeze). A dedicated headless server
    in --world bubbletest plus one scripted bot. Asserts:

      - the host came up:              [bubbletest.watcher] host ready
      - the gate opened:               [bubbletest.watcher] night window OPEN -- darkness=...
      - the creature actually appeared: [watcher] sighting <n> target=... stand=...
                                    and state=Watching in this level's own log line.

    PHASE 2, noon (--cycle-start-phase noon --cycle-freeze). Identical run, identical bot.
    Asserts the ABSENCE of all three creature lines -- no sighting, no state=Watching, and the
    window reported night=False throughout.

    THE ABSENCE CHECK CARRIES ITS CONTROL, which is the whole reason both phases run in one
    script. "No creature appeared at noon" is worth nothing on its own: a build where the
    creature never appears at all, a bot that never connected, a world that failed to load and a
    correctly-gated day all produce the identical empty log. Phase 1 is the positive control for
    phase 2, and this script fails if EITHER half is missing -- so a regression that silently
    kills the creature turns the day check from a pass into a fail.

    WHY A FROZEN CLOCK. The cycle is 720 s by default and the night band is a fraction of it; a
    run that merely started at night would drift into dawn mid-test and the two phases would stop
    being two phases. --cycle-freeze is server data on every peer (clients snap-correct to the
    frozen phase), which is what makes one flag enough.

    Exit 0 = PASS. No human interaction. Headless throughout -- no GPU, no display.
#>
[CmdletBinding()]
param(
    [int]$Port = 45871,
    # 40 s: the bot needs to connect (~4 s), walk (~10 s) and then simply exist while the brain
    # decides. WatcherTuning.DwellSeconds is 22, so a shorter run could end inside a sighting and
    # a longer one buys nothing -- the appearance is what is under test, not the withdrawal.
    [int]$DurationSec = 40,
    # Run-AllTests.ps1 passes this to EVERY registered suite so the marathon builds once rather
    # than seventy times. A suite whose param() block does not declare it does not merely skip the
    # optimisation -- PowerShell throws "A parameter cannot be found that matches parameter name
    # 'SkipBuild'" before the script body runs, and the runner records it as a FAIL. Measured on
    # this suite's first marathon: green standalone, red in the run, with no test having executed.
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $SkipBuild) { Reset-LogDir; Invoke-BuildAndImport }
else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

# Where the bot walks. Away from the spawn ring and out along the red arm, so the party of one is
# somewhere the standoff ring has room -- and, more to the point, so it is MOVING: exposure is
# stance x motion, and a body that never leaves its spawn scores the same either way.
$Walk = "30,-30"

function Invoke-Phase([string]$Phase, [int]$PortOffset, [string]$Tag) {
    $port = $Port + $PortOffset
    $serverOut = Join-Path $script:LogDir "$Tag.server.out.log"
    $procs = @()
    try {
        $server = Start-Godot @("--server", "--port", $port, "--world", "bubbletest",
            "--cycle-start-phase", $Phase, "--cycle-freeze") "$Tag.server"
        $procs += $server
        if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
            Write-Fail "[$Tag] server never reported listening; see $serverOut"
        }

        $bot = Start-Godot @("--bot", "--address", "127.0.0.1:$port", "--name", "EggBot",
            "--log", (Join-Path $script:LogDir "$Tag.bot.jsonl"),
            "--duration", $DurationSec, "--world", "bubbletest",
            "--goto-script", $Walk) "$Tag.bot"
        $procs += $bot
        if (-not (Wait-ForExit $bot ([int]$DurationSec + 90))) {
            Write-Fail "[$Tag] the bot never exited"
        }
        if ($bot.ExitCode -ne 0) {
            Write-Fail "[$Tag] the bot exited $($bot.ExitCode); see $script:LogDir\$Tag.bot.out.log"
        }
    }
    finally {
        Stop-Procs $procs
    }
    Start-Sleep -Seconds 1
    if (-not (Test-Path $serverOut)) { Write-Fail "[$Tag] no server log at $serverOut" }
    return @(Get-Content $serverOut)
}

function Count-Matching([string[]]$Lines, [string]$Pattern) {
    return @($Lines | Where-Object { $_ -match $Pattern }).Count
}

# ---------------------------------------------------------------------------------------------
Write-Host "[1/3] midnight -- the creature must appear..." -ForegroundColor Cyan
$night = Invoke-Phase "midnight" 0 "watchergate.night"

$nightHost     = Count-Matching $night "\[bubbletest\.watcher\] host ready"
$nightOpen     = Count-Matching $night "\[bubbletest\.watcher\] night window OPEN"
$nightSighting = Count-Matching $night "\[watcher\] sighting "
$nightWatching = Count-Matching $night "state=Watching"

Write-Host ("      host ready={0}  window OPEN={1}  sightings={2}  state=Watching lines={3}" -f `
    $nightHost, $nightOpen, $nightSighting, $nightWatching)

if ($nightHost -lt 1)     { Write-Fail "midnight: the watcher host never came up in the bubble test world" }
if ($nightOpen -lt 1)     { Write-Fail "midnight: the night window never opened -- the gate is reading the clock wrong" }
if ($nightSighting -lt 1) { Write-Fail "midnight: the creature never appeared. The gate is open and nothing came, which is the failure this whole feature is." }
if ($nightWatching -lt 1) { Write-Fail "midnight: [watcher] sighting fired but this level's own state line never reported Watching" }

# ---------------------------------------------------------------------------------------------
Write-Host "[2/3] noon -- the creature must NOT appear..." -ForegroundColor Cyan
$day = Invoke-Phase "noon" 2 "watchergate.day"

$dayHost     = Count-Matching $day "\[bubbletest\.watcher\] host ready"
$dayOpen     = Count-Matching $day "\[bubbletest\.watcher\] night window OPEN"
$daySighting = Count-Matching $day "\[watcher\] sighting "
$dayWatching = Count-Matching $day "state=Watching"
$dayHeart    = Count-Matching $day "\[bubbletest\.watcher\] heartbeat .*night=False"

Write-Host ("      host ready={0}  window OPEN={1}  sightings={2}  state=Watching lines={3}  night=False heartbeats={4}" -f `
    $dayHost, $dayOpen, $daySighting, $dayWatching, $dayHeart)

if ($dayHost -lt 1)   { Write-Fail "noon: the watcher host never came up -- the absence below would prove nothing" }
if ($dayHeart -lt 1)  { Write-Fail "noon: no heartbeat reported night=False -- the gate never evaluated, so its silence is not evidence" }
if ($dayOpen -ne 0)   { Write-Fail "noon: the night window OPENED at midday ($dayOpen time(s))" }
if ($daySighting -ne 0) { Write-Fail "noon: the creature appeared $daySighting time(s) in broad daylight" }
if ($dayWatching -ne 0) { Write-Fail "noon: state=Watching reported $dayWatching time(s) at midday" }

# ---------------------------------------------------------------------------------------------
Write-Host "[3/3] the pair..." -ForegroundColor Cyan
# Stated explicitly rather than left implicit in the two blocks above: the day result is only
# evidence BECAUSE the night result exists. Same tree, same world, same bot, same walk -- one
# flag apart.
Write-Host ("      night: {0} sighting(s)   day: {1} sighting(s)" -f $nightSighting, $daySighting)
if ($nightSighting -lt 1 -or $daySighting -ne 0) {
    Write-Fail "the pair did not hold"
}

Write-Host ""
Write-Host "PASS: the creature appears at midnight and not at noon (control: $nightSighting night sighting(s))." -ForegroundColor Green
exit 0
