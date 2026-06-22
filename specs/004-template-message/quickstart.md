# Quickstart: Send a Templated Email

A validation/run guide for the templated-send feature. All scenarios run **offline** — no Mailgun
credentials and no network. Implementation details live in the spec, [data-model.md](./data-model.md),
and [contracts/template-send-contract.md](./contracts/template-send-contract.md); task breakdown
comes from `/speckit-tasks`.

## Prerequisites

- .NET SDK pinned by `global.json`.
- Features 002 (registration) and 003 (single send) already implemented on this branch.

## Build & test

```bash
dotnet build
dotnet test
```

Expected: build succeeds with warnings-as-errors clean; all tests pass — the **new template tests**
**and** the **existing 003 send tests** (the latter prove plain sending is unchanged, FR-010/SC-005).

## Using the feature (illustrative consumer code)

```csharp
var message = new MailgunMessage
{
    From = new EmailAddress("noreply@mg.example.com", "Example"),
    Subject = "Welcome",
    Template = "welcome",            // render from the stored template instead of a body
    TemplateVersion = "v2",          // optional: pin a version
    GenerateTextFromTemplate = true, // optional: ask for a generated text part
};
message.To.Add("alice@example.com");
message.TemplateVariables["product"] = "Acme";
message.TemplateVariables["seats"] = 5;          // non-string values are preserved as JSON
message.TemplateVariables["owner"] = new { name = "Alice" };

SendResult result = await client.SendAsync(message);
// result.Id, result.Message — same result shape as a plain send
```

A plain (inline-body) send from feature 003 keeps working with no change:

```csharp
var plain = new MailgunMessage { From = ..., Subject = "Hi", Text = "Hello" };
plain.To.Add("bob@example.com");
await client.SendAsync(plain); // carries text/html only; no template fields
```

## Validation scenarios (offline, via the fake transport)

Each maps to a contract row in [template-send-contract.md](./contracts/template-send-contract.md)
and is asserted by inspecting the `StubHttpMessageHandler.LastFormData` fields (and parsing the
`t:variables` value with `JsonDocument`).

| Scenario | Setup | Expected observation |
|----------|-------|----------------------|
| Templated success (C1) | `Template` set, stub returns 2xx `{id,message}` | a `template` field present; `SendResult` exposes id + message |
| Variables payload shape (C2) | populate `TemplateVariables` with string/number/nested | exactly one `t:variables` field; its value parses to a JSON object with matching keys and value kinds |
| No / empty variables (C3) | leave `TemplateVariables` empty | no `t:variables` field; `template` still present |
| Version pin (C4/C5) | set / omit `TemplateVersion` | `t:version` present with value / absent when omitted or blank |
| Generated text (C6/C7) | set `GenerateTextFromTemplate` true / false | `t:text` equals `yes` / field absent |
| Plain regression (C8) | a plain message (Text only) | body parts present; none of `template`/`t:version`/`t:text`/`t:variables` present |
| Both template + body (C9) | set `Template` **and** `Text` | `ArgumentException` thrown; stub never receives a request |
| Template data without name (C10) | set `TemplateVariables`/version/text, leave `Template` null | `ArgumentException` thrown; no request |
| Transport parity (C12) | non-2xx, unparseable 2xx, canceled token | `MailgunnerException(status, body)` / cancellation — identical to 003 |

## Success signals

- New template tests green; all 003 tests still green (coexistence proven).
- The captured `t:variables` value is always valid JSON of the expected shape when variables are
  supplied (SC-002), and absent otherwise.
- No sending key appears in any captured field, result, or error (SC-008).
