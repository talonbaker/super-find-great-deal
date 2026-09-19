<#
.SYNOPSIS
    The goose honk (HONK-1) over real ENet, with real peers and a real relay. No mocks anywhere:
    the only synthetic thing is that the presses arrive from --honk-at instead of from a keyboard,
    and they go through HonkManager.ClientRequestHonk — the exact method the H key calls.

.DESCRIPTION
    Three fresh servers (the fresh-server-per-check pattern Run-VoiceTest / Run-VoiceGateTest /
    Run-SecurityTest use, so state never cross-contaminates), modelled on Run-VoiceGateTest's
    near/far cell because the question is the same question: does a proximity sound reach the
    peer beside you and not the peer across the map.

      1. Crosses the wire, positionally, and does not machine-gun.

           Honker  --honk-at x7,  walks to (-20, 0)   (outlives the listeners - see Invoke-Cell)
           Near    walks to (-16, 0)   ->  4 m from the honker: well inside the 24 m cutoff
           Far     walks to ( 28, 0)   -> 48 m from the honker: twice the cutoff

         NEAR is the packet's criterion 3: peer A presses, peer B hears it, and the JSONL
         records it against A's peer id - which is the same statement as "at A's position",
         because nothing on the wire carries a position at all and the receiver resolves it
         from A's replicated avatar (see HonkManager's class doc).

         FAR is criterion 4, the ABSENCE, and it is stated in the strong form: the far peer's
         RECEIVED counter must be > 0 while its HEARD counter is exactly 0. "Received nothing"
         would have been satisfied by a broken wire; "received the message and stayed silent"
         can only be satisfied by the range rule.

         THE POSITIVE CONTROL IS IN THE SAME RUN, on the same server, through the same counter:
         Near and Far are the same bot type with the same flags and different destinations. A
         range rule that "worked" by breaking honks outright silences Near and fails the cell;
         a counter that always read zero fails Near's assertion. Neither absence means anything
         without the presence beside it. (Cell 1 is therefore its own positive control for the
         range rule; cell 2 supplies the separate one criterion 4 asks for - see below.)

         ANTI-SPAM is criterion 6, measured in the same cell for free: seven presses, six of
         them inside a single 0.6 s cooldown, must produce exactly two honks.

      2. The far peer's silence was the RANGE and not a dead feature (criterion 4's positive
         control, stated as a separate run rather than as an argument): the identical bot, at
         the identical destination Far used in cell 1, with the HONKER moved next to it
         instead. The peer that heard nothing at 48 m must hear the honks at 4 m. Plus the
         forgery check (criterion 5): a bot fires the honk BROADCAST rpc straight at the other
         clients, naming their peer ids, and neither victim's received or heard counter may
         move for the forged id.

      3. The SERVER's cooldown, which cells 1 and 2 do not reach. Measured, on this suite's
         first run: seven presses produced two honks and the server logged nothing at all,
         because the client-side cooldown copy suppressed the extras before a packet left the
         machine. So cell 1 proves the courtesy latch and the AUTHORITATIVE one had never run.
         --honk-flood is the client that deleted the local check: raw requests every frame for
         two seconds. The server must log its drops and the listener must hear a handful, not
         a hundred.

    Audio *quality* is explicitly not provable here - whether it reads as a goose rather than a
    duck is Talon's ear, and the waveform's shape is pinned arithmetically in
    tests/unit/HonkTests.cs. This suite proves the mechanism.

    Exit 0 = PASS.
#>
[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# Guarded exactly like Run-VoiceTest.ps1 / Run-VoiceGateTest.ps1: the marathon owns the one reset
# at its start, and an unguarded reset here would destroy every earlier suite's logs mid-run.
if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

# --- The schedule, derived rather than typed beside itself -------------------------------------
# .claude/rules/test-suite.md, "A bot lifetime that is typed beside the mark it must outlive":
# every window below is computed from the press marks, so moving a mark moves the windows with it
# and no assertion can quietly stop covering what it names.
$WalkSettleSec = 9        # ~30 m of walking at walk speed, plus slack for a loaded machine
$MashPresses = 6          # criterion 6's N
$MashSpacingSec = 0.1     # 6 presses across 0.5 s: comfortably inside one 0.6 s cooldown
$SoloPressSec = $WalkSettleSec                       # one honk, after everyone has settled
$MashStartSec = $WalkSettleSec + 3.0                 # then the mash, a clear cooldown later
$MashEndSec = $MashStartSec + ($MashPresses - 1) * $MashSpacingSec
$DurationSec = [math]::Ceiling($MashEndSec + 5.0)    # 5 s of tail so the last honk is sampled
$HonkerExtraSec = 8       # the honker outlives the listeners - see Invoke-Cell

# The press marks: one lone honk that must be heard, then a mash that must not be heard six times.
$HonkMarks = @($SoloPressSec)
for ($i = 0; $i -lt $MashPresses; $i++) { $HonkMarks += ($MashStartSec + $i * $MashSpacingSec) }
# Total presses the honker makes = 1 + MashPresses. Granted honks must be 2: the solo one, and
# exactly one of the mash (the other five land inside its cooldown).
$TotalPresses = 1 + $MashPresses
$ExpectedHonks = 2

function Start-FreshServer([int]$Port, [string]$Tag) {
    # Not $args: that is PowerShell's automatic unbound-argument variable inside a function.
    $srvArgs = @("--server", "--port", $Port, "--world", "open", "--log-dir", $script:LogDir)
    $server = Start-Godot $srvArgs $Tag
    $script:allProcs += $server
    if (-not (Wait-ForLogLine (Join-Path $script:LogDir "$Tag.out.log") "\[server\] listening" 30)) {
        Write-Fail "server '$Tag' never came up; see $Tag.out.log"
    }
    return $server
}

# Any number that becomes a CLI argument goes through here. See the note at the --honk-at loop.
function Format-Arg([double]$Value) {
    return $Value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

function Read-BotSamples([string]$JsonLog, [string]$Label) {
    if (-not (Test-Path $JsonLog)) { Write-Fail "$Label produced no JSON log at $JsonLog" }
    $lines = @(Get-Content $JsonLog | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -lt 10) { Write-Fail "$Label log has only $($lines.Count) samples" }
    return @($lines | ForEach-Object { $_ | ConvertFrom-Json })
}

function Get-PeerId($Samples, [string]$Name) {
    foreach ($s in $Samples) {
        foreach ($p in $s.peers) { if ([string]$p.name -eq $Name) { return [long]$p.id } }
    }
    return -1
}

# Counters are cumulative, so the LAST sample is the total. Reads either map by name.
function Get-HonkCount($Samples, [string]$Field, [long]$HonkerId) {
    $last = $Samples[$Samples.Count - 1]
    $map = $last.$Field
    if ($null -eq $map) { return 0 }
    $prop = $map.PSObject.Properties | Where-Object { $_.Name -eq [string]$HonkerId }
    if ($null -eq $prop) { return 0 }
    return [long]$prop.Value
}

# Separation between this bot and the honker, read off its OWN samples rather than trusting the
# --goto targets, and measured OVER THE WINDOW IN WHICH HONKS WERE BROADCAST rather than at the end
# of the run. The distinction is the whole point of the range assertions: the honks land at 9 s and
# 12.0-12.5 s, the bots live to 18 s, and a single end-of-run reading would be evidence from six
# seconds after the fact. Today the bots walk and then stop, so the two agree — but "they agree
# today" is not what the assertion claims.
#
# Returns BOTH bounds, because the two listeners need opposite ones: the near bot must have been
# inside the cutoff for the WHOLE window (so its Max is what matters) and the far bot outside it for
# the whole window (its Min). -1 for either means no sample in the window carried both bodies.
#
# The window starts at the first press mark and runs to the end: a spawn or teardown frame
# legitimately has no entry for a peer, and treating that as a failure would fail the suite for a
# reason that has nothing to do with honking. (Run-VoiceGateTest's lesson, reused.) `t` is this
# bot's own engine clock and the marks are the HONKER's elapsed clock -- two processes -- so the
# window is deliberately generous at the front rather than precise; everything has settled by then
# and stays settled.
function Measure-Separation($Samples, [long]$HonkerId, [double]$FromSec) {
    $min = [double]::MaxValue
    $max = -1
    foreach ($s in $Samples) {
        if ([double]$s.t -lt ($FromSec * 1000)) { continue }
        $h = $s.peers | Where-Object { [long]$_.id -eq $HonkerId }
        $me = $s.peers | Where-Object { [long]$_.id -eq [long]$s.self }
        if ($null -eq $h -or $null -eq $me) { continue }
        $d = [math]::Sqrt([math]::Pow($h.x - $me.x, 2) + [math]::Pow($h.y - $me.y, 2) + [math]::Pow($h.z - $me.z, 2))
        if ($d -lt $min) { $min = $d }
        if ($d -gt $max) { $max = $d }
    }
    if ($max -lt 0) { return @{ Min = -1; Max = -1; Samples = 0 } }
    return @{ Min = $min; Max = $max }
}

# One cell: a server, a honker walking to $HonkerTo, and two listeners walking to $NearTo/$FarTo.
# The listeners are the SAME bot type with the same flags — only the destination differs, which is
# what makes the pair each other's control.
function Invoke-Cell([int]$Port, [string]$Tag, [string]$HonkerTo, [string]$NearTo, [string]$FarTo,
                     [string[]]$ExtraNear = @(), [string[]]$ExtraFar = @(), [string[]]$ExtraHonker = @()) {
    $srv = Start-FreshServer $Port "$Tag.server"

    $nearLog = Join-Path $script:LogDir "$Tag.near.jsonl"
    $farLog = Join-Path $script:LogDir "$Tag.far.jsonl"
    $common = @("--bot", "--address", "127.0.0.1:$Port", "--world", "open", "--duration", $DurationSec)

    # The honker deliberately OUTLIVES both listeners. Bots are launched a fraction of a second
    # apart and start-up skew grows under concurrent load, so an equal-duration honker can exit
    # first — its avatar despawns, and a listener's final sample then has neither a honker position
    # nor any late honk. That is a harness race, not a result. (Run-VoiceGateTest measured this one
    # flaking exactly that way; the fix is copied rather than rediscovered.)
    $honkerArgs = @("--bot", "--address", "127.0.0.1:$Port", "--world", "open",
        "--duration", ($DurationSec + $HonkerExtraSec), "--name", "Honker", "--goto-script", $HonkerTo)
    # InvariantCulture on every double that becomes a CLI argument: LaunchOptions parses with
    # CultureInfo.InvariantCulture, and PowerShell formats with the machine's. On a comma-decimal
    # locale "12,5" is silently dropped by the parser and every press mark disappears.
    foreach ($m in $HonkMarks) { $honkerArgs += @("--honk-at", (Format-Arg $m)) }
    $honkerArgs += $ExtraHonker
    $honker = Start-Godot $honkerArgs "$Tag.honker"
    $script:allProcs += $honker

    $near = Start-Godot ($common + @("--name", "Near", "--log", $nearLog, "--goto-script", $NearTo) + $ExtraNear) "$Tag.near"
    $script:allProcs += $near
    $far = Start-Godot ($common + @("--name", "Far", "--log", $farLog, "--goto-script", $FarTo) + $ExtraFar) "$Tag.far"
    $script:allProcs += $far

    foreach ($p in @($honker, $near, $far)) {
        if (-not (Wait-ForExit $p 120)) { Write-Fail "${Tag}: a bot did not exit in time" }
        if ($p.ExitCode -ne 0) { Write-Fail "${Tag}: a bot exited $($p.ExitCode)" }
    }
    Stop-Proc $srv

    $nearSamples = Read-BotSamples $nearLog "$Tag near"
    $farSamples = Read-BotSamples $farLog "$Tag far"
    $honkerId = Get-PeerId $nearSamples "Honker"
    if ($honkerId -lt 0) { Write-Fail "${Tag}: the Near bot never saw the Honker player at all" }
    $farHonkerId = Get-PeerId $farSamples "Honker"
    if ($farHonkerId -lt 0) { Write-Fail "${Tag}: the Far bot never saw the Honker player at all" }

    return @{
        HonkerId    = $honkerId
        NearSamples = $nearSamples
        FarSamples  = $farSamples
        NearHeard   = Get-HonkCount $nearSamples "honkHeard" $honkerId
        NearRecvd   = Get-HonkCount $nearSamples "honkReceived" $honkerId
        FarHeard    = Get-HonkCount $farSamples "honkHeard" $farHonkerId
        FarRecvd    = Get-HonkCount $farSamples "honkReceived" $farHonkerId
        NearSep     = Measure-Separation $nearSamples $honkerId $SoloPressSec
        FarSep      = Measure-Separation $farSamples $farHonkerId $SoloPressSec
    }
}

$allProcs = @()
Write-Host "=== the goose honk: crosses the wire, near hears, far does not, mashing does not machine-gun ===" -ForegroundColor White
Write-Host ("    schedule: solo press at {0}s, {1} presses from {2}s at {3}s apart, bots live {4}s" -f `
    $SoloPressSec, $MashPresses, $MashStartSec, $MashSpacingSec, $DurationSec) -ForegroundColor DarkGray

try {
    # --- 1. Near hears at the honker's position; far receives and stays silent -----------------
    Write-Host "[1/3] honk crosses the wire; near hears it, far receives it and stays silent..." -ForegroundColor Cyan
    $c1 = Invoke-Cell 7871 "honk-range" "-20,0" "-16,0" "28,0"

    # The whole cell is meaningless if the bots never actually walked apart.
    if ($c1.NearSep.Max -lt 0 -or $c1.FarSep.Max -lt 0) {
        Write-Fail "no sample ever contained both a listener's own body and the honker's"
    }
    # MAX for the near bot: it must have been inside the cutoff for the WHOLE honk window, not
    # merely at some point in it.
    if ($c1.NearSep.Max -gt 12) {
        Write-Fail "near bot reached $([math]::Round($c1.NearSep.Max,1)) m from the honker during the honk window - it must stay well inside the 24 m cutoff for this cell to prove anything"
    }
    # MIN for the far bot, and for the mirror-image reason: its CLOSEST approach during the honk
    # window has to be past the cutoff, or one of the honks it stayed silent for was legitimately
    # in range and the absence below means nothing.
    if ($c1.FarSep.Min -lt 30) {
        Write-Fail "far bot came within $([math]::Round($c1.FarSep.Min,1)) m of the honker during the honk window - it must stay past the 24 m cutoff for this cell to prove anything"
    }

    # Criterion 3: it crossed the wire and was attributed to the honker's peer id.
    if ($c1.NearHeard -lt 1) {
        Write-Fail "the NEAR peer heard $($c1.NearHeard) honks from peer $($c1.HonkerId) at $([math]::Round($c1.NearSep.Max,1)) m - the honk is not reaching other players at all"
    }
    # Criterion 6: N presses, fewer than N honks. Exactly $ExpectedHonks, in fact.
    if ($c1.NearHeard -ge $TotalPresses) {
        Write-Fail "the near peer heard $($c1.NearHeard) honks from $TotalPresses presses - the cooldown is not throttling anything"
    }
    if ($c1.NearHeard -ne $ExpectedHonks) {
        Write-Fail "the near peer heard $($c1.NearHeard) honks; $TotalPresses presses against a 0.6 s cooldown must yield exactly $ExpectedHonks (one solo + one of the mash)"
    }
    # NOTE, measured on this suite's first run and worth keeping: the server logs NOTHING in this
    # cell, and that is correct. The client-side cooldown copy suppresses the five extra presses
    # before a packet leaves the honker's machine, so the server is never asked. Cell 3 is what
    # exercises the authoritative latch — see its header.

    # Criterion 4, in the strong form: the far peer GOT the message and chose not to play it.
    if ($c1.FarRecvd -lt 1) {
        Write-Fail "the FAR peer received $($c1.FarRecvd) honk messages - it must receive them for its silence to mean anything (a zero here is a broken wire, not a range rule)"
    }
    if ($c1.FarHeard -ne 0) {
        Write-Fail "the FAR peer played $($c1.FarHeard) honks at $([math]::Round($c1.FarSep.Min,1)) m - past the 24 m cutoff nothing may be audible"
    }
    if ($c1.NearRecvd -ne $c1.FarRecvd) {
        Write-Fail "near received $($c1.NearRecvd) messages and far received $($c1.FarRecvd) - both must receive identically; only what they PLAY may differ"
    }
    Write-Host ("      ok ({0} presses -> {1} honks; near heard {2}/{3} received at {4} m, far heard 0/{5} received at {6} m)" -f `
        $TotalPresses, $c1.NearHeard, $c1.NearHeard, $c1.NearRecvd, [math]::Round($c1.NearSep.Max, 1), `
        $c1.FarRecvd, [math]::Round($c1.FarSep.Min, 1)) -ForegroundColor DarkGreen

    # --- 2. The far bot's positive control, plus the forgery check -----------------------------
    # The SAME bot at the SAME destination (28,0) that heard nothing in cell 1, with the honker
    # moved next to it. If this one is also silent, cell 1's absence proved nothing about range.
    Write-Host "[2/3] positive control: the same far bot, honker moved next to it; plus a forged honk..." -ForegroundColor Cyan
    $c2 = Invoke-Cell 7872 "honk-control" "24,0" "-16,0" "28,0" @("--honk-forge", (Format-Arg $MashEndSec))

    if ($c2.FarSep.Max -gt 12) {
        Write-Fail "control: the far bot reached $([math]::Round($c2.FarSep.Max,1)) m from the honker - it was supposed to be standing next to it"
    }
    if ($c2.FarHeard -lt 1) {
        Write-Fail "control: the far bot heard $($c2.FarHeard) honks at $([math]::Round($c2.FarSep.Max,1)) m - it cannot hear a honk at ALL, so cell 1's silence at 48 m proved nothing about range"
    }
    # ...and the near bot, now the distant one, must have gone quiet — the control both ways.
    if ($c2.NearSep.Min -lt 30) {
        Write-Fail "control: the near bot came within $([math]::Round($c2.NearSep.Min,1)) m of the honker; it needed to be out of range for this half"
    }
    if ($c2.NearHeard -ne 0) {
        Write-Fail "control: the now-distant bot played $($c2.NearHeard) honks at $([math]::Round($c2.NearSep.Min,1)) m"
    }

    # Criterion 5: the forgery. The Near bot fired PlayHonk client-to-client at every other peer,
    # naming ids that are not its own. RpcMode.Authority must refuse it, so no victim's counters
    # moved for a forged id. The victims' counters for the REAL honker are the positive control
    # sitting right beside it: the same counters are demonstrably alive in the same run.
    # The forger must actually have fired something. Read off its OWN log rather than assumed:
    # the first spelling of this probe enumerated Multiplayer.GetPeers(), which on an ENet CLIENT
    # returns only the server - so it attempted ZERO forgeries and this whole check passed while
    # RpcMode.Authority was deliberately downgraded to AnyPeer. Measured, and this line is what
    # stops it recurring.
    $forgeLog = Join-Path $script:LogDir "honk-control.near.out.log"
    $forgeLine = Select-String -Path $forgeLog -Pattern "FORGED (\d+) honk broadcasts" | Select-Object -First 1
    if ($null -eq $forgeLine) { Write-Fail "the forger never reported; see honk-control.near.out.log" }
    $forgeAttempts = [int]$forgeLine.Matches[0].Groups[1].Value
    if ($forgeAttempts -lt 2) {
        Write-Fail "the forger attempted only $forgeAttempts forgeries - 'no forged honk landed' proves nothing if none was thrown"
    }

    # A landed forgery shows up as a honk attributed to a peer that never honks: the Near bot
    # (the forger, which only listens) or the Far bot itself. Both ids were claimed; neither may
    # have moved either counter.
    $nearIdAsSeenByFar = Get-PeerId $c2.FarSamples "Near"
    $farOwnId = [long]$c2.FarSamples[$c2.FarSamples.Count - 1].self
    $forged = 0
    foreach ($claimed in @($nearIdAsSeenByFar, $farOwnId)) {
        if ($claimed -lt 0) { continue }
        $forged += (Get-HonkCount $c2.FarSamples "honkReceived" $claimed) +
                   (Get-HonkCount $c2.FarSamples "honkHeard" $claimed)
    }
    if ($forged -ne 0) {
        Write-Fail "a FORGED honk landed: the far bot recorded $forged honk events attributed to peers that never honked"
    }
    if ($c2.FarRecvd -lt 1) {
        Write-Fail "the forgery check passed vacuously - the far bot's counters never moved for the REAL honker either, so 'no forged honks' proves nothing"
    }
    Write-Host ("      ok (same bot at the same spot heard {0} honks with the honker at {1} m; forged honks landed: 0, against {2} real ones on the same counter)" -f `
        $c2.FarHeard, [math]::Round($c2.FarSep.Max, 1), $c2.FarRecvd) -ForegroundColor DarkGreen

    # --- 3. The SERVER's cooldown, against a client that does not have one ----------------------
    # Cell 1 proves the shipped path throttles, but it proves it on the CLIENT: the local cooldown
    # copy eats the extra presses before a packet is sent, so the server is never asked and its own
    # latch never runs. That was measured, not assumed — cell 1's first green run logged nothing on
    # the server at all. A player who deletes the local check from their build is the case the
    # authoritative latch exists for, and --honk-flood is that player: raw requests every frame,
    # cooldown copy bypassed.
    Write-Host "[3/3] the server's own cooldown, against a client that has none..." -ForegroundColor Cyan
    $c3 = Invoke-Cell 7873 "honk-flood" "-20,0" "-16,0" "28,0" @() @() @("--honk-flood", (Format-Arg $MashEndSec))

    $serverLog = Join-Path $script:LogDir "matchserver.log"
    if (-not (Wait-ForLogLine $serverLog "honk cooldown: press dropped" 10)) {
        Write-Fail "the server never logged a honk cooldown drop under a raw flood - the authoritative latch is not running"
    }
    # How many raw requests actually went out, from the flooder's own mouth. Asserting against a
    # number the bot MEASURED rather than one this script assumed: frame rate under load is not
    # something a runner may predict.
    # The flooder prints its WINDOW and the server's COOLDOWN alongside the count, so the ceiling
    # below is DERIVED rather than typed beside two constants that live in C#. Raising
    # BotHarness.HonkFloodDurationSec or HonkConfig.CooldownSec would otherwise turn a legitimate
    # result into a false red blaming the server (.claude/rules/test-suite.md).
    $floodLog = Join-Path $script:LogDir "honk-flood.honker.out.log"
    $floodLine = Select-String -Path $floodLog -Pattern `
        "honk flood: (\d+) raw requests over ([\d.]+)s window against a ([\d.]+)s server cooldown" |
        Select-Object -First 1
    if ($null -eq $floodLine) { Write-Fail "the flooder never reported its raw-request count; see honk-flood.honker.out.log" }
    $attempted = [int]$floodLine.Matches[0].Groups[1].Value
    $floodWindowSec = [double]$floodLine.Matches[0].Groups[2].Value
    $serverCooldownSec = [double]$floodLine.Matches[0].Groups[3].Value
    if ($serverCooldownSec -le 0) { Write-Fail "the flooder reported a $serverCooldownSec s server cooldown" }

    # A flood of length W against a cooldown C can grant at most ceil(W/C) + 1 honks; cell 1's two
    # scripted presses ride on top, and one more is slack for the jitter tolerance. Everything here
    # comes off the bot's own printed numbers.
    $maxHonks = [math]::Ceiling($floodWindowSec / $serverCooldownSec) + 4
    # Enough attempts that the ratio means something: at least ten times the ceiling.
    $minAttempts = $maxHonks * 10
    if ($attempted -lt $minAttempts) {
        Write-Fail "the flooder only managed $attempted raw requests in ${floodWindowSec}s - fewer than $minAttempts, so the throttle ratio below would not mean anything"
    }
    if ($c3.NearHeard -gt $maxHonks) {
        Write-Fail "the near peer heard $($c3.NearHeard) honks from $attempted raw requests - a ${floodWindowSec}s flood against a ${serverCooldownSec}s cooldown may grant at most $maxHonks, so the server's cooldown is not throttling"
    }
    if ($c3.NearHeard -lt 1) {
        Write-Fail "the near peer heard nothing at all during the flood - the throttle passed vacuously (a dead relay throttles perfectly)"
    }
    Write-Host ("      ok ({0} raw requests over {1}s with the client cooldown bypassed -> {2} honks heard, ceiling {3}; server logged the drop)" -f `
        $attempted, $floodWindowSec, $c3.NearHeard, $maxHonks) -ForegroundColor DarkGreen
} finally {
    Stop-Procs $allProcs
}

Write-Host ""
Write-Host "PASS: a honk crosses the wire to other peers at the honker's position, is inaudible past the voice range, is throttled by its cooldown, and cannot be forged." -ForegroundColor Green
exit 0
