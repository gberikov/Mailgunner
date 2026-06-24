---
description: "Task list for Personalized Mass Send (Batched Recipient Variables)"
---

# Tasks: Personalized Mass Send (Batched Recipient Variables)

**Input**: Design documents from `specs/005-batch-send/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/batch-send-contract.md, quickstart.md

**Tests**: INCLUDED — the feature is test-first and network-free per Constitution III (NON-NEGOTIABLE),
which explicitly mandates coverage of *batch auto-chunking at the 1000-recipient boundary* and *the
`recipient-variables` JSON shape*. All tests run offline against the fake transport.

**Organization**: Tasks are grouped by user story (P1 → P2 → P3) to enable independent implementation
and verification. Each story is independently testable against `StubHttpMessageHandler`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 (maps to user stories in spec.md). Setup/Foundational/Polish carry no story label.
- All paths are repository-relative.

## Path Conventions

Single class-library + test project (per plan.md):

- Library: `src/Mailgunner/`
- Tests: `tests/Mailgunner.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a clean, green baseline before any change.

- [X] T001 Confirm branch `005-batch-send` is checked out and run `dotnet build` + `dotnet test` to verify the existing 002/003/004 suite is green before modifying anything (baseline; no network/credentials).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared types, test infrastructure, and the client/content scaffolding that every user story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Create public data-only type `MailgunBatchMessage` in `src/Mailgunner/MailgunBatchMessage.cs` with members `From` (EmailAddress get/set), `Subject` (string? get/set), `Template` (string? get/set), `TemplateVersion` (string? get/set), `GenerateTextFromTemplate` (bool get/set), `TemplateVariables` (IDictionary<string, object?> get, init non-null), `Recipients` (IList<BatchRecipient> get, init non-null); XML docs on every public member (per data-model.md).
- [X] T003 [P] Create public data-only type `BatchRecipient` in `src/Mailgunner/BatchRecipient.cs` with ctor `BatchRecipient(EmailAddress address)`, `Address` (EmailAddress get), `Variables` (IDictionary<string, object?> get, init non-null); rely on implicit `string`→`EmailAddress` conversion; XML docs on every public member (per data-model.md).
- [X] T004 Add `Task<IReadOnlyList<SendResult>> SendBatchAsync(MailgunBatchMessage message, CancellationToken cancellationToken = default)` signature with XML docs to `src/Mailgunner/IMailgunnerClient.cs` (per contracts/batch-send-contract.md). Depends on T002.
- [X] T005 [P] Extend `tests/Mailgunner.Tests/Fakes/StubHttpMessageHandler.cs` additively to record EVERY request (per-request captured multipart fields, URI, method, content media type) and to accept an optional per-request-index response selector (so a chosen chunk can return a failing status); preserve existing `Last*`/`LastFormData` members pointing at the most recent request so all 003/004 tests stay green (per research.md Decision 8).
- [X] T006 Refactor `src/Mailgunner/MailgunnerClient.cs` to extract the existing response→`SendResult`-or-`MailgunnerException` logic (`TryParseResult`) into a private `SendContentAsync(HttpContent content, CancellationToken ct)`; rewire `SendAsync` to call it with NO behavior change; confirm existing single/template send tests still pass.
- [X] T007 Create `src/Mailgunner/Internal/MailgunBatchContent.cs` with: internal const `MaxRecipientsPerRequest = 1000`; `Validate(MailgunBatchMessage message)` enforcing (null → `ArgumentNullException`; missing/blank `From.Address` → `ArgumentException`; missing/blank `Template` → `ArgumentException`; any ordinal-duplicate recipient `Address` → `ArgumentException`) thrown before any request; and a `Chunk(IList<BatchRecipient>, int size)` order-preserving partition helper (chunk *k* = `[k·size, min((k+1)·size, N))`). Depends on T002, T003.

**Checkpoint**: Types compile, fake records all requests, client exposes shared send helper, validation + chunking helpers exist — user stories can begin.

---

## Phase 3: User Story 1 - Send a personalized templated email to thousands in as few requests as possible (Priority: P1) 🎯 MVP

**Goal**: One `SendBatchAsync` call delivers a stored-template message to a large list, auto-split into `ceil(N/1000)` sequential `multipart/form-data` requests, each reusing the same template, returning one `SendResult` per chunk.

**Independent Test**: Hand the client a template + global vars + several-thousand-entry recipient list against the fake transport; confirm minimum request count (each ≤1000), every recipient included exactly once, each request hits the messages endpoint via multipart, and one `SendResult` per request is returned.

### Tests for User Story 1 (write first; must FAIL before implementation) ⚠️

- [X] T008 [P] [US1] Create `tests/Mailgunner.Tests/Sending/BatchChunkingTests.cs` asserting request counts and split sizes: 2500 → exactly 3 requests sized 1000/1000/500; 1000 → exactly 1; 2000 → exactly 2; per-chunk `to` parts equal exactly that chunk's recipients (formatted via `EmailAddress.ToString()`, one part each, never comma-joined) in order; each request is `POST v3/{domain}/messages` with `multipart/form-data` and the same `template` value (SC-001, SC-002, SC-004, FR-002, FR-003, FR-005, FR-007, FR-009).
- [X] T009 [P] [US1] Create `tests/Mailgunner.Tests/Sending/BatchSendResultTests.cs` asserting `SendBatchAsync` returns one `SendResult` per chunk sent, in chunk order (FR-012).

### Implementation for User Story 1

- [X] T010 [US1] Implement the per-chunk multipart build in `src/Mailgunner/Internal/MailgunBatchContent.cs`: emit `from` (`From.ToString()`), one repeated `to` part per recipient (`Address.ToString()`, never comma-joined), `subject` when non-null, `template`, `t:version` when non-blank, `t:text=yes` when `GenerateTextFromTemplate` — reusing the field names/rules from `MailgunMessageContent` (feature 004). Depends on T007.
- [X] T011 [US1] Implement `SendBatchAsync` in `src/Mailgunner/MailgunnerClient.cs`: call `MailgunBatchContent.Validate`, `Chunk` the recipients at 1000, build each chunk's content, POST sequentially via `SendContentAsync` (fail-fast on first non-2xx), honor `CancellationToken` with `ConfigureAwait(false)`, and return `IReadOnlyList<SendResult>` (one per chunk). Depends on T006, T010.

**Checkpoint**: US1 fully functional — thousands send in `ceil(N/1000)` requests with per-chunk results; T008/T009 pass. MVP complete.

---

## Phase 4: User Story 2 - Each recipient is personalized and sees only their own address (Priority: P2)

**Goal**: Every chunk carries one `recipient-variables` JSON object keyed by each recipient's bare address (value = that recipient's own variables), and reuses the identical global `t:variables` across all chunks — the wire shape that makes Mailgun deliver an individual, personalized message per recipient.

**Independent Test**: Send a batch with three recipients holding distinct variables; capture the request; confirm `recipient-variables` is a single JSON object keyed by bare email with each value being exactly that recipient's variables (empty → `{}`), and that global `t:variables` is identical across chunks.

### Tests for User Story 2 (write first; must FAIL before implementation) ⚠️

- [X] T012 [P] [US2] Create `tests/Mailgunner.Tests/Sending/BatchRecipientVariablesTests.cs` asserting: each chunk has exactly one `recipient-variables` field whose top-level JSON kind is Object with one property per recipient keyed by the bare `Address`; each value is exactly that recipient's `Variables` (string→string, int→number, bool→bool, nested preserved); a recipient with no variables serializes to `{}`; per-recipient values are independent of global vars; and the global `t:variables` (when present) is identical across all chunks while being omitted when the global map is empty (FR-006, FR-007 wire shape, SC-005, SC-006; per contracts/batch-send-contract.md).

### Implementation for User Story 2

- [X] T013 [US2] Extend the build in `src/Mailgunner/Internal/MailgunBatchContent.cs` to emit one `recipient-variables` part per chunk — a `System.Text.Json.JsonSerializer.Serialize` of `{ recipient.Address.Address : recipient.Variables }` for every recipient in the chunk (empty map → `{}`) — and the global `t:variables` (serialize `TemplateVariables`, omit when empty, reusing the feature 004 rule), identical for every chunk. Depends on T010.

**Checkpoint**: US1 and US2 both pass independently — chunked sends now carry correctly keyed per-recipient variables and reused global variables.

---

## Phase 5: User Story 3 - Boundary behavior is deterministic and predictable (Priority: P3)

**Goal**: Splitting is exact and predictable — empty list is a zero-request no-op, exact multiples produce no trailing empty request, and supplied recipient order is preserved across chunk boundaries.

**Independent Test**: Run batch sends for empty, exactly 1000, exactly 2000, and 2500 lists against the fake; confirm request counts 0/1/2/3 with no stray trailing request and order preserved.

### Tests for User Story 3 (write first; must FAIL before implementation) ⚠️

- [X] T014 [P] [US3] Add boundary cases to `tests/Mailgunner.Tests/Sending/BatchChunkingTests.cs`: empty `Recipients` → zero requests recorded, returns an empty `IReadOnlyList<SendResult>`, no exception; exact-multiple 2000 → exactly 2 requests with no trailing empty request; recipient order preserved exactly across the chunk boundary (e.g. recipient #1000 ends chunk 1 and #1001 begins chunk 2) (SC-002, SC-003, FR-004, FR-008; per quickstart scenarios 3–4).

### Implementation for User Story 3

- [X] T015 [US3] Verify and harden the edge behavior in `src/Mailgunner/Internal/MailgunBatchContent.cs` (`Chunk`) and `src/Mailgunner/MailgunnerClient.cs` (`SendBatchAsync`): empty recipient list short-circuits to an empty result list before any request; `Chunk` produces no trailing empty slice on exact multiples; order is preserved. Fix any off-by-one. Depends on T007, T011.

**Checkpoint**: All three user stories pass independently; chunking is exact at every boundary.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validation, failure, cancellation, and secret-safety coverage that spans the stories, plus docs and final verification. (Validation and fail-fast/cancellation behavior were implemented in T007 and T011; these tasks lock them in with tests.) To preserve test-first for FR-010/FR-011, T017 (failure) and T018 (cancellation) MAY be authored before T011 and watched fail — they live in this phase only because they cut across stories, not because their behavior is implemented last.

- [X] T016 [P] Create `tests/Mailgunner.Tests/Sending/BatchValidationTests.cs`: null `message` → `ArgumentNullException`; missing `From` → `ArgumentException`; missing/blank `Template` → `ArgumentException`; duplicate recipient address → `ArgumentException` with ZERO requests recorded (FR-014; quickstart scenarios 8–9).
- [X] T017 [P] (may be authored before T011 — write first; must FAIL until T011) Create `tests/Mailgunner.Tests/Sending/BatchFailureTests.cs`: configure the fake so chunk #2 of 3 returns 500 → `SendBatchAsync` throws `MailgunnerException` exposing status 500 and the raw body, and only 2 requests are recorded (chunk #3 never sent); assert the sending key never appears in any recorded field, any `SendResult`, or the thrown `MailgunnerException` (FR-011, FR-011a; quickstart scenarios 10, 12).
- [X] T018 [P] (may be authored before T011 — write first; must FAIL until T011) Create `tests/Mailgunner.Tests/Sending/BatchCancellationTests.cs`: cancel after the first chunk → `OperationCanceledException` and remaining chunks not sent (FR-010; quickstart scenario 11).
- [X] T019 Update `CHANGELOG.md` (Unreleased) with the additive surface — `SendBatchAsync`, `MailgunBatchMessage`, `BatchRecipient` (create the file/section if absent).
- [X] T020 Run the quickstart.md offline validation scenarios and a final `dotnet build` + `dotnet test` (warnings-as-errors); confirm the full 002/003/004/005 suite is green with no network/credentials.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–5)**: All depend on Foundational. US2 (T013) extends the build introduced by US1 (T010); US3 (T015) hardens the chunking/loop from US1 (T011). For strict independence the stories can be tested in isolation, but the recommended build order is P1 → P2 → P3.
- **Polish (Phase 6)**: Depends on US1–US3 implementation (validation/fail-fast/cancellation impl live in T007/T011).

### Within Each User Story

- Tests are written FIRST and must FAIL before the implementation task in the same story.
- MailgunBatchContent build detail (T010 → T013) before / alongside the send loop (T011).

### Parallel Opportunities

- **Foundational**: T002 and T003 (different new files) run in parallel; T005 (test fake) is independent of the src types and also parallel. T004 waits on T002; T006 is independent; T007 waits on T002/T003.
- **US1 tests**: T008 and T009 (different new files) run in parallel, before T010/T011.
- **Polish tests**: T016, T017, T018 (three different new files) run in parallel.

---

## Parallel Example: Foundational

```bash
# Different files, no incomplete dependencies — launch together:
Task: "Create MailgunBatchMessage in src/Mailgunner/MailgunBatchMessage.cs"
Task: "Create BatchRecipient in src/Mailgunner/BatchRecipient.cs"
Task: "Extend StubHttpMessageHandler in tests/Mailgunner.Tests/Fakes/StubHttpMessageHandler.cs"
```

## Parallel Example: User Story 1 tests

```bash
Task: "BatchChunkingTests.cs (request counts + per-chunk to membership)"
Task: "BatchSendResultTests.cs (one SendResult per chunk)"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup (baseline green).
2. Phase 2: Foundational (types, fake, `SendContentAsync`, validate + chunk).
3. Phase 3: User Story 1 — chunked sequential send with per-chunk results.
4. **STOP and VALIDATE**: thousands send in `ceil(N/1000)` requests, offline.

### Incremental Delivery

1. Setup + Foundational → scaffolding ready.
2. US1 → headline batch send works (MVP).
3. US2 → per-recipient personalization + global-variable reuse.
4. US3 → exact boundary behavior locked in.
5. Polish → validation, fail-fast, cancellation, secret-safety, docs, final green run.
