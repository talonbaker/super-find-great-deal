<#
.SYNOPSIS
    The server-side voice relay proximity gate (VoiceProximityGate), over real ENet, with real
    peers, a real relay and real Opus frames. No mocks anywhere: the only synthetic thing is the
    audio content itself (a 220 Hz sine instead of a microphone), exactly as Run-VoiceTest.ps1
    already does.

.DESCRIPTION
    Three fresh servers (the fresh-server-per-check pattern Run-VoiceTest/Run-SecurityTest use so
    state never cross-contaminates), each with the SAME three bots:

        Speaker  --voice-send, walks to (-20, 0)   (runs 8 s LONGER than the listeners — see
                                                    Invoke-Cell for the race that forces it)
        Near     walks to (-16, 0)   -> 4 m from the speaker: inside the 24 m audibility cutoff
        Far      walks to ( 28, 0)   -> 48 m from the speaker: past the 40 m gate exit radius

    Running near and far in ONE session is deliberate — it makes each check its own positive
    control. A gate that "worked" by breaking the relay outright would silence Near too, and the
    suite would fail; a counter that always read zero would fail the Near assertion in every
    cell. "Far received nothing" only means something when "Near received everything" is true in
    the same run, off the same server, through the same counter.

      1. Gate ON       - Near gets the full stream and is STILL receiving in the final window;
                         Far's stream stops (zero new packets in the final window). Far is
                         allowed a nonzero total: all three bots spawn on the same 4 m ring and
                         walk apart, so the first few seconds are legitimately in range. What
                         must be true is that it STOPS.
      2. Gate OFF      - both Near and Far get the full stream and are both still receiving at
                         the end, and the server's own gate counter reads exactly 0 on every
                         sampled second. That is the "byte-identical to today" arm: with the
                         switch off, the relay does not merely behave the same, it provably
                         never consults the gate at all.
      3. Gate ON + PA  - --voice-pa-all makes the server resolve every sender as broadcasting on
                         the PA. Far must get the full stream ANYWAY, at 50 m, and still be
                         receiving at the end. This is the check that stops the gate from
                         silently muting the intercom — the PA route has no distance falloff, so
                         a distance cull that does not exempt it is a bug that would only ever
                         be found in a playtest.

    Bandwidth numbers are NOT asserted here (they depend on machine load and on how compressible
    the synthetic tone is). The measurement lives in Run-VoiceGateBandwidth.ps1; this suite only
    proves the gate routes correctly.

    Exit 0 = PASS.
#>
[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# Guarded exactly like Run-VoiceTest.ps1: the marathon owns the one reset at its start, and an
# unguarded reset here would destroy every earlier suite's logs mid-run.
if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

$DurationSec = 22
# The window the assertions read: long enough after the walk (~6 s at walk speed over 30 m) that
# every bot has settled, and long enough itself that a 50 packets/second stream would deposit
# hundreds of packets in it if it were still flowing.
$SettleWindowSec = 6

function Start-FreshServer([int]$Port, [string]$Tag, [string[]]$Extra) {
    # Not $args: that is PowerShell's automatic unbound-argument variable inside a function.
    $srvArgs = @("--server", "--port", $Port, "--world", "open", "--log-dir", $script:LogDir) + $Extra
    $server = Start-Godot $srvArgs $Tag
    $script:allProcs += $server
    if (-not (Wait-ForLogLine (Join-Path $script:LogDir "$Tag.out.log") "\[server\] listening" 30)) {
        Write-Fail "server '$Tag' never came up; see $Tag.out.log"
    }
    return $server
}

# Every JSONL sample from a bot log, as objects. Voice counters are cumulative, so a DELTA
# between two samples is what proves a stream is still flowing (or has stopped) rather than
# merely that it once flowed.
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

function Get-VoiceCount($Sample, [long]$SenderId) {
    if ($null -eq $Sample.voice) { return 0 }
    $prop = $Sample.voice.PSObject.Properties | Where-Object { $_.Name -eq [string]$SenderId }
    if ($null -eq $prop) { return 0 }
    return [long]$prop.Value
}

# (total, deltaOverFinalWindow) for one sender as seen by this bot. The delta is the load-bearing
# number: "stopped receiving" is a statement about the END of the run, not about the total.
function Measure-Stream($Samples, [long]$SenderId) {
    $last = $Samples[$Samples.Count - 1]
    $endT = [double]$last.t
    $windowStartT = $endT - ($SettleWindowSec * 1000)
    $baseline = $null
    foreach ($s in $Samples) {
        if ([double]$s.t -le $windowStartT) { $baseline = $s }
    }
    if ($null -eq $baseline) { Write-Fail "no sample before the settle window (run too short?)" }
    $total = Get-VoiceCount $last $SenderId
    $delta = $total - (Get-VoiceCount $baseline $SenderId)
    return @{ Total = $total; Delta = $delta }
}

# Runs one cell and returns the two measured streams. Bots are launched together so all three
# share one walk-out window.
function Invoke-Cell([int]$Port, [string]$Tag, [string[]]$ServerExtra) {
    $statsLog = Join-Path $script:LogDir "$Tag.netstats.jsonl"
    $srv = Start-FreshServer $Port "$Tag.server" ($ServerExtra + @("--net-stats", $statsLog))

    $nearLog = Join-Path $script:LogDir "$Tag.near.jsonl"
    $farLog = Join-Path $script:LogDir "$Tag.far.jsonl"
    $common = @("--bot", "--address", "127.0.0.1:$Port", "--world", "open", "--duration", $DurationSec)

    # The speaker deliberately OUTLIVES both listeners. Bots are launched a fraction of a second
    # apart and start-up skew grows under concurrent load, so an equal-duration speaker can exit
    # first — its avatar despawns, its stream stops, and a listener's final sample then has
    # neither a Speaker position nor any new packets. That is a harness race, not a gate result,
    # and it flaked exactly that way on a loaded machine. A longer speaker duration removes the
    # race outright instead of tolerating it.
    $speakerArgs = @("--bot", "--address", "127.0.0.1:$Port", "--world", "open",
        "--duration", ($DurationSec + 8), "--voice-send", "--name", "Speaker", "--goto-script", "-20,0")
    $speaker = Start-Godot $speakerArgs "$Tag.speaker"
    $script:allProcs += $speaker
    $near = Start-Godot ($common + @("--name", "Near", "--log", $nearLog, "--goto-script", "-16,0")) "$Tag.near"
    $script:allProcs += $near
    $far = Start-Godot ($common + @("--name", "Far", "--log", $farLog, "--goto-script", "28,0")) "$Tag.far"
    $script:allProcs += $far

    foreach ($p in @($speaker, $near, $far)) {
        if (-not (Wait-ForExit $p 90)) { Write-Fail "${Tag}: a bot did not exit in time" }
        if ($p.ExitCode -ne 0) { Write-Fail "${Tag}: a bot exited $($p.ExitCode)" }
    }
    Stop-Proc $srv

    $nearSamples = Read-BotSamples $nearLog "$Tag near"
    $farSamples = Read-BotSamples $farLog "$Tag far"
    $speakerId = Get-PeerId $nearSamples "Speaker"
    if ($speakerId -lt 0) { Write-Fail "${Tag}: the Near bot never saw the Speaker player at all" }
    $farSpeakerId = Get-PeerId $farSamples "Speaker"
    if ($farSpeakerId -lt 0) { Write-Fail "${Tag}: the Far bot never saw the Speaker player at all" }

    # Distance sanity: the whole suite is meaningless if the bots never actually walked apart.
    # Read it off the FAR bot's own samples rather than trusting the --goto targets — and off the
    # LAST sample that actually contains both bodies, not blindly off the final one. A sample
    # taken during a spawn or teardown frame legitimately has no entry for a peer, and treating
    # that as a failure would make the suite fail for a reason that has nothing to do with voice.
    $sep = -1
    foreach ($s in $farSamples) {
        $sp = $s.peers | Where-Object { [long]$_.id -eq $farSpeakerId }
        $me = $s.peers | Where-Object { [long]$_.id -eq [long]$s.self }
        if ($null -eq $sp -or $null -eq $me) { continue }
        $sep = [math]::Sqrt([math]::Pow($sp.x - $me.x, 2) + [math]::Pow($sp.y - $me.y, 2) + [math]::Pow($sp.z - $me.z, 2))
    }
    if ($sep -lt 0) { Write-Fail "${Tag}: no far-bot sample ever contained both its own body and the speaker's" }
    if ($sep -lt 42) {
        Write-Fail "${Tag}: far bot settled only $([math]::Round($sep,1)) m from the speaker - it must be past the 40 m gate exit radius for this cell to prove anything"
    }

    return @{
        Near     = Measure-Stream $nearSamples $speakerId
        Far      = Measure-Stream $farSamples $farSpeakerId
        SepM     = $sep
        StatsLog = $statsLog
    }
}

$allProcs = @()
Write-Host "=== voice relay proximity gate (near hears / far does not / PA exempt) ===" -ForegroundColor White

try {
    # --- 1. Gate ON ---------------------------------------------------------------------------
    Write-Host "[1/3] gate ON: near still hears, far goes quiet..." -ForegroundColor Cyan
    $on = Invoke-Cell 7861 "vgate-on" @("--voice-gate", "on")
    if ($on.Near.Total -lt 150) {
        Write-Fail "gate ON: the NEAR peer received only $($on.Near.Total) voice packets - the gate has broken the relay outright, not culled it"
    }
    if ($on.Near.Delta -lt 100) {
        Write-Fail "gate ON: the NEAR peer received only $($on.Near.Delta) packets in the final ${SettleWindowSec}s - a peer 4 m away must keep hearing"
    }
    if ($on.Far.Delta -ne 0) {
        Write-Fail "gate ON: the FAR peer (settled $([math]::Round($on.SepM,1)) m away) was still receiving $($on.Far.Delta) packets in the final ${SettleWindowSec}s - the cull is not culling"
    }
    if ($on.Far.Total -ge $on.Near.Total) {
        Write-Fail "gate ON: the far peer received $($on.Far.Total) vs the near peer's $($on.Near.Total) - the gate is not distance-dependent at all"
    }
    Write-Host ("      ok (near {0} total / {1} in final window; far {2} total / 0 in final window at {3} m)" -f `
        $on.Near.Total, $on.Near.Delta, $on.Far.Total, [math]::Round($on.SepM, 1)) -ForegroundColor DarkGreen

    # --- 2. Gate OFF: today's behaviour, unchanged --------------------------------------------
    Write-Host "[2/3] gate OFF: both peers hear, and the gate is never consulted..." -ForegroundColor Cyan
    $off = Invoke-Cell 7862 "vgate-off" @("--voice-gate", "off")
    if ($off.Near.Total -lt 150 -or $off.Near.Delta -lt 100) {
        Write-Fail "gate OFF: the near peer received $($off.Near.Total) total / $($off.Near.Delta) in the final window"
    }
    if ($off.Far.Total -lt 150 -or $off.Far.Delta -lt 100) {
        Write-Fail "gate OFF: the FAR peer received $($off.Far.Total) total / $($off.Far.Delta) in the final window - with the switch off, distance must not matter at all"
    }
    # The stronger form of "unchanged": the server's own counter says nothing was ever gated.
    $offStats = @(Get-Content $off.StatsLog | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_ | ConvertFrom-Json })
    if ($offStats.Count -lt 5) { Write-Fail "gate OFF: net-stats produced only $($offStats.Count) samples" }
    $gatedTotal = ($offStats | Measure-Object -Property gated -Sum).Sum
    if ($gatedTotal -ne 0) { Write-Fail "gate OFF: the server reports $gatedTotal gated packets - the switch is not off" }
    # Positive control on that very counter: the gate-ON cell above must have moved it, or
    # "gated == 0" here would prove nothing except that the field is dead.
    $onStats = @(Get-Content $on.StatsLog | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_ | ConvertFrom-Json })
    $onGated = ($onStats | Measure-Object -Property gated -Sum).Sum
    if ($onGated -le 0) {
        Write-Fail "the server's gated counter read 0 in the gate-ON cell too - the counter is dead, so 'gated == 0' with the switch off proves nothing"
    }
    Write-Host ("      ok (near {0}, far {1}, gated 0 with the switch off vs {2} with it on)" -f `
        $off.Near.Total, $off.Far.Total, $onGated) -ForegroundColor DarkGreen

    # --- 3. Gate ON + PA: the intercom must survive the cull -----------------------------------
    Write-Host "[3/3] gate ON + PA: the broadcast reaches everyone regardless of distance..." -ForegroundColor Cyan
    $pa = Invoke-Cell 7863 "vgate-pa" @("--voice-gate", "on", "--voice-pa-all")
    if ($pa.Far.Total -lt 150 -or $pa.Far.Delta -lt 100) {
        Write-Fail "gate ON + PA: the far peer received $($pa.Far.Total) total / $($pa.Far.Delta) in the final window at $([math]::Round($pa.SepM,1)) m - the gate is muting the PA"
    }
    if ($pa.Near.Total -lt 150) {
        Write-Fail "gate ON + PA: the near peer received only $($pa.Near.Total) packets"
    }
    $paStats = @(Get-Content $pa.StatsLog | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_ | ConvertFrom-Json })
    $paGated = ($paStats | Measure-Object -Property gated -Sum).Sum
    $paExempt = ($paStats | Measure-Object -Property paExempt -Sum).Sum
    if ($paGated -ne 0) { Write-Fail "gate ON + PA: $paGated packets were gated - a PA broadcast must never be culled" }
    if ($paExempt -le 0) { Write-Fail "gate ON + PA: the server's paExempt counter read 0 - the exemption branch never ran, so this cell passed for the wrong reason" }
    Write-Host ("      ok (far {0} packets at {1} m, {2} relays taken by the PA exemption, 0 gated)" -f `
        $pa.Far.Total, [math]::Round($pa.SepM, 1), $paExempt) -ForegroundColor DarkGreen
} finally {
    Stop-Procs $allProcs
}

Write-Host ""
Write-Host "PASS: near peers keep hearing, far peers are culled and stay culled, the switch off is provably inert, and the PA route survives the cull." -ForegroundColor Green
exit 0
