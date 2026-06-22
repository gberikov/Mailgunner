---
description: "Task list for feature: Send a Templated Email"
---

# Tasks: Send a Templated Email

**Input**: Design documents from `specs/004-template-message/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/template-send-contract.md, quickstart.md

**Tests**: INCLUDED. Constitution Principle III (Test-First, Network-Free) is NON-NEGOTIABLE and the spec
requires the feature be "verified against a fake transport, asserting the variables payload is valid JSON".
All behavior is exercised offline via the existing fake `HttpMessageHandler`. Write each test FIRST and
confirm it fails before implementing.

**Organization**: Tasks are grouped by user story in **priority order** (P1 → P2 → P2 → P3) so each story is
independently implementable and testable. US1 is the MVP. (US4 is P2, so it precedes the P3 US3.)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 / US4 — maps to the spec's user stories
- File paths are relative to the repository root

## Path Conventions

- Library source: `src/Mailgunner/`
- Tests: `tests/Mailgunner.Tests/` (xUnit, `net8.0`; reaches `internal` types via existing `InternalsVisibleTo`)

---

## Phase 1: Setup (Shared Infrastructure)

**No setup tasks.** `System.Text.Json` (the only dependency this feature uses, for serializing
`t:variables`) was already added in feature 003 and is centrally pinned in `Directory.Packages.props`.
The existing `StubHttpMessageHandler` already buffers multipart fields into `LastFormData`, so no test
infrastructure is needed either. Proceed directly to Foundational.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the template inputs to the shared message type. Every user story reads these members, so no story can compile or be tested until this is done.

**⚠️ CRITICAL**: Complete this phase before starting any user story.

- [X] T001 Add the template members to the existing `MailgunMessage` in `src/Mailgunner/MailgunMessage.cs`: `string? Template { get; set; }`, `string? TemplateVersion { get; set; }`, `bool GenerateTextFromTemplate { get; set; }`, and a get-only `IDictionary<string, object?> TemplateVariables` initialized to an empty `Dictionary<string, object?>` (same get-only, pre-initialized property **style** as `To`/`Cc`/`Bcc` — those are `IList`s, this is a dictionary; satisfies CA2227). XML docs on every new member per data-model.md (namespace `Mailgunner`); leave all existing members unchanged

**Checkpoint**: The message type exposes template inputs; user stories can begin.

---

## Phase 3: User Story 1 - Send an email from a stored template with global variables (Priority: P1) 🎯 MVP

**Goal**: A developer sets `Template` (and optionally a map of global `TemplateVariables`), calls `SendAsync`, and the request carries a `template` field plus exactly one `t:variables` JSON object; the same `SendResult` is returned as a plain send. A template name satisfies the body requirement, and a message that is both templated and inline (or carries template data without a name) is rejected before any request.

**Independent Test**: Send a templated message with global variables against a `StubHttpMessageHandler` returning `200 {"id":"<x>","message":"Queued. Thank you."}`; assert the captured request has a `template` field and exactly one `t:variables` field whose value `JsonDocument.Parse`s into a JSON object of the expected shape, and that `SendResult` exposes id + message — fully offline.

### Tests for User Story 1 (write first, confirm they fail)

- [X] T002 [P] [US1] Write `TemplateSendTests` in `tests/Mailgunner.Tests/Sending/TemplateSendTests.cs`: templated send with no inline body returns a `SendResult` and the captured request carries a `template` field with the name (C1); populated `TemplateVariables` (string, number, and a nested object value) produce **exactly one** `t:variables` field whose value parses via `JsonDocument` into a JSON object with matching keys and value kinds — string→string, number→number, nested→object (C2, SC-002); a templated send with no variables, and one with an explicitly empty `TemplateVariables`, both emit **no** `t:variables` field while `template` is still present (C3); the request is still `POST v3/{domain}/messages` with `multipart/form-data` (C12); a templated send against a non-2xx stub (e.g. 400 with a body) throws the same `MailgunnerException(status, body)`, confirming the shared error path applies to templated sends too (C12, FR-009); and the sending key never appears in any captured field, the `SendResult`, or the `MailgunnerException` (C-key-hygiene, FR-012/SC-008). Inject the stub via `AddMailgunner(...).ConfigurePrimaryHttpMessageHandler(() => stub)` per quickstart.md
- [X] T003 [P] [US1] Write `TemplateValidationTests` in `tests/Mailgunner.Tests/Sending/TemplateValidationTests.cs`: a message with both `Template` and `Text` (and separately `Template` and `Html`) throws `ArgumentException` before any request (assert `stub.LastRequest` is null) (C9); a message carrying template data (variables, or version, or `GenerateTextFromTemplate=true`) but a null/blank `Template` throws `ArgumentException` before any request (C10); a templated message with a `Template` but no inline body is **valid** (no throw); a message with neither template nor body still throws `ArgumentException` (existing FR-003)

### Implementation for User Story 1

- [X] T004 [US1] Extend the internal `MailgunMessageContent.Build` in `src/Mailgunner/Internal/MailgunMessageContent.cs`: (a) revise the body validation to accept "has Text/Html **OR** non-blank `Template`" (FR-003); (b) add mutual-exclusivity validation — non-blank `Template` together with any inline `Text`/`Html` throws `ArgumentException` (FR-003a); (c) add "template name required when template data present" — if `TemplateVariables` is non-empty, or `TemplateVersion` is non-blank, or `GenerateTextFromTemplate` is true, then `Template` must be non-blank else `ArgumentException`; (d) emit a `template` field when `Template` is non-blank; (e) when `TemplateVariables` is non-null and non-empty, emit exactly one `t:variables` field whose value is `System.Text.Json.JsonSerializer.Serialize(message.TemplateVariables)` (default options; keys verbatim). Keep `t:version` and `t:text` emission for US2/US3. Preserve existing `from`/recipients/`subject`/`text`/`html` emission order; append template fields after them — depends on T001

**Checkpoint**: MVP complete — a templated email sends with a single valid-JSON `t:variables` payload, verified offline. US2/US3 add the remaining `t:` fields.

---

## Phase 4: User Story 2 - Pin a specific template version (Priority: P2)

**Goal**: An optional `TemplateVersion` is emitted as `t:version`; a missing or blank version emits no field (active version used).

**Independent Test**: Send a templated message with `TemplateVersion` set, capture the request, and assert `t:version` equals the value; send another with no/blank version and assert no `t:version` field is present.

### Tests for User Story 2 (write first, confirm they fail)

- [X] T005 [P] [US2] Write `TemplateVersionTests` in `tests/Mailgunner.Tests/Sending/TemplateVersionTests.cs`: `TemplateVersion` set → a `t:version` field equals the supplied value (C4); `TemplateVersion` null, and separately blank/whitespace, → **no** `t:version` field while `template` is still present (C5, edge case)

### Implementation for User Story 2

- [X] T006 [US2] Extend `MailgunMessageContent.Build` in `src/Mailgunner/Internal/MailgunMessageContent.cs` to emit a `t:version` field equal to `TemplateVersion` only when it is non-null and not whitespace (FR-007) — depends on T004

**Checkpoint**: Version pinning works; US1 still passes.

---

## Phase 5: User Story 4 - Plain (non-templated) sending still works unchanged (Priority: P2)

**Goal**: Adding templated sending does not regress plain sends — a plain message carries its body parts and **none** of the four template fields, with identical success/error behavior.

**Independent Test**: Send a plain message (Text only, no template) and assert the captured request carries the body part and none of `template`/`t:version`/`t:text`/`t:variables`; confirm the existing 003 send tests still pass.

### Tests for User Story 4 (write first, confirm they fail)

- [X] T007 [P] [US4] Write `PlainSendRegressionTests` in `tests/Mailgunner.Tests/Sending/PlainSendRegressionTests.cs`: a plain message (Text and/or Html, no `Template`) sends successfully and the captured request contains the body parts but **none** of the fields `template`, `t:version`, `t:text`, `t:variables` (C8, SC-005); a plain message with template data left at defaults behaves exactly as in feature 003 (success/result shape unchanged)

**Checkpoint**: Plain and templated sends coexist; no production code change is required here (the four fields are gated on template state added in US1) — this story is a regression guarantee verified by test. All existing `003` `Sending/*.cs` tests must still pass.

---

## Phase 6: User Story 3 - Request a generated plain-text part (Priority: P3)

**Goal**: When requested, the message emits `t:text=yes`; otherwise no `t:text` field.

**Independent Test**: Send a templated message with `GenerateTextFromTemplate=true`, capture the request, and assert `t:text` equals `yes`; send another with it false and assert no `t:text` field.

### Tests for User Story 3 (write first, confirm they fail)

- [X] T008 [P] [US3] Write `TemplateTextTests` in `tests/Mailgunner.Tests/Sending/TemplateTextTests.cs`: `GenerateTextFromTemplate=true` → a `t:text` field equal to the literal `yes` (C6); `GenerateTextFromTemplate=false` → **no** `t:text` field emitted (C7, edge case)

### Implementation for User Story 3

- [X] T009 [US3] Extend `MailgunMessageContent.Build` in `src/Mailgunner/Internal/MailgunMessageContent.cs` to emit a `t:text` field with the literal value `yes` only when `GenerateTextFromTemplate` is true (FR-008) — depends on T004

**Checkpoint**: All template fields supported; US1/US2/US4 still pass.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T010 [P] Update `CHANGELOG.md` (Unreleased → Added): templated sending — `MailgunMessage.Template`, `TemplateVersion`, `GenerateTextFromTemplate`, and `TemplateVariables`, emitted as `template`/`t:version`/`t:text`/`t:variables`; note plain sends are unchanged
- [X] T011 Run `dotnet build` (warnings-as-errors, both `net8.0` and `netstandard2.0`) and `dotnet test` — confirm all green (new template tests **and** all existing 003 tests) with no network and no credentials
- [X] T012 Walk through `specs/004-template-message/quickstart.md` validation scenarios and confirm each maps to a passing test; confirm the automated key-hygiene assertion (added in T002) holds and that no captured field, `SendResult`, or `MailgunnerException` exposes the sending key (SC-008)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: None (no tasks).
- **Foundational (Phase 2)**: T001 BLOCKS all user stories.
- **User Stories (Phase 3–6)**: All depend on Foundational (T001). US1 (T004) is the MVP and creates the template-aware builder logic; US2 (T006) and US3 (T009) extend the **same** file (`MailgunMessageContent.cs`), so their implementation tasks depend on T004 and run sequentially after it. US4 has **no** implementation task — it is a regression test depending only on US1's gating (T004).
- **Polish (Phase 7)**: Depends on all desired stories being complete.

### Within Each User Story

- Tests are written first and must fail before implementation.
- T004 (US1 builder) before T006 (US2) and T009 (US3).

### Parallel Opportunities

- All test tasks are distinct files and `[P]`: T002, T003 (US1), T005 (US2), T007 (US4), T008 (US3) can all be written in parallel once T001 is done.
- Implementation tasks T004, T006, T009 all edit `MailgunMessageContent.cs`, so they are **sequential** (no `[P]`), even though their tests are parallel.
- T010 (CHANGELOG) is `[P]` against code tasks.

---

## Parallel Example: All story tests after Foundational

```bash
# Once T001 is done, write every story's tests together (distinct files):
Task: "Write TemplateSendTests in tests/Mailgunner.Tests/Sending/TemplateSendTests.cs"
Task: "Write TemplateValidationTests in tests/Mailgunner.Tests/Sending/TemplateValidationTests.cs"
Task: "Write TemplateVersionTests in tests/Mailgunner.Tests/Sending/TemplateVersionTests.cs"
Task: "Write PlainSendRegressionTests in tests/Mailgunner.Tests/Sending/PlainSendRegressionTests.cs"
Task: "Write TemplateTextTests in tests/Mailgunner.Tests/Sending/TemplateTextTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 2 (Foundational): add the template members (T001).
2. Complete Phase 3 (US1): validation + `template` + `t:variables` emission (T002–T004).
3. **STOP and VALIDATE**: a templated email sends with a single valid-JSON `t:variables` payload, offline.

### Incremental Delivery

1. Foundational → message exposes template inputs.
2. US1 → MVP: stored-template send with global variables.
3. US2 → version pinning (`t:version`).
4. US4 → confirm plain sends are unchanged (regression guarantee).
5. US3 → generated text part (`t:text=yes`).
6. Polish → CHANGELOG, full build/test, quickstart walkthrough.

---

## Notes

- `[P]` = different files, no dependencies on incomplete tasks.
- `[Story]` label maps each task to its user story for traceability.
- Verify each test fails before implementing; commit after each task or logical group (Conventional Commits).
- The `t:variables` value must always parse as valid JSON of the expected shape when variables are supplied (FR-011/SC-002); assert by parsing, not string comparison.
- The sending key must never appear in any captured field, `SendResult`, or `MailgunnerException` (FR-012 / SC-008) — asserted automatically in T002, not just by manual walkthrough.
- Cancellation on the templated path (FR-009) needs no new test: `SendAsync` is unchanged, so feature 003's existing `CancellationTests` already cover the shared cooperative-cancellation behavior for every send, templated or plain.
