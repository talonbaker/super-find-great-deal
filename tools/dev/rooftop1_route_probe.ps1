<#
.SYNOPSIS
    ROOFTOP-1's absence check: prove this branch did not touch Talon's cyan -> lab-roof -> tunnel
    route, and prove the probe can say so.

.DESCRIPTION
    Talon, 2026-09-04, about a traversal nobody designed:

        "if you walk toward cyan that corner of the map and if you cross the bridge, and if you go
         to the middle of the cyan map and turn left, and then if you jump off of that, you will be
         able to stand on the top of the lab scene, which is fantastic. I love it please do not
         change this"

    An absence claim needs an instrument, and an instrument needs a control. This is both.

    THE THREE SURFACES THE ROUTE IS MADE OF, and the files that own them:

      the cyan platform he jumps from    scenes/game/world/bubbletest/sections/CyanRun.tscn
      the lab roof he lands on           scenes/game/world/puffinlab/**   (PuffinLab, LabRoom,
                                         EscapeRoute)
      the tunnel he walks across         the same, Lab/HallCeil

    ROOFTOP-1 authors a pad ABOVE the lab roof, in its own new section file
    (sections/LabRoof.tscn), for exactly this reason: the roof itself is in a scene this packet is
    forbidden to edit, so "did the route change" reduces to "did any of these files change at all",
    which is a question git can answer exactly rather than approximately.

    -Control plants a real change in CyanRun.tscn, runs the same probe, requires it to FAIL, and
    then restores the file byte-for-byte. Without that pass, a probe that silently matched nothing
    would report a clean route forever.

.PARAMETER Base
    The merge base to diff against. Defaults to the branch this packet was cut from.

.PARAMETER Control
    Run the positive control instead of the real probe.

.EXAMPLE
    powershell -File tools/dev/rooftop1_route_probe.ps1
    powershell -File tools/dev/rooftop1_route_probe.ps1 -Control
#>
[CmdletBinding()]
param(
    [string] $Base = "playtest/2026-09-04-combined",
    [switch] $Control
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# The route's own files. A change to ANY of them is a change to the traversal until a human says
# otherwise -- deliberately coarse, because "which node inside CyanRun.tscn is the platform" is a
# judgement and "did CyanRun.tscn change" is a fact.
$guarded = @(
    "scenes/game/world/bubbletest/sections/CyanRun.tscn",
    "scenes/game/world/puffinlab/PuffinLab.tscn",
    "scenes/game/world/puffinlab/LabRoom.tscn",
    "scenes/game/world/puffinlab/EscapeRoute.tscn"
)

function Invoke-Probe {
    param([string] $Why)

    Write-Host ""
    Write-Host "[route-probe] $Why"
    Write-Host "[route-probe] base = $Base"

    $touched = @()
    foreach ($f in $guarded) {
        $diff = & git -C $repo diff --stat "$Base" -- $f
        if ($LASTEXITCODE -ne 0) { throw "git diff failed for $f" }
        if ([string]::IsNullOrWhiteSpace($diff)) {
            Write-Host ("[route-probe]   {0,-58} unchanged" -f $f)
        } else {
            Write-Host ("[route-probe]   {0,-58} CHANGED" -f $f)
            Write-Host ("[route-probe]     " + ($diff -join "`n[route-probe]     "))
            $touched += $f
        }
    }

    if ($touched.Count -eq 0) {
        Write-Host "[route-probe] ROUTE UNCHANGED - none of the $($guarded.Count) files the cyan/roof/tunnel traversal is made of was touched."
        return $true
    }
    Write-Host "[route-probe] ROUTE TOUCHED - $($touched.Count) file(s): $($touched -join ', ')"
    return $false
}

if (-not $Control) {
    $ok = Invoke-Probe "real run"
    if ($ok) { exit 0 }
    Write-Host "[route-probe] FAIL: this branch changed Talon's route, which it was told not to."
    exit 1
}

# --- the positive control ---------------------------------------------------------------------
# Plant one real change in the cyan platform's own ground collider, run the identical probe, and
# require it to say so. The file is copied aside and restored in a finally, because git stash in a
# shared checkout has eaten an agent's work on this machine before.
$victim = Join-Path $repo "scenes/game/world/bubbletest/sections/CyanRun.tscn"
$backup = Join-Path $env:TEMP ("rooftop1-cyanrun-" + [guid]::NewGuid().ToString("N") + ".bak")

Copy-Item -LiteralPath $victim -Destination $backup -Force
try {
    Write-Host "[route-probe] CONTROL: planting a 1 m eastward move of cyan's ground plate."
    $text = [System.IO.File]::ReadAllText($victim)
    $before = 'size = Vector3(140, 0.5, 100)'
    $after  = 'size = Vector3(141, 0.5, 100)'
    if ($text -notmatch [regex]::Escape($before)) {
        # Fall back to any edit at all: the control must plant SOMETHING or it proves nothing.
        $text = $text + "`n; ROOFTOP-1 route-probe positive control - this line must not survive.`n"
    } else {
        $text = $text.Replace($before, $after)
    }
    [System.IO.File]::WriteAllText($victim, $text)

    $ok = Invoke-Probe "POSITIVE CONTROL (a planted change to cyan's platform)"
    if ($ok) {
        Write-Host "[route-probe] CONTROL DID NOT FIRE: the probe called a modified CyanRun.tscn unchanged."
        Write-Host "[route-probe] Every 'route unchanged' result from this script is worthless until that is fixed."
        exit 1
    }
    Write-Host "[route-probe] CONTROL FIRED AS EXPECTED - the probe can see a change to the route."
    exit 0
}
finally {
    Copy-Item -LiteralPath $backup -Destination $victim -Force
    Remove-Item -LiteralPath $backup -Force
    Write-Host "[route-probe] CyanRun.tscn restored."
}
