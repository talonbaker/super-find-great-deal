using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The <c>--round-script</c> dev source.</b> Feeds the round's facts on a schedule so the whole
/// loop — Start, Confirm, the find, End, the reset edge and every teleport between them — can be
/// driven end to end on a server with no buttons, no bin and no towers in the world yet.
///
/// <para><b>It can only ADD a fact.</b> <c>HideSeekDriver</c> ORs every source's bools, so a human
/// pressing a real Start on the same server is never overridden and a real press is never
/// swallowed. Combined with "a launch flag nobody types by accident" and a <c>[round] DEV</c> line
/// on arming and on every fired verb, that is the gate. Nothing here persists anything, so there
/// is no per-player fact for <c>IIntentSource.IsHumanInput</c> to guard — the trap that rule
/// exists for (a bot spending a real achievement into the player's live profile) cannot be reached
/// from this file.</para>
///
/// <para><b>Presses are one-tick; levels latch.</b> <c>start</c>, <c>confirm</c> and <c>end</c>
/// are consumed by the very next <see cref="AfterStep"/>, exactly as a real button's latch is.
/// <c>found</c> is a LEVEL — a bin holding the target keeps holding it — and so are the hands and
/// the tower count. Getting that backwards is the classic round-loop bug, so the two kinds are
/// separated here rather than at the call site.</para>
///
/// <para><b>The defaults are what make the packet's own example work.</b>
/// <c>--round-script "start@2,confirm@8,found@20,end@25"</c> names no hands, so this source starts
/// with a rack object held, the target not held, and reachability unmeasured. The extra verbs
/// exist so a suite can provoke each named refusal (<c>noobject</c>, <c>holdtarget</c>,
/// <c>unreachable</c>) and watch the HUD render the sentence.</para>
/// </summary>
public sealed class ScriptedRoundFactSource : IRoundFactSource
{
    private readonly List<(string Verb, string Value, double AtSec)> _script;
    private int _cursor;
    private double _elapsed;

    // Levels, with the defaults that let a four-verb script run a whole round.
    private bool _heldRackProp = true;
    private bool _holdsTarget;
    private bool? _retrievable;
    private bool _inDropOff;
    private int _towers;

    // Edges, cleared by AfterStep.
    private bool _start;
    private bool _confirm;
    private bool _end;

    public ScriptedRoundFactSource(IReadOnlyList<(string Verb, string Value, double AtSec)> script)
    {
        _script = new List<(string, string, double)>(script);
        _script.Sort((a, b) => a.AtSec.CompareTo(b.AtSec));
        GD.Print($"[round] DEV --round-script armed with {_script.Count} entry(ies): "
                 + string.Join(", ", _script.ConvertAll(e =>
                     e.Value.Length > 0 ? $"{e.Verb}:{e.Value}@{e.AtSec:0.##}" : $"{e.Verb}@{e.AtSec:0.##}")));
    }

    /// <summary>Advances the schedule. Called by the driver once per sim tick, BEFORE the facts
    /// are read, so a verb due this tick lands on this tick rather than the next.
    ///
    /// <para>A tick long enough to pass several marks fires them all, in order, on that tick —
    /// the same "never drop a mark" shape <c>BotHarness.MaybeCapture</c> uses, and it matters here
    /// because dropping a <c>start</c> silently produces a session that simply never begins.</para></summary>
    public void Advance(double delta)
    {
        _elapsed += delta;
        while (_cursor < _script.Count && _elapsed >= _script[_cursor].AtSec)
        {
            (string verb, string value, double at) = _script[_cursor++];
            Apply(verb, value);
            GD.Print($"[round] DEV script fired '{verb}"
                     + (value.Length > 0 ? ":" + value : "") + $"' at {_elapsed:F2}s (scheduled {at:0.##}s)");
        }
    }

    private void Apply(string verb, string value)
    {
        switch (verb)
        {
            case "start": _start = true; break;
            case "confirm": _confirm = true; break;
            case "end": _end = true; break;
            case "found": _inDropOff = true; break;
            case "lost": _inDropOff = false; break;
            case "object": _heldRackProp = true; break;
            case "noobject": _heldRackProp = false; break;
            case "holdtarget": _holdsTarget = true; break;
            case "droptarget": _holdsTarget = false; break;
            case "reachable": _retrievable = true; break;
            case "unreachable": _retrievable = false; break;
            case "towers":
                _towers = int.TryParse(value, out int n) ? System.Math.Max(n, 0) : _towers;
                break;
            default:
                // Loud, not silent: a typo'd verb in a suite's launch line otherwise looks exactly
                // like a round that refused for a reason nobody can find.
                GD.PushWarning($"[round] DEV --round-script: unknown verb '{verb}' ignored");
                break;
        }
    }

    public bool HostPressedStart => _start;
    public bool HiderHeldRackProp => _heldRackProp;
    public bool HiderPressedConfirm => _confirm;
    public bool HiderHoldsTarget => _holdsTarget;
    public bool? TargetRetrievable => _retrievable;
    public bool TargetInDropOff => _inDropOff;
    public int TowersCompleted => _towers;
    public bool AnyPressedEnd => _end;

    /// <summary>The edges die here, exactly as a real button's latch does.</summary>
    public void AfterStep()
    {
        _start = false;
        _confirm = false;
        _end = false;
    }
}
