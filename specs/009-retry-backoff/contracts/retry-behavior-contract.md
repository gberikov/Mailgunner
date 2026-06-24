# Contract: Automatic Retry with Backoff

The "interface" for this feature is **observable behavior of the existing client surface** plus a
small additive options surface. No new public method is introduced on `IMailgunnerClient`; retry is a
cross-cutting behavior of every outbound request (sends and suppressions) through the shared typed
`HttpClient`.

## Public surface (additive)

```csharp
namespace Mailgunner;

/// Retry/backoff tuning. All values have constitution-compliant defaults; retry is on by default.
public sealed class RetryPolicyOptions
{
    public int MaxRetryAttempts { get; set; } = 3;          // retries after the first attempt; >= 0
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500); // > 0
    public TimeSpan MaxSingleWait { get; set; } = TimeSpan.FromSeconds(30);   // >= BaseDelay; caps EVERY wait
    public bool UseJitter { get; set; } = true;            // bounded additive jitter (a fraction < 1 of the base); waits stay strictly increasing
}

public sealed class MailgunnerOptions
{
    // ...existing Domain / SendingKey / Region unchanged...
    public RetryPolicyOptions Retry { get; set; } = new();  // never null
}
```

- **SemVer**: additive only — existing `AddMailgunner(...)` calls compile and behave unchanged
  (retry on by default).
- **Validation** (startup, secret-safe): `MaxRetryAttempts >= 0`; `BaseDelay > 0`;
  `MaxSingleWait >= BaseDelay`.

## Behavioral contract (per condition)

| # | Given the transport returns / does | Then (observable) | Reqs |
|---|------------------------------------|-------------------|------|
| C1 | 429 then a parseable 2xx | send succeeds; ≥ 2 attempts made; consumer never sees the 429 | FR-001, FR-003, FR-012, SC-001 |
| C2 | 5xx or 408 then a parseable 2xx | send succeeds; ≥ 2 attempts | FR-001, SC-002 |
| C3 | a non-429 4xx (400/401/403/404) | `MailgunnerException(status, body)` immediately; **exactly one** attempt; **no** wait | FR-002, FR-003, SC-003 |
| C4 | a retryable response with `Retry-After: <delta-seconds>` then 2xx | wait before next attempt ≥ requested seconds (≤ cap); then succeeds | FR-004, SC-004 |
| C5 | a retryable response with `Retry-After: <HTTP-date>` then 2xx | date converted to relative wait from now; wait ≥ that (≤ cap); a past date ⇒ fall back to computed backoff | FR-004 |
| C6 | a retryable response **without** `Retry-After` | wait uses computed exponential backoff (not a server value) | FR-005 |
| C7 | several consecutive retryable responses (waits below the cap) | each later wait **strictly** > an earlier wait under bounded additive jitter; waits are not a single constant (jitter present) | FR-006, SC-005 |
| C8 | a retryable response **with** `Retry-After` while computed backoff also applies | `Retry-After` takes precedence for that attempt (clamped to cap) | FR-004, FR-006§3, FR-015 |
| C9 | retryable responses on **every** attempt, beyond budget | bounded, finite attempts; final `MailgunnerException` carries the **last** status + body; an exhaustion **log record** is emitted | FR-007, FR-008, FR-009, SC-006 |
| C10 | first attempt is a parseable 2xx | **exactly one** attempt; **zero** waiting; result identical to non-retry success | FR-010, FR-012, SC-007 |
| C11 | a transient transport failure (timeout / connection reset/refused / DNS) then 2xx | retried like a 5xx with the same backoff; ultimately succeeds | FR-014 |
| C12 | transient transport failure on **every** attempt | same finite budget + exhaustion behavior as C9 | FR-014, FR-007, FR-008 |
| C13 | caller cancels during a pending backoff wait | `OperationCanceledException` surfaces **promptly**; no further attempts; no hang | FR-011, SC (cancellation) |
| C14 | `Retry-After` is unusually large or a far-future HTTP-date | the wait is clamped to `MaxSingleWait`; send cannot stall indefinitely | FR-015 |
| C15 | retry behavior under test | reproduced **offline** via fake transport + recording `TimeProvider`; zero network, zero real time | FR-013, SC-008 |

## Logging contract (exhaustion record — FR-008)

- **When**: exactly once, when the retry budget is spent and the final outcome is still a retryable
  failure (status response or transient transport exception).
- **Where**: `ILogger` (Warning level), category = the resilience handler type.
- **Contents**: final HTTP status (or transient-exception type) and the number of attempts made.
- **Never**: the sending key, the `Authorization` header, or the request body.
- **No logger configured**: behavior is unchanged except the record is a no-op (default
  `NullLoggerFactory`).

## Error contract (unchanged — FR-009 / Principle IV)

The only exception surfaced for a Mailgun failure remains `MailgunnerException`, exposing the HTTP
status code and raw response body of the **final** attempt. No new exception type is introduced for
retry, exhaustion, or transport failure.

## Non-goals (out of scope for this contract)

- No circuit breaker, hedging, bulkhead, or rate-limiter strategy (retry only).
- No per-call override of retry options (configured once via `MailgunnerOptions`).
- No change to request shape, routing, auth, batching, or the `SendResult` shape.
