# Feature Specification: Project Repository Scaffold

**Feature Branch**: `001-project-scaffold`

**Created**: 2026-06-22

**Status**: Draft

**Input**: User description: "Инициализируй проект-репозиторий с использованием современных рекомендаций по созданию .NET-библиотек (slnx, editorconfig и т.д.)."

## Clarifications

### Session 2026-06-22

- Q: Target framework strategy for the library project (single vs multi-target)? → A: Multi-target `net8.0;netstandard2.0` for broad consumer reach (modern .NET plus older runtimes / .NET Framework). Functionality requiring APIs absent on `netstandard2.0` (notably constant-time signature comparison via `CryptographicOperations.FixedTimeEquals`) is provided through a conditional polyfill branch without adding new direct dependencies.
- Q: Canonical repository URL and author identity for package metadata and SourceLink? → A: Repository/project URL `https://github.com/gberikov/Mailgunner`; author/owner "Gany Berikov" (gany@berikov.kz). These values back `RepositoryUrl`, `PackageProjectUrl`, package authors, and SourceLink.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Clone, build, and test the library out of the box (Priority: P1)

A developer joining the Mailgunner project clones the repository and, from a clean
checkout, restores, builds, and runs the test suite with a single standard command set,
without any manual setup, hidden prerequisites, or local secrets. The repository already
contains a solution, a library project, and a test project wired together so that the
build is green and tests pass on the first attempt.

**Why this priority**: A repository that cannot be built and tested from a clean clone
has no value. This is the foundational slice every later feature depends on and the
minimum that makes the project a working .NET library.

**Independent Test**: On a clean machine with the supported .NET SDK installed, clone the
repository, run restore/build/test, and confirm the build succeeds and all tests pass
with no network access and no credentials configured.

**Acceptance Scenarios**:

1. **Given** a clean checkout and the supported .NET SDK, **When** the developer runs the
   restore-and-build command, **Then** the build completes successfully with no errors
   and no warnings.
2. **Given** a successfully built repository, **When** the developer runs the test
   command, **Then** the test project is discovered and all tests pass without touching
   the network or requiring credentials.
3. **Given** a clean checkout, **When** the developer inspects the repository root,
   **Then** a single solution groups the library project and the test project.

---

### User Story 2 - Consistent style and quality enforced automatically (Priority: P2)

A contributor writes code and expects formatting, naming, and quality rules to be applied
and enforced uniformly regardless of their editor or IDE. The repository ships a shared
style and analyzer configuration plus centrally defined build settings so that every
project inherits identical rules, warnings are treated as errors, and nullable reference
types and the latest language version are active by default.

**Why this priority**: Consistent, machine-enforced quality prevents style drift and
catches defects early, but it builds on top of a repository that already compiles and
tests (P1).

**Independent Test**: Introduce a deliberate style or nullability violation in any
project and confirm the shared configuration reports it as a build-breaking error;
confirm that build settings (nullable, language version, documentation generation,
warnings-as-errors) apply without per-project duplication.

**Acceptance Scenarios**:

1. **Given** the shared style configuration, **When** code violating a formatting or
   naming rule is built, **Then** the violation is surfaced as an error rather than
   silently accepted.
2. **Given** centrally managed build settings, **When** a new project is added to the
   solution, **Then** it automatically inherits nullable reference types, the latest
   language version, documentation generation, and warnings-as-errors without copying
   settings into the project file.
3. **Given** a public type or member without documentation, **When** the library is
   built, **Then** the missing-documentation condition is reported as an error.

---

### User Story 3 - Package- and release-ready metadata and reproducibility (Priority: P3)

A maintainer prepares the library for distribution and expects the repository to already
carry the metadata and reproducibility settings required to produce a clean, debuggable,
publishable package: package identity and licensing, deterministic builds, source
linking, and dependency versions managed in one place.

**Why this priority**: Distribution readiness matters for shipping but is not required to
develop and validate the library locally, so it follows the build/test and quality
slices.

**Independent Test**: Produce a package from the repository and confirm it carries the
expected identity, license, README, and symbol/source-link information, and that building
the same commit twice yields equivalent output.

**Acceptance Scenarios**:

1. **Given** the repository configuration, **When** a maintainer produces a distributable
   package, **Then** the package includes identity, license, and README metadata and the
   information required to debug into the original sources.
2. **Given** dependency versions defined centrally, **When** a dependency version needs to
   change, **Then** it is updated in one location and applies to all projects.
3. **Given** the same source commit, **When** the package is built on two different
   machines, **Then** the produced output is equivalent (deterministic).

---

### Edge Cases

- What happens when a contributor uses an editor that does not natively honor the shared
  style configuration? The same rules MUST still be enforced at build time so quality does
  not depend on editor choice.
- What happens when an unsupported or older .NET SDK is used? The repository MUST make the
  required SDK explicit so the failure is a clear, actionable message rather than an
  obscure build error.
- What happens when transient or generated files (build output, IDE folders, secrets) are
  present locally? They MUST be excluded from version control so the repository stays clean
  and no secret is ever committed.
- How does the repository behave when a new project is added? It MUST inherit shared
  settings automatically without manual duplication.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST contain a single solution that groups all projects and
  can be opened and built by standard .NET tooling.
- **FR-002**: The repository MUST contain a class-library project that represents the
  publishable Mailgunner library and a separate automated-test project that references it.
- **FR-003**: A clean checkout MUST restore, build, and run the test suite using standard
  .NET commands with no manual setup, no network access during tests, and no credentials.
- **FR-004**: The repository MUST define a shared, editor-agnostic code-style and analyzer
  configuration that applies uniformly to all projects.
- **FR-005**: Build settings common to all projects (nullable reference types, latest
  language version, documentation generation, treat-warnings-as-errors) MUST be defined
  centrally and inherited by every project without per-project duplication.
- **FR-006**: Style, analyzer, and documentation rules MUST be enforced at build time such
  that violations fail the build, independent of the contributor's editor or IDE.
- **FR-007**: Dependency (package) versions MUST be managed centrally so a version is
  declared in one place and applied consistently across projects.
- **FR-008**: The repository MUST declare the required .NET SDK version explicitly so that
  using an unsupported SDK produces a clear, actionable error.
- **FR-009**: The library project MUST carry package metadata required for distribution:
  package identity `Mailgunner`, author/owner "Gany Berikov", a description, the license
  expression matching the repository LICENSE, README association, and project/repository
  URL `https://github.com/gberikov/Mailgunner`.
- **FR-010**: The build MUST be configured for reproducibility and debuggability,
  including deterministic output, continuous-integration build marking, and source-linking
  information (anchored to repository `https://github.com/gberikov/Mailgunner`) that lets
  consumers step into the original sources.
- **FR-011**: Version control MUST exclude build output, IDE/editor working folders,
  user-specific files, and any secret material via an appropriate ignore configuration.
- **FR-012**: The repository MUST include top-level project documentation (README) and a
  license file consistent with the declared package license.
- **FR-013**: The repository MUST include a human-readable change log following a
  recognized format to record notable changes per release.
- **FR-014**: The scaffold MUST be consistent with the project constitution: minimal
  dependency footprint, English-only artifacts, primary target of the modern .NET version
  defined by the constitution, and the quality gates it mandates.
- **FR-015**: Adding a new project to the solution MUST automatically apply the shared
  style, build, and dependency settings without copying configuration into the new
  project.
- **FR-016**: The library project MUST multi-target `net8.0` and `netstandard2.0`. Where a
  required capability depends on an API absent on `netstandard2.0` (e.g., constant-time
  signature comparison), it MUST be supplied via a conditional/polyfill code path without
  introducing new direct runtime dependencies beyond those permitted by the constitution.
  The test project MUST validate behavior on each target framework.

### Key Entities *(include if feature involves data)*

- **Solution**: The top-level grouping that ties the library and test projects together
  for tooling; one per repository.
- **Library Project**: The publishable unit representing the Mailgunner client; carries
  package metadata and is the subject of distribution.
- **Test Project**: The automated-verification unit that references the library and runs
  offline.
- **Shared Configuration**: The set of repository-level settings (style/analyzer rules,
  common build properties, central dependency versions, SDK pin, ignore rules) that all
  projects inherit.
- **Distribution Package**: The artifact produced from the library project, carrying
  identity, license, documentation, and source/symbol information.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can go from a clean clone to a green build and passing tests
  using only standard, documented commands, with zero manual configuration steps.
- **SC-002**: 100% of projects in the repository inherit the shared style, build, and
  dependency settings without any project re-declaring those settings locally.
- **SC-003**: Any introduced style, nullability, or missing-public-documentation violation
  causes the build to fail rather than being silently accepted.
- **SC-004**: The test suite runs to completion with no network access and no credentials
  present, and reports pass/fail deterministically.
- **SC-005**: A distributable package can be produced that includes identity, license,
  README, and source/symbol information sufficient to debug into the original sources.
- **SC-006**: Building the same commit on two independent machines produces equivalent
  package output (deterministic build verified).
- **SC-007**: No secret, credential, or build artifact is present in version control at any
  point.

## Assumptions

- The target audience is library maintainers and contributors working on a developer
  workstation or CI agent with the constitution-defined .NET SDK installed; end users of
  the published package are out of scope for this scaffolding feature.
- The permitted dependencies and quality gates are those fixed by the project constitution
  (slim dependency set, nullable, warnings-as-errors, XML docs, deterministic + source-
  linked builds); this feature realizes that foundation rather than re-deciding it. The
  primary target remains `net8.0` per the constitution, with `netstandard2.0` added as a
  contract-preserving compatibility target (see FR-016).
- The license already present in the repository is authoritative and the package license
  metadata MUST match it.
- Continuous-integration pipelines, NuGet publishing automation, and actual library
  functionality (sending mail, suppressions, webhooks) are separate features layered on
  top of this scaffold and are out of scope here.
- "Modern recommendations" includes an XML-based solution format and centrally managed
  build and dependency configuration as the preferred conventions, subject to support by
  the declared SDK.
