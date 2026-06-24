# Quickstart & Validation: Named Mailgunner Clients

Validates the feature end-to-end, entirely offline. See [public-api.md](./contracts/public-api.md) for the exact surface and [data-model.md](./data-model.md) for the constructs.

## Prerequisites

- .NET SDK (repo targets `net8.0` + `netstandard2.0`).
- From repo root: `dotnet build` then `dotnet test` (must be green with no Mailgun credentials present — Principle III).

## Consumer usage (illustrative)

```csharp
// Register two isolated identities in one container.
services.AddMailgunner("transactional", "mg.example.com", txKey, MailgunRegion.Us);
services.AddMailgunner("marketing", opts =>
{
    opts.Domain = "news.example.com";
    opts.SendingKey = mktKey;
    opts.Region = MailgunRegion.Eu;
    opts.Retry.MaxRetryAttempts = 5;
});
// Or bind a named client from configuration:
services.AddMailgunner("audit", configuration.GetSection("Mailgun:Audit"));

// Resolve by name at the point of sending.
var factory = provider.GetRequiredService<IMailgunnerClientFactory>();
IMailgunnerClient tx = factory.Get("transactional");
await tx.SendAsync(message, ct);
```

## Validation scenarios (network-free xUnit)

Attach a fake `HttpMessageHandler` per name through the returned `IHttpClientBuilder`
(`.ConfigurePrimaryHttpMessageHandler(() => fake)`), build the provider, resolve via the factory, and assert on the captured request.

| # | Scenario (spec ref) | Setup | Expected |
|---|---------------------|-------|----------|
| 1 | Register & resolve two names (US1, US2, SC-001) | Register "transactional" (US) and "marketing" (EU) | Both `factory.Get(...)` return ready clients exposing send + suppressions |
| 2 | Routing + auth isolation (US3, SC-002/SC-003) | Drive a send from each via its fake handler | Each request hits its own host (`api.mailgun.net` vs `api.eu.mailgun.net`) + its own domain path; `Authorization` = `Basic base64("api:" + thatKey)` |
| 3 | No cross-contamination (US3, SC-003) | Two names, different region/key/retry | Neither name's host/domain/auth/retry appears on the other's request |
| 4 | Retry isolation (US3 scenario 2) | Name A: `MaxRetryAttempts=0`; Name B: `>0` with a retryable status from the fake | A issues 1 attempt; B retries per its own budget |
| 5 | Backward compatibility (US4, SC-004) | Unnamed `AddMailgunner(...)` + a named one | Unnamed `IMailgunnerClient` resolves and behaves as before; named coexists |
| 6 | Only named, bare injection fails (US4 sc.3, FR-022) | Only named registrations | Resolving bare `IMailgunnerClient` throws the standard DI error; `factory.Get(name)` works |
| 7 | Blank name rejected (US5 sc.2, FR-011) | `AddMailgunner("", ...)` / whitespace | `ArgumentException` at registration naming the name |
| 8 | Duplicate name rejected (US5 sc.1, FR-012) | Two `AddMailgunner("dup", ...)` | `ArgumentException` at registration naming the duplicate |
| 9 | Invalid per-name settings (US5 sc.3, FR-013) | Named client with blank domain/key or undefined region | `OptionsValidationException` at `ValidateOnStart`; message identifies name+setting; no key value |
| 10 | Unknown name lookup (US5 sc.4, FR-014) | `factory.Get("nope")` | `ArgumentException` naming the unknown name; lists registered names; not null/default |
| 11 | Secret hygiene (US5 sc.5, SC-007) | Trigger each error above | No emitted message/log contains any sending key |
| 12 | Config-section binding (FR-021) | Bind a named client from an in-memory `IConfiguration` section | Resolved client targets the bound domain/region with bound auth |

## Done / expected outcome

- `dotnet build` succeeds for both target frameworks with warnings-as-errors.
- `dotnet test` is green offline; new tests in `tests/Mailgunner.Tests/Registration/` cover scenarios 1–12.
- CHANGELOG has an `Added` entry for named clients (SemVer MINOR).
