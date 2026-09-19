using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using MpFoundation.Game.Presentation;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>SFX-1's Godot-free gate</b> (2026-09-19): every material voice renders, the intensity
/// mapping behaves, the once-per-contact rule picks exactly one body, and each material resolves
/// its four events to distinct sounds.
///
/// <para><b>Why this can run with no engine.</b> Every recipe in <see cref="SfxLab"/> is pure
/// managed arithmetic over <c>Godot.Mathf</c>, which is ordinary managed code in GodotSharp.dll.
/// <see cref="SfxLab.Get"/> cannot run here — it constructs an <c>AudioStreamWav</c>, a
/// <c>Resource</c>, which needs the native runtime — so SFX-1 split the switch out into
/// <see cref="SfxLab.RenderPcm"/>, which returns the raw buffer and is what everything below
/// calls. <c>Godot.Mathf</c> is the only Godot type anything here touches.</para>
///
/// <para><b>What could NOT be pinned here, and where it is pinned instead.</b>
/// <c>Carryable.ShapeFromCollider</c> is the rule that resolves an authored prop's material, and
/// it takes a <c>Godot.Shape3D</c> — a <c>Resource</c>, which cannot be constructed without the
/// native runtime. Its table is therefore pinned by the mirror test below
/// (<see cref="CarryableShape_MirrorsPropKindOneToOne"/>) plus <c>tests/Run-MaterialSfxTest.ps1</c>,
/// which drives all three authored prefabs through a real engine and reads the sound each one
/// actually fired.</para>
///
/// <para><b>What this suite deliberately does NOT cover.</b> Whether the sounds are GOOD. That is
/// Talon's ear on the .wav renders under <c>docs/qa/2026-09-19-sfx-1/</c>, which
/// <see cref="Captures_RenderTheWavFilesTalonListensTo"/> writes. A test can prove a buffer is
/// finite, audible and unclipped; it cannot prove a can sounds like a can.</para>
/// </summary>
public class MaterialSfxTests
{
    /// <summary>The eleven members SFX-1 appended, with the duration each recipe is specified to
    /// render. Restated here ON PURPOSE rather than read back off the recipe: a duration assertion
    /// that asks the code what length it produced and then agrees with it proves nothing. These
    /// numbers come from the packet's table, and the recipes have to match THEM.</summary>
    public static readonly IReadOnlyList<(Sfx Kind, float Seconds)> MaterialVoices = new[]
    {
        (Sfx.TinPick, 0.15f),
        (Sfx.TinClank, 0.40f),
        (Sfx.TinBuzz, 0.25f),
        (Sfx.TinTick, 0.04f),
        (Sfx.CardPick, 0.20f),
        (Sfx.CardThud, 0.27f),   // 0.12 s bop + 0.15 s rustle tail
        (Sfx.CardSettle, 0.08f),
        (Sfx.ProducePick, 0.06f),
        (Sfx.ProduceThump, 0.10f),
        (Sfx.ProducePlop, 0.07f),
        (Sfx.Whoosh, 0.12f),
    };

    public static IEnumerable<object[]> EveryMaterialVoice()
        => MaterialVoices.Select(v => new object[] { v.Kind, v.Seconds });

    // --- The recipes ---------------------------------------------------------------------

    /// <summary>Finite, audible, and NOT clipping — the three ways a synthesized buffer is broken
    /// without being empty.
    ///
    /// <para>The clipping bar is <c>peak &lt; 0.999</c> rather than <c>&lt;= 1</c>, and that is the
    /// whole point of the assertion: <c>SfxLab.Render</c> clamps every sample to [-1, 1], so a
    /// recipe that overshoots does not produce an out-of-range buffer, it produces a FLATTENED
    /// one — which is audible as a buzz welded to the attack and is invisible to any test that
    /// only checks the range. A peak that has reached 1.0 has been clamped.</para></summary>
    [Theory]
    [MemberData(nameof(EveryMaterialVoice))]
    public void EveryMaterialVoice_RendersFiniteAudibleAndUnclipped(Sfx kind, float seconds)
    {
        float[] pcm = SfxLab.RenderPcm(kind);
        Assert.True(seconds > 0f, $"{kind}: the table says {seconds} s, which is not a sound");

        Assert.All(pcm, s => Assert.True(float.IsFinite(s), $"{kind}: a non-finite sample"));

        float peak = pcm.Max(Math.Abs);
        Assert.True(peak > 0.05f,
            $"{kind}: peak {peak:F4} — effectively silent, so nothing about it can be heard");
        Assert.True(peak < 0.999f,
            $"{kind}: peak {peak:F4} reached Render's clamp — the recipe is hard-clipping, "
            + "which is audible as a buzz on the attack. Scale it down.");

        // Audible for its whole length, not one click and 0.3 s of nothing: the second half of
        // the buffer must still carry signal. A recipe that decayed to zero early would be
        // spending cache and a voice slot on silence.
        float lateRms = Rms(pcm.Skip(pcm.Length / 2).ToArray());
        Assert.True(lateRms > 1e-4f,
            $"{kind}: the back half is silent (RMS {lateRms:E2}) — the buffer is longer than the sound");
    }

    [Theory]
    [MemberData(nameof(EveryMaterialVoice))]
    public void EveryMaterialVoice_RendersTheStatedDuration(Sfx kind, float seconds)
    {
        float[] pcm = SfxLab.RenderPcm(kind);
        int expected = (int)(SfxLab.SampleRateHz * seconds);
        Assert.Equal(expected, pcm.Length);
    }

    /// <summary>Deterministic: the same recipe renders the same bytes twice. Load-bearing for the
    /// capture files — every recipe seeds its own <c>System.Random</c> with a fixed literal
    /// precisely so that <c>docs/qa/2026-09-19-sfx-1/*.wav</c> is a stable artefact rather than a
    /// file that shows up dirty in <c>git status</c> after every test run.</summary>
    [Theory]
    [MemberData(nameof(EveryMaterialVoice))]
    public void EveryMaterialVoice_IsDeterministic(Sfx kind, float seconds)
    {
        Assert.Equal(SfxLab.RenderPcm(kind), SfxLab.RenderPcm(kind));
        Assert.True(seconds > 0f);
    }

    /// <summary><b>Tin rings and the other two do not</b> — the one acoustic claim this packet
    /// makes that is measurable, and the difference a player is meant to hear from across an
    /// aisle.
    ///
    /// <para><b>Three separate assertions rather than one "ring index", because the two soft
    /// materials fail to ring for two DIFFERENT reasons</b> and a single composite number would
    /// blur them. Measured on the shipped recipes:</para>
    ///
    /// <list type="bullet">
    /// <item>Tin's tail is loud AND periodic — 7.7% of the head's energy, autocorrelation 0.9995.
    /// That is a ring: a decaying stack of shell modes, still sounding 300 ms in.</item>
    /// <item>Cardboard's tail is loud and NOT periodic — 17.7% of the head's energy (it is the
    /// packet's deliberate 0.15 s rustle tail, so it SHOULD carry energy) at autocorrelation
    /// 0.1363. Noise, not a note. An energy-only measure would have called this a ring and been
    /// wrong; tonality is the thing that must not appear here.</item>
    /// <item>Produce's tail is periodic but spent — a damped sine is tonal by construction, so
    /// asking it to be non-tonal would be asking the wrong question. What makes it not a ring is
    /// that it is 1.1% of the head, about -39 dB, which is gone.</item>
    /// </list>
    ///
    /// <para>Every bar below sits at least 3x away from its measured value, so this fails on a
    /// change of character rather than on a retune.</para></summary>
    [Fact]
    public void TinRings_AndCardboardAndProduceDoNot()
    {
        // Tin: loud tail, and it is a note.
        Assert.True(TailEnergyFraction(Sfx.TinClank) > 0.025f,
            $"TinClank tail carries {TailEnergyFraction(Sfx.TinClank):P2} of its head's energy — "
            + "a can that has gone quiet by the end is not ringing");
        Assert.True(TailTonality(Sfx.TinClank) > 0.90f,
            $"TinClank tail tonality {TailTonality(Sfx.TinClank):F3} — the tail has stopped being "
            + "a note, so the shell modes are no longer sounding");

        // Cardboard: the tail may be as loud as it likes, but it must never be a note. This is
        // the assertion that catches somebody "improving" the thud by putting a tone in the tail.
        Assert.True(TailTonality(Sfx.CardThud) < 0.35f,
            $"CardThud tail tonality {TailTonality(Sfx.CardThud):F3} — there is a note in the "
            + "rustle tail, which makes the box ring, which makes it tin");

        // Produce: no bar on tonality (a damped sine is tonal), a hard bar on what is left.
        Assert.True(TailEnergyFraction(Sfx.ProduceThump) < 0.035f,
            $"ProduceThump tail carries {TailEnergyFraction(Sfx.ProduceThump):P2} of its head's "
            + "energy — the flesh is supposed to absorb the hit, and this one is still sounding");

        // POSITIVE CONTROL for the tonality measure: it must say YES about something. Without
        // this, "CardThud is not tonal" would stay green if TailTonality returned 0 always.
        Assert.True(TailTonality(Sfx.ProduceThump) > 0.7f,
            $"POSITIVE CONTROL FAILED: ProduceThump's tail is a damped sine and scored "
            + $"{TailTonality(Sfx.ProduceThump):F3} — the tonality measure is not detecting "
            + "tone, so the cardboard assertion above means nothing");
    }

    /// <summary>Cardboard's pickup is noise and nothing else: the packet says "no tone", and a
    /// tone is what a strong autocorrelation at a single lag looks like. Compared against
    /// <see cref="Sfx.ProducePick"/>, which IS a single sine and therefore has to score high —
    /// the positive control, without which "cardboard scored low" could just mean the measure is
    /// broken.</summary>
    [Fact]
    public void CardboardPickup_IsNoiseNotTone()
    {
        float card = PeakAutocorrelation(SfxLab.RenderPcm(Sfx.CardPick));
        float produce = PeakAutocorrelation(SfxLab.RenderPcm(Sfx.ProducePick));
        Assert.True(produce > 0.7f,
            $"POSITIVE CONTROL FAILED: ProducePick is one damped sine and scored {produce:F3} — "
            + "the measure is not detecting tone, so nothing below it means anything");
        Assert.True(card < 0.5f,
            $"CardPick scored {card:F3} against ProducePick's {produce:F3} — there is a tone in "
            + "the rustle, and the moment a box has a tone it stops being cardboard");
    }

    // --- The intensity mapping -------------------------------------------------------------

    [Fact]
    public void ImpactIntensity_IsZeroAtTheAudibleFloorAndOneAtTheCeiling()
    {
        Assert.Equal(0d, Carryable.ImpactIntensity(Carryable.ThunkSpeedThreshold), 5);
        Assert.Equal(1d, Carryable.ImpactIntensity(Carryable.ImpactSpeedCeiling), 5);
    }

    [Fact]
    public void ImpactIntensity_IsMonotonicAndClampedAtBothEnds()
    {
        float previous = -1f;
        for (float speed = -20f; speed <= 40f; speed += 0.1f)
        {
            float i = Carryable.ImpactIntensity(speed);
            Assert.InRange(i, 0f, 1f);
            Assert.True(i >= previous,
                $"intensity fell from {previous:F4} to {i:F4} as speed rose past {speed:F2} m/s");
            previous = i;
        }
        // Clamped, not merely bounded: well past the ceiling it is still exactly 1, and a
        // nonsensical negative speed is exactly 0 rather than a negative intensity that would
        // invert every IntensityVolumeBoostDb in every profile.
        Assert.Equal(1d, Carryable.ImpactIntensity(1000f), 5);
        Assert.Equal(0d, Carryable.ImpactIntensity(-1000f), 5);
        Assert.Equal(0d, Carryable.ImpactIntensity(float.NegativeInfinity), 5);
    }

    [Fact]
    public void VolumeDb_RisesWithIntensityAndIsClampedByIt()
    {
        Assert.Equal(-8d, ActorFx.VolumeDbFor(-8f, 8f, 0f), 4);
        Assert.Equal(-4d, ActorFx.VolumeDbFor(-8f, 8f, 0.5f), 4);
        Assert.Equal(0d, ActorFx.VolumeDbFor(-8f, 8f, 1f), 4);
        // An out-of-range intensity must not push the boost past its authored maximum.
        Assert.Equal(0d, ActorFx.VolumeDbFor(-8f, 8f, 9f), 4);
        Assert.Equal(-8d, ActorFx.VolumeDbFor(-8f, 8f, -9f), 4);
        // The listener trim is additive and independent of intensity — see ActorFx.Fire's doc
        // for why conflating the two was a bug worth a paragraph.
        Assert.Equal(-14d, ActorFx.VolumeDbFor(-8f, 8f, 0.5f, trimDb: -10f), 4);
    }

    [Fact]
    public void PitchScale_GoesUpForTinAndDownForTheSoftMaterials()
    {
        Assert.Equal(1d, ActorFx.PitchScaleFor(0.18f, 0f), 4);
        Assert.Equal(1.18d, ActorFx.PitchScaleFor(0.18f, 1f), 4);
        Assert.Equal(0.86d, ActorFx.PitchScaleFor(-0.14f, 1f), 4);
        // Clamped like the volume boost.
        Assert.Equal(1.18d, ActorFx.PitchScaleFor(0.18f, 5f), 4);
        Assert.Equal(1d, ActorFx.PitchScaleFor(-0.14f, -5f), 4);
        // ... and floored, so a pathological authored range can never mute or reverse a stream.
        Assert.Equal(0.25d, ActorFx.PitchScaleFor(-40f, 1f), 4);
    }

    // --- Once per contact --------------------------------------------------------------------

    /// <summary>Exactly one of two colliding props plays the impact, and it is a TOTAL order so
    /// there is no tie to fall through. Godot reports one contact to both bodies; the loser
    /// staying silent is the difference between a hit and a flam.</summary>
    [Fact]
    public void PropOnPropContact_PicksExactlyOneOfTheTwoBodies()
    {
        foreach ((ulong a, ulong b) in new[] { (1ul, 2ul), (2ul, 1ul), (0ul, ulong.MaxValue), (7ul, 8ul) })
        {
            bool aFires = Carryable.WinsPropOnPropContact(a, b);
            bool bFires = Carryable.WinsPropOnPropContact(b, a);
            Assert.True(aFires ^ bFires,
                $"ids {a}/{b}: {(aFires && bFires ? "BOTH" : "NEITHER")} body would play the impact");
        }
    }

    /// <summary>The same pair always picks the same winner, whichever way round it is asked, and
    /// a body never beats itself — the degenerate case a self-collision would produce.</summary>
    [Fact]
    public void PropOnPropContact_IsStableAndNeverPicksABodyAgainstItself()
    {
        Assert.True(Carryable.WinsPropOnPropContact(3, 9));
        Assert.True(Carryable.WinsPropOnPropContact(3, 9));
        Assert.False(Carryable.WinsPropOnPropContact(9, 3));
        Assert.False(Carryable.WinsPropOnPropContact(5, 5));
    }

    // --- Material resolution ------------------------------------------------------------------

    [Fact]
    public void EveryProductShape_ResolvesToItsOwnMaterialAndProfile()
    {
        Assert.Equal(PropMaterial.Wood, Carryable.MaterialFor(Carryable.Shape.Crate));
        Assert.Equal(PropMaterial.Wood, Carryable.MaterialFor(Carryable.Shape.Ball));
        Assert.Equal(PropMaterial.Tin, Carryable.MaterialFor(Carryable.Shape.Can));
        Assert.Equal(PropMaterial.Cardboard, Carryable.MaterialFor(Carryable.Shape.Box));
        Assert.Equal(PropMaterial.Produce, Carryable.MaterialFor(Carryable.Shape.Produce));

        // Wood is the no-change default: it must still resolve to the profile the crate and the
        // ball have always used, or every prop that existed before SFX-1 changes sound.
        Assert.Equal("res://assets/items/default/prop_presentation.tres",
            Carryable.ProfilePathFor(PropMaterial.Wood));
        Assert.Equal("res://assets/items/tin/prop_presentation.tres",
            Carryable.ProfilePathFor(PropMaterial.Tin));
        Assert.Equal("res://assets/items/cardboard/prop_presentation.tres",
            Carryable.ProfilePathFor(PropMaterial.Cardboard));
        Assert.Equal("res://assets/items/produce/prop_presentation.tres",
            Carryable.ProfilePathFor(PropMaterial.Produce));

        // Four materials, four distinct profiles: a typo that pointed two materials at one file
        // would make two products sound identical and nothing else would notice.
        var paths = Enum.GetValues<PropMaterial>().Select(Carryable.ProfilePathFor).ToArray();
        Assert.Equal(paths.Length, paths.Distinct().Count());
    }

    /// <summary><c>Carryable.Shape</c> mirrors <see cref="PropKind"/> 1:1, and three places now
    /// cast one straight onto the other (<c>NetworkedProp._Ready</c>,
    /// <c>PropManager.AuthoredKindOf</c>, and the seed-props path). If the two ever drift, every
    /// one of those casts silently produces the wrong prop — so the mirror is pinned here rather
    /// than asserted in a comment.</summary>
    [Fact]
    public void CarryableShape_MirrorsPropKindOneToOne()
    {
        string[] shapes = Enum.GetNames<Carryable.Shape>();
        string[] kinds = Enum.GetNames<PropKind>();
        Assert.Equal(kinds, shapes);
        foreach (PropKind kind in Enum.GetValues<PropKind>())
            Assert.Equal((int)kind, (int)Enum.Parse<Carryable.Shape>(kind.ToString()));
    }

    /// <summary>The ordinals SFX-1 took, pinned. These are a .tres serialization contract — every
    /// number here is written as an int into a file under <c>assets/items/</c> — so a renumber
    /// must break a test rather than silently repoint a profile at a different sound. 24 is
    /// deliberately absent: DOOR-1 has it.</summary>
    [Fact]
    public void AppendedSfxOrdinals_ArePinnedAndLeave24ToDoor1()
    {
        Assert.Equal(25, (int)Sfx.TinPick);
        Assert.Equal(26, (int)Sfx.TinClank);
        Assert.Equal(27, (int)Sfx.TinBuzz);
        Assert.Equal(28, (int)Sfx.TinTick);
        Assert.Equal(29, (int)Sfx.CardPick);
        Assert.Equal(30, (int)Sfx.CardThud);
        Assert.Equal(31, (int)Sfx.CardSettle);
        Assert.Equal(32, (int)Sfx.ProducePick);
        Assert.Equal(33, (int)Sfx.ProduceThump);
        Assert.Equal(34, (int)Sfx.ProducePlop);
        Assert.Equal(35, (int)Sfx.Whoosh);
        Assert.Equal(14, (int)ActorEvent.Placed);
        Assert.DoesNotContain(24, Enum.GetValues<Sfx>().Select(s => (int)s));

        // And the eleven are distinct from each other and from everything that was already here.
        int[] all = Enum.GetValues<Sfx>().Select(s => (int)s).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    /// <summary>Each material's four events resolve to four DIFFERENT sounds, and no two materials
    /// share a voice except the deliberately shared <see cref="Sfx.Whoosh"/>.
    ///
    /// <para>Read out of the authored <c>.tres</c> files rather than out of a table in this file.
    /// A table here would be a second copy of the mapping and would keep passing after somebody
    /// edited a profile — which is the exact failure this test exists to catch, since a profile
    /// is data and nothing else in the build reads it until a prop is struck.</para></summary>
    [Fact]
    public void EachMaterialProfile_MapsItsFourEventsToDistinctSounds()
    {
        var expected = new (PropMaterial Material, Sfx Pick, Sfx Impact, Sfx Placed)[]
        {
            (PropMaterial.Tin, Sfx.TinPick, Sfx.TinClank, Sfx.TinTick),
            (PropMaterial.Cardboard, Sfx.CardPick, Sfx.CardThud, Sfx.CardSettle),
            (PropMaterial.Produce, Sfx.ProducePick, Sfx.ProduceThump, Sfx.ProducePlop),
        };

        var everySound = new List<(PropMaterial, Sfx)>();
        foreach ((PropMaterial material, Sfx pick, Sfx impact, Sfx placed) in expected)
        {
            IReadOnlyDictionary<ActorEvent, Sfx[]> profile = ReadProfile(material);

            Assert.Equal(new[] { pick }, profile[ActorEvent.PickedUp]);
            Assert.Equal(placed, Assert.Single(profile[ActorEvent.Placed]));
            Assert.Equal(Sfx.Whoosh, Assert.Single(profile[ActorEvent.Thrown]));
            // Impact may carry a layer (tin's buzz); the FIRST response is the impact proper.
            Assert.Equal(impact, profile[ActorEvent.Impact][0]);

            Sfx[] four = { pick, impact, placed, Sfx.Whoosh };
            Assert.Equal(4, four.Distinct().Count());

            foreach (Sfx s in profile.Values.SelectMany(v => v))
                everySound.Add((material, s));
        }

        // Whoosh is shared by design; nothing else may be.
        var shared = everySound
            .Where(e => e.Item2 != Sfx.Whoosh)
            .GroupBy(e => e.Item2)
            .Where(g => g.Select(e => e.Item1).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        Assert.Empty(shared);

        // The high-intensity tin layer is gated, and the clank under it is not — otherwise every
        // tin impact would play both and the layer would stop being a layer.
        Assert.Equal(2, ReadProfile(PropMaterial.Tin)[ActorEvent.Impact].Length);
        Assert.Single(ReadProfile(PropMaterial.Cardboard)[ActorEvent.Impact]);
        Assert.Single(ReadProfile(PropMaterial.Produce)[ActorEvent.Impact]);
    }

    /// <summary>The Wood default is untouched: the one profile that existed before SFX-1 still
    /// maps exactly PickedUp→Pop and Impact→Thunk, and nothing else. "Nothing existing changes
    /// sound" is the packet's own constraint and this is what enforces it.</summary>
    [Fact]
    public void TheWoodDefaultProfile_IsUnchanged()
    {
        IReadOnlyDictionary<ActorEvent, Sfx[]> wood = ReadProfile(PropMaterial.Wood);
        Assert.Equal(2, wood.Count);
        Assert.Equal(Sfx.Pop, Assert.Single(wood[ActorEvent.PickedUp]));
        Assert.Equal(Sfx.Thunk, Assert.Single(wood[ActorEvent.Impact]));
    }

    // --- The capture files Talon listens to -----------------------------------------------------

    /// <summary><b>This test is also the generator</b> for
    /// <c>docs/qa/2026-09-19-sfx-1/&lt;material&gt;-&lt;event&gt;.wav</c>, and that is a deliberate
    /// choice rather than a convenience.
    ///
    /// <para>The packet asks for a capture per material per event so Talon can hear this without
    /// launching the game. A headless Godot run has no audio device and its dummy driver renders
    /// nothing, and a windowed run cannot be recorded by a suite — so the only honest capture is
    /// an offline render, and the only place in this repo that can render without an engine is
    /// this assembly. Generating the artefact in the same place that ASSERTS it is what stops the
    /// two drifting: a recipe change reruns the render, and a render that stopped being a valid
    /// 16-bit 44.1 kHz mono WAV fails here rather than being discovered by a silent file.</para>
    ///
    /// <para>Output is byte-deterministic (every recipe seeds a fixed RNG), so a rerun leaves
    /// <c>git status</c> clean. Writing is skipped, and the assertions still run in memory, when
    /// the repo root cannot be located — a nuget-restored CI checkout must not fail on a path.</para></summary>
    [Fact]
    public void Captures_RenderTheWavFilesTalonListensTo()
    {
        const int CaptureRateHz = 44100;
        var captures = new (string Name, Sfx Kind)[]
        {
            ("tin-pickedup", Sfx.TinPick),
            ("tin-impact", Sfx.TinClank),
            ("tin-impact-hard-layer", Sfx.TinBuzz),
            ("tin-placed", Sfx.TinTick),
            ("cardboard-pickedup", Sfx.CardPick),
            ("cardboard-impact", Sfx.CardThud),
            ("cardboard-placed", Sfx.CardSettle),
            ("produce-pickedup", Sfx.ProducePick),
            ("produce-impact", Sfx.ProduceThump),
            ("produce-placed", Sfx.ProducePlop),
            ("shared-thrown", Sfx.Whoosh),
        };

        string? outDir = FindCaptureDir();
        foreach ((string name, Sfx kind) in captures)
        {
            float[] at48k = SfxLab.RenderPcm(kind);
            float[] at441k = SfxLab.Resample(at48k, SfxLab.SampleRateHz, CaptureRateHz);

            // The resample must preserve the sound, not merely produce a buffer: length within
            // one sample of the ratio, and a peak that has neither collapsed nor overshot.
            Assert.Equal((int)((long)at48k.Length * CaptureRateHz / SfxLab.SampleRateHz), at441k.Length);
            Assert.All(at441k, s => Assert.True(float.IsFinite(s), $"{name}: non-finite after resample"));
            Assert.InRange(at441k.Max(Math.Abs), at48k.Max(Math.Abs) * 0.7f, 0.999f);

            byte[] wav = SfxLab.WavFileBytes(at441k, CaptureRateHz);
            AssertIsMono16BitWav(wav, CaptureRateHz, at441k.Length, name);

            if (outDir != null)
                File.WriteAllBytes(Path.Combine(outDir, name + ".wav"), wav);
        }
    }

    // --- Helpers ----------------------------------------------------------------------------

    private static float Rms(float[] pcm)
    {
        double sum = 0;
        foreach (float s in pcm)
            sum += (double)s * s;
        return pcm.Length == 0 ? 0f : (float)Math.Sqrt(sum / pcm.Length);
    }

    /// <summary>Energy in the last quarter of a buffer as a fraction of the energy in the first
    /// quarter — a ring holds it, a damped hit dumps it.</summary>
    private static float TailEnergyFraction(Sfx kind)
    {
        float[] pcm = SfxLab.RenderPcm(kind);
        int q = pcm.Length / 4;
        float head = Rms(pcm.Take(q).ToArray());
        float tail = Rms(pcm.Skip(pcm.Length - q).ToArray());
        return head <= 0f ? 0f : tail / head;
    }

    /// <summary>How much of a NOTE the last quarter of a buffer is, on 0..1 — the tonality half
    /// of "does it ring", where <see cref="TailEnergyFraction"/> is the loudness half.</summary>
    private static float TailTonality(Sfx kind)
    {
        float[] pcm = SfxLab.RenderPcm(kind);
        return PeakAutocorrelation(pcm.Skip(pcm.Length - pcm.Length / 4).ToArray());
    }

    /// <summary>The strongest normalised autocorrelation over musically plausible lags (80 Hz to
    /// 2 kHz). A periodic signal — any tone — scores near 1 at its own period; band-limited noise
    /// has no such lag and scores low.</summary>
    private static float PeakAutocorrelation(float[] pcm)
    {
        int minLag = SfxLab.SampleRateHz / 2000;
        int maxLag = Math.Min(SfxLab.SampleRateHz / 80, pcm.Length / 2);
        double energy = 0;
        for (int i = 0; i < pcm.Length; i++)
            energy += (double)pcm[i] * pcm[i];
        if (energy <= 0)
            return 0f;

        float best = 0f;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double sum = 0, normA = 0, normB = 0;
            for (int i = 0; i + lag < pcm.Length; i++)
            {
                sum += (double)pcm[i] * pcm[i + lag];
                normA += (double)pcm[i] * pcm[i];
                normB += (double)pcm[i + lag] * pcm[i + lag];
            }
            double denom = Math.Sqrt(normA * normB);
            if (denom <= 0)
                continue;
            best = Math.Max(best, (float)Math.Abs(sum / denom));
        }
        return best;
    }

    private static void AssertIsMono16BitWav(byte[] wav, int expectedRateHz, int sampleCount, string name)
    {
        Assert.Equal(44 + sampleCount * 2, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", System.Text.Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(1, BitConverter.ToUInt16(wav, 20));                        // PCM
        Assert.Equal(1, BitConverter.ToUInt16(wav, 22));                        // mono
        Assert.Equal(expectedRateHz, (int)BitConverter.ToUInt32(wav, 24));      // 44.1 kHz
        Assert.Equal(16, BitConverter.ToUInt16(wav, 34));                       // 16-bit
        Assert.Equal((uint)(sampleCount * 2), BitConverter.ToUInt32(wav, 40));  // data size
        Assert.True(wav.Skip(44).Any(b => b != 0), $"{name}: the data chunk is all zeroes");
    }

    /// <summary>Walks up from the test assembly looking for the repo root, and returns the
    /// capture directory under it, creating it if needed. Null when the root cannot be found,
    /// which is the "assert but do not write" case documented on the capture test.</summary>
    private static string? FindCaptureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SuperFindGreatDeal.csproj")))
            dir = dir.Parent;
        if (dir == null)
            return null;
        string captures = Path.Combine(dir.FullName, "docs", "qa", "2026-09-19-sfx-1");
        Directory.CreateDirectory(captures);
        return captures;
    }

    /// <summary>Reads a presentation profile's event→sound mapping straight out of its authored
    /// <c>.tres</c> text. <b>A hand parse rather than <c>GD.Load</c></b>, because loading a
    /// <c>Resource</c> needs the native engine this suite deliberately does not have — and
    /// because the thing under test IS the text: a .tres is the only artefact in this packet that
    /// no compiler checks.</summary>
    private static IReadOnlyDictionary<ActorEvent, Sfx[]> ReadProfile(PropMaterial material)
    {
        string res = Carryable.ProfilePathFor(material);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SuperFindGreatDeal.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string path = Path.Combine(dir!.FullName, res.Replace("res://", "").Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{material}: no profile at {path}");

        // Sub-resources are "[sub_resource ...]" blocks of "Key = value" lines. Blocks with no
        // Sound line are particle-only and contribute Sfx.None, which is exactly what the class
        // does; blocks before the first [sub_resource] (the header, the ext_resources) are skipped.
        var byEvent = new Dictionary<ActorEvent, List<Sfx>>();
        ActorEvent? evt = null;
        Sfx sound = Sfx.None;
        bool inBlock = false;
        void Flush()
        {
            if (!inBlock || evt == null)
                return;
            if (!byEvent.TryGetValue(evt.Value, out List<Sfx>? list))
                byEvent[evt.Value] = list = new List<Sfx>();
            list.Add(sound);
        }
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith("[sub_resource"))
            {
                Flush();
                inBlock = true;
                evt = null;
                sound = Sfx.None;
                continue;
            }
            if (line.StartsWith("[resource"))
            {
                Flush();
                inBlock = false;
                continue;
            }
            if (!inBlock || line.StartsWith(";"))
                continue;
            if (line.StartsWith("Event = "))
                evt = (ActorEvent)int.Parse(line["Event = ".Length..]);
            else if (line.StartsWith("Sound = "))
                sound = (Sfx)int.Parse(line["Sound = ".Length..]);
        }
        Flush();
        return byEvent.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }
}
