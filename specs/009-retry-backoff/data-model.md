# Phase 1 Data Model: Automatic Retry with Backoff

This feature is behavioral; its "entities" are configuration values and the in-flight decision/wait
computations of one send. Nothing is persisted. Types below map the spec's Key Entities
(`spec.md` §Key Entities) to concrete shapes.

## Public configuration

### `RetryPolicyOptions` (new public class, `Mailgunner` namespace)

Tunable retry knobs with constitution-aligned defaults; XML-documented. Exposed via
`MailgunnerOptions.Retry`.

| Field | Type | Default | Rule / meaning | Source |
|-------|------|---------|----------------|--------|
| `MaxRetryAttempts` | `int` | `3` | Number of **retries** after the first attempt (so ≤ 4 total attempts). Must be `≥ 0` (`0` disables retry). Finite — bounds the budget. | FR-007 |
| `BaseDelay` | `TimeSpan` | `500 ms` | Starting backoff for the first retry; grows exponentially across attempts. Must be `> 0`. | FR-005, FR-006 |
| `MaxSingleWait` | `TimeSpan` | `30 s` | **Mandatory** upper bound on **any single** wait, including a `Retry-After`-driven one. Must be `≥ BaseDelay`. | FR-015 |
| `UseJitter` | `bool` | `true` | Adds a **bounded additive** random component (a fraction `< 1` of the current base) to each computed backoff so retries are not synchronized while remaining strictly increasing. | FR-006 |

> Defaults are the planning-detail tuning the spec leaves open; the observable properties
> (FR-001…FR-015, SC-001…SC-008) hold for any values satisfying the rules above. Retry is **on by
> default** (Principle II).

### `MailgunnerOptions` (changed)

Adds one additive property; all existing properties and behavior unchanged.

| Field | Type | Default | Meaning |
|-------|------|---------|---------|
| `Retry` | `RetryPolicyOptions` | `new RetryPolicyOptions()` | Retry/backoff tuning. Never null; defaults give constitution-compliant resilience with no extra configuration. |

**Validation** (`MailgunnerOptionsValidator`, secret-safe messages):
- `Retry.MaxRetryAttempts < 0` → fail.
- `Retry.BaseDelay <= TimeSpan.Zero` → fail.
- `Retry.MaxSingleWait < Retry.BaseDelay` → fail.

## Internal decision/computation model

### Retry classification (`RetryClassification`, internal pure helpers)

| Concept | Input | Output | Rule |
|---------|-------|--------|------|
| Retryable status | HTTP status code | `bool` | `true` iff `429`, `408`, or `500–599`. Maps **Retryable Response**. |
| Permanent failure | HTTP status code | `bool` | a non-429 4xx (`400–428, 430–499`) ⇒ not retryable. Maps **Permanent Failure Response**. |
| Transient transport | exception + caller token | `bool` | `true` for `HttpRequestException`, or `TaskCanceledException`/`TimeoutException` **not** caused by the caller's token (HttpClient timeout). `false` for caller-initiated cancellation. Maps **Retryable Response** (transport arm, FR-014). |
| Retry-After parse | `RetryConditionHeaderValue?` + now | `TimeSpan?` | `Delta` if `> 0`; else `Date - now` if `> 0`; else `null` (no enforced wait). Both forms; past/non-positive ⇒ none. Maps **Retry-After Instruction**. |
| Cap a wait | `TimeSpan` + `MaxSingleWait` | `TimeSpan` | `min(wait, MaxSingleWait)`. Enforces FR-015. |

### Wait computation (in `MailgunResilienceHandler` `DelayGenerator`) — **Backoff Wait** entity

For retry attempt *n* (1-based), the next wait is:

```
retryAfter = RetryClassification.ParseRetryAfter(response, now)
if retryAfter is not null:
    wait = min(retryAfter, MaxSingleWait)          # Retry-After precedence + cap (FR-004, FR-015)
else:
    base   = BaseDelay * 2^(n-1)                   # exponential growth (FR-005)
    jitter = UseJitter ? rng.NextDouble() * JitterFraction * base : 0   # bounded additive (FR-006)
    wait   = min(base + jitter, MaxSingleWait)     # cap (FR-015)
```

- The base grows with *n* (each later base is double the previous). The jitter is **bounded additive**:
  a random fraction in `[0, JitterFraction)` of the *current* base, with a constant `JitterFraction < 1`
  (e.g. `0.5`). Because the smallest possible wait for attempt *n+1* (`base·2`) exceeds the largest
  possible wait for attempt *n* (`base·(1 + JitterFraction)`), **each later wait is strictly greater
  than an earlier one regardless of the random draws**, until a wait is clamped to `MaxSingleWait`
  (SC-005, FR-006).
- Implemented with a **custom `DelayGenerator`** that computes `base + jitter` itself — **not** Polly's
  built-in `UseJitter` (decorrelated/AWS-style jitter, which can make a later wait smaller than an
  earlier one and would break the strict-increase property). The generator returns `min(retryAfter, cap)`
  for the Retry-After branch and the clamped `base + jitter` for the fallback branch. The RNG is
  injectable/seedable so the wait sequence is deterministic under test (T020).

### Retry Budget (entity)

The finite ceiling = `MaxRetryAttempts` retries (≤ `MaxRetryAttempts + 1` total attempts). When spent
and the outcome is still failing, the loop stops and the final outcome surfaces.

### Exhaustion Record (entity)

Emitted once when the budget is spent with a still-failing outcome:
- **Channel**: `ILogger` (Warning).
- **Payload**: final HTTP status (or transient-exception type) + attempt count. **Never** the sending
  key or request body (Principle V).
- Maps FR-008; no-op when no logger is configured.

## Surfaced result / error (unchanged contract)

| Outcome | Consumer sees |
|---------|---------------|
| Any attempt returns parseable 2xx | `SendResult` — indistinguishable from a first-attempt success (FR-010, FR-012). |
| Budget spent, last outcome a failing **response** | `MailgunnerException(lastStatus, lastBody)` (FR-009). Single typed error. |
| Budget spent, last outcome a transient **transport exception** | the final attempt's response/exception path yields `MailgunnerException` consistent with the existing send contract; transport failure does not introduce a new exception type. |
| Caller cancels during a wait | `OperationCanceledException` promptly; no further attempts (FR-011). |
| First attempt parseable 2xx | exactly one attempt, zero waiting (FR-010, SC-007). |

## State transitions (one send)

```
attempt → outcome?
  ├─ parseable 2xx ........................ SUCCESS (return SendResult)
  ├─ non-429 4xx ......................... FAIL NOW (MailgunnerException; 1 attempt; no wait)   [US2]
  ├─ 429/408/5xx OR transient transport .. budget left? ── yes ─→ compute wait → (cancelable) wait → attempt
  │                                                     └─ no ──→ log exhaustion → surface final error  [US5]
  └─ caller canceled ..................... OperationCanceledException (abandon wait)              [FR-011]
```
