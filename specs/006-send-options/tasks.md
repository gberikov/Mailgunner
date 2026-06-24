---
description: "Task list for Send Enrichment Options (Attachments, Tags, Scheduling, Tracking, Custom Headers & Variables)"
---

# Tasks: Send Enrichment Options (Attachments, Tags, Scheduling, Tracking, Custom Headers & Variables)

**Input**: Design documents from `specs/006-send-options/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/send-options-contract.md, quickstart.md

**Tests**: INCLUDED — test-first and network-free per Constitution III (NON-NEGOTIABLE), which mandates
coverage of *multipart construction* for messages. Feature 006 extends that construction with option,
header, variable, and file parts; every assertion runs offline against the fake transport.

**Organization**: Tasks are grouped by user story (P1 → P2 → P3 → P4). Each story is independently
testable against `StubHttpMessageHandler`. All enrichment emission lives in one internal type
(`MailgunOptionsContent`), so the per-story implementation tasks extend the **same file** sequentially
while their test files are independent.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks).
- **[Story]**: US1, US2, US3, US4 (maps to user stories in spec.md). Setup/Foundational/Polish carry no story label.
- All paths are repository-relative.

## Path Conventions

Single class-library + test project (per plan.md):

- Library: `src/Mailgunner/`
- Tests: `tests/Mailgunner.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a clean, green baseline before any change.

- [X] T001 Confirm branch `006-send-options` is checked out and run `dotnet build` + `dotnet test` to verify the existing 002/003/004/005 suite is green before modifying anything (baseline; no network/credentials).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The new public types, the shared emitter, the two builder wirings, and the test-fake extension that every user story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Create public enum `ClickTracking` in `src/Mailgunner/ClickTracking.cs` with members `Yes`, `No`, `HtmlOnly`; XML docs on the type and each member documenting the wire values `yes`/`no`/`htmlonly` (per data-model.md).
- [X] T003 [P] Create public data-only type `MailgunFile` in `src/Mailgunner/MailgunFile.cs` with ctor `MailgunFile(string fileName, byte[] content, string? contentType = null)` that throws `ArgumentException` on blank `fileName` and `ArgumentNullException` on null `content`; get-only properties `FileName`, `Content`, `ContentType`; XML docs on every public member (per data-model.md).
- [X] T004 [P] Create public data-only type `MailgunSendOptions` in `src/Mailgunner/MailgunSendOptions.cs` with members `Tags` (IList<string> get, init non-null), `TestMode` (bool get/set), `TrackingOpens` (bool? get/set), `TrackingClicks` (ClickTracking? get/set), `DeliveryTime` (DateTimeOffset? get/set), `CustomHeaders` (IDictionary<string,string> get, init ordinal non-null), `CustomVariables` (IDictionary<string,string> get, init ordinal non-null); XML docs on every public member (per data-model.md). Depends on T002.
- [X] T005 [P] Add `Options` (MailgunSendOptions get, init non-null via `new()`), `Attachments` (IList<MailgunFile> get, init non-null), and `InlineFiles` (IList<MailgunFile> get, init non-null) members with XML docs to `src/Mailgunner/MailgunMessage.cs` (per data-model.md). Depends on T003, T004.
- [X] T006 [P] Add the identical `Options` / `Attachments` / `InlineFiles` members with XML docs to `src/Mailgunner/MailgunBatchMessage.cs` (per data-model.md). Depends on T003, T004.
- [X] T007 [P] Extend `tests/Mailgunner.Tests/Fakes/StubHttpMessageHandler.cs` additively so each captured part records its filename and content type: change `FormField` to `record struct FormField(string Name, string Value, string? FileName, string? ContentType)`, capture `part.Headers.ContentDisposition?.FileName` (unquoted) and `part.Headers.ContentType?.MediaType` in the handler, and add a `CapturedRequest` helper to fetch file parts by field name; preserve all existing members (`Values`/`Value`/`Count`/`LastFormData`/`Requests`/`ResponseSelector`/`OnSend`) so the 003/004/005 tests stay green (per research.md Decision 8).
- [X] T008 Create internal static `src/Mailgunner/Internal/MailgunOptionsContent.cs` with method `Append(MultipartFormDataContent content, MailgunSendOptions options, IEnumerable<MailgunFile> attachments, IEnumerable<MailgunFile> inlineFiles)` and a private `StringContent` add helper (initially a no-op body that compiles; per-story branches are filled in later). XML/internal docs describing the deterministic emit order from data-model.md. Depends on T003, T004.
- [X] T009 Wire `src/Mailgunner/Internal/MailgunMessageContent.cs` to call `MailgunOptionsContent.Append(content, message.Options, message.Attachments, message.InlineFiles)` at the END of `Build`, after the existing body/template fields; confirm a no-options message produces an unchanged request (no added parts) and the 003/004 tests still pass. Depends on T005, T008.
- [X] T010 Wire `src/Mailgunner/Internal/MailgunBatchContent.cs` to call `MailgunOptionsContent.Append(content, message.Options, message.Attachments, message.InlineFiles)` at the END of `BuildChunk` (so EVERY chunk carries the enrichments); confirm the 005 batch tests still pass with no options set. Depends on T006, T008.

**Checkpoint**: New types compile; the fake captures filename/content type; both builders call the (still empty) emitter; no-options sends are unchanged — user stories can begin.

---

## Phase 3: User Story 1 - Attach files and embed inline files in a send (Priority: P1) 🎯 MVP

**Goal**: An attachment appears as a file part carrying its filename and content type (defaulting to `application/octet-stream` when omitted); an inline file appears under the distinct `inline` field; multiple files each get their own part.

**Independent Test**: Send a message with one attachment (content + filename + content type) and one inline file against the fake; confirm each appears as its own file part — `attachment` and `inline` respectively — carrying its filename and declared content type.

### Tests for User Story 1 (write first; must FAIL before implementation) ⚠️

- [X] T011 [P] [US1] Create `tests/Mailgunner.Tests/Sending/AttachmentTests.cs` asserting: an attachment with content type appears as a part named `attachment` with that `filename` and `Content-Type`; an attachment with no content type defaults to `application/octet-stream`; an inline file appears as a part named `inline`, distinct from `attachment`, carrying its filename + content type; multiple attachments and multiple inline files each appear as their own part preserving filename/content type; and `MailgunFile("", bytes)` throws `ArgumentException` while `MailgunFile("f", null!)` throws `ArgumentNullException` (SC-001, SC-002, FR-002, FR-003, FR-004; quickstart scenarios 1–4, 13; per contracts/send-options-contract.md).

### Implementation for User Story 1

- [X] T012 [US1] Implement attachment + inline emission in `src/Mailgunner/Internal/MailgunOptionsContent.cs` `Append`: for each `attachments` entry add a `ByteArrayContent` part named `attachment` via `content.Add(byteContent, "attachment", file.FileName)` with `byteContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType)`; do the same for each `inlineFiles` entry under the name `inline`. Depends on T008.

**Checkpoint**: US1 fully functional — attachments and inline files ride single/templated sends with correct filename + content type; T011 passes. MVP complete.

---

## Phase 4: User Story 2 - Tag, test, and toggle tracking on a campaign (Priority: P2)

**Goal**: One or more tags all appear as repeated `o:tag` parts; `o:testmode=yes` when enabled; `o:tracking-opens` and `o:tracking-clicks` (including `htmlonly`) appear with the requested values; all are absent when unset.

**Independent Test**: Send a message with three tags, test mode on, open tracking on and click tracking `htmlonly`; capture the request and confirm three `o:tag` parts, `o:testmode=yes`, `o:tracking-opens=yes`, `o:tracking-clicks=htmlonly`; a send with none set has no such parts.

### Tests for User Story 2 (write first; must FAIL before implementation) ⚠️

- [X] T013 [P] [US2] Create `tests/Mailgunner.Tests/Sending/OptionsTagsTrackingTests.cs` asserting: the same tag supplied three times yields exactly three `o:tag` parts with all values present in order (not de-duplicated); `o:testmode=yes` present when `TestMode` is true and ABSENT when false; `o:tracking-opens` is `yes`/`no` per `TrackingOpens` and absent when null; `o:tracking-clicks` is `yes`/`no`/`htmlonly` per `TrackingClicks` and absent when null (SC-003, SC-004, FR-005, FR-006, FR-007, FR-008; quickstart scenarios 5–7).

### Implementation for User Story 2

- [X] T014 [US2] Extend `src/Mailgunner/Internal/MailgunOptionsContent.cs` `Append` to emit, before the file parts: one repeated `o:tag` part per non-blank `options.Tags` entry (in order, not de-duplicated); `o:testmode=yes` when `options.TestMode`; `o:tracking-opens=yes|no` when `options.TrackingOpens` is non-null; `o:tracking-clicks=yes|no|htmlonly` when `options.TrackingClicks` is non-null (map `ClickTracking` → its wire value). Depends on T008.

**Checkpoint**: US1 and US2 both pass independently — campaigns can be tagged, test-run, and tracking-toggled.

---

## Phase 5: User Story 3 - Schedule a send for a future time (Priority: P3)

**Goal**: A scheduled delivery time appears as `o:deliverytime` formatted exactly as RFC 2822 with a numeric timezone offset (`+0000`, `+0300`), never a named zone; absent when unset.

**Independent Test**: Schedule a send for a known instant with a known offset; capture the request and confirm `o:deliverytime` is exactly RFC 2822 with a numeric offset and contains no named zone or colon in the offset.

### Tests for User Story 3 (write first; must FAIL before implementation) ⚠️

- [X] T015 [P] [US3] Create `tests/Mailgunner.Tests/Sending/DeliveryTimeTests.cs` asserting: a `DateTimeOffset` with zero offset emits `o:deliverytime` matching `^[A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2}:\d{2} \+0000$` (e.g. `Thu, 25 Jun 2026 14:00:00 +0000`); a `+03:00` offset emits `… +0300`; the offset contains no colon and the value contains no alphabetic zone token (no `UTC`/`EST`/`GMT`); `o:deliverytime` is ABSENT when `DeliveryTime` is null (SC-005, FR-009, FR-010; quickstart scenarios 8–9).

### Implementation for User Story 3

- [X] T016 [US3] Extend `src/Mailgunner/Internal/MailgunOptionsContent.cs` `Append` to emit `o:deliverytime` when `options.DeliveryTime` is non-null, using a private `FormatRfc2822(DateTimeOffset)` helper that renders `value.ToString("ddd, dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture)` followed by `value.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", "")` (numeric offset, colon stripped). Depends on T008.

**Checkpoint**: US1–US3 pass independently — scheduled sends carry an exact RFC 2822 numeric-offset delivery time.

---

## Phase 6: User Story 4 - Attach custom headers and custom variables (Priority: P4)

**Goal**: Custom headers appear under the `h:` prefix and custom variables under the `v:` prefix, each carrying the supplied name and string value verbatim, with no collision between headers, variables, and built-in options.

**Independent Test**: Send a message with a custom header `X-Correlation-Id` and a custom variable `campaign_id`; capture the request and confirm `h:X-Correlation-Id` and `v:campaign_id` appear with the supplied values.

### Tests for User Story 4 (write first; must FAIL before implementation) ⚠️

- [X] T017 [P] [US4] Create `tests/Mailgunner.Tests/Sending/CustomHeadersVariablesTests.cs` asserting: a custom header appears as `h:<name>` with its value; a custom variable appears as `v:<name>` with its string value verbatim; multiple headers and variables each appear as their own part with no collision between `h:`/`v:`/`o:`; re-assigning the same header/variable name replaces its value (no duplicate `h:`/`v:` part for that name); and a blank custom-header or custom-variable name throws `ArgumentException` with zero requests recorded (SC-006, FR-011, FR-012, FR-013; quickstart scenarios 10, 13).

### Implementation for User Story 4

- [X] T018 [US4] Extend `src/Mailgunner/Internal/MailgunOptionsContent.cs` `Append` to emit, after the `o:` options and before the file parts: one `h:<name>` part per `options.CustomHeaders` entry and one `v:<name>` part per `options.CustomVariables` entry (string values verbatim); throw `ArgumentException` for any entry whose name is null/blank, before the request is built. Depends on T008.

**Checkpoint**: All four user stories pass independently — full enrichment surface emits correctly.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Composition across send types, regression/secret-safety, and the documentation obligations, plus final verification.

- [X] T019 [P] Create `tests/Mailgunner.Tests/Sending/OptionsCompositionTests.cs` asserting: the same options/attachments ride a PLAIN `SendAsync`, a TEMPLATED `SendAsync`, and a batch `SendBatchAsync`; for a 2500-recipient batch (3 chunks) EVERY chunk carries the identical option/header/variable/attachment parts; a send supplying NO options/attachments/inline files produces a request with none of the `o:`/`h:`/`v:`/`attachment`/`inline` parts (equivalent to the pre-006 request, modulo the random multipart boundary); and the sending key never appears in any captured field/part (FR-001, FR-015, SC-008; quickstart scenarios 7, 11, 12, 14). Depends on T012, T014, T016, T018.
- [X] T020 [P] Update `README.md` to document the new enrichment surface and the **combined 16KB cap** on `o:`/`h:`/`v:`/`t:` parameters per request (service-enforced; exceeding it surfaces as `MailgunnerException`), and the `o:deliverytime` RFC 2822 + numeric-offset requirement (SC-007, FR-014; per contracts/send-options-contract.md documentation obligation).
- [X] T021 [P] Update `CHANGELOG.md` (Unreleased) with the additive surface — `MailgunSendOptions`, `MailgunFile`, `ClickTracking`, and the `Options`/`Attachments`/`InlineFiles` members on `MailgunMessage` and `MailgunBatchMessage`; note the 16KB documented cap and document-only behavior.
- [X] T022 Run the quickstart.md offline validation scenarios and a final `dotnet build` + `dotnet test` (warnings-as-errors); confirm the full 002/003/004/005/006 suite is green with no network/credentials. Depends on T019.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories. The emitter file (T008) and both builder wirings (T009, T010) must exist before any story test can observe parts.
- **User Stories (Phase 3–6)**: All depend on Foundational. Each story's implementation extends the SAME file (`MailgunOptionsContent.cs`), so T012 → T014 → T016 → T018 are sequential by file, but each story is independently testable. Recommended build order P1 → P2 → P3 → P4.
- **Polish (Phase 7)**: T019 depends on all four story implementations; T020/T021 are independent docs; T022 is last.

### Within Each User Story

- The test task is written FIRST and must FAIL before the implementation task in the same story.
- All implementation tasks touch `MailgunOptionsContent.cs` and therefore serialize against each other (no [P] among T012/T014/T016/T018).

### Parallel Opportunities

- **Foundational**: T002, T003, T007 (different new files) run in parallel; T004 waits on T002; T005/T006 wait on T003/T004 (different message files, parallel with each other); T008 waits on T003/T004; T009 waits on T005/T008; T010 waits on T006/T008.
- **Story tests**: T011, T013, T015, T017 are four different new test files and can all be authored in parallel (each must fail until its implementation task lands).
- **Polish**: T019, T020, T021 (test file + two docs) run in parallel; T022 is final.

---

## Parallel Example: Foundational types

```bash
# Different new files, no incomplete dependencies — launch together:
Task: "Create ClickTracking enum in src/Mailgunner/ClickTracking.cs"
Task: "Create MailgunFile in src/Mailgunner/MailgunFile.cs"
Task: "Extend StubHttpMessageHandler in tests/Mailgunner.Tests/Fakes/StubHttpMessageHandler.cs"
```

## Parallel Example: User Story tests

```bash
# Four independent test files — author together, each failing until its impl task:
Task: "AttachmentTests.cs (US1 file parts)"
Task: "OptionsTagsTrackingTests.cs (US2 o:tag/o:testmode/o:tracking-*)"
Task: "DeliveryTimeTests.cs (US3 RFC 2822 numeric offset)"
Task: "CustomHeadersVariablesTests.cs (US4 h:/v: prefixes)"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup (baseline green).
2. Phase 2: Foundational (types, fake extension, emitter shell, both builder wirings).
3. Phase 3: User Story 1 — attachments and inline files emit as file parts.
4. **STOP and VALIDATE**: attach a ticket PDF, confirm the file part carries filename + content type, offline.

### Incremental Delivery

1. Setup + Foundational → scaffolding ready, no-options sends unchanged.
2. US1 → attachments & inline files (MVP).
3. US2 → tags, test mode, tracking toggles.
4. US3 → scheduled delivery time (RFC 2822 numeric offset).
5. US4 → custom headers & variables.
6. Polish → composition across send types, no-stray-parts + secret-safety, README 16KB cap, CHANGELOG, final green run.

### Parallel Team Strategy

After Foundational, the four story test files (T011/T013/T015/T017) can be authored in parallel; the four
implementation branches serialize against `MailgunOptionsContent.cs`, so one developer should own that
file (or land the branches in quick succession) while others drive the independent test files and the
docs tasks (T020/T021).
