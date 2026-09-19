# Telemetry Backend Setup (one-time, manual — Talon only)

The analytics/crash/feedback system POSTs directly to Firebase Firestore's REST API. The code
is complete and dormant until the two credentials below are filled into
`scripts/telemetry/TelemetryConfig.cs`. Until then `TelemetryConfig.IsConfigured` is `false`
and the client shows no prompts and sends nothing (it logs one line pointing here).

## 1. Create the project

1. Firebase console → **Add project** → free **Spark** plan (no billing account).
2. **Build → Firestore Database → Create database → Native mode**, any region.

## 2. Security rules (copy verbatim)

Firestore → Rules. This allows create-only on `events`, no read/update/delete, validates the
report type, and bounds document size (the Web API key is public, so the rules are the only
gatekeeper):

```
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    match /events/{event} {
      allow read, update, delete: if false;
      allow create: if
        request.resource.data.report_type in ['usage', 'crash', 'feedback']
        && request.resource.data.keys().size() <= 40
        && request.resource.data.device_id is string
        && request.resource.data.device_id.size() <= 64;
    }
    match /{document=**} {
      allow read, write: if false;
    }
  }
}
```

## 3. Get the credentials

- **Project ID:** Project settings → General → *Project ID*.
- **Web API key:** Project settings → General → *Web API Key*.

Paste both into `scripts/telemetry/TelemetryConfig.cs` (`ProjectId`, `ApiKey`). They are meant
to be public and committed.

## 4. REST request shape (what the client sends — for reference)

`POST https://firestore.googleapis.com/v1/projects/<ProjectId>/databases/(default)/documents/events?key=<ApiKey>`
`Content-Type: application/json`

Body (Firestore typed values; example — real reports carry the full envelope):

```json
{
  "fields": {
    "device_id":    { "stringValue": "3f2b...-guid" },
    "report_type":  { "stringValue": "usage" },
    "sent_at":      { "stringValue": "2026-07-13T18:04:11.2Z" },
    "ram_mb":       { "integerValue": "16384" },
    "voice_used":   { "booleanValue": true },
    "stack_trace":  { "nullValue": null }
  }
}
```

A `201 Created` (HTTP 200-family) means the write succeeded.
