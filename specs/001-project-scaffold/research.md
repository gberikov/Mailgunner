# Phase 0 Research: Project Repository Scaffold

All Technical Context unknowns were resolved by the spec, the constitution, and targeted
verification of current (2026-06) .NET guidance. No `NEEDS CLARIFICATION` markers remain.

## R1 — Solution file format: `slnx`

- **Decision**: Use a single `Mailgunner.slnx` (XML) solution; no `.sln` kept alongside it.
- **Rationale**: The user explicitly requested `slnx`. It is GA in the `dotnet` CLI from
  **SDK 9.0.200+** (`dotnet build/test/sln` all operate on it) and is the **default** for
  `dotnet new sln` in **.NET 10**. XML form reduces merge conflicts and is human-readable.
- **Alternatives considered**: Classic `.sln` (legacy, noisy diffs) — rejected per user
  request and modern guidance. Keeping both formats — rejected; Microsoft recommends one.
- **Implication**: SDK must be pinned to a `slnx`-capable version (see R2). Some non-VS/CLI
  tools (older Rider/VSCode) had partial support; acceptable since CI and CLI fully support it.

## R2 — SDK pinning: `global.json`

- **Decision**: Add `global.json` pinning a `slnx`-capable SDK with `rollForward`
  (`latestFeature` or `latestMinor`) so the exact patch is environment-flexible but the
  floor guarantees `slnx` + modern analyzers. Target floor: a `.NET 10` SDK (GA since
  2025-11) which defaults `dotnet new sln` to `slnx`; minimum acceptable is `9.0.200`.
- **Rationale**: FR-008 requires an explicit, actionable SDK requirement; `rollForward`
  avoids brittle exact-patch pins while keeping a hard floor.
- **Alternatives considered**: No `global.json` (silent use of any installed SDK) — rejected,
  fails FR-008. Exact-patch pin with `disable` rollForward — rejected as brittle on CI.
- **Implication**: `/speckit-implement` resolves the concrete installed SDK version for the
  floor; library still compiles `net8.0;netstandard2.0` regardless of the (newer) SDK.

## R3 — Multi-targeting `net8.0;netstandard2.0`

- **Decision**: Library `<TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>`.
- **Rationale**: Clarification Q1 chose broad reach. `net8.0` is the constitution primary;
  `netstandard2.0` extends to .NET Framework / older runtimes. Verified that all three
  permitted runtime packages support `netstandard2.0` (`System.Text.Json`; `Polly` 8.x has a
  dedicated `netstandard2.0` target; `Microsoft.Extensions.*` support `netstandard2.0`).
- **Alternatives considered**: Single `net8.0` (simplest, narrower reach) — rejected by Q1.
  Adding `netstandard2.1`/`net462` — deferred; add later only if a contract-preserving need
  arises (Principle I).
- **Implication**: One BCL gap — `CryptographicOperations.FixedTimeEquals` is absent on
  `netstandard2.0`. Resolve with an internal constant-time comparison polyfill behind
  `#if NETSTANDARD2_0` (XOR-accumulate over bytes), **no added package**. Transitive polyfills
  (`System.Memory`, etc.) flow from the permitted packages — not new direct dependencies.

## R4 — SourceLink + deterministic builds (FR-010)

- **Decision**: Enable SourceLink via the **SDK-implicit** integration. Set, in
  `Directory.Build.props`: `PublishRepositoryUrl=true`, `EmbedUntrackedSources=true`,
  `IncludeSymbols=true`, `SymbolPackageFormat=snupkg`, `Deterministic=true`, and
  `ContinuousIntegrationBuild=true` **only when building on CI** (e.g.,
  `Condition="'$(CI)'=='true' or '$(TF_BUILD)'=='true'"` / a `ContinuousIntegrationBuild`
  passed by the pipeline). **Do NOT** add a `Microsoft.SourceLink.GitHub` PackageReference.
- **Rationale**: Verified current SourceLink guidance: since **.NET 8** SourceLink ships in
  the SDK for GitHub-hosted repos; adding the explicit package **silently disables** implicit
  SourceLink (no warning, empty SourceLink CDI). `ContinuousIntegrationBuild=true` enables
  deterministic source-path mapping but must not be on for local dev builds.
- **Alternatives considered**: Explicit `Microsoft.SourceLink.GitHub` PackageReference —
  rejected (regression trap on SDK 8+). Always-on `ContinuousIntegrationBuild` — rejected
  (would normalize local paths and hurt local debugging).
- **Implication**: Repo must be GitHub-hosted at `https://github.com/gberikov/Mailgunner`
  (Q2) for SourceLink to resolve; the remote should be configured for full source-linking.

## R5 — Central Package Management (CPM) (FR-007)

- **Decision**: `Directory.Packages.props` at root with
  `ManagePackageVersionsCentrally=true`; declare `<PackageVersion>` for the test stack and
  the permitted runtime catalog. Project files use `<PackageReference Include=...>` without
  `Version`.
- **Rationale**: Modern single-source-of-truth versioning; satisfies FR-007 and FR-015 (new
  projects inherit versions). Declaring runtime-package versions now keeps them ready for
  feature work without referencing them from the placeholder library.
- **Alternatives considered**: Per-project versions — rejected (drift, FR-007 fail).
  `packages.lock.json` only — complementary, not a substitute; optional, may be enabled later.

## R6 — Style & analyzer enforcement: `.editorconfig` + build (FR-004, FR-006)

- **Decision**: Root `.editorconfig` defining formatting, naming, and analyzer **severities**
  (key rules as `error`). Enable `EnforceCodeStyleInBuild=true` and `AnalysisLevel=latest`
  (with `AnalysisMode` recommended) in `Directory.Build.props` so style/analyzer violations
  fail the build regardless of editor. `TreatWarningsAsErrors=true` makes them blocking.
- **Rationale**: FR-006 requires editor-agnostic, build-time enforcement; `.editorconfig`
  alone only affects IDEs unless `EnforceCodeStyleInBuild` is set.
- **Alternatives considered**: StyleCop.Analyzers package — deferred; built-in .NET analyzers
  + code-style-in-build cover the requirements without an added dependency.

## R7 — Centralized build properties (FR-005, FR-014)

- **Decision**: `Directory.Build.props` centralizes: `Nullable=enable`, `LangVersion=latest`,
  `ImplicitUsings` (enable), `GenerateDocumentationFile=true`, `TreatWarningsAsErrors=true`,
  `EnforceCodeStyleInBuild=true`, deterministic/SourceLink props (R4), and shared package
  metadata (authors, company, license, repository URL, README, `PackageProjectUrl`,
  `RepositoryType=git`). Test projects opt out of packaging/docs via their own props or
  `IsPackable=false`/`GenerateDocumentationFile=false`.
- **Rationale**: One inherited file → FR-005/FR-015. Public-doc gate (Principle IV) enforced
  through `GenerateDocumentationFile` + `TreatWarningsAsErrors` (CS1591 becomes an error).
- **Alternatives considered**: Duplicate properties per csproj — rejected (FR-005).

## R8 — Package metadata & identity (FR-009, Q2)

- **Decision**: `PackageId=Mailgunner`, `Authors`/`Company="Gany Berikov"`,
  `PackageProjectUrl`/`RepositoryUrl=https://github.com/gberikov/Mailgunner`,
  `PackageLicenseExpression` matching `LICENSE` (confirm SPDX id from the file during
  implement), `PackageReadmeFile=README.md`, description, and `PackageTags`. Versioning via
  SemVer; release versions derived from `v*` git tags at release time (separate CI feature).
- **Rationale**: Satisfies FR-009; values supplied by Q2; license must match the existing,
  authoritative `LICENSE`.
- **Open item for implement**: read `LICENSE` to set the exact `PackageLicenseExpression`
  SPDX identifier (e.g., `MIT`, `Apache-2.0`).

## R9 — `.gitignore` / secret hygiene (FR-011, Principle V)

- **Decision**: Use the standard Visual Studio / .NET `.gitignore` (bin/obj, `.vs/`, user
  files, `*.user`, test results) plus explicit exclusion of secret material (`*.env`,
  `appsettings.*.local.json`, key files). Confirm the existing `.gitignore` already covers
  build output; extend as needed.
- **Rationale**: FR-011 + Principle V (no secret may be committed).
- **Alternatives considered**: Minimal hand-written ignore — rejected (gaps risk committing
  artifacts/secrets).

## R10 — CHANGELOG & docs (FR-012, FR-013)

- **Decision**: Seed `CHANGELOG.md` in **Keep a Changelog** format with an `Unreleased`
  section; expand `README.md` with project intent, install/usage stub, and a link to the
  changelog and license. Keep the existing `LICENSE`.
- **Rationale**: FR-012/FR-013 and Principle IV (CHANGELOG for SemVer changes).

## Summary of decisions

| ID | Decision |
|----|----------|
| R1 | `Mailgunner.slnx` (XML solution, single format) |
| R2 | `global.json` pins a `slnx`-capable SDK floor with `rollForward` |
| R3 | Library multi-targets `net8.0;netstandard2.0`; constant-time polyfill on TS2.0 |
| R4 | SDK-implicit SourceLink + deterministic CI builds; no SourceLink package |
| R5 | Central Package Management via `Directory.Packages.props` |
| R6 | `.editorconfig` + `EnforceCodeStyleInBuild` for build-time style enforcement |
| R7 | `Directory.Build.props` centralizes all shared build properties |
| R8 | Package metadata anchored to identity/URL from Q2; SPDX license from `LICENSE` |
| R9 | Standard .NET `.gitignore` + explicit secret exclusions |
| R10 | Keep a Changelog `CHANGELOG.md`; expanded `README.md` |
