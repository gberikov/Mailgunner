# Multiple named clients
[← All Mailgunner docs](https://github.com/gberikov/Mailgunner/blob/master/README.md#documentation)

Need to talk to more than one Mailgun identity from one application — several domains, or a
transactional/marketing split across subdomains? Register each under a distinct **name**. Every
named client keeps its own domain, sending key, region, and retry settings, fully isolated from the
others and from the unnamed registration.

```csharp
// Register as many names as you need (explicit, delegate, or bound from configuration):
services.AddMailgunner("transactional", "tx.example.com", txKey, MailgunRegion.Us);
services.AddMailgunner("marketing", options =>
{
    options.Domain = "news.example.com";
    options.SendingKey = mktKey;
    options.Region = MailgunRegion.Eu;
    options.Retry.MaxRetryAttempts = 5;
});
services.AddMailgunner("audit", configuration.GetSection("Mailgun:Audit")); // IConfiguration binding

// Resolve a specific one at the point of sending:
var factory = serviceProvider.GetRequiredService<IMailgunnerClientFactory>();
IMailgunnerClient tx = factory.Get("transactional");
await tx.SendAsync(message, cancellationToken);
```

Notes:

- Names are **case-sensitive** (ordinal): `"transactional"` and `"Transactional"` are different names.
- A **blank** or **duplicate** name is rejected when you register it; resolving an **unknown** name
  throws a clear `ArgumentException` (it never returns a default client). These are standard .NET
  errors and never expose a sending key — `MailgunnerException` stays reserved for HTTP responses.
- The existing **unnamed** `AddMailgunner` keeps working unchanged and can coexist with named clients.
  If you register **only** named clients, a bare `IMailgunnerClient` is intentionally not resolvable
  (there is no implicit default) — resolve through `IMailgunnerClientFactory.Get(name)` instead.
- The per-name region/domain match matters exactly as above: a mismatch yields **HTTP 404**.

