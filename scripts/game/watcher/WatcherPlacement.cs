using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Watcher;

/// <summary>
/// Picks the spot it is standing in. Pure, deterministic given its seed, and the one piece of
/// this feature with a doctrine constraint written against it by name.
///
/// <para><b>Three prior `/direct` passes filed forward flags naming this packet as the one most
/// likely to break §8.3 (reliable telegraphs), and this class is where they are discharged.</b>
/// The camp-woods canopy pass: <i>"the first packet that spawns a creature preferentially in the
/// darkest cover turns the canopy into a reliable telegraph."</i> The land pass filed the same
/// sentence about hollows. The campfire pass: <i>"the woods must not become where the creature is
/// reliably met, or the fetch becomes a scheduled introduction."</i> So the choice here is
/// uniform over the admissible ring and takes <b>no</b> input from cover density, canopy
/// thickness, terrain concavity, trail position or evidence sites — not as an oversight, and not
/// as something to be improved later by a smarter heuristic. There is deliberately no parameter
/// on this method through which such a preference could be supplied.</para>
///
/// <para>The one thing it does care about is <i>who is looking</i>: a candidate inside any
/// player's view cone is rejected, because a creature that materialises in view is a startle,
/// and §8.2 forbids startle as the engine of a build. When every candidate is refused, this
/// returns false and nothing appears — waiting is always a legal outcome.</para>
/// </summary>
public static class WatcherPlacement
{
    /// <summary>How many angles around the ring are considered — one every five degrees.</summary>
    private const int RingSamples = 72;

    /// <summary>
    /// Finds a stand position beyond the lit radius that no listed player can currently see and
    /// that sits in the target's readable range band.
    /// </summary>
    /// <param name="fireOrigin">Centre of the lit ground (the campfire).</param>
    /// <param name="litRadiusM">From <c>INightPressure.LitRadiusM</c>. The result is always
    /// strictly outside this, by at least <c>tuning.StandoffMinM</c>.</param>
    /// <param name="target">Who it intends to look at.</param>
    /// <param name="peers">Everyone whose view must be avoided, target included.</param>
    /// <param name="groundHeight">Height sampler, so the creature stands on the ground rather
    /// than hovering at the fire's altitude. Pass a flat function in a lab.</param>
    /// <param name="seed">Varies the search order between sightings. Same seed, same answer.</param>
    public static bool TryChoose(Vector3 fireOrigin, float litRadiusM, in WatcherPeerView target,
                                 IReadOnlyList<WatcherPeerView> peers, WatcherTuning tuning,
                                 System.Func<float, float, float> groundHeight, uint seed,
                                 out Vector3 stand)
    {
        stand = default;
        float halfAngle = tuning.ViewHalfAngleDeg;

        // Two rings rather than one: the near ring is the interesting one (a body at 20-30 m
        // resolves), the far ring is the fallback when the near one is entirely in view.
        for (int rIdx = 0; rIdx < 2; rIdx++)
        {
            float radius = litRadiusM + Mathf.Lerp(tuning.StandoffMinM, tuning.StandoffMaxM,
                                                   rIdx == 0 ? 0.25f : 0.75f);

            // Collect EVERY admissible angle first, then take one by index. The earlier version
            // walked the ring in a strided order and took the first survivor, which is subtly
            // not the same thing: whenever a view cone rejects the first few candidates the
            // answer lands wherever the stride happened to jump, and
            // Placement_SpreadsAroundTheRingRatherThanFavouringOneArc caught that as real
            // clustering (quadrant counts 15/78/75/32). Uniform-over-admissible is what the
            // three §8.3 flags in the class remarks actually ask for, so it is worth the list.
            var admissible = new List<Vector3>(RingSamples);
            for (int k = 0; k < RingSamples; k++)
            {
                float a = Mathf.Tau * k / RingSamples;
                float x = fireOrigin.X + Mathf.Cos(a) * radius;
                float z = fireOrigin.Z + Mathf.Sin(a) * radius;
                var candidate = new Vector3(x, groundHeight(x, z), z);

                float range = WatcherBrain.HorizontalDistance(candidate, target.Position);
                if (range < tuning.MinRangeM || range > tuning.MaxRangeM)
                    continue;
                if (AnyoneSees(peers, candidate, halfAngle))
                    continue;

                admissible.Add(candidate);
            }
            if (admissible.Count == 0)
                continue;

            stand = admissible[(int)(seed % (uint)admissible.Count)];
            return true;
        }
        return false;
    }

    private static bool AnyoneSees(IReadOnlyList<WatcherPeerView> peers, Vector3 spot, float halfAngleDeg)
    {
        for (int i = 0; i < peers.Count; i++)
            if (WatcherSight.CanSee(peers[i].Position, peers[i].Forward, spot, halfAngleDeg,
                                    WatcherLimits.SightRangeM))
                return true;
        return false;
    }
}
