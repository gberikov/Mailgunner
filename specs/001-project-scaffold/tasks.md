---
description: "Task list for Project Repository Scaffold"
---

# Tasks: Project Repository Scaffold

**Input**: Design documents from `specs/001-project-scaffold/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/build-package-contract.md, quickstart.md

**Tests**: REQUIRED. The constitution's Principle III (Test-First, Network-Free Tests) is NON-NEGOTIABLE, so the offline smoke test is included and lands with the scaffold.

**Organization**: Tasks are grouped by the three user stories from spec.md (P1 → P3). The stories are layered (foundation → quality → distribution); each is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (setup, foundational, and polish tasks carry no story label)
- All paths are repository-root-relative.

## Path Conventions

Single-library layout: `src/Mailgunner/`, `tests/Mailgunner.Tests/`, with shared config at repository root (`Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `global.json`, `Mailgunner.slnx`).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repository layout, SDK pin, VCS hygiene, and the empty solution.

- [X] T001 Create source/test directory layout: `src/Mailgunner/` and `tests/Mailgunner.Tests/`
- [X] T002 [P] Create `global.json` at repo root pinning a slnx-capable SDK floor (≥ 9.0.200; target a .NET 10 floor) with `rollForward: latestFeature` (per research.md R2)
- [X] T003 [P] Verify and extend root `.gitignore` to exclude `bin/`, `obj/`, `.vs/`, `*.user`, test results, and secret material (`*.env`, `appsettings.*.local.json`, key files) (FR-011, research.md R9)
- [X] T004 [P] Create root `.gitattributes` normalizing line endings (`* text=auto`, enforce LF for `*.cs`, `*.props`, `*.editorconfig`)
- [X] T005 Create empty `Mailgunner.slnx` (XML solution) at repo root via `dotnet new sln --name Mailgunner --format slnx` (or equivalent) — single solution format, no `.sln` (FR-001, research.md R1)

**Checkpoint**: Repo skeleton, SDK pin, and empty solution exist.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Root MSBuild configuration that every project inherits. Required before any project can build/test consistently.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T006 Create minimal root `Directory.Build.props` skeleton (empty `<Project>` with a single shared `<PropertyGroup>` placeholder) — quality, metadata, and reproducibility properties are added in US2/US3 (data-model.md "Shared Build Configuration")
- [X] T007 Create root `Directory.Packages.props` with `ManagePackageVersionsCentrally=true` and `<PackageVersion>` entries for the test stack (`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`) and the permitted runtime catalog (`System.Text.Json`, `Polly`, `Microsoft.Extensions.Http`) (FR-007, research.md R5; verify latest stable versions at implement time)

**Checkpoint**: Central build props + CPM in place — project files can now be added.

---

## Phase 3: User Story 1 - Clone, build, and test out of the box (Priority: P1) 🎯 MVP

**Goal**: From a clean clone, `dotnet restore` → `dotnet build` → `dotnet test` succeed with no manual setup, no network, and no credentials; a single solution groups the library and test projects.

**Independent Test**: On a clean checkout with the pinned SDK, run restore/build/test — build succeeds and the smoke test passes offline (quickstart.md Scenario 1; SC-001, SC-004).

### Tests for User Story 1 ⚠️ (write first, ensure it runs offline)

- [X] T008 [US1] Create offline smoke test `tests/Mailgunner.Tests/SmokeTests.cs` (xUnit) that references the library's placeholder public type and asserts a trivial invariant — no network, no credentials (Principle III)

### Implementation for User Story 1

- [X] T009 [P] [US1] Create library project `src/Mailgunner/Mailgunner.csproj` with `<TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>`, `IsPackable=true`, inheriting `Directory.Build.props` (FR-002, FR-016, research.md R3)
- [X] T010 [P] [US1] Add a placeholder public, XML-documented type `src/Mailgunner/Placeholder.cs` (e.g., a `MailgunnerInfo` class exposing a doc-commented constant) so the package is non-empty and docs exist up front (data-model.md "Library Project")
- [X] T011 [US1] Create test project `tests/Mailgunner.Tests/Mailgunner.Tests.csproj` (xUnit refs via CPM, `IsPackable=false`, `GenerateDocumentationFile=false`, multi-target to validate both library TFMs) with a `ProjectReference` to `src/Mailgunner/Mailgunner.csproj` (FR-002, FR-016)
- [X] T012 [US1] Add both projects to `Mailgunner.slnx` (`dotnet sln Mailgunner.slnx add ...`) (FR-001)
- [X] T013 [US1] Validate MVP: run `dotnet restore`, `dotnet build Mailgunner.slnx -c Release`, `dotnet test Mailgunner.slnx -c Release`; confirm green, offline, no credentials (SC-001, SC-004)

**Checkpoint**: Clean clone builds and tests green — MVP deliverable.

---

## Phase 4: User Story 2 - Consistent style and quality enforced automatically (Priority: P2)

**Goal**: Shared style/analyzer config + centrally defined build settings make every project inherit identical rules; violations fail the build regardless of editor; nullable, latest language version, docs, and warnings-as-errors are active by default.

**Independent Test**: Introduce a style/nullability/missing-doc violation → build FAILS; confirm projects inherit settings without per-project duplication (quickstart.md Scenarios 2 & 3; SC-002, SC-003).

### Implementation for User Story 2

- [X] T014 [US2] Extend root `Directory.Build.props` with quality properties: `Nullable=enable`, `LangVersion=latest`, `ImplicitUsings=enable`, `GenerateDocumentationFile=true` (library), `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest` (FR-004, FR-005, FR-014; contracts C2)
- [X] T015 [P] [US2] Create root `.editorconfig` defining formatting, naming conventions, and analyzer/code-style severities with key rules as `error` (FR-006, research.md R6)
- [X] T016 [US2] Build with the new gates on and resolve any surfaced violations (ensure placeholder type has complete XML docs; fix formatting/naming) so `dotnet build` stays warning-free and green for both TFMs
- [X] T017 [US2] Validate enforcement: temporarily (a) remove an XML doc → expect CS1591 build error, (b) add a `.editorconfig` style violation → expect build failure, (c) add `Version="x"` to a `PackageReference` → expect CPM restore/build failure; revert all (SC-003; quickstart.md Scenario 2)

**Checkpoint**: Quality is machine-enforced at build time; settings inherited centrally.

---

## Phase 5: User Story 3 - Package- and release-ready metadata and reproducibility (Priority: P3)

**Goal**: The repository carries package identity/licensing, deterministic + source-linked builds, and the docs needed to produce a clean, debuggable, publishable package.

**Independent Test**: `dotnet pack` produces a `.nupkg` (+ `.snupkg`) with correct identity, license, README, repository URL, both TFM assemblies, and SourceLink CDI; same commit builds equivalently on two machines (quickstart.md Scenario 4; SC-005, SC-006).

### Implementation for User Story 3

- [X] T018 [US3] Read repository `LICENSE` to determine the SPDX id, then add package metadata to root `Directory.Build.props`: `PackageId=Mailgunner`, `Authors`/`Company="Gany Berikov"`, `Description`, `PackageLicenseExpression=<SPDX>`, `PackageProjectUrl`/`RepositoryUrl=https://github.com/gberikov/Mailgunner`, `RepositoryType=git`, `PackageReadmeFile=README.md`, `PackageTags` (FR-009, research.md R8; contracts C4)
- [X] T019 [US3] Add reproducibility/SourceLink properties to root `Directory.Build.props`: `Deterministic=true`, `PublishRepositoryUrl=true`, `EmbedUntrackedSources=true`, `IncludeSymbols=true`, `SymbolPackageFormat=snupkg`, and `ContinuousIntegrationBuild=true` conditioned on CI only — and DO NOT add any `Microsoft.SourceLink.GitHub` PackageReference (FR-010, research.md R4; contracts C2)
- [X] T020 [P] [US3] Expand `README.md` with project intent, install/usage stub, and links to `CHANGELOG.md` and `LICENSE` (FR-012)
- [X] T021 [P] [US3] Create `CHANGELOG.md` in Keep a Changelog format with an `Unreleased` section (FR-013, research.md R10)
- [X] T022 [US3] Ensure `README.md` is packed (e.g., `<None Include="..\..\README.md" Pack="true" PackagePath="\" />` or repo-root pack path) so `PackageReadmeFile` resolves
- [X] T023 [US3] Validate packaging: `dotnet pack src/Mailgunner/Mailgunner.csproj -c Release -o ./artifacts`; confirm `.nupkg` + `.snupkg`, correct metadata, `lib/net8.0/` and `lib/netstandard2.0/` present, and SourceLink/Repository CDI in PDBs (SC-005; quickstart.md Scenario 4)

**Checkpoint**: A publishable, debuggable, deterministic package can be produced.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Full-scaffold validation and hygiene across all stories.

- [X] T024 [P] Run the complete `quickstart.md` validation (all five scenarios) and record results
- [X] T025 [P] Verify VCS hygiene: after `dotnet build -c Release`, `git status` shows no `bin/`/`obj/`, IDE, user, or secret files to be committed (SC-007; FR-011; quickstart.md Scenario 5)
- [ ] T026 Final constitution compliance review (Principles I–V, English-only artifacts) and commit using Conventional Commits

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **US1 (Phase 3)**: Depends on Foundational. MVP.
- **US2 (Phase 4)**: Depends on US1 (extends the projects/props that US1 creates).
- **US3 (Phase 5)**: Depends on US1; independent of US2 in principle, but sequenced after it (both edit `Directory.Build.props`).
- **Polish (Phase 6)**: Depends on all desired stories being complete.

### User Story Dependencies

- **US1 (P1)**: The foundation; no dependency on other stories.
- **US2 (P2)**: Builds on US1's projects (turns on enforcement). Independently testable via deliberate violations.
- **US3 (P3)**: Builds on US1's library project (adds package/repro metadata). Independently testable via `dotnet pack`.

### Within Each User Story

- US1: smoke test (T008) defined alongside project creation; build/test validated last (T013).
- Models/types before validation; root-config edits are sequential (same file).
- Story complete before moving to next priority.

### File-contention notes (limits [P])

- `Directory.Build.props` is edited by T006 (skeleton), T014 (quality), T018 (metadata), T019 (repro) — these are **sequential**, never parallel.
- Project files (`Mailgunner.csproj`, `Mailgunner.Tests.csproj`), `README.md`, `CHANGELOG.md`, `.editorconfig`, `.gitignore`, `.gitattributes`, `global.json` are distinct files → eligible for [P] where marked.

### Parallel Opportunities

- Setup: T002, T003, T004 in parallel.
- US1: T009 (library csproj) and T010 (placeholder type) in parallel before T011/T012.
- US3: T020 (README) and T021 (CHANGELOG) in parallel.
- Polish: T024 and T025 in parallel.

---

## Parallel Example: Setup Phase

```bash
# Launch independent setup files together:
Task: "Create global.json pinning slnx-capable SDK floor (T002)"
Task: "Verify/extend .gitignore for build output + secrets (T003)"
Task: "Create .gitattributes for line-ending normalization (T004)"
```

## Parallel Example: User Story 3

```bash
# Independent docs in parallel:
Task: "Expand README.md with intent + install stub + links (T020)"
Task: "Create CHANGELOG.md in Keep a Changelog format (T021)"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup → 2. Phase 2: Foundational → 3. Phase 3: US1.
4. **STOP and VALIDATE**: clean clone builds + smoke test passes offline.
5. This is a usable, buildable library skeleton (MVP).

### Incremental Delivery

1. Setup + Foundational → root config ready.
2. US1 → buildable/testable skeleton (MVP).
3. US2 → quality gates enforced.
4. US3 → publishable, deterministic, source-linked package.
5. Polish → full quickstart validation + hygiene + Conventional Commit.

### Notes

- [P] = different files, no dependencies.
- Verify the smoke test runs offline; never touch the real Mailgun service (Principle III).
- Pin exact package/SDK versions to latest stable at implement time (research.md R2, R5).
- Commit after each logical group using Conventional Commits.
