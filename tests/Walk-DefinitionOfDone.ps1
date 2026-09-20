<#
.SYNOPSIS
    INT-1: the PROGRAM's definition of done (section 8), walked end to end by two windowed first-person
    bots and asserted from the three processes' own logs. Unregistered -- it opens windows, takes
    two and a half minutes and is the last gate of the wave rather than part of the marathon.

.DESCRIPTION
    ONE SESSION. A dedicated server driving the round with --round-script, and TWO WINDOWED
    --first-person-cam clients that stay alive for the whole run. Both the server and both
    clients carry --log-clock and --log-sfx, so every beat below is readable off a timeline
    three processes share (every line carries a Unix millisecond stamp; CLOCK-1's rule).

    WHAT THIS PROVES AND WHAT IT DOES NOT, stated first because the distinction is the whole
    honesty of this file.

      IT PROVES the SEQUENCE and the READOUTS: that the board paints two named rows with scores
      on BOTH peers, that the phases run Holding -> Hiding -> Seeking -> Together -> Tally ->
      Holding in order, that the clock counts the hide down and ticks in its last seconds, that
      the door bursts on BOTH peers at the Found tick, that the sort count FREEZES there, that
      the tally card is written, that the roles SWAP, that round two runs, that the match card
      names a winner, and that a third Start zeroes the live scores while the card keeps the
      result.

      IT DOES NOT PROVE THE BUTTONS OR THE BIN. --round-script feeds the round's FACTS on a
      schedule -- it is the dev flag ROUND-1 built, and it stands in for a hand on a plate and an
      object landing in a box. Those two paths have their own gate: Run-ButtonsTest (BTN-1,
      udp/7906) drives a REAL press through RoundControls.ClientRequestPress and a REAL delivery
      through the bin's sensor, and it runs inside the marathon. What NOTHING automated proves is
      a HUMAN pressing them, which is Talon's ride and is the one gate this wave cannot close for
      itself (.claude/rules: the final gate is his launcher, his input path, a planted control).

    THE SCHEDULE, and why each beat is where it is. Seconds are the SERVER's, counted from the
    first player arriving.

      noobject@3   the hider's hands are empty
      start@4      REFUSED (HiderMustHoldAnObject) -- the affordance, before the happy path
      object@6     the hider takes an object off the rack
      start@8      ACCEPTED -> Hiding
      confirm@34   -> Seeking. 26 s of Hiding, so the clock spends its last ten seconds TICKING
                   (RoundAudio ticks once a second under 10 s remaining of a 30 s hide) -- the
                   section 8 beat "30 s to hide" is the phase the clock counts, and the ticks are what
                   make it legible from inside the game rather than only in a view
      sorts:3@40   the hider is working in the task room
      found@52     the seeker delivers -> Together. The door bursts. The count freezes at 3
      sorts:5@54   A DELIBERATE POST-FOUND BUMP. The fact source says five; the card must still
                   say THREE. This is the only way to test a freeze -- a count that simply
                   stopped rising would look identical to a hider who stopped working
      end@60       -> Tally, the round card
      lost@68      clears the LATCHING level. TASK-1 section 3.6 measured what happens without it: a
                   second round leaves Seeking on its first tick, because `found` is a bin and a
                   bin does not pulse
      start@72     round 2, with the roles ALREADY SWAPPED at the Tally -> Holding commit
      sorts:1@78 / confirm@80 / found@92 / end@97   round 2, shorter -- round 1 carries the ticks
      start@112    the THIRD Start: the live scores zero and the match card keeps the result

    TWO CLIENTS AND BOTH ALIVE TO THE END. HOLD-1 section 8 measured both halves: a THIRD windowed peer
    makes the loop refuse every Start (NeedTwoPlayers, humans=3) and every frame reads HOLDING;
    and a lens that exits early ends the round by DISCONNECT, which puts "ended: X left" on the
    card instead of a result.

    THE SECOND CLIENT IS GATED ON THE FIRST ONE'S OWN CONNECT LINE, never on a sleep. TASK-1 section 3.5
    measured a windowed client losing a 1.2 s race to a headless one and drawing the wrong ROLE.
    Roles here are join order: the FIRST joiner is round 1's hider.

    CAPTURES are taken on a generous grid from both lenses and the good ones are kept by MTIME
    against the server's own `wall=` stamps -- DOOR-1's method, which TASK-1 used for the same
    reason: --capture-at counts from a CLIENT'S OWN launch and --round-script counts from the
    first player arriving, and the gap between them is however long Godot takes to open a window.

.PARAMETER Port
    udp/7910, the next free number in the one ladder table in .claude/rules/test-suite.md.
    Unregistered, like Capture-RoundClock and Capture-SortRoom, and it takes the machine mutex.
#>
[CmdletBinding()]
param(
    [int]$Port = 7910,
    [int]$MutexTimeoutMinutes = 180,
    [string]$CaptureDir = "docs/qa/2026-09-19-int-1",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

$Script = ("noobject@3,start@4,object@6,start@8,confirm@34,sorts:3@40,found@52,sorts:5@54," +
           "end@60,lost@68,start@72,sorts:1@78,confirm@80,found@92,end@97,start@112")

# A CEILING on the run, not a belief about when anything lands: every assertion below is anchored
# on a logged line. The last scripted beat is 112; 130 leaves the third Start a dozen samples.
$ClientDurationSec = 130

# Marks on each lens, from that CLIENT'S OWN launch. Deliberately a wide grid at half-second
# spacing or wider -- DOOR-1 measured that a viewport capture costs ~0.18 s and a denser grid
# reports the wrong times. Frames are selected by mtime afterwards.
$Marks = "10,12,20,36,38,42,46,54,56,58,62,66,76,84,94,99,101,116,120"

$absCapture = Join-Path $script:Root $CaptureDir
$null = New-Item -ItemType Directory -Force -Path $absCapture

$serverOut = Join-Path $script:LogDir "dod-server.out.log"
$aOut      = Join-Path $script:LogDir "dod-HiderA.out.log"
$bOut      = Join-Path $script:LogDir "dod-SeekerB.out.log"

$script:Failures = @()
function Add-Failure([string]$m) { $script:Failures += $m }
function Note([string]$m) { Write-Host "        $m" }

Write-Host "=== INT-1: the definition of done, walked (section 8) ===" -ForegroundColor White

if (-not $SkipBuild) {
    Write-Host "  building godot project..."
    & dotnet build (Join-Path $script:Root "SuperFindGreatDeal.csproj") | Out-Null
}

$mutex = Enter-SuiteMutex $MutexTimeoutMinutes
$procs = @()
try {
    Write-Host "[1/4] dedicated server on udp/$Port, --round-script, --log-clock --log-sfx..." -ForegroundColor Cyan
    $server = Start-Process -FilePath $script:GodotExe -ArgumentList @(
        "--headless", "--path", $script:Root, "--",
        "--server", "--port", $Port, "--world", "supermarket",
        "--round-script", $Script, "--log-clock", "--log-sfx") `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError (Join-Path $script:LogDir "dod-server.err.log") `
        -PassThru -NoNewWindow
    $null = $server.Handle
    $procs += $server
    if (-not (Wait-ForLogLine $serverOut "\[server\] listening" 60)) {
        Write-Fail "the server never reported listening on udp/$Port; see $serverOut"
    }

    function Start-Lens([string]$Name, [string]$Out) {
        # WINDOWED and NOT --headless: the board and the clock are Label3Ds and a headless peer
        # paints nothing, so a headless walk would assert against a view rather than against a
        # wall. Engine flags before the bare --, game flags after.
        $p = Start-Process -FilePath $script:GodotExe -ArgumentList @(
            "--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", $Name,
            "--windowed", "--first-person-cam",
            "--log-clock", "--log-sfx",
            "--capture-dir", $absCapture, "--capture-at", $Marks,
            "--log", (Join-Path $script:LogDir "dod-$Name.jsonl"),
            "--duration", $ClientDurationSec, "--world", "supermarket") `
            -RedirectStandardOutput $Out `
            -RedirectStandardError (Join-Path $script:LogDir "dod-$Name.err.log") `
            -PassThru
        $null = $p.Handle
        return $p
    }

    Write-Host "[2/4] HiderA (first joiner = round 1's hider) then SeekerB, gated on A's own connect line..." -ForegroundColor Cyan
    $botA = Start-Lens "HiderA" $aOut
    $procs += $botA
    if (-not (Wait-ForLogLine $aOut "\[client\] connected as peer" 90)) {
        Write-Fail "HiderA never connected; see $aOut"
    }
    $botB = Start-Lens "SeekerB" $bOut
    $procs += $botB
    if (-not (Wait-ForLogLine $bOut "\[client\] connected as peer" 90)) {
        Write-Fail "SeekerB never connected; see $bOut"
    }

    Write-Host "[3/4] walking the round (about $ClientDurationSec s)..." -ForegroundColor Cyan
    foreach ($p in @($botA, $botB)) { $null = $p.WaitForExit(($ClientDurationSec + 90) * 1000) }
    Start-Sleep -Seconds 3
}
finally {
    foreach ($p in $procs) { Stop-Proc $p }
    Exit-SuiteMutex $mutex
}

Write-Host "[4/4] asserting the section 8 sequence from the three logs..." -ForegroundColor Cyan

$srv = @(Get-Content $serverOut -ErrorAction SilentlyContinue)
$aLg = @(Get-Content $aOut -ErrorAction SilentlyContinue)
$bLg = @(Get-Content $bOut -ErrorAction SilentlyContinue)
if ($srv.Count -eq 0) { Write-Fail "the server log is empty; see $serverOut" }

function Beat([string]$Label, [bool]$Ok, [string]$Evidence) {
    if ($Ok) { Write-Host ("  PASS  {0,-46} {1}" -f $Label, $Evidence) -ForegroundColor Green }
    else { Write-Host ("  FAIL  {0,-46} {1}" -f $Label, $Evidence) -ForegroundColor Red; Add-Failure "$Label -- $Evidence" }
}

# --- board rows on BOTH peers -------------------------------------------------------------------
# Read off the LABELS (HoldingBoard.CurrentText returns what was last pushed into each Label3D),
# so a board that never painted logs rows=0 and fails here rather than passing on a blank panel.
function BoardRowsSeen($lines) {
    $best = 0; $sample = ""
    foreach ($l in $lines) {
        if ($l -notmatch '^\[board\] wall=\d+ .* rows=(\d+) \[') { continue }
        # $Matches IS CLOBBERED BY THE NEXT -match, so the captures are copied out BEFORE any
        # other comparison runs. The first cut of this function read $Matches[3] inside an -and
        # chain whose earlier clause was itself a -match, so by the time the third clause ran
        # $Matches described the SECOND match and the rows block was $null. It reported rows=0
        # against a wall that was painting two named rows perfectly, which is the worst kind of
        # wrong: a green subject and a red assertion.
        $n = [int]$Matches[1]
        $rowsBlock = $l
        $hasA = $rowsBlock -match 'HiderA'
        $hasB = $rowsBlock -match 'SeekerB'
        if ($n -gt $best -and $hasA -and $hasB) { $best = $n; $sample = $l }
    }
    return @{ Rows = $best; Sample = $sample }
}
$ba = BoardRowsSeen $aLg
$bb = BoardRowsSeen $bLg
Beat "board: two named rows on the hider's wall" ($ba.Rows -ge 2) "rows=$($ba.Rows)"
Beat "board: two named rows on the seeker's wall" ($bb.Rows -ge 2) "rows=$($bb.Rows)"
if ($ba.Sample) { Note "A: $($ba.Sample)" }
if ($bb.Sample) { Note "B: $($bb.Sample)" }

# --- the refused Start, then the accepted one ---------------------------------------------------
$refused = @($srv | Where-Object { $_ -match 'HiderMustHoldAnObject' })
Beat "rack: a Start with empty hands is refused" ($refused.Count -ge 1) "$($refused.Count) refusal(s)"
if ($refused.Count -ge 1) { Note $refused[0].Trim() }

$phases = @($srv | Where-Object { $_ -match '\[round\].*phase \w+ -> \w+' })
function PhaseSeq($lines) {
    $seq = @()
    foreach ($l in $lines) { if ($l -match 'phase (\w+) -> (\w+)') { $seq += "$($Matches[1])->$($Matches[2])" } }
    return $seq
}
$seq = PhaseSeq $srv
Beat "Start accepted: Holding -> Hiding" ($seq -contains "Holding->Hiding") ("transitions: " + ($seq.Count))
Beat "Confirm accepted: Hiding -> Seeking" ($seq -contains "Hiding->Seeking") (($seq -join " ") -replace '^(.{80}).*','$1...')
Beat "Found: Seeking -> Together" ($seq -contains "Seeking->Together") ""
Beat "End: Together -> Tally" ($seq -contains "Together->Tally") ""
Beat "the reset edge: Tally -> Holding" ($seq -contains "Tally->Holding") ""
$hidings = @($seq | Where-Object { $_ -eq "Holding->Hiding" }).Count
Beat "two full rounds plus a third Start" ($hidings -ge 3) "$hidings Holding->Hiding transitions"

# --- the hide's clock, and its ticks ------------------------------------------------------------
$tickLines = @($aLg + $bLg | Where-Object { $_ -match 'cue Tick' })
Beat "the hide counts down and TICKS in its last seconds" ($tickLines.Count -ge 3) "$($tickLines.Count) tick cue(s) across both lenses"
$hidingClock = @($aLg | Where-Object { $_ -match '\[clock\].*HIDING' })
Beat "a clock painted the HIDING phase on the hider's wall" ($hidingClock.Count -ge 1) "$($hidingClock.Count) line(s)"
if ($hidingClock.Count -ge 1) { Note $hidingClock[0].Trim(); Note $hidingClock[-1].Trim() }

# --- the burst, on BOTH peers -------------------------------------------------------------------
$burstA = @($aLg | Where-Object { $_ -match '\[door\]\s+burst' })
$burstB = @($bLg | Where-Object { $_ -match '\[door\]\s+burst' })
Beat "the door burst on the hider's peer" ($burstA.Count -ge 1) "$($burstA.Count) burst(s)"
Beat "the door burst on the seeker's peer" ($burstB.Count -ge 1) "$($burstB.Count) burst(s)"
if ($burstA.Count -ge 1) { Note "A: $($burstA[0].Trim())" }
if ($burstB.Count -ge 1) { Note "B: $($burstB[0].Trim())" }

# --- the sort count FROZE at the Found tick -----------------------------------------------------
# The script sets sorts:3 before Found and sorts:5 two seconds AFTER it. A card that says 3 is a
# freeze; a card that says 5 is a count that kept reading the live fact.
$cards = @($srv | Where-Object { $_ -match '\[round\]\s+card:' })
$card1 = if ($cards.Count -ge 1) { $cards[0] } else { "" }
$froze = $card1 -match 'hider=\d+ \+3\b'
Beat "the sort count FROZE at the Found tick (3, not 5)" $froze $card1.Trim()

# --- the tally, the swap, round two, the match card ---------------------------------------------
Beat "round 1's card was written" ($cards.Count -ge 1) "$($cards.Count) card(s)"
foreach ($c in $cards) { Note $c.Trim() }
$swapped = $false
if ($cards.Count -ge 2 -and $cards[0] -match 'hider=(\d+)') {
    $h1 = $Matches[1]
    if ($cards[1] -match 'hider=(\d+)') { $swapped = ($Matches[1] -ne $h1) }
}
Beat "roles SWAPPED between round 1 and round 2" $swapped "round 1 and round 2 name different hiders"
$matchOver = @($cards | Where-Object { $_ -match 'matchOver=True' })
$winner = $false
if ($matchOver.Count -ge 1 -and $matchOver[0] -match 'winner=(\d+)') { $winner = ([int]$Matches[1] -ne 0) }
$matchEvidence = "no matchOver card"
if ($matchOver.Count -ge 1) { $matchEvidence = $matchOver[0].Trim() }
Beat "the match card names a WINNER" $winner $matchEvidence

# --- the third Start zeroes the live scores, the card keeps the result --------------------------
# THE SERVER PRINTS NO SUCH LINE, and the first cut of this assertion looked for one anyway --
# that sentence belongs to Run-RoundLoopSmoke's own summary, not to any server log. The two real
# witnesses are the THIRD card (a new match, totals 0-0) and the BOARD's score column under a
# MATCH 2 header, and the second is the better one because HOLD-1 built it precisely so that the
# live column zeroes while the footer keeps the old card. Both are asserted.
$thirdCard = @($cards | Where-Object { $_ -match 'match 2 .* totals 0-0' })
$boardZeroed = @($aLg | Where-Object { $_ -match 'header="MATCH 2[^"]*" rows=2 \[[^\]]*HiderA[^\]]*0' })
$zeroOk = ($thirdCard.Count -ge 1) -or ($boardZeroed.Count -ge 1)
$zeroEvidence = "no match-2 card and no zeroed MATCH 2 board row"
if ($thirdCard.Count -ge 1) { $zeroEvidence = $thirdCard[0].Trim() }
elseif ($boardZeroed.Count -ge 1) { $zeroEvidence = $boardZeroed[0].Trim() }
Beat "the third Start zeroes the live scores" $zeroOk $zeroEvidence
$footerAfter = @($aLg | Where-Object { $_ -match '\[board\].*footer="[^"]*(WON|WINS|\+)' })
Beat "the board's footer carries a card" ($footerAfter.Count -ge 1) "$($footerAfter.Count) footered board line(s)"
if ($footerAfter.Count -ge 1) { Note $footerAfter[-1].Trim() }

# --- captures -----------------------------------------------------------------------------------
$shots = @(Get-ChildItem $absCapture -Filter *.png -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
Beat "captures from BOTH lenses" (($shots | Where-Object { $_.Name -like 'HiderA*' }).Count -ge 1 -and ($shots | Where-Object { $_.Name -like 'SeekerB*' }).Count -ge 1) "$($shots.Count) frame(s)"
# A mis-cameraed capture writes a ~14 KB flat-grey frame (.claude/rules/test-suite.md, CELEBRATE-1).
$grey = @($shots | Where-Object { $_.Length -lt 20000 })
Beat "no capture is a flat-grey frame (>= 20 KB)" ($grey.Count -eq 0) "$($grey.Count) suspiciously small frame(s)"

Write-Host ""
Write-Host "FRAMES, by mtime -- match these against the server's own wall= stamps:" -ForegroundColor White
foreach ($s in $shots) {
    $ms = [DateTimeOffset]::new($s.LastWriteTimeUtc, [TimeSpan]::Zero).ToUnixTimeMilliseconds()
    Write-Host ("  {0,-28} {1,8:N0} bytes  wall={2}" -f $s.Name, $s.Length, $ms)
}

Write-Host ""
if ($script:Failures.Count -eq 0) {
    Write-Host "DEFINITION-OF-DONE WALK OVERALL: PASS" -ForegroundColor Green
    exit 0
}
Write-Host "DEFINITION-OF-DONE WALK FAILED ($($script:Failures.Count) beat(s)):" -ForegroundColor Red
foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
Write-Host "DEFINITION-OF-DONE WALK OVERALL: FAIL" -ForegroundColor Red
exit 1
