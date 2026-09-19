using System;
using System.Linq;
using System.Reflection;
using MpFoundation.Game.Sandbox;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>A scripted body earns nothing.</b>
///
/// <para><b>The defect</b> (W7-8, 2026-08-30, from the master review). <c>SandboxAvatar</c> built
/// an <c>AchievementRuntime</c> for every OWNER-role avatar, and a suite bot owns its own avatar.
/// <c>Run-AimTest</c>'s scripted 2 s → 6 s aim hold clears <c>AchievementTracker.ToughGuyHoldSec</c>
/// (3.0 s), so every run of it unlocked "Tough guy" — and <c>AchievementStore</c> writes into
/// <c>user://settings.cfg</c>, which resolves by project NAME and is therefore the ONE real profile
/// shared by every worktree on this machine. Measured on Talon's live file: <c>tough_guy=true</c>
/// was the ONLY achievement key present, i.e. exactly the one a bot can reach and no others. His
/// first-time unlock was already spent, and W7-5's whole "first time" semantics were unverifiable
/// on this machine.</para>
///
/// <para><b>Why the gate is on the intent source.</b> "Is there a person driving this body" is a
/// per-avatar question; <c>--bot</c> and "is this a test build" are per-process answers, and the
/// second is a flag a real playtest could also carry. <c>IIntentSource</c> is the actual seam
/// between a person and a script.</para>
///
/// <para><b>What this file can and cannot reach.</b> The wiring itself lives in
/// <c>SandboxAvatar.ConfigureAsNetworked</c>, which needs a live scene tree — the same boundary
/// <c>AchievementTests.cs</c> already documents for the store. So these pin the CONTRACT the wiring
/// reads, and the runtime half is proved empirically in the W7-8 report by running the harness that
/// used to earn the achievement against a profile that did not have it.</para>
/// </summary>
public class BotAchievementGateTests
{
    private sealed class ScriptedDouble : IIntentSource
    {
        public MoveIntent NextIntent(double delta) => MoveIntent.None;
    }

    private sealed class HumanDouble : IIntentSource
    {
        public MoveIntent NextIntent(double delta) => MoveIntent.None;
        public bool IsHumanInput => true;
    }

    /// <summary>
    /// The default is "not a person", so a new scripted source is excluded without anyone
    /// remembering to exclude it. <b>Carries its own positive control</b>: a source that opts in
    /// reads true through the same interface reference, so "false" is a real answer rather than a
    /// property that cannot say anything else.
    /// </summary>
    [Fact]
    public void TheInterfaceDefaultIsNotHuman()
    {
        Assert.False(((IIntentSource)new ScriptedDouble()).IsHumanInput);
        Assert.True(((IIntentSource)new HumanDouble()).IsHumanInput);
    }

    /// <summary>
    /// <b>Exactly one implementation in the whole game assembly may claim to be a person.</b> The
    /// failure this guards is a scripted source quietly opting in — which would put achievement
    /// writes back into the shared real profile with no other symptom. Enumerating every
    /// implementer rather than checking the known ones is the point: the failure is an ADDITION.
    /// </summary>
    [Fact]
    public void OnlyLocalInputClaimsToBeHumanInput()
    {
        Type[] implementers = typeof(IIntentSource).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntentSource).IsAssignableFrom(t))
            .ToArray();

        // Floor: if the reflection stops finding sources, every assertion below is vacuous.
        // Seven sources ship today (local input, the deterministic walk, goto, carry, aim, the
        // charge-jump lab source is gone with the movement lab); the floor sits below that.
        Assert.True(implementers.Length >= 5,
            $"only {implementers.Length} IIntentSource implementations found — the reflection is not seeing the assembly");

        string[] claimHuman = implementers
            .Where(t => t.GetProperty("IsHumanInput", BindingFlags.Public | BindingFlags.Instance)
                         ?.DeclaringType == t)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { nameof(LocalInputIntentSource) }, claimHuman);
    }
}
