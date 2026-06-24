---
description: "Task list for Webhook Signature Verification"
---

# Tasks: Webhook Signature Verification

**Input**: Design documents from `specs/008-webhook-signature-verification/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/webhook-signature-contract.md, quickstart.md

**Tests**: INCLUDED — test-first and network-free per Constitution III (NON-NEGOTIABLE), which explicitly
lists *"webhook signature validation (valid and invalid)"* as required unit coverage. This is the first
feature with **no HTTP path**, so tests use **no fake `HttpMessageHandler`** — they compute a reference
HMAC-SHA256 hex locally (with a test-only key) and assert the boolean/throwing behavior of the pure
function.

**Organization**: Tasks are grouped by user story (P1 → P1 → P2). The entire feature is a single public
static method, `MailgunWebhookSignature.Verify(...)`. US1 delivers the working method (the MVP); US2 adds
fail-closed input guards and verifies rejection of forged/malformed input; US3 verifies the constant-time
property. All implementation lands in **one source file**, so the implementation tasks serialize against
each other while the test files are independent.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks).
- **[Story]**: US1, US2, US3 (maps to user stories in spec.md). Setup/Foundational/Polish carry no story label.
- All paths are repository-relative.

## Path Conventions

Single class-library + test project (per plan.md):

- Library: `src/Mailgunner/`
- Tests: `tests/Mailgunner.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a clean, green baseline before any change.

- [X] T001 Confirm branch `008-webhook-signature-verification` is checked out and run `dotnet build` + `dotnet test` to verify the existing 002–007 suite is green before modifying anything (baseline; no network/credentials).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish the public surface so story tests compile and fail meaningfully.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Create public static class `MailgunWebhookSignature` in `src/Mailgunner/MailgunWebhookSignature.cs` (namespace `Mailgunner`) with the documented method `public static bool Verify(string signingKey, string timestamp, string token, string signature)`. Implement only the **signing-key precondition** now — throw `ArgumentException` when `signingKey` is `null`, empty, or whitespace (per data-model.md R1 / contract C12) — and return `false` for all other inputs as a temporary stub. Add full XML docs on the type, the method, every parameter, the return value, and the `<exception cref="System.ArgumentException">`. Must compile on `net8.0` and `netstandard2.0` under warnings-as-errors. (No client/interface change; this type is standalone per the clarified static-pure design.)

**Checkpoint**: Public surface exists and builds; signing-key validation works; all "authentic" cases still return `false`, so US1 tests will fail until implemented.

---

## Phase 3: User Story 1 - Accept a genuine Mailgun webhook (Priority: P1) 🎯 MVP

**Goal**: A signature equal to the HMAC-SHA256 of `timestamp + token` keyed by the signing key validates as authentic; a non-matching signing key does not.

**Independent Test**: Compute the reference HMAC-SHA256 hex of a known `timestamp + token` with a test-only key, pass it to `Verify`, and confirm `true`; repeat with a different key and confirm `false`.

### Tests for User Story 1 (write first; must FAIL before implementation) ⚠️

- [X] T003 [P] [US1] Create `tests/Mailgunner.Tests/Webhooks/WebhookSignatureVerificationTests.cs` asserting: given a test-only signing key and a known `(timestamp, token)`, the signature computed as lowercase-hex `HMACSHA256(UTF8(key), UTF8(timestamp + token))` makes `Verify` return `true`; the same triple verified with a **different** signing key returns `false`. The test computes the reference signature itself with `System.Security.Cryptography.HMACSHA256` (no production code reused). (US1, FR-001, FR-002, SC-001; contract C1–C2.)

### Implementation for User Story 1

- [X] T004 [US1] Implement the real verification body in `src/Mailgunner/MailgunWebhookSignature.cs`: after the signing-key precondition, compute `HMACSHA256` with `key = UTF8(signingKey)` over `message = UTF8(timestamp + token)`, encode the 32-byte MAC as **lowercase** hex, and compare it against `signature` using a **constant-time** comparison — `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals` on `net8.0` and a private internal fixed-width XOR-accumulate helper on `netstandard2.0`, selected with `#if NET8_0_OR_GREATER` (the helper returns `false` on length mismatch, else XOR-accumulates every byte pair with no early return). Add private helpers `ToLowerHex(byte[])` and the `FixedTimeEquals` shim. This step may assume the webhook-supplied values are non-null (null handling is US2). Depends on T002. (FR-002, FR-004; research Decisions 2, 3, 5.)

**Checkpoint**: US1 fully functional — a genuine signature verifies as authentic, a wrong key fails, and the comparison is constant-time by construction. MVP complete.

---

## Phase 4: User Story 2 - Reject a forged or tampered webhook (Priority: P1)

**Goal**: Any alteration to the signature, timestamp, or token fails validation; any missing/malformed webhook-supplied value fails closed to `false` without throwing; only a misconfigured signing key throws.

**Independent Test**: Start from a valid triple, alter exactly one field at a time and confirm `false` for each; pass empty/null/wrong-length/non-hex signatures and confirm `false` with no exception; pass a blank signing key and confirm `ArgumentException`.

### Tests for User Story 2 (write first; must FAIL before implementation) ⚠️

- [X] T005 [P] [US2] Create `tests/Mailgunner.Tests/Webhooks/WebhookTamperingTests.cs` asserting that, starting from a valid `(key, timestamp, token, signature)`, altering only the signature → `false`, altering only the timestamp (original token+signature) → `false`, and altering only the token (original timestamp+signature) → `false`. (US2, FR-003, SC-002; contract C3–C5.)
- [X] T006 [P] [US2] Create `tests/Mailgunner.Tests/Webhooks/WebhookSignatureFormatTests.cs` asserting that, with a valid key/timestamp/token, the following each return `false` **without throwing**: `signature = ""`, `signature = null`, a hex signature of the wrong length, a 64-char non-hexadecimal/garbage signature; and that a `null` `timestamp` or `null` `token` returns `false` without throwing. Also assert contract **C11**: an **empty-string** `timestamp` AND `token` paired with the signature correctly computed over them (i.e. `HMACSHA256` of `"" + ""`) returns `true` — empty (vs. null) webhook fields still yield a definite, signature-driven answer. (Note per analysis F1: the comparison happens in the produced-lowercase-hex domain, so a "non-hex" signature has no hex-decode path — it simply fails to match; no parsing exception exists.) (US2, FR-005, SC-003; contract C6–C11.)
- [X] T007 [P] [US2] Create `tests/Mailgunner.Tests/Webhooks/WebhookSigningKeyValidationTests.cs` asserting that `Verify` throws `ArgumentException` when `signingKey` is `null`, `""`, or whitespace (e.g. `"   "`), for any timestamp/token/signature. (US2, FR-008; contract C12.)

### Implementation for User Story 2

- [X] T008 [US2] Add fail-closed guards in `src/Mailgunner/MailgunWebhookSignature.cs`: after the signing-key precondition and before any HMAC/encoding/compare, return `false` when `signature` is `null`, or when `timestamp` or `token` is `null`. Depends on T004. (FR-005; contract C6–C11.)
  - **Which T006 cases need this task vs. already pass after T004**: the **null-`signature`** and **null-`timestamp`/null-`token`** assertions will FAIL until this guard lands (without it, `Encoding.UTF8.GetBytes(null)` throws on the null signature) — T008 makes them pass. The **wrong-length** and **64-char non-hex** assertions already pass after T004 (the length-checked constant-time compare rejects wrong length; non-hex simply mismatches in the hex domain — no parsing path). The **empty-string ts/token** assertion (C11) also already passes after T004 (no guard needed for empty, only for null).
  - The T005 tamper cases and T007 blank-key cases are independent of this task: they already pass from T004 (tamper → HMAC mismatch) and T002 (blank key → `ArgumentException`) respectively.

**Checkpoint**: US1 and US2 both pass independently — genuine webhooks are accepted and every forged/tampered/malformed input is rejected, with only a misconfigured key throwing.

---

## Phase 5: User Story 3 - Verification resists timing-based probing (Priority: P2)

**Goal**: The signature comparison examines the full candidate width and does not short-circuit on the first differing character, so timing reveals nothing about how many leading characters matched.

**Independent Test**: Provide a signature equal to the expected value except at the **last** character and confirm `false`; confirm the comparison performs position-independent work (no first-mismatch early exit) — guaranteed by construction and locked behaviorally by the test.

### Tests for User Story 3 (write first; must FAIL before implementation) ⚠️

- [X] T009 [P] [US3] Create `tests/Mailgunner.Tests/Webhooks/WebhookConstantTimeTests.cs` asserting: a signature identical to the expected lowercase-hex except for its **final** character returns `false`; a signature differing at the **first** character also returns `false`; both correct-length wrong signatures resolve to `false` (full-width comparison, no short-circuit). Add a code comment documenting that the constant-time guarantee is provided by construction (`CryptographicOperations.FixedTimeEquals` / the fixed-width XOR helper) and reviewed, not asserted via wall-clock timing. (US3, FR-004, SC-004; contract C13.)

### Implementation for User Story 3

> No new production code: the constant-time comparison was delivered in T004 by design (implementing it correctly the first time avoids rework). This phase verifies and locks that property.

**Checkpoint**: US1–US3 pass independently — verification is correct, fails closed, and is timing-resistant.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Prove purity/independence, document the surface, and final verification.

- [X] T010 [P] Create `tests/Mailgunner.Tests/Webhooks/WebhookIndependenceTests.cs` asserting that `Verify` is callable with **no** `IMailgunnerClient`, DI container, `HttpClient`, or configuration constructed (a direct static call), and that it is pure — the same inputs return the same result across repeated calls. Confirm the test references nothing from the client/sending/suppression namespaces. (FR-006, FR-007, SC-005; contract C14.)
- [X] T011 [P] Update `README.md` to document webhook signature verification: `MailgunWebhookSignature.Verify(signingKey, timestamp, token, signature)`, the HMAC-SHA256-over-`timestamp+token` + constant-time-comparison contract, that the signing key must come from the caller's own configuration (use the webhook signing key, not the sending key), and that replay/freshness checks are the consumer's responsibility (out of scope). Document the **one throwing path** for callers: a `null`/empty/whitespace `signingKey` raises `ArgumentException` (a configuration error), whereas every malformed or missing webhook-supplied field returns `false` rather than throwing. Reference contracts/webhook-signature-contract.md.
- [X] T012 [P] Update `CHANGELOG.md` (Unreleased) with the additive public surface: `MailgunWebhookSignature.Verify(...)`. Note it adds no new dependency and no HTTP surface.
- [X] T013 Run the quickstart.md offline validation scenarios and a final `dotnet build` + `dotnet test` (warnings-as-errors); confirm the full 002–008 suite is green with no network/credentials, on both target frameworks, and that the signing key never appears in any test fixture or output. Depends on T010.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories. T002 establishes the public method so every story test compiles.
- **User Stories (Phase 3–5)**: All depend on Foundational. The implementation tasks (T004, T008) extend the SAME file (`MailgunWebhookSignature.cs`) and are therefore sequential (T004 → T008). US3 adds no implementation. Recommended build order P1 → P1 → P2.
- **Polish (Phase 6)**: T010 depends on a working method (T004/T008); T011/T012 are independent docs; T013 is last.

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational — delivers the working method (MVP). No dependency on other stories.
- **US2 (P1)**: Test tasks can start after Foundational; its impl (T008) depends on T004. Independently testable.
- **US3 (P2)**: Test task can start once T004 lands (the constant-time behavior it asserts is delivered there). Independently testable.

### Within Each User Story

- The test task(s) are written FIRST and must FAIL before the implementation task in the same story.
- Both implementation tasks touch `MailgunWebhookSignature.cs` and therefore serialize against each other (no [P] among T004/T008).

### Parallel Opportunities

- **Story tests**: T003, T005, T006, T007, T009 are five different new test files and can all be authored in parallel (each must fail until its driving implementation lands).
- **Polish**: T010, T011, T012 (one test file + two docs) run in parallel; T013 is final.

---

## Parallel Example: User Story tests

```bash
# Different new test files, no shared state — author together (each fails until its impl task):
Task: "WebhookSignatureVerificationTests.cs (US1 valid → true / wrong key → false)"
Task: "WebhookTamperingTests.cs (US2 altered signature/timestamp/token → false)"
Task: "WebhookSignatureFormatTests.cs (US2 empty/null/wrong-length/non-hex → false, no throw)"
Task: "WebhookSigningKeyValidationTests.cs (US2 blank key → ArgumentException)"
Task: "WebhookConstantTimeTests.cs (US3 last-char-diff → false, full-width)"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup (baseline green).
2. Phase 2: Foundational (public method + signing-key precondition stub).
3. Phase 3: User Story 1 — real HMAC + lowercase hex + constant-time compare.
4. **STOP and VALIDATE**: a genuine signature verifies, a wrong key fails — offline, with nothing else configured.

### Incremental Delivery

1. Setup + Foundational → public surface compiles, key precondition works.
2. US1 → genuine signature accepted, wrong key rejected (MVP).
3. US2 → fail-closed guards: tampered/empty/null/wrong-length/non-hex → `false`, no throw.
4. US3 → constant-time / no-short-circuit verified and locked.
5. Polish → independence/purity test, README + CHANGELOG, final green run + secret-safety check.

### Notes

- This is a transport-free pure function — no fake `HttpMessageHandler`, no HTTP, no DI, no `MailgunnerException` path.
- Implement the constant-time comparison correctly in T004 (not a naive compare to be hardened later) to avoid rework; US3 verifies it.
- Commit after each task or logical group; keep the signing key out of all code, tests, and fixtures.
