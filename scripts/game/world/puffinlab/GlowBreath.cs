using System.Collections.Generic;
using Godot;

namespace Sail.Game.World.PuffinLab;

/// <remarks>PORTED from the sibling repo <c>mp-foundation</c> @ <c>8d07fed</c>
/// (<c>scripts/game/world/GlowBreath.cs</c>) by packet EGG-1, 2026-09-02. The ONLY diff
/// against the source is the namespace line above and this remark: no behaviour, no
/// tuning value and no comment was touched, so the two files stay diffable in both
/// directions. Do not "improve" it in place — drift makes the next re-port unreadable.
/// It is presentation-only (no collision, no player awareness, no consequence logic) and
/// is driven from <see cref="PuffinLabRoom"/>, which owns every schedule it obeys.</remarks>

/// <summary>
/// Makes the sick-green beacon breathe: one slow, liquid modulation applied to the
/// hole's glow lights and the spilt-fluid emissive materials, so the light reads as
/// alive and unstable — never a strobe (three incommensurate slow sines; the primary
/// period is ~12 s and the total swing stays gentle). Deterministic, no randomness;
/// purely visual, so the headless suites are untouched.
///
/// Emissive material bases are passed explicitly (not sampled at Add time): the
/// materials are shared static instances, and a scene reload would otherwise capture
/// a mid-breath value as the new baseline and drift.
/// </summary>
public partial class GlowBreath : Node
{
    private readonly List<(OmniLight3D Light, float Base, float Phase)> _lights = new();
    private readonly List<(StandardMaterial3D Mat, float Base)> _materials = new();
    private double _time;

    public GlowBreath AddLight(OmniLight3D light, float phase = 0f)
    {
        _lights.Add((light, light.LightEnergy, phase));
        return this;
    }

    public GlowBreath AddMaterial(StandardMaterial3D material, float baseEmission)
    {
        _materials.Add((material, baseEmission));
        return this;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        float t = (float)_time;
        foreach ((OmniLight3D light, float baseEnergy, float phase) in _lights)
            light.LightEnergy = baseEnergy * Breath(t + phase);
        foreach ((StandardMaterial3D mat, float baseEmission) in _materials)
            mat.EmissionEnergyMultiplier = baseEmission * Breath(t);
    }

    public override void _ExitTree()
    {
        // Leave the shared materials at their resting values for whoever loads next.
        foreach ((StandardMaterial3D mat, float baseEmission) in _materials)
            mat.EmissionEnergyMultiplier = baseEmission;
    }

    private static float Breath(float t) =>
        1f + 0.16f * Mathf.Sin(t * 0.53f)
           + 0.07f * Mathf.Sin(t * 1.31f + 1.7f)
           + 0.035f * Mathf.Sin(t * 3.1f + 0.6f);
}
