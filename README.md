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
