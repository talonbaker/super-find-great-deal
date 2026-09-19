using Godot;
using MpFoundation.Ui;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// The throttled "what would Interact do right now" poll, and the affordance it drives: the
/// candidate's shimmer (IHighlightable.Highlighted) and the floating key chip (InteractPrompt).
/// Polls the nearest Carryable and the nearest <see cref="IPressable"/> and highlights whichever
/// of the two is closest — never more than one at once. When a pressable wins, it also arms
/// SandboxAvatar.InteractOverride so HandleCarryIntent's existing "interactives get first
/// refusal" check routes the next Interact press into <see cref="IPressable.Press"/> instead of
/// carry logic, and disarms it the moment that candidate is no longer the winner. This class
/// only ever decides what SHIMMERS and which override is armed; WorldInteract (also
/// SandboxAvatar, wired by Gameplay) does its own independent proximity check for anything that
/// asks the server.
/// </summary>
public sealed class InteractHighlighter
{
    private const double PollIntervalSec = 0.1;

    private readonly System.Func<SandboxAvatar?> _viewer;
    private double _accumulator;
    private IHighlightable? _highlighted;

    public InteractHighlighter(System.Func<SandboxAvatar?> viewer) => _viewer = viewer;

    public void Tick(double delta)
    {
        _accumulator += delta;
        if (_accumulator < PollIntervalSec)
            return;
        _accumulator = 0;

        SandboxAvatar? viewer = _viewer();
        if (viewer == null || !GodotObject.IsInstanceValid(viewer))
        {
            Clear();
            return;
        }

        Carryable? carryable = viewer.FindNearestCarryable();
        // BT-8: the one candidate that is not a named type. See IPressable for why this kind is
        // an interface — the lever it was added for lives in a level's own namespace, and the
        // shared interaction core must not learn about one level's feature.
        IPressable? pressable = viewer.FindNearestPressable();
        IHighlightable? nearest = PickNearest(viewer.GlobalPosition, carryable, pressable);

        viewer.InteractOverride = nearest switch
        {
            // Always consumes the press, refusal included: a lever on cooldown answers the player
            // itself (INTERACTION-BIBLE — a refusal is feedback, not silence), and letting the
            // press fall through to carry logic would make standing at a lever drop your cargo.
            IPressable p => () => { p.Press(); return true; },
            _ => null,
        };

        if (ReferenceEquals(nearest, _highlighted))
        {
            if (nearest != null)
                InteractPrompt.SetTarget(nearest.GlobalPosition);
            return;
        }

        if (_highlighted != null && IsAlive(_highlighted))
            _highlighted.Highlighted = false;
        if (nearest != null)
            nearest.Highlighted = true;
        _highlighted = nearest;
        InteractPrompt.SetTarget(nearest?.GlobalPosition);
    }

    /// <summary>Nearest non-null candidate by squared distance from <paramref name="from"/>, or
    /// null if every candidate is null. Generic over however many interactable kinds the
    /// highlighter polls — adding a new kind is a call-site argument, never a new pairwise
    /// comparison to write.</summary>
    private static IHighlightable? PickNearest(Vector3 from, params IHighlightable?[] candidates)
    {
        IHighlightable? best = null;
        float bestDistSq = float.MaxValue;
        foreach (IHighlightable? c in candidates)
        {
            if (c == null)
                continue;
            float distSq = c.GlobalPosition.DistanceSquaredTo(from);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = c;
            }
        }
        return best;
    }

    private static bool IsAlive(IHighlightable h) => h switch
    {
        GodotObject g => GodotObject.IsInstanceValid(g),
        _ => true,
    };

    public void Clear()
    {
        if (_highlighted != null && IsAlive(_highlighted))
            _highlighted.Highlighted = false;
        _highlighted = null;
        InteractPrompt.SetTarget(null);
    }

    internal IHighlightable? Highlighted => _highlighted;
}
