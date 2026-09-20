using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;

namespace MpFoundation.Net;

/// <summary>The outcome of a load: the tuning to use, and every warning the readout must show.</summary>
/// <param name="Tuning">Always usable — <c>MotorTuning.Default</c> where the file was discarded.</param>
/// <param name="Warnings">One line per thing that was ignored, clamped or replaced. Empty is the
/// clean case.</param>
/// <param name="FileExisted">False on a first run, which is not a problem and is not warned about.</param>
/// <param name="Discarded">True when the whole file was thrown away (parse failure or an unknown
/// version) — never a partial application: half a corrupt file is a tuning nobody authored.</param>
public readonly record struct MotorTuningLoad(MotorTuning Tuning, IReadOnlyList<string> Warnings,
    bool FileExisted, bool Discarded);

/// <summary>
/// <b>The tuning file</b> — MOVE-4a §6.2 and §6.3. <c>user://movement-tuning.json</c>, Godot's
/// per-user data directory, <b>deliberately outside the repo</b>: a lab experiment must not be able
/// to change the game for everyone by being committed. The path from a tuning worth keeping to the
/// game is the printed C# (<see cref="MotorTuningPrint"/>) and a reviewed commit, which is the
/// whole point of that format.
///
/// <para><b>Sparse by design.</b> <c>values</c> carries only what differs from
/// <see cref="MotorTuning.Default"/>. A full dump would be thirty-one lines of which twenty-eight say
/// "unchanged", and a file that restates a default silently pins that default against a future
/// change to the shipped constant.</para>
///
/// <para><b>Godot-free by construction.</b> Everything below is <c>System.IO</c> and
/// <c>System.Text.Json</c> over absolute paths; the engine is consulted only by
/// <see cref="AbsolutePath"/>, so the whole format and every malformed case is exercised by the
/// unit suite without a running engine.</para>
///
/// <para><b>Not a save-game</b> (MECHANICS §5): the tuning survives a process restart; it does not
/// survive a machine change, is not carried across a network session, and is part of no save.</para>
/// </summary>
public static class MotorTuningFile
{
    /// <summary>The engine-relative location. Resolved through <c>ProjectSettings</c>, never
    /// hard-coded to a real directory.</summary>
    public const string UserPath = "user://movement-tuning.json";

    /// <summary>Where a printed tuning is archived, one file per print (§6.2).</summary>
    public const string PrintDirUserPath = "user://movement-tuning-prints";

    /// <summary>The format version this build writes and is willing to read. An unknown version is
    /// discarded rather than guessed at: forward compatibility is a promise this file does not
    /// make.</summary>
    public const int FormatVersion = 1;

    /// <summary>The real filesystem path of <see cref="UserPath"/>. The one call that needs a live
    /// engine.</summary>
    public static string AbsolutePath() => ProjectSettings.GlobalizePath(UserPath);

    /// <summary>The real filesystem path of <see cref="PrintDirUserPath"/>.</summary>
    public static string PrintDirAbsolutePath() => ProjectSettings.GlobalizePath(PrintDirUserPath);

    // --- Serialise ------------------------------------------------------------------------------

    /// <summary>
    /// Renders <paramref name="tuning"/> as the §6.3 document: a version, a UTC stamp, and only the
    /// fields that differ from the shipped defaults.
    /// </summary>
    public static string ToJson(in MotorTuning tuning, DateTimeOffset savedUtc)
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
            foreach (MotorKnob knob in MotorTuningKnobs.All)
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

    // --- Deserialise ----------------------------------------------------------------------------

    /// <summary>
    /// <b>Every malformed case §6.3 names, handled and reported.</b> A parse failure or an unknown
    /// version discards the whole document; an unknown field name is ignored; a missing field takes
    /// its shipped default, which is the normal case for a sparse file; and every surviving value
    /// then goes through <see cref="MotorTuning.Validate"/>, which replaces non-finite values and
    /// clamps out-of-range and out-of-order ones.
    /// </summary>
    public static MotorTuningLoad FromJson(string json)
    {
        var warnings = new List<string>();
        MotorTuning t = MotorTuning.Default;

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
            warnings.Add($"{UserPath} is not valid JSON and the WHOLE file was discarded — every "
                       + $"knob is at its shipped default. ({e.Message})");
            return new MotorTuningLoad(MotorTuning.Default, warnings, true, true);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                warnings.Add($"{UserPath} is JSON but not an object, so the WHOLE file was "
                           + "discarded — every knob is at its shipped default.");
                return new MotorTuningLoad(MotorTuning.Default, warnings, true, true);
            }

            int version = root.TryGetProperty("version", out JsonElement v)
                          && v.ValueKind == JsonValueKind.Number
                          && v.TryGetInt32(out int parsed)
                ? parsed
                : -1;
            if (version != FormatVersion)
            {
                warnings.Add($"{UserPath} declares version {(version < 0 ? "(missing)" : version)}, "
                           + $"and this build reads version {FormatVersion} — the WHOLE file was "
                           + "discarded. Forward compatibility is a promise this file does not make.");
                return new MotorTuningLoad(MotorTuning.Default, warnings, true, true);
            }

            if (!root.TryGetProperty("values", out JsonElement values)
                || values.ValueKind != JsonValueKind.Object)
            {
                warnings.Add($"{UserPath} has no `values` object — every knob is at its shipped "
                           + "default. (An empty `values` is the legitimate 'nothing moved' file.)");
                return new MotorTuningLoad(MotorTuning.Default, warnings, true, false);
            }

            foreach (JsonProperty field in values.EnumerateObject())
            {
                if (!MotorTuningKnobs.TryByName(field.Name, out MotorKnob knob))
                {
                    warnings.Add($"`{field.Name}` is not a knob this build has — ignored. A knob "
                               + "that was removed must not stop the file loading.");
                    continue;
                }

                if (field.Value.ValueKind != JsonValueKind.Number
                    || !field.Value.TryGetDouble(out double raw))
                {
                    warnings.Add($"{knob.Name} is not a number in the file — replaced by the "
                               + $"shipped default {MotorTuningPrint.Literal(knob.Default, knob.Step)}.");
                    continue;
                }

                t = knob.Set(t, (float)raw);
            }
        }

        t = MotorTuning.Validate(t, out IReadOnlyList<string> clamps);
        warnings.AddRange(clamps);
        return new MotorTuningLoad(t, warnings, true, false);
    }

    // --- Files ----------------------------------------------------------------------------------

    /// <summary>Writes the document to an absolute path, creating the directory if needed. UTF-8,
    /// no BOM.</summary>
    public static void SaveTo(string absolutePath, in MotorTuning tuning, DateTimeOffset savedUtc)
    {
        string? dir = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(absolutePath, ToJson(tuning, savedUtc), new UTF8Encoding(false));
    }

    /// <summary>Reads and applies §6.3's load rules. A file that is absent is the first run and is
    /// silent: <c>Default</c>, no warning.</summary>
    public static MotorTuningLoad LoadFrom(string absolutePath)
    {
        if (!File.Exists(absolutePath))
            return new MotorTuningLoad(MotorTuning.Default, Array.Empty<string>(), false, false);

        string text;
        try
        {
            text = File.ReadAllText(absolutePath);
        }
        catch (IOException e)
        {
            return new MotorTuningLoad(MotorTuning.Default,
                new[] { $"{absolutePath} could not be read — every knob is at its shipped default. "
                      + $"({e.Message})" },
                true, true);
        }
        return FromJson(text);
    }

    /// <summary>
    /// <b>Written on every apply</b>, not only on quit (§6.2): a lab session that ends in a crash
    /// or an Alt-F4 must not lose the setting, and "the settings he keeps" is the value of the
    /// whole wave. Needs a live engine to resolve <c>user://</c>.
    /// </summary>
    public static void Save(in MotorTuning tuning) =>
        SaveTo(AbsolutePath(), tuning, DateTimeOffset.UtcNow);

    /// <summary>Reads the tuning file. <b>At playground startup only</b> — a hot reload mid-session
    /// would be a second writer of <c>MotorTuning.Current</c>. Needs a live engine.</summary>
    public static MotorTuningLoad Load() => LoadFrom(AbsolutePath());
}
