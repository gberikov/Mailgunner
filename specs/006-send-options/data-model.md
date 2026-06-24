# Phase 1 Data Model: Send Enrichment Options

New and modified public types, their fields, validation rules, and the exact wire emission. All types are
data-only (no behavior beyond construction guards); emission lives in the internal
`MailgunOptionsContent`.

## New public types

### `MailgunSendOptions` (class, `namespace Mailgunner`)

Reusable container for the `o:`/`h:`/`v:` enrichments. Referenced (get-only, auto-initialized) by both
`MailgunMessage` and `MailgunBatchMessage`.

| Member | Type | Default | Emitted as | Notes |
|--------|------|---------|-----------|-------|
| `Tags` | `IList<string>` | empty list | `o:tag` (one part **per** entry, repeated) | Additive; not de-duplicated; entries are emitted in order. A blank/whitespace-only entry is **skipped** (not emitted). |
| `TestMode` | `bool` | `false` | `o:testmode = yes` | Field **omitted** when `false`. |
| `TrackingOpens` | `bool?` | `null` | `o:tracking-opens = yes\|no` | Omitted when `null`. |
| `TrackingClicks` | `ClickTracking?` | `null` | `o:tracking-clicks = yes\|no\|htmlonly` | Omitted when `null`. |
| `DeliveryTime` | `DateTimeOffset?` | `null` | `o:deliverytime = <RFC 2822 + numeric offset>` | Omitted when `null`. See formatting rule below. |
| `CustomHeaders` | `IDictionary<string,string>` | empty (ordinal) | `h:<name> = <value>` (one part per entry) | Name must be non-blank (else `ArgumentException` at emit). **Names are unique** (the dictionary key enforces this; a re-assigned name replaces its value). Emission order is immaterial. |
| `CustomVariables` | `IDictionary<string,string>` | empty (ordinal) | `v:<name> = <value>` (one part per entry) | String values emitted verbatim; name must be non-blank. **Names are unique** (dictionary key); emission order is immaterial. |

> **Why a dictionary for headers/variables**: `IDictionary<string,string>` makes names inherently unique
> — there is no "two headers with the same name" case to resolve — and matches how callers think about
> setting a named header/variable. Since the service treats each `h:`/`v:` field independently, the
> relative order the library emits them in carries no meaning, and tests assert presence (and absence),
> not ordering. (Default `Dictionary<,>` preserves insertion order in practice, but the contract does
> not rely on it.)

### `MailgunFile` (class, `namespace Mailgunner`)

A file to attach or embed.

| Member | Type | Required | Notes |
|--------|------|----------|-------|
| `FileName` | `string` | yes (non-blank) | Constructor throws `ArgumentException` when null/empty/whitespace (mirrors `EmailAddress`). |
| `Content` | `byte[]` | yes (non-null) | Constructor throws `ArgumentNullException` when null. May be empty (zero-length part is allowed). |
| `ContentType` | `string?` | no | When null/blank, the emitter uses `application/octet-stream`. |

Suggested constructor: `MailgunFile(string fileName, byte[] content, string? contentType = null)`.

### `ClickTracking` (enum, `namespace Mailgunner`)

| Member | Emitted value |
|--------|---------------|
| `Yes` | `yes` |
| `No` | `no` |
| `HtmlOnly` | `htmlonly` |

## Modified public types

### `MailgunMessage` (add members)

```csharp
public MailgunSendOptions Options { get; } = new();
public IList<MailgunFile> Attachments { get; } = new List<MailgunFile>();
public IList<MailgunFile> InlineFiles { get; } = new List<MailgunFile>();
```

### `MailgunBatchMessage` (add the same three members)

Identical declarations to `MailgunMessage`. Per FR-015, these enrichments are emitted on **every chunk**
of a batch send.

> Rationale for duplication over a base class: matches the existing `Template`/`TemplateVariables`
> duplication across these two types; keeps the public shape flat and predictable.

## Emission rules (internal `MailgunOptionsContent.Append`)

Signature:

```csharp
internal static void Append(
    MultipartFormDataContent content,
    MailgunSendOptions options,
    IEnumerable<MailgunFile> attachments,
    IEnumerable<MailgunFile> inlineFiles);
```

Called at the **end** of `MailgunMessageContent.Build` and `MailgunBatchContent.BuildChunk`. Appends, in
this deterministic order (order is immaterial to the service; fixed for stable assertions):

1. **Tags** — for each `options.Tags` entry: a `o:tag` string part with that value (repeated; not
   de-duplicated; blank entries are skipped).
2. **Test mode** — if `options.TestMode`: `o:testmode = yes`.
3. **Tracking opens** — if `options.TrackingOpens` is non-null: `o:tracking-opens = yes|no`.
4. **Tracking clicks** — if `options.TrackingClicks` is non-null: `o:tracking-clicks = yes|no|htmlonly`.
5. **Delivery time** — if `options.DeliveryTime` is non-null: `o:deliverytime = FormatRfc2822(value)`.
6. **Custom headers** — for each entry: `h:<name> = <value>` (throw `ArgumentException` on blank name).
7. **Custom variables** — for each entry: `v:<name> = <value>` (throw `ArgumentException` on blank name).
8. **Attachments** — for each `MailgunFile`: a `ByteArrayContent` part named `attachment`, with
   `filename` set from `FileName` and `Content-Type` set to `ContentType ?? application/octet-stream`.
9. **Inline files** — same as attachments but the part is named `inline`.

### RFC 2822 delivery-time formatting

```csharp
// Invariant culture → English day/month abbreviations; numeric offset, colon stripped.
static string FormatRfc2822(DateTimeOffset value)
{
    var body = value.ToString("ddd, dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture);
    var offset = value.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", "");
    return body + offset; // e.g. "Thu, 25 Jun 2026 14:00:00 +0000"
}
```

Result MUST match `^[A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2}:\d{2} [+-]\d{4}$` — numeric
offset, no colon, no named zone.

## Validation rules (client-side, before any request)

| Rule | Trigger | Exception |
|------|---------|-----------|
| Filename required | `MailgunFile` constructed with blank `FileName` | `ArgumentException` (in `MailgunFile` ctor) |
| Content required | `MailgunFile` constructed with null `Content` | `ArgumentNullException` (in `MailgunFile` ctor) |
| Header name required | A `CustomHeaders` entry has a blank name | `ArgumentException` at emit |
| Variable name required | A `CustomVariables` entry has a blank name | `ArgumentException` at emit |

No size validation (16KB cap is documented only — service-enforced). No change to existing message
validation (sender/recipient/body/template rules from 003–005 are untouched). `MailgunnerException`
remains reserved for actual HTTP responses.

## What is intentionally NOT modeled

- Streaming attachment content (v1 uses `byte[]`).
- Content-ID assignment for inline files (the caller references the file by its `FileName`/cid in their
  own HTML body; the library only emits the `inline` part).
- Client-side combined-size accounting (documented limit only).
- Per-recipient options in a batch (options/headers/variables/files are batch-wide and repeated per
  chunk; per-recipient values remain `BatchRecipient.Variables` from feature 005).
