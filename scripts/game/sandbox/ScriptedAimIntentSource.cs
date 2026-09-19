namespace MpFoundation.Game.Sandbox;

/// <summary>
/// Decorator, not a standalone brain: layers scripted aim-rig raise/lower intent on top of
/// whatever <see cref="IIntentSource"/> it wraps (in practice a movement brain — see
/// SandboxAvatar.ConfigureNetworkedInstance) — the same composition shape
/// feat/tier0-camcorder's ScriptedCamcorderIntentSource uses for its own raise/record
/// scheduling. --aim-script never has to know how the bot is moving, only when to raise and
/// (optionally) when to lower — this is what Run-AimTest.ps1 uses to prove stance replicates
/// to remote peers (WP-L3 acceptance criterion) without any human input at all.
///
/// Paced by the fixed physics tick like every other scripted brain in this file's family — its
/// own clock starts at 0 on the bot's very first NextIntent call, the same instant the wrapped
/// source's clock starts.
/// </summary>
public sealed class ScriptedAimIntentSource : IIntentSource
{
    private readonly IIntentSource _inner;
    private readonly double _raiseAtSec;
    private readonly double _lowerAtSec;

    private double _clock;

    /// <param name="lowerAtSec">Negative (the default via LaunchOptions) = never lower within
    /// the scripted window; the bot stays Raised for the rest of the run once it raises.</param>
    public ScriptedAimIntentSource(IIntentSource inner, double raiseAtSec, double lowerAtSec)
    {
        _inner = inner;
        _raiseAtSec = raiseAtSec;
        _lowerAtSec = lowerAtSec;
    }

    public MoveIntent NextIntent(double delta)
    {
        _clock += delta;
        MoveIntent baseIntent = _inner.NextIntent(delta);

        bool raise = _clock >= _raiseAtSec && (_lowerAtSec < 0 || _clock < _lowerAtSec);
        return baseIntent with { AimRaise = raise };
    }
}
