# Quickstart & Validation: One-Click List-Unsubscribe

This guide shows how to use the feature and how to validate it offline. It references
[contracts/public-api.md](./contracts/public-api.md) and [data-model.md](./data-model.md) for the exact
surface and rules.

## Prerequisites

- Repo builds and tests green: `dotnet build` and `dotnet test` from the repo root.
- No Mailgun credentials needed — all validation is network-free via the test fakes.

## Usage (consumer perspective)

One-click marketing blast:

```csharp
var message = new MailgunMessage
{
    From = "news@mg.example.com",
    Subject = "Big announcement",
    Html = "<h1>Hello</h1>",
};
message.To.Add("subscriber@example.com");

message.Options.ListUnsubscribe = new ListUnsubscribeOptions
{
    Url = "https://example.com/unsubscribe?id=abc123",
    MailtoAddress = "unsubscribe@mg.example.com", // optional second target
    OneClick = true,
};

await client.SendAsync(message);
```

This emits:

```
List-Unsubscribe: <https://example.com/unsubscribe?id=abc123>, <mailto:unsubscribe@mg.example.com>
List-Unsubscribe-Post: List-Unsubscribe=One-Click
```

mailto-only, non-one-click:

```csharp
message.Options.ListUnsubscribe = new ListUnsubscribeOptions
{
    MailtoAddress = "unsubscribe@mg.example.com",
};
// → List-Unsubscribe: <mailto:unsubscribe@mg.example.com>   (no List-Unsubscribe-Post)
```

Transactional mail (default): leave `Options.ListUnsubscribe` unset → no headers emitted.

> Note: the emitted `h:List-Unsubscribe`/`h:List-Unsubscribe-Post` fields count toward Mailgun's combined
> 16 KB `o:`/`h:`/`v:`/`t:` cap. The library does not enforce this cap (consistent with the other
> options); an oversize request is rejected by the service and surfaced as a `MailgunnerException`.

## Validation scenarios (offline tests)

Run `dotnet test`. The new `ListUnsubscribeTests` must cover, asserting the exact emitted field
value(s) via the capturing/stub handler (see existing `CustomHeadersVariablesTests` for the pattern):

| Scenario | Expected |
|----------|----------|
| URL-only, one-click | `h:List-Unsubscribe` = `<https://…>`; `h:List-Unsubscribe-Post` = `List-Unsubscribe=One-Click` |
| URL-only, not one-click | `h:List-Unsubscribe` = `<https://…>`; no `-Post` field |
| mailto-only | `h:List-Unsubscribe` = `<mailto:…>`; no `-Post` field |
| both targets | `h:List-Unsubscribe` = `<https://…>, <mailto:…>` (URL first) |
| feature unset | no `h:List-Unsubscribe*` field present (regression guard) |
| one-click without URL | `ArgumentException`, `stub.Requests` empty |
| non-https URL (`http://…`) | `ArgumentException`, no request |
| control char / CRLF in URL | `ArgumentException`, no request |
| empty target (no URL, no mailto) | `ArgumentException`, no request |
| manual `List-Unsubscribe` in CustomHeaders + typed target (any casing) | `ArgumentException`, no request |
| batch send with one-click options | every chunk carries both headers |

## Definition of done

- All scenarios above pass network-free.
- `dotnet build` and `dotnet test` green with no new warnings (warnings-as-errors).
- XML docs present on `ListUnsubscribeOptions` and the new property.
- CHANGELOG `Unreleased` → `Added` entry describing the typed List-Unsubscribe support.
