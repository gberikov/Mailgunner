---

description: "Task list for Named Mailgunner Clients"
---

# Tasks: Named Mailgunner Clients

**Input**: Design documents from `specs/012-named-clients/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/public-api.md, quickstart.md

**Tests**: REQUIRED — constitution Principle III (NON-NEGOTIABLE) mandates network-free xUnit tests for all new/changed behavior, via a fake `HttpMessageHandler`.

**Organization**: Grouped by user story. The three P1 stories (US1 register, US2 resolve, US3 isolate) together form the MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US5 from spec.md
- All paths are repository-relative.

## Path Conventions

- Library: `src/Mailgunner/`
- Tests: `tests/Mailgunner.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Dependency and test scaffolding the feature needs.

- [x] T001 [P] Add the `Microsoft.Extensions.Options.ConfigurationExtensions` package (for the config-section binding overload, FR-021): add the version to `Directory.Packages.props` and a `<PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />` to `src/Mailgunner/Mailgunner.csproj` (constitution Principle I deviation — already justified in plan Complexity Tracking).
- [x] T002 [P] Create the test area `tests/Mailgunner.Tests/Registration/` and reuse the existing fake/recording `HttpMessageHandler` test transport (used by prior send/suppression tests) so each named client can be given its own capturing primary handler via `IHttpClientBuilder.ConfigurePrimaryHttpMessageHandler`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared named-client machinery every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T003 [P] Add the public interface `IMailgunnerClientFactory` with `IMailgunnerClient Get(string name)` (full XML docs per Principle IV) in `src/Mailgunner/IMailgunnerClientFactory.cs`.
- [x] T004 [P] Add the internal `NamedClientRegistry` (ordinal `HashSet<string>`; `Add(name)`→bool, `Contains(name)`, `RegisteredNames` snapshot, `static HttpClientName(name) => "Mailgunner:" + name`) in `src/Mailgunner/Internal/NamedClientRegistry.cs`.
- [x] T005 Refactor `src/Mailgunner/Internal/MailgunResilienceHandler.cs`: add an internal constructor `(TimeProvider, RetryPolicyOptions, ILogger<MailgunResilienceHandler>, IRetryRandom)` and have the existing `IOptions<MailgunnerOptions>` constructor delegate to it (`options.Value.Retry`). Keep the existing behavior identical so the unnamed path is unchanged.
- [x] T006 Implement the internal `MailgunnerClientFactory : IMailgunnerClientFactory` (depends on T003, T004) in `src/Mailgunner/Internal/MailgunnerClientFactory.cs`: ctor takes `IHttpClientFactory`, `IOptionsMonitor<MailgunnerOptions>`, `NamedClientRegistry`; `Get` builds a `MailgunnerClient` from `CreateClient(NamedClientRegistry.HttpClientName(name))` + `Options.Create(monitor.Get(name))`. (Unknown-name guard is added in US5/T028.)
- [x] T007 Add a private shared helper in `src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs` (depends on T004, T005, T006) that, given a name: get-or-adds `NamedClientRegistry` to the services and records the name; registers named `MailgunnerOptions` (`AddOptions<MailgunnerOptions>(name)...ValidateOnStart()`) and the shared `MailgunnerOptionsValidator`; `TryAddSingleton<IMailgunnerClientFactory, MailgunnerClientFactory>()` plus `TimeProvider`/`IRetryRandom`; and `AddHttpClient(NamedClientRegistry.HttpClientName(name), …)` configuring base URL + Basic auth from `monitor.Get(name)` and `AddHttpMessageHandler(sp => new MailgunResilienceHandler(timeProvider, monitor.Get(name).Retry, logger, random))`; returns the named `IHttpClientBuilder`.

**Checkpoint**: Named machinery exists; user story implementation can begin.

---

## Phase 3: User Story 1 - Register several named clients side by side (Priority: P1) 🎯 MVP

**Goal**: Multiple named clients coexist in one container via explicit, callback, and configuration-section registration forms; none overwrites another.

**Independent Test**: Register two named clients with distinct settings (and one via config section), build the container, confirm both names resolve to ready clients.

### Tests for User Story 1 ⚠️ (write first, must fail)

- [x] T008 [P] [US1] Test that the explicit-args and callback forms register two distinct names that both resolve via the factory, and that repeated resolution is stable, in `tests/Mailgunner.Tests/Registration/NamedRegistrationTests.cs`.
- [x] T009 [P] [US1] Test that the configuration-section binding overload registers a resolvable named client bound from an in-memory `IConfiguration` section, in `tests/Mailgunner.Tests/Registration/NamedConfigurationBindingTests.cs`.

### Implementation for User Story 1

- [x] T010 [US1] Add `AddMailgunner(this IServiceCollection, string name, string domain, string sendingKey, MailgunRegion region)` (delegates to the callback form) in `src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs` (uses T007 helper).
- [x] T011 [US1] Add `AddMailgunner(this IServiceCollection, string name, Action<MailgunnerOptions> configure)` in the same file (configures named options, then calls the T007 helper).
- [x] T012 [US1] Add `AddMailgunner(this IServiceCollection, string name, IConfiguration configuration)` in the same file (`AddOptions<MailgunnerOptions>(name).Bind(configuration)`, then the T007 helper); depends on T001.

**Checkpoint**: Multiple named clients register and resolve (happy path).

---

## Phase 4: User Story 2 - Resolve a specific client by name (Priority: P1) 🎯 MVP

**Goal**: `IMailgunnerClientFactory.Get(name)` returns the full client (send + suppressions) bound to that name.

**Independent Test**: Register two names, resolve each by name, confirm the returned instance exposes send and suppression capabilities.

### Tests for User Story 2 ⚠️ (write first, must fail)

- [x] T013 [P] [US2] Test that `factory.Get(name)` returns a non-null full `IMailgunnerClient` whose `Suppressions` is usable, that name matching is ordinal/case-sensitive, and that resolving each of several names yields exactly that name's client, in `tests/Mailgunner.Tests/Registration/NamedResolutionTests.cs`.

### Implementation for User Story 2

- [x] T014 [US2] Confirm/finish `MailgunnerClientFactory.Get` in `src/Mailgunner/Internal/MailgunnerClientFactory.cs` returns the complete `IMailgunnerClient` surface (send + suppressions) and is registered as a singleton via the T007 helper; ensure ordinal matching against the registry.

**Checkpoint**: Register + resolve works end-to-end — minimal usable feature.

---

## Phase 5: User Story 3 - Full isolation between clients (Priority: P1) 🎯 MVP

**Goal**: Each client (named or unnamed) uses only its own host/domain/auth/retry; nothing leaks.

**Independent Test**: Drive a request from each of two differently-configured names via per-name fake handlers and assert host/domain/auth; assert retry budgets are independent.

### Tests for User Story 3 ⚠️ (write first, must fail)

- [x] T015 [P] [US3] Test that two named clients (one US, one EU, different keys/domains) each send to their own regional host + domain path with their own `Basic base64("api:"+key)` auth, in `tests/Mailgunner.Tests/Registration/NamedRoutingIsolationTests.cs`.
- [x] T016 [P] [US3] Test retry isolation: name A with `MaxRetryAttempts=0` issues a single attempt while name B with `MaxRetryAttempts>0` retries its own budget on a retryable status, in `tests/Mailgunner.Tests/Registration/NamedRetryIsolationTests.cs`.
- [x] T017 [P] [US3] Test that an unnamed registration coexisting with named ones shows no cross-contamination of host/domain/auth/retry in either direction, in `tests/Mailgunner.Tests/Registration/NamedUnnamedIsolationTests.cs`.

### Implementation for User Story 3

- [x] T018 [US3] Verify the T007 helper wires each named client's resilience handler with that name's `monitor.Get(name).Retry` (per-name pipeline), adjusting the helper if needed in `src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs`.

**Checkpoint**: All three P1 stories complete — full MVP delivered.

---

## Phase 6: User Story 4 - Backward compatibility (Priority: P2)

**Goal**: The existing unnamed registration is unchanged and coexists with named clients; no implicit default.

**Independent Test**: Use unnamed registration as before (still resolves identically), add a named one (unnamed still works); with only named clients, a bare `IMailgunnerClient` request fails clearly.

### Tests for User Story 4 ⚠️ (write first, must fail)

- [x] T019 [P] [US4] Test that the unnamed `AddMailgunner(...)` still resolves `IMailgunnerClient` with identical routing/auth and remains resolvable after a named registration is added, in `tests/Mailgunner.Tests/Registration/UnnamedBackwardCompatTests.cs`.
- [x] T020 [P] [US4] Test that when only named clients are registered, resolving a bare `IMailgunnerClient` throws the standard DI resolution error while `factory.Get(name)` works (FR-022), in `tests/Mailgunner.Tests/Registration/BareUnnamedFallbackTests.cs`.

### Implementation for User Story 4

- [x] T021 [US4] Verify the named registration helper (T007) does NOT register the unnamed/default `IMailgunnerClient` typed client, so bare injection naturally fails when no unnamed registration exists; correct the helper if it leaks a default in `src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs`.

**Checkpoint**: Unnamed and named coexist; no implicit default.

---

## Phase 7: User Story 5 - Invalid configuration fails fast, secret-safe (Priority: P2)

**Goal**: Blank/duplicate names and bad per-name settings and unknown lookups fail with clear errors; no error ever exposes a sending key.

**Independent Test**: Exercise each invalid case and assert the error type/message and that no sending key appears.

### Tests for User Story 5 ⚠️ (write first, must fail)

- [x] T022 [P] [US5] Test that a blank/whitespace name (any overload) throws `ArgumentException` at registration naming the name, in `tests/Mailgunner.Tests/Registration/NamedValidationTests.cs`.
- [x] T023 [P] [US5] Test that two registrations under the same name throw `ArgumentException` at registration naming the duplicate, in `tests/Mailgunner.Tests/Registration/NamedDuplicateTests.cs`.
- [x] T024 [P] [US5] Test that a named client with blank domain/key or undefined region fails at `ValidateOnStart` with an `OptionsValidationException` identifying name + setting, and assert the thrown type is NOT `MailgunnerException` (FR-016: validation failures are standard .NET exceptions only), in `tests/Mailgunner.Tests/Registration/NamedValidateOnStartTests.cs`.
- [x] T025 [P] [US5] Test that `factory.Get(unknownName)` throws `ArgumentException` that names the unknown name and lists registered names (never null/default), that `Get(null)` and `Get("")`/whitespace also throw `ArgumentException` (FR-004 contract), and that none of these is a `MailgunnerException` (FR-016), in `tests/Mailgunner.Tests/Registration/UnknownNameTests.cs`.
- [x] T026 [P] [US5] Test secret hygiene: trigger each error above with a real-looking key and assert no exception message contains the sending key value, in `tests/Mailgunner.Tests/Registration/NamedSecretHygieneTests.cs`.

### Implementation for User Story 5

- [x] T027 [US5] Add blank-name and duplicate-name guards to the registration helper/overloads (throw `ArgumentException`, secret-safe) in `src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs`.
- [x] T028 [US5] Add the name guards to `MailgunnerClientFactory.Get` in `src/Mailgunner/Internal/MailgunnerClientFactory.cs`: reject null/empty/whitespace `name` with `ArgumentException`, and reject an unknown name with an `ArgumentException` that names it and lists `RegisteredNames`; never return null/default and never throw `MailgunnerException`.

**Checkpoint**: All error paths clear and secret-safe.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, packaging, and full validation.

- [x] T029 [P] Add an `Added` entry for named clients (factory + three named overloads, SemVer MINOR) to `CHANGELOG.md`.
- [x] T030 [P] Document named clients in `README.md`: registering multiple named clients, resolving via `IMailgunnerClientFactory.Get(name)`, ordinal/case-sensitive names, config-section binding, and the per-name region/domain mismatch note.
- [x] T031 [P] Verify XML docs on every new public type/member and that the build is clean under `TreatWarningsAsErrors`.
- [x] T032 Build both target frameworks (`net8.0`, `netstandard2.0`) and run `dotnet test` — must be green offline with no Mailgun credentials present. This run is also the FR-019 regression guard: the existing send/suppression wire-format tests must still pass unchanged.
- [x] T033 Run all `quickstart.md` validation scenarios (currently scenarios 1–12) and confirm each passes; if the quickstart scenario list has changed, run whatever it now lists.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; BLOCKS all user stories. Internal order: T003, T004 [P] → T005 [P] → T006 (needs T003/T004) → T007 (needs T004/T005/T006).
- **User Stories (Phases 3–7)**: all depend on Foundational. US1/US2/US3 (P1) form the MVP; US4/US5 (P2) follow.
- **Polish (Phase 8)**: depends on all targeted stories.

### User Story Dependencies

- **US1 (P1)**: needs Foundational. Its resolution assertions use the factory (T006).
- **US2 (P1)**: needs Foundational (factory). Independently testable once names are registrable.
- **US3 (P1)**: needs Foundational + at least US1 registration to have clients to isolate.
- **US4 (P2)**: needs the existing unnamed path (unchanged) + named registration (US1).
- **US5 (P2)**: needs US1 (registration helper) and US2 (factory) to attach guards to.

### Within Each User Story

- Tests are written first and must fail before implementation (constitution Principle III).
- Implementation files are shared (`MailgunnerServiceCollectionExtensions.cs`, `MailgunnerClientFactory.cs`), so same-file tasks within/across stories are sequential, not [P].

### Parallel Opportunities

- Setup: T001, T002 [P].
- Foundational: T003, T004 [P] (then T005, then T006, then T007).
- All test tasks within a story (T008/T009; T015/T016/T017; T022–T026) are [P] — separate test files.
- Implementation edits to the same source file are sequential.

---

## Parallel Example: User Story 3

```bash
# Author the isolation tests together (separate files):
Task: "Routing/auth isolation test in tests/Mailgunner.Tests/Registration/NamedRoutingIsolationTests.cs"
Task: "Retry isolation test in tests/Mailgunner.Tests/Registration/NamedRetryIsolationTests.cs"
Task: "Named vs unnamed isolation test in tests/Mailgunner.Tests/Registration/NamedUnnamedIsolationTests.cs"
```

---

## Implementation Strategy

### MVP First (P1 stories)

1. Phase 1 Setup → Phase 2 Foundational.
2. US1 (register) + US2 (resolve) → register-and-resolve works.
3. US3 (isolation) → per-client guarantees verified.
4. **STOP and VALIDATE**: the MVP (multiple isolated named clients, resolved by name) is complete and demoable.

### Incremental Delivery

1. Foundation ready → MVP (US1+US2+US3).
2. Add US4 (backward-compat guarantees) → test → demo.
3. Add US5 (clear, secret-safe error handling) → test → demo.
4. Polish: CHANGELOG, README, full build/test/quickstart.

---

## Notes

- [P] = different files, no incomplete dependencies.
- The unnamed code path stays byte-for-byte unchanged (T005 keeps the `IOptions` ctor; no edits to the unnamed registration).
- Reserve `MailgunnerException` for HTTP responses; all configuration/lookup failures are `ArgumentException`/`OptionsValidationException`.
- Commit after each task or logical group; keep every commit green for both target frameworks.
