# Phase 1 Data Model: Project Repository Scaffold

This feature produces configuration artifacts rather than domain data. The "entities" below
are the repository artifacts and their key attributes/relationships, derived from the spec's
Key Entities and Functional Requirements. Concrete property values are finalized during
`/speckit-implement`.

## Entity: Solution (`Mailgunner.slnx`)

- **Represents**: The top-level grouping tying all projects together for tooling (FR-001).
- **Format**: XML (`slnx`). Single solution file; no `.sln` retained.
- **Attributes / contents**:
  - References `src/Mailgunner/Mailgunner.csproj`.
  - References `tests/Mailgunner.Tests/Mailgunner.Tests.csproj`.
- **Relationships**: Parent of both projects.
- **Validation**: `dotnet build Mailgunner.slnx` and `dotnet test Mailgunner.slnx` succeed
  on the pinned SDK. Exactly one solution file at repo root.

## Entity: Library Project (`src/Mailgunner/Mailgunner.csproj`)

- **Represents**: The publishable Mailgunner client unit (FR-002, FR-009).
- **Attributes**:
  - `TargetFrameworks = net8.0;netstandard2.0` (FR-016).
  - `IsPackable = true`; inherits all shared build/package properties from
    `Directory.Build.props`.
  - Contains a placeholder public, XML-documented type so the package is non-empty and the
    public-documentation gate is exercised. No Mailgun functionality.
  - Package references: none yet (runtime catalog provisioned centrally for later features).
- **Relationships**: Inherits `Directory.Build.props` + `Directory.Packages.props`; produced
  into a NuGet package (+ `.snupkg` symbols).
- **Validation**: Builds with `TreatWarningsAsErrors` and `GenerateDocumentationFile` on, for
  both TFMs; `dotnet pack` yields a package carrying metadata + SourceLink.

## Entity: Test Project (`tests/Mailgunner.Tests/Mailgunner.Tests.csproj`)

- **Represents**: The automated, offline verification unit (FR-002, FR-003; Principle III).
- **Attributes**:
  - xUnit; references `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`,
    `coverlet.collector` (versions from CPM).
  - `IsPackable = false`; `GenerateDocumentationFile = false`.
  - `ProjectReference` → `src/Mailgunner/Mailgunner.csproj`.
  - Multi-targets so library behavior is validated on each library TFM (FR-016).
  - Contains a smoke test (no network, no credentials).
- **Validation**: `dotnet test` discovers and passes all tests offline.

## Entity: Shared Build Configuration (`Directory.Build.props`)

- **Represents**: Centrally inherited MSBuild properties (FR-005, FR-014).
- **Attributes (key properties)**:
  - Quality: `Nullable=enable`, `LangVersion=latest`, `ImplicitUsings=enable`,
    `GenerateDocumentationFile=true`, `TreatWarningsAsErrors=true`,
    `EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest`.
  - Reproducibility/debug: `Deterministic=true`, `PublishRepositoryUrl=true`,
    `EmbedUntrackedSources=true`, `IncludeSymbols=true`, `SymbolPackageFormat=snupkg`,
    `ContinuousIntegrationBuild=true` **conditioned on CI**.
  - Package metadata: `Authors`/`Company`, `PackageProjectUrl`, `RepositoryUrl`,
    `RepositoryType=git`, `PackageLicenseExpression` (SPDX from `LICENSE`),
    `PackageReadmeFile=README.md`, `Description`, `PackageTags`.
- **Relationships**: Inherited by every project; test projects override packaging/doc flags.
- **Validation**: Removing a property from a csproj does not change behavior (proves
  inheritance); a new project added to the solution inherits these automatically (FR-015).

## Entity: Central Package Versions (`Directory.Packages.props`)

- **Represents**: Single source of truth for dependency versions (FR-007).
- **Attributes**:
  - `ManagePackageVersionsCentrally = true`.
  - `<PackageVersion>` entries: test stack (`Microsoft.NET.Test.Sdk`, `xunit`,
    `xunit.runner.visualstudio`, `coverlet.collector`) and the permitted runtime catalog
    (`System.Text.Json`, `Polly`, `Microsoft.Extensions.Http`).
- **Relationships**: Consumed by all projects via version-less `PackageReference`.
- **Validation**: Adding a versioned `PackageReference` in a csproj would error under CPM,
  proving central management is active.

## Entity: Style & Analyzer Rules (`.editorconfig`)

- **Represents**: Editor-agnostic formatting/naming/analyzer configuration (FR-004, FR-006).
- **Attributes**: Formatting + naming conventions; analyzer/code-style severities (key rules
  as `error`); applies repo-wide from root.
- **Relationships**: Enforced at build via `EnforceCodeStyleInBuild` (in Build.props).
- **Validation**: An introduced style/naming violation fails the build (SC-003).

## Entity: SDK Pin (`global.json`)

- **Represents**: Explicit required SDK floor (FR-008).
- **Attributes**: `sdk.version` = a `slnx`-capable floor; `rollForward` = `latestFeature`.
- **Validation**: Using an SDK below the floor produces a clear, actionable error.

## Entity: VCS Hygiene (`.gitignore`, `.gitattributes`)

- **Represents**: Exclusion of build output, IDE/user files, and secrets; line-ending
  normalization (FR-011, Principle V).
- **Validation**: `git status` on a built tree shows no `bin/`, `obj/`, IDE, or secret files
  as tracked/untracked-to-be-added (SC-007).

## Entity: Project Docs (`README.md`, `CHANGELOG.md`, `LICENSE`)

- **Represents**: Top-level documentation, change history, and licensing (FR-012, FR-013).
- **Attributes**: `README.md` expanded + packaged; `CHANGELOG.md` in Keep a Changelog format
  with `Unreleased`; existing `LICENSE` is authoritative and matched by package metadata.
- **Validation**: Package includes README + license metadata; changelog present and valid.

## Entity: Distribution Package (output)

- **Represents**: The `.nupkg` (+ `.snupkg`) artifact from the library (FR-009, FR-010).
- **Attributes**: Carries identity, license, README, repository URL, and SourceLink/symbol
  data sufficient to debug into original sources.
- **Validation**: `dotnet pack -c Release` produces a package whose metadata and SourceLink
  CDI are present (verifiable via NuGet Package Explorer); same commit builds equivalently on
  two machines (SC-005, SC-006).
