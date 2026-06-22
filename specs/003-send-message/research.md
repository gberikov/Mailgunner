# Phase 0 Research: Send a Single Email

All Technical Context unknowns are resolved below. No `NEEDS CLARIFICATION` markers remain (the
three clarification questions were resolved in the spec's Clarifications section).

---

## R1. How should `EmailAddress` be modeled (clarification Q1: a dedicated value type)?

**Decision**: A `public readonly struct EmailAddress : IEquatable<EmailAddress>` with:
- `Address` (required, non-empty) and `DisplayName` (optional) get-only properties set via a
  constructor that throws `ArgumentException` on a blank address.
- `ToString()` that formats the wire value: `"Display Name <address>"` when a display name is
  present, otherwise just `address`.
- An `implicit operator EmailAddress(string)` for ergonomics (the `EmailAddress(string)`
  constructor is the named alternative that satisfies CA2225).
- Hand-written `Equals`/`GetHashCode`/`==`/`!=` (value semantics).

**Rationale**: A `readonly struct` gives value semantics with no heap allocation and satisfies
**CA1815** (value types must override equality). Writing equality by hand avoids the
`IsExternalInit` polyfill a `record`/`init` type would require on `netstandard2.0`, keeping the
multi-target build clean with no extra shim. The implicit string conversion keeps call sites
readable (`message.To.Add("a@b.com")`) while the type still owns formatting.

**Alternatives considered**:
- *`readonly record struct`* — fewer lines, but risks an `IsExternalInit`/`with` interaction on
  `netstandard2.0`; rejected for predictability over a tiny amount of boilerplate.
- *Plain `string`* — rejected by clarification Q1.

---

## R2. How is the success response parsed, and how are bad/non-success bodies handled (clarification Q2)?

**Decision**: Read the response body once, then parse with `System.Text.Json`'s `JsonDocument`:
extract the string properties `id` and `message`. If the body is missing either property, is not
valid JSON, or the status is non-success, raise `MailgunnerException(statusCode, rawBody)`. Only a
2xx response that yields both `id` and `message` produces a `SendResult`.

**Rationale**: `JsonDocument` is reflection-free, so it works on `netstandard2.0` and triggers no
trim/AOT analyzer warnings (no DTO, no serializer configuration). Routing an unparseable 2xx body
to the same `MailgunnerException` implements clarification Q2 and the constitution's single-typed-
exception contract — one error path for every failure to obtain a usable result.

**Alternatives considered**:
- *`JsonSerializer.Deserialize<T>` + DTO* — reflection-based; risks IL2026/IL3050 warnings if the
  library is ever marked trimmable, and adds a type for no gain here.
- *Source-generated `JsonSerializerContext`* — more machinery than a two-field read needs.

---

## R3. How are multipart fields built, especially repeated recipients (FR-004)?

**Decision**: Build a `MultipartFormDataContent`. Add `from` once (the formatted sender). For each
recipient in `To`, add a separate `StringContent` part named `to`; likewise one `cc` part per Cc
and one `bcc` part per Bcc. Add `subject`, `text`, and `html` parts only when present. Blank
recipient entries are skipped (never added as empty fields).

**Rationale**: `MultipartFormDataContent.Add(content, name)` with a repeated name produces repeated
parts, which is exactly Mailgun's required representation (repeated `to` fields, not a comma-joined
string) and the constitution's API-fidelity rule. Keeping this in an internal
`MailgunMessageContent` builder makes it unit-testable directly and reusable by later features
(templates, batches) without duplicating multipart logic.

**Alternatives considered**:
- *Comma-joining recipients into one field* — explicitly forbidden (FR-004).

---

## R4. Where does the request path / domain come from?

**Decision**: `MailgunnerClient` takes `IOptions<MailgunnerOptions>` in addition to its typed
`HttpClient` (the typed-client factory resolves both). It reads the trimmed `Domain` and issues
`POST` to the **relative** URI `v3/{domain}/messages`, which combines with the region base URL set
in feature 002 (`https://api[.eu].mailgun.net/`).

**Rationale**: The base address (region host) and Basic auth are already configured on the typed
client by feature 002; only the domain-bearing path is feature-specific. Pulling the domain from
the same validated options avoids re-plumbing configuration and keeps a single source of truth.

**Alternatives considered**:
- *Pass the domain through `SendAsync`* — leaks configuration into every call site; rejected.

---

## R5. Cancellation and the `netstandard2.0` HTTP gap.

**Decision**: `SendAsync(MailgunMessage, CancellationToken)` passes the token to
`HttpClient.PostAsync(uri, content, cancellationToken)` and awaits with `ConfigureAwait(false)`.
Reading the body uses the token-bearing `ReadAsStringAsync(cancellationToken)` on modern targets
and the no-token overload on `netstandard2.0`, behind `#if NET8_0_OR_GREATER`. An already-canceled
or in-flight-canceled token surfaces as `OperationCanceledException`/`TaskCanceledException` — it is
**not** wrapped in `MailgunnerException`.

**Rationale**: Satisfies constitution Principle II (every public async method takes a token and uses
`ConfigureAwait(false)`). The `#if` is the one BCL gap on `netstandard2.0`, handled per the
established polyfill-by-`#if` pattern with no added dependency. The fake handler in tests honors the
token so cancellation is verified offline.

**Alternatives considered**:
- *Swallowing cancellation into the typed error* — hides intent and breaks cooperative cancellation.

---

## R6. Shape of `MailgunnerException` (and CA1032).

**Decision**: `public sealed class MailgunnerException : Exception` exposing `int StatusCode` and
`string ResponseBody`, constructed from `(int statusCode, string responseBody)` with a generated,
informative `Message` (e.g., `"Mailgun request failed with status {code}."`). The standard
parameterless / message-only / message+inner constructors are intentionally omitted; **CA1032 is
suppressed at the type** with a documented justification.

**Rationale**: The constitution mandates exactly one typed exception that exposes the HTTP status
code and raw body. `int StatusCode` faithfully represents any status (including non-standard codes)
better than the `HttpStatusCode` enum. The exception is never valid without its HTTP context, so the
standard constructors would permit invalid instances — suppressing CA1032 with justification is the
correct trade-off. No legacy `BinaryFormatter`/`ISerializable` surface is added (obsolete on modern
.NET).

**Alternatives considered**:
- *`HttpStatusCode StatusCode`* — can't cleanly carry non-standard numeric codes; `int` is more
  faithful to "raw status code".
- *Adding the standard ctors to satisfy CA1032* — would allow constructing the exception without its
  required HTTP context; rejected.

---

## R7. Why resilience (Polly) is deferred (Principle II).

**Decision**: Do not wire Polly retry in this feature. Record the deferral in the plan's Complexity
Tracking; keep the pinned Polly versions ready for a dedicated resilience feature.

**Rationale**: The spec explicitly scopes transient-fault handling out, and its acceptance criteria
treat a single 4xx/5xx as an immediate typed error — wiring retries now would change those tests and
pre-empt a focused resilience feature. Resilience is additive: a `DelegatingHandler` on the
already-registered typed client, layerable later **without changing the send code**, so deferral
incurs no rework.

**Alternatives considered**:
- *Wire Polly now* — contradicts the spec's acceptance tests and conflates two features; not simpler.
