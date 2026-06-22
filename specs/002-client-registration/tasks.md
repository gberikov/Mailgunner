---
description: "Task list for Client Registration & Regional Bootstrap"
---

# Tasks: Client Registration & Regional Bootstrap

**Input**: Design documents from `specs/002-client-registration/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/registration-contract.md, quickstart.md

**Tests**: INCLUDED and REQUIRED. Constitution Principle III (Test-First, Network-Free) is
NON-NEGOTIABLE — every behavior lands with offline xUnit tests written before implementation,
exercised through a fake `HttpMessageHandler`. Region-based base-URL selection is an explicitly
required coverage item.

**Organization**: Tasks are grouped by user story (from spec.md) so each story is an
independently testable increment. US1 is the MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 (omitted for Setup, Foundational, Polish)

## Path Conventions

Single-library layout: production code under `src/Mailgunner/`, tests under
`tests/Mailgunner.Tests/`. Shared build config (`Directory.Build.props`,
`Directory.Packages.props`) already exists from the scaffold. Test folders are created
implicitly by the first file written into them.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Wire the one new dependency and the test-visibility seam.

- [X] T001 Add `<PackageReference Include="Microsoft.Extensions.Http" />` to the library project in `src/Mailgunner/Mailgunner.csproj` (version is centrally managed in `Directory.Packages.props`; no version attribute).
- [X] T002 Add `[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Mailgunner.Tests")]` in a new `src/Mailgunner/AssemblyInfo.cs` so tests can assert on internal members.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Story-agnostic types and test infrastructure that every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Create the public `MailgunRegion` enum (`Us`, `Eu`) with XML docs naming each region's host in `src/Mailgunner/MailgunRegion.cs`.
- [X] T004 [P] Create the public `MailgunnerOptions` POCO (`Domain`, `SendingKey`, `Region`) with XML docs (note `SendingKey` is a secret) in `src/Mailgunner/MailgunnerOptions.cs`.
- [X] T005 [P] Create the public `IMailgunnerClient` entry-point interface (no operational members; XML-documented as the foundation) in `src/Mailgunner/IMailgunnerClient.cs`.
- [X] T006 [P] Create the `internal static MailgunRegionEndpoints` map (`Us → https://api.mailgun.net/`, `Eu → https://api.eu.mailgun.net/`, trailing slash) in `src/Mailgunner/Internal/MailgunRegionEndpoints.cs`.
- [X] T007 Create the `internal sealed MailgunnerClient : IMailgunnerClient` typed-client class taking an `HttpClient` and exposing it via an `internal HttpClient HttpClient { get; }` property in `src/Mailgunner/MailgunnerClient.cs` (depends on T005).
- [X] T008 [P] Create the `CapturingHttpMessageHandler` test fake (records the `HttpRequestMessage`, returns a canned `200`) in `tests/Mailgunner.Tests/Fakes/CapturingHttpMessageHandler.cs`.

**Checkpoint**: Shared types and the fake transport exist; user stories can begin.

---

## Phase 3: User Story 1 - Register and resolve a ready client (Priority: P1) 🎯 MVP

**Goal**: A single `AddMailgunner(...)` call registers a resolvable, ready `IMailgunnerClient`
whose requests carry HTTP Basic auth derived from the sending key.

**Independent Test**: Register valid settings, build the provider, resolve `IMailgunnerClient`
→ non-null/ready; drive a request through the fake transport → it carries
`Authorization: Basic base64("api:" + key)` and hits no real network. Resolving twice and
calling `AddMailgunner` twice both leave a usable client (last call wins).

### Tests for User Story 1 (write first; must FAIL before implementation) ⚠️

- [X] T009 [P] [US1] `ClientResolutionTests` — valid settings → `GetRequiredService<IMailgunnerClient>()` non-null (C-01); repeated resolution yields a usable client (C-02); calling `AddMailgunner` twice with different settings leaves it resolvable with the last settings (C-09) — in `tests/Mailgunner.Tests/Registration/ClientResolutionTests.cs`.
- [X] T010 [P] [US1] `AuthenticationTests` — drive a request through `CapturingHttpMessageHandler` (injected via `ConfigurePrimaryHttpMessageHandler`) and assert the captured request carries `Authorization: Basic base64("api:" + sendingKey)` and that no real network call occurs (C-05, C-11) — in `tests/Mailgunner.Tests/Registration/AuthenticationTests.cs`.

### Implementation for User Story 1

- [X] T011 [US1] Create `MailgunnerServiceCollectionExtensions` (namespace `Microsoft.Extensions.DependencyInjection`) with both `AddMailgunner` overloads (explicit `domain/sendingKey/region` and `Action<MailgunnerOptions>`), registering `AddOptions<MailgunnerOptions>().Configure(...).ValidateOnStart()` and `AddHttpClient<IMailgunnerClient, MailgunnerClient>(...)`, returning `IHttpClientBuilder`, with XML docs, in `src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs` (depends on T003–T008).
- [X] T012 [US1] In the typed-client configure delegate, set `client.BaseAddress` from `MailgunRegionEndpoints[options.Region]` and set `client.DefaultRequestHeaders.Authorization` to `Basic base64("api:" + options.SendingKey)`, in `src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs` (depends on T011).

**Checkpoint**: US1 fully functional — resolvable, authenticated client; T009/T010 green. MVP deliverable.

---

## Phase 4: User Story 2 - Requests target the correct regional host (Priority: P2)

**Goal**: EU-configured clients hit the EU host and US-configured clients hit the US host; the
region/domain mismatch is documented as a known failure mode.

**Independent Test**: Register with `Eu` and with `Us`; drive a request through the fake
transport in each case → request host is `api.eu.mailgun.net` / `api.mailgun.net` respectively.

### Tests for User Story 2 (write first; must FAIL before implementation) ⚠️

- [X] T013 [P] [US2] `RegionRoutingTests` — EU registration → captured request host `api.eu.mailgun.net` (C-03); US registration → `api.mailgun.net` (C-04); both via the fake transport, no network (C-11) — in `tests/Mailgunner.Tests/Registration/RegionRoutingTests.cs`.

### Implementation for User Story 2

- [X] T014 [US2] Document the region/domain mismatch failure mode (FR-010) in the **shipped XML docs**: add `<remarks>` to `MailgunRegion` (`src/Mailgunner/MailgunRegion.cs`) and to `AddMailgunner` (`src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs`) explaining that a mismatched region routes to a host where the domain returns 404 (routing wiring already delivered in T012; the consumer-facing README "Regions" note is handled once in T018; depends on T012).

**Checkpoint**: US1 + US2 both pass independently; routing verified across both regions.

---

## Phase 5: User Story 3 - Invalid configuration fails fast (Priority: P2)

**Goal**: Missing/blank domain or sending key, and undefined region, are rejected at startup
with a clear, secret-safe `OptionsValidationException` before any request is attempted.

**Independent Test**: Register settings with a blank domain (and separately a blank key, and an
undefined region); trigger startup validation via `IStartupValidator.Validate()` → throws
`OptionsValidationException` naming the offending setting; the sending-key value never appears.

### Tests for User Story 3 (write first; must FAIL before implementation) ⚠️

- [X] T015 [P] [US3] `ConfigurationValidationTests` — blank/whitespace/missing `Domain` → throws naming the domain (C-06); blank `SendingKey` → throws naming the key with the **value absent** from the message (C-07, FR-011); undefined `Region` → throws naming the region (C-08); assert the thrown type is `OptionsValidationException`, not `MailgunnerException` (C-10); validation triggered via `IStartupValidator.Validate()` with no network — in `tests/Mailgunner.Tests/Registration/ConfigurationValidationTests.cs`.

### Implementation for User Story 3

- [X] T016 [P] [US3] Create the `internal sealed MailgunnerOptionsValidator : IValidateOptions<MailgunnerOptions>` — trims `Domain`/`SendingKey`, fails on null/empty/whitespace naming the offending setting (never echoing the key), fails when `Region` is not `Enum.IsDefined`, and aggregates all failures into one `ValidateOptionsResult` — in `src/Mailgunner/Internal/MailgunnerOptionsValidator.cs`.
- [X] T017 [US3] Register the validator in `AddMailgunner` (`services.AddSingleton<IValidateOptions<MailgunnerOptions>, MailgunnerOptionsValidator>()`) ensuring `.ValidateOnStart()` is in effect, in `src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs` (depends on T011, T016).

**Checkpoint**: All three user stories independently functional and tested.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T018 [P] Update `README.md` in a single pass: add a "Getting started / registration" section (both `AddMailgunner` overloads; recommend a Domain Sending Key per Principle V) **and** a "Regions" subsection describing the region/domain mismatch 404 (FR-010); confirm the existing non-affiliation notice is present.
- [X] T019 [P] Add an `Unreleased` entry to `CHANGELOG.md` (Keep a Changelog) describing the DI client registration, regional routing, and startup validation.
- [X] T020 Verify multi-target build: `dotnet build` is clean with warnings-as-errors on both `net8.0` and `netstandard2.0`, and every public member has XML docs (no CS1591).
- [X] T021 Run the `quickstart.md` validation scenarios end-to-end: `dotnet test` green with no network and no credentials.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–5)**: All depend on Foundational. US1 is the MVP and establishes
  `AddMailgunner`; US2 and US3 extend it.
- **Polish (Phase 6)**: Depends on the desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Depends only on Foundational. Creates `AddMailgunner` + auth + base-address wiring.
- **US2 (P2)**: Depends on Foundational; its routing wiring (T012) is delivered in US1, so US2
  adds routing tests + the mismatch documentation. Independently testable.
- **US3 (P2)**: Depends on Foundational; T017 modifies the `AddMailgunner` file created in US1
  (T011). Validator (T016) is independent and can be written in parallel. Independently testable.

### Within Each User Story

- Tests are written first and MUST fail before implementation (TDD, Principle III).
- Shared types (Phase 2) before the registration extension.
- `AddMailgunner` skeleton (T011) before its base-address/auth wiring (T012) and before
  validator registration (T017).

### Parallel Opportunities

- Foundational: T003, T004, T005, T006, T008 are all `[P]` (different files); T007 depends on T005.
- Tests across stories (T009/T010, T013, T015) are `[P]` — different files, no shared state.
- T016 (validator class) is `[P]` and can be authored alongside US1/US2 work; only its
  registration (T017) touches the shared extensions file.
- Polish T018/T019 are `[P]`.

---

## Parallel Example: Foundational + US1 tests

```bash
# Foundational types (different files, no inter-dependencies except T007←T005):
Task: "T003 Create MailgunRegion enum in src/Mailgunner/MailgunRegion.cs"
Task: "T004 Create MailgunnerOptions in src/Mailgunner/MailgunnerOptions.cs"
Task: "T005 Create IMailgunnerClient in src/Mailgunner/IMailgunnerClient.cs"
Task: "T006 Create MailgunRegionEndpoints in src/Mailgunner/Internal/MailgunRegionEndpoints.cs"
Task: "T008 Create CapturingHttpMessageHandler in tests/Mailgunner.Tests/Fakes/CapturingHttpMessageHandler.cs"

# US1 tests (write first, expect failure):
Task: "T009 ClientResolutionTests in tests/Mailgunner.Tests/Registration/ClientResolutionTests.cs"
Task: "T010 AuthenticationTests in tests/Mailgunner.Tests/Registration/AuthenticationTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup → 2. Phase 2: Foundational → 3. Phase 3: US1.
4. **STOP and VALIDATE**: resolvable, authenticated client; T009/T010 green offline.
5. This is a demoable MVP — the entry point every later capability builds on.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → resolvable authenticated client (MVP).
3. US2 → verified regional routing + documented mismatch.
4. US3 → fail-fast, secret-safe startup validation.
5. Polish → docs, CHANGELOG, multi-target & quickstart verification.

---

## Notes

- `[P]` = different files, no dependencies. `[Story]` maps each task to its user story.
- Verify each story's tests fail before implementing; keep the suite offline (no network, no credentials).
- The sending key must never appear in errors/logs/diagnostics (FR-011 / Principle V).
- Config validation surfaces as `OptionsValidationException`, never `MailgunnerException` (clarification Q2).
- Polly resilience and message sending are intentionally out of scope (separate features).
- Commit after each task or logical group (Conventional Commits).
