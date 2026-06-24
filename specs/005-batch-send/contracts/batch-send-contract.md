# Contract: Batch Send (Personalized Mass Send)

Defines the public surface added by feature 005 and the observable HTTP contract of each chunk request.
Everything is verifiable offline against the fake transport.

## Public surface (additive)

```csharp
namespace Mailgunner;

public sealed class MailgunBatchMessage
{
    public EmailAddress From { get; set; }
    public string? Subject { get; set; }
    public string? Template { get; set; }                 // required
    public string? TemplateVersion { get; set; }
    public bool GenerateTextFromTemplate { get; set; }
    public IDictionary<string, object?> TemplateVariables { get; } // global; → t:variables
    public IList<BatchRecipient> Recipients { get; }
}

public sealed class BatchRecipient
{
    public BatchRecipient(EmailAddress address);
    public EmailAddress Address { get; }
    public IDictionary<string, object?> Variables { get; }         // → this recipient's recipient-variables entry
}

public interface IMailgunnerClient
{
    // ... existing SendAsync(MailgunMessage, CancellationToken) ...

    Task<IReadOnlyList<SendResult>> SendBatchAsync(
        MailgunBatchMessage message,
        CancellationToken cancellationToken = default);
}
```

### `SendBatchAsync` behavioral contract

| Aspect | Contract |
|--------|----------|
| Requests issued | `ceil(Recipients.Count / 1000)`, sequential, in recipient order. |
| Split example | 2500 recipients → 3 requests of 1000, 1000, 500 (in order). |
| Exact-multiple | 1000 → 1 request; 2000 → 2 requests; no trailing empty request. |
| Empty list | Zero requests; returns an empty `IReadOnlyList<SendResult>`; no exception. |
| Success return | One `SendResult` per chunk sent, in chunk order. |
| Endpoint / method / content | Each request: `POST v3/{domain}/messages`, `multipart/form-data` (reuses 002 base URL + Basic auth). |
| Failure (any chunk non-2xx) | Throws `MailgunnerException(StatusCode, ResponseBody)` immediately; no further chunks sent (fail-fast). Prior chunks remain sent. |
| Unparseable 2xx body | Throws `MailgunnerException` (same single-error path as single send). |
| `ArgumentNullException` | `message` is null. |
| `ArgumentException` (pre-request) | Missing `From`; missing/blank `Template`; duplicate recipient `Address`. |
| Cancellation | `CancellationToken` honored between and during chunks; once observed, no further chunks are issued (`OperationCanceledException`). |
| Secret safety | The sending key never appears in any request field, any `SendResult`, or any `MailgunnerException`. |

## Observable HTTP contract — per chunk

For a chunk containing recipients `r1..rn` (n ≤ 1000):

| Field | Cardinality | Value |
|-------|-------------|-------|
| `from` | 1 | `From.ToString()` |
| `to` | n (repeated, never comma-joined) | `ri.Address.ToString()` — one part per recipient in the chunk, in order |
| `subject` | 0..1 | present iff `Subject` non-null |
| `template` | 1 | `Template` (same value every chunk) |
| `t:version` | 0..1 | present iff `TemplateVersion` non-blank (same every chunk) |
| `t:text` | 0..1 | `yes` iff `GenerateTextFromTemplate` (same every chunk) |
| `t:variables` | 0..1 | one JSON object of the **global** `TemplateVariables`; omitted when that map is empty (same every chunk) |
| `recipient-variables` | 1 | one JSON object keyed by each recipient's **bare address**, value = that recipient's `Variables` (empty `Variables` → `{}`) |

### `recipient-variables` JSON shape

```json
{
  "alice@example.com": { "name": "Alice", "ticket": 1024, "link": "https://e/x/alice" },
  "bob@example.com":   { "name": "Bob",   "ticket": 1025, "link": "https://e/x/bob" }
}
```

- Top-level value kind is `Object`; one property per recipient in the chunk; property name equals the
  recipient's bare `Address`.
- Each property value is an object holding exactly that recipient's variables (string→string,
  int→number, bool→bool, nested object/array preserved — same `System.Text.Json` rules as 004's
  `t:variables`).
- A recipient with no variables appears as `"addr": {}`.

## Out of scope (asserted absent)

No `o:` options, `h:` headers, `v:` custom variables, attachments/inline files, suppressions, or
webhooks are emitted by this feature. Address-privacy (each recipient seeing only their own address) is
delivered by Mailgun in response to the `to` + `recipient-variables` shape above; it is not client-side
filtering and is not asserted at the transport.
