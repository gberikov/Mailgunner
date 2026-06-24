---
description: "Task list for Automatic Retry with Backoff"
---

# Tasks: Automatic Retry with Backoff

**Input**: Design documents from `/specs/009-retry-backoff/`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/retry-behavior-contract.md ✅, quickstart.md ✅

**Tests**: INCLUDED. The constitution's Principle III (Test-First, Network-Free) is NON-NEGOTIABLE, and the spec mandates deterministic offline verification (FR-013, SC-008). Every behavioral slice ships with tests written before implementation.

**Organization**: Tasks are grouped by user story (P1→P3). Because this feature is a single cross-cutting `DelegatingHandler`, the shared mechanism (classification helper, options, DI wiring, test fakes, pipeline skeleton) is built once in **Foundational**; each user story then adds its observable behavior slice and proves it with an independently runnable test suite.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US5 maps to the spec's user stories

## Path Conventions

Single class-library + test project (per plan.md):
- Library: `src/Mailgunner/`
- Tests: `tests/Mailgunner.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make the permitted Polly dependency available to the library.

- [X] T001 Add `<PackageReference Include="Polly" />` (centrally versioned via `Directory.Packages.props`, 8.7.0) to `src/Mailgunner/Mailgunner.csproj`; run `dotnet restore` and confirm the library still builds clean for `net8.0` and `netstandard2.0` (warnings-as-errors).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the configuration surface, validation, deterministic test seam, the pure classification helper, the resilience handler skeleton, and the DI wiring that ALL user stories depend on.

**⚠️ CRITICAL**: No user story slice can be implemented or tested until this phase is complete.

- [X] T002 [P] Create public `RetryPolicyOptions` in `src/Mailgunner/RetryPolicyOptions.cs` with XML-documented, defaulted properties: `MaxRetryAttempts` (int, default 3, ≥0), `BaseDelay` (TimeSpan, default 500ms, >0), `MaxSingleWait` (TimeSpan, default 30s, ≥BaseDelay), `UseJitter` (bool, default true). (data-model.md §Public configuration)
- [X] T003 Add additive `Retry` property (`RetryPolicyOptions Retry { get; set; } = new();`, never null, XML-documented) to `src/Mailgunner/MailgunnerOptions.cs`; leave all existing properties unchanged.
- [X] T004 Add retry validation to `src/Mailgunner/Internal/MailgunnerOptionsValidator.cs` with secret-safe messages: fail when `Retry.MaxRetryAttempts < 0`, `Retry.BaseDelay <= TimeSpan.Zero`, or `Retry.MaxSingleWait < Retry.BaseDelay`.
- [X] T005 [P] Create `tests/Mailgunner.Tests/Fakes/RecordingTimeProvider.cs`: a `TimeProvider` that completes each scheduled delay immediately while recording every requested duration (exposed as an ordered list) and supports a controllable `GetUtcNow()` for HTTP-date math.
- [X] T006 [P] Extend `tests/Mailgunner.Tests/Fakes/StubHttpMessageHandler.cs` (preserving the existing `ResponseSelector` / per-index API) so a chosen response can carry a `Retry-After` header (delta-seconds or HTTP-date) and a chosen attempt index can throw a transient transport exception (e.g. `HttpRequestException`) instead of returning a response.
- [X] T007 [P] [US2] Write `tests/Mailgunner.Tests/Retry/RetryClassificationTests.cs` (TDD — must FAIL first): unit-cover the pure helper — retryable status set (429/408/500–599 true; non-429 4xx and 2xx/3xx false), transient-transport vs caller-cancel distinction, Retry-After parse for both forms incl. past/non-positive ⇒ none, and cap = `min(wait, MaxSingleWait)`.
- [X] T008 [US2] Create internal `src/Mailgunner/Internal/RetryClassification.cs` with pure helpers: `IsRetryableStatus(int)`, `IsTransientTransport(Exception, CancellationToken)`, `ParseRetryAfter(RetryConditionHeaderValue?, DateTimeOffset now) → TimeSpan?`, `Cap(TimeSpan, TimeSpan maxSingleWait)`; make T007 pass. Use `#if NET8_0_OR_GREATER` only where APIs differ between targets.
- [X] T009 Create internal `src/Mailgunner/Internal/MailgunResilienceHandler.cs` (`DelegatingHandler`) skeleton: build a Polly v8 `ResiliencePipeline<HttpResponseMessage>` via `ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = <injected> }`, execute `base.SendAsync` through `pipeline.ExecuteAsync(..., cancellationToken)` flowing the caller's token, and accept injected `TimeProvider`, `RetryPolicyOptions`, and `ILogger<MailgunResilienceHandler>`. Leave `ShouldHandle`/`DelayGenerator`/`OnRetry` as minimal stubs to be filled by the user-story phases.
- [X] T010 Wire DI in `src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs`: `services.TryAddSingleton(TimeProvider.System)` and, on the existing `IHttpClientBuilder` from `AddHttpClient<IMailgunnerClient, MailgunnerClient>(...)`, call `AddHttpMessageHandler<MailgunResilienceHandler>()` so it runs above the primary handler; register the handler with DI.

**Checkpoint**: Library builds with retry options + validation, DI attaches a pass-through resilience handler (no behavior change yet), pure classification is unit-tested green, and the offline test seam (recording clock + extended stub) is ready.

---

## Phase 3: User Story 1 - Survive a transient rejection without consumer retry code (Priority: P1) 🎯 MVP

**Goal**: 429 / 408 / 5xx status responses and transient transport failures are retried automatically and ultimately succeed; a first-attempt success makes exactly one attempt with zero waiting.

**Independent Test**: Feed sequenced responses (transient → success) through the fake transport and confirm the send succeeds with >1 attempt; feed a single 200 and confirm exactly 1 attempt and 0 recorded waits.

### Tests for User Story 1 (write FIRST, ensure they FAIL) ⚠️

- [X] T011 [P] [US1] Write `tests/Mailgunner.Tests/Retry/RetryOnTransientStatusTests.cs`: 429→200 and 5xx/408→200 ultimately succeed with ≥2 attempts and the consumer never sees the intermediate rejection (C1, C2, SC-001, SC-002).
- [X] T012 [P] [US1] Write `tests/Mailgunner.Tests/Retry/FirstAttemptSuccessTests.cs`: a single 200 ⇒ exactly one transport attempt, zero recorded waits, result identical to a non-retry success (C10, FR-010, SC-007).
- [X] T013 [P] [US1] Write `tests/Mailgunner.Tests/Retry/TransportFailureRetryTests.cs`: a transient transport exception then 200 is retried like a 5xx and succeeds (C11, FR-014).

### Implementation for User Story 1

- [X] T014 [US1] In `src/Mailgunner/Internal/MailgunResilienceHandler.cs`, implement `ShouldHandle` to return true for retryable statuses (via `RetryClassification.IsRetryableStatus`) and for transient transport exceptions (via `RetryClassification.IsTransientTransport`), false otherwise; set `MaxRetryAttempts` from options and a default exponential delay so retries actually occur. Make T011–T013 pass.

**Checkpoint**: MVP — transient turbulence is absorbed automatically; happy path adds zero waiting. Existing 002–008 suite still green.

---

## Phase 4: User Story 2 - Fail fast on permanent client errors (Priority: P1)

**Goal**: A non-429 4xx is never retried — it surfaces immediately as `MailgunnerException` after exactly one attempt with no wait.

**Independent Test**: Feed a single 400/401/404 and confirm `MailgunnerException` after exactly one attempt and zero recorded waits; feed a 429 and confirm it IS eligible for retry.

> Classification correctness (the pure helper + its unit test) was delivered in Foundational (T007/T008); this phase proves the observable end-to-end fail-fast behavior through the handler.

### Tests for User Story 2 (write FIRST, ensure they FAIL) ⚠️

- [X] T015 [P] [US2] Write `tests/Mailgunner.Tests/Retry/NoRetryOnPermanentClientErrorTests.cs`: non-429 4xx ⇒ `MailgunnerException(status, body)` after exactly one attempt and no wait; a 429 is treated as retryable (C3, FR-002, FR-003, SC-003).

### Implementation for User Story 2

- [X] T016 [US2] In `src/Mailgunner/Internal/MailgunResilienceHandler.cs`, confirm/adjust `ShouldHandle` so non-429 4xx (and 2xx/3xx) are NOT handled, leaving the single-attempt failure to surface unchanged via `MailgunnerClient.SendContentAsync`. Make T015 pass.

**Checkpoint**: US1 + US2 both hold — transient retried, permanent surfaces immediately.

---

## Phase 5: User Story 3 - Honor the server's requested wait (Priority: P2)

**Goal**: When a retryable response carries `Retry-After` (delta-seconds OR HTTP-date), the next wait is at least the requested duration, clamped to the mandatory `MaxSingleWait` cap.

**Independent Test**: Feed a retryable response with `Retry-After` then 200; confirm the recorded wait ≥ requested and ≤ cap; a huge/far-future value is clamped to the cap; an absent header falls back to computed backoff.

### Tests for User Story 3 (write FIRST, ensure they FAIL) ⚠️

- [X] T017 [P] [US3] Write `tests/Mailgunner.Tests/Retry/RetryAfterHonoredTests.cs`: `Retry-After` delta-seconds AND HTTP-date ⇒ recorded wait ≥ requested (≤ cap); a past HTTP-date / absent header ⇒ falls back to computed backoff (C4, C5, C6, FR-004, FR-005, SC-004).
- [X] T018 [P] [US3] Write `tests/Mailgunner.Tests/Retry/SingleWaitCapTests.cs`: an unusually large or far-future `Retry-After` ⇒ the wait is clamped to `MaxSingleWait`; the send cannot stall indefinitely (C14, FR-015).

### Implementation for User Story 3

- [X] T019 [US3] In `src/Mailgunner/Internal/MailgunResilienceHandler.cs`, implement the `DelayGenerator` Retry-After branch: when the outcome is a response with a usable Retry-After (via `RetryClassification.ParseRetryAfter` using the injected `TimeProvider`), return `RetryClassification.Cap(retryAfter, MaxSingleWait)`; otherwise return `null` to defer to computed backoff. Make T017–T018 pass.

**Checkpoint**: Server back-pressure is honored and bounded by the cap.

---

## Phase 6: User Story 4 - Spread out retries with increasing, jittered backoff (Priority: P2)

**Goal**: Without a Retry-After, successive waits grow (each later wait > earlier) and carry randomized jitter; a present Retry-After takes precedence for that attempt.

**Independent Test**: Feed several consecutive retryable responses through the fake transport and assert the recorded wait sequence is strictly increasing and not a single constant (jitter present).

### Tests for User Story 4 (write FIRST, ensure they FAIL) ⚠️

- [X] T020 [P] [US4] Write `tests/Mailgunner.Tests/Retry/BackoffIncreasesWithJitterTests.cs`: with a **fixed RNG seed** and a config whose waits stay below `MaxSingleWait`, several consecutive transients ⇒ the recorded waits are **strictly increasing**, and at least one wait exceeds its pure exponential base (jitter is observable, not bare exponential); and a Retry-After present on one attempt takes precedence over computed backoff for that attempt (C7, C8, FR-006, SC-005).

### Implementation for User Story 4

- [X] T021 [US4] In `src/Mailgunner/Internal/MailgunResilienceHandler.cs`, implement the fallback backoff **inside the `DelayGenerator`** (the same generator as the Retry-After branch) as **bounded additive jitter**: `base = options.Retry.BaseDelay * 2^(attemptNumber)`, then when `options.Retry.UseJitter` add a random `[0, JitterFraction) * base` with a constant `JitterFraction < 1` (e.g. `0.5`) so the minimum next wait (`base*2`) always exceeds the maximum current wait (`base*(1+JitterFraction)`) — strict monotonic increase holds for any draw — then clamp via `RetryClassification.Cap(..., options.Retry.MaxSingleWait)`. Do **NOT** use Polly's built-in `BackoffType = Exponential` + `UseJitter` (decorrelated jitter can make a later wait smaller and flake T020). Make the RNG **injectable/seedable** so T020 is deterministic. Make T020 pass.

**Checkpoint**: Retry cadence is desynchronized and strictly increasing, capped, with Retry-After precedence intact.

---

## Phase 7: User Story 5 - Make exhausted retries observable and surface the final error (Priority: P3)

**Goal**: After a bounded number of attempts the library gives up, surfaces the final `MailgunnerException` (last status + body), and emits an exhaustion log record; a pending wait is promptly cancelable.

**Independent Test**: Feed retryable responses on every attempt beyond the budget and confirm bounded attempts + final error + a captured Warning log record; cancel during a pending wait and confirm prompt `OperationCanceledException` with no further attempts.

### Tests for User Story 5 (write FIRST, ensure they FAIL) ⚠️

- [X] T022 [P] [US5] Write `tests/Mailgunner.Tests/Retry/RetryExhaustionTests.cs`: retryable on every attempt (status arm) beyond the budget ⇒ bounded finite attempts, final `MailgunnerException` carrying the LAST status+body, and a captured exhaustion Warning log record (status/attempt-count only, never the key or body) (C9, FR-007, FR-008, FR-009, SC-006).
- [X] T023 [P] [US5] Add transport-exhaustion coverage to `tests/Mailgunner.Tests/Retry/TransportFailureRetryTests.cs` (or a sibling): a transient transport exception on EVERY attempt ⇒ same finite budget + exhaustion behavior as C9 (C12, FR-014).
- [X] T024 [P] [US5] Write `tests/Mailgunner.Tests/Retry/RetryCancellationTests.cs`: cancelling during a pending backoff wait ⇒ prompt `OperationCanceledException`, no further attempts, no hang (C13, FR-011).

### Implementation for User Story 5

- [X] T025 [US5] In `src/Mailgunner/Internal/MailgunResilienceHandler.cs`, count attempts via `OnRetry` and, after `ExecuteAsync` returns with a still-failing handled outcome (retryable status or rethrown transient transport exception), emit a single `ILogger` Warning exhaustion record containing only the final status/exception-type and attempt count — never the sending key, `Authorization` header, or body; ensure the caller's `CancellationToken` flows into the pipeline so pending waits abandon promptly. Make T022–T024 pass.

**Checkpoint**: Give-up behavior is bounded, observable, and cancelable; all five stories independently verified.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, multi-target verification, and full-suite validation.

- [X] T026 [P] Update `README.md` to document automatic retry/backoff (on by default, the `Retry` tuning surface, Retry-After honored, non-429 4xx not retried).
- [X] T027 [P] Add an Unreleased entry to `CHANGELOG.md` noting automatic retry with exponential backoff + jitter, Retry-After support, mandatory single-wait cap, and exhaustion logging.
- [X] T028 Verify the library builds clean for BOTH `net8.0` and `netstandard2.0` (warnings-as-errors), confirming `TimeProvider` resolves transitively via Polly's `Microsoft.Bcl.TimeProvider` on `netstandard2.0`.
- [X] T029 Run the full suite (`dotnet test`) and confirm it is green offline with no network and no real waiting, including the unchanged 002–008 regression tests (first-attempt success, 4xx surfacing, batch chunking, cancellation, suppressions, webhook signature) per quickstart.md.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phases 3–7)**: All depend on Foundational. Test files (the `[P]` test tasks) are fully independent across stories. The single implementation tasks T014, T016, T019, T021, T025 all edit `MailgunResilienceHandler.cs`, so they must proceed **sequentially in priority order** (not in parallel with each other).
- **Polish (Phase 8)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Foundational only. Delivers the MVP retry mechanism.
- **US2 (P1)**: Foundational only; independently testable. Reuses the classification helper from Foundational.
- **US3 (P2)**: Foundational only (DelayGenerator Retry-After branch).
- **US4 (P2)**: Foundational only (DelayGenerator backoff branch). Logically complements US3 in the same generator but is independently testable.
- **US5 (P3)**: Foundational only (OnRetry + exhaustion log + cancellation).

### Within Each User Story

- Tests are written FIRST and must FAIL before the implementation task.
- The pure helper precedes the handler (delivered in Foundational).
- Story complete before moving to the next priority.

### Parallel Opportunities

- T002, T005, T006 in Foundational are `[P]` (different files); T007 (classification test) is `[P]` and precedes T008.
- All test-writing tasks within and across stories (T011–T013, T015, T017–T018, T020, T022–T024) are `[P]` — different files, no shared state.
- Polish T026 and T027 are `[P]` (different files).

---

## Parallel Example: User Story 1

```bash
# Write all US1 tests together (they must fail before T014):
Task: "Write RetryOnTransientStatusTests.cs in tests/Mailgunner.Tests/Retry/"
Task: "Write FirstAttemptSuccessTests.cs in tests/Mailgunner.Tests/Retry/"
Task: "Write TransportFailureRetryTests.cs in tests/Mailgunner.Tests/Retry/"
# Then implement T014 to make them pass.
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup (Polly reference).
2. Phase 2: Foundational (options, validation, fakes, classification + unit tests, handler skeleton, DI wiring).
3. Phase 3: User Story 1.
4. **STOP and VALIDATE**: transient → success and first-attempt success behave correctly; regression suite green.
5. Ship — retry-on-by-default resilience is live.

### Incremental Delivery

1. Setup + Foundational → resilience mechanism present (pass-through).
2. US1 → MVP transient recovery. Validate.
3. US2 → fail-fast on permanent errors. Validate.
4. US3 → honor Retry-After (capped). Validate.
5. US4 → increasing, jittered backoff. Validate.
6. US5 → bounded, observable, cancelable give-up. Validate.
7. Polish → docs + multi-target + full-suite verification.

---

## Notes

- `[P]` = different files, no dependencies on incomplete tasks.
- The five handler implementation tasks share one file by design (a single cross-cutting `DelegatingHandler`) — keep them sequential in priority order; only the test files parallelize freely.
- Retry is on by default; all changes are additive and SemVer-safe (Principle IV).
- Never log the sending key, `Authorization` header, or request body (Principle V).
- Verify each story's tests fail before implementing; commit after each task or logical group.
