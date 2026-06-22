# Quickstart & Validation: Send a Single Email

A run/validation guide proving the feature works end-to-end, offline. For the public surface see
[contracts/send-contract.md](./contracts/send-contract.md); for types see [data-model.md](./data-model.md).

## Prerequisites

- The pinned .NET SDK (`global.json`) restored.
- Feature 002 (client registration) in place — the registered client provides the region base URL
  and HTTP Basic auth this feature reuses.
- `Mailgunner.csproj` references `System.Text.Json` (centrally versioned).
- No Mailgun credentials required — everything here is offline.

## Consumer usage (the shipped experience)

```csharp
var client = serviceProvider.GetRequiredService<IMailgunnerClient>();

var message = new MailgunMessage
{
    From = new EmailAddress("noreply@mg.example.com", "Example"),
    Subject = "Hello",
    Text = "Hi there!",
    Html = "<p>Hi there!</p>",
};
message.To.Add("alice@example.com");                 // implicit string -> EmailAddress
message.To.Add(new EmailAddress("bob@example.com", "Bob"));
message.Cc.Add("carol@example.com");

SendResult result = await client.SendAsync(message, cancellationToken);
// result.Id, result.Message
```

On a non-success response (or a 2xx body that can't be parsed), `SendAsync` throws
`MailgunnerException` exposing `StatusCode` and `ResponseBody`. Invalid input (no sender, no
recipient, or no body) throws `ArgumentException` before any request.

## Validation scenarios (all offline, via the fake transport)

Each maps to contract IDs. Implement as xUnit tests under `tests/Mailgunner.Tests/Sending/` (plus
`EmailAddressTests.cs`).

| # | Scenario | Setup | Expected | Contract |
|---|----------|-------|----------|----------|
| 1 | Success parse | Stub returns 200 `{"id":"<x>","message":"Queued. Thank you."}` | `SendResult.Id`/`Message` populated | C-01 |
| 2 | Multipart + endpoint | Capture request | `POST v3/{domain}/messages`, content is `multipart/form-data` | C-03 |
| 3 | Repeated recipients | 3 `to` (+cc/bcc) | 3 distinct `to` parts; cc/bcc each distinct; none comma-joined | C-04, C-05 |
| 4 | 4xx error | Stub returns 400 + body | `MailgunnerException` with `StatusCode==400`, `ResponseBody==body` | C-06 |
| 5 | 5xx error | Stub returns 502 + body | same exception type, code 502, raw body | C-07 |
| 6 | Unparseable 2xx | Stub returns 200 + non-JSON/missing fields | `MailgunnerException` (status + raw body), no result | C-08 |
| 7 | Empty error body | Stub returns 500, empty body | `MailgunnerException`, `ResponseBody==""` (non-null) | C-09 |
| 8 | Input validation | Missing sender / no recipient / no body | `ArgumentException` before any request | C-10 |
| 9 | Null message | `SendAsync(null!)` | `ArgumentNullException` | C-11 |
| 10 | Cancellation | Already-canceled (and in-flight-canceled) token | `OperationCanceledException`; no result | C-12 |
| 11 | Address formatting | `new EmailAddress("a@b.com","Bob")` | `ToString()` == `"Bob <a@b.com>"`; bare address formats plainly; value equality holds | — |

### How tests inject the fake transport

```csharp
var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"id\":\"<id>\",\"message\":\"Queued. Thank you.\"}");
var services = new ServiceCollection();
services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us)
        .ConfigurePrimaryHttpMessageHandler(() => stub);
using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IMailgunnerClient>();

var result = await client.SendAsync(message);
// inspect stub.LastRequest for method/URI/content-type/recipient parts
```

## Run

```bash
dotnet build
dotnet test         # all green, no network, no credentials
```

## Expected outcomes (Definition of Done for this feature)

- 2xx with a valid body → `SendResult` with id + message (SC-001).
- N recipients → N distinct recipient fields, never comma-joined (SC-002).
- 100% of non-success (and unparseable-2xx) responses → the one typed error with exact status + body (SC-003).
- Canceled send → reports cancellation, never a result (SC-004).
- 100% behavior covered offline via a fake transport (SC-005).
- Every send uses `multipart/form-data` (SC-006).
- The sending key never appears in a result or error (SC-007).
