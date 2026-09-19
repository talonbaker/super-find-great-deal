namespace MpFoundation.Net;

/// <summary>
/// <b>The one replicated clock a whole process can be compared against.</b> Built for ANIM-M3's
/// <c>--capture-at-tick</c>, and for nothing else — it decides nothing, it is never read by
/// simulation, and it never crosses the wire.
///
/// <para><b>The problem it exists to solve, stated exactly.</b> The brief requires two clients
/// rendering the same player to be captured <i>at the same replicated tick</i>. The harness could
/// only offer the same <i>elapsed second</i>: <c>BotHarness.MaybeCapture</c> fires on the bot's own
/// wall clock since it entered the tree, and two processes started sequentially have different
/// origins by however long <c>Start-Process</c> took (ANIM-M0 §5.4). Proving parity with a knob that
/// cannot express parity is not proof.</para>
///
/// <para><b>Which tick, and why that one.</b> Only <b>remote proxies</b> publish here, and each
/// publishes its <c>SnapshotBuffer.RenderTick</c> — the interpolated clock, deliberately
/// <c>InterpDelayTicks</c> behind the newest tick received. Three consequences, all wanted:</para>
/// <list type="number">
/// <item>It is the tick the body <i>on screen</i> is at, so a capture taken on it is a capture of a
/// frame that genuinely shows that moment.</item>
/// <item>Every client interpolating the same avatar runs the same delay off the same server tick
/// series, so two clients reach a given value of this clock showing the same replicated state.</item>
/// <item>A <b>predicted owner</b> deliberately does NOT publish. Its clock runs ahead by roughly
/// half the round trip and by a different amount on every machine — that is what client-side
/// prediction IS — so including it would make the shared clock unshared. A client with no remote
/// proxies has nothing to compare against and reports <see cref="Unavailable"/>, which the harness
/// must say out loud rather than silently substituting local time.</item>
/// </list>
///
/// <para><b>Monotonic by construction.</b> The published value is a running maximum, so a proxy that
/// despawns, a buffer that resets on an epoch change, or a peer that joins late can never walk the
/// clock backwards past a mark the harness is waiting on.</para>
///
/// <para><b>Process-wide static, like the capability flags.</b> A capture mark is a property of the
/// process, not of a scene, and the gameplay scene can be entered more than once per session.</para>
/// </summary>
public static class NetClock
{
    /// <summary>What <see cref="ObservedTick"/> reads when no remote proxy has ever sampled — a
    /// single-player session, a headless server with no clients, or the first frames before the
    /// first snapshot lands.</summary>
    public const long Unavailable = -1;

    private static long _observed = Unavailable;

    /// <summary>The newest replicated render tick any remote proxy in this process has reached, or
    /// <see cref="Unavailable"/>.</summary>
    public static long ObservedTick => _observed;

    /// <summary>Publish one proxy's render tick. Cheap enough to call every frame per body: one
    /// compare and, rarely, one store.</summary>
    public static void Observe(double renderTick)
    {
        if (!double.IsFinite(renderTick) || renderTick < 0.0)
            return;
        long t = (long)renderTick;
        if (t > _observed)
            _observed = t;
    }

    /// <summary>Back to <see cref="Unavailable"/>. For the tests, and for a session teardown that
    /// will be followed by a fresh connection to a different server whose tick series starts
    /// again — without this, the running maximum from the previous session would swallow every mark
    /// in the next one.</summary>
    public static void Reset() => _observed = Unavailable;
}
