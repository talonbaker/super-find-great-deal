using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>Every number the startle has</b> (packet DOOR-1; program
/// <c>C:/repos/PROGRAM-2026-09-19-SUPERMARKET.md</c> §5). One file, as the packet asks, so a
/// retune is one place rather than a hunt through a door, a camera and a synth.
///
/// <para><b>A plain data record; it does not clamp itself.</b> Same stance
/// <see cref="HideSeekTuning"/> takes and for the same reason — a struct that clamped on
/// construction would still need the clamp re-applied at every <c>with</c> a test or an overlay
/// writes. <see cref="Validate"/> is the one place, and <see cref="StartleTuningFile"/> is its
/// only real caller.</para>
///
/// <para><b>Every field here is named by the packet or by §5</b>, and nothing else is. Three of
/// them ship OFF and are the anticipation knobs §5 lists as "default off for the first ride"
/// (<see cref="FakeKnockRatePerMin"/>, <see cref="SeekerKnockKey"/>,
/// <see cref="SeekerHeatOnIntercom"/>); their status and what tuning each would need is in
/// <c>docs/agents/handoffs/2026-09-19-DOOR-1.md</c>.</para>
///
/// <para><b>Godot-free by construction.</b> Nothing in this file (or in
/// <see cref="StartleTimeline"/>) touches the engine, so the whole staging timeline and the
/// impulse falloff are exercised by <c>tests/unit/StartleTimelineTests.cs</c> with no scene tree
/// and no renderer. The engine appears exactly once in the overlay, at
/// <see cref="StartleTuningFile.DefaultAbsolutePath"/>, which is the only call that needs
/// <c>ProjectSettings</c>.</para>
/// </summary>
public readonly record struct StartleTuning
{
    // --- 1. The tell (§5 step 2) ----------------------------------------------------------

    /// <summary>How long the tell runs before the burst. <b>0 = no tell at all</b>, which §5
    /// names explicitly: the door simply goes. Sub-second on purpose — past about a second it
    /// stops being a startle and becomes a warning, and the whole beat is that the instant is
    /// unknowable. Packet default 0.4; §5's stated range is 0..1.5.</summary>
    public float TellSec { get; init; }

    /// <summary>How far the task-room light drops during the tell, as a fraction of its authored
    /// energy. §5: "the task-room lights dip". 0.40 = down 40 %, so a 2.2-energy lamp reads 1.32
    /// for <see cref="TellSec"/> and then comes straight back at the burst.</summary>
    public float TellLightDipFraction { get; init; }

    // --- 2. The leaf (§5 step 3) ----------------------------------------------------------

    /// <summary>How long the leaf takes to reach <see cref="LeafOvershootDeg"/> from shut. 0.12 s
    /// — the burst half of "fast tween with a bounce, not physics".</summary>
    public float LeafOpenSec { get; init; }

    /// <summary>Where the leaf comes to rest, degrees about its hinge. 110.</summary>
    public float LeafOpenDeg { get; init; }

    /// <summary>How far past <see cref="LeafOpenDeg"/> the leaf throws before settling back.
    /// 125 — the bounce. A tween and not a hinge joint, deliberately: §5's own parenthesis says
    /// a joint can tunnel, and a leaf that tunnels through a player is the one failure this beat
    /// cannot survive.</summary>
    public float LeafOvershootDeg { get; init; }

    /// <summary>How long the leaf takes to fall back from <see cref="LeafOvershootDeg"/> to
    /// <see cref="LeafOpenDeg"/>. 0.25 s.</summary>
    public float LeafSettleSec { get; init; }

    /// <summary>How long the leaf takes to swing shut on <c>ResetRequested</c> (§5 step 4 of the
    /// packet's Build list). 0.6 s — slow, because the close is housekeeping and the open is the
    /// event.</summary>
    public float LeafCloseSec { get; init; }

    // --- 3. The shove (§5 step 3) ---------------------------------------------------------

    /// <summary>Radius around the doorway inside which a Loose or Resting prop is shoved. 3.0 m.
    /// Server-only; see <c>PropManager.ServerBurstImpulse</c>.</summary>
    public float BurstRadiusM { get; init; }

    /// <summary>Impulse at the doorway itself, newton-seconds, falling off LINEARLY to zero at
    /// <see cref="BurstRadiusM"/> (<see cref="StartleTimeline.ImpulseNsAt"/>). Newton-seconds
    /// rather than a velocity so a heavy prop is shoved less than a light one by construction —
    /// 6 N·s on the 1 kg crate this game ships is 6 m/s, which is a stack of sorted objects going over.</summary>
    public float BurstImpulseNs { get; init; }

    /// <summary><b>The flinch.</b> On the burst tick the server releases the hider's held prop
    /// into Loose, through <c>PropManager</c>'s existing public release funnel. Default on.</summary>
    public bool ForceDropOnBurst { get; init; }

    // --- 4. The kick and the bang (§5 step 3) ---------------------------------------------

    /// <summary>Amplitude of the hiders' camera kick, degrees. 4 — enough to feel struck, small
    /// enough that it is not a camera the player has to wait out.</summary>
    public float KickDegrees { get; init; }

    /// <summary>How long the kick takes to decay to nothing. 0.3 s. One full sine cycle over that
    /// window, decaying linearly — the frequency is DERIVED from this rather than being a second
    /// knob (see <see cref="StartleTimeline.KickPitchDegAt"/>).</summary>
    public float KickSec { get; init; }

    /// <summary>Gain of the second, flat bang on the hiders' intercom bus, dB. −3. §5: a
    /// positional-only bang is too quiet if the hider happens to be facing away.</summary>
    public float BangFlatDb { get; init; }

    // --- 5. The three anticipation knobs, all OFF (§5's last paragraph) -------------------

    /// <summary>Fake knocks per minute during Seeking, hider-side. <b>0 = off</b>, which is the
    /// shipped value. Server-seeded when it is turned on, so every peer hears the same knock at
    /// the same instant rather than each rolling its own.</summary>
    public float FakeKnockRatePerMin { get; init; }

    /// <summary>Whether the seeker may rattle the door from their side as a taunt.
    /// <b>False = off.</b></summary>
    public bool SeekerKnockKey { get; init; }

    /// <summary>Whether the hiders' intercom carries a crackle whose gain follows the seeker's
    /// distance to the target. <b>False = off.</b></summary>
    public bool SeekerHeatOnIntercom { get; init; }

    /// <summary>The packet's numbers and §5's, unchanged.</summary>
    public static readonly StartleTuning Default = new()
    {
        TellSec = 0.4f,
        TellLightDipFraction = 0.40f,
        LeafOpenSec = 0.12f,
        LeafOpenDeg = 110f,
        LeafOvershootDeg = 125f,
        LeafSettleSec = 0.25f,
        LeafCloseSec = 0.6f,
        BurstRadiusM = 3.0f,
        BurstImpulseNs = 6f,
        ForceDropOnBurst = true,
        KickDegrees = 4f,
        KickSec = 0.3f,
        BangFlatDb = -3f,
        FakeKnockRatePerMin = 0f,
        SeekerKnockKey = false,
        SeekerHeatOnIntercom = false,
    };

    /// <summary>The live tuning — the one-mutable-static idiom <see cref="HideSeekTuning.Current"/>
    /// and <c>MotorTuning.Current</c> both use, so the door, the camera and the synth can never
    /// read two different sessions.</summary>
    public static StartleTuning Current { get; set; } = Default;

    /// <summary>
    /// Replaces every non-finite or out-of-range value and reports each replacement in
    /// <paramref name="warnings"/>. Called once, by the overlay loader; a caller that builds a
    /// tuning in code is trusted, exactly as <c>MotorTuning.Validate</c>'s callers are.
    ///
    /// <para><b>The one ORDERING rule is <see cref="LeafOvershootDeg"/> ≥
    /// <see cref="LeafOpenDeg"/>.</b> An overshoot below the rest angle is not a bounce, it is
    /// the leaf arriving from the wrong side, and it would read on screen as a door that opens
    /// past itself and then keeps going.</para>
    /// </summary>
    public static StartleTuning Validate(in StartleTuning t, out IReadOnlyList<string> warnings)
    {
        var w = new List<string>();
        StartleTuning v = t;
        foreach (StartleKnob knob in StartleKnobs.All)
        {
            float raw = knob.Get(v);
            if (!float.IsFinite(raw))
            {
                w.Add($"{knob.Name} was {raw}, which is not a finite number — replaced by the "
                      + $"shipped default {knob.Default}.");
                v = knob.Set(v, knob.Default);
                continue;
            }
            if (raw < knob.Min || raw > knob.Max)
            {
                float clamped = Math.Clamp(raw, knob.Min, knob.Max);
                w.Add($"{knob.Name} was {raw}, outside [{knob.Min}, {knob.Max}] — clamped to "
                      + $"{clamped}.");
                v = knob.Set(v, clamped);
            }
        }
        if (v.LeafOvershootDeg < v.LeafOpenDeg)
        {
            w.Add($"LeafOvershootDeg ({v.LeafOvershootDeg}) is less than LeafOpenDeg "
                  + $"({v.LeafOpenDeg}) — raised to LeafOpenDeg. An overshoot under the rest "
                  + "angle is not a bounce; it is the leaf arriving from the wrong side.");
            v = v with { LeafOvershootDeg = v.LeafOpenDeg };
        }
        warnings = w;
        return v;
    }
}

/// <summary>One overlayable number: enough metadata to name it in JSON, bound it, and read or
/// write it. Deliberately a tenth of <c>MotorKnob</c> — that table carries a declaration site, a
/// group, a unit and a bound REASON per row because a live tuning panel renders them, and this
/// one has no panel. What it keeps is the part the overlay needs: a name, a default, a window,
/// and the two accessors.</summary>
public readonly record struct StartleKnob
{
    public string Name { get; init; }
    public float Default { get; init; }
    public float Min { get; init; }
    public float Max { get; init; }
    public Func<StartleTuning, float> Get { get; init; }
    public Func<StartleTuning, float, StartleTuning> Set { get; init; }
}

/// <summary>The knob table. <b>Booleans ride as 0/1</b> rather than as JSON <c>true</c>/<c>false</c>
/// so the overlay has ONE value shape and one malformed-value rule; anything non-zero is on, which
/// is the convention <c>MotorTuning.BodyYawFollowsAim</c> (knob 58) already uses for a 0/1
/// row.</summary>
public static class StartleKnobs
{
    /// <summary><paramref name="shipped"/> is not decoration. <see cref="StartleTuningFile.ToJson"/>
    /// decides what to omit by comparing the live value against <see cref="StartleKnob.Default"/>,
    /// so a bool row that claimed a default of 0 while the struct shipped <c>true</c> would write
    /// <c>"ForceDropOnBurst": 1</c> into every "nothing moved" file — which then pins the flinch
    /// on against a future change to the shipped constant, silently. Caught by
    /// <c>StartleTimelineTests.Overlay_EveryKnobsTableDefault_IsTheLiteralOnTheShippedTuning</c>,
    /// which is a property over the table rather than a row-by-row transcription for exactly this
    /// reason.</summary>
    private static StartleKnob Bool(string name, bool shipped, Func<StartleTuning, bool> get,
        Func<StartleTuning, bool, StartleTuning> set) => new()
    {
        Name = name, Default = shipped ? 1f : 0f, Min = 0f, Max = 1f,
        Get = t => get(t) ? 1f : 0f,
        Set = (t, v) => set(t, v != 0f),
    };

    public static readonly IReadOnlyList<StartleKnob> All = new StartleKnob[]
    {
        new() { Name = "TellSec", Default = 0.4f, Min = 0f, Max = 1.5f,
                Get = t => t.TellSec, Set = (t, v) => t with { TellSec = v } },
        new() { Name = "TellLightDipFraction", Default = 0.40f, Min = 0f, Max = 1f,
                Get = t => t.TellLightDipFraction, Set = (t, v) => t with { TellLightDipFraction = v } },
        new() { Name = "LeafOpenSec", Default = 0.12f, Min = 0.01f, Max = 2f,
                Get = t => t.LeafOpenSec, Set = (t, v) => t with { LeafOpenSec = v } },
        new() { Name = "LeafOpenDeg", Default = 110f, Min = 10f, Max = 170f,
                Get = t => t.LeafOpenDeg, Set = (t, v) => t with { LeafOpenDeg = v } },
        new() { Name = "LeafOvershootDeg", Default = 125f, Min = 10f, Max = 175f,
                Get = t => t.LeafOvershootDeg, Set = (t, v) => t with { LeafOvershootDeg = v } },
        new() { Name = "LeafSettleSec", Default = 0.25f, Min = 0f, Max = 2f,
                Get = t => t.LeafSettleSec, Set = (t, v) => t with { LeafSettleSec = v } },
        new() { Name = "LeafCloseSec", Default = 0.6f, Min = 0.05f, Max = 4f,
                Get = t => t.LeafCloseSec, Set = (t, v) => t with { LeafCloseSec = v } },
        new() { Name = "BurstRadiusM", Default = 3.0f, Min = 0f, Max = 12f,
                Get = t => t.BurstRadiusM, Set = (t, v) => t with { BurstRadiusM = v } },
        new() { Name = "BurstImpulseNs", Default = 6f, Min = 0f, Max = 60f,
                Get = t => t.BurstImpulseNs, Set = (t, v) => t with { BurstImpulseNs = v } },
        new() { Name = "KickDegrees", Default = 4f, Min = 0f, Max = 20f,
                Get = t => t.KickDegrees, Set = (t, v) => t with { KickDegrees = v } },
        new() { Name = "KickSec", Default = 0.3f, Min = 0f, Max = 2f,
                Get = t => t.KickSec, Set = (t, v) => t with { KickSec = v } },
        new() { Name = "BangFlatDb", Default = -3f, Min = -60f, Max = 6f,
                Get = t => t.BangFlatDb, Set = (t, v) => t with { BangFlatDb = v } },
        new() { Name = "FakeKnockRatePerMin", Default = 0f, Min = 0f, Max = 30f,
                Get = t => t.FakeKnockRatePerMin, Set = (t, v) => t with { FakeKnockRatePerMin = v } },
        Bool("ForceDropOnBurst", true, t => t.ForceDropOnBurst,
             (t, v) => t with { ForceDropOnBurst = v }),
        Bool("SeekerKnockKey", false, t => t.SeekerKnockKey,
             (t, v) => t with { SeekerKnockKey = v }),
        Bool("SeekerHeatOnIntercom", false, t => t.SeekerHeatOnIntercom,
             (t, v) => t with { SeekerHeatOnIntercom = v }),
    };

    /// <summary>Case-insensitive, because a hand-edited file is the whole point of the overlay and
    /// "tellsec" failing silently is the kind of thing that costs an evening.</summary>
    public static bool TryByName(string name, out StartleKnob knob)
    {
        foreach (StartleKnob k in All)
        {
            if (string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                knob = k;
                return true;
            }
        }
        knob = default;
        return false;
    }
}

/// <summary>The outcome of a load. Same shape and same contract as <c>MotorTuningLoad</c>.</summary>
/// <param name="Tuning">Always usable — <see cref="StartleTuning.Default"/> where the file was
/// discarded.</param>
/// <param name="Warnings">One line per thing ignored, clamped or replaced.</param>
/// <param name="FileExisted">False when there is no overlay, which is every ordinary launch and
/// is not warned about.</param>
/// <param name="Discarded">True when the WHOLE document was thrown away (parse failure or an
/// unknown version) — never a partial application: half a corrupt file is a tuning nobody
/// authored.</param>
public readonly record struct StartleTuningLoad(StartleTuning Tuning, IReadOnlyList<string> Warnings,
    bool FileExisted, bool Discarded);

/// <summary>
/// <b>The startle overlay Talon can edit without a rebuild</b> — the packet's
/// "<c>--startle-file</c>, mirror how <c>MotorTuningFile</c> loads". Same document shape
/// (<c>version</c>, <c>savedUtc</c>, a sparse <c>values</c> object), the same four malformed
/// cases with the same outcomes, and the same "a parse failure discards the WHOLE file" rule.
///
/// <para><b>Three deliberate differences from <c>MotorTuningFile</c>, all of them about who
/// writes it.</b> The motor file is written BY the game (a lab session's apply) and read back;
/// this one is written by a person in a text editor and only ever read. So: the path is given on
/// the command line rather than fixed under <c>user://</c> (a tuning pass on a door wants the
/// file beside the notes, not buried in an AppData folder); names match case-insensitively; and
/// <see cref="ToJson"/> exists to SEED a starting file rather than to save state.</para>
///
/// <para><b>Godot is touched in exactly one method.</b> Everything else is <c>System.IO</c> and
/// <c>System.Text.Json</c> over an absolute path, so every malformed case is exercised by the
/// unit suite with no engine.</para>
/// </summary>
public static class StartleTuningFile
{
    /// <summary>Where an unqualified <c>--startle-file</c> lands if somebody wants one without
    /// typing a path. Resolved through <c>ProjectSettings</c>, never hard-coded.</summary>
    public const string DefaultUserPath = "user://startle-tuning.json";

    /// <summary>The format version this build writes and is willing to read. An unknown version
    /// is discarded rather than guessed at.</summary>
    public const int FormatVersion = 1;

    /// <summary>The one call that needs a live engine.</summary>
    public static string DefaultAbsolutePath() => Godot.ProjectSettings.GlobalizePath(DefaultUserPath);

    /// <summary>Renders a tuning as the overlay document: a version, a UTC stamp, and only the
    /// fields that differ from the shipped defaults. Sparse for <c>MotorTuningFile</c>'s reason —
    /// a file that restates a default silently pins it against a future change to the shipped
    /// constant.</summary>
    public static string ToJson(in StartleTuning tuning, DateTimeOffset savedUtc)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", FormatVersion);
            writer.WriteString("savedUtc",
                savedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture));
            writer.WriteStartObject("values");
            foreach (StartleKnob knob in StartleKnobs.All)
            {
                float value = knob.Get(tuning);
                if (value == knob.Default)
                    continue;
                writer.WriteNumber(knob.Name, value);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Every malformed case, handled and reported: a parse failure or an unknown version
    /// discards the whole document; an unknown field name is ignored; a missing field takes its
    /// shipped default (the normal case for a sparse file); and every surviving value goes through
    /// <see cref="StartleTuning.Validate"/>.</summary>
    public static StartleTuningLoad FromJson(string json)
    {
        var warnings = new List<string>();
        StartleTuning t = StartleTuning.Default;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException e)
        {
            warnings.Add("the startle overlay is not valid JSON and the WHOLE file was discarded — "
                       + $"every knob is at its shipped default. ({e.Message})");
            return new StartleTuningLoad(StartleTuning.Default, warnings, true, true);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("the startle overlay is JSON but not an object, so the WHOLE file was "
                           + "discarded — every knob is at its shipped default.");
                return new StartleTuningLoad(StartleTuning.Default, warnings, true, true);
            }

            int version = root.TryGetProperty("version", out JsonElement v)
                          && v.ValueKind == JsonValueKind.Number
                          && v.TryGetInt32(out int parsed)
                ? parsed
                : -1;
            if (version != FormatVersion)
            {
                warnings.Add($"the startle overlay declares version "
                           + $"{(version < 0 ? "(missing)" : version.ToString(CultureInfo.InvariantCulture))}, "
                           + $"and this build reads version {FormatVersion} — the WHOLE file was "
                           + "discarded. Forward compatibility is a promise this file does not make.");
                return new StartleTuningLoad(StartleTuning.Default, warnings, true, true);
            }

            if (!root.TryGetProperty("values", out JsonElement values)
                || values.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("the startle overlay has no `values` object — every knob is at its "
                           + "shipped default. (An empty `values` is the legitimate 'nothing "
                           + "moved' file.)");
                return new StartleTuningLoad(StartleTuning.Default, warnings, true, false);
            }

            foreach (JsonProperty field in values.EnumerateObject())
            {
                if (!StartleKnobs.TryByName(field.Name, out StartleKnob knob))
                {
                    warnings.Add($"`{field.Name}` is not a startle knob this build has — ignored. "
                               + "A knob that was removed must not stop the file loading.");
                    continue;
                }

                // A bool written the natural way is accepted as well as 0/1: the file is
                // hand-edited, and refusing `"ForceDropOnBurst": false` on a technicality would
                // be a trap rather than a rule.
                if (field.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    t = knob.Set(t, field.Value.ValueKind == JsonValueKind.True ? 1f : 0f);
                    continue;
                }

                if (field.Value.ValueKind != JsonValueKind.Number
                    || !field.Value.TryGetDouble(out double raw))
                {
                    warnings.Add($"{knob.Name} is not a number in the file — replaced by the "
                               + $"shipped default {knob.Default}.");
                    continue;
                }

                t = knob.Set(t, (float)raw);
            }
        }

        t = StartleTuning.Validate(t, out IReadOnlyList<string> clamps);
        warnings.AddRange(clamps);
        return new StartleTuningLoad(t, warnings, true, false);
    }

    /// <summary>Reads an overlay from an absolute path. A file that is absent is the ordinary
    /// launch and is silent: <see cref="StartleTuning.Default"/>, no warning.</summary>
    public static StartleTuningLoad LoadFrom(string absolutePath)
    {
        if (!File.Exists(absolutePath))
            return new StartleTuningLoad(StartleTuning.Default, Array.Empty<string>(), false, false);

        string text;
        try
        {
            text = File.ReadAllText(absolutePath);
        }
        catch (IOException e)
        {
            return new StartleTuningLoad(StartleTuning.Default,
                new[] { $"{absolutePath} could not be read — every startle knob is at its shipped "
                      + $"default. ({e.Message})" },
                true, true);
        }
        return FromJson(text);
    }
}
