# Contract: Templated Send — public surface & observable HTTP

This feature adds no new public type and no new method signature. It extends the existing send
contract (feature 003) with template inputs on `MailgunMessage` and the corresponding observable
HTTP fields.

## Public surface (additions to `Mailgunner.MailgunMessage`)

```csharp
namespace Mailgunner;

public sealed class MailgunMessage
{
    // ... existing 003 members: From, To, Cc, Bcc, Subject, Text, Html ...

    /// <summary>Name of the server-side stored template to render. When set, an inline
    /// Text/Html body must not also be supplied.</summary>
    public string? Template { get; set; }

    /// <summary>Optional template version to pin. When omitted, the active version is used.</summary>
    public string? TemplateVersion { get; set; }

    /// <summary>When true, requests a generated plain-text part from the template.</summary>
    public bool GenerateTextFromTemplate { get; set; }

    /// <summary>Global variables applied to the whole send, serialized once into the
    /// template-variables payload. Values may be any JSON-representable type.</summary>
    public IDictionary<string, object?> TemplateVariables { get; }
}
```

`IMailgunnerClient.SendAsync(MailgunMessage, CancellationToken)` is **unchanged** in signature and
return/exception contract.

## Behavioral contract

| # | Given | When | Then |
|---|-------|------|------|
| C1 | A message with `Template` set, a sender, and ≥1 recipient | sent, service returns 2xx with `{id,message}` | request carries a `template` field with the name; caller gets `SendResult(Id, Message)` |
| C2 | A templated message with `TemplateVariables` populated | sent | exactly **one** `t:variables` field whose value parses as a JSON object keyed by variable name, with value kinds preserved |
| C3 | A templated message with **no** / **empty** variables | sent | **no** `t:variables` field; `template` still present |
| C4 | A templated message with `TemplateVersion` set | sent | `t:version` field equals the supplied version |
| C5 | A templated message with no (or blank) version | sent | **no** `t:version` field |
| C6 | A templated message with `GenerateTextFromTemplate = true` | sent | `t:text` field equals `yes` |
| C7 | A templated message with `GenerateTextFromTemplate = false` | sent | **no** `t:text` field |
| C8 | A plain message (Text/Html, no Template) | sent | request carries body parts and **none** of `template`/`t:version`/`t:text`/`t:variables` |
| C9 | A message with **both** `Template` and `Text`/`Html` | sent | `ArgumentException` before any request (no HTTP call) |
| C10 | A message with template data (variables/version/text) but **no** `Template` name | sent | `ArgumentException` before any request |
| C11 | A templated message with neither body nor recipient/sender | sent | `ArgumentException` before any request (existing 003 rules still apply) |
| C12 | A templated send | always | targets `v3/{domain}/messages` via `multipart/form-data`; non-2xx (or unparseable 2xx) → `MailgunnerException(status, body)`; cancellation honored — identical to 003 |

## Observable HTTP (unchanged transport, extended fields)

- Method/URL/content type: `POST {regionBase}/v3/{domain}/messages`, `multipart/form-data` — unchanged.
- New fields appended after the existing `from`/`to`/`cc`/`bcc`/`subject`/`text`/`html` parts:
  `template`, `t:version`, `t:text`, `t:variables` — each emitted only under the conditions in
  [data-model.md](../data-model.md#wire-mapping-emitted-by-mailgunmessagecontentbuild-into-multipartform-data).
- Secret hygiene: the sending key never appears in any field, the result, or the error (Principle V).

## Out of scope (explicitly not in this contract)

Per-recipient `recipient-variables` and batch sending, attachments/inline files, sending options
(`o:*`), custom headers (`h:X-*`), custom variables (`v:*`). These are later features.
