# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- One-click List-Unsubscribe (RFC 8058): a typed, opt-in `MailgunSendOptions.ListUnsubscribe` property
  (new `ListUnsubscribeOptions` type with `Url`, `MailtoAddress`, and `OneClick`) emits a correctly
  formatted `List-Unsubscribe` header — and, when `OneClick` is set, the
  `List-Unsubscribe-Post: List-Unsubscribe=One-Click` header — so marketing mail can meet the Gmail/Yahoo
  bulk-sender one-click requirement without hand-assembling raw headers. Supports an `https` URL only, a
  `mailto` address only, or both (emitted URL-first, comma-separated, each in angle brackets). Validated
  before any request: the URL must be absolute `https` and free of control characters / line breaks,
  one-click requires an `https` URL, and a target set both here and as a manual
  `List-Unsubscribe`/`List-Unsubscribe-Post` entry in `CustomHeaders` (matched case-insensitively) is
  rejected so no duplicate header reaches the wire — all via `ArgumentException` (no new exception type).
  Applies uniformly to single, templated, and batch sends (repeated identically on every chunk). Unset by
  default, so transactional mail is unaffected. Purely additive (SemVer MINOR).
- Named clients: `AddMailgunner` now has named overloads — `AddMailgunner(name, domain, sendingKey,
  region)`, `AddMailgunner(name, Action<MailgunnerOptions>)`, and `AddMailgunner(name, IConfiguration)`
  — so several independently configured Mailgunner clients can coexist in one container (for example
  separate Mailgun domains, or a transactional/marketing split), each with its own domain, sending
  key, region, and `RetryPolicyOptions`. Resolve one at runtime with the new
  `IMailgunnerClientFactory.Get(name)`, which returns a full `IMailgunnerClient` (sending +
  suppressions). Each named client keeps its own typed `HttpClient` (via `IHttpClientFactory`), base
  URL/auth, and resilience pipeline, fully isolated from other names and from the existing unnamed
  registration. Names are non-blank and compared case-sensitively (ordinal); a blank or duplicate name
  is rejected at registration and an unknown name at resolution, both with a clear `ArgumentException`
  that never exposes a sending key. Per-name configuration is validated at startup
  (`ValidateOnStart`). The existing unnamed `AddMailgunner` is unchanged and may coexist; when only
  named clients are registered, a bare `IMailgunnerClient` is intentionally not resolvable (no implicit
  default). This is purely additive (SemVer MINOR). Adds the first-party
  `Microsoft.Extensions.Options.ConfigurationExtensions` dependency, used only by the configuration-
  section overload.

### Security

- Suppression-list pagination now validates a caller-supplied cursor before following it: only an
  absolute `https` URL on the configured Mailgun host (matching the client's base address) and
  addressing the same list is accepted; anything else throws `ArgumentException` with no request
  issued. Previously an arbitrary absolute cursor was sent verbatim, which — because the client
  carries HTTP Basic auth on every request — could leak the sending key to a foreign host.
- Header/address injection hardening: `EmailAddress` now rejects control characters (including CR/LF)
  in the address and display name; a display name containing RFC 5322 special characters is emitted
  as a quoted string (with embedded `"` and `\` escaped). Custom header names must be valid RFC 7230
  tokens and custom header values must not contain line breaks; custom variable names must be free of
  control characters. All are rejected with `ArgumentException` before any request.
- CI/release supply-chain hardening: GitHub Actions are pinned to commit SHAs (not mutable `@v4`
  tags), a failing `dotnet list package --vulnerable` audit gate was added to CI, and a Dependabot
  configuration keeps the action pins and NuGet packages current.

### Changed

- Batch send validates every recipient address up front: a recipient created from a
  `default(EmailAddress)` (blank address) now throws `ArgumentException` before any request instead
  of failing later during multipart construction.
- Suppression-list page size is now bounded to the Mailgun-documented range `1..1000`; an
  out-of-range value throws `ArgumentOutOfRangeException` before any request.

### Removed

- Removed the placeholder public `MailgunnerInfo` type (a scaffold artifact with no runtime value);
  the offline smoke test now asserts the real client contract instead.

## [0.1.0-preview.1] - 2026-06-24

First public **pre-release** to NuGet. Ships the complete `0.1.0` foundation below for early
feedback while the public API may still evolve; published as a pre-release so it is not
surfaced as the latest stable version. See [0.1.0](#010---2026-06-24) for the full feature
set included in this pre-release.

## [0.1.0] - 2026-06-24

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
- Webhook signature verification: `MailgunWebhookSignature.Verify(signingKey, timestamp, token,
  signature)`, a pure, network-free static method that validates a Mailgun event webhook's signature
  as the lowercase-hex HMAC-SHA256 of `timestamp + token` keyed by the caller-supplied webhook signing
  key, using a constant-time comparison that never short-circuits on the first differing character. A
  `null`/empty/whitespace signing key throws `ArgumentException`; any missing or malformed
  webhook-supplied value (a `null` timestamp/token, or a `null`, empty, wrong-length, or
  non-hexadecimal signature) returns `false` rather than throwing. Replay/freshness checks are left to
  the consumer. No HTTP, no dependency injection, and no new dependency are involved (uses the in-box
  `System.Security.Cryptography`).
- Automatic retry with backoff: every outbound request (sends and suppressions, which share the typed
  `HttpClient`) is now wrapped in resilience that is **on by default**. Transient failures — HTTP
  `429`, `408`, and any `5xx`, plus transport-level faults with no response (timeout, connection
  reset/refused, DNS failure) — are retried automatically, while a non-429 `4xx` is never retried and
  surfaces immediately after one attempt. Each computed wait uses exponential backoff with bounded
  additive jitter (so successive waits are strictly increasing and desynchronized); a `Retry-After`
  header on a retryable response (delta-seconds **or** HTTP-date) takes precedence for that attempt.
  **Every** single wait is clamped to a mandatory cap so a hostile or far-future value cannot stall a
  send. The retry budget is finite; when it is exhausted the final failure surfaces unchanged via the
  single `MailgunnerException` contract (last status + body) and a single Warning exhaustion record is
  logged (status/exception type and attempt count only — never the sending key, `Authorization`
  header, or body). Pending waits are cancelable: the caller's `CancellationToken` abandons a wait
  promptly. Tuning is additive and defaulted via `MailgunnerOptions.Retry` (`RetryPolicyOptions`:
  `MaxRetryAttempts` = 3, `BaseDelay` = 500 ms, `MaxSingleWait` = 30 s, `UseJitter` = true), so every
  existing registration is unaffected. This is the library's first use of `Polly` (a permitted
  dependency); `Microsoft.Extensions.Http.Polly`/`.Resilience` are deliberately not used. An eventual
  success is indistinguishable from a first-attempt success (which still makes exactly one attempt with
  no waiting).
- First-run experience: a copy-paste **README quickstart** and a runnable, non-packable console
  **sample** (`samples/Mailgunner.Sample`) that performs a personalized conference-invitation batch
  send (each recipient gets their own `name`/`ticket`/`link` from one stored Handlebars template via
  the `t:variables` ↔ `recipient-variables` bridge). The sample is the project's single
  environment-gated live check — it reads credentials from configuration/environment and is skipped
  (not failed) when they are absent — and its credential-presence resolver is covered by an offline
  unit test, so the default build/test stay green with no credentials.

[Unreleased]: https://github.com/gberikov/Mailgunner/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/gberikov/Mailgunner/releases/tag/v0.1.0
