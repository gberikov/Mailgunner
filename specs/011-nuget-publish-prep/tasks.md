---
description: "Task list for NuGet Publication Readiness"
---

# Tasks: NuGet Publication Readiness

**Input**: Design documents from `specs/011-nuget-publish-prep/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: No automated test tasks are generated — this feature adds no runtime library
behavior (FR-017), so constitution Principle III requires no new unit tests. Verification is
performed by `dotnet pack` artifact inspection, CI, and the quickstart validation guide.

**Organization**: Tasks are grouped by user story for independent implementation/testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: US1 / US2 / US3 (maps to spec.md user stories)
- Exact file paths are included in each task

## Path Conventions

Single .NET library repository at root; library under `src/Mailgunner/`, shared MSBuild in
`Directory.Build.props` / `Directory.Packages.props`, CI under `.github/workflows/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Bring in the versioning tool catalog entry and record a clean baseline.

- [X] T001 Add a `MinVer` `7.0.0` `<PackageVersion>` entry to `Directory.Packages.props` (build-time tool; latest stable per research.md R2)
- [X] T002 [P] Baseline check: run `dotnet build -c Release` and `dotnet pack src/Mailgunner/Mailgunner.csproj -c Release -o ./artifacts`; confirm current `.nupkg` + `.snupkg` build with 0 warnings (records the pre-change state)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish the single version source of truth (MinVer from `v*` tags). Every
story produces or describes an artifact whose version must come from this mechanism.

**⚠️ CRITICAL**: No user story work should be validated until this phase is complete.

- [X] T003 Reference `MinVer` in `src/Mailgunner/Mailgunner.csproj` with `PrivateAssets="all"` so it never enters the consumer dependency graph (constitution Principle I; FR-017)
- [X] T004 In `Directory.Build.props`: remove `<VersionPrefix>0.1.0</VersionPrefix>`, and add `<MinVerTagPrefix>v</MinVerTagPrefix>` and `<MinVerMinimumMajorMinor>0.1</MinVerMinimumMajorMinor>` (FR-008)
- [X] T005 Verify version derivation: confirm an untagged `dotnet pack` yields a `0.1.0-*` height version; create a throwaway local tag `v0.1.0-preview.1`, confirm `dotnet pack` yields exactly `0.1.0-preview.1`, then delete the tag; confirm the produced `.nupkg` `<dependencies>` groups contain **no** MinVer entry (FR-008, FR-017)

**Checkpoint**: Version source of truth established; all stories can proceed.

---

## Phase 3: User Story 1 - Maintainer produces a complete, valid package artifact (Priority: P1) 🎯 MVP

**Goal**: A clean checkout produces a correct `.nupkg` + `.snupkg` — both target frameworks,
README, license, complete core metadata — with zero errors/warnings.

**Independent Test**: Run `dotnet pack` on a clean checkout and open the `.nupkg`; confirm
assemblies for both TFMs, README, license, and complete identity metadata are present
(quickstart.md steps 2–3).

- [X] T006 [US1] Confirm packaging scope: verify `IsPackable=true` only in `src/Mailgunner/Mailgunner.csproj`, and that `tests/Mailgunner.Tests/Mailgunner.Tests.csproj` and `samples/Mailgunner.Sample/Mailgunner.Sample.csproj` are excluded from packaging (FR-002, US1 AC4)
- [X] T007 [US1] Verify packed payload from `dotnet pack -c Release`: `lib/net8.0/Mailgunner.dll`+`.xml` AND `lib/netstandard2.0/Mailgunner.dll`+`.xml`, `README.md`, and a companion `.snupkg` containing portable PDBs (FR-001, FR-006, FR-007; research.md R1)
- [X] T008 [US1] Verify generated `Mailgunner.nuspec` carries complete, accurate core metadata — `id`, `version`, `authors`, `description`, `license` (MIT expression), `projectUrl`, `repository` (url/commit/branch), `tags`, `readme` — with 0 pack warnings (FR-003, FR-004, SC-001, SC-002)

**Checkpoint**: A valid, publishable artifact is reproducibly produced (MVP complete).

---

## Phase 4: User Story 2 - Publishing is a single deliberate, credential-gated action (Priority: P2)

**Goal**: CI builds/tests on push & PR with no credentials; a separate `v*`-tag-triggered
release packs and publishes only when a `NUGET_API_KEY` secret is present — and stays inert
(no secret, no tag) in the delivered state.

**Independent Test**: Review both workflows; confirm `ci.yml` references no secret and is
green credential-free, and `release.yml` triggers only on `v*` tags with a publish step that
is cleanly skipped when `NUGET_API_KEY` is absent (quickstart.md step 6; contracts/release-pipeline.md).

- [X] T009 [P] [US2] Create `.github/workflows/ci.yml`: `on: [push, pull_request]`; `actions/checkout` with `fetch-depth: 0`; `actions/setup-dotnet` honoring `global.json`; steps `dotnet restore` → `dotnet build -c Release` → `dotnet test -c Release`; no secrets referenced (FR-018; contracts/release-pipeline.md Workflow A)
- [X] T010 [P] [US2] Create `.github/workflows/release.yml`: `on: push: tags: ['v*']`; checkout `fetch-depth: 0`; setup-dotnet; `dotnet pack -c Release -o artifacts`; gated push step with step-level `env: NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}` and `if: ${{ env.NUGET_API_KEY != '' }}` running `dotnet nuget push "artifacts/*.nupkg" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate` (FR-009, FR-010, FR-015, FR-019; contracts/release-pipeline.md Workflow B)
- [X] T011 [P] [US2] Create `docs/RELEASING.md`: explicit, minimal, reproducible release steps — **document how to verify the `Mailgunner` id is available/owned before the first publish** (browse `https://www.nuget.org/packages/Mailgunner`, or check the registration API `https://api.nuget.org/v3/registration5-semver1/mailgunner/index.json` → `404` = unclaimed; note that the id is claimed by the first push and consider reserving an id prefix; currently available per research.md R9), set the `NUGET_API_KEY` repository secret, push tag `v0.1.0-preview.1`; state that following the steps without the secret stops cleanly before upload (FR-012, FR-014)
- [X] T012 [US2] Validate the workflows (e.g. `actionlint` or YAML parse): confirm `ci.yml` references no secrets, and that `release.yml` gates publish via step-level `env` (never `secrets.*` in a job-level `if`) and uses `--skip-duplicate` (FR-015, FR-019; depends on T009, T010)

**Checkpoint**: Release is automated, deliberate, and gated — and verifiably inert (no
secret, no tag pushed; FR-016).

---

## Phase 5: User Story 3 - The published listing is trustworthy and discoverable (Priority: P3)

**Goal**: The listing shows an icon, accurate description and tags, a non-affiliation
disclaimer, a correctly-rendering README (absolute links), license/source provenance, and
reachable per-version release notes.

**Independent Test**: Inspect the re-packed artifact's metadata and packed README as they
would render on nuget.org; confirm icon, release notes, absolute links, and disclaimer are
present and accurate (quickstart.md steps 3–5).

- [X] T013 [P] [US3] Create `icon.png` at the repository root, 128×128 PNG ≤ 1 MB. NOTE: the maintainer-provided source URL turned out to be the official **Sinch Mailgun trademark logo**, which conflicts with the non-affiliation disclaimer (Principle IV / FR-005); per maintainer decision a **neutral, original** icon (envelope on a blue tile) was generated instead (FR-020; research.md R4)
- [X] T014 [US3] Wire the icon into the package: add `<PackageIcon>icon.png</PackageIcon>` to `Directory.Build.props` and `<None Include="..\..\icon.png" Pack="true" PackagePath="\" />` to `src/Mailgunner/Mailgunner.csproj` (FR-020; depends on T013)
- [X] T015 [P] [US3] Add `<PackageReleaseNotes>` to `Directory.Build.props` pointing to the absolute CHANGELOG URL `https://github.com/gberikov/Mailgunner/blob/master/CHANGELOG.md` (FR-020; research.md R6)
- [X] T016 [P] [US3] Add a `## [0.1.0-preview.1]` entry to `CHANGELOG.md` aligned with the existing `0.1.0` content so the changelog matches the first published pre-release version (FR-008; research.md R3)
- [X] T017 [P] [US3] Rewrite repository-relative file links in `README.md` to absolute GitHub URLs (branch `master`): `](LICENSE)`, `](CHANGELOG.md)`, `](samples/Mailgunner.Sample)`; leave anchor links unchanged (FR-013, SC-007; research.md R5)
- [X] T018 [US3] Verify the non-affiliation disclaimer is present in `README.md` AND surfaced in package metadata/listing fields (description/release notes) (FR-005, SC-008)
- [X] T019 [US3] Re-pack and verify: `Mailgunner.nuspec` now contains `<icon>icon.png</icon>` and `<releaseNotes>`; the `.nupkg` contains `icon.png`; and the packed `README.md` has no relative file links (FR-013, FR-020; depends on T013–T017)

**Checkpoint**: The eventual listing is complete, trustworthy, and discoverable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification across all stories; confirm nothing is published and the
library is untouched.

- [X] T020 [P] Secret scan: confirm no secrets, API keys, or signing keys exist anywhere in the repository, including the new workflow files (FR-011, SC-004)
- [X] T021 Run the full `quickstart.md` validation (steps 1–8); confirm every checkbox passes and that no package is published (no `NUGET_API_KEY` secret, no `v*` tag) (SC-005, SC-006)
- [X] T022 [P] Confirm the library is frozen: no changes under `src/Mailgunner/` except the packaging lines in `Mailgunner.csproj` (MinVer reference, icon `None` item) — public API, behavior, and runtime dependencies unchanged (FR-017)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — start immediately.
- **Foundational (Phase 2)**: depends on Setup (T001 before T003). BLOCKS validation of all stories.
- **User Stories (Phase 3–5)**: depend on Foundational completion. US1 → US2 → US3 in priority order; US2 and US3 are independent of each other and may run in parallel after Foundational.
- **Polish (Phase 6)**: depends on all desired stories complete.

### User Story Dependencies

- **US1 (P1)**: after Foundational. The MVP — no dependency on US2/US3.
- **US2 (P2)**: after Foundational. Independent of US3; `release.yml` packs the same artifact US1 validates.
- **US3 (P3)**: after Foundational. Edits packaging metadata/README/CHANGELOG; re-pack in T019 supersedes the US1 baseline pack (icon + release notes added).

### Within Stories

- T013 (icon asset) before T014 (icon wiring) and before T019 (re-pack verify).
- T009/T010 before T012 (workflow validation).
- T017 before T019 (link rewrite verified in re-pack).

### Parallel Opportunities

- T001 ‖ T002 (Setup).
- US2 fully parallel authoring: T009 ‖ T010 ‖ T011 (different files), then T012.
- US3: T015 ‖ T016 ‖ T017 ‖ T013 (different files); T014 after T013; T019 last.
- Polish: T020 ‖ T022.
- After Foundational, an US2 author and an US3 author can work simultaneously.

---

## Parallel Example: User Story 2

```bash
# Author the three independent files together:
Task: "Create .github/workflows/ci.yml"
Task: "Create .github/workflows/release.yml"
Task: "Create docs/RELEASING.md"
# Then validate:
Task: "actionlint / YAML-validate both workflows"
```

## Parallel Example: User Story 3

```bash
# Independent edits in parallel:
Task: "Create icon.png (128x128) from the provided logo"
Task: "Add PackageReleaseNotes to Directory.Build.props"
Task: "Add [0.1.0-preview.1] entry to CHANGELOG.md"
Task: "Rewrite relative README links to absolute GitHub URLs"
# Then: wire icon (T014) → re-pack verify (T019)
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (MinVer version source).
2. Phase 3 US1 → **STOP and VALIDATE**: a correct `.nupkg` + `.snupkg` is reproducibly
   produced (quickstart steps 1–3). This alone is a publishable artifact.

### Incremental Delivery

1. Setup + Foundational → version source ready.
2. US1 → valid artifact (MVP).
3. US2 → gated, automated release path (still inert — nothing published).
4. US3 → polished, trustworthy listing (icon, notes, absolute links).
5. Polish → secret scan + full quickstart validation; confirm library frozen and nothing published.

### Notes

- This feature performs **no publish** (FR-016): never create the `NUGET_API_KEY` secret and
  never push a `v*` tag as part of these tasks. T005's local tag is throwaway and deleted.
- [P] = different files, no incomplete dependencies.
- Commit after each task or logical group (Conventional Commits).
