# Phase 0 Research: Automatic Retry with Backoff

All decisions below resolve the Technical Context and remove every NEEDS CLARIFICATION. Sources:
the project constitution v1.1.0 (Principle II mandates Polly retry on 429/408/5xx with
exponential-backoff-plus-jitter and `Retry-After`), the 002–008 codebase, the feature spec
(FR-001…FR-015), and Polly v8 documentation (`/app-vnext/polly`).

## Decision 1 — Polly integration without `Microsoft.Extensions.Http.Polly`/`.Resilience`

**Decision**: Add a reference to `Polly` (8.7.0, already pinned) and integrate it through a
**hand-written internal `DelegatingHandler`** (`MailgunResilienceHandler`) that executes the outgoing
request through a Polly v8 `ResiliencePipeline<HttpResponseMessage>`. Attach it to the existing
`IHttpClientBuilder` with `AddHttpMessageHandler<MailgunResilienceHandler>()`.

**Rationale**: Principle I fixes the permitted runtime dependencies to exactly
`{System.Text.Json, Polly, Microsoft.Extensions.Http}`. The usual conveniences — `AddPolicyHandler`
(from `Microsoft.Extensions.Http.Polly`) and `AddResilienceHandler` (from
`Microsoft.Extensions.Http.Resilience`) — would each add a package outside that list. `Polly` itself
plus `AddHttpMessageHandler` (already in `Microsoft.Extensions.Http`) are sufficient: the handler
calls `pipeline.ExecuteAsync(...)` and inside it `base.SendAsync(request, token)`. This keeps all HTTP
on the `IHttpClientFactory` typed client (Principle II) and adds nothing beyond a permitted package.

**Alternatives considered**:
- *`Microsoft.Extensions.Http.Resilience` (`AddResilienceHandler`)* — the modern idiomatic path, but
  not in the permitted catalog → rejected on Principle I.
- *`Microsoft.Extensions.Http.Polly` (`AddPolicyHandler`)* — Polly v7-style, also outside the catalog
  and built around the legacy `IAsyncPolicy` API → rejected.
- *Retry inside `MailgunnerClient` (application-level loop)* — would bypass the typed-client/handler
  pipeline, duplicate logic for the suppressions path, and re-buffer multipart content manually →
  rejected; the handler covers every outbound request uniformly.

**Note on request re-send**: a `DelegatingHandler` re-sends the **same** `HttpRequestMessage` across
attempts. The send path already builds `multipart/form-data` via `HttpContent` that supports being
re-read by the in-box handlers for retry of idempotent-shaped POSTs in this offline/test context; the
fake transport reads content per attempt. (Mailgun message POSTs are safe to retry on transient
failure — at-least-once delivery is the accepted, documented trade-off for a transient 5xx/429.)

## Decision 2 — Retryable vs. permanent classification

**Decision**: `ShouldHandle` returns true for: HTTP **429**, **408**, any **5xx** (500–599); and for
transient **transport exceptions** with no HTTP response — `HttpRequestException`, and a
`TaskCanceledException`/`TimeoutException` that is **not** triggered by the caller's
`CancellationToken` (i.e. an HttpClient timeout). It returns false for every **non-429 4xx** and for
2xx/3xx. A caller-initiated `OperationCanceledException` is **never** handled (it propagates as
cancellation, not a retry).

**Rationale**: Directly encodes FR-001/FR-002/FR-003 and the constitution's retryable set. 429 is
explicitly retryable and distinct from permanent 4xx (FR-003). FR-014 requires transient transport
failures to be retried like a 5xx; distinguishing an HttpClient timeout from user cancellation is the
one subtlety — the caller's token being canceled means "stop", an HttpClient-internal timeout means
"transient, retry". Polly evaluates `ShouldHandle` over `Outcome<HttpResponseMessage>` (result or
exception), so both arms live in one predicate.

**Alternatives considered**: retrying all 4xx (harmful per US2, amplifies load — rejected); treating
all `OperationCanceledException` as transient (would swallow user cancellation, violates FR-011 —
rejected).

## Decision 3 — Wait computation: Retry-After precedence, backoff+jitter, mandatory cap

**Decision**: Configure `RetryStrategyOptions<HttpResponseMessage>` with
`BackoffType = DelayBackoffType.Exponential`, `UseJitter = true`, a base `Delay`, a finite
`MaxRetryAttempts`, and a custom `DelayGenerator` that:
1. If the outcome is a response carrying a usable `Retry-After`, returns
   `min(retryAfter, MaxSingleWait)` — Retry-After takes precedence for that attempt (FR-004/FR-006§3).
2. Otherwise returns `null`, letting Polly use its internal exponential-with-jitter delay (FR-005/
   FR-006), with `MaxDelay = MaxSingleWait` capping that computed delay.

Because Polly's `MaxDelay` does **not** automatically clamp a value returned from `DelayGenerator`,
the **cap is applied explicitly** inside the generator for the Retry-After branch (FR-015). Net
effect: *every* single wait is `≤ MaxSingleWait`, and a Retry-After wait is `≥` the requested value
only up to that cap ("at least … up to the cap", per FR-015/spec assumptions).

**`Retry-After` parsing**: read `HttpResponseMessage.Headers.RetryAfter` (`RetryConditionHeaderValue`):
- `Delta` (a `TimeSpan?`) → delta-seconds form, used directly when `> 0`.
- `Date` (a `DateTimeOffset?`) → HTTP-date form → relative wait = `Date - TimeProvider.GetUtcNow()`;
  a past/non-positive result yields **no** enforced wait (fall back to computed backoff), per FR-004.

**Rationale**: Matches FR-004/005/006/015 exactly and uses Polly's documented `DelayGenerator`
(return `null` ⇒ "use internal delay"; return a value ⇒ override). Exponential+jitter is the
constitution-mandated shape. The explicit clamp closes the `MaxDelay`-vs-`DelayGenerator` gap so a
far-future HTTP-date or hostile delta cannot stall a send (the spec's named edge case).

**Strictly-increasing guarantee (SC-005)**: with exponential base growth and **bounded additive
jitter** (jitter kept a fraction of the step so step *n+1* always exceeds the jittered step *n*),
later waits are strictly greater than earlier ones while still non-constant. Tests assert this on the
**recorded** wait sequence under a seeded/recording `TimeProvider`, so the property is deterministic
rather than probabilistic.

**Alternatives considered**: relying solely on `MaxDelay` for the cap (insufficient — does not clamp
DelayGenerator output → rejected); Polly's decorrelated-jitter (`DelayBackoffType` V2 style) which is
not monotonic and could violate the strict-increase in SC-005 (rejected in favor of bounded additive
jitter on an exponential base).

## Decision 4 — Deterministic, offline timing via injectable `TimeProvider`

**Decision**: Build the pipeline with `ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = <injected> }`. Register `TimeProvider.System` in DI by default
(`services.TryAddSingleton(TimeProvider.System)`), inject it into `MailgunResilienceHandler`. Tests
register a **`RecordingTimeProvider`** that completes each scheduled delay immediately and records its
requested duration.

**Rationale**: FR-013/SC-008 require deterministic offline verification with no real clock. Polly v8
routes all delays through its `TimeProvider`, so a recording/advancing provider makes waits both
instantaneous and observable — the only way to assert "increase + jitter + ≥ Retry-After + ≤ cap"
without sleeping or flakiness. `TimeProvider` is in-box on `net8.0`; on `netstandard2.0` it is
supplied transitively by Polly (`Microsoft.Bcl.TimeProvider`), so no new top-level dependency.

**Alternatives considered**: tiny real delays + wall-clock assertions (slow, flaky, can't prove
ordering reliably — rejected); a bespoke internal clock abstraction (reinvents `TimeProvider`, more
surface — rejected).

## Decision 5 — Exhaustion observability (FR-008) via `ILogger`

**Decision**: Resolve `ILogger<MailgunResilienceHandler>` (or a dedicated category) from DI in the
handler. Count retries via the retry strategy's `OnRetry`; after `ExecuteAsync` returns, if the final
outcome is still a handled failure (a retryable status response, or a rethrown transient transport
exception), emit a single **Warning** "retries exhausted" record including the final status/exception
type and the number of attempts made — **never** the sending key or request body. When no logging is
configured, the default `ILoggerFactory`/`NullLogger` makes this a no-op.

**Rationale**: FR-008 requires an observable record on exhaustion via "the standard logging
abstraction" — `ILogger`, available transitively through `Microsoft.Extensions.Http`. Polly has no
built-in "exhausted" event, so detecting it in the handler (final outcome still failing after the
budget) is the deterministic, testable approach; a test asserts the record using a captured
`ILogger`/`ILoggerProvider`. Security (Principle V): only status + attempt count are logged.

**Alternatives considered**: Polly telemetry/`ResilienceStrategyTelemetry` (heavier, harder to assert
deterministically — rejected); throwing a distinct "exhausted" exception (violates the single-
`MailgunnerException` contract, FR-009/Principle IV — rejected).

## Decision 6 — Configuration surface and defaults

**Decision**: Add a public, XML-documented `RetryPolicyOptions` (max attempts, base delay, max single
wait, jitter toggle) exposed as a nested `Retry` property on `MailgunnerOptions`, all with
constitution-aligned **defaults** so existing `AddMailgunner` calls are unaffected and retry is **on**
by default. Defaults: a small finite budget (≈3 retries / 4 attempts), a sub-second base delay, and a
mandatory single-wait cap on the order of tens of seconds. Validate in `MailgunnerOptionsValidator`
(non-negative attempts, positive base delay, cap ≥ base delay).

**Rationale**: Principle II says resilience is the default behavior; spec assumptions leave exact
counts/delays/jitter as tuning details provided the observable properties hold. Additive, defaulted
options are SemVer-safe (Principle IV) and keep all prior tests/usages valid. The exact numeric
defaults are confirmed in `data-model.md`.

**Alternatives considered**: hard-coded constants with no knobs (less flexible, but acceptable — the
options approach is preferred for operability and matches the existing `MailgunnerOptions` pattern);
separate DI extension for retry (unnecessary surface — rejected).

## Decision 7 — Cancellation (FR-011)

**Decision**: Flow the caller's `CancellationToken` from `HttpClient.PostAsync` → handler →
`pipeline.ExecuteAsync(..., cancellationToken)`. Polly observes the token for both the inner send and
the inter-attempt delay, abandoning a pending wait promptly with `OperationCanceledException`.

**Rationale**: FR-011 requires waits to be cancelable and cancellation to surface rather than hang.
This is native Polly v8 behavior; a test cancels during a recorded/pending wait and asserts prompt
`OperationCanceledException` with no further attempts. Consistent with the existing
`CancellationTests` pattern.

## Decision 8 — `netstandard2.0` parity

**Decision**: Keep multi-targeting. Use `#if NET8_0_OR_GREATER` only where an API differs between
targets; rely on Polly's transitive `Microsoft.Bcl.TimeProvider` to provide `TimeProvider` on
`netstandard2.0`. No behavioral divergence between targets.

**Rationale**: Principle I/IV require both targets to build warning-free; Polly 8.x supports
`netstandard2.0` and brings the `TimeProvider` shim itself, so the seam works identically on both.

## Resolved unknowns

| Item | Resolution |
|------|-----------|
| Polly wiring without extra packages | Hand-written `DelegatingHandler` + `AddHttpMessageHandler` + `ResiliencePipeline<HttpResponseMessage>` |
| Retryable set | 429, 408, 5xx, transient transport exceptions; non-429 4xx never retried; user-cancel never retried |
| Retry-After forms | delta-seconds + HTTP-date (relative; past/non-positive ⇒ none), clamped to the cap |
| Single-wait cap | `MaxDelay` for computed backoff + explicit clamp in `DelayGenerator` for Retry-After (FR-015) |
| Strict increase + jitter | exponential base + bounded additive jitter; asserted on recorded waits |
| Deterministic timing | injectable `TimeProvider` + test `RecordingTimeProvider` |
| Exhaustion record | Warning via `ILogger` from the handler when budget spent; status + attempt count only |
| Config | additive defaulted `RetryPolicyOptions` on `MailgunnerOptions`, validated at startup |
| Cancellation | caller token flows into `ExecuteAsync`; pending wait abandoned promptly |
| netstandard2.0 | supported; `TimeProvider` via Polly's transitive `Microsoft.Bcl.TimeProvider` |
