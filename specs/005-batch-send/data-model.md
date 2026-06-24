# Phase 1 Data Model: Personalized Mass Send (Batched Recipient Variables)

All new types live in `src/Mailgunner/`. No persistence — these are in-memory request models plus the
existing `SendResult` reused as the per-chunk outcome.

## New public type: `MailgunBatchMessage`

One developer-issued batch: a template-based message plus an ordered recipient list with per-recipient
variables.

| Member | Type | Notes |
|--------|------|-------|
| `From` | `EmailAddress` (get/set) | Required sender. |
| `Subject` | `string?` (get/set) | Optional subject; emitted as `subject` when non-null. |
| `Template` | `string?` (get/set) | **Required** stored-template name; emitted as `template`. |
| `TemplateVersion` | `string?` (get/set) | Optional pinned version; emitted as `t:version` when non-blank (reuses 004 rule). |
| `GenerateTextFromTemplate` | `bool` (get/set) | When `true`, emit `t:text=yes`; else omit (reuses 004 rule). |
| `TemplateVariables` | `IDictionary<string, object?>` (get, init non-null) | **Global** variables shared by all recipients; serialized once into `t:variables`, omitted when empty (reuses 004 rule). |
| `Recipients` | `IList<BatchRecipient>` (get, init non-null) | Ordered recipient list; each appears in exactly one chunk. |

## New public type: `BatchRecipient`

One recipient and that recipient's own personalization values.

| Member | Type | Notes |
|--------|------|-------|
| ctor `BatchRecipient(EmailAddress address)` | — | Address required (non-empty enforced by `EmailAddress`). |
| `Address` | `EmailAddress` (get) | The recipient. The **bare `Address`** is the `recipient-variables` key; `ToString()` is the repeated `to` value. |
| `Variables` | `IDictionary<string, object?>` (get, init non-null) | This recipient's named values (e.g. `name`, `ticket`, `link`); becomes this recipient's entry in `recipient-variables`. May be empty → serializes to `{}`. |

A bare address string converts implicitly to `EmailAddress`, so callers may write
`new BatchRecipient("alice@example.com")`.

## Reused type: `SendResult`

Unchanged (id + message). `SendBatchAsync` returns `IReadOnlyList<SendResult>` — one per chunk actually
sent; empty list when there are no recipients.

## Validation rules (enforced before any request — `ArgumentException`/`ArgumentNullException`)

Performed by `MailgunBatchContent` (or a `Validate` entry it calls) at the start of `SendBatchAsync`:

1. `message` is `null` → `ArgumentNullException`.
2. `From.Address` missing/blank → `ArgumentException` ("A sender (From) is required.").
3. `Template` missing/blank → `ArgumentException` ("A batch send requires a Template name.").
4. Any duplicate recipient `Address` (ordinal) anywhere in `Recipients` → `ArgumentException`
   ("Duplicate recipient address: '{address}'.") — thrown before any request (FR-014).
5. Empty `Recipients` is **valid** and is a no-op (zero requests, empty result list) — *not* an error
   (FR-004).

Per-recipient `Variables` being empty is valid (entry is `{}`). Global `TemplateVariables` being empty
is valid (the `t:variables` field is omitted, per 004).

## Chunking rule

- Constant `MaxRecipientsPerRequest = 1000` (internal; not configurable).
- `Chunk(Recipients, 1000)` yields consecutive, order-preserving slices: chunk *k* = recipients
  `[k·1000, min((k+1)·1000, N))`.
- Request count = `ceil(N / 1000)`: `0→0`, `1000→1`, `2000→2`, `2500→3` (1000/1000/500).

## State / lifecycle

None. A `MailgunBatchMessage` is built, validated, partitioned, and each chunk is rendered to a fresh
`MultipartFormDataContent` and POSTed sequentially. No object is mutated by sending.

## Per-chunk wire mapping (see contract for full detail)

For chunk *c* with recipients `R_c`:
- `from` ← `From.ToString()`
- one `to` part per `r ∈ R_c` ← `r.Address.ToString()`
- `subject` (if set), `template`, `t:version`/`t:text`/`t:variables` (global, same for every chunk)
- one `recipient-variables` part ← `JsonSerializer.Serialize` of `{ r.Address.Address : r.Variables }`
  for every `r ∈ R_c`.
