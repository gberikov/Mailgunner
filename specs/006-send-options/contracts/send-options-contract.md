# Contract: Send Enrichment Options

Defines the public surface added by feature 006 and the observable HTTP contract the options add to each
request. These enrichments ride on the **existing** `SendAsync` (003/004) and `SendBatchAsync` (005)
calls — no new client method is introduced. Everything is verifiable offline against the fake transport.

## Public surface (additive)

```csharp
namespace Mailgunner;

public enum ClickTracking { Yes, No, HtmlOnly }

public sealed class MailgunFile
{
    public MailgunFile(string fileName, byte[] content, string? contentType = null);
    public string FileName { get; }       // required, non-blank
    public byte[] Content { get; }         // required, non-null
    public string? ContentType { get; }    // null/blank → application/octet-stream on the wire
}

public sealed class MailgunSendOptions
{
    public IList<string> Tags { get; }                          // → repeated o:tag
    public bool TestMode { get; set; }                          // true → o:testmode=yes
    public bool? TrackingOpens { get; set; }                    // → o:tracking-opens=yes|no
    public ClickTracking? TrackingClicks { get; set; }          // → o:tracking-clicks=yes|no|htmlonly
    public DateTimeOffset? DeliveryTime { get; set; }           // → o:deliverytime (RFC 2822 + numeric offset)
    public IDictionary<string, string> CustomHeaders { get; }   // → h:<name>
    public IDictionary<string, string> CustomVariables { get; } // → v:<name>
}

// Added to BOTH message types (same declarations):
public sealed class MailgunMessage      { /* ...003/004... */
    public MailgunSendOptions Options { get; }
    public IList<MailgunFile> Attachments { get; }
    public IList<MailgunFile> InlineFiles { get; }
}
public sealed class MailgunBatchMessage { /* ...005... */
    public MailgunSendOptions Options { get; }
    public IList<MailgunFile> Attachments { get; }
    public IList<MailgunFile> InlineFiles { get; }
}
```

No change to `IMailgunnerClient`, `SendResult`, `MailgunnerException`, `EmailAddress`, or `BatchRecipient`.

## Observable HTTP contract — parts the options add

Appended to each request's existing `multipart/form-data` body (after `from`/`to`/body/`template`/
`recipient-variables`). For a batch, the **same** parts appear on **every** chunk.

| Field / part | Cardinality | Value | Present when |
|--------------|-------------|-------|--------------|
| `o:tag` | 0..N (repeated) | each `Options.Tags` entry, verbatim, in order | one per non-blank tag |
| `o:testmode` | 0..1 | `yes` | `Options.TestMode == true` |
| `o:tracking-opens` | 0..1 | `yes` / `no` | `Options.TrackingOpens` non-null |
| `o:tracking-clicks` | 0..1 | `yes` / `no` / `htmlonly` | `Options.TrackingClicks` non-null |
| `o:deliverytime` | 0..1 | RFC 2822 with numeric offset, e.g. `Thu, 25 Jun 2026 14:00:00 +0000` | `Options.DeliveryTime` non-null |
| `h:<name>` | 0..N | each `Options.CustomHeaders` value | one per header entry |
| `v:<name>` | 0..N | each `Options.CustomVariables` value (string, verbatim) | one per variable entry |
| `attachment` (file part) | 0..N | file content; `Content-Disposition` `filename` = `FileName`; `Content-Type` = `ContentType ?? application/octet-stream` | one per `Attachments` entry |
| `inline` (file part) | 0..N | as `attachment` but field name `inline` | one per `InlineFiles` entry |

### File-part shape

```
Content-Disposition: form-data; name="attachment"; filename="ticket.pdf"
Content-Type: application/pdf

<bytes>
```

- An attachment with no `ContentType` carries `Content-Type: application/octet-stream`.
- An inline file is identical except `name="inline"`, so it is distinct from an attachment and can be
  referenced from the HTML body by content id.

### `o:deliverytime` format contract

- Matches `^[A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2}:\d{2} [+-]\d{4}$`.
- Offset is **numeric** (`+0000`, `+0300`), never a named zone (`UTC`, `EST`, `GMT`) and contains no
  colon. Rendered with `CultureInfo.InvariantCulture`.

## Behavioral contract

| Aspect | Contract |
|--------|----------|
| Optionality | Every enrichment is optional; a send supplying none produces a request identical to the pre-006 request for that send (no stray parts). |
| Composition | The same enrichments apply to single (003), templated (004), and batched (005) sends; on a batch they repeat identically on each chunk. |
| Tag multiplicity | Supplying the same/different tag values N times yields N `o:tag` parts; the library does not collapse or de-duplicate. A blank/whitespace tag entry is skipped. |
| Header/variable uniqueness | `CustomHeaders`/`CustomVariables` names are unique (dictionary key); re-assigning a name replaces its value (no duplicate `h:`/`v:` parts for the same name). The relative emission order of headers/variables is immaterial — assertions check presence/absence, not order. |
| Absent toggles | When a toggle/option is unset, its field is **absent** (account defaults apply). |
| `ArgumentException` (pre-request) | `MailgunFile` with blank `FileName`; a custom header or variable with a blank name. |
| `ArgumentNullException` | `MailgunFile` constructed with null `Content`. |
| Error contract | Unchanged: non-2xx (including a 16KB-over rejection) → `MailgunnerException(StatusCode, ResponseBody)`. |
| 16KB cap | Documented (README + this contract); **not** enforced client-side. |
| Secret safety | The sending key never appears in any field/part, `SendResult`, or `MailgunnerException`. |

## Documentation obligation

The README and this contract MUST state: *the combined size of the `o:` options, `h:` custom headers,
`v:` custom variables, and `t:` template parameters per request is capped at 16KB by Mailgun; exceeding
it causes the service to reject the request, surfaced as `MailgunnerException`.*
