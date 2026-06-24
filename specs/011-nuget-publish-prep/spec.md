# Feature Specification: NuGet Publication Readiness

**Feature Branch**: `011-nuget-publish-prep`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "Подготовь репозиторий для публикации в NuGet. Пока только подготовка, саму публикацию делать не надо."

## Clarifications

### Session 2026-06-24

- Q: Which target frameworks should the published package support? → A: Keep `net8.0` + `netstandard2.0`, but only if the pinned dependencies actually restore under `netstandard2.0`; otherwise fall back to `net8.0`-only.
- Q: How is the released package version derived (single source of truth)? → A: Use MinVer (build-time-only, `PrivateAssets=all`) to derive the version automatically from the `v*` git tag; not a runtime dependency.
- Q: Is the first publish a stable or pre-release version? → A: Pre-release — `0.1.0-preview.1` (tag `v0.1.0-preview.1`); first stable release comes later.
- Q: Where does the package icon come from? → A: Originally to be derived from a maintainer-provided logo URL; during implementation that source proved to be the official **Sinch Mailgun trademark logo**, which conflicts with the non-affiliation disclaimer (Principle IV / FR-005). Per maintainer decision, a **neutral, original** icon (an envelope on a blue tile) was generated and committed to the repository instead, packed via `PackageIcon`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Maintainer produces a complete, valid package artifact (Priority: P1)

As the library maintainer, I can run a single packaging command and obtain a NuGet
package artifact (plus its companion symbol package) that contains everything nuget.org
requires — accurate identity and metadata, the rendered README, the license, and the
compiled assemblies for every supported framework — without any error, warning, or
missing piece.

**Why this priority**: A package that cannot be produced cleanly cannot be published at
all. This is the irreducible core of "publication readiness"; every other story builds on
having a correct artifact in hand.

**Independent Test**: Run the packaging step on a clean checkout and inspect the produced
artifact. It can be fully validated offline by opening the package and confirming the
assemblies, metadata fields, README, and license are present and correct — no upload
required.

**Acceptance Scenarios**:

1. **Given** a clean checkout with no credentials configured, **When** the maintainer runs
   the packaging step, **Then** a package artifact and a matching symbol artifact are
   produced with zero errors and zero warnings.
2. **Given** the produced package artifact, **When** its contents are inspected, **Then**
   it contains the library assemblies for each supported target framework, the rendered
   README, the license, and complete identity metadata (id, version, authors,
   description, project/repository links, tags).
3. **Given** the produced package artifact, **When** its metadata is reviewed, **Then** the
   description and an explicit non-affiliation notice are present so a consumer browsing
   the listing understands the library is community-maintained and unofficial.
4. **Given** the test and sample projects in the repository, **When** the packaging step
   runs, **Then** only the library project is packaged and the test and sample projects
   are excluded from packaging.

---

### User Story 2 - Publishing is a single deliberate, credential-gated action (Priority: P2)

As the maintainer, when I eventually decide to publish, I can trigger the release through
one deliberate, documented action that is gated on a credential held outside the
repository — so that no publish can happen accidentally or as a side effect of ordinary
development, and so the act of releasing requires an explicit, intentional step.

**Why this priority**: The user explicitly scoped this work to *preparation only* — the
actual publish must not happen now. The repository must reach a state where releasing is
ready and obvious but inert until a human performs the gated trigger and supplies the
secret.

**Independent Test**: Review the release procedure end to end and confirm it stops short
of contacting the package registry whenever the publish credential is absent. Can be
verified without ever publishing, by confirming the gate prevents the final upload step.

**Acceptance Scenarios**:

1. **Given** the repository as delivered by this feature, **When** ordinary development
   activity occurs (commits, pull requests, normal builds), **Then** no package is ever
   uploaded to the registry.
2. **Given** the documented release procedure, **When** a maintainer follows it without
   the publish credential present, **Then** the procedure halts before the upload step and
   reports the missing credential rather than failing obscurely or publishing partial
   results.
3. **Given** the release procedure, **When** a maintainer reads it, **Then** the exact,
   minimal sequence of steps required to publish (including where the credential must be
   supplied) is documented and unambiguous.
4. **Given** the chosen versioning scheme, **When** a maintainer prepares a release,
   **Then** the published version is derived from a single, explicit source of truth and
   cannot silently diverge from what was reviewed.

---

### User Story 3 - The published listing is trustworthy and discoverable (Priority: P3)

As a prospective consumer browsing the package registry, I can find the package by
relevant search terms and, on its listing, immediately see what it does, that it is
unofficial/community-maintained, where its source lives, what license governs it, and
what changed in this version — so I can decide to adopt it without leaving the page.

**Why this priority**: Discoverability and trust determine adoption. It depends on a valid
artifact (US1) existing first, but materially improves the quality of the eventual
listing and protects the project legally (non-affiliation).

**Independent Test**: Inspect the artifact's metadata and the rendered README/release
notes as they would appear on the listing, and confirm each trust/discoverability element
is present and accurate — verifiable offline against the artifact.

**Acceptance Scenarios**:

1. **Given** the package metadata, **When** a consumer searches the registry with terms
   relevant to the library's purpose, **Then** the package surfaces via its tags and
   description.
2. **Given** the package listing, **When** a consumer reads it, **Then** the README renders
   correctly (links resolve to absolute destinations that work off-repository) and the
   release notes / changelog for the version are reachable.
3. **Given** the package listing, **When** a consumer looks for provenance, **Then** the
   license, project URL, repository URL, and the non-affiliation disclaimer are all
   present and consistent with the repository.

---

### Edge Cases

- **Package identity already taken**: The intended package identity may already be
  registered by someone else on the public registry. The preparation MUST surface how the
  maintainer confirms the identity is available (or owned) *before* a first publish, so the
  first release does not fail at upload time.
- **Missing publish credential**: When the publish credential is absent, the release path
  MUST stop cleanly with a clear message and MUST NOT attempt a partial or anonymous
  upload.
- **Multi-framework correctness**: Each *retained* target framework's assembly MUST be
  present and loadable from the package, and a consumer on any supported framework MUST be
  able to install and reference it. If `netstandard2.0` cannot satisfy the pinned
  dependencies, it MUST be dropped (fall back to `net8.0`-only) rather than published in a
  broken state.
- **README rendering off-repository**: Relative links and images that work inside the
  repository may break on the registry listing; the packaged README MUST render correctly
  when viewed outside the repository.
- **Pre-release vs stable versioning**: The first publish is a **pre-release**:
  `0.1.0-preview.1` (git tag `v0.1.0-preview.1`), so NuGet does not mark it as the latest
  stable version and the still-evolving public API is not prematurely frozen. The first
  stable `0.1.0` release is a later, separate decision.
- **Re-running the release for an already-published version**: Attempting to release a
  version that already exists on the registry MUST be prevented or fail safely without
  corrupting the listing.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST produce a publishable package artifact and a matching
  symbol artifact for the library project from a clean checkout, with no errors and no
  warnings.
- **FR-002**: Only the library project MUST be packable; test and sample projects MUST be
  excluded from packaging.
- **FR-003**: The package MUST carry complete and accurate identity metadata: a unique
  package identity, a version, authors, a description, license, project URL, repository
  URL, and search tags.
- **FR-004**: The package MUST include the rendered README and the license so they appear
  on the registry listing.
- **FR-005**: The package metadata and/or README MUST state that the library is
  community-maintained and not affiliated with, authorized by, or endorsed by the upstream
  service or its owner.
- **FR-006**: The package MUST contain a loadable library assembly for every supported
  target framework. The intended targets are `net8.0` and `netstandard2.0`; `netstandard2.0`
  MUST be retained only if every pinned runtime dependency restores and builds cleanly under
  it. If any dependency does not support `netstandard2.0`, the package MUST fall back to
  `net8.0`-only rather than ship a broken or partially-resolving target.
- **FR-007**: The package MUST be built deterministically with source-debugging support
  (embedded untracked sources and source linking) so consumers can step into the code.
- **FR-008**: The version of any released package MUST come from a single explicit source
  of truth — the `v*` git tag, resolved at build time by MinVer (a build-time-only,
  `PrivateAssets=all` dependency that never enters the consumer's dependency graph) — and
  MUST follow semantic versioning, with a corresponding changelog entry for the released
  version.
- **FR-009**: No publish MUST occur as a side effect of ordinary development (commits,
  pull requests, routine builds); publishing MUST require a deliberate, explicit trigger.
- **FR-010**: The publish step MUST be gated on a credential supplied from outside the
  repository; when that credential is absent the publish MUST be skipped (not failed in a
  way that blocks normal work) and MUST never be embedded in the repository.
- **FR-011**: The repository MUST contain no secrets, API keys, or signing keys in code,
  configuration, samples, or fixtures.
- **FR-012**: The release procedure MUST be documented as an explicit, minimal,
  reproducible sequence, including where the publish credential is supplied and how the
  version is chosen.
- **FR-013**: The packaged README MUST render correctly on the registry listing, with all
  links resolving to destinations that work outside the repository.
- **FR-014**: The preparation MUST document how the maintainer verifies the package
  identity is available (or already owned) before the first publish.
- **FR-015**: The release path MUST prevent or safely fail an attempt to publish a version
  that already exists on the registry.
- **FR-016**: This feature MUST NOT perform an actual publish to the public registry; its
  deliverable is a repository that is *ready* to publish on a later deliberate action.
- **FR-017**: The library's existing public behavior, public surface, and runtime
  dependency set MUST NOT change as a result of this preparation.
- **FR-018**: An automated continuous-integration pipeline MUST be established that builds
  and tests the repository on every push and pull request, keeping the default run green
  with no credentials present.
- **FR-019**: A separate automated release path MUST package and publish the library only
  on a deliberate version-tag trigger, and MUST be gated on a publish credential held in
  the CI secret store; absent that credential, the release path MUST skip the upload
  cleanly without failing ordinary integration runs.
- **FR-020**: The package listing MUST include a package icon — a **neutral, original**
  icon (the maintainer-provided source was the upstream trademark logo and was rejected to
  avoid conflict with FR-005) committed into the repository as a packaged icon
  (`PackageIcon`), not referenced by an external URL — and the package metadata MUST carry
  per-version release notes (or a reachable changelog reference) for the released version.

### Key Entities *(include if feature involves data)*

- **Package artifact**: The distributable unit uploaded to the registry; carries the
  compiled assemblies (per framework), identity metadata, README, and license.
- **Symbol artifact**: The companion debugging-symbols unit that lets consumers step into
  the source.
- **Package metadata**: The set of identity and descriptive fields (id, version, authors,
  description, license, URLs, tags, disclaimer) shown on the listing and used for search.
- **Release procedure**: The documented, gated sequence that turns a reviewed commit into a
  published version, requiring an externally-held credential.
- **Version source of truth**: The single authoritative origin of the released version
  number — the `v*` git tag resolved by MinVer at build time — tied to semantic versioning
  and the changelog.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A maintainer can produce the package and symbol artifacts from a clean
  checkout in a single packaging step with zero errors and zero warnings.
- **SC-002**: 100% of registry-required metadata fields (id, version, authors,
  description, license, project URL, repository URL, tags, README) are present and accurate
  in the produced artifact.
- **SC-003**: The package contains a loadable assembly for every *retained* supported
  target framework (`net8.0`, plus `netstandard2.0` only if its dependencies restore;
  otherwise `net8.0` alone), verified by inspecting the artifact.
- **SC-004**: Zero secrets are present anywhere in the repository, confirmed by inspection.
- **SC-005**: No publish occurs during this feature's work; the registry shows no new or
  changed listing as a result of it.
- **SC-006**: A maintainer unfamiliar with the project can follow the documented release
  procedure and reach the exact point just before upload (where the credential would be
  supplied) without external help.
- **SC-007**: The packaged README renders on the listing with 100% of its links resolving
  to working off-repository destinations.
- **SC-008**: The non-affiliation disclaimer is present in both the README and the package
  metadata.

## Assumptions

- **Preparation only, no publish**: Per the explicit user instruction, this feature stops
  at "ready to publish." The actual upload to the public registry is a separate, later,
  deliberate action and is out of scope.
- **Target registry**: The public nuget.org registry is the intended destination.
- **Package identity**: The package identity is the library's existing name ("Mailgunner");
  the maintainer is responsible for confirming/owning that identity on the registry before
  the first publish.
- **First version**: The first release is a **pre-release** `0.1.0-preview.1` (git tag
  `v0.1.0-preview.1`), building on the existing `0.1.0` foundation already recorded in the
  changelog. The first stable `0.1.0` publish is a later, separate decision.
- **Existing groundwork is reused**: Package metadata, deterministic/source-linked build
  settings, multi-target framework selection, the packed README, and the non-affiliation
  disclaimer already exist in the repository and are validated/completed rather than
  recreated.
- **Hosting platform**: The repository is hosted on GitHub (per its project/repository
  URLs); GitHub Actions is the automation platform for both the integration pipeline
  (build/test on push and pull request) and the tag-triggered release path.
- **Listing polish in scope**: A package icon and per-version release notes are part of
  this preparation (not deferred). The icon is a neutral, original asset committed into the
  repository (packed via `PackageIcon`), not referenced by an external URL — the originally
  proposed source was the upstream trademark logo and was rejected to preserve the
  non-affiliation stance.
- **Library is frozen**: This feature changes packaging/release wiring and documentation
  only; it does not modify the library's public API, behavior, or runtime dependencies.
- **Credential handling**: Any publish credential is held only in the CI/release system's
  secret store or supplied locally by the maintainer at publish time, never committed.
