---
description: "Task list for Suppression Lists Management (Bounces, Unsubscribes, Complaints)"
---

# Tasks: Suppression Lists Management (Bounces, Unsubscribes, Complaints)

**Input**: Design documents from `specs/007-suppression-lists/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/suppressions-contract.md, quickstart.md

**Tests**: INCLUDED — test-first and network-free per Constitution III (NON-NEGOTIABLE). This feature adds
the library's first JSON + pagination coverage; every assertion runs offline against the fake transport
(`StubHttpMessageHandler`), and the default `dotnet test` stays green with no Mailgun credentials.

**Organization**: Tasks are grouped by user story (P1 → P2 → P3). Each story is independently testable
against the fake transport. All operations live in one internal generic type
(`MailgunSuppressionList<TEntry, TDto, TAddDto>`, implementing the public `ISuppressionList<TEntry>`), so the per-story implementation tasks extend the **same file**
sequentially while their test files are independent.

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

- [X] T001 Confirm branch `007-suppression-lists` is checked out and run `dotnet build` + `dotnet test` to verify the existing 002–006 suite is green before modifying anything (baseline; no network/credentials).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The public read models, page type, interfaces, the source-gen JSON context + internal wire DTOs, the generic list implementation shell with the shared JSON-error helper, the facade, the client accessor, and the test-fake extension that every user story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Create public read model `Bounce` in `src/Mailgunner/Bounce.cs`: `Address` (string, required), `Code` (string?), `Error` (string?), `CreatedAt` (DateTimeOffset?), all `get; init;`; XML docs on the type and every member (per data-model.md).
- [X] T003 [P] Create public read model `Unsubscribe` in `src/Mailgunner/Unsubscribe.cs`: `Address` (string, required), `Tags` (IReadOnlyList<string>, defaults to empty), `CreatedAt` (DateTimeOffset?), all `get; init;`; XML docs on every member (per data-model.md).
- [X] T004 [P] Create public read model `Complaint` in `src/Mailgunner/Complaint.cs`: `Address` (string, required), `CreatedAt` (DateTimeOffset?), `get; init;`; XML docs on every member (per data-model.md).
- [X] T005 [P] Create public `SuppressionPage<TEntry>` in `src/Mailgunner/SuppressionPage.cs`: `Items` (IReadOnlyList<TEntry>), `NextCursor` (string?, opaque), `HasMore` (bool, true when Items non-empty AND NextCursor present); ctor sets all three; XML docs on every member (per data-model.md, contracts).
- [X] T006 [P] Create public generic interface `ISuppressionList<TEntry>` in `src/Mailgunner/ISuppressionList.cs` with the seven operations from contracts/suppressions-contract.md — `ListAsync(int? pageSize, CancellationToken)` (IAsyncEnumerable), `ListPageAsync(int? pageSize, CancellationToken)`, `ListPageAsync(string cursor, CancellationToken)`, `GetAsync(string address, CancellationToken)`, `AddAsync(TEntry entry, CancellationToken)`, `RemoveAsync(string address, CancellationToken)`, `ClearAsync(CancellationToken)` — each with full XML docs incl. `<exception>` for `ArgumentException`/`MailgunnerException`.
- [X] T007 [P] Create public facade interface `IMailgunSuppressions` in `src/Mailgunner/IMailgunSuppressions.cs` exposing `Bounces` (ISuppressionList<Bounce>), `Unsubscribes` (ISuppressionList<Unsubscribe>), `Complaints` (ISuppressionList<Complaint>) with XML docs. Depends on T002, T003, T004, T006.
- [X] T008 [P] Create internal wire DTO records in `src/Mailgunner/Internal/SuppressionWireDtos.cs` with `[JsonPropertyName]`: `PageDto<TItem>` (`items`, `paging`), `PagingDto` (`next`/`previous`/`first`/`last`), `BounceDto` (`address`/`code`/`error`/`created_at`), `UnsubscribeDto` (`address`/`tags`/`created_at`), `ComplaintDto` (`address`/`created_at`), and add-body DTOs `AddBounceDto` (`address`/`code?`/`error?`), `AddUnsubscribeDto` (`address`/`tags?`), `AddComplaintDto` (`address`) (per data-model.md).
- [X] T009 [P] Create source-generated `SuppressionJsonContext` (partial `JsonSerializerContext`) in `src/Mailgunner/Internal/SuppressionJsonContext.cs` with `[JsonSerializable]` for `PageDto<BounceDto>`, `PageDto<UnsubscribeDto>`, `PageDto<ComplaintDto>`, the single-entry DTOs, and the three add-body DTOs; configure `PropertyNamingPolicy`/options as needed for the `created_at`/`tags` shapes (System.Text.Json only, per Constitution I). Depends on T008.
- [X] T010 Create internal generic class `MailgunSuppressionList<TEntry, TDto, TAddDto>` (implementing `ISuppressionList<TEntry>`) in `src/Mailgunner/Internal/MailgunSuppressionList.cs` with a constructor taking `(HttpClient httpClient, string domain, string listSegment, Func<TDto,TEntry> project, Func<TEntry,TAddDto> toAddBody, JsonTypeInfo<...> typeInfos)`; add a private shared helper that issues a request, reads the body (`#if NET8_0_OR_GREATER` `ReadAsStringAsync(ct)` else `ReadAsStringAsync()`), and on non-2xx throws `new MailgunnerException((int)response.StatusCode, body)` (mirroring `MailgunnerClient.SendContentAsync`); operation bodies throw `NotImplementedException` for now so the project compiles. Depends on T006, T008, T009.
- [X] T011 Create internal `MailgunSuppressions` (implementing `IMailgunSuppressions`) in `src/Mailgunner/Internal/MailgunSuppressions.cs` constructed from `(HttpClient, string domain)`; build the three `MailgunSuppressionList<…>` instances with `listSegment` `bounces`/`unsubscribes`/`complaints`, the per-type DTO→model projections (copy address/code/error/tags; parse `created_at` via a shared internal `SuppressionTime.Parse` using `DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, AssumeUniversal | AdjustToUniversal)` → nullable), and the per-type entry→add-body factories. Depends on T002, T003, T004, T007, T008, T010.
- [X] T012 Add get-only member `IMailgunSuppressions Suppressions { get; }` (with XML docs) to `src/Mailgunner/IMailgunnerClient.cs`, and implement it in `src/Mailgunner/MailgunnerClient.cs` as a lazily-constructed `MailgunSuppressions` built from the existing `HttpClient` and trimmed `_domain` (no new HTTP/auth/region plumbing); confirm existing 002–006 tests still compile (no interface mocks rely on the old shape). Depends on T007, T011.
- [X] T013 [P] Extend `tests/Mailgunner.Tests/Fakes/StubHttpMessageHandler.cs` additively to capture each request's raw body for non-multipart (JSON) content: add `LastBody` (string?) and a `Body` (string?) field on `CapturedRequest`, populated from `request.Content.ReadAsStringAsync(...)` when the content is not `MultipartFormDataContent`; preserve ALL existing members (multipart `FormField` capture, `Values`/`Value`/`Count`/`Fields`/`LastFormData`/`Requests`/`ResponseSelector`/`OnSend`) so 002–006 tests stay green (per research.md D8).

**Checkpoint**: New types compile; the JSON context generates; the facade and `client.Suppressions` resolve; the fake captures JSON bodies; all operations stubbed — user stories can begin.

---

## Phase 3: User Story 1 - List a suppression list, following pagination through large lists (Priority: P1) 🎯 MVP

**Goal**: Reading each list type returns typed models; `ListAsync` transparently follows pagination across pages and stops on the final (empty) page; an optional page size applies to the first request only; the single-page primitive and single-entry get work; not-found get surfaces the typed error.

**Independent Test**: Prime the fake with a multi-page sequence (page 1 + next, more pages, final empty page) and list each of the three types; confirm all entries from every page return as that type's typed model and enumeration stops cleanly; confirm `GetAsync` returns a typed model and a 404 throws.

### Tests for User Story 1 (write first; must FAIL before implementation) ⚠️

- [X] T014 [P] [US1] Create `tests/Mailgunner.Tests/Suppressions/SuppressionListPaginationTests.cs` asserting: a single page (then an empty page) yields exactly that page's items and stops; a 3-page sequence (via `ResponseSelector` index 0→page1+next, 1→page2+next, 2→empty) yields every item in order with none dropped/duplicated and stops after the empty page; an empty list yields zero items with only the first request issued; and cancelling the token mid-enumeration stops before the next page is fetched (`OperationCanceledException`) (SC-001, SC-002, SC-003, FR-002, FR-003, FR-004, FR-013; quickstart scenarios 1–3, 12).
- [X] T015 [P] [US1] Create `tests/Mailgunner.Tests/Suppressions/SuppressionModelTests.cs` asserting each list type deserializes into its distinct typed model: `Bounce.Address/Code/Error/CreatedAt`, `Unsubscribe.Address/Tags/CreatedAt`, `Complaint.Address/CreatedAt`; `created_at` like `Fri, 21 Oct 2011 11:02:55 GMT` parses to a UTC `DateTimeOffset`; an absent/unparseable `created_at` yields `null` (not an exception) (SC-004, FR-006; quickstart scenario 4; research D6).
- [X] T016 [P] [US1] Create `tests/Mailgunner.Tests/Suppressions/SuppressionPageSizeTests.cs` asserting: `ListAsync(pageSize: 250)` / `ListPageAsync(pageSize: 250)` issue a first request whose URI contains `limit=250`; the followed `next` URL is requested verbatim with no library-added `limit`; and omitting the page size issues a first request with no `limit` (FR-015, FR-003; quickstart scenario 5).
- [X] T017 [P] [US1] Create `tests/Mailgunner.Tests/Suppressions/SuppressionPagePrimitiveTests.cs` asserting: `ListPageAsync()` returns a `SuppressionPage` with parsed `Items`, an opaque `NextCursor` equal to the response `paging.next`, and `HasMore` true while items+next present; `ListPageAsync(cursor)` issues a GET to the cursor URL verbatim and returns the next page (FR-002a, FR-003; quickstart scenario 6).
- [X] T018 [P] [US1] Create `tests/Mailgunner.Tests/Suppressions/SuppressionGetTests.cs` asserting: `GetAsync(address)` issues `GET {list}/{address}` and returns the typed model; a 404 response throws `MailgunnerException` with `StatusCode == 404` and the raw body; a blank address throws `ArgumentException` with no request issued (SC-008, FR-017; quickstart scenario 7).

### Implementation for User Story 1

- [X] T019 [US1] Implement `ListPageAsync(int? pageSize, …)` and `ListPageAsync(string cursor, …)` in `src/Mailgunner/Internal/MailgunSuppressionList.cs`: first-page issues `GET v3/{domain}/{listSegment}` plus `?limit={pageSize}` when supplied; cursor form validates non-blank then issues `GET {cursor}` as an absolute URI verbatim; deserialize the body into `PageDto<TDto>` via `SuppressionJsonContext`, project items to `TEntry`, and return a `SuppressionPage<TEntry>` with `NextCursor = paging?.next`; non-2xx → `MailgunnerException`. Depends on T010, T011.
- [X] T020 [US1] Implement `ListAsync(int? pageSize, [EnumeratorCancellation] CancellationToken)` in `MailgunSuppressionList.cs` as an async iterator over `ListPageAsync`: fetch the first page (with pageSize), `yield return` each item, then follow `NextCursor` while the page is non-empty and a next pointer exists; stop on an empty page or absent next; honor the cancellation token between page fetches (FR-002b, FR-004, FR-013). Depends on T019.
- [X] T021 [US1] Implement `GetAsync(string address, …)` in `MailgunSuppressionList.cs`: validate non-blank address (`ArgumentException`); issue `GET v3/{domain}/{listSegment}/{Uri.EscapeDataString(address)}`; deserialize a single `TDto` via the JSON context and project to `TEntry`; non-2xx (incl. 404) → `MailgunnerException`. Depends on T010, T011.

**Checkpoint**: US1 fully functional — all three lists read with typed models, auto-following pagination, optional first-request page size, the caller-driven page primitive, and single-entry get; T014–T018 pass. MVP complete.

---

## Phase 4: User Story 2 - Add an address to a suppression list (Priority: P2)

**Goal**: Adding an address issues the JSON create operation to the correct per-type endpoint carrying the address plus that type's optional fields.

**Independent Test**: Add to each of the three list types against the fake; confirm each is a `POST {list}` with `Content-Type: application/json` and a body containing the address (and code/error for a bounce, tags for an unsubscribe).

### Tests for User Story 2 (write first; must FAIL before implementation) ⚠️

- [X] T022 [P] [US2] Create `tests/Mailgunner.Tests/Suppressions/SuppressionAddTests.cs` asserting: `AddAsync` issues `POST v3/{domain}/{list}` with `Content-Type: application/json`; the captured JSON body contains the `address` and, per type, `code`/`error` (bounce) or `tags` (unsubscribe) when supplied and omits them when not; `AddAsync(null)` throws `ArgumentNullException` and a blank `entry.Address` throws `ArgumentException`, both with no request issued; a non-2xx response throws `MailgunnerException` (SC-005, FR-007, FR-008; quickstart scenario 8). Depends on T013.

### Implementation for User Story 2

- [X] T023 [US2] Implement `AddAsync(TEntry entry, …)` in `src/Mailgunner/Internal/MailgunSuppressionList.cs`: guard null entry (`ArgumentNullException`) and blank `entry.Address` (`ArgumentException`); build the add-body DTO via the injected `toAddBody` factory, serialize it with `SuppressionJsonContext` into a `StringContent` with media type `application/json`, and `POST v3/{domain}/{listSegment}`; non-2xx → `MailgunnerException`. Depends on T010, T011.

**Checkpoint**: US1 and US2 pass independently — addresses can be read and added across all three list types.

---

## Phase 5: User Story 3 - Remove an address from, or clear, a suppression list (Priority: P3)

**Goal**: Removing a single address issues the per-type delete targeting that address; clearing a list issues the delete-all targeting no specific address.

**Independent Test**: Remove an address from each type (confirm `DELETE {list}/{address}`) and clear each type (confirm `DELETE {list}` with no address segment) against the fake.

### Tests for User Story 3 (write first; must FAIL before implementation) ⚠️

- [X] T024 [P] [US3] Create `tests/Mailgunner.Tests/Suppressions/SuppressionRemoveClearTests.cs` asserting: `RemoveAsync(address)` issues `DELETE v3/{domain}/{list}/{address}` (address in the path); a blank address throws `ArgumentException` with no request; `ClearAsync()` issues `DELETE v3/{domain}/{list}` with no address segment; a non-2xx response (incl. 404 on remove) throws `MailgunnerException` (SC-006, FR-009, FR-016; quickstart scenario 9).

### Implementation for User Story 3

- [X] T025 [US3] Implement `RemoveAsync(string address, …)` and `ClearAsync(…)` in `src/Mailgunner/Internal/MailgunSuppressionList.cs`: `RemoveAsync` validates non-blank address then issues `DELETE v3/{domain}/{listSegment}/{Uri.EscapeDataString(address)}`; `ClearAsync` issues `DELETE v3/{domain}/{listSegment}`; both surface non-2xx as `MailgunnerException`. Depends on T010, T011.

**Checkpoint**: All three user stories pass independently — the full list/get/add/remove/clear surface works across bounces, unsubscribes, and complaints.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Cross-operation error and secret-safety verification, sending-independence, the documentation obligations, and final verification.

- [X] T026 [P] Create `tests/Mailgunner.Tests/Suppressions/SuppressionErrorTests.cs` asserting that a non-2xx response on EACH operation (list, get, add, remove, clear) for each list type surfaces `MailgunnerException` exposing the status code and raw body, and that the sending key never appears in any captured request URI, header value, or body (SC-007, FR-012; quickstart scenario 10). Depends on T019, T020, T021, T023, T025.
- [X] T027 [P] Create `tests/Mailgunner.Tests/Suppressions/SuppressionIndependenceTests.cs` asserting the entire capability is reachable through `client.Suppressions.*` with NO send call made (a registered client whose only exercised path is suppressions), demonstrating independence from the sending pipeline (SC-009, FR-011; quickstart scenario 11). Depends on T019, T020, T021, T023, T025.
- [X] T028 [P] Update `README.md` with a "Suppressions" section documenting `client.Suppressions.Bounces/Unsubscribes/Complaints`, the auto-following `ListAsync` vs caller-driven `ListPageAsync`, the optional first-request page size, `GetAsync`/`AddAsync`/`RemoveAsync`/`ClearAsync`, and that failures (incl. not-found) surface `MailgunnerException` (per contracts documentation obligation).
- [X] T029 [P] Update `CHANGELOG.md` (Unreleased → Added) with the additive surface: `IMailgunnerClient.Suppressions`, `IMailgunSuppressions`, `ISuppressionList<TEntry>`, `SuppressionPage<TEntry>`, and the `Bounce`/`Unsubscribe`/`Complaint` models; note JSON endpoints with cursor pagination, single-address remove plus clear-all, and single-entry get.
- [X] T030 Run the quickstart.md offline validation scenarios and a final `dotnet build` + `dotnet test` (warnings-as-errors, both `net8.0` and `netstandard2.0` compile); confirm the full 002–007 suite is green with no network/credentials. Depends on T026, T027.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories. The generic list shell (T010), the facade (T011), the client accessor (T012), and the fake extension (T013) must exist before any story test can observe behavior.
- **User Stories (Phase 3–5)**: All depend on Foundational. Each story's implementation extends the SAME file (`MailgunSuppressionList.cs`), so T019/T020/T021 → T023 → T025 serialize by file, but each story is independently testable. Recommended build order P1 → P2 → P3.
- **Polish (Phase 6)**: T026/T027 depend on all operation implementations; T028/T029 are independent docs; T030 is last.

### Within Each User Story

- The test task(s) are written FIRST and must FAIL before the implementation task in the same story.
- All implementation tasks touch `MailgunSuppressionList.cs` and therefore serialize against each other (no [P] among T019/T020/T021/T023/T025).

### Parallel Opportunities

- **Foundational**: T002–T009 and T013 are different new files and run in parallel; T007 waits on T002–T004/T006; T009 waits on T008; T010 waits on T006/T008/T009; T011 waits on T002–T004/T007/T008/T010; T012 waits on T007/T011.
- **Story tests**: T014–T018 (US1), T022 (US2), T024 (US3) are independent new test files and can all be authored in parallel (each must fail until its implementation lands).
- **Polish**: T026, T027, T028, T029 run in parallel; T030 is final.

---

## Parallel Example: Foundational types

```bash
# Different new files, no incomplete dependencies — launch together:
Task: "Create Bounce model in src/Mailgunner/Bounce.cs"
Task: "Create Unsubscribe model in src/Mailgunner/Unsubscribe.cs"
Task: "Create Complaint model in src/Mailgunner/Complaint.cs"
Task: "Create SuppressionPage<TEntry> in src/Mailgunner/SuppressionPage.cs"
Task: "Create wire DTOs in src/Mailgunner/Internal/SuppressionWireDtos.cs"
Task: "Extend StubHttpMessageHandler to capture JSON bodies"
```

## Parallel Example: User Story tests

```bash
# Independent test files — author together, each failing until its impl task:
Task: "SuppressionListPaginationTests.cs (US1 follow + stop + cancel)"
Task: "SuppressionModelTests.cs (US1 typed models + created_at)"
Task: "SuppressionPageSizeTests.cs (US1 first-request limit)"
Task: "SuppressionGetTests.cs (US1 get + 404)"
Task: "SuppressionAddTests.cs (US2 POST JSON)"
Task: "SuppressionRemoveClearTests.cs (US3 DELETE single + all)"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup (baseline green).
2. Phase 2: Foundational (models, interfaces, JSON context, generic list shell, facade, client accessor, fake extension).
3. Phase 3: User Story 1 — read lists with typed models, auto-following pagination, page size, page primitive, single-entry get.
4. **STOP and VALIDATE**: page through a multi-thousand-entry fake list offline; confirm every entry returns and enumeration stops.

### Incremental Delivery

1. Setup + Foundational → scaffolding ready, all operations stubbed and compiling.
2. US1 → list/get/pagination (MVP — the core "why they chose Mailgun" read path).
3. US2 → add an address (JSON create per type).
4. US3 → remove a single address and clear an entire list.
5. Polish → cross-operation error + secret-safety, sending-independence, README + CHANGELOG, final green run.

### Parallel Team Strategy

After Foundational, the story test files (T014–T018, T022, T024) can be authored in parallel; the
operation implementations serialize against `MailgunSuppressionList.cs`, so one developer should own that
file (or land the operations in quick succession) while others drive the independent test files and the
docs tasks (T028/T029).

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- [Story] label maps each task to a user story for traceability.
- Constitution III (NON-NEGOTIABLE): write each story's test(s) first and confirm they FAIL before implementing; the default `dotnet test` run stays green offline with no credentials.
- No new dependency is added: `IAsyncEnumerable` resolves on `netstandard2.0` via the already-transitive `Microsoft.Bcl.AsyncInterfaces` (research D2); JSON is `System.Text.Json` source generation only.
- Commit after each task or logical group (Conventional Commits).
- Stop at any checkpoint to validate a story independently.
