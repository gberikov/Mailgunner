# Phase 0 Research: Send Enrichment Options

All four spec clarifications (Session 2026-06-24) are already resolved; this document records the
remaining design/technical decisions and the small amount of new mechanics (RFC 2822 offset formatting,
file-part construction, fake-transport capture). There are **no** open `NEEDS CLARIFICATION` items.

## Decision 1 — Where the options live (public shape)

**Decision**: Introduce one reusable, data-only `MailgunSendOptions` plus a `MailgunFile` type and a
`ClickTracking` enum. Expose them on **both** `MailgunMessage` and `MailgunBatchMessage` as
`Options` (get-only, auto-initialized), `Attachments`, and `InlineFiles` (get-only `IList<MailgunFile>`).

**Rationale**: The enrichments are identical no matter which send carries them (FR-001, FR-015). A single
options object is the DRY, minimal-surface way to model them and gives one emit site. Duplicating the
three members across the two message classes (rather than a shared base) **exactly mirrors the existing
codebase**, where `Template`, `TemplateVersion`, `GenerateTextFromTemplate`, and `TemplateVariables` are
already duplicated between `MailgunMessage` and `MailgunBatchMessage`. Get-only auto-initialized
properties (like the existing `TemplateVariables`/`Recipients`) remove null-handling and let callers
write `message.Options.Tags.Add("welcome")` directly.

**Alternatives considered**:
- *Flat properties on each message*: rejected — duplicates ~7 members twice and scatters the emit logic.
- *Shared base class for the two messages*: rejected — introduces an inheritance hierarchy the library
  has so far avoided; changes the predictable flat public shape; no behavioral benefit.
- *Attachments inside `MailgunSendOptions`*: rejected for clarity — attachments/inline are file **parts**,
  not `o:`/`h:`/`v:` options, so they sit beside `Options` as their own collections; the internal emitter
  still handles all of them in one place.

## Decision 2 — Tracking representation (tri-state for clicks)

**Decision**: `TrackingOpens` is `bool?` (null = omit, true = `yes`, false = `no`). `TrackingClicks` is
`ClickTracking?` where `ClickTracking { Yes, No, HtmlOnly }` (null = omit). Emitted values:
`o:tracking-opens = yes|no`, `o:tracking-clicks = yes|no|htmlonly`.

**Rationale**: Confirmed by clarification — open tracking is on/off, click tracking additionally supports
`htmlonly`. Nullable types give a clean three-way (omit / on / off) without a sentinel, and FR-008
requires the field to be **absent** when unset so account defaults apply.

**Alternatives considered**: a shared `bool?` for both (rejected — cannot express `htmlonly`); a single
enum for both with a `Default` member (rejected — `bool?` is the idiomatic on/off/omit for opens).

## Decision 3 — Scheduled delivery time: RFC 2822 with a numeric offset

**Decision**: `DeliveryTime` is `DateTimeOffset?`. When set, emit `o:deliverytime` formatted as
RFC 2822 with a **numeric** offset and no colon, e.g. `Thu, 25 Jun 2026 14:00:00 +0000`. Build it with the
invariant-culture pattern `ddd, dd MMM yyyy HH:mm:ss` followed by the offset rendered as `+HHmm`
(strip the colon that .NET's `zzz` specifier emits).

**Rationale**: FR-010 demands exactly RFC 2822 with a numeric offset (`+0000`), never a named zone.
A `DateTimeOffset` inherently carries a numeric offset, so no zone-name ambiguity can arise (clarified:
the caller supplies a point-in-time value; the library formats it). `CultureInfo.InvariantCulture`
guarantees English day/month abbreviations regardless of the host locale. The colon strip is the single
formatting subtlety: `DateTimeOffset.ToString("…zzz")` yields `+03:00`, but Mailgun's RFC 2822 examples
use `+0000`/`+0300`.

**Alternatives considered**:
- *Accept a pre-formatted string from the caller*: rejected — pushes the format constraint onto every
  caller and makes "named zone" mistakes possible (the very thing FR-010 forbids).
- *`DateTime` instead of `DateTimeOffset`*: rejected — `DateTime` has no reliable offset; `Local`/
  `Unspecified` kinds would force guessing, risking a wrong instant.
- *`"R"` (RFC1123) format*: rejected — emits `GMT` (a named zone), violating FR-010.

**Verification approach**: assert the emitted value matches a regex like
`^[A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2}:\d{2} [+-]\d{4}$` and that it contains no `:` in
the offset and no alphabetic zone token; cover `+0000` and a non-UTC offset (`+0300`).

## Decision 4 — File parts (attachments & inline files)

**Decision**: `MailgunFile` holds `FileName` (required, non-blank — guarded in the constructor like
`EmailAddress`), `Content` (`byte[]`), and `ContentType` (optional `string?`). Emit each attachment as a
file part named `attachment` and each inline file as a file part named `inline`, using
`MultipartFormDataContent.Add(byteContent, name, fileName)` with
`byteContent.Headers.ContentType = new MediaTypeHeaderValue(ContentType ?? "application/octet-stream")`.

**Rationale**: A file part must carry a filename and a content type (acceptance criterion / FR-002).
`ByteArrayContent` + the three-arg `Add` overload produces a `Content-Disposition: form-data;
name="attachment"; filename="…"` plus an explicit `Content-Type`, exactly what the criterion checks.
`byte[]` is the simplest representation that works on both `net8.0` and `netstandard2.0` and avoids
stream-lifetime/disposal hazards for transactional-sized attachments. Inline files use the distinct
`inline` field (FR-003) so they are embeddable (referenceable from HTML by content id) while attachments
are downloadable.

**Default content type**: `application/octet-stream` when omitted (clarified) — no filename-extension
sniffing, keeping the library dependency-light and behavior deterministic.

**Alternatives considered**:
- *`Stream` content*: rejected for v1 — adds ownership/disposal and re-read complexity; not needed for
  typical ticket-PDF/logo attachments.
- *Infer content type from extension*: rejected by clarification (Decision: always default to
  `application/octet-stream`).

## Decision 5 — Custom variables are string-valued

**Decision**: `CustomVariables` is `IDictionary<string,string>`; each entry is emitted verbatim as
`v:<name> = <value>` (no JSON serialization). `CustomHeaders` is likewise `IDictionary<string,string>`
emitted as `h:<name> = <value>`.

**Rationale**: Clarified — `v:` fields are string-valued form fields; a string→string contract is the
faithful, trivially assertable mapping. Callers needing structured data JSON-encode it into the string
themselves. This also keeps the `v:` path off the reflection-based JSON serializer (no new
trimming/AOT considerations).

**Alternatives considered**: `IDictionary<string,object?>` serialized to JSON (rejected by clarification —
over-broad and inconsistent with the service's string-valued `v:` fields).

## Decision 6 — 16KB combined cap: document only

**Decision**: Do **not** compute or enforce the combined `o:`/`h:`/`v:`/`t:` size client-side. State the
16KB limit in the README and the contract. If exceeded, the service rejects the request and the existing
single `MailgunnerException(StatusCode, ResponseBody)` surfaces unchanged.

**Rationale**: Clarified — the external constraint is literally "must be documented." Client-side size
accounting would duplicate a check the service performs reliably, add fragile byte-counting logic, and
grow the surface for no correctness gain (constitution Principle I).

**Alternatives considered**: client-side `ArgumentException` pre-validation; best-effort warning — both
rejected by clarification.

## Decision 7 — Emission site & ordering

**Decision**: Centralize all enrichment emission in a new internal
`MailgunOptionsContent.Append(MultipartFormDataContent content, MailgunSendOptions options,
IEnumerable<MailgunFile> attachments, IEnumerable<MailgunFile> inlineFiles)`. Call it at the **end** of
`MailgunMessageContent.Build` and at the end of `MailgunBatchContent.BuildChunk` (so every chunk carries
the same enrichments — FR-015). Multipart part order is irrelevant to the service; parts are appended
deterministically (options, then headers, then variables, then attachments, then inline files) for
stable test assertions.

**Rationale**: One emit site keeps the two builders thin and guarantees single/templated/batch sends
behave identically. Appending at the end leaves all existing field construction (recipients, body,
template, recipient-variables) byte-for-byte unchanged, so a send with no options is identical to the
pre-006 request (regression safety).

**Alternatives considered**: duplicating emit code in each builder (rejected — drift risk, violates DRY).

## Decision 8 — Test fake extension (capturing file metadata)

**Decision**: Extend `FormField` to `record struct FormField(string Name, string Value, string? FileName,
string? ContentType)` and capture, per multipart part, `part.Headers.ContentDisposition?.FileName`
(unquoted) and `part.Headers.ContentType?.MediaType`. `CapturedRequest` gains helpers to find file parts
by field name. All existing members (`Values`, `Value`, `Count`, `LastFormData`, `Requests`,
`ResponseSelector`, `OnSend`) are preserved; the single positional construction site in the handler is
updated.

**Rationale**: FR-016 requires asserting filename and content type on file parts offline. The existing
fake only kept name+value, which cannot verify FR-002/FR-003. The change is additive: string parts simply
have `null` filename/content type.

**Alternatives considered**: a parallel capture list for files only (rejected — two code paths to keep in
sync; one unified `FormField` with optional metadata is simpler and lets a test treat every part
uniformly).

## Summary of resolved unknowns

| Topic | Resolution |
|-------|-----------|
| Options public shape | One `MailgunSendOptions` + `MailgunFile` + `ClickTracking`; exposed on both message types as `Options`/`Attachments`/`InlineFiles` |
| Tracking | opens `bool?`; clicks `ClickTracking?` (Yes/No/HtmlOnly); omitted when null |
| Scheduled time | `DateTimeOffset?` → RFC 2822 with numeric offset, colon stripped, invariant culture |
| File parts | `byte[]` content; `attachment`/`inline` fields; content type defaults to `application/octet-stream` |
| Custom variables/headers | `IDictionary<string,string>`, emitted verbatim under `v:`/`h:` |
| 16KB cap | Documented only; no client-side enforcement |
| Emit site | Internal `MailgunOptionsContent.Append`, called by both builders; appended last |
| Fake transport | `FormField` additively captures filename + content type per part |
