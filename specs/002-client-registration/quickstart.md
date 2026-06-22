# Quickstart & Validation: Client Registration & Regional Bootstrap

A run/validation guide proving the feature works end-to-end, offline. For the public surface
see [contracts/registration-contract.md](./contracts/registration-contract.md); for types see
[data-model.md](./data-model.md).

## Prerequisites

- The pinned .NET SDK (`global.json`) restored.
- No Mailgun credentials required — everything here is offline.
- `Mailgunner.csproj` references `Microsoft.Extensions.Http` (centrally versioned).

## Consumer usage (the shipped experience)

A consuming application registers the client with a single call and resolves it:

```csharp
// Explicit settings:
services.AddMailgunner(domain: "mg.example.com", sendingKey: mySendingKey, region: MailgunRegion.Eu);

// …or bind from configuration/environment:
services.AddMailgunner(o =>
{
    o.Domain = config["Mailgun:Domain"]!;
    o.SendingKey = config["Mailgun:SendingKey"]!;
    o.Region = MailgunRegion.Us;
});

// Later, anywhere DI is available:
var client = provider.GetRequiredService<IMailgunnerClient>(); // ready to use
```

If `Domain`, `SendingKey`, or `Region` is missing/blank/undefined, the **host fails to start**
with an `OptionsValidationException` naming the offending setting — before any request is made.

## Validation scenarios (all offline, via fake transport)

Each maps to contract IDs and spec acceptance scenarios. Implement as xUnit tests under
`tests/Mailgunner.Tests/Registration/`.

| # | Scenario | Setup | Expected | Contract |
|---|----------|-------|----------|----------|
| 1 | Resolvable, ready client | Register valid settings, build provider | `GetRequiredService<IMailgunnerClient>()` returns non-null | C-01 |
| 2 | Stable repeated resolution | Resolve twice | Both usable | C-02 |
| 3 | EU routing | Region `Eu`, drive a request through the captured handler | Request host `api.eu.mailgun.net` | C-03 |
| 4 | US routing | Region `Us`, drive a request | Request host `api.mailgun.net` | C-04 |
| 5 | Basic auth | Any valid registration, drive a request | `Authorization: Basic base64("api:" + key)` | C-05 |
| 6 | Blank domain rejected | `Domain=""`/whitespace, trigger startup validation | `OptionsValidationException` naming domain | C-06 |
| 7 | Blank key rejected (secret-safe) | `SendingKey=" "`, trigger validation | `OptionsValidationException` naming the key; **key value absent** from message | C-07 |
| 8 | Undefined region rejected | `Region=(MailgunRegion)999`, trigger validation | `OptionsValidationException` naming region | C-08 |
| 9 | Last-call-wins | Call `AddMailgunner` twice (US then EU), build & drive a request | Routes to EU | C-09 |

### How tests inject the fake transport

```csharp
var fake = new CapturingHttpMessageHandler();           // records the request, returns 200
services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Eu)
        .ConfigurePrimaryHttpMessageHandler(() => fake); // returned IHttpClientBuilder
```

### How tests trigger startup validation (no host needed)

```csharp
var sp = services.BuildServiceProvider();
var validator = sp.GetRequiredService<IStartupValidator>(); // Microsoft.Extensions.Options
Assert.Throws<OptionsValidationException>(() => validator.Validate());
```

## Run

```bash
dotnet build
dotnet test         # all green, no network, no credentials
```

## Expected outcomes (Definition of Done for this feature)

- Valid settings → resolvable, ready `IMailgunnerClient` (SC-001).
- 100% EU/US requests hit the matching host (SC-002).
- 100% requests carry Basic auth derived from the sending key (SC-003).
- Every invalid-config case fails at startup naming the setting, before any request (SC-004).
- 100% behavior covered by offline tests with a fake transport (SC-005).
- The sending key never appears in any error/log/diagnostic (SC-006).
