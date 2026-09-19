namespace MpFoundation.Game.World;

/// <summary>
/// Nearest-K audible selection, as a pure function, shared by every director that has more
/// candidate emitters than voices to give them.
///
/// This was a since-removed fire audio ranker's body, lifted out unchanged when a second consumer
/// appeared (<see cref="Sandbox.FootstepAudioDirector"/> — remote players' footsteps, 2026-08-13).
/// Lifted rather than copied, deliberately: the three failures the fire's version was written
/// against are properties of the *rule*, not of fires, and a second hand-rolled nearest-K would
/// have re-shipped all three.
///
///   1. <b>Ties.</b> Two candidates at the same distance must resolve the same way every frame.
///      An unstable comparison makes an emitter flicker in and out at no cost to anything, and
///      the player has no way to learn why.
///   2. <b>Boundary chatter.</b> A candidate sitting exactly at rank K swaps in and out as the
///      listener breathes. Same failure as (1), arriving without a tie, and the one a naive
///      implementation always ships. The <paramref name="hysteresisM"/> incumbent bonus is the fix.
///   3. <b>The far horizon.</b> Rank alone would keep the K-th nearest candidate audible from two
///      hundred metres away in an empty camp, holding a voice open for something inaudible.
///      <paramref name="maxAudibleM"/> bounds it.
///
/// (2) is not a polish item. THRILL-BIBLE §7.2 requires rules learnable by observation, and a cue
/// that comes and goes on sub-metre movements is a rule that changes without a cause — which reads
/// as broken rather than mysterious.
///
/// Godot-free by design so the xUnit suite can hammer it — see <c>FootstepAudioTests</c>.
/// </summary>
public static class NearestKAudio
{
    /// <summary>Which candidates should sound, given where they are and which are sounding now.
    ///
    /// <paramref name="audibleNow"/> is what makes this stable: pass the previous result back in
    /// and the incumbent bias applies. Pass all-false and you get the cold-start selection, which
    /// is what a fresh listener wants.</summary>
    /// <param name="distanceM">Distance from the listener to each candidate. NaN or negative
    /// entries are treated as unavailable rather than as very close — a candidate whose position
    /// has not resolved yet, or one that does not want to sound at all, must not win the nearest
    /// slot. Callers pass <c>float.PositiveInfinity</c> for the latter.</param>
    /// <param name="audibleNow">Which candidates currently hold a voice. A shorter array is
    /// treated as all-false past its end rather than throwing, because a candidate registered
    /// this frame legitimately has no previous state.</param>
    /// <param name="cap">How many may sound at once.</param>
    /// <param name="hysteresisM">How much closer a challenger must be before it takes an
    /// incumbent's slot, in metres. Implemented as a distance bonus for whoever currently holds
    /// one, so the selection is still a single ordering rather than a special case. Its job is to
    /// be larger than jitter and smaller than intent.</param>
    /// <param name="maxAudibleM">Past this, a candidate is silent regardless of rank.</param>
    public static bool[] Select(float[]? distanceM, bool[]? audibleNow, int cap,
        float hysteresisM, float maxAudibleM)
    {
        int n = distanceM?.Length ?? 0;
        var result = new bool[n];
        if (n == 0 || cap <= 0)
            return result;

        var order = new int[n];
        var effective = new float[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
            float d = distanceM![i];
            bool incumbent = audibleNow != null && i < audibleNow.Length && audibleNow[i];
            effective[i] = !float.IsFinite(d) || d < 0f
                ? float.PositiveInfinity
                : (incumbent ? d - hysteresisM : d);
        }

        // Insertion sort: n is the number of live candidates — single digits for both consumers
        // (lit fires in a camp, players in a lobby) — and a stable, obviously-correct sort with an
        // explicit index tiebreak is worth more here than an asymptotically better one whose tie
        // behaviour has to be looked up.
        for (int i = 1; i < n; i++)
        {
            int key = order[i];
            int j = i - 1;
            while (j >= 0 && Precedes(key, order[j], effective))
            {
                order[j + 1] = order[j];
                j--;
            }
            order[j + 1] = key;
        }

        int granted = 0;
        for (int k = 0; k < n && granted < cap; k++)
        {
            int idx = order[k];
            float d = distanceM![idx];
            if (!float.IsFinite(d) || d < 0f || d > maxAudibleM)
                continue;
            result[idx] = true;
            granted++;
        }
        return result;
    }

    /// <summary>Strict ordering: nearer effective distance first, index ascending on a tie. The
    /// index tiebreak is what makes the selection deterministic frame to frame — see the class
    /// doc's failure (1).</summary>
    private static bool Precedes(int a, int b, float[] effective)
    {
        float ea = effective[a], eb = effective[b];
        if (ea < eb)
            return true;
        if (ea > eb)
            return false;
        return a < b;
    }
}
