---

description: "Task list for One-Click List-Unsubscribe (RFC 8058)"
---

# Tasks: One-Click List-Unsubscribe (RFC 8058)

**Input**: Design documents from `specs/013-list-unsubscribe/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/public-api.md, quickstart.md

**Tests**: REQUIRED — constitution Principle III (NON-NEGOTIABLE) and spec FR-013 mandate network-free
xUnit tests for all new behavior, via the existing fake `HttpMessageHandler` (`StubHttpMessageHandler`),
asserting the exact emitted `h:List-Unsubscribe` / `h:List-Unsubscribe-Post` field values and every
rejection path.

**Organization**: Grouped by user story. US1 (one-click) is the MVP. US1–US3 all extend the single
shared emission helper `src/Mailgunner/Internal/MailgunOptionsContent.cs`, so their implementation tasks
on that file are sequential (not `[P]` against each other); their test files are independent (`[P]`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US3 from spec.md
- All paths are repository-relative.

## Path Conventions

- Library: `src/Mailgunner/`
- Tests: `tests/Mailgunner.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Test scaffolding the feature needs.

- [X] T001 [P] Create the test file `tests/Mailgunner.Tests/Sending/ListUnsubscribeTests.cs` with a class skeleton and the established offline harness (mirror `tests/Mailgunner.Tests/Sending/CustomHeadersVariablesTests.cs`: a `BuildClient()` using `services.AddMailgunner(...)` + `ConfigurePrimaryHttpMessageHandler(() => stub)` with `StubHttpMessageHandler`, and a `NewMessage()` helper). No test bodies yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared public type and property every user story depends on. No emission logic yet.

**⚠️ CRITICAL**: User-story emission work cannot begin until this phase is complete.

- [X] T002 Create the public type `src/Mailgunner/ListUnsubscribeOptions.cs` (`namespace Mailgunner`, `public sealed class`) with XML-documented members `string? Url`, `EmailAddress? MailtoAddress`, and `bool OneClick`, per `contracts/public-api.md`. No validation logic in the type itself (validation happens at request-build time).
- [X] T003 Add the XML-documented nullable property `public ListUnsubscribeOptions? ListUnsubscribe { get; set; }` to `src/Mailgunner/MailgunSendOptions.cs` (default `null` ⇒ opt-in, emits nothing).

**Checkpoint**: The public surface compiles (`dotnet build`); no behavior change yet — existing sends still emit nothing new.

---

## Phase 3: User Story 1 - One-click unsubscribe on a marketing blast (Priority: P1) 🎯 MVP

**Goal**: A one-click `https` target emits a correct `List-Unsubscribe` header plus
`List-Unsubscribe-Post: List-Unsubscribe=One-Click`; one-click without a valid `https` URL is rejected.

**Independent Test**: Configure a one-click `https` target, send through the stub, and assert the exact
`h:List-Unsubscribe` and `h:List-Unsubscribe-Post` field values; assert one-click-without-URL throws
with no request issued.

### Tests for User Story 1 (write first, ensure they FAIL) ⚠️

- [X] T004 [P] [US1] In `tests/Mailgunner.Tests/Sending/ListUnsubscribeTests.cs`, add tests: (a) one-click `https` URL emits `h:List-Unsubscribe` = `<https://…>` AND `h:List-Unsubscribe-Post` = `List-Unsubscribe=One-Click` (assert exact values and `Count(...) == 1` for each); (b) `OneClick=true` with no `Url` (mailto-only or empty) throws `ArgumentException` and leaves `stub.Requests` empty (contract C2, C6).

### Implementation for User Story 1

- [X] T005 [US1] In `src/Mailgunner/Internal/MailgunOptionsContent.cs`, add a `List-Unsubscribe` emission step inside `Append` (after the custom-headers/variables steps): when `options.ListUnsubscribe` is non-null, build the header value and emit `h:List-Unsubscribe`; when `OneClick` is true, also emit `h:List-Unsubscribe-Post` with the literal `List-Unsubscribe=One-Click`. Implement the `https` URL path first (wrap URL in `<…>`).
- [X] T006 [US1] In the same step, add validation (throwing `System.ArgumentException` with `nameof(options)`, before adding any field): treat a null/blank/whitespace-only `Url` as "no URL present"; `OneClick=true` requires a present, valid `https` `Url`; a present `Url` must parse as an absolute URI with scheme `https` (ordinal-ignore-case) and contain no control characters or line breaks (reuse the existing `ContainsLineBreak`/`ContainsControlCharacter` helpers). (Contracts C6, C7, C8 for the URL.)

**Checkpoint**: US1 fully functional — one-click marketing mail emits both RFC 8058 headers; invalid one-click is rejected. MVP deliverable.

---

## Phase 4: User Story 2 - Declare a non-one-click unsubscribe target (Priority: P2)

**Goal**: Support `mailto`-only, `https`-only (non-one-click), and both-together targets in a single
`List-Unsubscribe` header (URL first, comma-separated), with no `List-Unsubscribe-Post`.

**Independent Test**: For each of {mailto-only, url-only, both} without one-click, assert the single
`h:List-Unsubscribe` value and that `h:List-Unsubscribe-Post` is absent.

### Tests for User Story 2 (write first, ensure they FAIL) ⚠️

- [X] T007 [P] [US2] In `tests/Mailgunner.Tests/Sending/ListUnsubscribeTests.cs`, add tests asserting exact `h:List-Unsubscribe` values and `Count("h:List-Unsubscribe-Post") == 0` for: (a) mailto-only → `<mailto:ADDR>`; (b) url-only non-one-click → `<https://…>`; (c) both → `<https://…>, <mailto:ADDR>` (URL first, `", "` separator) (contracts C1, C3, C4).

### Implementation for User Story 2

- [X] T008 [US2] Extend the emission step in `src/Mailgunner/Internal/MailgunOptionsContent.cs` to compose the header value from both targets: include `<URL>` when `Url` is present and `<mailto:ADDR>` (using `MailtoAddress.Value.Address`) when `MailtoAddress` is present, joined by `", "` in URL-then-mailto order; emit `h:List-Unsubscribe` only (no `-Post`) when `OneClick` is false. (Depends on T005; same file as US1.)

**Checkpoint**: US1 and US2 both work — every target combination emits a correct single `List-Unsubscribe` header.

---

## Phase 5: User Story 3 - Safe, validated, opt-in behavior (Priority: P3)

**Goal**: Reject all invalid inputs before any request, guarantee the feature emits nothing when unset,
forbid a duplicate header when a manual one is also set, and repeat headers identically on every batch
chunk.

**Independent Test**: Each invalid input throws with no request; an unset target emits no headers; a
batch send repeats the headers per chunk.

### Tests for User Story 3 (write first, ensure they FAIL) ⚠️

- [X] T009 [P] [US3] In `tests/Mailgunner.Tests/Sending/ListUnsubscribeTests.cs`, add rejection tests (each asserts `ArgumentException` and `stub.Requests` empty): (a) non-`https` URL e.g. `http://…`; (b) control char / CRLF in `Url`; (c) target set but neither `Url` nor `MailtoAddress` present, plus a blank/whitespace-only `Url` with no mailto (treated as absent); (d) typed target set AND a manual `List-Unsubscribe` entry in `Options.CustomHeaders` — include a casing variant (e.g. `list-unsubscribe`) and a `List-Unsubscribe-Post` variant to prove case-insensitive detection; (e) a `MailtoAddress` carrying a control character / CRLF is rejected — this is delegated to the `EmailAddress` constructor (which throws at assignment); assert the throw and that no request is issued (covers the mailto half of FR-008; see the existing `tests/Mailgunner.Tests/EmailAddressTests.cs` for the underlying control-character guard) (contracts C7, C8, C9, C10).
- [X] T010 [P] [US3] In `tests/Mailgunner.Tests/Sending/ListUnsubscribeTests.cs`, add: (a) a regression test that with `Options.ListUnsubscribe` unset the request has `Count("h:List-Unsubscribe") == 0` and `Count("h:List-Unsubscribe-Post") == 0` (contract C5); (b) a batch test (mirror the batch setup in `tests/Mailgunner.Tests/Sending/Batch*Tests.cs`) sending enough recipients to span ≥2 chunks — i.e. >1000, the fixed `MailgunBatchContent.MaxRecipientsPerRequest` boundary — with one-click options, asserting every captured chunk request carries both headers (contract C11).

### Implementation for User Story 3

- [X] T011 [US3] In `src/Mailgunner/Internal/MailgunOptionsContent.cs`, add the remaining validation to the emission step (throwing `ArgumentException` before emitting any field): reject a "set but empty" target — neither a present `Url` (a blank/whitespace-only `Url` counts as absent, per T006) nor a `MailtoAddress`; and add the duplicate-header guard — if `options.CustomHeaders` contains a key equal ordinal-ignore-case to `List-Unsubscribe` or `List-Unsubscribe-Post`, throw. (Depends on T005/T008; same file.)

**Checkpoint**: All user stories independently functional; the feature is safe-by-construction and opt-in.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, changelog, and full-suite verification.

- [X] T012 [P] Add a CHANGELOG entry under `## [Unreleased]` → `### Added` in `CHANGELOG.md` describing the typed `MailgunSendOptions.ListUnsubscribe` / `ListUnsubscribeOptions` one-click List-Unsubscribe support (RFC 8058), noting it is additive (SemVer MINOR), opt-in, and emits `h:List-Unsubscribe` (+ `h:List-Unsubscribe-Post` for one-click).
- [X] T013 [P] Review XML docs on `src/Mailgunner/ListUnsubscribeOptions.cs` and the new `MailgunSendOptions.ListUnsubscribe` property for completeness (every public member documented; warnings-as-errors will fail the build otherwise); note the `https`-only, one-click-requires-URL, and duplicate-header-conflict rules in the property summary.
- [X] T014 Run `dotnet build` and `dotnet test` from the repo root; confirm green with no new warnings and that the new `ListUnsubscribeTests` all pass. Then walk the `specs/013-list-unsubscribe/quickstart.md` validation table to confirm every scenario is covered.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories (creates the type + property).
- **User Stories (Phase 3–5)**: All depend on Foundational. They share `MailgunOptionsContent.cs`, so
  their implementation tasks are sequential: T005/T006 (US1) → T008 (US2) → T011 (US3). Each story's
  **tests** are independent files-of-tests and `[P]`.
- **Polish (Phase 6)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Core emission + one-click — the MVP; no dependency on US2/US3.
- **US2 (P2)**: Extends the same emission step to compose multiple targets — builds on US1's step (T005).
- **US3 (P3)**: Adds the remaining validation + conflict guard + regression/batch coverage — builds on
  the US1/US2 emission code.

### Within Each User Story

- Tests written first and expected to FAIL before the matching implementation task.
- Foundational type/property before any emission.
- Emission before validation refinements.

### Parallel Opportunities

- T001 and (after T002/T003) the per-story **test** authoring tasks T004, T007, T009, T010 are `[P]`
  (all in the one test file but independent test methods — author together, run together).
- Polish T012 and T013 are `[P]` (CHANGELOG vs source docs, different files).
- The **implementation** tasks on `MailgunOptionsContent.cs` (T005, T006, T008, T011) are NOT parallel —
  same file, incremental.

---

## Parallel Example: User Story 1

```bash
# After Foundational (T002, T003), author US1 tests, then implement:
Task: "T004 [US1] one-click emission + one-click-without-URL rejection tests in tests/Mailgunner.Tests/Sending/ListUnsubscribeTests.cs"
# then (sequential, same file):
Task: "T005 [US1] emit h:List-Unsubscribe / h:List-Unsubscribe-Post in src/Mailgunner/Internal/MailgunOptionsContent.cs"
Task: "T006 [US1] one-click + https URL validation in src/Mailgunner/Internal/MailgunOptionsContent.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup (T001).
2. Phase 2: Foundational (T002, T003) — type + property.
3. Phase 3: User Story 1 (T004–T006).
4. **STOP and VALIDATE**: one-click mail emits both RFC 8058 headers; invalid one-click rejected.

### Incremental Delivery

1. Setup + Foundational → public surface ready.
2. US1 → one-click headers (MVP) → validate.
3. US2 → all target combinations in one header → validate.
4. US3 → full validation, conflict guard, opt-in regression, batch coverage → validate.
5. Polish → CHANGELOG, docs, full `dotnet test`.

---

## Notes

- [P] tasks = different files, no dependencies. Implementation on the shared
  `MailgunOptionsContent.cs` is intentionally sequential across stories.
- [Story] label maps each task to its user story for traceability.
- Verify tests fail before implementing (Constitution III, TDD).
- No new runtime dependency; no new public exception type (input errors → `ArgumentException`).
- Commit after each task or logical group; keep `dotnet build`/`dotnet test` green before each commit.
