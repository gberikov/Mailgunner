# Mailgunner

Lightweight, modern, unofficial .NET client for the [Mailgun](https://www.mailgun.com/)
(Sinch) REST API, focused on bulk personalized email delivery.

> **Latest release:** `0.1.1`. Sending (single, templated, personalized batches), suppression
> lists, domain webhook management, webhook signature verification, named clients, one-click
> List-Unsubscribe, stream attachments and a safe-by-default send retry mode. See the
> [changelog](https://github.com/gberikov/Mailgunner/blob/master/CHANGELOG.md).

## Highlights

- **Modern & slim** — multi-targets `net10.0`, `net8.0` and `netstandard2.0`; minimal dependency
  footprint (`System.Text.Json`, `Polly.Core`, `Microsoft.Extensions.Http`).
- **Resilient HTTP** — built around typed `HttpClient` via `IHttpClientFactory` with Polly
  transient-fault handling (automatic retry with backoff, on by default).
- **Documented & strict** — nullable reference types, XML docs, and warnings-as-errors.
- **Debuggable packages** — deterministic builds with SourceLink and symbol packages.

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

## Documentation

Full documentation lives in the repository; every link below is absolute so it also works from the
NuGet package page.

| Guide | What it covers |
|---|---|
| [Getting started](https://github.com/gberikov/Mailgunner/blob/master/docs/getting-started.md) | Installation, registration and DI, which credential each operation needs, regions, running the sample. |
| [Sending messages](https://github.com/gberikov/Mailgunner/blob/master/docs/sending.md) | Personalized batches from a stored template, attachments and per-send options, the 16KB cap, recovering batch progress. |
| [Multiple named clients](https://github.com/gberikov/Mailgunner/blob/master/docs/named-clients.md) | Several Mailgun identities in one application, each with its own domain, key, region and retry settings. |
| [Automatic retry & backoff](https://github.com/gberikov/Mailgunner/blob/master/docs/retry.md) | What is retried and what is not, `SendRetryMode`, `Retry-After`, the mandatory wait cap, tuning. |
| [Suppression lists](https://github.com/gberikov/Mailgunner/blob/master/docs/suppressions.md) | Bounces, unsubscribes and complaints — list, get, add, remove, clear, and how paging works. |
| [Webhooks](https://github.com/gberikov/Mailgunner/blob/master/docs/webhooks.md) | Managing domain webhook registrations, and verifying incoming webhook signatures. |
| [Limitations & API coverage](https://github.com/gberikov/Mailgunner/blob/master/docs/limitations.md) | Trimming/AOT, duplicate delivery, timeouts, large imports — and which Mailgun endpoints are implemented. |
| [Building from source](https://github.com/gberikov/Mailgunner/blob/master/docs/contributing.md) | Build and test commands, opt-in live integration tests, project layout. |
| [Releasing](https://github.com/gberikov/Mailgunner/blob/master/docs/RELEASING.md) | How a version is tagged and published. |

## Changelog & license

- Changes are recorded in [CHANGELOG.md](https://github.com/gberikov/Mailgunner/blob/master/CHANGELOG.md) (Keep a Changelog format).
- Licensed under the [MIT License](https://github.com/gberikov/Mailgunner/blob/master/LICENSE).

## Disclaimer

Mailgunner is a community-maintained, unofficial library. It is not affiliated with, authorized
by, or endorsed by Mailgun or Sinch. "Mailgun" and "Sinch" are trademarks of their respective
owners.
