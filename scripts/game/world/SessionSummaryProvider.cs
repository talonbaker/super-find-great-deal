using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.World;

/// <summary>
/// The real <see cref="ISessionSummarySource"/> — wired into <see cref="SessionSummarySource.Current"/>
/// from <c>Gameplay.cs</c>. Reads off whatever is actually in the build rather than guessing at
/// systems that are not:
///
/// <list type="bullet">
/// <item><see cref="PerKidQuarters"/> / <see cref="GroupTotalQuarters"/> / <see cref="PhotosTaken"/>
/// — EMPTY. The economy and the camera that fed them are not part of this build, so the honest
/// answer is no balances and no photos, which is exactly what the null source says too. They are
/// kept on the interface (and the panel still draws them) so the summary's shape does not change
/// under the UI; a future system that has something to report fills them in here.</item>
/// <item><see cref="MapCoveragePercent"/> — UNDEFINED, deliberately left a placeholder. Nobody
/// has decided what "map coverage" measures (evidence found vs. evidence total? ground walked?
/// trails visited?) — <see cref="ISessionSummarySource.MapCoveragePercent"/>'s own doc names the
/// gap but does not resolve it, and inventing a definition here would be a design decision this
/// integration pass has no authority to make. Returns 0 with this comment as the marker; flagged
/// as an open question for Talon. DO NOT fill this in without his call.</item>
/// </list>
/// </summary>
public sealed class SessionSummaryProvider : ISessionSummarySource
{
    private readonly Node3D _players;

    /// <summary><paramref name="players"/> is the same Players root Gameplay already hands
    /// <c>PropManager</c>'s AvatarResolver delegate — read-only here, never mutated, purely for
    /// the id -> DisplayName lookup <see cref="ResolveDisplayName"/> offers a future per-player
    /// readout.</summary>
    public SessionSummaryProvider(Node3D players)
    {
        _players = players;
    }

    public IReadOnlyList<KidQuarters> PerKidQuarters => Array.Empty<KidQuarters>();

    public int GroupTotalQuarters => 0;

    public int PhotosTaken => 0;

    /// <summary>UNDEFINED placeholder — see class doc. Do not infer a definition from this
    /// value; it is a marker, not a measurement.</summary>
    public float MapCoveragePercent => 0f;

    /// <summary>DisplayName for a peer id, resolved against the live Players root — never invented
    /// here, and blank (not "Player N") for a peer whose avatar has already despawned;
    /// <see cref="Ui.SessionSummaryFormatter"/>'s own "Player {PeerId}" fallback is exactly the
    /// seam for that case.</summary>
    public string ResolveDisplayName(int peerId) =>
        _players.GetNodeOrNull<SandboxAvatar>(peerId.ToString())?.DisplayName ?? "";
}
