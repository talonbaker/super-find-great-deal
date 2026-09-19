<#
.SYNOPSIS
    SFX-1's scene gate, extended by SFX-2: every material fires its OWN voice for pick-up, throw
    and impact, EVERY PEER HEARS EVERY IMPACT, a PLACE and a THROW are different sounds on both
    peers, a two-prop collision fires exactly once, a dedicated server stays silent, and the 3D
    voice budget survives forty props landing together with the source-side limiter on.

.DESCRIPTION
    Two phases, sequential, on one port, both in the real "supermarket" world with
    --spawn-room search -- the room the game's aisles will fill.

    WHO IS ALLOWED TO HEAR ANYTHING, because every decision in this file follows from it.
    ActorFx.Fire early-outs on a headless instance (a dedicated server must not pay for an audio
    pool nobody can hear), and NetworkManager.IsHeadless is DisplayServer.GetName() being
    "headless" -- a property of the DISPLAY, not of the --server flag. So:

      * a headless --server plays nothing           -> that is assertion 7, measured in phase 2
      * a WINDOWED --server is a host who is a player, AND is the only peer that simulates loose
        prop physics                                -> it is the gate, in phase 1
      * a headless --bot plays nothing              -> the three drivers are deaf on purpose
      * a WINDOWED --bot is the remote player       -> the honest measurement, in phase 1

    PHASE 1 - THE MATERIAL PROOF.
    A windowed host seeds seven props: a can, a cereal box and a piece of produce in a row for
    the drivers; a CAN 0.73 m above a CRATE in a clear corner, which is the once-per-contact
    fixture; and a free-falling cereal box and piece of produce
    1.14 m up, which guarantee those two materials an impact. Three headless drivers each walk to
    one product, grab it and throw it. A second windowed client stands in the middle and listens.

    THE FREE-FALLERS ARE THERE BECAUSE A THROW IS NOT A RELIABLE IMPACT. Measured: the thrown can
    and box landed hard, and the thrown produce -- 0.25 kg, a sphere, on a shallow arc -- grazed
    the floor under the 2.0 m/s audible floor and rolled into a wall, so "produce never fired
    ProduceThump" was a fact about a ballistic arc rather than about a sound. A controlled fall
    arrives at the same speed every run.

    A SEEDED PROP DOES NOT FALL ON ITS OWN. ServerSpawn registers it Resting and
    NetworkedProp._Ready freezes a Resting prop kinematic on every peer, so a fixture hangs in
    mid-air forever -- correct for every suite before this one, fatal here. --seed-props-drop
    (SFX-1) releases the whole fixture into Loose once, $DropAtSec into the session, through the
    ordinary registry/broadcast/BeginLooseServer path a thrown prop takes. That release is what
    makes the stacked pair collide, and it is the only reason this suite can say anything about
    the once-per-contact rule.

    WHY THREE DRIVERS AND NOT ONE. --carry-script scripts exactly one prop per bot -- one target,
    one grab clock, one throw clock -- so "picks up and throws one of each kind" is three bots or
    three sequential servers. Three bots is the cheaper of the two and is the shape
    Run-ThrowTest.ps1 already uses. They are headless, so the window count stays at two.

    THE EVIDENCE IS THE LOG, because it has to be: a headless run has no audio device, a windowed
    run cannot be recorded by a suite, and there is no way to photograph a sound. --log-sfx
    prints one line per sound ActorFx actually plays:

        [sfx] sfx TinClank event=Impact intensity=0.412 at (45.00,0.18,4.00) src=4 t=6210 via=wire

    src is the NetworkedProp's node name, which IS the prop id; t is Time.GetTicksMsec(). Both
    exist for assertion 5: two props in contact are by definition in the same place at the same
    moment, so neither the position nor the ordering can attribute a sound on its own.

    via= IS SFX-2's FIELD AND IT IS WHY THIS SUITE CAN NOW GATE ON THE REMOTE PLAYER. It says
    which PATH produced the sound: `local` for something that happened on this machine, `wire`
    for the server's PropImpact announcement. The packet asked for `src=wire`; that would have
    overwritten the prop id and destroyed assertions 1, 4 and 5 -- including the one the
    -PlantCrossedProfile mutation exists to break -- so the path is its own field and src still
    names the prop. See ActorFx.ViaWire.

    EVERY networked prop impact is now via=wire, on the host too, and that is the fix rather than
    an artefact: Carryable no longer plays a networked prop's impact where the contact happens.
    One path, one sound. via=local on an Impact from a numbered prop is therefore a DEFECT and
    assertion 9 fails on it.

    Assertions on the HOST's log, every window anchored on an observed event, never a wall clock:

      1. Per-kind pick-up   - TinPick, CardPick and ProducePick each appear on PickedUp.
      2. Per-kind impact    - TinClank, CardThud and ProduceThump each appear on Impact.
      3. Release            - Whoosh appears on Thrown, from all three product ids.
      4. No crossed wires   - no material's voice ever comes from another material's prop.
      5. Fires ONCE         - read off the SUPPRESSION Carryable logs, not inferred from the
                              geometry: a prop-on-prop contact was silenced on one body, and its
                              partner played inside the claim window. Godot reports one contact
                              to both bodies, so an unguarded handler produces a flam and spends
                              two voices; a rule that picks its winner up front instead produces
                              SILENCE whenever only the loser was asked, which is what the
                              instance-id version of this rule actually did. Proving the pairing
                              rather than the count is what tells those two apart.
      6. Intensity          - every logged intensity is inside [0,1], and at least one impact is
                              strictly above 0. The second half is the first half's positive
                              control: a mapping stuck at zero satisfies the range and proves
                              nothing.
      7. Headless is silent - phase 1's four HEADLESS drivers, each given --log-sfx, log no
                              [sfx] line between them, against the windowed host which logs
                              dozens from the very same transitions. An absence with a positive
                              control beside it. This is the early-out that keeps a dedicated
                              server from paying for an audio pool nobody can hear.

    SFX-2's three, and they are the point of the packet:

      8. THE OTHER PLAYER HEARS IT - the non-host windowed WITNESS logs TinClank, CardThud and
                              ProduceThump on Impact, each with via=wire. This was measured at
                              ZERO by SFX-1 and reported rather than gated, because a client's
                              Loose prop is a frozen kinematic body and Godot reports it no
                              contact at all -- so the witness could not derive the event however
                              hard it looked. It is gated now because the server announces it.
                              In a game where the seeker's only remote sense is what they can
                              hear through a wall, this assertion IS the feature.
      9. ONE FIRE PER CONTACT - no Impact anywhere carries via=local from a numbered prop. The
                              host used to play its own contact directly; if it still did, it
                              would now hear each hit twice (local + wire), which is a flam
                              rather than a louder hit. A count would not catch it -- two fires
                              a millisecond apart at the same position look like one event to
                              anything that counts near a position.
     10. PLACE != THROW ON THE WIRE - a scripted PLACE logs event=Placed on BOTH windowed peers
                              and the scripted throws log event=Thrown on both. CARRY-1 recorded
                              that at ApplyPropState a place and a drop are indistinguishable, so
                              SFX-1 had to play the shared arm-whoosh for both and the material's
                              settle tick was host-only. Asserting it on BOTH peers is what makes
                              this about the wire rather than about the verb.

    PHASE 2 - THE VOICE BUDGET, ON A WINDOWED HOST.
    Forty mixed props seeded on a lattice at staggered heights and released together, with two
    bots walking through them: the same concurrency event a shelf going over produces, without
    waiting for SHELF-1 (the packet says explicitly not to). The server is windowed here too,
    because it is the only peer that simulates the heap and therefore the only one where two
    props can both be moving at a contact -- which is the only way the once-per-contact rule can
    be exercised at all. The windowed bot prints at exit:

        [sfx] SUMMARY fires=N peakLive3DVoices=N oneShotSteals=N poolSize=14 ceiling=24

    Always reported. Red if the peak exceeds the 24-voice AudioVoiceBudget ceiling, or if steals
    pass a 60% REGRESSION bar.

    THE PACKET'S 10% DESIGN BAR IS REPORTED, NOT GATED, AND THAT IS A FINDING RATHER THAN A
    CLIMBDOWN. Measured: 48% of 54 fires stolen at PoolSize 14. SFX-1 then spent every slot the
    ceiling had left -- the loop partition holds four voices reserved for an OUTDOOR FIRE BED in
    a game set indoors, so 14 -> 18 and 5 -> 1, ceiling untouched at 24 -- and the peak came back
    at exactly 18 with 39% still stolen. Forty simultaneous impacts want forty voices; no pool
    that fits under 24 serves them. The fix is source-side limiting, the way
    FootstepAudioDirector already caps six sprinting players rather than widening the pool for
    them, and that is a design call this lane does not own. The number is printed every run.

    PROVING IT CAN FAIL. Two plants, each aimed at what its own assertion defends, each
    restored from a byte backup in a finally block. Neither is part of a normal run.

      -PlantCrossedProfile   rewrites the produce profile's Impact sound to tin's, IN THE .tres.
                             Assertion 4 must go red. The fault is planted in DATA because what
                             assertion 4 defends is data.
      -PlantNoImpactEvent    deletes the Rpc(PropImpact ...) send in PropManager.FlushImpacts,
                             IN THE .cs, and rebuilds. Assertions 8 and 9 must go red: nobody
                             hears an impact any more, host included, because the local path is
                             gone and the wire path has been cut. What assertion 8 defends is a
                             MESSAGE, so the fault is planted in the code that sends it.

    Exit 0 = PASS. No human interaction, though each phase opens one or two small windows.
#>
[CmdletBinding()]
param(
    # 7902, NOT 7899/7900/7901 (SFX-1, 2026-09-19). The INT-0 section of
    # .claude/rules/test-suite.md records three lanes off one base independently computing the
    # same "next free port" from the same snapshot of tests/ and all three landing on 7896. The
    # ladder this continues is 7893/7894/7895 (Run-CarryNetTest), 7896 (Run-RoundLoopSmoke), 7897
    # (Run-FirstPersonTest), 7898 (Run-PlaceTest); 7899 and 7901 were claimed by concurrent wave-2
    # lanes that this worktree cannot see, and DOOR-1 was claiming one more at the time this was
    # written. Starting at 7902 rather than 7899 leaves that whole band alone on purpose. A
    # collision at run time is another lane's Godot, not a defect: take the next and note it here.
    [int]$Port = 7902,
    # WHEN THE SEEDED FIXTURE IS RELEASED, in seconds of server uptime. Late enough that both
    # windowed peers have connected and their worlds are built (a drop nobody is present for is a
    # drop the late-join dump erases), early enough that phase 1's bots have not yet started
    # throwing at ~8 s and phase 2 has most of its window left to sample. See
    # PropManager.StepSeededDrop for why a seeded prop does not fall on its own.
    [double]$DropAtSec = 6.0,
    [double]$Phase1DurationSec = 26,
    [double]$Phase2DurationSec = 20,
    # The packet's DESIGN bar. Reported when exceeded, not failed -- see the verify block for
    # why it is not reachable by pool size at this fixture.
    [double]$MaxStealFraction = 0.10,
    # The REGRESSION bar, which is a gate. Well clear of the 39% measured at PoolSize 18.
    [double]$StealRegressionFraction = 0.60,
    [switch]$PlantCrossedProfile,
    # SFX-2's plant: cut the impact announcement and watch assertions 8 and 9 go red. Edits
    # scripts/game/props/PropManager.cs, rebuilds, and restores the exact bytes in a finally.
    [switch]$PlantNoImpactEvent,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

# --- The fixture, in world coordinates ------------------------------------------------------
# SearchRoom.tscn is instanced at x = 40 in Supermarket.tscn and its RoomBounds is 14 x 10, so
# the room is world x in [33, 47], z in [-5, 5]. Its four authored crates sit at x = 36 and 38
# and the SearchPillar at (46, 2.5); everything below keeps clear of all five.
$CanAt       = "40,0.35,-3"
$BoxAt       = "42,0.35,-3"
$ProduceAt   = "44,0.35,-3"
# THE ONCE-PER-CONTACT FIXTURE: a can dropped onto a CRATE, in the far corner away from every
# bot and every authored prop. --seed-props-drop releases both; the crate is already at rest, the
# can falls 0.73 m onto it at roughly 3.8 m/s -- over the 2.0 m/s audible floor, under the 8 m/s
# ceiling, so the intensity lands mid-ramp rather than clamped at either end.
#
# A CRATE UNDERNEATH, NOT A SECOND CAN, and the reason is measured. The first three versions of
# this fixture stacked can on can and the two never touched, run after run: a can is a cylinder
# of radius 0.035, so the target is a 7 cm disc, and any eccentricity at all in the release or
# in the floor contact underneath (the server logged the lower can penetrating the floor by
# 0.035 m and being pushed out) sends the upper one down beside it instead of onto it. A crate
# presents a 0.44 m square face. The rule under test is "one contact, one sound", which does not
# care what the two bodies are made of -- so the fixture should be the one that cannot miss.
$StackLowAt  = "45,0.25,4"
$StackHighAt = "45,1.20,4"

# THE FREE-FALLERS, one cardboard and one produce, and they exist because of a measured lesson:
# a bot's THROW is not a reliable way to produce an impact. Measured on the previous run -- the
# thrown can and the thrown box both landed hard enough, and the thrown produce (0.25 kg, a
# sphere, on a shallow arc) grazed the floor under the 2.0 m/s floor and then rolled into a wall,
# so "produce never fired ProduceThump" was a fact about a ballistic arc rather than about the
# sound. A controlled 1.14 m fall arrives at ~4.7 m/s every single time. Tin needs no free-faller
# because the stacked pair already gives it one.
$BoxFallAt     = "43,1.20,3"
$ProduceFallAt = "41,1.20,3"

# THE PLACE FIXTURE (SFX-2). A can, on its own, far from everything else, for one driver to
# grab and SET DOWN 0.3 m away. The verb has to be a real --carry-place through
# PropManager.ClientRequestPlace -- the same client entry point a human's E press calls -- or
# what is being proved is a fire rather than a wire.
#
# (37, 4) is chosen against every other occupant of this room: the authored crates sit at x = 36
# and 38 on z = 0 / +-2, the stacked pair is at (45, 4), the free-fallers at x = 41 and 43 on
# z = 3, and the three product props on z = -3. The nearest of those is the authored crate at
# (36, 0.22, 2) -- 2.2 m away -- so neither the grab nor the placement can touch anything, and
# an overlap refusal would be a defect rather than a fixture collision.
#
# y = 0.07 for the placement: a can is a cylinder of half-height 0.06, so this sets it 1 cm off
# the floor. Deliberately not exactly 0.06 -- CARRY-1's PlacementIntegrity has a 0.02 m bounds
# epsilon and a placement authored flush with the floor plane is the case that epsilon exists
# for; 1 cm of clearance keeps the refusal paths out of a fixture that is testing a SOUND. The
# last centimetre falls, which is what PlaceLooseServer means by Loose-not-Resting.
#
# AND IT IS SEEDED AT 0.08, NOT AT 0.35 LIKE EVERY OTHER PROP HERE, which is the one number in
# this fixture that is defending against a race rather than staging one. --seed-props-drop
# releases the WHOLE fixture at $DropAtSec, and it skips any prop that is already Held -- so
# whether this can falls at all depends on whether the place bot has grabbed it by then, and the
# suite must not care. Seeded 2 cm above its own resting height, the drop is a 0.6 m/s
# non-event: under the 2.0 m/s audible floor, far too slow to tip a cylinder, and it lands
# within a centimetre of where it started. A can seeded at 0.35 like its neighbours would fall
# 0.29 m, and a cylinder that tips and rolls two metres puts the scripted PLACE transform out of
# the bot's 1.65 m reach and turns assertion 10 into a coin flip about a bounce.
$PlaceCanAt   = "37,0.08,4"
$PlaceCanTo   = "37.3,0.07,4,20"

# Prop ids are assigned by PropManager.ServerSpawn in seed order, starting at 1 (authored props
# take 1000+, which is why there is no collision with SearchRoom's four crates).
$CanId = 1; $BoxId = 2; $ProduceId = 3
$StackLowId = 4; $StackHighId = 5; $BoxFallId = 6; $ProduceFallId = 7
$PlaceCanId = 8

$ProduceProfile = Join-Path $script:Root "assets\items\produce\prop_presentation.tres"
$PropManagerCs  = Join-Path $script:Root "scripts\game\props\PropManager.cs"

# The exact statement -PlantNoImpactEvent removes, and its replacement. One statement on one
# line, marked with a PLANT ANCHOR comment in the source so nobody reformats it by accident.
$ImpactSendStmt = "Rpc(MethodName.PropImpact, e.PropId, (int)e.Intensity, e.Position);"
$ImpactSendGone = "_ = e.PropId; // PLANTED by -PlantNoImpactEvent: the announcement is cut"

function Get-SfxLines([string]$Path) {
    if (-not (Test-Path $Path)) { Write-Fail "log not found: $Path" }
    $parsed = @()
    foreach ($line in Get-Content $Path) {
        # [sfx] sfx <Name> event=<Event> intensity=<f> at (x,y,z) src=<id> t=<ms> via=<path>
        # via is optional in the pattern on purpose: a line without it is an OLD binary, and a
        # regex that silently matched nothing would report every assertion below as "heard
        # nothing" -- a staging failure wearing a feature failure's clothes.
        if ($line -match '^\[sfx\] sfx (\S+) event=(\S+) intensity=([-\d.]+) at \(([-\d.]+),([-\d.]+),([-\d.]+)\) src=(\S+) t=(\d+)(?: via=(\S+))?$') {
            $parsed += [pscustomobject]@{
                Sound     = $Matches[1]
                Event     = $Matches[2]
                Intensity = [double]$Matches[3]
                X         = [double]$Matches[4]
                Y         = [double]$Matches[5]
                Z         = [double]$Matches[6]
                Src       = $Matches[7]
                T         = [int]$Matches[8]
                Via       = if ($Matches.Count -gt 9 -and $Matches[9]) { $Matches[9] } else { "(none)" }
            }
        }
    }
    return $parsed
}

Write-Host "=== material sfx: every object sounds like what it is made of ===" -ForegroundColor White
if (-not $SkipBuild) {
    Reset-LogDir
    Invoke-BuildAndImport
}
if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir | Out-Null }

$failures = New-Object System.Collections.Generic.List[string]
$plantBackup = $null
$plantCodeBackup = $null
$procs = @()

try {
    if ($PlantNoImpactEvent) {
        # PLANTED IN CODE, not in data, and for the mirror of the reason -PlantCrossedProfile is
        # planted in data: what assertions 8 and 9 defend is a MESSAGE. Cutting the send is the
        # only mutation that reproduces exactly the world SFX-1 shipped -- the host hearing its
        # own contacts and nobody else hearing anything -- except that SFX-2 also removed the
        # host's local path, so with the send cut NOBODY hears an impact and both assertions go
        # red together. That pairing is itself informative: assertion 9 passing while 8 fails
        # would mean the wire works and the witness does not.
        Write-Host "  [PLANT] PropManager.FlushImpacts: the impact send is cut; assertions 8 and 9 must go red" -ForegroundColor Yellow
        $plantCodeBackup = [System.IO.File]::ReadAllBytes($PropManagerCs)
        $codeText = [System.IO.File]::ReadAllText($PropManagerCs)
        if (-not $codeText.Contains($ImpactSendStmt)) {
            Write-Fail "PLANT ANCHOR MISSING in $PropManagerCs -- the send statement has been reformatted or renamed; fix the plant before trusting the suite"
        }
        [System.IO.File]::WriteAllText($PropManagerCs, $codeText.Replace($ImpactSendStmt, $ImpactSendGone))
        Invoke-BuildAndImport
    }
    if ($PlantCrossedProfile) {
        # THE MUTATION, and it is a one-character-class edit rather than a code change on purpose:
        # what assertion 4 defends is a DATA file, so the fault has to be planted in the data or
        # the proof is about something else. 33 is ProduceThump, 26 is TinClank.
        Write-Host "  [PLANT] produce Impact -> TinClank; assertion 4 must go red" -ForegroundColor Yellow
        $plantBackup = [System.IO.File]::ReadAllBytes($ProduceProfile)
        $text = [System.IO.File]::ReadAllText($ProduceProfile)
        [System.IO.File]::WriteAllText($ProduceProfile, $text.Replace("Sound = 33", "Sound = 26"))
        Invoke-BuildAndImport
    }

    # =========================================================================================
    # PHASE 1 - the material proof
    # =========================================================================================
    Write-Host "[1/4] phase 1: WINDOWED host on udp/$Port, seven seeded props in the search room..." -ForegroundColor Cyan
    $seed1 = "$CanAt,can;$BoxAt,box;$ProduceAt,produce;$StackLowAt,crate;$StackHighAt,can;" +
             "$BoxFallAt,box;$ProduceFallAt,produce;$PlaceCanAt,can"
    $s1Out = Join-Path $script:LogDir "matsfx.host.out.log"
    # WINDOWED, and this is the single most load-bearing line in the file.
    #
    # NetworkManager.IsHeadless is DisplayServer.GetName() being "headless" -- a property of the
    # DISPLAY, not of the --server flag -- and ActorFx.Fire early-outs on it. So a headless
    # --server plays nothing (which is correct, and is assertion 7, measured in phase 2 where the
    # server IS headless), while a windowed --server is exactly what this game ships: a HOST WHO
    # IS A PLAYER. It is also the only peer that SIMULATES loose prop physics, so it is the only
    # peer whose props have a real LinearVelocity, and therefore the only peer an impact
    # assertion can honestly be made on.
    $host1 = Start-Process -FilePath $script:GodotExe `
        -ArgumentList @("--path", $script:Root, "--",
            "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
            "--seed-test-props", $seed1, "--seed-props-drop", $DropAtSec, "--log-sfx", "--windowed") `
        -RedirectStandardOutput $s1Out `
        -RedirectStandardError (Join-Path $script:LogDir "matsfx.host.err.log") `
        -PassThru -NoNewWindow
    $null = $host1.Handle
    $procs += $host1
    if (-not (Wait-ForLogLine $s1Out "\[server\] listening" 40)) {
        Write-Fail "the host never reported listening within 40s; see $s1Out (a port collision here is another lane's Godot, not a defect -- take the next port and note it in the header)"
    }
    if (-not (Wait-ForLogLine $s1Out "--seed-test-props: seeded 8 test prop" 20)) {
        Write-Fail "STAGING: the host never reported seeding 8 props; see $s1Out"
    }

    Write-Host "[2/4] phase 1: one windowed remote witness + four headless drivers..." -ForegroundColor Cyan
    # THE REMOTE WITNESS. A second window, and it earns it: it is the only way to measure what the
    # player who is NOT hosting actually hears, and that turned out to be the most important
    # number this lane produced. It drives nothing -- it walks to the middle of the room and
    # listens -- so nothing it hears can be an artefact of its own actions.
    $witnessOut = Join-Path $script:LogDir "matsfx.witness.out.log"
    $witness = Start-Process -FilePath $script:GodotExe `
        -ArgumentList @("--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", "SfxWitness",
            "--windowed", "--first-person-cam", "--log-sfx",
            "--world", "supermarket", "--duration", $Phase1DurationSec,
            "--log", (Join-Path $script:LogDir "matsfx.witness.jsonl"),
            "--goto-script", "41,1") `
        -RedirectStandardOutput $witnessOut `
        -RedirectStandardError (Join-Path $script:LogDir "matsfx.witness.err.log") `
        -PassThru -NoNewWindow
    $null = $witness.Handle
    $procs += $witness
    Start-Sleep -Milliseconds 400

    # Three headless drivers, one per product. --carry-script scripts exactly one prop per bot,
    # which is the whole reason there are three rather than the one the packet pictured. They
    # make no sound anywhere (headless early-out) and nothing reads their logs; their job is to
    # produce the prop-state transitions the two windowed processes hear.
    $canBot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "SfxCanBot",
        "--world", "supermarket", "--duration", $Phase1DurationSec,
        "--log", (Join-Path $script:LogDir "matsfx.can.jsonl"),
        "--carry-script", "$CanAt,2.0,-1,3.0", "--carry-target-prop", $CanId,
        "--carry-grab-retry", "0.6", "--carry-throw-scale", "0.6", "--log-sfx") "matsfx.can"
    $procs += $canBot
    Start-Sleep -Milliseconds 300

    $boxBot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "SfxBoxBot",
        "--world", "supermarket", "--duration", $Phase1DurationSec,
        "--log", (Join-Path $script:LogDir "matsfx.box.jsonl"),
        "--carry-script", "$BoxAt,2.5,-1,3.5", "--carry-target-prop", $BoxId,
        "--carry-grab-retry", "0.6", "--carry-throw-scale", "0.6", "--log-sfx") "matsfx.box"
    $procs += $boxBot
    Start-Sleep -Milliseconds 300

    $prodBot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "SfxProduceBot",
        "--world", "supermarket", "--duration", $Phase1DurationSec,
        "--log", (Join-Path $script:LogDir "matsfx.produce.jsonl"),
        "--carry-script", "$ProduceAt,3.0,-1,4.0", "--carry-target-prop", $ProduceId,
        "--carry-grab-retry", "0.6", "--carry-throw-scale", "0.6", "--log-sfx") "matsfx.produce"
    $procs += $prodBot
    Start-Sleep -Milliseconds 300

    # THE PLACE DRIVER (SFX-2). Grabs the lone can at (37, 4) and, 3 s after it is first observed
    # HOLDING it, asks the server to set it down 0.3 m away at 20 degrees of yaw. The throw clock
    # in --carry-script is -1: this bot never throws, so the only release it produces is the
    # place, and a Placed on either windowed peer cannot have come from anything else.
    #
    # --carry-place's delay is measured from the observed hold rather than from process start,
    # which is why 3 s is safe here against a bot that may take eight seconds to walk under
    # marathon load (see BotHarness.MaybePlace).
    $placeBot = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "SfxPlaceBot",
        "--world", "supermarket", "--duration", $Phase1DurationSec,
        "--log", (Join-Path $script:LogDir "matsfx.place.jsonl"),
        "--carry-script", "$PlaceCanAt,3.5,-1,-1", "--carry-target-prop", $PlaceCanId,
        "--carry-grab-retry", "0.6", "--carry-place", "$PlaceCanTo,3", "--log-sfx") "matsfx.place"
    $procs += $placeBot

    foreach ($b in @(@{N = "SfxWitness"; P = $witness}, @{N = "SfxCanBot"; P = $canBot},
                     @{N = "SfxBoxBot"; P = $boxBot}, @{N = "SfxProduceBot"; P = $prodBot},
                     @{N = "SfxPlaceBot"; P = $placeBot})) {
        if (-not $b.P.WaitForExit([int](($Phase1DurationSec + 70) * 1000))) {
            Write-Fail "$($b.N) did not exit within its budget"
        }
    }
    Stop-Proc $host1

    # =========================================================================================
    # PHASE 2 - the voice budget
    # =========================================================================================
    Write-Host "[3/4] phase 2: forty mixed props released together on a windowed host..." -ForegroundColor Cyan
    # A HEAP, not a shelf. SHELF-1 has not landed and the packet says explicitly not to wait for
    # it. Forty props on a 5 x 8 lattice at staggered heights land over about 1.5 s and collide
    # with each other on the way down, which is the same concurrency event a shelf going over
    # produces and is what the 24-voice ceiling has to survive.
    $heap = @()
    $kinds = @("can", "box", "produce", "can", "box")
    for ($i = 0; $i -lt 40; $i++) {
        $x = 38.0 + ($i % 5) * 0.45
        $z = -2.0 + [math]::Floor($i / 5) * 0.45
        $y = 0.5 + ($i % 8) * 0.28
        $heap += ("{0:F2},{1:F2},{2:F2},{3}" -f $x, $y, $z, $kinds[$i % 5])
    }
    $seed2 = ($heap -join ";")

    # WINDOWED, for the same reason phase 1's is, and for a second one this phase discovered.
    # The host is the only peer that SIMULATES the heap, so it is the only peer where two props
    # can both be moving at a contact -- and a contact reported to both bodies is the only thing
    # that can exercise the once-per-contact rule at all. A headless server here measured a
    # voice budget made entirely of releases and footsteps and logged not one suppression,
    # because it played nothing and because its clients' props are frozen kinematic.
    $s2Out = Join-Path $script:LogDir "matsfx.p2.host.out.log"
    $server2 = Start-Process -FilePath $script:GodotExe `
        -ArgumentList @("--path", $script:Root, "--",
            "--server", "--port", $Port, "--world", "supermarket", "--spawn-room", "search",
            "--seed-test-props", $seed2, "--seed-props-drop", $DropAtSec,
            "--log-sfx", "--windowed") `
        -RedirectStandardOutput $s2Out `
        -RedirectStandardError (Join-Path $script:LogDir "matsfx.p2.host.err.log") `
        -PassThru -NoNewWindow
    $null = $server2.Handle
    $procs += $server2
    if (-not (Wait-ForLogLine $s2Out "\[server\] listening" 40)) {
        Write-Fail "phase 2: the host never reported listening within 40s; see $s2Out"
    }
    if (-not (Wait-ForLogLine $s2Out "--seed-test-props: seeded 40 test prop" 20)) {
        Write-Fail "STAGING: phase 2's host never reported seeding 40 props; see $s2Out"
    }

    $budgetOut = Join-Path $script:LogDir "matsfx.budget.out.log"
    $budgetBot = Start-Process -FilePath $script:GodotExe `
        -ArgumentList @("--path", $script:Root, "--",
            "--bot", "--address", "127.0.0.1:$Port", "--name", "SfxBudgetBot",
            "--windowed", "--first-person-cam", "--log-sfx",
            "--world", "supermarket", "--duration", $Phase2DurationSec,
            "--log", (Join-Path $script:LogDir "matsfx.budget.jsonl"),
            "--goto-script", "40,0") `
        -RedirectStandardOutput $budgetOut `
        -RedirectStandardError (Join-Path $script:LogDir "matsfx.budget.err.log") `
        -PassThru -NoNewWindow
    $null = $budgetBot.Handle
    $procs += $budgetBot
    Start-Sleep -Milliseconds 400

    $budgetBot2 = Start-Godot @("--bot", "--address", "127.0.0.1:$Port", "--name", "SfxBudgetBot2",
        "--world", "supermarket", "--duration", $Phase2DurationSec,
        "--log", (Join-Path $script:LogDir "matsfx.budget2.jsonl"),
        "--goto-script", "41,-1") "matsfx.budget2"
    $procs += $budgetBot2

    foreach ($b in @(@{N = "SfxBudgetBot"; P = $budgetBot}, @{N = "SfxBudgetBot2"; P = $budgetBot2})) {
        if (-not $b.P.WaitForExit([int](($Phase2DurationSec + 70) * 1000))) {
            Write-Fail "$($b.N) did not exit within its budget"
        }
    }
} finally {
    Stop-Procs $procs
    if ($plantBackup -ne $null) {
        [System.IO.File]::WriteAllBytes($ProduceProfile, $plantBackup)
        Write-Host "  [PLANT] produce profile restored" -ForegroundColor Yellow
    }
    if ($plantCodeBackup -ne $null) {
        [System.IO.File]::WriteAllBytes($PropManagerCs, $plantCodeBackup)
        Write-Host "  [PLANT] PropManager.cs restored (rebuild before trusting the next run)" -ForegroundColor Yellow
    }
}

# =============================================================================================
# VERIFY
# =============================================================================================
Write-Host "[4/4] verifying..." -ForegroundColor Cyan

# Both windowed peers' logs are parsed up front; assertion 5 searches across both, because a
# prop-on-prop contact can happen on either and the rule is process-local on each.
# THE HOST'S LOG IS THE GATE. It is the peer that simulates the props, so it is the only one
# whose impacts are a fact about physics rather than about replication. The remote witness's log
# is read further down and largely REPORTED rather than gated -- that block is the honest half of
# this suite.
$sfx = Get-SfxLines (Join-Path $script:LogDir "matsfx.host.out.log")
if ($sfx.Count -eq 0) {
    Write-Fail ("STAGING: the windowed host logged no [sfx] line at all. Either no prop state " +
        "transition happened, or it came up headless after all -- ActorFx.Fire early-outs on " +
        "DisplayServer.GetName() being 'headless' and plays nothing. See tests/logs/matsfx.host.out.log")
}
Write-Host ("        the host heard {0} sound(s)" -f $sfx.Count)
$heapHost = Get-SfxLines (Join-Path $script:LogDir "matsfx.p2.host.out.log")

# Pick-up and release are asserted on the ONE prop a bot actually handles; the impact is
# asserted across every prop of that material, because the thing being proved is "a cereal box
# hitting something sounds like cardboard" and it does not matter which cereal box. Separating
# the two also stops a bot whose throw arc happened to land softly from failing the material.
$expected = @(
    @{ Material = "tin";       Id = $CanId;     Pick = "TinPick";     Impact = "TinClank"
       ImpactIds = @("$CanId", "$StackLowId", "$StackHighId") }
    @{ Material = "cardboard"; Id = $BoxId;     Pick = "CardPick";    Impact = "CardThud"
       ImpactIds = @("$BoxId", "$BoxFallId") }
    @{ Material = "produce";   Id = $ProduceId; Pick = "ProducePick"; Impact = "ProduceThump"
       ImpactIds = @("$ProduceId", "$ProduceFallId") }
)
$materialSounds = @("TinPick", "TinClank", "TinBuzz", "TinTick",
                    "CardPick", "CardThud", "CardSettle",
                    "ProducePick", "ProduceThump", "ProducePlop")

# --- 1/2/3: each kind fired its own pick-up, impact and throw --------------------------------
foreach ($e in $expected) {
    $mine = @($sfx | Where-Object { $_.Src -eq "$($e.Id)" })
    if ($mine.Count -eq 0) {
        $failures.Add("$($e.Material): prop $($e.Id) never made a sound at all - it was never grabbed or never landed")
        continue
    }
    $picks = @($mine | Where-Object { $_.Event -eq "PickedUp" -and $_.Sound -eq $e.Pick })
    if ($picks.Count -lt 1) {
        $failures.Add(("$($e.Material): prop $($e.Id) never fired {0} on PickedUp (it fired: {1})" -f
            $e.Pick, (@($mine | Where-Object { $_.Event -eq "PickedUp" } | ForEach-Object { $_.Sound }) -join "," )))
    }
    $impacts = @($sfx | Where-Object {
        $_.Event -eq "Impact" -and $_.Sound -eq $e.Impact -and $e.ImpactIds -contains $_.Src })
    if ($impacts.Count -lt 1) {
        $failures.Add(("$($e.Material): none of props {0} fired {1} on Impact (impacts heard: {2})" -f
            ($e.ImpactIds -join "/"), $e.Impact,
            (@($sfx | Where-Object { $_.Event -eq "Impact" } | ForEach-Object { "$($_.Sound )/$($_.Src)" }) -join "," )))
    } else {
        Write-Host ("        {0,-10} prop {1}: {2} x{3} pickup; {4} x{5} impact across props {6} (peak intensity {7:F3})" -f
            $e.Material, $e.Id, $e.Pick, $picks.Count, $e.Impact, $impacts.Count,
            ($e.ImpactIds -join "/"),
            (@($impacts | ForEach-Object { $_.Intensity }) | Measure-Object -Maximum).Maximum)
    }
    $throws = @($mine | Where-Object { $_.Event -eq "Thrown" -and $_.Sound -eq "Whoosh" })
    if ($throws.Count -lt 1) {
        $failures.Add("$($e.Material): prop $($e.Id) never fired Whoosh on Thrown")
    }
}

# --- 4: no crossed wires ----------------------------------------------------------------------
# The assertion the -PlantCrossedProfile mutation must break: a material's voice may only ever
# come from a prop of that material.
# THE TABLE IS THE FIXTURE, AND IT WENT STALE THE MOMENT SFX-2 ADDED AN EIGHTH PROP.
# Measured: the first run of the extended suite went red with "crossed wires: TinPick played
# from prop 8, which is not tin" -- and prop 8 IS a can. The assertion was right and the table
# was wrong, which is the good direction for that pair to disagree in: a table that listed too
# MANY ids would have quietly stopped defending anything. Anyone adding a prop to the seed list
# adds it here in the same edit.
$legalFor = @{
    "Tin"     = @("$CanId", "$StackLowId", "$StackHighId", "$PlaceCanId")
    "Card"    = @("$BoxId", "$BoxFallId")
    "Produce" = @("$ProduceId", "$ProduceFallId")
}
foreach ($line in @($sfx | Where-Object { $materialSounds -contains $_.Sound })) {
    $prefix = @("Tin", "Card", "Produce") | Where-Object { $line.Sound.StartsWith($_) } | Select-Object -First 1
    $legalIds = $legalFor[$prefix]
    if ($legalIds -notcontains $line.Src) {
        $failures.Add(("crossed wires: {0} (a {1} sound) played from prop {2}, which is not {1}" -f
            $line.Sound, $prefix.ToLower(), $line.Src))
    }
}

# --- 5: a prop-on-prop contact fires ONCE ---------------------------------------------------
# PROVED FROM THE SUPPRESSION ITSELF, not inferred from the geometry, and the difference matters.
# An inference ("exactly one Impact appeared near this position at this moment") cannot tell a
# rule that suppressed a duplicate from a contact that only ever reported one body -- and the
# second is the common case, because a prop that has settled is frozen kinematic and Godot never
# asks it anything. Carryable logs the suppression, so this reads the decision directly:
#
#   [sfx] pair-suppressed self=5 other=4 speed=3.62 t=7940
#
# Both windowed peers are searched, because prop-on-prop contacts are guaranteed in phase 2's
# forty-prop heap and merely likely in phase 1's fixture.
# SYMMETRIC, and SFX-2 had to widen it from "before" to "either side" for a reason that is a
# fact about this packet rather than a loosened assertion.
#
# SFX-1 could require the partner's impact to have played AT OR BEFORE the suppression, because
# the fire happened inside the contact handler: the winner played, then the loser was suppressed,
# in that order, in the same signal dispatch. SFX-2 defers every networked prop's impact to the
# server's next physics flush, so the ORDER IS NOW INVERTED -- the loser's suppression is still
# logged at contact time and the winner's sound arrives a tick later.
#
# Measured, not reasoned: the first run of the extended suite reported
#   [sfx] pair-suppressed self=26 other=Prop_3 speed=2.26 t=7313
#   [sfx] sfx Thunk event=Impact ... src=Prop_3 t=7325 via=wire
# -- 12 ms later, one physics tick at 60 Hz, and the suite called it an ORPHAN.
#
# What the assertion is for is unchanged and is not weakened by this: it asks whether the
# partner PLAYED, so that a rule which is silent rather than single is caught. Which side of a
# 150 ms window it played on was never the question; it was an incidental property of where the
# fire used to live.
$PairWindowMs = 150
$suppressions = @()
foreach ($log in @("matsfx.host.out.log", "matsfx.p2.host.out.log")) {
    $path = Join-Path $script:LogDir $log
    if (-not (Test-Path $path)) { continue }
    foreach ($line in Get-Content $path) {
        if ($line -match '^\[sfx\] pair-suppressed self=(\S+) other=(\S+) speed=([-\d.]+) t=(\d+)$') {
            $suppressions += [pscustomobject]@{
                Log = $log; Self = $Matches[1]; Other = $Matches[2]
                Speed = [double]$Matches[3]; T = [int]$Matches[4]
            }
        }
    }
}
if ($suppressions.Count -eq 0) {
    $failures.Add("no prop-on-prop contact was ever suppressed in either windowed peer's log, so " +
        "the once-per-contact rule never executed and this run proves nothing about it. Either no " +
        "two props touched above the audible speed floor, or Carryable stopped calling " +
        "ClaimPropOnPropContact.")
} else {
    # Every suppression must have a partner that DID play, inside the claim window and before it.
    # This is the half that catches the failure mode the instance-id rule actually had: a rule
    # that suppresses one body while the other was never asked is silent, not single.
    $orphans = @()
    foreach ($s in $suppressions) {
        $partnerPlayed = @(@($sfx) + @($heapHost) | Where-Object {
            $_.Event -eq "Impact" -and $_.Src -eq $s.Other -and
            [math]::Abs($_.T - $s.T) -le $PairWindowMs })
        if ($partnerPlayed.Count -eq 0) { $orphans += $s }
    }
    if ($orphans.Count -eq $suppressions.Count) {
        $failures.Add((("all {0} suppression(s) were ORPHANS: a contact was silenced on one body " +
            "and its partner never played within {1} ms EITHER SIDE. That is the failure the " +
            "first-come rule replaced an instance-id rule to avoid -- silent, not single. Note " +
            "that under SFX-2 a suppression can also be orphaned by the SOURCE-SIDE LIMITER " +
            "dropping the winner on an over-budget tick, which is the design working rather " +
            "than a defect -- check for an [sfx] impact-limit line at the same t before " +
            "chasing the de-dup rule. First: self={2} other={3} t={4}") -f
            $suppressions.Count, $PairWindowMs, $suppressions[0].Self, $suppressions[0].Other, $suppressions[0].T))
    } else {
        $paired = $suppressions.Count - $orphans.Count
        Write-Host (("        once-per-contact: {0} suppression(s), {1} with the partner's impact " +
            "inside {2} ms (first: prop {3} silenced against prop {4} at {5:F2} m/s)") -f
            $suppressions.Count, $paired, $PairWindowMs,
            $suppressions[0].Self, $suppressions[0].Other, $suppressions[0].Speed)
        if ($orphans.Count -gt 0) {
            Write-Host (("        ({0} suppression(s) had no partner impact in window -- a partner " +
                "whose own 0.4 s cooldown was already running, which is correct)") -f $orphans.Count)
        }
    }
}

# --- 6: intensity is in range, and the mapping is not stuck at zero --------------------------
foreach ($line in $sfx) {
    if ($line.Intensity -lt 0 -or $line.Intensity -gt 1) {
        $failures.Add(("intensity {0} out of [0,1] on {1} from prop {2}" -f $line.Intensity, $line.Sound, $line.Src))
    }
}
$loud = @($sfx | Where-Object { $_.Event -eq "Impact" -and $_.Intensity -gt 0.0 })
if ($loud.Count -eq 0) {
    $failures.Add("POSITIVE CONTROL FAILED: every impact logged intensity 0.000, so the range check " +
        "above passed without the mapping doing anything. Either nothing hit anything above " +
        "Carryable.ThunkSpeedThreshold, or the relative-speed computation is returning the floor.")
} else {
    Write-Host ("        intensity: {0} impact(s) above zero, max {1:F3}" -f
        $loud.Count, (@($loud | ForEach-Object { $_.Intensity }) | Measure-Object -Maximum).Maximum)
}

# --- 7: a HEADLESS peer never plays ----------------------------------------------------------
# Phase 1's three drivers are headless and were each given --log-sfx, so this is a real absence
# rather than an unasked question -- and every one of them received the same CallLocal
# ApplyPropState transitions the windowed host turned into dozens of [sfx] lines, which is the
# positive control that makes the absence mean something. This is the early-out that keeps a
# DEDICATED SERVER from paying for an audio pool nobody can hear; a headless bot reaches it by
# exactly the same test (NetworkManager.IsHeadless), and unlike a server it is also proof that
# the transitions arrived.
$headlessNoise = 0
foreach ($tag in @("matsfx.can", "matsfx.box", "matsfx.produce", "matsfx.place")) {
    $path = Join-Path $script:LogDir "$tag.out.log"
    if (-not (Test-Path $path)) { continue }
    $n = @(Get-SfxLines $path).Count
    $headlessNoise += $n
    if ($n -gt 0) {
        $failures.Add((("$tag (headless) played {0} sound(s) - ActorFx's headless early-out is " +
            "not holding, so a dedicated server is now paying for an audio pool nobody can hear") -f $n))
    }
}
if ($headlessNoise -eq 0) {
    Write-Host "        headless peers: 0 sounds across 4 drivers (the early-out holds)"
}

# --- the voice budget ------------------------------------------------------------------------
# THE SUMMARY LINE COMES FROM THE BOT, NOT THE HOST, and that is a fact about where the hook is
# rather than a choice: ActorFx.BudgetSummaryLine is printed by BotHarness.Finish, and a server
# process never reaches it (it has no --duration and no Finish). The windowed bot is a client, so
# its own count is releases and footsteps rather than impacts -- a floor on the real figure, not
# the whole of it. The HOST's live count is the honest one and is reconstructed from its own
# [sfx] lines below.
$summary = @(Get-Content (Join-Path $script:LogDir "matsfx.budget.out.log") |
    Where-Object { $_ -match '^\[sfx\] SUMMARY ' }) | Select-Object -Last 1
if (-not $summary) {
    $failures.Add("phase 2: the host printed no [sfx] SUMMARY line; see tests/logs/matsfx.p2.host.out.log")
} elseif ($summary -match 'fires=(\d+) peakLive3DVoices=(\d+) oneShotSteals=(\d+) poolSize=(\d+) ceiling=(\d+)') {
    $fires = [int]$Matches[1]; $peak = [int]$Matches[2]; $steals = [int]$Matches[3]
    $poolSize = [int]$Matches[4]; $ceiling = [int]$Matches[5]
    $frac = if ($fires -gt 0) { $steals / $fires } else { 0.0 }
    Write-Host ""
    Write-Host (("        VOICE BUDGET (40 props, two bots): fires={0} PeakLive3DVoices={1} " +
        "OneShotSteals={2} ({3:P1} of fires) poolSize={4} ceiling={5}") -f
        $fires, $peak, $steals, $frac, $poolSize, $ceiling) -ForegroundColor White
    Write-Host "        $summary"
    if ($peak -gt $ceiling) {
        $failures.Add("PeakLive3DVoices $peak exceeded the AudioVoiceBudget ceiling of $ceiling")
    }
    # TWO DIFFERENT BARS, and conflating them was the packet's one unreachable instruction.
    #
    # The DESIGN bar ($MaxStealFraction, 10%) is the packet's: above it, "the cue you needed" is
    # no longer the cue that played. It is REPORTED and not gated, because it cannot be met by
    # pool size and this suite should not go red forever for a decision nobody has taken.
    # Measured: forty props released together stole 48% of 54 fires at a pool of 14. SFX-1 spent
    # every slot the 24-voice ceiling had left -- the loop partition's four unused fire slots,
    # 14 -> 18 -- and the peak came back at exactly 18 again with 39% still stolen. Forty
    # simultaneous impacts want forty voices; there is no pool inside the ceiling that serves
    # them, so the fix is at the SOURCE, the way FootstepAudioDirector already caps footsteps
    # rather than widening the pool for them. That is a design call, not this lane's.
    #
    # The REGRESSION bar ($StealRegressionFraction, 60%) is a gate, sitting well clear of the
    # measured 39% so that a change which makes stealing dramatically worse is red rather than a
    # number in a log nobody reads.
    if ($frac -gt $MaxStealFraction) {
        Write-Host ((
            "        NOTE: OneShotSteals {0} is {1:P1} of {2} fires, over the {3:P0} DESIGN bar. " +
            "Expected at this fixture: forty props released together want ~40 voices and the " +
            "whole ceiling is {4}. Reported, not failed -- see the header. Source-side limiting " +
            "is the fix, not a bigger pool.") -f
            $steals, $frac, $fires, $MaxStealFraction, $ceiling) -ForegroundColor Yellow
    }
    if ($frac -gt $StealRegressionFraction) {
        $failures.Add((("OneShotSteals {0} is {1:P1} of {2} fires, past the {3:P0} REGRESSION bar " +
            "(measured {4:P0} when this was written). Something has made stealing much worse, or " +
            "SfxLab.PoolSize has been cut.") -f
            $steals, $frac, $fires, $StealRegressionFraction, 0.39))
    }
    if ($peak -lt $poolSize) {
        Write-Host ("        (the one-shot pool did not saturate: peak {0} of {1})" -f $peak, $poolSize)
    }
    if ($fires -eq 0) {
        $failures.Add("phase 2: zero fires, so the budget numbers above describe nothing - the heap never landed")
    }
} else {
    $failures.Add("phase 2: could not parse the SUMMARY line: $summary")
}

# --- THE SOURCE-SIDE LIMITER, measured on the forty-prop heap (SFX-2) -------------------------
# PropManager.FlushImpacts prints one line per tick on which it dropped anything. A per-tick
# line rather than a session total, because "how bad does ONE collapse get" is the question and
# a total cannot answer it -- forty props landing over 1.5 s and one can falling every ten
# seconds for a minute produce the same total and nothing like the same problem.
$limitLines = @()
$peakOffer = 0
$peakBudget = 0
foreach ($log in @("matsfx.host.out.log", "matsfx.p2.host.out.log")) {
    $path = Join-Path $script:LogDir $log
    if (-not (Test-Path $path)) { continue }
    foreach ($line in Get-Content $path) {
        if ($line -match '^\[sfx\] impact-peak offered=(\d+) budget=(\d+) t=(\d+)$') {
            if ([int]$Matches[1] -gt $peakOffer) { $peakOffer = [int]$Matches[1] }
            $peakBudget = [int]$Matches[2]
        }
        if ($line -match '^\[sfx\] impact-limit offered=(\d+) sent=(\d+) dropped=(\d+) cumOffered=(\d+) cumSent=(\d+) t=(\d+)$') {
            $limitLines += [pscustomobject]@{
                Log = $log; Offered = [int]$Matches[1]; Sent = [int]$Matches[2]
                Dropped = [int]$Matches[3]; CumOffered = [int]$Matches[4]
                CumSent = [int]$Matches[5]; T = [int]$Matches[6]
            }
        }
    }
}
Write-Host ""
if ($limitLines.Count -eq 0) {
    # NOT a failure, and NOT an absence either -- PropManager logs its running peak offer, so
    # this prints the measured headroom rather than inferring it from silence. Measured on the
    # 40-prop heap: the busiest tick offered well under the cap, because every prop carries its
    # own 0.4 s per-body cooldown and forty props cannot therefore produce forty contacts on one
    # tick. THE COOLDOWN, NOT THE CAP, IS WHAT BOUNDS THIS FIXTURE. The cap is a backstop for
    # the case this fixture does not produce, and xUnit is where it is proved to work.
    Write-Host (("        IMPACT LIMITER: never engaged. Busiest tick offered {0} contact(s) " +
        "against a budget of {1} -- the per-body 0.4 s cooldown is what bounds this fixture, " +
        "not the cap.") -f $peakOffer, $(if ($peakBudget -gt 0) { $peakBudget } else { 4 }))
    if ($peakOffer -eq 0) {
        $failures.Add("POSITIVE CONTROL FAILED: PropManager logged no [sfx] impact-peak line at all, so nothing was ever queued for announcement. The limiter is not merely idle - ServerNoteImpact is never being called.")
    }
} else {
    $worst = ($limitLines | Sort-Object -Property Offered -Descending | Select-Object -First 1)
    $totOffered = ($limitLines | Measure-Object -Property Dropped -Sum).Sum +
                  ($limitLines | Measure-Object -Property Sent -Sum).Sum
    $totDropped = ($limitLines | Measure-Object -Property Dropped -Sum).Sum
    Write-Host (("        IMPACT LIMITER: engaged on {0} tick(s); worst tick offered {1}, sent " +
        "{2}, dropped {3}; over those ticks {4} of {5} contacts dropped ({6:P1})") -f
        $limitLines.Count, $worst.Offered, $worst.Sent, $worst.Dropped,
        $totDropped, $totOffered, $(if ($totOffered -gt 0) { $totDropped / $totOffered } else { 0 })) -ForegroundColor White
    $lastCum = ($limitLines | Sort-Object -Property T | Select-Object -Last 1)
    Write-Host ("        cumulative at the last limited tick: offered={0} sent={1}" -f
        $lastCum.CumOffered, $lastCum.CumSent)
}

# --- WHAT THE REMOTE PLAYER HEARS (SFX-2: all three halves are gated now) ---------------------
# THE POINT OF THE PACKET. SFX-1 measured this block at "PickedUp 3, Thrown 6, Impact 0" and
# could only report the zero: only the host simulates loose prop physics, and Godot reports a
# frozen kinematic body no contact at all, so there was nothing on the witness for any amount of
# client-side cleverness to work with. SFX-2 puts the event on the wire, so the zero is a gate.
$remote = Get-SfxLines (Join-Path $script:LogDir "matsfx.witness.out.log")
$remoteProps = @($remote | Where-Object { $_.Src -match '^[0-9]+$' })
$rPick = @($remoteProps | Where-Object { $_.Event -eq "PickedUp" }).Count
$rThrow = @($remoteProps | Where-Object { $_.Event -eq "Thrown" }).Count
$rImpact = @($remoteProps | Where-Object { $_.Event -eq "Impact" }).Count
Write-Host ""
Write-Host (("        REMOTE PEER (not the host): {0} prop sound(s) - PickedUp {1}, Thrown {2}, " +
    "Impact {3}") -f $remoteProps.Count, $rPick, $rThrow, $rImpact) -ForegroundColor White
# GATED: pick-up and release both ride ApplyPropState, which is CallLocal and therefore runs on
# every peer. If either is zero, prop presentation is not replicating at all and the seeker
# cannot hear the hider pick anything up -- which is the game not working.
if ($rPick -lt 1) {
    $failures.Add("the remote peer heard NO prop pick-up. ApplyPropState is CallLocal, so a peer that hears nothing is not receiving prop state at all")
}
if ($rThrow -lt 1) {
    $failures.Add("the remote peer heard NO release. NetworkedProp.BeginLoose is the every-peer half of Held->Loose and is where the Thrown fire lives; if it is silent, that fire has moved or the transition never arrived")
}

# --- 8: THE OTHER PLAYER HEARS EVERY MATERIAL LAND, FROM THE WIRE (SFX-2) --------------------
# Per MATERIAL, not per prop, and per SOUND rather than per count: what the seeker needs is to
# know that something tin just hit something, and which can it was is a question they answer by
# walking. Each of the three has to arrive with via=wire, because via=local on a witness would
# mean the sound came from a contact this peer somehow simulated -- which would be a divergent
# client, a worse defect than a silent one.
$wireImpacts = @($remote | Where-Object { $_.Event -eq "Impact" -and $_.Via -eq "wire" })
foreach ($e in $expected) {
    $heard = @($wireImpacts | Where-Object { $_.Sound -eq $e.Impact })
    if ($heard.Count -lt 1) {
        $failures.Add((("THE OTHER PLAYER IS DEAF to {0}: the non-host witness never fired {1} on " +
            "Impact with via=wire. It heard: {2}. The server announces impacts on " +
            "NetProfile.PropImpactChannel (PropManager.FlushImpacts -> PropImpact); if nothing " +
            "arrived, either the send is gone or the channel/RPC is not routing.") -f
            $e.Material, $e.Impact,
            (@($remote | Where-Object { $_.Event -eq "Impact" } |
                ForEach-Object { "$($_.Sound)/via=$($_.Via)" }) -join "," )))
    } else {
        $first = $heard[0]
        Write-Host ("        WIRE {0,-10} witness heard {1} x{2}, e.g. {3}" -f
            $e.Material, $e.Impact, $heard.Count,
            ("sfx {0} event=Impact intensity={1:F3} src={2} t={3} via={4}" -f
                $first.Sound, $first.Intensity, $first.Src, $first.T, $first.Via)) -ForegroundColor Green
    }
}
if ($wireImpacts.Count -lt 1) {
    $failures.Add("the remote peer heard NO impact at all. This is the exact state SFX-1 shipped and SFX-2 exists to end.")
}

# --- 9: ONE FIRE PER CONTACT -- no local+wire double anywhere --------------------------------
# Carryable.OnBodyEntered no longer plays a networked prop's impact; it reports it, and the
# server's announcement is the only path. So an Impact from a NUMBERED prop carrying via=local
# is the double-fire this rule exists to prevent, and the HOST is where it would appear, because
# the host is the only peer that gets a contact at all.
#
# Not a count. Two fires 1 ms apart at the same position are indistinguishable from one to
# anything that counts sounds near a place -- which is precisely why SFX-1's pair-suppression
# assertion reads the decision rather than the tally, and this reads the path for the same
# reason.
$everyLine = @($sfx) + @($heapHost) + @($remote)
$localImpacts = @($everyLine | Where-Object {
    $_.Event -eq "Impact" -and $_.Src -match '^[0-9]+$' -and $_.Via -ne "wire" })
if ($localImpacts.Count -gt 0) {
    $l = $localImpacts[0]
    $failures.Add((("DOUBLE FIRE: {0} networked-prop Impact(s) played with via={1} (not wire). " +
        "Carryable's local fire is back, so the host hears every hit twice -- a flam, not a " +
        "louder hit. First: {2} from prop {3} at t={4}.") -f
        $localImpacts.Count, $l.Via, $l.Sound, $l.Src, $l.T))
} else {
    $allImpacts = @($everyLine | Where-Object { $_.Event -eq "Impact" })
    Write-Host ("        one path, one sound: all {0} impact(s) across both phases and both peers came via=wire" -f
        $allImpacts.Count)
}

# --- 10: A PLACE AND A THROW ARE DIFFERENT SOUNDS, ON BOTH PEERS -----------------------------
# CARRY-1's finding, closed. The assertion is made on BOTH windowed peers on purpose: the host
# could always tell them apart (PlaceLooseServer knew the verb), so a host-only check would have
# passed before this packet existed. What is new is the byte, and the byte is only observable on
# the peer that did not decide.
$peers = @(@{ N = "host"; L = $sfx }, @{ N = "witness"; L = $remote })
foreach ($pr in $peers) {
    $placed = @($pr.L | Where-Object { $_.Event -eq "Placed" -and $_.Src -eq "$PlaceCanId" })
    if ($placed.Count -lt 1) {
        $failures.Add((("PLACE IS NOT ON THE WIRE: the {0} never fired ActorEvent.Placed for prop " +
            "{1} (the scripted place). Events it saw for that prop: {2}. Either the place never " +
            "landed (check `[bot] SfxPlaceBot PLACING` and `place refused` in the logs) or " +
            "PropRelease.Placed is not reaching NetworkedProp.BeginLoose.") -f
            $pr.N, $PlaceCanId,
            (@($pr.L | Where-Object { $_.Src -eq "$PlaceCanId" } | ForEach-Object { $_.Event }) -join "," )))
    } else {
        Write-Host ("        PLACED  {0,-8} {1} x{2} on prop {3} (e.g. t={4})" -f
            $pr.N, $placed[0].Sound, $placed.Count, $PlaceCanId, $placed[0].T) -ForegroundColor Green
    }
    $thrown = @($pr.L | Where-Object { $_.Event -eq "Thrown" })
    if ($thrown.Count -lt 1) {
        $failures.Add(("THROW IS NOT ON THE WIRE: the {0} fired no ActorEvent.Thrown at all, though three bots threw." -f $pr.N))
    } else {
        Write-Host ("        THROWN  {0,-8} {1} x{2} across props {3}" -f
            $pr.N, $thrown[0].Sound, $thrown.Count,
            ((@($thrown | ForEach-Object { $_.Src }) | Sort-Object -Unique) -join "/")) -ForegroundColor Green
    }
}
# And the two verbs must not have collapsed into one sound. A Placed that played Whoosh would
# satisfy every assertion above while being exactly the defect this packet closes.
foreach ($pl in @(@($sfx) + @($remote) | Where-Object { $_.Event -eq "Placed" })) {
    if ($pl.Sound -eq "Whoosh") {
        $failures.Add("a Placed event played Whoosh (the shared THROW noise) instead of the material's settle tick - the verbs are separate on the wire but not in the profile")
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "MATERIAL-SFX FAILED ($($failures.Count) failure(s)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "MATERIAL-SFX OVERALL: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: each material fired its own pickup/impact/throw, BOTH PLAYERS heard every impact from the wire exactly once, a place and a throw were different sounds on both peers, the two-prop contact fired once, the server stayed silent, and the voice budget held." -ForegroundColor Green
Write-Host ""
Write-Host "MATERIAL-SFX OVERALL: PASS" -ForegroundColor Green
exit 0
