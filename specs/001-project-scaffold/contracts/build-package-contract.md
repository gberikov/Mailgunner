# Contract: Build & Package Configuration

The scaffold has no public *API* surface yet (the library ships only a placeholder type).
Its externally observable contract is the **build behavior** and the **produced NuGet
package**. This document defines that contract; it is the reference the test/quickstart
artifacts and `/speckit-tasks` validate against. Exact versions/SPDX id are bound at
`/speckit-implement`.

## C1 — Toolchain contract

| Aspect | Required behavior |
|--------|-------------------|
| SDK floor | `global.json` pins a `slnx`-capable SDK (floor ≥ `9.0.200`; target a `.NET 10` floor) with `rollForward: latestFeature`. An SDK below the floor fails with a clear message. |
| Solution | `Mailgunner.slnx` (XML) is the single solution; `dotnet build`/`dotnet test` operate on it. |
| Restore/build/test | From a clean clone: `dotnet restore` → `dotnet build -c Release` → `dotnet test` succeed with no manual setup, no network during tests, no credentials. |
| Determinism | `Deterministic=true`; the same commit produces equivalent output on two machines (CI sets `ContinuousIntegrationBuild=true`). |

## C2 — Shared build-property contract (inherited by every project)

These MUST be in effect for the library (and any future project) via `Directory.Build.props`,
without per-project duplication:

| Property | Required value | Requirement |
|----------|----------------|-------------|
| `Nullable` | `enable` | Principle IV |
| `LangVersion` | `latest` | Principle IV |
| `GenerateDocumentationFile` | `true` (library) | Principle IV / public-doc gate |
| `TreatWarningsAsErrors` | `true` | Principle IV — missing XML doc (CS1591) and style violations become errors |
| `EnforceCodeStyleInBuild` | `true` | FR-006 — `.editorconfig` enforced at build, editor-agnostic |
| `Deterministic` | `true` | FR-010 |
| `PublishRepositoryUrl` | `true` | FR-010 — repo URL in nuspec |
| `EmbedUntrackedSources` | `true` | FR-010 |
| `ContinuousIntegrationBuild` | `true` on CI only | FR-010 — deterministic source paths; must NOT be on for local builds |
| `IncludeSymbols` / `SymbolPackageFormat` | `true` / `snupkg` | FR-010 — symbol package |

**Prohibited**: an explicit `Microsoft.SourceLink.GitHub` PackageReference (silently disables
implicit SDK SourceLink on SDK 8+). SourceLink is provided by the SDK; properties only.

## C3 — Dependency-management contract

| Aspect | Required behavior |
|--------|-------------------|
| CPM | `Directory.Packages.props` with `ManagePackageVersionsCentrally=true`; all versions declared centrally (FR-007). |
| Project references | `PackageReference` entries carry NO `Version` attribute. |
| Permitted runtime catalog | Only `System.Text.Json`, `Polly`, `Microsoft.Extensions.Http` may appear as runtime versions (Principle I). No `Newtonsoft.Json`/FluentEmail. |
| Library refs (scaffold) | The placeholder library references NO runtime packages yet. |

## C4 — Package (`.nupkg`) metadata contract

The library's produced package MUST carry:

| Field | Value |
|-------|-------|
| `PackageId` | `Mailgunner` |
| `Authors` / `Company` | `Gany Berikov` |
| `Description` | Non-empty; describes the Mailgun client |
| `PackageLicenseExpression` | SPDX id matching repository `LICENSE` (bound at implement) |
| `PackageReadmeFile` | `README.md` (packed) |
| `PackageProjectUrl` / `RepositoryUrl` | `https://github.com/gberikov/Mailgunner` |
| `RepositoryType` | `git` |
| Target frameworks | `net8.0` and `netstandard2.0` lib folders present |
| SourceLink | Repository CDI present in PDBs; `.snupkg` produced |

## C5 — Multi-target capability contract (FR-016)

| Aspect | Required behavior |
|--------|-------------------|
| TFMs | Library builds for `net8.0` AND `netstandard2.0`. |
| API-gap handling | Capabilities depending on APIs absent on `netstandard2.0` (notably constant-time comparison via `CryptographicOperations.FixedTimeEquals`) are provided by an internal polyfill behind `#if NETSTANDARD2_0`, adding NO new direct dependency. |
| Tests | The test project validates behavior on each library TFM. |

## C6 — Quality-gate contract (observable failures)

| Trigger | Required result |
|---------|-----------------|
| Public member without XML doc | Build FAILS (CS1591 as error) |
| Style/naming violation per `.editorconfig` | Build FAILS |
| Versioned `PackageReference` under CPM | Build/restore FAILS |
| Build artifact or secret staged | Excluded by `.gitignore`; never committed |
| Test requiring network/credentials | Not present; full suite runs offline |
