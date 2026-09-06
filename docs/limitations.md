# Limitations & API coverage
[← All Mailgunner docs](https://github.com/gberikov/Mailgunner/blob/master/README.md#documentation)

- **No trimming/AOT guarantee.** Template and recipient variables (`t:variables`, `recipient-variables`) are
  serialized with reflection-based `System.Text.Json`; in a Native AOT app that path throws at runtime. The
  suppression and webhook DTOs use source generation and are unaffected.
- **Duplicate delivery vs. retries.** A send is retried only on HTTP 429 by default (`SendRetryMode.Safe`); with
  `SendRetryMode.Full` a lost response can lead to the same message being delivered twice.
- **Timeouts.** Each attempt is bounded by `Retry.AttemptTimeout` up to the response headers; the typed
  `HttpClient.Timeout` bounds the whole call, body reads included, at
  `(MaxRetryAttempts + 1) × AttemptTimeout + MaxRetryAttempts × MaxSingleWait`.
- **Transport failures are not `MailgunnerException`.** A response, success or failure, always maps to a
  result or a `MailgunnerException`. When no response is obtained, the underlying exception surfaces after
  the retry budget: `HttpRequestException` (connection/DNS), `TimeoutException` (an attempt exceeded
  `AttemptTimeout`), or `TaskCanceledException` (the overall `HttpClient.Timeout` elapsed).
- **Batch failures.** `SendBatchAsync` is fail-fast. `BatchSendProgress.FromException(ex)` exposes an
  immutable snapshot for HTTP errors, transport failures, cancellation, and serialization failures
  during chunk processing. It preserves the original exception type. Initial validation errors have
  no progress because no chunks were processed. The legacy `MailgunnerException.AcceptedResults` /
  `FailedChunkIndex` properties remain available.
- **Attachment cancellation.** On .NET 8/10, source copying receives the request's cancellation token,
  including the attempt timeout. The netstandard2.0 serialization API cannot pass that token to the
  source read; a stalled source on that target must enforce its own read timeout. Stream factories
  are synchronous and must return promptly on every target.
- **Large imports.** `AddRangeAsync` validates and materializes the entire input before the first
  request, using memory proportional to the number of entries. Chunking limits request sizes, not
  the initial memory footprint. Batch sends are sequential; there is no built-in concurrent dispatcher.
- **16KB parameter cap** on `o:`/`h:`/`v:`/`t:` fields is not enforced client-side (see
  [Send options & limits](https://github.com/gberikov/Mailgunner/blob/master/docs/sending.md#send-options--limits)).


## API coverage

Mailgunner focuses on sending. The following matrix describes HTTP endpoint coverage, not just
convenience methods such as automatic batching. Compared with the Mailgun Send OpenAPI on 2026-09-06:

| API area | Implemented | Remaining |
|---|---|---|
| Messages | Component-based send (plain, template, personalized batch) | MIME send, stored-message retrieval/resend, queue status, deleting scheduled mail |
| Bounces, unsubscribes, complaints | List/get/add/delete/clear; JSON additions in chunks | CSV import |
| Domain webhooks | v3 list/get/create/update/delete | v4 operations |
| Account webhooks | — | All operations |
| Logs, Metrics, Tags | — | Queries, reporting, tag management |
| Templates | Referencing a stored template when sending | Account/domain template and version management |
| Domains, routing, mailing lists, allowlist, IPs, account administration | — | These API areas |

See the [Mailgun API reference](https://documentation.mailgun.com/docs/mailgun/api-reference/send/mailgun).
The legacy Events API is deprecated in favor of Logs. The library currently parses neither Logs nor
incoming webhook event payloads; `MailgunWebhookSignature` verifies supplied signature fields only.
Consumers own payload parsing and token-reuse storage (shared and atomic across application instances).
Implemented response models are typed; arbitrary `object` values are confined to outgoing custom
template/recipient data, not API responses.

