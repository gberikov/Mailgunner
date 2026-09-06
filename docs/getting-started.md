# Getting started
[← All Mailgunner docs](https://github.com/gberikov/Mailgunner/blob/master/README.md#documentation)

Install the package, register the client, send your first message. For everything you can put
on a send, see [Sending messages](https://github.com/gberikov/Mailgunner/blob/master/docs/sending.md).

## Installation

```bash
dotnet add package Mailgunner
```

> Releases are published on `v*` tags; see [docs/RELEASING.md](https://github.com/gberikov/Mailgunner/blob/master/docs/RELEASING.md).

## Quickstart

For a single message, register the client with your host and resolve it from DI:

```csharp
using Mailgunner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddMailgunner(
    "mg.example.com", builder.Configuration["Mailgun:SendingKey"]!, MailgunRegion.Us);
using var host = builder.Build();
var client = host.Services.GetRequiredService<IMailgunnerClient>();
var message = new MailgunMessage
{
    From = "hello@mg.example.com",
    Subject = "Hello",
    Text = "Your first message from Mailgunner.",
};
message.To.Add("you@example.com");
SendResult result = await client.SendAsync(message);
Console.WriteLine(result.Id); // accepted by Mailgun; this is not a delivery confirmation
```

This console example also uses the `Microsoft.Extensions.Hosting` package. Supply the key through
configuration or `Mailgun__SendingKey`; use the region and sender domain configured in your account.

## Registering the client

Register the client into your dependency-injection container with a single call, supplying your
Mailgun domain, a sending key, and a region. Resolving `IMailgunnerClient` then yields a ready
instance whose requests target the correct regional host and carry HTTP Basic authentication.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Mailgunner;

// Explicit settings:
services.AddMailgunner(
    domain: "mg.example.com",
    sendingKey: configuration["Mailgun:SendingKey"]!,
    region: MailgunRegion.Eu);

// …or configure via a delegate (e.g. bound from configuration):
services.AddMailgunner(options =>
{
    options.Domain = configuration["Mailgun:Domain"]!;
    options.SendingKey = configuration["Mailgun:SendingKey"]!;
    options.Region = MailgunRegion.Us;
});

// Later, anywhere DI is available:
var client = serviceProvider.GetRequiredService<IMailgunnerClient>();
```

For **sending**, prefer a **Domain Sending Key**, supplied from configuration or environment.
It grants only `POST /messages` and `/messages.mime` for its domain. It does **not** authorize
suppression-list or webhook-management operations. Those need an API key with the corresponding
read/write permissions; the primary account key has broad account access.
See [Mailgun authentication](https://documentation.mailgun.com/docs/mailgun/api-reference/mg-auth).

| Operation | Credential |
|---|---|
| `SendAsync`, `SendBatchAsync` | Domain Sending Key, or an API key authorized to send |
| `Suppressions.*`, `Webhooks.*` | API key authorized for these management operations |
| `MailgunWebhookSignature.Verify` | HTTP webhook signing key, not either API key above |

Use separate named clients when one application needs both sending and administration:

```csharp
services.AddMailgunner("sending", "mg.example.com", configuration["Mailgun:SendingKey"]!, MailgunRegion.Us);
services.AddMailgunner("management", "mg.example.com", configuration["Mailgun:ManagementKey"]!, MailgunRegion.Us);
var factory = serviceProvider.GetRequiredService<IMailgunnerClientFactory>();
await factory.Get("sending").SendAsync(message, cancellationToken);
var webhooks = await factory.Get("management").Webhooks.ListAsync(cancellationToken);
```

The configuration property is still named `SendingKey` for compatibility; it holds the API key used
by that client for all its HTTP operations. Keep management credentials out of sending-only services.

Invalid configuration (a missing/blank domain or sending key, or an unspecified/unrecognized
region) is rejected when the host starts, with a clear error that names the offending setting.

## Regions

The region selects the API host: `MailgunRegion.Us` → `https://api.mailgun.net`,
`MailgunRegion.Eu` → `https://api.eu.mailgun.net`. The region and the sending domain are
independent: if you configure a region that does **not** match where your domain is hosted, the
client still builds, but requests go to a host where the domain is not found and Mailgun
responds with **HTTP 404**. Make sure the region matches your domain's region.

## Run the sample

A runnable version of the exact scenario above lives in
[`samples/Mailgunner.Sample`](https://github.com/gberikov/Mailgunner/tree/master/samples/Mailgunner.Sample). It is also the project's single
environment-gated **live** check: it sends only when credentials are present and is **skipped — not
failed — when they are absent**.

**One-time setup** (live run only): in the Mailgun dashboard, add your test addresses as
**authorized recipients** of your **sandbox** domain, and create a **stored Handlebars template**
named `conference-invitation` whose body references the per-recipient fields, for example:

```handlebars
<p>Hi {{name}}, your ticket is <strong>{{ticket}}</strong>.</p>
<p>Your personal link: <a href="{{link}}">{{link}}</a></p>
```

**Supply credentials** via environment variables (note the `__` section separator) or user-secrets —
never edit source or commit a secret:

```bash
export Mailgun__Domain="sandbox123.mailgun.org"
export Mailgun__SendingKey="key-…"                 # prefer a Domain Sending Key
export Mailgun__Region="Us"                          # Us or Eu (must match the domain)
export Mailgun__Recipients__0__Address="you@example.com"
export Mailgun__Recipients__1__Address="teammate@example.com"
```

```bash
dotnet run --project samples/Mailgunner.Sample
```

With credentials present, the sample sends one personalized batch and prints a success line (id +
status) per chunk. **With any required setting absent**, it makes no request, prints exactly which
settings are missing and where to supply them, and exits `0`.

