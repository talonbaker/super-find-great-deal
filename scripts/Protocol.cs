using MpFoundation.Net;

namespace MpFoundation;

/// <summary>
/// Network protocol identity. Thin, stable aliases over the foundation's single per-title
/// config (<see cref="NetProfile"/>) — kept so the many existing call sites reading
/// <c>Protocol.Version</c> / <c>Protocol.MaxPlayers</c> compile unchanged while the values
/// have one real home. Change them in <see cref="NetProfile"/>, not here.
/// </summary>
public static class Protocol
{
    public const int Version = NetProfile.ProtocolVersion;
    public const int MaxPlayers = NetProfile.MaxPlayers;
}
