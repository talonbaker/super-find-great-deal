<#
.SYNOPSIS
    LD-6: the affordance kit's scene self-test -- the six primitives' validator rules asserted
    against the SHIPPED scenes/dev/KitCourse.tscn, plus the three positive controls that make
    a green here mean something.

.DESCRIPTION
    THE FAILURE THIS SUITE EXISTS TO PREVENT is on the record. The first KitCourse.tscn shipped
    with all 22 of its rotated pieces mirrored -- three Wedges descending, both Sags domed, three
    Tilts leaning the wrong way, the return ramp climbing away from the plateau -- and the
    generator's own validator passed it, because the angle rules are sign-blind and every height
    claim was DECLARED by the same arithmetic that placed the piece. A checker that reconstructs
    its subject the way its subject was built agrees with it, wrong sign and all.

    So there are two authorships and one of them is the engine's. `tools/dev/kit_course.py`
    reconstructs the geometry from pos/rot/shape and stamps its conclusions into the scene;
    `tools/dev/kit_selftest.gd` re-derives the same quantities from the LOADED scene through
    Godot's own transform code and refuses to agree on python's word.

    FOUR STEPS:
      1. The generator's own positive controls (--controls): the motor derivation must reproduce
         MOVE-3e's four in-engine measurements, and a slab authored to descend must reconstruct
         as descending. A checker that cannot tell a ramp from a drop on a fixture where the
         answer is known by construction has no verdict to offer on the course.
      2. Byte-reproducibility: two generator runs to two throwaway paths, hashes compared.
         Deliberately NOT compared against the committed scene -- Talon's 2026-09-02 ruling is
         that the file may be opened and hand-placed in the editor, so the suite asserts the KIT
         RULES against whatever ships rather than byte-equality with a fresh run.
      3. The scene self-test against the shipped scene. Exit 0 required.
      4. TWO POSITIVE CONTROLS, and they are the reason step 3 counts:
         a. the GENERATOR must reject a deliberately mirrored Wedge (--violate wedge-sign);
         b. the SELF-TEST must reject a scene emitted with that same mirror in it.
         Both are printed as expected failures. If either ever stops failing, every green run
         before it was worthless.

    Exit 0 = PASS. No human interaction, no GPU.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Common.ps1"

if (-not $SkipBuild) { Reset-LogDir }
if (-not $SkipBuild) { Invoke-BuildAndImport } else { Write-Host "  (build skipped)" -ForegroundColor DarkGray }

Write-Host "=== LD-6: the kit -- generator controls, reproducibility, scene self-test ===" -ForegroundColor White

$gen = Join-Path $script:Root "tools\dev\kit_course.py"
$tmp = Join-Path $script:Root "docs\qa\LD-6\tmp"
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

# _Common.ps1's Start-Godot puts EVERY argument after `--`, which is correct for the game's own
# LaunchOptions flags and wrong for an engine flag: `--script` before the `--` is a Godot flag,
# after it is an inert user string and the engine boots the project's main scene instead, which
# looks exactly like a hang. So this suite launches the script runner itself.
function Start-GodotScript([string]$ScriptRes, [string[]]$UserArgs, [string]$Tag) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $err = Join-Path $script:LogDir "$Tag.err.log"
    $a = @("--headless", "--path", $script:Root, "--script", $ScriptRes)
    if ($UserArgs.Count -gt 0) { $a += @("--") + $UserArgs }
    $p = Start-Process -FilePath $script:GodotExe -ArgumentList $a `
        -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
    $null = $p.Handle
    return $p
}

# Windows PowerShell 5.1 wraps a native command's stderr in an ErrorRecord (NativeCommandError)
# and, under $ErrorActionPreference = "Stop", turns it into a terminating error -- which would
# make the POSITIVE CONTROLS below (whose whole job is to make python fail loudly) blow up the
# suite instead of being read. Start-Process keeps the two streams apart and hands back the real
# exit code.
function Invoke-Python([string[]]$Arguments, [string]$Tag) {
    $out = Join-Path $script:LogDir "$Tag.out.log"
    $err = Join-Path $script:LogDir "$Tag.err.log"
    $p = Start-Process -FilePath "python" -ArgumentList $Arguments `
        -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow -Wait
    $merged = Join-Path $script:LogDir "$Tag.log"
    Get-Content $out, $err -ErrorAction SilentlyContinue | Set-Content -Encoding utf8 $merged
    return @{ Code = $p.ExitCode; Log = $merged }
}

try {
    # --- 1. the generator's own positive controls ------------------------------------------
    Write-Host "[1/4] generator positive controls (motor derivation, ramp-vs-drop)..." -ForegroundColor Cyan
    $r = Invoke-Python @($gen, "--controls") "kit-controls"
    if ($r.Code -ne 0) { Write-Fail "the generator's positive controls failed; see $($r.Log)" }
    Get-Content $r.Log | ForEach-Object { Write-Host "        $_" -ForegroundColor DarkGray }

    # --- 2. byte-reproducibility ------------------------------------------------------------
    Write-Host "[2/4] byte-reproducibility across two runs..." -ForegroundColor Cyan
    $a = Join-Path $tmp "repro_a.tscn"
    $b = Join-Path $tmp "repro_b.tscn"
    $r = Invoke-Python @($gen, "--out", $a) "kit-gen-a"
    if ($r.Code -ne 0) { Write-Fail "the generator refused to write run A; see $($r.Log)" }
    $r = Invoke-Python @($gen, "--out", $b) "kit-gen-b"
    if ($r.Code -ne 0) { Write-Fail "the generator refused to write run B; see $($r.Log)" }
    $ha = (Get-FileHash $a -Algorithm SHA256).Hash
    $hb = (Get-FileHash $b -Algorithm SHA256).Hash
    Write-Host "        run A $ha" -ForegroundColor DarkGray
    Write-Host "        run B $hb" -ForegroundColor DarkGray
    if ($ha -ne $hb) { Write-Fail "two generator runs produced different bytes; the course is not reproducible" }

    # --- 3. the scene self-test against what actually ships ---------------------------------
    Write-Host "[3/4] scene self-test against scenes/dev/KitCourse.tscn..." -ForegroundColor Cyan
    $proc = Start-GodotScript "res://tools/dev/kit_selftest.gd" @() "kit-selftest"
    if (-not (Wait-ForExit $proc 120)) { Stop-Proc $proc; Write-Fail "the kit self-test did not exit in time" }
    $out = Join-Path $script:LogDir "kit-selftest.out.log"
    if ($proc.ExitCode -ne 0) { Write-Fail "the kit self-test exited $($proc.ExitCode); see kit-selftest.out.log and kit-selftest.err.log" }
    if (-not (Select-String -Path $out -Pattern "\[kit-selftest\] PASS" -Quiet)) {
        Write-Fail "the kit self-test never printed PASS; see kit-selftest.out.log"
    }
    Select-String -Path $out -Pattern "^\[kit-selftest\]|^\[motor-arc\]" | ForEach-Object {
        Write-Host "        $($_.Line.Trim())" -ForegroundColor DarkGray
    }

    # --- 4. the two positive controls --------------------------------------------------------
    # Every rule here is an ABSENCE check ("no primitive lies about what it is"), so each one is
    # worth exactly what its control is worth. A detector that has never fired positive is not
    # known to work -- and this repo has shipped one that could not have.
    Write-Host "[4/4] positive controls: a mirrored Wedge must be rejected twice..." -ForegroundColor Cyan
    $r = Invoke-Python @($gen, "--violate", "wedge-sign") "kit-violate"
    if ($r.Code -eq 0) {
        Write-Fail "the GENERATOR accepted a deliberately mirrored Wedge. Its validator is not validating; every green run before this one was worthless."
    }
    $why = (Select-String -Path $r.Log -Pattern "FAIL \[run-sign\]" | Select-Object -First 1)
    if (-not $why) { Write-Fail "the generator rejected the mirrored Wedge but not by the run-sign rule; see $($r.Log)" }
    Write-Host "        generator positive control FAILED AS EXPECTED (exit $($r.Code))" -ForegroundColor DarkGray
    Write-Host "         $($why.Line.Trim())" -ForegroundColor DarkGray

    $fixture = Join-Path $tmp "mirrored.tscn"
    $r = Invoke-Python @($gen, "--emit-violation", "wedge-sign", "--out", $fixture) "kit-fixture"
    if ($r.Code -ne 0) { Write-Fail "could not emit the mirrored-Wedge fixture; see $($r.Log)" }
    $proc = Start-GodotScript "res://tools/dev/kit_selftest.gd" `
        @("--kit-scene", "res://docs/qa/LD-6/tmp/mirrored.tscn") "kit-selftest-control"
    if (-not (Wait-ForExit $proc 120)) { Stop-Proc $proc; Write-Fail "the self-test's positive control did not exit in time" }
    if ($proc.ExitCode -eq 0) {
        Write-Fail "the SELF-TEST accepted a scene with a mirrored Wedge in it. It is not measuring the geometry; every green run before this one was worthless."
    }
    $cerr = Join-Path $script:LogDir "kit-selftest-control.err.log"
    if (-not (Select-String -Path $cerr -Pattern "authored to climb along" -Quiet)) {
        Write-Fail "the self-test's positive control failed, but not on the mirrored rotation; see kit-selftest-control.err.log"
    }
    Write-Host "        self-test positive control FAILED AS EXPECTED (exit $($proc.ExitCode))" -ForegroundColor DarkGray
    Select-String -Path $cerr -Pattern "authored to climb along" | Select-Object -First 1 | ForEach-Object {
        Write-Host "         $($_.Line.Trim())" -ForegroundColor DarkGray
    }
}
finally {
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host "PASS: motor derivation control, ramp-vs-drop control, byte-reproducibility, the six primitives' rules against the shipped scene (forbidden band, Block bands, Denial by distance, Wedge angle, Tilt angle + no-ledge, Log cylinder + crown, Sag continuity + concavity, Dome cap), collider audit, non-billboarded labels, and both mirrored-Wedge positive controls." -ForegroundColor Green
Write-Host ""
Write-Host "KIT COURSE TEST OVERALL: PASS" -ForegroundColor Green
exit 0
