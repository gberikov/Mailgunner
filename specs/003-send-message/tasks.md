---
description: "Task list for feature: Send a Single Email"
---

# Tasks: Send a Single Email

**Input**: Design documents from `specs/003-send-message/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/send-contract.md, quickstart.md

**Tests**: INCLUDED. Constitution Principle III (Test-First, Network-Free) is NON-NEGOTIABLE and the spec
requires the feature be "verified entirely against a fake transport". All behavior is exercised offline
via a fake `HttpMessageHandler`. Write each test FIRST and confirm it fails before implementing.

**Organization**: Tasks are grouped by user story (P1 → P3) so each story is independently implementable
and testable. US1 is the MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 / US4 — maps to the spec's user stories
- File paths are relative to the repository root

## Path Conventions

- Library source: `src/Mailgunner/`
- Tests: `tests/Mailgunner.Tests/` (xUnit, `net8.0`; reaches `internal` types via existing `InternalsVisibleTo`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the one new dependency this feature needs.

- [X] T001 Add `<PackageReference Include="System.Text.Json" />` to the dependencies `ItemGroup` in `src/Mailgunner/Mailgunner.csproj` (version is centrally managed in `Directory.Packages.props`, already pinned at 10.0.9 — do not specify a version attribute)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared public types, the interface change, and the test transport that every user story depends on. No story can compile or be tested until this phase is complete.

**⚠️ CRITICAL**: Complete this phase before starting any user story.

- [X] T002 [P] Create the `EmailAddress` public `readonly struct` in `src/Mailgunner/EmailAddress.cs`: `Address` (required) + `DisplayName?` get-only properties; constructor `EmailAddress(string address, string? displayName = null)` throwing `ArgumentException` on a blank address; `implicit operator EmailAddress(string)`; `ToString()` → `"DisplayName <Address>"` when a display name is set else `Address`; full value equality (`IEquatable<EmailAddress>`, `Equals`/`GetHashCode`/`==`/`!=`) to satisfy CA1815; XML docs on every member (namespace `Mailgunner`)
- [X] T003 [P] Create the `MailgunMessage` public sealed class in `src/Mailgunner/MailgunMessage.cs`: `From` (`EmailAddress`, get/set); get-only `IList<EmailAddress>` `To`/`Cc`/`Bcc` initialized to empty lists (satisfies CA2227/CA1002); `Subject`/`Text`/`Html` (`string?`, get/set); XML docs on every member (namespace `Mailgunner`)
- [X] T004 [P] Create the `SendResult` public sealed class in `src/Mailgunner/SendResult.cs`: immutable get-only `Id` and `Message` set via constructor `SendResult(string id, string message)`; XML docs (namespace `Mailgunner`)
- [X] T005 [P] Create the `MailgunnerException` public sealed class in `src/Mailgunner/MailgunnerException.cs` deriving from `System.Exception`: constructor `(int statusCode, string responseBody)` with a generated informative `Message`; get-only `int StatusCode` and `string ResponseBody`; suppress CA1032 at the type with a documented justification (no parameterless/message-only ctors — they would allow an invalid instance); never reference the sending key; XML docs (namespace `Mailgunner`)
- [X] T006 Add `Task<SendResult> SendAsync(MailgunMessage message, CancellationToken cancellationToken = default)` to `IMailgunnerClient` in `src/Mailgunner/IMailgunnerClient.cs` with XML docs describing success (`SendResult`), `MailgunnerException` (non-success or unparseable 2xx), and `ArgumentException` (invalid input before any request) — depends on T003, T004
- [X] T007 [P] Create the `StubHttpMessageHandler` fake in `tests/Mailgunner.Tests/Fakes/StubHttpMessageHandler.cs`: configurable response status code + body, captures the last outgoing `HttpRequestMessage` (exposed as `LastRequest`), and honors the `CancellationToken` (throws on a canceled token) so cancellation can be verified offline
- [X] T008 [P] Write `EmailAddressTests` in `tests/Mailgunner.Tests/EmailAddressTests.cs`: `ToString()` formats `"Bob <a@b.com>"` with a display name and the bare address without one; implicit string conversion works; value equality holds; blank address throws `ArgumentException` — write FIRST against T002 and confirm it passes the formatting/equality contract

**Checkpoint**: Public contract and test transport exist; user stories can begin.

---

## Phase 3: User Story 1 - Send a single email and get a success result (Priority: P1) 🎯 MVP

**Goal**: A developer composes a message (sender, recipient, subject, text and/or HTML body), calls `SendAsync`, and receives a `SendResult` exposing Mailgun's id and status message. The request POSTs `multipart/form-data` to `v3/{domain}/messages`.

**Independent Test**: Send a message with one recipient and a body against a `StubHttpMessageHandler` returning `200 {"id":"<x>","message":"Queued. Thank you."}`; assert `SendResult.Id`/`Message` are populated and the captured request is a `multipart/form-data` POST to the messages endpoint — fully offline.

### Tests for User Story 1 (write first, confirm they fail)

- [X] T009 [P] [US1] Write `SendMessageTests` in `tests/Mailgunner.Tests/Sending/SendMessageTests.cs`: success 2xx body → `SendResult` exposing id + message (C-01); HTML body (with/without text) is carried and returns the same way (C-02); the captured request is `POST v3/{domain}/messages` with `multipart/form-data` content and no real network call (C-03); the `subject` part is emitted when `Subject` is set and absent when it is null (FR-001); the `from` part carries the formatted sender. Inject the stub via `AddMailgunner(...).ConfigurePrimaryHttpMessageHandler(() => stub)` per quickstart.md
- [X] T010 [P] [US1] Write `MessageValidationTests` in `tests/Mailgunner.Tests/Sending/MessageValidationTests.cs`: missing sender, no recipient (across To/Cc/Bcc), and no body part each throw `ArgumentException` before any request is issued (assert `stub.LastRequest` is null) (C-10); a null `message` throws `ArgumentNullException` (C-11)

### Implementation for User Story 1

- [X] T011 [US1] Create the internal `MailgunMessageContent` builder in `src/Mailgunner/Internal/MailgunMessageContent.cs` (namespace `Mailgunner.Internal`): validate the message per FR-002 (`ArgumentNullException` on null; `ArgumentException` for blank `From.Address`, no recipient, or no `Text`/`Html`); build a `MultipartFormDataContent` with one `from` part (formatted sender), each `To` recipient as its own repeated `to` part, and `subject`/`text`/`html` parts only when present. NOTE: `Cc`/`Bcc` emission and blank/whitespace-recipient skipping (spec.md edge cases) are added in US2/T014 — keep the US1 builder focused on `To` and treat the skip rule as deferred there
- [X] T012 [US1] Implement `SendAsync` in `src/Mailgunner/MailgunnerClient.cs`: add `IOptions<MailgunnerOptions>` to the constructor (resolved by the typed-client factory) and read the trimmed `Domain`; build content via `MailgunMessageContent`; `await HttpClient.PostAsync(new Uri("v3/{domain}/messages", UriKind.Relative), content, cancellationToken).ConfigureAwait(false)`; on 2xx parse the body with `System.Text.Json` `JsonDocument` for `id` + `message` → `SendResult`; throw `MailgunnerException(statusCode, rawBody)` otherwise (full error contract hardened in US3) — depends on T011

**Checkpoint**: MVP complete — a single email sends and returns a result, verified offline.

---

## Phase 4: User Story 2 - Address multiple recipients across to/cc/bcc (Priority: P2)

**Goal**: Each recipient across To/Cc/Bcc is emitted as its own distinct field — never comma-joined — and blank entries are skipped.

**Independent Test**: Send a message with three `to` recipients plus cc/bcc, capture the request, and assert the count of recipient fields equals the number of distinct recipients with no comma-joined value.

### Tests for User Story 2 (write first, confirm they fail)

- [X] T013 [P] [US2] Write `RecipientFieldsTests` in `tests/Mailgunner.Tests/Sending/RecipientFieldsTests.cs`: three `to` recipients produce three distinct `to` parts, none comma-joined (C-04, SC-002); each cc and each bcc appears as its own distinct field (C-05); the total recipient-field count equals N distinct recipients for N ≥ 1; blank/whitespace recipient entries are not turned into empty fields

### Implementation for User Story 2

- [X] T014 [US2] Extend `MailgunMessageContent` in `src/Mailgunner/Internal/MailgunMessageContent.cs` to emit one repeated `cc` part per `Cc` recipient and one repeated `bcc` part per `Bcc` recipient, and to skip any recipient whose address is null/blank/whitespace (across To/Cc/Bcc) — depends on T011

**Checkpoint**: Multi-recipient sends produce correct repeated fields; US1 still passes.

---

## Phase 5: User Story 3 - Non-success responses raise a typed error (Priority: P2)

**Goal**: Every failure to obtain a usable result surfaces as exactly one `MailgunnerException` exposing the HTTP status code and the raw response body, with the sending key never exposed.

**Independent Test**: Run sends against the stub returning 4xx, 5xx, an unparseable 2xx, and an empty-body non-success; assert each throws `MailgunnerException` with the exact status and raw body (empty-but-non-null where applicable), and never a `SendResult`.

### Tests for User Story 3 (write first, confirm they fail)

- [X] T015 [P] [US3] Write `SendErrorTests` in `tests/Mailgunner.Tests/Sending/SendErrorTests.cs`: 4xx with body → `MailgunnerException` `StatusCode==400`, `ResponseBody==body` (C-06); 5xx with body → same type, code 502, raw body (C-07); 2xx with non-JSON/missing-field body → `MailgunnerException` with status + raw body, no result (C-08); non-success with empty body → `ResponseBody==""` non-null (C-09); the sending key never appears in the result or the exception (C-13)

### Implementation for User Story 3

- [X] T016 [US3] Harden error handling in `SendAsync` in `src/Mailgunner/MailgunnerClient.cs`: route a 2xx body that is invalid JSON or missing `id`/`message` to the same `MailgunnerException` (FR-006a); ensure `ResponseBody` is a non-null empty string when the response has no body (FR-011); confirm a single typed error path for all non-success responses (FR-007) and that the raw body is captured verbatim — depends on T012

**Checkpoint**: All error paths funnel through the one typed exception; US1/US2 still pass.

---

## Phase 6: User Story 4 - Cancellation is honored (Priority: P3)

**Goal**: A canceled token stops the send cooperatively and surfaces `OperationCanceledException` rather than returning a result.

**Independent Test**: Start a send with an already-canceled token (and a token canceled in-flight by the stub) and assert cancellation is surfaced and no `SendResult` is returned.

### Tests for User Story 4 (write first, confirm they fail)

- [X] T017 [P] [US4] Write `CancellationTests` in `tests/Mailgunner.Tests/Sending/CancellationTests.cs`: an already-canceled token causes `SendAsync` to surface `OperationCanceledException` and return no result; a token canceled while the request is in flight (via the stub) surfaces cancellation cooperatively (C-12, SC-004)

### Implementation for User Story 4

- [X] T018 [US4] Ensure `SendAsync` in `src/Mailgunner/MailgunnerClient.cs` threads `cancellationToken` into `PostAsync` and the body read, awaits with `ConfigureAwait(false)`, and surfaces `OperationCanceledException` unwrapped (not as `MailgunnerException`); read the body via `ReadAsStringAsync(cancellationToken)` behind `#if NET8_0_OR_GREATER` and the no-token overload on `netstandard2.0` — depends on T012

**Checkpoint**: Cancellation honored on both target frameworks; all stories pass.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T019 [P] Update `CHANGELOG.md` documenting the new public surface: `EmailAddress`, `MailgunMessage`, `SendResult`, `MailgunnerException`, and `IMailgunnerClient.SendAsync`
- [X] T020 Run `dotnet build` (warnings-as-errors, both `net8.0` and `netstandard2.0`) and `dotnet test` — confirm all green with no network and no credentials
- [X] T021 Walk through `specs/003-send-message/quickstart.md` validation scenarios 1–11 and confirm each maps to a passing test

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–6)**: All depend on Foundational. US1 is the MVP. US2/US3/US4 each refine `MailgunnerClient.cs`/`MailgunMessageContent.cs` created in US1, so their implementation tasks (T014, T016, T018) depend on US1's T011/T012 and should land in priority order. Their tests (T013, T015, T017) are independent and can be written anytime after Foundational.
- **Polish (Phase 7)**: Depends on all desired stories being complete.

### Within Each User Story

- Tests are written first and must fail before implementation.
- T011 (builder) before T012 (SendAsync); T012 before T014/T016/T018.

### Parallel Opportunities

- Foundational type tasks T002, T003, T004, T005, T007, T008 are all `[P]` (distinct files) — only T006 waits on T003/T004.
- Within a story, the test task `[P]` (T009/T010, T013, T015, T017) runs alongside other stories' test tasks since each is a distinct file.
- Because US2/US3/US4 implementation tasks edit the same two source files, their *implementation* steps are sequential even though their *tests* are parallel.

---

## Parallel Example: Foundational Phase

```bash
# Launch the independent type + fake creations together:
Task: "Create EmailAddress struct in src/Mailgunner/EmailAddress.cs"
Task: "Create MailgunMessage class in src/Mailgunner/MailgunMessage.cs"
Task: "Create SendResult class in src/Mailgunner/SendResult.cs"
Task: "Create MailgunnerException in src/Mailgunner/MailgunnerException.cs"
Task: "Create StubHttpMessageHandler in tests/Mailgunner.Tests/Fakes/StubHttpMessageHandler.cs"
Task: "Write EmailAddressTests in tests/Mailgunner.Tests/EmailAddressTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational).
2. Complete Phase 3 (US1): builder + `SendAsync` happy path with basic error throw.
3. **STOP and VALIDATE**: a single email sends and returns a result offline.

### Incremental Delivery

1. Setup + Foundational → contract and test transport ready.
2. US1 → MVP: single send returns a result.
3. US2 → multi-recipient repeated fields.
4. US3 → hardened single typed error contract.
5. US4 → cooperative cancellation across both TFMs.
6. Polish → CHANGELOG, full build/test, quickstart walkthrough.

---

## Notes

- `[P]` = different files, no dependencies on incomplete tasks.
- `[Story]` label maps each task to its user story for traceability.
- Verify each test fails before implementing; commit after each task or logical group (Conventional Commits).
- The sending key must never appear in a `SendResult` or `MailgunnerException` (FR-010 / SC-007).
