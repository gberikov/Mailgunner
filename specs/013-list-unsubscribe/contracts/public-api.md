# Phase 1 Contract: Public API surface

The feature adds one public type and one public property. All members carry XML documentation. The
change is additive (SemVer **MINOR**); no existing signature changes.

## New type: `Mailgunner.ListUnsubscribeOptions`

```csharp
namespace Mailgunner;

/// <summary>
/// An opt-in declaration of how recipients unsubscribe from a send, emitted as RFC 8058 / RFC 2369
/// List-Unsubscribe headers. Attach via <see cref="MailgunSendOptions.ListUnsubscribe"/>.
/// </summary>
public sealed class ListUnsubscribeOptions
{
    /// <summary>
    /// Gets or sets the HTTPS unsubscribe endpoint. Required when <see cref="OneClick"/> is true.
    /// Must be an absolute https URI with no control characters or line breaks; emitted verbatim.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the unsubscribe email address (bare; the library forms the mailto: URI). Only the
    /// address is used; any display name is ignored. Validated by the EmailAddress rules.
    /// </summary>
    public EmailAddress? MailtoAddress { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether one-click unsubscribe is requested. When true, the send
    /// also emits List-Unsubscribe-Post: List-Unsubscribe=One-Click and requires a valid https Url.
    /// </summary>
    public bool OneClick { get; set; }
}
```

## New property: `Mailgunner.MailgunSendOptions.ListUnsubscribe`

```csharp
/// <summary>
/// Gets or sets the opt-in unsubscribe target emitted as List-Unsubscribe (and, when one-click,
/// List-Unsubscribe-Post). Null by default — no header is emitted and transactional sends are
/// unaffected. Setting this and also supplying a List-Unsubscribe/List-Unsubscribe-Post entry in
/// <see cref="CustomHeaders"/> is a conflict and throws when the request is built.
/// </summary>
public ListUnsubscribeOptions? ListUnsubscribe { get; set; }
```

## Behavioral contract

| # | Given | When request built | Then |
|---|-------|--------------------|------|
| C1 | `Url` set (https), `OneClick=false` | build | field `h:List-Unsubscribe` = `<URL>`; no `h:List-Unsubscribe-Post` |
| C2 | `Url` set (https), `OneClick=true` | build | `h:List-Unsubscribe` = `<URL>` **and** `h:List-Unsubscribe-Post` = `List-Unsubscribe=One-Click` |
| C3 | `MailtoAddress` set only | build | `h:List-Unsubscribe` = `<mailto:ADDR>`; no `-Post` |
| C4 | `Url`+`MailtoAddress` set, `OneClick=false` | build | `h:List-Unsubscribe` = `<URL>, <mailto:ADDR>` (URL first); no `-Post` |
| C5 | `ListUnsubscribe` null (default) | build | no `h:List-Unsubscribe*` fields; existing behavior unchanged |
| C6 | `OneClick=true`, no `Url` (mailto only or none) | build | throws `ArgumentException`; no request issued |
| C7 | `Url` scheme not https (e.g. http) | build | throws `ArgumentException`; no request issued |
| C8 | `Url` or mailto contains control char / CRLF | build/assign | throws `ArgumentException`; no request issued |
| C9 | target set but neither `Url` nor `MailtoAddress` present | build | throws `ArgumentException`; no request issued |
| C10 | `ListUnsubscribe` set **and** manual `List-Unsubscribe`/`-Post` in `CustomHeaders` (any casing) | build | throws `ArgumentException`; no request issued |
| C11 | one-click options on a batch send | build each chunk | every chunk carries the identical `h:List-Unsubscribe` + `h:List-Unsubscribe-Post` |

## Error contract

- Input/validation failures (C6–C10) throw `System.ArgumentException` (param `options`) before any HTTP
  request — no new exception type.
- `MailgunnerException` is unchanged and continues to surface only non-2xx HTTP responses.

## Versioning

- SemVer **MINOR** (additive). CHANGELOG `Unreleased` → `Added` entry required.
