# Quickstart & Validation: Automatic Retry with Backoff

A run/validation guide proving the feature works end-to-end **offline**. Design details live in
[plan.md](./plan.md), [data-model.md](./data-model.md), and
[contracts/retry-behavior-contract.md](./contracts/retry-behavior-contract.md).

## Prerequisites

- .NET 8 SDK (repo multi-targets `net8.0` + `netstandard2.0`; tests run on `net8.0`).
- No Mailgun credentials or network — everything is exercised through the fake transport.

## Consumer experience (what ships)

Retry is **on by default**; existing registrations get resilience for free:

```csharp
services.AddMailgunner("mg.example.com", sendingKey, MailgunRegion.Us);
// 429 / 408 / 5xx and transient transport failures are now retried automatically,
// Retry-After is honored, and a non-429 4xx still surfaces immediately.
```

Tuning (optional):

```csharp
services.AddMailgunner(o =>
{
    o.Domain = "mg.example.com";
    o.SendingKey = sendingKey;
    o.Region = MailgunRegion.Us;
    o.Retry.MaxRetryAttempts = 3;                       // finite budget
    o.Retry.BaseDelay = TimeSpan.FromMilliseconds(500);
    o.Retry.MaxSingleWait = TimeSpan.FromSeconds(30);   // mandatory cap on any single wait
    o.Retry.UseJitter = true;
});
```

## Build & test

```bash
dotnet build
dotnet test        # green offline, no credentials, no network, no real waiting
```

## Test seam (how it stays offline & deterministic)

- **Sequenced responses**: the existing `StubHttpMessageHandler.ResponseSelector` returns a different
  status/body per request index (e.g. index 0 → 429, index 1 → 200). Extended so a chosen response
  can carry a `Retry-After` header and a chosen attempt can throw a transient transport exception.
- **No real time**: a test `RecordingTimeProvider` completes each scheduled delay immediately and
  records its requested duration; the resilience pipeline is built with this `TimeProvider`, so wait
  sequences are asserted instantly.
- **Real DI**: tests use `AddMailgunner(...).ConfigurePrimaryHttpMessageHandler(() => stub)` and
  override the `TimeProvider` (and capture an `ILogger`) before `BuildServiceProvider()`. The
  resilience handler runs above the stub primary handler, so the wiring is exercised exactly as in
  production.

## Validation scenarios (map to acceptance criteria)

Run `dotnet test`; each scenario corresponds to a test under `tests/Mailgunner.Tests/Retry/`
(contract IDs from [the behavior contract](./contracts/retry-behavior-contract.md)).

| Scenario | Setup (fake transport) | Expected | Contract / Spec |
|----------|------------------------|----------|-----------------|
| Survive a 429 | 429 then 200 | succeeds; ≥ 2 attempts; no 429 surfaced | C1 / US1, SC-001 |
| Survive a 5xx/408 | 500 (or 408) then 200 | succeeds; ≥ 2 attempts | C2 / SC-002 |
| Fail fast on 4xx | single 400/401/404 | `MailgunnerException` at once; exactly 1 attempt; 0 waits | C3 / US2, SC-003 |
| Honor Retry-After (seconds) | 429 + `Retry-After: 2` then 200 | recorded wait ≥ 2s, ≤ cap; succeeds | C4 / US3, SC-004 |
| Honor Retry-After (HTTP-date) | 429 + future-date header then 200 | wait ≈ (date − now), ≤ cap; past date ⇒ computed backoff | C5 / US3 |
| Backoff increases + jitter | several consecutive 503s | recorded waits strictly increasing; not constant | C7 / US4, SC-005 |
| Retry-After precedence | 503 + Retry-After among retries | that wait uses Retry-After (clamped), not computed backoff | C8 / US4 |
| Exhaustion | retryable on every attempt, > budget | bounded attempts; final error has last status+body; exhaustion log emitted | C9 / US5, SC-006 |
| Transport retry | transient transport exception then 200 | retried like 5xx; succeeds | C11 / FR-014 |
| Transport exhaustion | transient transport exception every attempt | same budget + exhaustion behavior | C12 / FR-014 |
| Cancel during wait | retryable then cancel mid-wait | prompt `OperationCanceledException`; no further attempts | C13 / FR-011 |
| First-attempt success | single 200 | exactly 1 attempt; 0 waits | C10 / FR-010, SC-007 |
| Single-wait cap | 429 + huge / far-future Retry-After | wait clamped to `MaxSingleWait` | C14 / FR-015 |

## Regression guard

The new pass-through handler must not change existing behavior: the full 002–008 suite (first-attempt
success, 4xx error surfacing, batch chunking, cancellation, suppressions, webhook signature) must
stay green unchanged.

## Definition of done

- `dotnet build` and `dotnet test` green on `net8.0` (and library builds clean for
  `netstandard2.0`), offline, warnings-as-errors.
- Every scenario above passes; CHANGELOG (Unreleased) and README note automatic retry/backoff.
