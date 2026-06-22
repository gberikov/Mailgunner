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
- Single-message sending: `IMailgunnerClient.SendAsync(MailgunMessage, CancellationToken)` POSTs
  `multipart/form-data` to `v3/{domain}/messages`, expressing each recipient as a repeated distinct
  field (never comma-joined). New public types `EmailAddress` (address + optional display name, with
  implicit conversion from `string` and value equality), `MailgunMessage` (sender, to/cc/bcc, subject,
  text/HTML body), and `SendResult` (Mailgun's id and status message).
- `MailgunnerException`: the single typed error exposing the HTTP `StatusCode` and raw `ResponseBody`,
  raised on any non-success response or a success body that cannot be parsed; the sending key never
  appears in the result or the error. Invalid input (no sender, no recipient, or no body) throws
  `ArgumentException` before any request is issued, and a canceled token surfaces
  `OperationCanceledException`.
- Templated sending: `MailgunMessage` gains `Template` (stored-template name), `TemplateVersion`
  (optional pinned version), `GenerateTextFromTemplate` (request a generated plain-text part), and
  `TemplateVariables` (global variables applied to the whole send). These are emitted as the
  `template`, `t:version`, `t:text=yes`, and `t:variables` fields respectively; `t:variables` carries
  the variables as a single JSON object (any JSON-representable value type), and the optional fields
  are omitted when unset/empty. A message must be either templated or inline — supplying both a
  `Template` and an inline `Text`/`Html` body (or template data without a `Template` name) throws
  `ArgumentException` before any request. Plain sends are unchanged.

[Unreleased]: https://github.com/gberikov/Mailgunner/commits/master
