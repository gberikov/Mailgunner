# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial repository scaffold: `slnx` solution, multi-targeted (`net8.0;netstandard2.0`)
  library project, and an offline xUnit test project.
- Centralized configuration: `Directory.Build.props` (nullable, latest C#, XML docs,
  warnings-as-errors, build-enforced code style), `Directory.Packages.props` (Central
  Package Management), `.editorconfig`, and a pinned SDK via `global.json`.
- Package metadata, deterministic builds, and SDK-implicit SourceLink with symbol packages.
- Dependency-injection client registration: `AddMailgunner` (explicit settings and
  `Action<MailgunnerOptions>` overloads) registers a resolvable `IMailgunnerClient` as a typed
  `HttpClient` via `IHttpClientFactory`.
- Regional routing: `MailgunRegion` (US/EU) selects the API base URL
  (`https://api.mailgun.net` / `https://api.eu.mailgun.net`); a region/domain mismatch is
  documented as a known HTTP 404 failure mode.
- HTTP Basic authentication derived from the sending key (username `api`).
- Fail-fast configuration validation at startup (`ValidateOnStart`): a missing/blank domain or
  sending key, or an unspecified/unrecognized region, fails startup with an
  `OptionsValidationException` naming the offending setting; the sending-key value is never exposed.

[Unreleased]: https://github.com/gberikov/Mailgunner/commits/master
