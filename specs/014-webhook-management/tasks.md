---

description: "Task list for Domain Webhook Management (Register, List, Read, Update, Delete)"
---

# Tasks: Domain Webhook Management (Register, List, Read, Update, Delete)

**Input**: Design documents from `specs/014-webhook-management/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/public-api.md, quickstart.md

**Tests**: REQUIRED — constitution Principle III (NON-NEGOTIABLE) and spec FR-015 mandate network-free
xUnit tests for all new behavior, via the existing fake `HttpMessageHandler` (`StubHttpMessageHandler` /
`CapturingHttpMessageHandler`), asserting the per-operation wire format (method, path, multipart
`id`/`url` fields), region/domain routing, the fan-out request count/order on partial failure, the typed
registration model, and the `MailgunnerException` surface on non-2xx.

**Organization**: Grouped by user story. US1 (register) is the MVP. All operations live in one internal
class `src/Mailgunner/Internal/MailgunWebhooks.cs`, so implementation tasks on that file are sequential
across stories (not `[P]` against each other); each story's **test** files are independent (`[P]`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US4 from spec.md
- All paths are repository-relative.

## Path Conventions

- Library: `src/Mailgunner/`
- Tests: `tests/Mailgunner.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Test scaffolding the feature needs.

- [X] T001 [P] Create the test folder `tests/Mailgunner.Tests/WebhookManagement/` and a shared offline harness `tests/Mailgunner.Tests/WebhookManagement/WebhookManagementTestHarness.cs` (mirror the client-building pattern in `tests/Mailgunner.Tests/Suppressions/SuppressionGetTests.cs`: a `BuildClient(StubHttpMessageHandler stub, MailgunRegion region = Us, string domain = "mg.example.com")` using `services.AddMailgunner(...)` + `ConfigurePrimaryHttpMessageHandler(() => stub)` returning `IMailgunnerClient`), plus reusable JSON response fixtures: a single-webhook envelope `{"webhook":{"urls":[...]}}`, a list map `{"webhooks":{"delivered":{"urls":[...]},...}}`, and an empty list `{"webhooks":{}}`. No test bodies yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The typed model, response DTOs, JSON context, capability interface, and client wiring every
user story depends on. No operation logic yet.

**⚠️ CRITICAL**: User-story operation work cannot begin until this phase is complete.

- [X] T002 [P] Create the public enum `src/Mailgunner/WebhookEventType.cs` (`namespace Mailgunner`) with the closed set of 7 XML-documented members `Delivered, Opened, Clicked, Unsubscribed, Complained, PermanentFail, TemporaryFail` per `contracts/public-api.md` (note `PermanentFail` == "failed"; `accepted` intentionally excluded).
- [X] T003 [P] Create the public record `src/Mailgunner/WebhookRegistration.cs` (`namespace Mailgunner`, `public sealed record`) with XML-documented `WebhookEventType EventType` and `IReadOnlyList<string> Urls`, constructed by an internal constructor/initializer (not part of the public construction surface), per `contracts/public-api.md` and `data-model.md`.
- [X] T004 [P] Create `src/Mailgunner/Internal/WebhookWireDtos.cs` (`namespace Mailgunner.Internal`) with the response DTOs `WebhookUrlsDto { [urls] List<string>? Urls }`, `WebhookEnvelopeDto { [webhook] WebhookUrlsDto? Webhook }`, `WebhookListDto { [webhooks] Dictionary<string, WebhookUrlsDto?>? Webhooks }` (all `[JsonPropertyName]`-annotated), plus a static `WebhookEventTypes` helper exposing `string ToToken(WebhookEventType)` (throws `ArgumentOutOfRangeException` for an undefined value) and `WebhookEventType? TryParseToken(string)` mapping the snake_case wire tokens. No request DTOs (create/update use form parts).
- [X] T005 Create `src/Mailgunner/Internal/WebhookJsonContext.cs`: an `internal sealed partial class WebhookJsonContext : System.Text.Json.Serialization.JsonSerializerContext` with `[JsonSerializable]` for `WebhookListDto`, `WebhookEnvelopeDto`, and `WebhookUrlsDto` (mirror `src/Mailgunner/Internal/SuppressionJsonContext.cs`, including `DefaultIgnoreCondition = WhenWritingNull`). Depends on T004.
- [X] T006 [P] Create the public interface `src/Mailgunner/IMailgunWebhooks.cs` (`namespace Mailgunner`) with the six XML-documented members from `contracts/public-api.md`: `ListAsync`, `GetAsync(eventType)`, `CreateAsync(eventType, urls)`, `CreateAsync(eventTypes, url)`, `UpdateAsync(eventType, urls)`, `DeleteAsync(eventType)` — each with a trailing `CancellationToken cancellationToken = default`. Depends on T002, T003.
- [X] T007 Create the implementation skeleton `src/Mailgunner/Internal/MailgunWebhooks.cs` (`internal sealed class MailgunWebhooks : IMailgunWebhooks`), mirroring `src/Mailgunner/Internal/MailgunSuppressionList.cs`: constructor `(System.Net.Http.HttpClient httpClient, string domain)`; private URI helpers `RootUri()` → `v3/{domain}/webhooks` and `ItemUri(WebhookEventType)` → `v3/{domain}/webhooks/{token}` (both relative); a private `SendCoreAsync(HttpRequestMessage, CancellationToken)` that disposes the request, reads the body, throws `MailgunnerException((int)status, body)` on non-success and returns `(status, body)` (copy the `#if NET8_0_OR_GREATER` body-read pattern); and a private `ProjectRegistration(WebhookEventType, WebhookUrlsDto?)` returning a `WebhookRegistration` with a non-null `Urls`. Stub the six interface methods to `throw new NotImplementedException()` for now. Depends on T004, T005, T006.
- [X] T008 Wire the capability onto the client: add `IMailgunWebhooks Webhooks { get; }` (XML-documented) to `src/Mailgunner/IMailgunnerClient.cs`, and in `src/Mailgunner/MailgunnerClient.cs` add a `System.Lazy<IMailgunWebhooks>` backing field initialized to `new MailgunWebhooks(HttpClient, _domain)` with `public IMailgunWebhooks Webhooks => _webhooks.Value;` (mirror the existing `Suppressions` wiring). Depends on T006, T007.
- [X] T009 [P] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookEventTypeMappingTests.cs`: assert every `WebhookEventType` round-trips to/from its exact wire token via `WebhookEventTypes.ToToken`/`TryParseToken` (explicitly cover `permanent_fail` and `temporary_fail`), an unknown token parses to null, and an undefined `(WebhookEventType)999` throws `ArgumentOutOfRangeException`. Depends on T004.

**Checkpoint**: The public surface compiles (`dotnet build`); `client.Webhooks` is reachable; event-type mapping is verified. No operation behavior yet.

---

## Phase 3: User Story 1 - Register a webhook endpoint for one or more event types (Priority: P1) 🎯 MVP

**Goal**: Register one event type with one or more callback URLs, and register one URL across several
event types in a single call (fan-out, sequential, fail-fast no-rollback).

**Independent Test**: Against the stub, create a webhook for one event type with a URL and assert the
outgoing `POST` targets `v3/{domain}/webhooks` on the region host carrying `id`=token and the `url`(s),
returning the created registration; then create one URL across several event types and assert one create
per event type (and, on an injected mid-sequence non-2xx, fail-fast with the exact request count/order).

### Tests for User Story 1 (write first, ensure they FAIL) ⚠️

- [X] T010 [P] [US1] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookCreateTests.cs`: (a) `CreateAsync(Delivered, ["https://a"])` issues one `POST` to `v3/mg.example.com/webhooks` with multipart `id=delivered` (`Count("id")==1`) and one `url=https://a`, and returns `{Delivered, ["https://a"]}`; (b) `CreateAsync(Clicked, ["https://a","https://b"])` issues one `POST` with `id=clicked` and two `url` parts (`Count("url")==2`) and returns both URLs; (c) a non-2xx response throws `MailgunnerException` exposing the status code and raw body (contracts C1, C2, C13).
- [X] T011 [P] [US1] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookCreateMultiEventTests.cs`: (a) `CreateAsync([Delivered,Opened,Clicked], "https://a")` issues exactly three `POST`s in order (`stub.Requests` with `id` = `delivered`, then `opened`, then `clicked`), each carrying `url=https://a`, and returns 3 registrations in order; (b) with a `ResponseSelector` failing the 2nd request with a permanent 400 (a non-retryable status, so the resilience handler does not inflate the request count), exactly two requests are issued (`delivered` ok, `opened` fails), `MailgunnerException(400,…)` is thrown, no `clicked` request is issued, and no rollback occurs (contracts C3, C4; SC-002).
- [X] T012 [P] [US1] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookValidationTests.cs`: each asserts `stub.Requests` empty (no request issued) — `ArgumentException` for (a) `CreateAsync(Delivered, [])`; (b) `CreateAsync(Delivered, ["  "])` (all-blank); (c) `CreateAsync(Delivered, null)`; (d) fan-out `CreateAsync([], "https://a")` (empty event-type set); (e) fan-out `CreateAsync([Delivered], "  ")` (blank url) (contract C14); and `ArgumentOutOfRangeException` for (f) an undefined event-type cast on a real operation call, e.g. `GetAsync((WebhookEventType)999)` and `CreateAsync((WebhookEventType)999, ["https://a"])`, thrown by the token mapping before any request (data-model rule 4; complements T009's `ToToken` unit test).

### Implementation for User Story 1

- [X] T013 [US1] In `src/Mailgunner/Internal/MailgunWebhooks.cs`, implement `CreateAsync(WebhookEventType eventType, IEnumerable<string> urls, CancellationToken)`: validate ≥1 non-blank URL (else `ArgumentException` before any request); build `MultipartFormDataContent` with one `id` part (the token) and one `url` part per supplied URL; `POST` to `RootUri()` via `SendCoreAsync`; deserialize the body with `WebhookJsonContext.Default.WebhookEnvelopeDto` and project to a `WebhookRegistration` for `eventType` (use the supplied URLs if the response omits them). Use `ConfigureAwait(false)`.
- [X] T014 [US1] In the same file, implement `CreateAsync(IEnumerable<WebhookEventType> eventTypes, string url, CancellationToken)`: validate a non-empty event-type set and a non-blank `url` (else `ArgumentException`); iterate in order, calling the per-event-type create with `[url]`, accumulating results; `cancellationToken.ThrowIfCancellationRequested()` between iterations; on a thrown `MailgunnerException` propagate immediately (fail-fast) leaving earlier results uncommitted/in-place; return the accumulated `IReadOnlyList<WebhookRegistration>` on full success. Depends on T013 (same file).

**Checkpoint**: US1 fully functional — registration (single and fan-out) works and is the MVP deliverable.

---

## Phase 4: User Story 2 - List all registrations and read a single one (Priority: P2)

**Goal**: List every configured event type's registration, and read one event type's registration; an
empty domain lists empty, an unregistered read surfaces the typed error.

**Independent Test**: Against a stub primed with a multi-event list map, `ListAsync` returns one typed
registration per event type; an empty map returns an empty list with no error. `GetAsync` returns a
registered event type's registration and surfaces `MailgunnerException` for an unregistered one (404).

### Tests for User Story 2 (write first, ensure they FAIL) ⚠️

- [X] T015 [P] [US2] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookListTests.cs`: (a) primed with `{"webhooks":{"delivered":{"urls":["https://a"]},"opened":{"urls":["https://b","https://c"]}}}`, `ListAsync` issues `GET v3/mg.example.com/webhooks` and returns two registrations with the right event types and URL(s); (b) primed with `{"webhooks":{}}`, `ListAsync` returns an empty list and issues exactly one request, no error; (c) a non-2xx throws `MailgunnerException` (contracts C5, C6, C13; SC-003).
- [X] T016 [P] [US2] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookGetTests.cs`: (a) primed with `{"webhook":{"urls":["https://a"]}}`, `GetAsync(Opened)` issues `GET v3/mg.example.com/webhooks/opened` and returns `{Opened, ["https://a"]}`; (b) a 404 response throws `MailgunnerException(404, body)` rather than returning null/empty (contracts C7, C8; SC-004).

### Implementation for User Story 2

- [X] T017 [US2] In `src/Mailgunner/Internal/MailgunWebhooks.cs`, implement `ListAsync(CancellationToken)`: `GET RootUri()` via `SendCoreAsync`; deserialize with `WebhookJsonContext.Default.WebhookListDto`; for each entry whose key maps via `TryParseToken` to a known event type and whose `urls` is non-empty, project a `WebhookRegistration`; return the (possibly empty) `IReadOnlyList<WebhookRegistration>`. Depends on T007 (skeleton). Same file as US1.
- [X] T018 [US2] In the same file, implement `GetAsync(WebhookEventType eventType, CancellationToken)`: `GET ItemUri(eventType)` via `SendCoreAsync` (non-2xx already throws); deserialize with `WebhookJsonContext.Default.WebhookEnvelopeDto` and project to a `WebhookRegistration` (throw `MailgunnerException(status, body)` if the envelope/`webhook` is null on a 2xx). Depends on T017 (same file).

**Checkpoint**: US1 and US2 work — registrations can be created, listed, and read back independently.

---

## Phase 5: User Story 3 - Update an existing registration's callback URL(s) (Priority: P3)

**Goal**: Replace an event type's callback URL(s) in place; updating an unregistered event type surfaces
the typed error.

**Independent Test**: Against the stub, `UpdateAsync(eventType, newUrls)` issues a `PUT` to that event
type's endpoint carrying the new `url`(s) and returns the updated registration; updating an unregistered
event type (404) surfaces `MailgunnerException`; empty URLs are rejected before any request.

### Tests for User Story 3 (write first, ensure they FAIL) ⚠️

- [X] T019 [P] [US3] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookUpdateTests.cs`: (a) `UpdateAsync(Delivered, ["https://new"])` issues `PUT v3/mg.example.com/webhooks/delivered` with `url=https://new` and returns the updated registration; (b) multiple URLs emit multiple `url` parts; (c) a 404 throws `MailgunnerException(404, body)`; (d) empty/all-blank/null `urls` throws `ArgumentException` with `stub.Requests` empty (contracts C9, C10, C14; SC-005).

### Implementation for User Story 3

- [X] T020 [US3] In `src/Mailgunner/Internal/MailgunWebhooks.cs`, implement `UpdateAsync(WebhookEventType eventType, IEnumerable<string> urls, CancellationToken)`: validate ≥1 non-blank URL (else `ArgumentException`); build `MultipartFormDataContent` with one `url` part per URL; `PUT ItemUri(eventType)` via `SendCoreAsync`; deserialize with `WebhookEnvelopeDto` and project to the updated `WebhookRegistration` (fall back to the supplied URLs if the response omits them). Depends on T007; same file.

**Checkpoint**: US1–US3 work — registrations can be created, inspected, and repointed.

---

## Phase 6: User Story 4 - Delete a registration (Priority: P3)

**Goal**: Delete an event type's registration; deleting an unregistered event type surfaces the typed
error.

**Independent Test**: Against the stub, `DeleteAsync(eventType)` issues a `DELETE` to that event type's
endpoint and reports success; deleting an unregistered event type (404) surfaces `MailgunnerException`.

### Tests for User Story 4 (write first, ensure they FAIL) ⚠️

- [X] T021 [P] [US4] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookDeleteTests.cs`: (a) `DeleteAsync(Clicked)` issues `DELETE v3/mg.example.com/webhooks/clicked` and completes on a 2xx; (b) a 404 throws `MailgunnerException(404, body)` rather than reporting success (contracts C11, C12; SC-006).

### Implementation for User Story 4

- [X] T022 [US4] In `src/Mailgunner/Internal/MailgunWebhooks.cs`, implement `DeleteAsync(WebhookEventType eventType, CancellationToken)`: `DELETE ItemUri(eventType)` via `SendCoreAsync` (non-2xx already throws); return on success. Depends on T007; same file.

**Checkpoint**: All four user stories independently functional — full webhook CRUD over the v3 surface.

---

## Phase 7: Cross-Cutting Tests (routing, errors, cancellation, independence)

**Purpose**: Verify the contract behaviors that span every operation. Authored against the now-complete
surface.

- [X] T023 [P] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookRoutingTests.cs`: assert that for the EU region the request host is `api.eu.mailgun.net` and for US it is `api.mailgun.net`, every operation's path carries the configured domain, and the `Authorization: Basic` header is present (mirror `tests/Mailgunner.Tests/Suppressions/`/`Registration/` routing+auth assertions) (contract C15; SC-008).
- [X] T024 [P] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookCancellationTests.cs`: using the stub's `OnSend` hook to cancel mid-flight, assert each operation throws `OperationCanceledException`/`TaskCanceledException`; for the fan-out create, assert cancellation after the first create issues no further creates (FR-013; contract C16).
- [X] T025 [P] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookIndependenceTests.cs`: build a client with only feature-002 configuration (no send/suppression usage) and exercise a webhook create+list+get+update+delete round-trip against the stub, proving the capability is usable in isolation (FR-014; SC-009; mirror `tests/Mailgunner.Tests/Suppressions/SuppressionIndependenceTests.cs`).
- [X] T026 [P] Create `tests/Mailgunner.Tests/WebhookManagement/WebhookErrorTests.cs`: for each operation, assert a non-2xx surfaces `MailgunnerException` exposing `StatusCode` and the verbatim `ResponseBody`, and that no other exception type is thrown (contract C13; SC-007).

**Checkpoint**: Every cross-cutting contract behavior is verified offline.

---

## Phase 8: Polish & Documentation

**Purpose**: Changelog, docs, and full-suite verification.

- [X] T027 [P] Add a CHANGELOG entry under `## [Unreleased]` → `### Added` in `CHANGELOG.md` describing the new `client.Webhooks` (`IMailgunWebhooks`) domain webhook management surface — list/read/create/update/delete over the Mailgun v3 webhook endpoints, the typed `WebhookEventType` (closed set of 7) and `WebhookRegistration` record, the one-URL-across-many-event-types fan-out (sequential, fail-fast, no rollback), and that it reuses the registered client's region/auth and surfaces `MailgunnerException` on non-2xx. Note it is additive (SemVer MINOR), introduces no new runtime dependency and no new exception type.
- [X] T028 [P] Review XML docs on `src/Mailgunner/IMailgunWebhooks.cs`, `src/Mailgunner/WebhookEventType.cs`, `src/Mailgunner/WebhookRegistration.cs`, and the new `IMailgunnerClient.Webhooks` property for completeness (every public member documented; warnings-as-errors will fail the build otherwise); ensure the fan-out fail-fast/no-rollback and not-found→`MailgunnerException` behaviors are documented on the relevant members.
- [X] T029 Run `dotnet build` and `dotnet test` from the repo root; confirm green with no new warnings and that all new `WebhookManagement` tests pass. Then walk the `specs/014-webhook-management/quickstart.md` validation table to confirm every scenario is covered.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories (types, DTOs, JSON context, interface, client wiring, skeleton).
- **User Stories (Phase 3–6)**: All depend on Foundational. Their **implementation** tasks all edit the single file `src/Mailgunner/Internal/MailgunWebhooks.cs`, so they are sequential: T013/T014 (US1) → T017/T018 (US2) → T020 (US3) → T022 (US4). Each story's **test** files are independent (`[P]`).
- **Cross-Cutting Tests (Phase 7)**: Depend on all four operations existing (Phases 3–6).
- **Polish (Phase 8)**: Depends on all desired user stories and cross-cutting tests being complete.

### User Story Dependencies

- **US1 (P1)**: Create (single + fan-out) — the MVP; no dependency on US2–US4.
- **US2 (P2)**: List + read-one — independent of US1 at the API level, but shares `MailgunWebhooks.cs`; reuses the typed model and `SendCoreAsync`.
- **US3 (P3)**: Update — independent; reuses the same per-event-type routing and model.
- **US4 (P3)**: Delete — independent; reuses the same per-event-type routing.

### Within Each User Story

- Tests written first and expected to FAIL before the matching implementation task (Constitution III, TDD).
- Foundational types/DTOs/interface/skeleton before any operation.
- The shared `SendCoreAsync`/URI helpers (T007) before any operation method.

### Parallel Opportunities

- Setup T001 runs alone; Foundational T002, T003, T004, T006 are `[P]` (distinct new files); T009 (mapping test) is `[P]` once T004 exists. T005 (after T004), T007 (after T004–T006), T008 (after T006–T007) are sequential.
- Each story's test files (T010, T011, T012; T015, T016; T019; T021) are `[P]` — distinct files.
- All cross-cutting test files (T023, T024, T025, T026) are `[P]` — distinct files.
- Polish T027 and T028 are `[P]` (CHANGELOG vs source docs).
- The **implementation** tasks on `MailgunWebhooks.cs` (T013, T014, T017, T018, T020, T022) are NOT parallel — same file, incremental.

---

## Parallel Example: User Story 1

```bash
# After Foundational (T002–T009), author US1 tests together (distinct files):
Task: "T010 [US1] create single/multi-URL + error tests in tests/Mailgunner.Tests/WebhookManagement/WebhookCreateTests.cs"
Task: "T011 [US1] fan-out order + fail-fast tests in tests/Mailgunner.Tests/WebhookManagement/WebhookCreateMultiEventTests.cs"
Task: "T012 [US1] create validation tests in tests/Mailgunner.Tests/WebhookManagement/WebhookValidationTests.cs"
# then implement (sequential, same file):
Task: "T013 [US1] CreateAsync(eventType, urls) in src/Mailgunner/Internal/MailgunWebhooks.cs"
Task: "T014 [US1] CreateAsync(eventTypes, url) fan-out in src/Mailgunner/Internal/MailgunWebhooks.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup (T001).
2. Phase 2: Foundational (T002–T009) — types, DTOs, JSON context, interface, client wiring, skeleton, mapping test.
3. Phase 3: User Story 1 (T010–T014) — register single + fan-out.
4. **STOP and VALIDATE**: webhooks can be registered (single and one-URL-across-many) against the stub; partial-failure is fail-fast.

### Incremental Delivery

1. Setup + Foundational → public surface compiles, `client.Webhooks` reachable.
2. US1 → registration (MVP) → validate.
3. US2 → list + read-one → validate.
4. US3 → update → validate.
5. US4 → delete → validate.
6. Cross-cutting tests → routing/errors/cancellation/independence.
7. Polish → CHANGELOG, docs, full `dotnet test` + quickstart walk.

### Parallel Team Strategy

After Foundational completes, the operation methods share one file so they are best implemented by one
developer in sequence (US1 → US2 → US3 → US4); meanwhile a second developer can author the independent
test files (`[P]`) for each story and the cross-cutting suite.

---

## Notes

- [P] tasks = different files, no dependencies. Implementation on the shared `MailgunWebhooks.cs` is intentionally sequential across stories.
- [Story] label maps each task to its user story for traceability.
- Verify tests fail before implementing (Constitution III, TDD).
- Wire surface is **v3** (`v3/{domain}/webhooks`, event-type-keyed); create/update send `multipart/form-data` (`id`/`url`), responses are JSON via source generation. See `research.md` §1–§2.
- No new runtime dependency; no new public exception type (input errors → `ArgumentException`, HTTP errors → `MailgunnerException`).
- Commit after each task or logical group; keep `dotnet build`/`dotnet test` green before each commit.
