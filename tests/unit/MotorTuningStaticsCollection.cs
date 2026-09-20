using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The three test classes that park <c>MotorTuning.Current</c> run one at a time.</b>
///
/// <para><c>MotorTuning.Current</c> is ONE process-wide static and it is what the live
/// <c>AvatarMotor</c> reads (<c>MotorTuning.TryApply</c> is its only writer). Three classes in
/// this suite write it — <c>AnticipationCoilTests</c>, <c>MotorTuningTests</c> and
/// <c>MotorTuningSessionTests</c>, 27 call sites between them. Each of them parks the value and
/// restores it in a <c>finally</c>, which is correct WITHIN a class and cannot defend against
/// anything: xUnit gives every class its own collection by default and runs collections in
/// parallel, so a second class can apply a different tuning BETWEEN two lines of the first one's
/// test. There is no <c>xunit.runner.json</c> in this project disabling that.</para>
///
/// <para><b>Measured at INT-1, 2026-09-19</b>, on the tree with TASK-1 merged:
/// <c>AnticipationCoilTests.TheLiveGravityForActuallyReadsTheModeAndTheTwoBakedKnobs</c> failed
/// once in fourteen full runs with <c>Expected: 31.9758396, Actual: 24</c> — 24 being
/// <c>AvatarMotor.Gravity</c> with the launch-coil factor absent, i.e. the live motor reading a
/// tuning that was not the one the test had just applied two lines above. Nothing in this wave
/// touches gravity, the coil or the tuning; what changed was the SCHEDULE, because 69 new tests
/// landed elsewhere in the suite. That is the whole shape of the bug: a latent race whose
/// trigger is any test added anywhere.</para>
///
/// <para><b>Why a collection rather than a lock.</b> A lock inside each test would serialise the
/// three classes just as well and would have to be taken by every future test that touches the
/// tuning, remembered by every author, with no failure when it is forgotten. The collection is
/// declarative and the compiler-visible attribute is the reminder. It costs the suite's wall
/// clock nothing measurable — the three classes total well under a second — and it changes no
/// assertion.</para>
///
/// <para><b>If you write a fourth class that calls <c>MotorTuning.TryApply</c>,
/// <c>SetSessionProbeForTests</c> or <c>ResetForTests</c>, put this attribute on it.</b> The
/// same reasoning applies to any other process-wide static a test parks; this collection is
/// named for the one that has actually been measured biting.</para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class MotorTuningStaticsCollection
{
    /// <summary>The collection name, so the three attributes cannot drift apart by a typo.</summary>
    public const string Name = "MotorTuning statics (process-wide, not parallel-safe)";
}
