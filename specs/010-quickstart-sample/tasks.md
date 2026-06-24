---
description: "Task list for Quickstart & First-Release Readiness"
---

# Tasks: Quickstart & First-Release Readiness

**Input**: Design documents from `specs/010-quickstart-sample/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: One **offline** test is included — not optional gold-plating but mandated by constitution
Principle III ("new or changed behavior MUST land with tests") and called out in plan.md: the
sample's credential-presence resolver (FR-003 / SC-002). The live **send** path is the single
environment-gated check embodied by the sample itself and is **not** an automated test.

**Organization**: Tasks are grouped by user story (P1→P3) for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (from spec.md)
- Exact file paths are included in each task.

## Path Conventions

Single-library .NET repo: library in `src/Mailgunner/`, offline tests in `tests/Mailgunner.Tests/`,
new non-packable console sample in `samples/Mailgunner.Sample/`. Repo-root docs/metadata:
`README.md`, `CHANGELOG.md`, `Mailgunner.slnx`, `Directory.Packages.props`, `Directory.Build.props`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Scaffold the net-new, non-packable sample project and wire it into the solution. (Used
by US1 only; US2/US3 are documentation-only and do not depend on this phase.)

- [X] T001 Create `samples/Mailgunner.Sample/Mailgunner.Sample.csproj` — `<OutputType>Exe</OutputType>`, `<TargetFramework>net8.0</TargetFramework>`, `<IsPackable>false</IsPackable>`, `<GenerateDocumentationFile>false</GenerateDocumentationFile>`, and a `<ProjectReference Include="..\..\src\Mailgunner\Mailgunner.csproj" />`. (Warnings-as-errors stays inherited from `Directory.Build.props`.)
- [X] T002 Add a centrally-pinned, sample-only `<PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.9" />` to `Directory.Packages.props` (library catalog unchanged) and reference it (`<PackageReference Include="Microsoft.Extensions.Hosting" />`) in `samples/Mailgunner.Sample/Mailgunner.Sample.csproj`. **Pin to 10.0.9 to match the existing `Microsoft.Extensions.Http` 10.0.9 line** (same `Microsoft.Extensions.*` 10.0.x train, which ships `net8.0` assets, so the sample's `net8.0` target restores cleanly under warnings-as-errors); if a restore/NU warning surfaces on `net8.0`, fall back to the latest `9.x`.
- [X] T003 [P] Register the sample in `Mailgunner.slnx`: add a `<Folder Name="/samples/">` containing `<Project Path="samples/Mailgunner.Sample/Mailgunner.Sample.csproj" />`.
- [X] T004 [P] Create `samples/Mailgunner.Sample/appsettings.json` with **non-secret** placeholders only (e.g. `"Mailgun": { "Region": "Us", "Template": "conference-invitation", "Domain": "", "SendingKey": "" }`) and set it to copy to output in the csproj (`<None Update="appsettings.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>`). No credentials.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Enable the offline test suite to exercise the sample's resolver.

**⚠️ CRITICAL**: Blocks US1's test (T006). US2/US3 do not depend on this phase.

- [X] T005 Add a `<ProjectReference Include="..\..\samples\Mailgunner.Sample\Mailgunner.Sample.csproj" />` to `tests/Mailgunner.Tests/Mailgunner.Tests.csproj` so the test project can reach the sample's **public** `SampleConfiguration` resolver (no `InternalsVisibleTo` needed — see T007).

**Checkpoint**: Sample project builds and is referenceable by tests — US1 can begin.

---

## Phase 3: User Story 1 - Reach a working personalized send from a runnable sample (Priority: P1) 🎯 MVP

**Goal**: A developer supplies sandbox credentials via configuration and runs the sample to perform a
real personalized conference-invitation batch send (per-recipient name/ticket/link); with no
credentials it exits cleanly naming the missing settings.

**Independent Test**: With valid sandbox credentials, `dotnet run --project samples/Mailgunner.Sample`
delivers a personalized batch where each recipient's fields differ; with credentials absent it makes
no request and names exactly which settings are missing and where to supply them.

### Tests for User Story 1 (constitution-mandated, offline) ⚠️

> Write this test FIRST and ensure it FAILS before implementing T007.

- [X] T006 [P] [US1] Offline unit test in `tests/Mailgunner.Tests/Sample/SampleConfigurationTests.cs`: complete config resolves to a typed configuration; missing `Mailgun:Domain` / `Mailgun:SendingKey` / `Mailgun:Region` each yields a result listing the exact missing key(s) + guidance and **never** resolves; a partial config never resolves; an unparseable `Mailgun:Region` is reported as missing/invalid. No network, no live send (FR-003 / SC-002).

### Implementation for User Story 1

- [X] T007 [P] [US1] Implement the pure, **public** resolver `SampleConfiguration` in `samples/Mailgunner.Sample/SampleConfiguration.cs`: given an `IConfiguration`, return either a resolved settings object (Domain, SendingKey, Region, From defaulting to `postmaster@{Domain}`, Template defaulting to `conference-invitation`, recipient addresses) **or** an ordered list of missing keys with where-to-supply guidance. No I/O, no network (data-model.md §1).
- [X] T008 [P] [US1] Implement `ConferenceInvitation` in `samples/Mailgunner.Sample/ConferenceInvitation.cs`: build 2–3 `BatchRecipient` entries whose `Variables` carry **visible, in-source** `name`/`ticket`/`link` paired to the configured addresses, and set the global `MailgunBatchMessage.TemplateVariables` bridge (`name→"%recipient.name%"`, `ticket→"%recipient.ticket%"`, `link→"%recipient.link%"`) (data-model.md §2, research §3).
- [X] T009 [US1] Implement `samples/Mailgunner.Sample/Program.cs`: build `IConfiguration` (environment variables + optional user-secrets + `appsettings.json`); resolve via `SampleConfiguration`; **if missing** → print each missing setting and where to supply it and `return 0` (no host start, no send); **else** `AddMailgunner(domain, sendingKey, region)`, resolve `IMailgunnerClient`, compose the `MailgunBatchMessage` (template + bridge + recipients from `ConferenceInvitation`), `await SendBatchAsync`, and print one success line (id + status) per chunk; surface `MailgunnerException` (status + body) readably with a region/authorized-recipient hint (depends on T007, T008) (contracts/sample-runtime-contract.md).
- [X] T010 [US1] Verify offline: run `dotnet test Mailgunner.slnx -c Release` (T006 passes) and `dotnet build Mailgunner.slnx -c Release` with **no** Mailgun credentials present — both green (SC-006 / FR-004); confirm `dotnet run --project samples/Mailgunner.Sample` with vars unset prints the missing-settings message and exits 0.

**Checkpoint**: US1 fully functional — the MVP runnable sample + offline guarantee are in place.

---

## Phase 4: User Story 2 - Understand the library from a copy-paste README quickstart (Priority: P2)

**Goal**: A reader finds, in the README, a single copy-paste quickstart (registration → personalized
send) plus regions, suppression/unsubscribe, and a non-affiliation disclaimer — without leaving the
README.

**Independent Test**: Copy the quickstart block, adapt only domain/key/recipients, and get a
compiling personalized send; the regions, suppression/unsubscribe, and disclaimer sections are each
present and self-contained. (Doc-only — independent of Phases 1–3.)

### Implementation for User Story 2

- [X] T011 [US2] Add a **single copy-paste quickstart** to `README.md` taking the reader from `AddMailgunner` registration → resolve `IMailgunnerClient` → build the conference-invitation `MailgunBatchMessage` (template + per-recipient `name`/`ticket`/`link` + global bridge) → `SendBatchAsync`, using the **same** variable names and scenario as the sample (FR-005, FR-011; contracts/docs-content-contract.md).
- [X] T012 [US2] Add a **"Run the sample"** section to `README.md`: how to supply sandbox credentials via env/user-secrets, the one-time **stored Handlebars template** prerequisite (body referencing `{{name}}`/`{{ticket}}`/`{{link}}`), and the no-credentials clean-skip behavior (FR-001, FR-003, FR-012).
- [X] T013 [P] [US2] Verify/align the existing **Regions** (FR-006), **Suppression lists / unsubscribe** (FR-007), and **Disclaimer** (FR-008) sections in `README.md` for consistency with the new quickstart, and refresh the top **status banner** to reflect the `0.1.0` first release (no longer "foundation scaffold; functionality delivered later").

**Checkpoint**: A README-only reader can self-qualify the library (SC-003).

---

## Phase 5: User Story 3 - Discover and trust the package, and see the first release recorded (Priority: P3)

**Goal**: The package presentation carries the README and a `mailgun` discovery tag, and the
changelog records a dated, versioned first release.

**Independent Test**: Package metadata carries the README and tags including `mailgun`; the changelog
has a dated, versioned `0.1.0` entry rather than only an open "Unreleased" section. (Doc/metadata-only
— independent of Phases 1–4.)

### Implementation for User Story 3

- [X] T014 [US3] In `CHANGELOG.md`, promote `## [Unreleased]` → `## [0.1.0] - 2026-06-24` with `### Added` enumerating the shipped capabilities (DI registration/regions/auth, single + templated send, personalized batch, send options, suppressions, webhook verification, retry/backoff) **plus** the new quickstart + runnable conference-invitation sample; add a fresh empty `## [Unreleased]` on top; update link refs to `[Unreleased]: https://github.com/gberikov/Mailgunner/compare/v0.1.0...HEAD` and `[0.1.0]: https://github.com/gberikov/Mailgunner/releases/tag/v0.1.0` (FR-010; Keep a Changelog).
- [X] T015 [P] [US3] Verify `Directory.Build.props` carries `PackageReadmeFile=README.md` and `PackageTags` including `mailgun` (FR-009), and — since this ships `0.1.0` — confirm the constitution-IV release settings are present (`Deterministic`, `EmbedUntrackedSources`, `IncludeSymbols`/`snupkg`, SDK-implicit SourceLink, `ContinuousIntegrationBuild` on CI). Optionally `dotnet pack src/Mailgunner/Mailgunner.csproj -c Release` and confirm the `.nupkg` embeds `README.md`, the nuspec `<tags>` include `mailgun`, and a `.snupkg` symbol package is produced.

**Checkpoint**: Registry browsers see the README, the `mailgun` tag, and a recorded first release (SC-005).

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: End-to-end validation and consistency.

- [X] T016 [P] Execute `specs/010-quickstart-sample/quickstart.md` end-to-end: the no-credentials skip path and the offline build/test path (no network, no credentials).
- [X] T017 Cross-check that the README quickstart and the runnable sample demonstrate the **identical** conference-invitation scenario — same template name and `name`/`ticket`/`link` variable names (FR-011 consistency; Edge "README drift").
- [X] T018 [P] Final gate: `dotnet build Mailgunner.slnx -c Release` and `dotnet test Mailgunner.slnx -c Release` green with no credentials; commit using Conventional Commits.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately. (Scaffolds the sample for US1.)
- **Foundational (Phase 2)**: Depends on Setup — blocks US1's test (T006).
- **US1 (Phase 3)**: Depends on Setup + Foundational.
- **US2 (Phase 4)** and **US3 (Phase 5)**: **Documentation-only — independent of Phases 1–3 and of each other.** They can start immediately and run fully in parallel with US1.
- **Polish (Phase 6)**: Depends on all desired stories being complete.

### User Story Dependencies

- **US1 (P1)**: Needs the sample project (Setup) + test wiring (Foundational). No dependency on US2/US3.
- **US2 (P2)**: Independent (README edits only).
- **US3 (P3)**: Independent (CHANGELOG + metadata only).

### Within US1

- Write the offline test (T006) and see it FAIL → implement resolver (T007) → it passes.
- Resolver (T007) and data (T008) are parallel; `Program.cs` (T009) depends on both; verify (T010) last.

### Parallel Opportunities

- Setup: T003 and T004 are [P] (different files).
- Across stories: once Setup+Foundational are done, **US1, US2, and US3 can all proceed in parallel** (US2/US3 don't even need Setup).
- Within US1: T006, T007, T008 are [P] (separate files); T009 joins them.

---

## Parallel Example: cross-story kickoff

```bash
# US2 and US3 are doc-only and need nothing from Setup/Foundational — start them alongside US1:
Task: "T011 [US2] Add single copy-paste quickstart to README.md"
Task: "T014 [US3] Promote CHANGELOG.md [Unreleased] → [0.1.0] - 2026-06-24"

# Within US1, after Setup+Foundational, launch the independent files together:
Task: "T006 [US1] Offline SampleConfigurationTests in tests/Mailgunner.Tests/Sample/SampleConfigurationTests.cs"
Task: "T007 [US1] SampleConfiguration resolver in samples/Mailgunner.Sample/SampleConfiguration.cs"
Task: "T008 [US1] ConferenceInvitation in samples/Mailgunner.Sample/ConferenceInvitation.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup (sample project scaffolding).
2. Phase 2: Foundational (test wiring).
3. Phase 3: US1 — the runnable personalized sample + offline guarantee.
4. **STOP and VALIDATE**: run the sample with and without credentials (SC-001/SC-002/SC-004/SC-006).

### Incremental Delivery

1. Setup + Foundational → sample builds.
2. US1 → runnable personalized send (MVP).
3. US2 → README quickstart + orienting sections (can ship in parallel).
4. US3 → `0.1.0` changelog + metadata verification (can ship in parallel).
5. Polish → end-to-end quickstart validation + README/sample consistency.

### Parallel Team Strategy

- Dev A: Setup + Foundational + US1 (the sample + test).
- Dev B: US2 (README) — needs no code.
- Dev C: US3 (CHANGELOG + metadata) — needs no code.
- Converge at Phase 6 (consistency check + final green build).

---

## Notes

- [P] = different files, no incomplete-task dependencies.
- The sample's **live send is never part of CI**; only the offline resolver test runs in the suite.
- No secret may appear in `samples/Mailgunner.Sample/` source, `appsettings.json`, README snippets, or any committed config (Principle V / Edge "Secrets in source").
- The library (`src/Mailgunner/`) and its public API/dependency set are **not** modified by this feature.
- Commit after each task or logical group using Conventional Commits.
