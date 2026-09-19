using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Presentation;

/// <summary>Per-actor presentation data: the full list of event responses for one
/// creature or item type, authored as a .tres and assigned in the inspector (or by the
/// spawning code). Resolution is through a lazily-built index — built on first
/// <see cref="ResponsesFor"/> and rebuilt only when <see cref="Responses"/> is
/// reassigned (an editor .tres live-reload re-runs the setter on the SAME cached
/// instance, so the index must not outlive its data). Steady-state, every
/// <see cref="ResponsesFor"/> is a plain dictionary hit returning a cached array (the
/// zero-allocation contract: Fire runs per footstep, ~4 Hz × 16 avatars — and Fire
/// never touches the setter).</summary>
[GlobalClass]
public partial class PresentationProfile : Resource
{
    private EventResponse[] _responses = System.Array.Empty<EventResponse>();

    [Export]
    public EventResponse[] Responses
    {
        get => _responses;
        set { _responses = value; _byEvent = null; }
    }

    private Dictionary<ActorEvent, EventResponse[]>? _byEvent;
    private static readonly EventResponse[] Empty = System.Array.Empty<EventResponse>();

    /// <summary>Every response mapped to <paramref name="evt"/>, in authored order.
    /// Never null — an unmapped event resolves to a shared empty array (silence).</summary>
    public EventResponse[] ResponsesFor(ActorEvent evt)
    {
        _byEvent ??= BuildIndex();
        return _byEvent.TryGetValue(evt, out EventResponse[]? responses) ? responses : Empty;
    }

    private Dictionary<ActorEvent, EventResponse[]> BuildIndex()
    {
        var lists = new Dictionary<ActorEvent, List<EventResponse>>();
        foreach (EventResponse r in Responses)
        {
            if (r == null)
                continue; // a hole in a hand-authored .tres array degrades, never crashes
            if (!lists.TryGetValue(r.Event, out List<EventResponse>? list))
                lists[r.Event] = list = new List<EventResponse>();
            list.Add(r);
        }
        var index = new Dictionary<ActorEvent, EventResponse[]>(lists.Count);
        foreach (KeyValuePair<ActorEvent, List<EventResponse>> entry in lists)
            index[entry.Key] = entry.Value.ToArray();
        return index;
    }
}
