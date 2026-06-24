# Phase 0 Research: Domain Webhook Management

Most Technical Context items were known from the existing codebase (the `MailgunSuppressions` capability
area is the direct template). The decisions below resolve the spec's open wire shapes and, critically,
reconcile a wire-version error discovered during planning so Phase 1 can proceed without
`NEEDS CLARIFICATION`.

## 1. Wire surface: v3, not v4 (corrected during planning)

- **Decision**: Target the Mailgun **v3** domain-webhook surface `{base}/v3/{domain}/webhooks`:
  - `GET /v3/{domain}/webhooks` — list all webhooks. Response:
    `{"webhooks":{"delivered":{"urls":[…]},"opened":{"urls":[…]},…}}` (an object keyed by event type,
    each with a `urls` array). Event types with no registration are absent or carry an empty `urls`.
  - `GET /v3/{domain}/webhooks/{name}` — read one event type. Response: `{"webhook":{"urls":[…]}}`.
  - `POST /v3/{domain}/webhooks` — create for one event type. Form fields: `id` = event-type token, and
    one or more `url` (Mailgun caps at 3). Response: `{"webhook":{"urls":[…]}}` (with a `message`).
  - `PUT /v3/{domain}/webhooks/{name}` — replace the URL list for that event type with the supplied
    `url`(s). Response: `{"webhook":{"urls":[…]}}`.
  - `DELETE /v3/{domain}/webhooks/{name}` — delete that event type's webhook.
- **Rationale**: The spec input and a transient constitution amendment (v1.3.0) named **v4**, intending
  "multiple callback URLs per event type." Verifying the live Mailgun (2026) contract showed this is
  backwards:
  - The **v3** surface is **event-type-centric** — one webhook keyed by a single event type, associating
    one or more URLs, with per-event-type `.../{name}` endpoints and a `GET` list/read. This is exactly
    the model in user stories US1–US4, the data model, and every acceptance scenario.
  - The real **v4** surface is **URL-centric**: `POST`/`PUT /v4/{domain}/webhooks` take `url` + repeated
    `event_types` (one URL associated with many event types), `DELETE /v4/{domain}/webhooks?url=…` removes
    a URL from all its event types, and **there is no v4 `GET`** and **no v4 `.../{name}` endpoint**. v4
    cannot back the stories as written (no read, no per-event-type update/delete).
  - Therefore the capability the library promises is v3. The constitution was corrected v4 → v3
    (v1.4.0) and the spec's FR-005 / clarifications / assumptions aligned, all in this change.
- **Alternatives considered**:
  - *Literal v4 (URL-centric), rewrite the spec* — rejected by the user during planning: it inverts the
    event-type-keyed model the whole feature is designed around and still needs v3 `GET` for read (a
    hybrid), for no benefit.
  - *Hybrid (v4 mutate + v3 read) behind an event-type facade* — rejected: v4 mutations are URL-keyed and
    cannot express per-event-type update/delete cleanly; it would leak surprising semantics.

## 2. Request encoding: form parts, not a JSON body

- **Decision**: Create and update send their fields as **`multipart/form-data`** parts (`id` plus one or
  more `url` for create; `url`(s) for update), reusing the same form-content approach as the message send
  path. List, read-one, and delete carry **no request body**. **Responses are JSON** and are deserialized
  with source generation.
- **Rationale**: The live v3 contract documents create as form fields (`-F id=… -F url=…`), and update by
  the `url` param; these are not JSON request bodies. The spec's FR-010 emphasis on "JSON request" was
  part of the same v4 misreading; it is corrected to "JSON **responses**, form-encoded create/update
  requests." Using `MultipartFormDataContent` lets repeated `url` parts be asserted with the existing
  `StubHttpMessageHandler` `Values("url")`/`Count("url")` helpers, and matches Mailgun's documented
  example. The constitution's "JSON (de)serialization MUST use source generation" applies to the
  responses, which it does.
- **Alternatives considered**: `application/x-www-form-urlencoded` (`FormUrlEncodedContent`) — equally
  valid on the wire and lighter, but the test fakes give richer assertions over multipart parts, and
  multipart matches the documented curl example and the existing send pipeline. Either is acceptable;
  multipart is chosen for test ergonomics and consistency.

## 3. Public shape: a capability area mirroring `client.Suppressions`

- **Decision**: Expose `IMailgunWebhooks Webhooks { get; }` on `IMailgunnerClient`, backed by a lazy
  internal `MailgunWebhooks` constructed over the configured `HttpClient` + trimmed domain — the identical
  pattern to `IMailgunSuppressions`/`MailgunSuppressions`. Operations: `ListAsync`, `GetAsync(eventType)`,
  `CreateAsync(eventType, urls)`, `CreateAsync(eventTypes, url)` (the one-URL-across-many fan-out),
  `UpdateAsync(eventType, urls)`, `DeleteAsync(eventType)`.
- **Rationale**: FR-001 mandates the suppressions-shaped capability area. Reusing the proven lazy-property
  pattern keeps the client constructor cheap and the surface familiar. Two `CreateAsync` overloads cover
  the spec's two registration shapes (one event type ↔ many URLs; one URL ↔ many event types).
- **Alternatives considered**: A single `CreateAsync` taking both a set of event types and a set of URLs —
  rejected as ambiguous (it would imply a cross-product the v3 API does not offer in one call) and harder
  to document against the per-event-type wire reality.

## 4. Event-type model: a closed typed enum with explicit wire tokens

- **Decision**: Model the supported set as a public `enum WebhookEventType { Delivered, Opened, Clicked,
  Unsubscribed, Complained, PermanentFail, TemporaryFail }`. A small internal map converts each value to
  and from its wire token (`delivered`, `opened`, `clicked`, `unsubscribed`, `complained`,
  `permanent_fail`, `temporary_fail`). `PermanentFail` is documented as the "failed" event.
- **Rationale**: FR-002 requires a typed, closed set so a caller cannot silently target an unrecognized
  event type. An enum gives compile-time safety; the explicit token map handles the snake_case wire names
  and isolates the wire vocabulary from the public name. The list response is parsed by mapping each
  present key back to its enum value (unknown keys are ignored for forward-tolerance).
- **Alternatives considered**:
  - *Accept a free string* — rejected by FR-002.
  - *Include `accepted`* — Mailgun supports it, but the spec's closed set excludes it; left out
    deliberately (it can be added later as a SemVer MINOR enum addition without breaking callers).

## 5. The one-URL-across-many-event-types fan-out (FR-004)

- **Decision**: `CreateAsync(IEnumerable<WebhookEventType> eventTypes, string url, …)` issues **one v3
  create per event type, sequentially, in the supplied order**, and returns one `WebhookRegistration` per
  event type on full success. On a non-2xx from any single create it is **fail-fast with no rollback**:
  the first failure throws the single `MailgunnerException` and stops the remaining creates; registrations
  already created are left in place.
- **Rationale**: Resolved by clarification (Session 2026-06-24) and consistent with the batch-send
  fail-fast contract already in the library. v3 create targets exactly one event type per call, so the
  fan-out is the only way to express "one URL for several event types." Issuing sequentially makes the
  request count and order deterministic and offline-assertable (SC-002).
- **Alternatives considered**: Concurrent fan-out — rejected: non-deterministic request order defeats the
  partial-failure assertion and offers no real benefit for a handful of event types. Rollback of
  already-created registrations — rejected by clarification (no rollback; state may be partial).

## 6. Error contract and input validation

- **Decision**: Any non-2xx response on any operation throws the single `MailgunnerException(statusCode,
  rawBody)` — including read-one / update / delete of an unregistered event type (the service returns
  not-found, surfaced verbatim) — exactly as the send and suppression paths do. Input validation (at least
  one non-blank `url` on create/update; a non-empty event-type set on the fan-out create) throws
  `System.ArgumentException` **before any request is issued**. No new exception type.
- **Rationale**: Constitution IV mandates exactly one HTTP-error type and forbids bespoke exception
  proliferation; `ArgumentException` is the library's established input-validation contract (sends,
  suppressions). The library does not pre-validate URL format or registration existence (FR per spec
  Assumptions); the service's validation/not-found is surfaced through `MailgunnerException`.
- **Alternatives considered**: Returning a nullable/empty result for not-found read — rejected by FR-007
  (must surface the typed error, never a null/empty success).

## 7. Response deserialization: source-generated, projected to public models

- **Decision**: A new `internal sealed partial class WebhookJsonContext : JsonSerializerContext` registers
  the response DTOs: a list envelope `{"webhooks": { <event>: {"urls":[…]} }}` (a
  `Dictionary<string, WebhookUrlsDto?>`), a single envelope `{"webhook": {"urls":[…]}}`, and the shared
  `WebhookUrlsDto { urls }`. The internal DTOs are projected into the public `WebhookRegistration`
  (`EventType` + `Urls`) before leaving the library; `urls` is normalized to a non-null read-only list.
- **Rationale**: Constitution I/IV require trim/AOT-safe, reflection-free JSON. This mirrors
  `SuppressionJsonContext`/`SuppressionWireDtos`. Only read-side DTOs are needed (requests are form parts).
- **Alternatives considered**: `JsonDocument` ad-hoc parsing (as in `MailgunnerClient.TryParseResult`) —
  acceptable but the DTO + source-gen path is cleaner for the keyed-map list response and matches the
  suppressions precedent.
