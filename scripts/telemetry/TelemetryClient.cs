using System;
using System.Threading.Tasks;
using Godot;

namespace MpFoundation.Telemetry;

/// <summary>
/// The thin HTTPRequest wrapper that turns a flat report into a Firestore REST document-create
/// POST. The send is exposed as an injectable delegate (<see cref="Send"/>) so CI stubs it and
/// never touches the live project; the real delegate is wired only when
/// <see cref="TelemetryConfig.IsConfigured"/> is true. Security is entirely Firestore rules —
/// the Web API key is public by design (spec decision 2).
/// </summary>
public sealed class TelemetryClient
{
    private const double TimeoutSec = 5.0;

    private readonly HttpRequest? _request;

    /// <summary>Sends one flat-report JSON string; returns success. Replaceable in tests.
    /// Default (no HTTPRequest, or unconfigured) fails closed so the report stays queued.</summary>
    public Func<string, Task<bool>> Send { get; set; }

    public TelemetryClient(HttpRequest? request)
    {
        _request = request;
        if (_request != null)
            _request.Timeout = TimeoutSec;
        Send = TelemetryConfig.IsConfigured && _request != null
            ? SendReal
            : _ => Task.FromResult(false);
    }

    /// <summary>Firestore document-create endpoint for the events collection.</summary>
    public static string Endpoint =>
        $"https://firestore.googleapis.com/v1/projects/{TelemetryConfig.ProjectId}" +
        $"/databases/(default)/documents/events?key={TelemetryConfig.ApiKey}";

    /// <summary>Converts a flat report dictionary into a Firestore-typed request body
    /// ({"fields":{name:{stringValue|integerValue|booleanValue|nullValue|mapValue}}}). Nested
    /// dictionaries (the feedback report's structured "answers" object) recurse into a Firestore
    /// mapValue rather than falling through to a stringified dump.</summary>
    public static string ToFirestoreBody(Godot.Collections.Dictionary flatReport) =>
        Json.Stringify(new Godot.Collections.Dictionary { { "fields", TypedFields(flatReport) } });

    private static Godot.Collections.Dictionary TypedFields(Godot.Collections.Dictionary dict)
    {
        var fields = new Godot.Collections.Dictionary();
        foreach (Variant key in dict.Keys)
            fields[key.AsString()] = TypedValue(dict[key]);
        return fields;
    }

    private static Godot.Collections.Dictionary TypedValue(Variant value) => value.VariantType switch
    {
        Variant.Type.Bool => new Godot.Collections.Dictionary { { "booleanValue", value.AsBool() } },
        Variant.Type.Int => new Godot.Collections.Dictionary
            { { "integerValue", value.AsInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) } },
        Variant.Type.Nil => new Godot.Collections.Dictionary { { "nullValue", new Variant() } },
        Variant.Type.Dictionary => new Godot.Collections.Dictionary
            { { "mapValue", new Godot.Collections.Dictionary { { "fields", TypedFields(value.AsGodotDictionary()) } } } },
        _ => new Godot.Collections.Dictionary { { "stringValue", value.AsString() } },
    };

    // Real POST: convert the flat JSON to a typed body, fire it, await completion (or the 5s
    // timeout), and report a 2xx as success. Never throws — a failure keeps the report queued.
    private async Task<bool> SendReal(string flatReportJson)
    {
        if (_request == null)
            return false;
        try
        {
            var parsed = Json.ParseString(flatReportJson);
            if (parsed.VariantType != Variant.Type.Dictionary)
                return false;
            string body = ToFirestoreBody(parsed.AsGodotDictionary());
            string[] headers = { "Content-Type: application/json" };

            var tcs = new TaskCompletionSource<bool>();
            void OnCompleted(long result, long code, string[] _, byte[] __)
            {
                _request.RequestCompleted -= OnCompleted;
                tcs.TrySetResult(result == (long)HttpRequest.Result.Success && code is >= 200 and < 300);
            }
            _request.RequestCompleted += OnCompleted;

            Error err = _request.Request(Endpoint, headers, Godot.HttpClient.Method.Post, body);
            if (err != Error.Ok)
            {
                _request.RequestCompleted -= OnCompleted;
                return false;
            }
            return await tcs.Task;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[telemetry] send failed: {e.Message}");
            return false;
        }
    }
}
