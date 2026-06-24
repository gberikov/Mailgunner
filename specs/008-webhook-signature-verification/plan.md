# Implementation Plan: Webhook Signature Verification

**Branch**: `008-webhook-signature-verification` (feature dir `008-webhook-signature-verification`) | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/008-webhook-signature-verification/spec.md`

## Summary

Add the last v1 capability area — **webhook signature verification** — as a single, standalone,
**static pure** function with **no HTTP, no client, and no dependency injection** (per the
`/speckit-clarify` decision). A consumer that receives a Mailgun event webhook (bounce, complaint,
unsubscribe) extracts the three signed fields — `timestamp`, `token`, and `signature` — and calls
`MailgunWebhookSignature.Verify(signingKey, timestamp, token, signature)`, which returns `true`
only when the provided hex signature equals the HMAC-SHA256 of `timestamp + token` keyed by the
signing key. The comparison is **constant-time** (no short-circuit on the first differing
character), so timing cannot reveal how many leading characters matched. The signing key is a
caller-supplied **precondition** (blank/whitespace → `ArgumentException`); the three
webhook-supplied values are **untrusted input** and any null / empty / malformed / wrong-length /
non-hex signature yields a definite `false` and **never throws**. The HMAC primitive
(`System.Security.Cryptography.HMACSHA256`) and, on `net8.0`, the fixed-time comparison
(`CryptographicOperations.FixedTimeEquals`) are in-box on both target frameworks — **no new
dependency**. Because `CryptographicOperations` does **not** exist on `netstandard2.0`, that target
uses a small internal fixed-width XOR-accumulate comparison behind `#if NET8_0_OR_GREATER`. The
function touches **no existing `src/` file** (no client/interface change): it is delivered as one
new public static class plus private hex/compare helpers, exercised entirely offline by pure unit
tests with **no fake `HttpMessageHandler`** (there is no transport to fake). Replay/freshness and
request parsing remain the consumer's responsibility (out of scope, per spec).

## Technical Context

**Language/Version**: C# (`LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`) — inherited from `Directory.Build.props`.

**Primary Dependencies**: **No new direct dependency.** Verification uses
`System.Security.Cryptography.HMACSHA256` (in-box on both TFMs). Constant-time comparison uses
`System.Security.Cryptography.CryptographicOperations.FixedTimeEquals` on `net8.0`; on
`netstandard2.0` that type is unavailable, so an internal fixed-width XOR-accumulate helper is used
behind `#if NET8_0_OR_GREATER`. `System.Text.Json`, `Polly`, and `Microsoft.Extensions.Http` remain
present from earlier features but are **not used by this feature** (no JSON, no HTTP, no resilience
surface here).

**Storage**: N/A (library; pure function, no persistence, no state).

**Testing**: xUnit, fully offline and **transport-free** — this is the first feature with no HTTP
path, so no `StubHttpMessageHandler`/`CapturingHttpMessageHandler` is involved. Tests independently
compute a reference HMAC-SHA256 hex of `timestamp + token` with a test-local key and assert: a
matching signature verifies as authentic; a non-matching signing key fails; tampering with the
signature, the timestamp, or the token each fails; a signature that is empty, null, wrong-length,
or non-hexadecimal returns `false` without throwing; a blank/whitespace/null signing key throws
`ArgumentException`; a signature differing from the expected only at the **last** character returns
`false` (full-width comparison, not a first-mismatch short-circuit); and the function is callable
with nothing else from the library configured (no client, no DI, no `HttpClient`).

**Target Platform**: Cross-platform .NET. Library multi-targets `net8.0` and `netstandard2.0`; tests run on `net8.0`.

**Project Type**: Single class-library + test project (NuGet-distributable library).

**Performance Goals**: N/A. Verification is a single HMAC computation over a short message plus a
fixed-width byte comparison — O(signature length), constant memory.

**Constraints**: Offline, transport-free tests; warnings-as-errors; XML docs on every public
member; English-only; multi-target compatible; signing key supplied only by the caller and never
hard-coded/logged/stored; comparison MUST be constant-time (no first-mismatch short-circuit);
webhooks are explicitly **in v1 scope** per the constitution, which itself mandates exactly this
construction ("HMAC-SHA256 over `timestamp + token` with a constant-time comparison").

**Key environment facts (verified against the 002–007 code):**
- The public namespace is `Mailgunner`; internal helpers live in `Mailgunner.Internal`. This feature
  adds one public type in `Mailgunner` and keeps its hex/compare helpers private (no new internal
  public surface needed).
- `IMailgunnerClient`'s doc comment already names "webhooks" as a later capability, but the
  capability is intentionally **off the client**: it is a static pure function (clarified), so
  `IMailgunnerClient` / `MailgunnerClient` are **not modified**. No accessor is added.
- The repo already uses `#if NET8_0_OR_GREATER` to bridge target-framework API gaps (e.g.
  `Guard.NotNull`, `HttpContent.ReadAsStringAsync` overloads). The same pattern bridges the
  `CryptographicOperations.FixedTimeEquals` gap on `netstandard2.0`.
- `Guard.NotNull` exists for null-argument checks, but the signing-key precondition here is
  "null **or** blank/whitespace" → `ArgumentException`, so a small local check is used (consistent
  with the spec's "empty or missing signing key → configuration error" edge case). Webhook-supplied
  values are never guarded into exceptions — they fail closed to `false`.
- All API *failures* in the library surface as `MailgunnerException`, but that contract is for
  Mailgun **HTTP** responses; this feature issues no request and therefore has no `MailgunnerException`
  path. Argument validation via `ArgumentException` matches the existing send/suppression validation
  convention (thrown before any work).
- The signing key and message are UTF-8 encoded; Mailgun emits the signature as **lowercase** hex, so
  the expected value is produced as lowercase hex and compared exactly (documented assumption).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Constitution v1.1.0 (2026-06-22). Gates derived from its principles:

| Principle | Gate for this feature | Status |
|-----------|------------------------|--------|
| I. Minimal Dependencies & Modern .NET | Adds **no new dependency**. `HMACSHA256` and `CryptographicOperations.FixedTimeEquals` are in-box (BCL); the `netstandard2.0` gap for `FixedTimeEquals` is closed with an internal fixed-time helper, not a package. No `Newtonsoft.Json`; no JSON used at all. | ✅ PASS |
| II. Managed HTTP & Resilience | **Not applicable** — the feature performs no HTTP and exposes no async method, so the `HttpClient`/Polly/`CancellationToken`/`ConfigureAwait` rules do not engage. A synchronous pure function is the correct shape; there is nothing to make resilient. | ✅ PASS (N/A) |
| III. Test-First, Network-Free (NON-NEGOTIABLE) | Inherently network-free; no fake transport needed. The constitution explicitly lists "webhook signature validation (valid and invalid)" as required unit coverage — delivered here, with tests landing alongside the implementation. | ✅ PASS |
| IV. Documented, Strict Public API | One new public static class (`MailgunWebhookSignature`) with one documented method; XML docs on the type and member. Invalid signing key → standard `ArgumentException` (pre-condition), consistent with existing validation; no Mailgun-API error path exists, so the single-`MailgunnerException` rule is honored vacuously. Pre-1.0 additive surface, SemVer-safe; CHANGELOG (Unreleased) + README updated. | ✅ PASS |
| V. Security & Scope Discipline | This feature **is** the literal implementation of the principle's clause: "Webhook signature verification MUST use HMAC-SHA256 over `timestamp + token` with a constant-time comparison." Webhooks are in v1 scope. No secret in code/tests/fixtures; the key is caller-supplied and never logged/stored. Replay protection is correctly left to the consumer (documented). | ✅ PASS |
| Mailgun API Fidelity | Matches Mailgun's webhook signing scheme (HMAC-SHA256 of timestamp+token keyed by the HTTP webhook signing key, lowercase hex). No out-of-scope endpoint touched (no endpoint at all). | ✅ PASS |
| Dev Workflow & Quality Gates | Conventional Commits; `dotnet build`/`dotnet test` green offline; no secrets committed. | ✅ PASS |

**Result:** **No deviations.** Unlike features 003–007, the Principle II resilience deferral does
not apply here because the feature has no HTTP path. Complexity Tracking is therefore empty.

**Post-Phase-1 re-check:** The design adds exactly one public static class with one method and a
couple of private static helpers (hex-encode, fixed-time compare), plus a target-framework shim
behind `#if`. It modifies no existing type, adds no dependency, introduces no async/HTTP surface,
and adds no fake. No principle status changes. Gate still passes.

## Project Structure

### Documentation (this feature)

```text
specs/008-webhook-signature-verification/
├── plan.md                              # This file (/speckit-plan output)
├── research.md                          # Phase 0 output (decisions: static-pure surface, HMAC primitive,
│                                         #   constant-time on both TFMs, fail-closed input contract, hex casing)
├── data-model.md                        # Phase 1 output (inputs: signing key + signed triple; result; rules)
├── quickstart.md                        # Phase 1 output (validation/run guide; ASP.NET handler usage sketch)
├── contracts/
│   └── webhook-signature-contract.md    # Phase 1 output (public surface + per-input observable behavior)
├── checklists/
│   └── requirements.md                  # Spec quality checklist (from /speckit-specify, re-validated by /speckit-clarify)
└── tasks.md                             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
└── Mailgunner/
    └── MailgunWebhookSignature.cs       # NEW: public static class; `bool Verify(signingKey, timestamp, token, signature)`.
                                          #      Private helpers: lowercase-hex encode of HMAC bytes; constant-time compare
                                          #      (CryptographicOperations.FixedTimeEquals on net8.0, internal XOR-accumulate
                                          #      on netstandard2.0 behind #if NET8_0_OR_GREATER). UTF-8 key/message encoding.

    # (002–007 files entirely unchanged: client/interface, send paths, suppressions, options,
    #  MailgunnerException, EmailAddress, region, DI, Guard, JSON context — none are touched.)

tests/
└── Mailgunner.Tests/
    └── Webhooks/
        ├── WebhookSignatureVerificationTests.cs # NEW US1: correct signature → authentic; wrong signing key → not authentic
        ├── WebhookTamperingTests.cs             # NEW US2: altered signature / timestamp / token each → not authentic
        ├── WebhookSignatureFormatTests.cs       # NEW FR-005 + edge cases: empty/null/wrong-length/non-hex signature → false, no throw
        ├── WebhookSigningKeyValidationTests.cs  # NEW edge case: null/empty/whitespace signing key → ArgumentException
        ├── WebhookConstantTimeTests.cs          # NEW US3/SC-004: signature differing only at last char → false (full-width compare)
        └── WebhookIndependenceTests.cs          # NEW FR-006/FR-007: callable with no client/DI/HttpClient; pure, repeatable, offline

        # existing 002–007 tests remain and must still pass unchanged
```

**Structure Decision**: Continue the established single-library layout, but as the **simplest**
feature in the suite: a lone public static class with no transport, no state, and no client
coupling — exactly matching the clarified "static pure method, signing key as a parameter" contract
and the acceptance criterion "a pure function with no network dependency; built independently." All
cryptographic detail (HMAC computation, lowercase-hex encoding, constant-time comparison, and the
`netstandard2.0` `FixedTimeEquals` shim) is centralized as **private** helpers inside
`MailgunWebhookSignature`, keeping the public surface to one type and one method. Tests live under a
new `Webhooks/` folder and use no fakes, since there is nothing to fake; they compute reference
HMACs locally to prove correctness and the fail-closed behavior, and assert full-width comparison
behaviorally (the true constant-time guarantee is provided by construction via `FixedTimeEquals` /
the fixed-width XOR helper and verified by review, since wall-clock timing is not a reliable unit
assertion).

## Complexity Tracking

> No constitutional deviations for this feature — this section is intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | — | — |
