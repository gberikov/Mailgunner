# Phase 1 Data Model: Domain Webhook Management

The feature adds two public types (an enum and a record), one public capability interface, and the
internal wire DTOs + event-token mapping. No persistence is involved; the only mutable wire artifacts are
the form fields sent on create/update and the JSON responses parsed back.

## Public type: `WebhookEventType` (enum)

The closed set of supported delivery events. A caller can only name one of these; an unrecognized event
type cannot be targeted (FR-002).

| Member | Wire token | Notes |
|--------|-----------|-------|
| `Delivered` | `delivered` | Message delivered. |
| `Opened` | `opened` | Recipient opened the message. |
| `Clicked` | `clicked` | Recipient clicked a tracked link. |
| `Unsubscribed` | `unsubscribed` | Recipient unsubscribed. |
| `Complained` | `complained` | Recipient marked as spam. |
| `PermanentFail` | `permanent_fail` | Permanent failure (a.k.a. "failed"). |
| `TemporaryFail` | `temporary_fail` | Temporary failure. |

`accepted` is supported by Mailgun but intentionally **excluded** from this feature's closed set; it can
be added later as a backward-compatible enum addition.

## Public type: `WebhookRegistration` (record)

The unit returned by read-one and update, and the per-event-type element of a list. Associates one event
type with its callback URL(s).

| Field | Type | Notes |
|-------|------|-------|
| `EventType` | `WebhookEventType` | The event type this registration is keyed by. |
| `Urls` | `IReadOnlyList<string>` | The callback URL(s) Mailgun invokes for this event type; never null, never empty for a real registration (1–3 URLs). |

Immutable value (record). Constructed only by the library from a parsed response.

## Public capability: `IMailgunWebhooks` (reached via `IMailgunnerClient.Webhooks`)

| Operation | Signature (CancellationToken omitted) | Maps to |
|-----------|----------------------------------------|---------|
| List | `Task<IReadOnlyList<WebhookRegistration>> ListAsync()` | `GET /v3/{domain}/webhooks` |
| Read one | `Task<WebhookRegistration> GetAsync(WebhookEventType eventType)` | `GET /v3/{domain}/webhooks/{token}` |
| Create | `Task<WebhookRegistration> CreateAsync(WebhookEventType eventType, IEnumerable<string> urls)` | `POST /v3/{domain}/webhooks` (`id`=token, `url`×N) |
| Create (fan-out) | `Task<IReadOnlyList<WebhookRegistration>> CreateAsync(IEnumerable<WebhookEventType> eventTypes, string url)` | one `POST` per event type, sequential, fail-fast |
| Update | `Task<WebhookRegistration> UpdateAsync(WebhookEventType eventType, IEnumerable<string> urls)` | `PUT /v3/{domain}/webhooks/{token}` (`url`×N) |
| Delete | `Task DeleteAsync(WebhookEventType eventType)` | `DELETE /v3/{domain}/webhooks/{token}` |

Every method takes a trailing `CancellationToken cancellationToken = default` and uses
`ConfigureAwait(false)` internally.

### Validation rules (enforced before any HTTP request)

1. **At least one URL on create/update**: `urls` must be non-null and contain at least one non-blank
   entry; otherwise `ArgumentException`. (Blank/whitespace entries are rejected.)
2. **Non-empty event-type set on the fan-out create**: `eventTypes` must be non-null and non-empty, and
   `url` must be non-blank; otherwise `ArgumentException`.
3. **No URL-format / existence pre-check**: beyond requiring at least one URL, the library does not
   validate URL syntax, de-duplicate, or check that an event type is already registered. Service-side
   validation, conflicts, and not-found are surfaced via `MailgunnerException` (see §error contract).
4. **Event type is typed**: an out-of-range `WebhookEventType` (e.g. an undefined cast) is rejected with
   `ArgumentOutOfRangeException` by the token mapping before any request.

### Error contract

- Any non-2xx response (create/list/read-one/update/delete) throws `MailgunnerException(statusCode,
  rawBody)`. Read-one / update / delete of an unregistered event type surface the service's not-found this
  way — never a null/empty success (FR-007/008/009).
- No new exception type is introduced; input failures use `ArgumentException` /
  `ArgumentOutOfRangeException`.

### List semantics

- `ListAsync` returns one `WebhookRegistration` per event type that has at least one URL. A domain with no
  registrations returns an **empty list** without error (FR-006). Unknown keys in the response map are
  ignored (forward-tolerant).

## Internal wire DTOs (`Internal/WebhookWireDtos.cs`) — responses only

| DTO | Shape | Used by |
|-----|-------|---------|
| `WebhookUrlsDto` | `{ "urls": string[]? }` | the inner object for every operation |
| `WebhookEnvelopeDto` | `{ "webhook": WebhookUrlsDto? }` | read-one, create, update responses |
| `WebhookListDto` | `{ "webhooks": Dictionary<string, WebhookUrlsDto?>? }` | list response (keyed by event token) |

Plus a static event-type ↔ wire-token map (`ToToken(WebhookEventType)` and
`TryParseToken(string) → WebhookEventType?`). Registered for source generation in `WebhookJsonContext`.

There are **no request DTOs**: create/update fields (`id`, `url`) are emitted as `MultipartFormDataContent`
parts; list/read-one/delete carry no body.

## Relationships

- `IMailgunnerClient.Webhooks : IMailgunWebhooks` — new property, lazily backed by an internal
  `MailgunWebhooks(HttpClient, domain)`, mirroring `IMailgunnerClient.Suppressions`.
- `MailgunWebhooks` reuses the client's configured `HttpClient` (region base URL + Basic auth) and the
  trimmed sending domain from feature 002; it shares nothing with the send or suppression paths.

## State / lifecycle

Stateless. Each call builds its request, issues it, and projects the response. The fan-out create holds
no cross-call state beyond the in-memory list of registrations created so far in that single call (left
intact on fail-fast).
