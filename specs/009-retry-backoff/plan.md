# Implementation Plan: Automatic Retry with Backoff

**Branch**: `009-retry-backoff` (feature dir `009-retry-backoff`) | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/009-retry-backoff/spec.md`

## Summary

Make the library absorb transient Mailgun turbulence automatically: retry on **HTTP 429, 408,
and any 5xx**, plus transient **transport-level** failures with no HTTP response (timeout,
connection reset/refused, DNS failure); **never** retry a non-429 4xx (it surfaces immediately
after one attempt). Each retry waits with **exponential backoff plus jitter**, but a
**`Retry-After`** header on a retryable response (delta-seconds **or** HTTP-date) takes precedence
for that attempt; every single wait is **capped** at a mandatory upper bound so a hostile or
far-future value cannot stall a send. The retry budget is **finite**; when it is exhausted the
final failure surfaces (carrying the last status + body via the existing single `MailgunnerException`
contract) and an **exhaustion log record** is emitted. Waits are **cancelable** — the caller's
`CancellationToken` abandons a pending wait promptly. An eventual success is **indistinguishable**
from a first-attempt success.

This feature finally wires in the resilience that constitution **Principle II** mandates and that
features 003–007 deferred. `Polly` (8.7.0) is already in the permitted dependency catalog
(Principle I) but not yet referenced by the library; this plan references it for the first time.
Because the permitted catalog does **not** include `Microsoft.Extensions.Http.Polly` or
`Microsoft.Extensions.Http.Resilience`, the integration is a **hand-written `DelegatingHandler`**
that executes the request through a Polly v8 `ResiliencePipeline<HttpResponseMessage>` and is
attached with `IHttpClientBuilder.AddHttpMessageHandler` (which **is** part of
`Microsoft.Extensions.Http`). The handler sits **above** the primary handler, so the existing test
seam — `AddMailgunner(...).ConfigurePrimaryHttpMessageHandler(() => stub)` — keeps working unchanged
and the suppressions path (same typed `HttpClient`) inherits retry for free. Delays use an injectable
`TimeProvider` so the timing properties (increase, jitter, Retry-After precedence, cap, cancellation)
are verified **deterministically and offline** with the existing fake-transport approach, no real
clock and no network.

## Technical Context

**Language/Version**: C# (`LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`) — inherited from `Directory.Build.props`.

**Primary Dependencies**: Adds the first reference to **`Polly` 8.7.0** (already pinned in
`Directory.Packages.props`, explicitly permitted by Principle I). No package outside the permitted
catalog is added: the HTTP wiring uses `AddHttpMessageHandler` from the already-referenced
`Microsoft.Extensions.Http`; `ILogger` for the exhaustion record uses
`Microsoft.Extensions.Logging.Abstractions` (transitive via `Microsoft.Extensions.Http`, no new
top-level dependency); `TimeProvider` is in-box on `net8.0` and provided to `netstandard2.0` as a
transitive dependency of Polly (`Microsoft.Bcl.TimeProvider`). `System.Text.Json` is unchanged.
**Not added:** `Microsoft.Extensions.Http.Polly`, `Microsoft.Extensions.Http.Resilience`.

**Storage**: N/A (stateless library; retry state lives only for the duration of a single call).

**Testing**: xUnit, fully offline. The existing `StubHttpMessageHandler` already returns
sequenced responses by request index (`ResponseSelector`) — the core seam for "429 then 200". It is
extended to (a) attach a `Retry-After` header to a chosen response and (b) optionally throw a
transient transport exception for a chosen attempt. A test-only **recording `TimeProvider`**
completes each scheduled delay immediately while recording its requested duration, so the wait
sequence is asserted instantly (increasing, jittered, ≥ Retry-After, ≤ cap) with zero wall-clock
time. Tests build a real `ServiceCollection` via `AddMailgunner(...)`, override the primary handler
and the `TimeProvider`, resolve `IMailgunnerClient`, and assert behavior and attempt counts.

**Target Platform**: Cross-platform .NET. Library multi-targets `net8.0` and `netstandard2.0`; tests run on `net8.0`.

**Project Type**: Single class-library + test project (NuGet-distributable library).

**Performance Goals**: N/A beyond correctness. The happy path (first attempt succeeds) adds one
pass-through `DelegatingHandler` and **zero** waiting. Worst case is bounded by the finite retry
budget and the per-wait cap.

**Constraints**: Offline/deterministic tests; warnings-as-errors; XML docs on every public member;
English-only; multi-target (`net8.0` + `netstandard2.0`); single typed error (`MailgunnerException`)
preserved; every public async method already accepts a `CancellationToken` and uses
`ConfigureAwait(false)`; no secret in code/tests/fixtures.

**Key environment facts (verified against the 002–008 code):**
- DI registration is `services.AddHttpClient<IMailgunnerClient, MailgunnerClient>(...)` returning an
  `IHttpClientBuilder` (`MailgunnerServiceCollectionExtensions`). The resilience handler is attached
  to that same builder, so it composes with the existing base-URL + Basic-auth configuration.
- The test seam `ConfigurePrimaryHttpMessageHandler(() => stub)` sets the **primary** handler; a
  handler added via `AddHttpMessageHandler` runs **above** it, so all existing tests and the new
  retry tests share the same stub transport. No change to how tests build the client.
- `MailgunnerClient.SendContentAsync` already turns the final non-success response into
  `MailgunnerException((int)status, body)`. Retry happens **below** `HttpClient.PostAsync` (in the
  handler), so this method is **unchanged** except that the response it sees is the post-retry final
  one — the single-error contract (FR-009) is preserved with no edit to the send path.
- `MailgunSuppressions` shares the same typed `HttpClient`, so it inherits retry automatically
  (matches the spec assumption: policy applies to all outbound requests, not only sends).
- The repo already bridges TFM gaps with `#if NET8_0_OR_GREATER` (e.g. `Guard`,
  `ReadAsStringAsync`); the same pattern covers any `TimeProvider`/API differences between targets.
- `MailgunnerOptions` is the established configuration surface, validated at startup by
  `MailgunnerOptionsValidator`. Retry tuning is added here with safe defaults so every existing
  `AddMailgunner` call keeps compiling and behaving identically (retry is on by default per the
  constitution).
- The library does not log today; the exhaustion record is the first use of `ILogger`, resolved from
  DI (`ILogger<T>` / `ILoggerFactory`), with a no-op when none is registered (factory default).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Constitution v1.1.0 (2026-06-22). Gates derived from its principles:

| Principle | Gate for this feature | Status |
|-----------|------------------------|--------|
| I. Minimal Dependencies & Modern .NET | Adds a reference to **`Polly`**, which is one of the **three explicitly permitted** runtime dependencies and is already pinned in the catalog — this is its intended first use, not a new-dependency decision. No package outside `{System.Text.Json, Polly, Microsoft.Extensions.Http}` is added; `Microsoft.Extensions.Http.Polly`/`.Resilience` are deliberately **avoided**. `ILogger`/`TimeProvider` come in-box or transitively. `System.Text.Json` remains the only serializer. | ✅ PASS |
| II. Managed HTTP & Resilience | This feature **is** the literal fulfillment of Principle II: "Transient-fault handling MUST be provided by Polly: retry on HTTP 429, 408, and 5xx … exponential backoff with jitter, and the `Retry-After` header MUST be honored." All HTTP still flows through the `IHttpClientFactory` typed client; the library does not construct `HttpClient`. Every public async method already takes a `CancellationToken` and uses `ConfigureAwait(false)`, and the retry wait honors that token. **Closes the resilience deferral** carried since features 003–007. | ✅ PASS (fulfills) |
| III. Test-First, Network-Free (NON-NEGOTIABLE) | Entirely offline via the existing fake `HttpMessageHandler`; the recording `TimeProvider` removes real waits. New/changed behavior lands with tests; the deterministic-fake-transport requirement (FR-013/SC-008) is met by construction. | ✅ PASS |
| IV. Documented, Strict Public API | Final failure still surfaces as exactly one `MailgunnerException` carrying status + body (FR-009) — no new exception type. New public surface is limited to additive retry-tuning options on `MailgunnerOptions` (XML-documented), all with defaults → SemVer-safe additive change; CHANGELOG (Unreleased) + README updated. Warnings-as-errors respected. | ✅ PASS |
| V. Security & Scope Discipline | No secret added; retry never logs the sending key or request body (exhaustion record carries only status/attempt count). Stays within v1 scope (messages/suppressions/webhooks) — it governs the shared HTTP path, adds no endpoint. | ✅ PASS |
| Mailgun API Fidelity | Honors the Mailgun/HTTP contract for transient handling: 429 (rate limit) + 408 + 5xx retryable, `Retry-After` respected (both forms). No out-of-scope endpoint touched. | ✅ PASS |
| Dev Workflow & Quality Gates | Conventional Commits; `dotnet build`/`dotnet test` green offline without credentials; no secret committed. | ✅ PASS |

**Result:** **No deviations.** This feature *satisfies* a previously-deferred principle rather than
straining any. Complexity Tracking is empty.

**Post-Phase-1 re-check:** The design adds one internal `DelegatingHandler`, a small internal
backoff/Retry-After computation helper, additive options + their validation, one DI line wiring the
handler and a default `TimeProvider`, and tests. It modifies no public type signature, adds no
dependency beyond the permitted `Polly`, introduces no new exception type, and preserves the existing
test seam. No principle status changes. Gate still passes.

## Project Structure

### Documentation (this feature)

```text
specs/009-retry-backoff/
├── plan.md                       # This file (/speckit-plan output)
├── research.md                   # Phase 0 output (decisions: hand-rolled DelegatingHandler + Polly v8 pipeline;
│                                  #   ShouldHandle set; DelayGenerator w/ Retry-After + cap; TimeProvider seam;
│                                  #   exhaustion-logging seam; options defaults; cancellation; netstandard2.0 notes)
├── data-model.md                 # Phase 1 output (RetryPolicyOptions, classification, wait computation, budget, records)
├── quickstart.md                 # Phase 1 output (validation/run guide; how each acceptance scenario is proven offline)
├── contracts/
│   └── retry-behavior-contract.md# Phase 1 output (observable behavior per condition; options surface; logging contract)
├── checklists/
│   └── requirements.md           # Spec quality checklist (from /speckit-specify, re-validated by /speckit-clarify)
└── tasks.md                      # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
└── Mailgunner/
    ├── Mailgunner.csproj                         # CHANGED: add <PackageReference Include="Polly" /> (permitted; centrally versioned).
    ├── MailgunnerOptions.cs                       # CHANGED: add additive, defaulted retry-tuning surface
    │                                              #   (max attempts, base delay, max single-wait cap, jitter toggle),
    │                                              #   e.g. a nested `Retry` RetryPolicyOptions object. Existing props unchanged.
    ├── RetryPolicyOptions.cs                      # NEW (public): tunable retry knobs with constitution-aligned defaults + XML docs.
    ├── DependencyInjection/
    │   └── MailgunnerServiceCollectionExtensions.cs # CHANGED: register default TimeProvider (TryAdd); on the IHttpClientBuilder,
    │                                              #   AddHttpMessageHandler<MailgunResilienceHandler>() above the primary handler.
    └── Internal/
        ├── MailgunResilienceHandler.cs           # NEW (internal DelegatingHandler): builds a Polly v8
        │                                          #   ResiliencePipeline<HttpResponseMessage> (ShouldHandle 429/408/5xx + transient
        │                                          #   transport exceptions; DelayGenerator: Retry-After→clamp-to-cap else
        │                                          #   exponential+jitter via TimeProvider; MaxRetryAttempts; OnRetry count) and
        │                                          #   executes SendAsync through it. Emits the exhaustion log record when the
        │                                          #   budget is spent. Honors the caller's CancellationToken for cancelable waits.
        └── RetryClassification.cs                 # NEW (internal): pure helpers — is-status-retryable (429/408/5xx),
                                                   #   is-transient-transport-exception, parse Retry-After (delta-seconds + HTTP-date
                                                   #   → relative, non-positive ⇒ none), and cap a single wait. Unit-testable in isolation.

    # (002–008 files unchanged: client/interface, send & suppression paths, MailgunnerException, EmailAddress,
    #  region, Guard, JSON contexts, webhook signature — none of their behavior changes.)

tests/
└── Mailgunner.Tests/
    ├── Fakes/
    │   ├── StubHttpMessageHandler.cs              # CHANGED: per-index response may carry a Retry-After header; per-index option to
    │   │                                          #   throw a transient transport exception (no HTTP response). Existing API preserved.
    │   └── RecordingTimeProvider.cs               # NEW: TimeProvider that completes scheduled delays immediately and records each
    │                                              #   requested duration, so wait sequences are asserted offline with zero real time.
    └── Retry/
        ├── RetryOnTransientStatusTests.cs         # NEW US1 (SC-001/002): 429→200 and 5xx/408→200 ultimately succeed; >1 attempt made.
        ├── NoRetryOnPermanentClientErrorTests.cs  # NEW US2 (SC-003): non-429 4xx → MailgunnerException after exactly one attempt, no wait.
        ├── RetryAfterHonoredTests.cs              # NEW US3 (SC-004): Retry-After (delta-seconds AND HTTP-date) ⇒ wait ≥ requested, ≤ cap;
        │                                          #   absent ⇒ falls back to computed backoff.
        ├── BackoffIncreasesWithJitterTests.cs     # NEW US4 (SC-005): consecutive transients ⇒ later waits > earlier; jitter observable;
        │                                          #   Retry-After takes precedence over computed backoff for that attempt.
        ├── RetryExhaustionTests.cs                # NEW US5 (SC-006): transients beyond budget ⇒ bounded attempts, final error carries last
        │                                          #   status+body, exhaustion log record emitted.
        ├── TransportFailureRetryTests.cs          # NEW FR-014: timeout/connection/DNS-style transport exception retried like a 5xx;
        │                                          #   every-attempt transport failure ⇒ same budget + exhaustion behavior.
        ├── RetryCancellationTests.cs              # NEW FR-011: cancel during a pending wait ⇒ prompt OperationCanceledException, not a hang.
        ├── FirstAttemptSuccessTests.cs            # NEW FR-010/SC-007: first attempt OK ⇒ exactly one attempt, zero waiting.
        ├── SingleWaitCapTests.cs                  # NEW FR-015: huge/far-future Retry-After ⇒ wait clamped to the mandatory cap.
        └── RetryClassificationTests.cs            # NEW: pure-unit coverage of RetryClassification (status set, Retry-After parsing both
                                                   #   forms incl. past/non-positive, transient-vs-user-cancel distinction, cap).

    # existing 002–008 tests remain and must still pass unchanged (the pass-through handler must not
    # alter first-attempt success, 4xx error surfacing, batch chunking, or cancellation semantics).
```

**Structure Decision**: Continue the established single-library layout and the existing DI + fake-
transport test seam. The resilience policy is isolated in one internal `DelegatingHandler`
(`MailgunResilienceHandler`) with a pure, separately unit-tested classification/wait helper
(`RetryClassification`), keeping the public surface to **additive, defaulted options**. Integrating
Polly v8 through a hand-written handler + `AddHttpMessageHandler` (rather than
`Microsoft.Extensions.Http.Polly`/`.Resilience`) is the choice that honors Principle I's exact
permitted-dependency list while still flowing all HTTP through the `IHttpClientFactory` typed client
per Principle II. The injectable `TimeProvider` is the seam that makes the spec's timing properties
verifiable deterministically and offline.

## Complexity Tracking

> No constitutional deviations for this feature — this section is intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | — | — |
