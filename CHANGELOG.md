# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `BatchSendProgress.FromException` exposes accepted chunks, the failed chunk index, and whether its
  HTTP send path started, for HTTP/transport errors, cancellation, and serialization failures without
  replacing the original exception type.
- `WebhookRegistration.EventToken` preserves future event names with typed URLs; their enum value is
  `WebhookEventType.Unknown` (response-only).

### Fixed

- Stream attachments propagate cancellation and per-attempt timeouts to source copying on .NET 8/10.
- Undefined `SendRetryMode` values fail validation instead of enabling full send retries.
- Suppression reads reject missing item arrays and addresses; webhook reads reject missing envelopes
  and malformed URL lists instead of silently returning empty or fabricated results.
- AMP-only single messages and stored-template messages/batches that inherit the template's From
  header are accepted by local validation.
- Multi-event webhook creation validates all event types before creating any registration.
- Suppression additions avoid a redundant chunk copy and observe cancellation during input validation.
- README examples and guidance now cover management-key permissions, recipient-visible custom
  variables, batch recovery, API coverage, and netstandard2.0 attachment cancellation limits.

## [0.1.1] - 2026-09-04

### Added

- `net10.0` target. The package now multi-targets `net10.0`, `net8.0` and `netstandard2.0`;
  consumers on .NET 10 (LTS through November 2028) get the current runtime's asset instead of
  the `net8.0` one. No API change — `net8.0` and `netstandard2.0` stay supported.

### Changed

- The `net8.0` and `net10.0` dependency groups no longer list `System.Text.Json`: with a
  `net10.0` target present, the SDK prunes packages that the shared framework already provides
  (it is in-box on both). The `netstandard2.0` group still carries it, as before.
- The package no longer declares `Microsoft.Extensions.Configuration`. It was never referenced by
  the library: repository-wide transitive pinning promoted it to a direct dependency because a
  `PackageVersion` entry exists for the test project. Pinning is now off for the packable project,
  so the published dependency list contains only what the library actually references. Consumers
  are unaffected — the package still reaches them through
  `Microsoft.Extensions.Options.ConfigurationExtensions`, at the same 8.0.0.

## [0.1.0] - 2026-09-03

First version published to NuGet. It ships the foundation drafted on 2026-06-24 (listed at the end of
this file; never tagged or published) together with the review-driven changes below. The Changed,
Removed, Fixed and Security entries describe changes relative to that unpublished draft.

### Added

- Send options `Dkim`, `SecondaryDkim`, `SecondaryDkimPublic`, `DeliverWithin`,
  `DeliveryTimeOptimizePeriod`, `TrackingPixelLocationTop`, `ArchiveTo` and `SuppressHeaders`
  (`o:dkim`, `o:secondary-dkim`, `o:secondary-dkim-public`, `o:deliver-within`,
  `o:deliverytime-optimize-period`, `o:tracking-pixel-location-top`, `o:archive-to`, `o:suppress-headers`).
- Every request carries a `User-Agent: Mailgunner/<version>` header.
- `WebhookRegistration` has a public constructor, so a hand-written test double of `IMailgunWebhooks`
  can build the registrations it returns.

- Domain webhook management: a new `client.Webhooks` (`IMailgunWebhooks`) capability area that lists,
  reads, creates, updates, and deletes a domain's webhook registrations over Mailgun's v3 webhook
  endpoints (`/v3/domains/{domain}/webhooks`), mirroring the shape of `client.Suppressions`. A webhook is keyed by
  one of a closed, typed set of event types (`WebhookEventType`: `Delivered`, `Opened`, `Clicked`,
  `Unsubscribed`, `Complained`, `PermanentFail`, `TemporaryFail`) and carries one or more callback URLs,
  returned as a typed `WebhookRegistration` (`EventType` + `Urls`). `CreateAsync(eventType, urls)` and
  `UpdateAsync(eventType, urls)` send the URL(s) as form fields and return the registration;
  `CreateAsync(eventTypes, url)` registers one URL across several event types, fanning out to one create
  per event type sequentially with fail-fast, no-rollback semantics on a partial failure; `ListAsync`
  returns one registration per configured event type (empty when none); `GetAsync`/`DeleteAsync` act on a
  single event type. Every operation reuses the registered client's region/base URL and Basic auth, takes
  a `CancellationToken`, and surfaces a non-2xx response as the single `MailgunnerException` (status code +
  raw body). Pairs with the existing `MailgunWebhookSignature.Verify` primitive to complete the push-based
  delivery-tracking story. Purely additive (SemVer MINOR); no new runtime dependency and no new exception
  type.
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
- MailgunWebhookSignature.Verify overload with maxAge (and optional TimeProvider) rejecting stale/future timestamps.
- WebhookEventType.Accepted.
- ReplyTo on MailgunMessage and MailgunBatchMessage (emitted as h:Reply-To); Options is now settable so one MailgunSendOptions can be shared.
- Send options RequireTls, SkipVerification, Tracking, SendingIp, SendingIpPool, TimeZoneLocalize and MailgunMessage.AmpHtml.
- Batch sends without a stored template: MailgunBatchMessage.Text / Html with %recipient.var% placeholders.
- MailgunnerException.FailedChunkIndex / AcceptedResults expose which batch chunks were accepted before a failure.
- ISuppressionList<T>.AddRangeAsync for bulk adds (chunks of 1000 per request). **Upgrade note:**
  this is a required member on a public interface with no default implementation, so a hand-written
  implementation of `ISuppressionList<T>` (for example a test double) must add it to keep compiling;
  mocking frameworks are unaffected.
- Stream-backed MailgunFile(fileName, Func<Stream>, contentType, length); Content is now nullable for such files.
- RetryPolicyOptions.AttemptTimeout (default 100 s) bounds each individual request attempt under the resilience handler.
- The netstandard2.0 build is now executed by a net48 test project on the Windows CI leg.
- A new `tests/Mailgunner.IntegrationTests` project runs sends, suppressions, and webhooks against a
  real Mailgun account when the `Mailgun__Domain` / `Mailgun__SendingKey` / `Mailgun__Region` (and, for
  the send test, `Mailgun__Recipients__0__Address`) environment variables are set; every test reports
  `Skipped` (via `Xunit.SkippableFact`) when they are absent, so the project builds and runs green with
  no secrets and is never picked up by CI or the release workflow's scoped `dotnet test` commands. Each
  test cleans up what it created — restoring, not deleting, a pre-existing webhook registration for the
  event type it exercises — even on a failing assertion, and isolates each cleanup step so one failing
  does not block another.
- The release workflow now builds and runs the test suite before packing, and the package is
  validated for target-framework compatibility on pack (`EnablePackageValidation`); a red test
  suite or an invalid package now blocks publishing.

### Changed

- **Message sends are now retried only on HTTP 429 by default**, because `POST /messages` is not
  idempotent (new `RetryPolicyOptions.SendRetryMode`, default `Safe`). Set `SendRetryMode.Full` to
  restore the previous behaviour — retry on `408`/`5xx`/transport faults too — accepting the risk of
  duplicate delivery. Suppression and webhook requests are unaffected and keep retrying on the full
  policy. **Upgrade note:** a send that previously retried transient failures now surfaces them after
  one attempt unless you opt into `Full`.
- **Each attempt is now bounded by the new `RetryPolicyOptions.AttemptTimeout`** (default 100 s, up to
  the response headers), and the typed `HttpClient`'s overall `Timeout` is set to the worst case over
  every attempt and wait, `(MaxRetryAttempts + 1) × AttemptTimeout + MaxRetryAttempts × MaxSingleWait`
  (490 s with the defaults), so retries and backoff waits are never cut short while a stalled response
  body, which `HttpClient` reads outside the per-attempt timeout, can never hang a caller that passed
  no `CancellationToken`. **Upgrade note:** a caller relying on the previous 100 s `HttpClient` default
  to bound the *whole* call should lower `AttemptTimeout` and/or `MaxRetryAttempts`.
- `MailgunSendOptions.CustomHeaders` compares header names case-insensitively, like mail headers, so
  `Reply-To` and `reply-to` can no longer both reach the wire.
- A success response whose body is not valid JSON now surfaces as `MailgunnerException` (status + raw
  body) from the suppression and webhook operations too, instead of a raw `JsonException`; the send
  path already behaved this way.
- Webhook callback URLs must be absolute `http` or `https` URLs; anything else throws
  `ArgumentException` before any request. The multi-event `CreateAsync` overload registers each distinct
  event type once, so a repeated event type no longer triggers a service rejection mid-fan-out.
- Transport failures that yield no response (`HttpRequestException`, `TimeoutException`,
  `TaskCanceledException`) are now documented on `IMailgunnerClient` and in the README; they were always
  surfaced unwrapped.
- MailgunnerException.Message now includes Mailgun's "message" from a JSON error body (truncated to 200 chars).
- Batch send validates every recipient address up front: a recipient created from a
  `default(EmailAddress)` (blank address) now throws `ArgumentException` before any request instead
  of failing later during multipart construction.
- Suppression-list page size is now bounded to the Mailgun-documented range `1..1000`; an
  out-of-range value throws `ArgumentOutOfRangeException` before any request.
- Runtime dependency floors lowered to the 8.0.x Extensions train (Microsoft.Extensions.Http 8.0.1,
  System.Text.Json 8.0.5); Polly replaced by the slimmer Polly.Core.
- MaxRetryAttempts is validated to be at most 10; backoff math saturates at MaxSingleWait instead of overflowing.

### Removed

- Removed the placeholder public `MailgunnerInfo` type (a scaffold artifact with no runtime value);
  the offline smoke test now asserts the real client contract instead.

### Fixed

- Domain webhook management now targets Mailgun's actual path `/v3/domains/{domain}/webhooks`; the previous `/v3/{domain}/webhooks` returned HTTP 404 for every operation.
- Suppression entries' CreatedAt is now populated: Mailgun's "…UTC" timestamps were silently parsed to null.
- Suppression AddAsync now sends the JSON array shape Mailgun documents; a bare JSON object was rejected.
- Calling the unnamed AddMailgunner more than once no longer stacks a second retry handler (which multiplied attempts and waits); the latest options still win.
- The sample reports a `Mailgun:From` or `Mailgun:Recipients:N:Address` setting in `Name <addr>` form
  as a missing/invalid setting and exits cleanly, instead of crashing with `ArgumentException`.
- On the netstandard2.0 build, per-thread retry jitter sources are seeded explicitly; on .NET Framework
  the parameterless `Random` seeds from the clock tick, so threads started together drew identical jitter.

### Security

- The `Authorization` header is redacted from the HTTP client factory's own request logging
  (`RedactLoggedHeaders`), so a `Trace`-level logger no longer prints the Basic-auth sending key.
- `MailgunFile` rejects a file name containing a control character or a double quote with
  `ArgumentException` at construction; previously such a name surfaced late, at send time, as a transport
  `FormatException`.

- The sending domain is now percent-encoded in request paths, preventing a domain value containing `/`, `?`, `#` or space from escaping its path segment and rewriting the request target.
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
- EmailAddress now rejects list/delimiter characters (, ; < > " ( ) [ ] \ and whitespace) and malformed
  '@' placement, so a single caller-supplied value can no longer smuggle extra recipients.
  **Upgrade note:** a display name must now be passed separately — `new EmailAddress("bob@example.com",
  "Bob")` — rather than embedded in the address string as `"Bob <bob@example.com>"`. The library
  quotes and escapes the display name for you when it builds the wire value, so the emitted header is
  unchanged.
- CI/release supply-chain hardening: GitHub Actions are pinned to commit SHAs (not mutable `@v4`
  tags), a failing `dotnet list package --vulnerable` audit gate was added to CI, and a Dependabot
  configuration keeps the action pins and NuGet packages current.

## Unpublished draft - 2026-06-24

The foundation on which [0.1.0](#010---2026-09-03) builds. It was drafted as `0.1.0` (and a
`0.1.0-preview.1` pre-release was planned) but never tagged or pushed to nuget.org; it is kept here
because the 0.1.0 entries above describe their changes relative to it.

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

[Unreleased]: https://github.com/gberikov/Mailgunner/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/gberikov/Mailgunner/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/gberikov/Mailgunner/releases/tag/v0.1.0
