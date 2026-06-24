# Mailgunner

Lightweight, modern, unofficial .NET client for the [Mailgun](https://www.mailgun.com/)
(Sinch) REST API, focused on bulk personalized email delivery.

> **Status:** Foundation scaffold. The repository, build, packaging, and quality gates are
> in place; client functionality (messages, suppressions, webhooks) is delivered by
> subsequent features.

## Highlights

- **Modern & slim** — multi-targets `net8.0` and `netstandard2.0`; minimal dependency
  footprint (`System.Text.Json`, `Polly`, `Microsoft.Extensions.Http`).
- **Resilient HTTP** — built around typed `HttpClient` via `IHttpClientFactory` with Polly
  transient-fault handling (planned).
- **Documented & strict** — nullable reference types, XML docs, and warnings-as-errors.
- **Debuggable packages** — deterministic builds with SourceLink and symbol packages.

## Installation

```bash
dotnet add package Mailgunner
```

> Not yet published while the library is in its foundation phase.

## Getting started

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

Prefer a **Domain Sending Key** over your primary account key, and supply it from configuration
or environment — never hard-code it.

Invalid configuration (a missing/blank domain or sending key, or an unspecified/unrecognized
region) is rejected when the host starts, with a clear error that names the offending setting.

### Regions

The region selects the API host: `MailgunRegion.Us` → `https://api.mailgun.net`,
`MailgunRegion.Eu` → `https://api.eu.mailgun.net`. The region and the sending domain are
independent: if you configure a region that does **not** match where your domain is hosted, the
client still builds, but requests go to a host where the domain is not found and Mailgun
responds with **HTTP 404**. Make sure the region matches your domain's region.

## Send options & limits

Any send — single, templated, or a personalized batch — can be enriched with optional production
"knobs" via `MailgunMessage.Options` / `MailgunBatchMessage.Options` (a `MailgunSendOptions`), plus
the `Attachments` and `InlineFiles` collections. Every knob is optional; omitting one leaves your
Mailgun account default in effect.

- **Attachments & inline files** — add `MailgunFile(fileName, content, contentType?)` to `Attachments`
  (downloadable) or `InlineFiles` (embeddable, referenced from HTML by content id). When the content
  type is omitted it defaults to `application/octet-stream`.
- **Tags** — `Options.Tags` may carry several values; all are sent (not de-duplicated).
- **Test mode** — `Options.TestMode = true` exercises the pipeline without delivering.
- **Tracking** — `Options.TrackingOpens` (on/off) and `Options.TrackingClicks`
  (`ClickTracking.Yes`/`No`/`HtmlOnly`).
- **Scheduled delivery** — `Options.DeliveryTime` (a `DateTimeOffset`) is sent as an **RFC 2822**
  date-time with a **numeric** timezone offset (for example `Thu, 25 Jun 2026 14:00:00 +0000`), never
  a named zone.
- **Custom headers & variables** — `Options.CustomHeaders` (`h:` prefix) and `Options.CustomVariables`
  (`v:` prefix, string values).

> **16KB limit.** Mailgun caps the **combined** size of the option (`o:`), custom-header (`h:`),
> custom-variable (`v:`), and template (`t:`) parameters at **16KB per request**. Mailgunner does not
> enforce this client-side; exceeding it causes the service to reject the request, surfaced as a
> `MailgunnerException` carrying the HTTP status code and response body.

## Building from source

Requires a [.NET SDK](https://dotnet.microsoft.com/download) matching `global.json`
(a `slnx`-capable SDK; .NET 10 recommended).

```bash
dotnet restore
dotnet build Mailgunner.slnx -c Release
dotnet test Mailgunner.slnx -c Release
```

Tests run fully offline — no network access or Mailgun credentials are required.

## Project layout

| Path | Purpose |
|------|---------|
| `src/Mailgunner/` | The publishable library. |
| `tests/Mailgunner.Tests/` | Offline xUnit test suite. |
| `Directory.Build.props` | Shared build/quality/package settings. |
| `Directory.Packages.props` | Central Package Management (pinned versions). |
| `.editorconfig` | Build-enforced style & analyzer rules. |

## Documentation & history

- Changes are recorded in [CHANGELOG.md](CHANGELOG.md) (Keep a Changelog format).
- Licensed under the [MIT License](LICENSE).

## Disclaimer

Mailgunner is a community-maintained, unofficial library. It is not affiliated with, authorized
by, or endorsed by Mailgun or Sinch. "Mailgun" and "Sinch" are trademarks of their respective
owners.
