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
- Personalized mass send: `IMailgunnerClient.SendBatchAsync(MailgunBatchMessage, CancellationToken)`
  delivers one stored-template message to a large recipient list, automatically chunking it into the
  fewest possible `multipart/form-data` requests (at most 1000 recipients each, `ceil(N / 1000)`
  requests). New public types `MailgunBatchMessage` (sender, subject, template + optional version/
  generated-text, global `TemplateVariables`, and an ordered `Recipients` list) and `BatchRecipient`
  (an address plus that recipient's own `Variables`). Each request reuses the same template and global
  `t:variables` and carries a single `recipient-variables` JSON object keyed by each recipient's bare
  address (a recipient with no variables serializes to `{}`), so Mailgun delivers an individual,
  personalized message per recipient. Recipient order is preserved across chunk boundaries; an empty
  list is a no-op returning an empty result set; a duplicate recipient address throws
  `ArgumentException` before any request. Sending is sequential and fail-fast: the first non-success
  response throws `MailgunnerException` (status + body) and issues no further requests, returning one
  `SendResult` per chunk on success.
- Send enrichment options: any send (single, templated, or batched) can now carry optional production
  knobs via `MailgunSendOptions` (exposed as `Options` on both `MailgunMessage` and
  `MailgunBatchMessage`) plus `Attachments` and `InlineFiles` collections. New public types
  `MailgunSendOptions`, `MailgunFile` (file name + bytes + optional content type), and the
  `ClickTracking` enum (`Yes`/`No`/`HtmlOnly`). Attachments and inline files are emitted as
  `attachment`/`inline` file parts carrying their file name and content type (defaulting to
  `application/octet-stream` when omitted); tags as repeated `o:tag` fields (additive, blank entries
  skipped); `o:testmode`, `o:tracking-opens`, and `o:tracking-clicks` (including `htmlonly`) when set;
  `o:deliverytime` formatted as RFC 2822 with a numeric timezone offset (never a named zone); custom
  headers as `h:<name>` and custom variables as `v:<name>` (string values, unique names). On a batch
  the enrichments repeat identically on every chunk. A blank file name or custom header/variable name
  throws `ArgumentException` before any request; the error contract is otherwise unchanged. The
  combined 16KB cap on `o:`/`h:`/`v:`/`t:` parameters is documented (README) but not enforced
  client-side — exceeding it is surfaced as `MailgunnerException`. Sends supplying no options are
  unchanged.
- Suppression lists: `IMailgunnerClient.Suppressions` exposes a domain's bounces, unsubscribes, and
  complaints lists, independent of the sending pipeline. New public types `IMailgunSuppressions`,
  `ISuppressionList<TEntry>`, `SuppressionPage<TEntry>`, and the typed entries `Bounce` (address, code,
  error, created-at), `Unsubscribe` (address, tags, created-at), and `Complaint` (address, created-at).
  These are JSON endpoints (`GET`/`POST`/`DELETE /v3/{domain}/{bounces|unsubscribes|complaints}`). Each
  list offers `ListAsync` — an `IAsyncEnumerable<T>` that transparently follows the response's cursor
  pagination and streams large lists — over a caller-driven single-page primitive `ListPageAsync`
  (entries + opaque `NextCursor`); an optional page size is applied to the first request only.
  `GetAsync` fetches one entry by address; `AddAsync` creates an entry (address plus each type's optional
  fields) via a JSON body; `RemoveAsync` deletes a single address and `ClearAsync` deletes the whole
  list. A null entry or blank address throws `ArgumentException`/`ArgumentNullException` before any
  request; any non-success response (including a not-found get/remove) surfaces `MailgunnerException`
  with the HTTP status code and raw body. JSON (de)serialization uses `System.Text.Json` source
  generation; no new dependency is added.

[Unreleased]: https://github.com/gberikov/Mailgunner/commits/master
