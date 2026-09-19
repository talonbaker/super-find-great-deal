using System;
using System.Linq;
using System.Reflection;
using MpFoundation.Ui;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The guard that keeps the next first-run panel from re-covering every capture in
/// <c>tests/</c>.</b>
///
/// <para><b>The defect</b> (W7-8, 2026-08-30; found by W7-4 while working on drowning): the
/// first-run How-to-Play overlay rendered across every windowed <c>*Capture</c> run. Captures are
/// how this repo verifies everything Talon judges by eye, and a covered capture is evidence of
/// nothing that looks exactly like evidence of something. It was intermittent for a reason no
/// worktree could explain: <c>user://</c> resolves by project NAME, so every worktree on this
/// machine shares one <c>settings.cfg</c>, and whichever agent last ticked "don't show this again"
/// decided whether the next agent's capture came back covered.</para>
///
/// <para><b>Why the test walks the class by reflection instead of naming the flags.</b> There were
/// already two first-run panels when this was written (How-to-Play and the playtest foreword) and a
/// third was landing the same day (W7-2, the anonymous-usage notice — Talon's note 15). A fix, or a
/// test, that named How-to-Play would have been stale by that evening. This asserts the RULE — every
/// public <c>…Dismissed</c> flag on this class answers true while the run-level suppression is on —
/// so a new flag that forgets to route through the gate turns the suite red on the day it is added,
/// which is the only moment anyone can cheaply fix it.</para>
/// </summary>
public class FirstRunSuppressionTests
{
    private static PropertyInfo[] DismissedFlags() =>
        typeof(OnboardingSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.Name.EndsWith("Dismissed", StringComparison.Ordinal)
                        && p.PropertyType == typeof(bool))
            .ToArray();

    /// <summary>Sanity floor for the reflection above: if the naming convention ever changes, every
    /// other test in this file would pass vacuously over an empty set. This is what stops that.
    /// </summary>
    [Fact]
    public void TheClassStillExposesFirstRunFlagsToWalk()
    {
        Assert.NotEmpty(DismissedFlags());
    }

    /// <summary>Suppression on: every first-run panel reads as already dismissed, whatever the
    /// persisted value is. This is the assertion the capture harnesses depend on.</summary>
    [Fact]
    public void SuppressionDismissesEveryFirstRunPanel()
    {
        try
        {
            OnboardingSettings.ForceShowFirstRunPanels = false;
            OnboardingSettings.SuppressFirstRunPanels = true;
            foreach (PropertyInfo flag in DismissedFlags())
                Assert.True((bool)flag.GetValue(null)!, $"{flag.Name} ignores SuppressFirstRunPanels");
        }
        finally
        {
            OnboardingSettings.SuppressFirstRunPanels = false;
        }
    }

    /// <summary><b>Positive control.</b> With suppression off and nothing persisted (a fresh
    /// process never calls Load), every flag reads false — i.e. the panels SHOW. Without this, the
    /// assertion above would stay green if the properties were hard-wired to true, and a test that
    /// cannot tell the two states apart proves nothing about either.</summary>
    [Fact]
    public void WithoutSuppressionTheFlagsAreNotForcedTrue()
    {
        try
        {
            OnboardingSettings.SuppressFirstRunPanels = false;
            OnboardingSettings.ForceShowFirstRunPanels = false;
            foreach (PropertyInfo flag in DismissedFlags())
                Assert.False((bool)flag.GetValue(null)!, $"{flag.Name} reads dismissed with no suppression and nothing persisted");
        }
        finally
        {
            OnboardingSettings.SuppressFirstRunPanels = false;
        }
    }

    /// <summary><b>The one flag in this category that the walk above cannot see.</b>
    /// <c>OnboardingSettings.GoalLineSuppressed</c> obeys the identical three-way run-level
    /// decision, but it is deliberately not named <c>…Dismissed</c> — the on-entry goal line is
    /// shown every session, nobody dismisses it and nothing about it is persisted, so naming it
    /// that way purely to be swept up by the reflection would buy coverage with a lie. Asserted by
    /// name here instead, in the same file, so the rule still has exactly one home.
    ///
    /// <para>The stake is the same one the class doc describes: this line is copy over the frame
    /// during the exact seconds a windowed <c>*Capture</c> run takes its shot.</para></summary>
    [Fact]
    public void TheGoalLine_ObeysTheSameThreeWayDecision()
    {
        try
        {
            OnboardingSettings.ForceShowFirstRunPanels = false;
            OnboardingSettings.SuppressFirstRunPanels = false;
            Assert.False(OnboardingSettings.GoalLineSuppressed, "shows by default");

            OnboardingSettings.SuppressFirstRunPanels = true;
            Assert.True(OnboardingSettings.GoalLineSuppressed, "captures and bots must not get it");

            OnboardingSettings.ForceShowFirstRunPanels = true;
            Assert.False(OnboardingSettings.GoalLineSuppressed, "--show-first-run-panels outranks suppression");
        }
        finally
        {
            OnboardingSettings.SuppressFirstRunPanels = false;
            OnboardingSettings.ForceShowFirstRunPanels = false;
        }
    }

    /// <summary>The escape hatch outranks the suppression: <c>--show-first-run-panels</c> is an
    /// explicit ask, and an explicit ask beats a default. This is how a capture whose SUBJECT is a
    /// first-run panel is still possible, and it is how W7-8 photographed the covered frame it was
    /// fixing without writing to a machine-wide settings file four other packets were using.
    /// </summary>
    [Fact]
    public void ForceShowBeatsSuppression()
    {
        try
        {
            OnboardingSettings.SuppressFirstRunPanels = true;
            OnboardingSettings.ForceShowFirstRunPanels = true;
            foreach (PropertyInfo flag in DismissedFlags())
                Assert.False((bool)flag.GetValue(null)!, $"{flag.Name} stayed dismissed despite --show-first-run-panels");
        }
        finally
        {
            OnboardingSettings.SuppressFirstRunPanels = false;
            OnboardingSettings.ForceShowFirstRunPanels = false;
        }
    }
}
