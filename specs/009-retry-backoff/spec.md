# Feature Specification: Automatic Retry with Backoff

**Feature Branch**: `009-retry-backoff`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "When Mailgun temporarily rejects a request due to rate limiting or transient server errors, the library automatically retries with increasing backoff so consumers' sends succeed without bespoke retry code, while permanent client errors surface immediately rather than being retried. Bursty conference sends will hit rate limits, and silent, well-behaved retry is expected of a production-grade client. External constraints (requirements): rate-limit responses use HTTP 429 and may include a Retry-After header that must be respected; transient server failures are 5xx/408. Acceptance criteria: A 429 followed by success ultimately succeeds after a retry. When Retry-After is present, the wait honors it. Backoff increases between attempts and includes jitter. A non-429 4xx is not retried and surfaces immediately. Retry exhaustion is observable (logged) and surfaces the final error. Verified by simulating sequenced responses through a fake transport."

## Clarifications

### Session 2026-06-24

- Q: Should transport-level failures with no HTTP response (request timeout, connection reset/refused, DNS failure) be retried, in addition to 429/408/5xx status responses? → A: Yes — retry transient transport exceptions with the same backoff, treating them as a transient server failure.
- Q: Which Retry-After formats must the library understand — delta-seconds, HTTP-date, or both? → A: Both — delta-seconds and HTTP-date; an HTTP-date is converted to a relative wait from the current moment.
- Q: Should the per-wait upper bound be mandatory (MUST) rather than optional (MAY)? → A: Mandatory — a MUST cap on any single wait; the exact cap value is a planning detail.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Survive a transient rejection without consumer retry code (Priority: P1)

A consumer sends a message while Mailgun is momentarily over capacity or rate-limiting the
account. The request comes back as a temporary rejection (a rate-limit response, or a
transient server failure). The consumer wrote no retry logic of its own; instead, the
library quietly waits and re-sends the same request. When a following attempt succeeds, the
consumer receives the successful result as if the transient rejection had never happened.

**Why this priority**: This is the reason the feature exists — a production-grade client must
absorb the normal, expected turbulence of bursty sending so that consumers get a successful
result without writing bespoke retry code. A single transient rejection that is recovered
automatically delivers the entire core value.

**Independent Test**: Feed a sequenced set of responses through a simulated transport — a
temporary rejection followed by a success — and confirm the consumer's send ultimately
succeeds, with more than one attempt having been made against the transport.

**Acceptance Scenarios**:

1. **Given** the transport will return a rate-limit (HTTP 429) response and then a success,
   **When** the consumer sends a message, **Then** the send ultimately succeeds and the
   consumer is not exposed to the intermediate rejection.
2. **Given** the transport will return a transient server failure (HTTP 5xx or 408) and then
   a success, **When** the consumer sends a message, **Then** the send ultimately succeeds.
3. **Given** the very first attempt succeeds, **When** the consumer sends a message, **Then**
   exactly one attempt is made and no waiting occurs.

---

### User Story 2 - Fail fast on permanent client errors (Priority: P1)

A consumer sends a request that Mailgun rejects for a permanent, caller-side reason — a bad
request, an authentication failure, a not-found, or any other non-rate-limit client error.
Retrying such a request would only waste time and resend a request that can never succeed.
The library therefore does not retry it; the failure surfaces immediately so the consumer can
correct the problem.

**Why this priority**: Retrying permanent failures is actively harmful — it delays the error
the consumer needs to see and can amplify load. Correctly distinguishing "try again later"
from "this will never work" is as essential as the retry itself, so it ships together with
User Story 1.

**Independent Test**: Feed a single permanent client-error response (a 4xx that is not 429)
through the simulated transport and confirm the failure surfaces after exactly one attempt,
with no waiting and no further attempts.

**Acceptance Scenarios**:

1. **Given** the transport returns a non-429 client error (e.g. HTTP 400, 401, 404), **When**
   the consumer sends a message, **Then** the failure surfaces immediately, exactly one
   attempt is made, and no retry wait occurs.
2. **Given** the transport returns a rate-limit (429) response, **When** the consumer sends a
   message, **Then** the request IS eligible for retry (429 is treated as retryable, not as a
   permanent client error).

---

### User Story 3 - Honor the server's requested wait (Priority: P2)

When Mailgun rate-limits a request, it may tell the client exactly how long to wait before
trying again via a Retry-After indication. A well-behaved client respects that instruction
rather than guessing, so it neither hammers the service early nor waits longer than asked.

**Why this priority**: Retry already works without this (User Story 1), but honoring the
server's explicit back-pressure signal is what makes the client a good citizen under sustained
rate limiting and avoids escalating throttling. It refines the core retry behavior.

**Independent Test**: Feed a rate-limit response that carries a Retry-After value followed by a
success through the simulated transport, and confirm the wait before the next attempt reflects
the server-provided duration rather than the library's default backoff.

**Acceptance Scenarios**:

1. **Given** a rate-limit response that includes a Retry-After value followed by a success,
   **When** the consumer sends a message, **Then** the library waits for at least the
   server-requested duration before the next attempt, and then succeeds.
2. **Given** a retryable response that does NOT include a Retry-After value, **When** the
   consumer sends a message, **Then** the library falls back to its own increasing backoff
   wait.

---

### User Story 4 - Spread out retries with increasing, jittered backoff (Priority: P2)

When repeated transient rejections occur, the library does not retry on a fixed, synchronized
cadence. Each successive wait grows (so a struggling service is given more room), and each wait
carries randomized jitter (so many clients retrying at once do not all strike again at the same
instant). This prevents the library itself from contributing to a retry storm.

**Why this priority**: Increasing, jittered backoff is the safety property that keeps automatic
retry from making a bad situation worse at scale. It is a refinement on top of the basic retry
loop rather than a prerequisite for it.

**Independent Test**: Feed a sequence of several transient rejections through the simulated
transport and observe the computed waits between attempts: confirm later waits are larger than
earlier ones and that waits are not a single fixed constant (jitter is present).

**Acceptance Scenarios**:

1. **Given** multiple consecutive transient rejections, **When** the library retries, **Then**
   the wait before each later attempt is greater than the wait before an earlier attempt
   (backoff increases between attempts).
2. **Given** the backoff is computed for a given attempt, **When** the wait is determined,
   **Then** it includes a randomized jitter component rather than a single deterministic fixed
   value.
3. **Given** a server-provided Retry-After is present for an attempt, **When** the wait is
   determined, **Then** the Retry-After instruction takes precedence over the computed backoff
   for that attempt.

---

### User Story 5 - Make exhausted retries observable and surface the final error (Priority: P3)

A consumer's send keeps hitting transient rejections until the library reaches its retry limit.
At that point the library stops trying and surfaces the final failure to the consumer, and it
records that retries were exhausted so an operator can see it happened. The consumer is never
left with a silent, swallowed failure or an indefinite hang.

**Why this priority**: Bounded, observable give-up behavior matters for operability and trust,
but it only comes into play in the minority of cases where every retry fails. The happy-path
recovery (User Stories 1–4) delivers value first.

**Independent Test**: Feed a run of transient rejections longer than the retry budget through
the simulated transport, and confirm the consumer receives the final error after a bounded
number of attempts and that an observable record (log entry) of the exhaustion is produced.

**Acceptance Scenarios**:

1. **Given** the transport returns transient rejections on every attempt, **When** the retry
   budget is exhausted, **Then** the consumer receives the final failure (carrying the last
   response's status and body) and the library makes no further attempts.
2. **Given** retries were exhausted, **When** the final failure is surfaced, **Then** an
   observable log record of the exhaustion is emitted for operators.
3. **Given** any retry wait is pending, **When** the consumer cancels the operation, **Then**
   the wait is abandoned promptly and cancellation surfaces rather than the request hanging
   until the budget is spent.

---

### Edge Cases

- What happens when a 429 carries no Retry-After value? → The library uses its own increasing,
  jittered backoff for the wait.
- What happens when Retry-After is present but unusually large? → The server-requested wait is
  honored up to a mandatory upper-bound cap on a single wait, so a hostile or erroneous value
  (including a far-future HTTP-date) cannot stall a send indefinitely (see FR-015).
- What happens when the first attempt already succeeds? → No retry and no wait; exactly one
  attempt is made.
- What happens when a permanent client error (non-429 4xx) is returned? → Surfaced immediately,
  not retried.
- What happens when the consumer cancels during a backoff wait? → Cancellation is honored
  promptly; the operation does not block for the remaining backoff.
- What happens when an attempt fails at the transport level with no HTTP response (timeout,
  connection reset/refused, DNS failure)? → It is treated as a transient condition and retried
  with the same backoff as a transient server failure.
- What happens when every attempt fails transiently? → After a bounded number of attempts the
  library gives up, surfaces the final error, and logs that retries were exhausted.
- Does retry change the consumer-visible result shape on eventual success? → No; an eventual
  success looks identical to a first-attempt success.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The library MUST automatically retry a request when the response indicates a
  transient condition — a rate-limit response (HTTP 429) or a transient server failure
  (HTTP 408 or any 5xx) — without requiring the consumer to write any retry code.
- **FR-002**: The library MUST NOT retry a permanent client error — any 4xx response other than
  429 (e.g. 400, 401, 403, 404) — and MUST surface that failure immediately after a single
  attempt.
- **FR-003**: The library MUST treat HTTP 429 as a retryable rate-limit response, distinct from
  permanent 4xx client errors.
- **FR-004**: When a retryable response includes a Retry-After indication, the library MUST wait
  at least the server-requested duration before the next attempt, taking precedence over its own
  computed backoff for that attempt. The library MUST understand both Retry-After forms: a
  delta-seconds value and an HTTP-date value, converting an HTTP-date to a relative wait from the
  current moment (a past or non-positive value yields no enforced wait, falling back to computed
  backoff).
- **FR-005**: When a retryable response does not include a Retry-After indication, the library
  MUST wait using its own backoff that increases between successive attempts.
- **FR-006**: The backoff wait MUST grow across successive retry attempts and MUST include a
  randomized jitter component, so retries are not emitted on a fixed, synchronized cadence. The jitter
  MUST be bounded — added on top of the growing base backoff rather than a free-form randomization that
  could shrink a later wait below an earlier one — so that, until a wait is clamped to the single-wait
  cap (FR-015), each later wait remains strictly greater than an earlier one despite the jitter.
- **FR-007**: The library MUST bound the number of retry attempts to a finite limit; once the
  limit is reached it MUST stop retrying and surface the final failure to the consumer.
- **FR-008**: When retries are exhausted, the library MUST emit an observable record (a log
  entry) noting that retries were exhausted, in addition to surfacing the final error.
- **FR-009**: The final surfaced failure MUST carry the information from the last attempt (the
  HTTP status code and the raw response body), consistent with the library's single typed-error
  contract.
- **FR-010**: A request whose first attempt succeeds MUST be made exactly once, with no retry
  and no backoff wait.
- **FR-011**: Retry waiting MUST be cancelable: if the consumer cancels the operation during a
  backoff wait, the library MUST abandon the wait promptly and surface cancellation rather than
  blocking for the remaining wait.
- **FR-012**: An eventual success after one or more retries MUST be indistinguishable to the
  consumer from a first-attempt success (same result shape; intermediate rejections are not
  surfaced).
- **FR-013**: The retry behavior MUST be verifiable deterministically and offline by simulating
  a sequence of responses through a fake transport, with no dependency on the real Mailgun
  service or the network.
- **FR-014**: The library MUST also retry transient transport-level failures where no HTTP
  response is received — request timeout, connection reset/refused, or DNS resolution failure —
  applying the same increasing, jittered backoff used for a transient server failure. A
  transport failure on every attempt MUST be subject to the same finite retry budget (FR-007)
  and exhaustion behavior (FR-008/FR-009) as a transient status response.
- **FR-015**: Any single retry wait MUST be capped at a finite upper bound, so an unusually large
  or hostile Retry-After value (including a far-future HTTP-date) cannot stall a send
  indefinitely. Honoring "at least" the server-requested wait remains satisfied up to that cap;
  the exact cap value is a planning detail.

### Key Entities *(include if feature involves data)*

- **Retryable Response**: A response that signals a temporary condition — a rate-limit (429) or
  a transient server failure (408/5xx) — for which another attempt is warranted. A transient
  transport-level failure with no HTTP response (timeout, connection reset/refused, DNS failure)
  is treated as the same retryable condition.
- **Permanent Failure Response**: A non-429 4xx response indicating a caller-side problem that
  re-sending cannot fix; surfaced immediately without retry.
- **Retry-After Instruction**: An optional server-provided duration on a rate-limit response
  telling the client how long to wait before retrying; expressed either as delta-seconds or as an
  HTTP-date. When present it governs the next wait (an HTTP-date is interpreted relative to the
  current moment).
- **Backoff Wait**: The library-computed delay between attempts that increases with each
  successive attempt and carries randomized jitter; used when no Retry-After is provided.
- **Retry Budget**: The finite ceiling on attempts; once spent, the library stops and surfaces
  the final error.
- **Exhaustion Record**: The observable log entry produced when the retry budget is spent,
  signaling to operators that a send gave up after retrying.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A rate-limit (429) response followed by a success results in an eventual
  successful send in 100% of cases, with the consumer writing no retry code.
- **SC-002**: A transient server failure (408 or 5xx) followed by a success results in an
  eventual successful send in 100% of cases.
- **SC-003**: A non-429 4xx response is never retried — exactly one attempt is made — and the
  failure surfaces immediately in 100% of cases.
- **SC-004**: When a Retry-After value is present, the observed wait before the next attempt is
  at least the server-requested duration in 100% of cases.
- **SC-005**: Across a run of consecutive transient rejections whose waits stay below the single-wait
  cap, each later inter-attempt wait is strictly greater than an earlier one, and the waits are not a
  single constant value (the bounded jitter is observable).
- **SC-006**: A run of transient rejections longer than the retry budget results in a bounded,
  finite number of attempts, a surfaced final error carrying the last status and body, and an
  emitted exhaustion log record, in 100% of cases.
- **SC-007**: A first-attempt success triggers exactly one transport attempt and zero waiting in
  100% of cases.
- **SC-008**: The entire behavior is validated using only a simulated transport, with zero
  network requests, so the suite runs offline and deterministically.

## Assumptions

- The classification of retryable vs. permanent responses is fixed by the external Mailgun /
  HTTP contract and the project constitution: retry on 429, 408, and 5xx; do not retry other
  4xx. This is stated as a given requirement rather than a design choice.
- The retry policy applies to the library's outbound Mailgun requests generally (sends and other
  API calls that flow through the shared HTTP path), not solely to message sends; conference-burst
  sending is the motivating example, not a scope limit.
- A small, finite default retry budget (on the order of a few attempts) is appropriate for a
  transactional client; the exact count, base delay, and jitter range are tuning details left to
  the planning phase, provided the observable properties in this spec hold.
- A single backoff wait MUST be capped at a reasonable upper bound so that an unusually large or
  hostile Retry-After value cannot stall a send indefinitely; honoring "at least" the requested
  wait remains satisfied up to that cap (see FR-015). The exact cap value is a planning detail.
- "Observable (logged)" means emission through the standard logging abstraction already used by
  the library; no new external telemetry system is introduced by this feature.
- The fake-transport simulation reuses the project's existing network-free test approach (a fake
  HTTP message handler returning sequenced responses); no real Mailgun credentials or network
  access are required.
- Resilience is provided through the library's existing managed-HTTP/resilience mechanism (per
  the constitution), so this feature governs retry policy and observable behavior rather than
  introducing a new HTTP stack.
