# Phase 1 Data Model: Send a Single Email

The model is the small set of public types a consumer composes/receives, plus the internal builder
that turns a message into the wire request. Field names below are the planned public contract.

---

## Entity: `EmailAddress` (public readonly struct)

A single email address with an optional display name (clarification Q1).

| Member | Type | Notes |
|--------|------|-------|
| `Address` | `string` | The email address. Required, non-empty. |
| `DisplayName` | `string?` | Optional display name. |

- Constructor: `EmailAddress(string address, string? displayName = null)` — throws
  `ArgumentException` when `address` is null/empty/whitespace.
- `implicit operator EmailAddress(string)` — converts a bare address string.
- `ToString()` → `"DisplayName <Address>"` when a display name is set, else `Address`.
- Value semantics: `IEquatable<EmailAddress>`, `Equals`/`GetHashCode`/`==`/`!=` (satisfies CA1815).
- Namespace: `Mailgunner`.

---

## Entity: `MailgunMessage` (public class)

The email a consumer wants to send.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `From` | `EmailAddress` | Yes | The sender. A default/empty value fails validation. |
| `To` | `IList<EmailAddress>` | ≥1 across To/Cc/Bcc | Get-only, initialized to an empty list; callers `Add`. |
| `Cc` | `IList<EmailAddress>` | No | Get-only list. |
| `Bcc` | `IList<EmailAddress>` | No | Get-only list. |
| `Subject` | `string?` | No | Optional (the service permits an empty subject). |
| `Text` | `string?` | At least one of Text/Html | Plain-text body part. |
| `Html` | `string?` | At least one of Text/Html | HTML body part. |

- Get-only collection properties (satisfies CA2227); exposed as `IList<EmailAddress>` (satisfies CA1002).
- Namespace: `Mailgunner`.

**Validation rules (FR-002; thrown as `ArgumentException` before any request):**

| Condition | Result |
|-----------|--------|
| `From.Address` null/blank | `ArgumentException` — sender required |
| No recipients across To/Cc/Bcc | `ArgumentException` — at least one recipient required |
| Both `Text` and `Html` null/blank | `ArgumentException` — at least one body part required |
| `message` itself null | `ArgumentNullException` |

---

## Entity: `SendResult` (public class)

The success outcome of a send.

| Member | Type | Notes |
|--------|------|-------|
| `Id` | `string` | Mailgun's message id. |
| `Message` | `string` | Mailgun's accompanying status message (e.g., a queued acknowledgment). |

- Immutable: get-only properties set via constructor.
- Returned only when a 2xx body parses into both `id` and `message`.
- Namespace: `Mailgunner`.

---

## Entity: `MailgunnerException` (public sealed exception)

The single typed error for every failure to obtain a usable result.

| Member | Type | Notes |
|--------|------|-------|
| `StatusCode` | `int` | The HTTP status code of the response. |
| `ResponseBody` | `string` | The raw response body (never null; empty when the response had no body). |

- Constructed from `(int statusCode, string responseBody)`; `Message` is a generated summary.
- Raised on: any non-success (4xx/5xx) response (FR-006); a 2xx body that cannot be parsed
  (FR-006a); a non-success with an empty body still carries the status and an empty body (FR-011).
- Never carries the sending key (FR-010).
- CA1032 suppressed at the type (see research R6).
- Namespace: `Mailgunner`.

---

## Entity: `IMailgunnerClient.SendAsync` (new member on the existing interface)

```
Task<SendResult> SendAsync(MailgunMessage message, CancellationToken cancellationToken = default)
```

- Validates input (above), then issues the request and returns a `SendResult` or throws
  `MailgunnerException`. Honors `cancellationToken` (FR-008), surfacing
  `OperationCanceledException` on cancellation (not wrapped).

---

## Internal helper (not public surface)

| Type | Responsibility |
|------|----------------|
| `MailgunMessageContent` (`internal`, `Mailgunner.Internal`) | Validates a `MailgunMessage` and builds the `MultipartFormDataContent`: one `from`, one `to`/`cc`/`bcc` part per recipient (repeated, never comma-joined), and `subject`/`text`/`html` when present. |

---

## Derived request/response (observable contract)

| Attribute | Derivation |
|-----------|------------|
| Request method + path | `POST` `v3/{domain}/messages` relative to the region base URL (FR-003) |
| Request content type | `multipart/form-data` (FR-003, SC-006) |
| Recipient fields | One distinct field per recipient for each of `to`/`cc`/`bcc` (FR-004, SC-002) |
| Success result | `SendResult { Id, Message }` parsed from a 2xx `{ "id", "message" }` body (FR-005) |
| Error | `MailgunnerException { StatusCode, ResponseBody }` on non-2xx or unparseable-2xx (FR-006/006a) |

---

## State / lifecycle

No persistence and no state machine. The only flow is per call:

```
SendAsync(message, ct)
   ├─ validate input        → invalid → ArgumentException (no request)
   ├─ build multipart, POST → canceled → OperationCanceledException
   └─ on response:
        ├─ 2xx & body parses → SendResult(id, message)
        └─ non-2xx OR 2xx-unparseable → MailgunnerException(status, rawBody)
```
