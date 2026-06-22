# Implementation Plan: Project Repository Scaffold

**Branch**: `master` (feature dir `001-project-scaffold`) | **Date**: 2026-06-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-project-scaffold/spec.md`

## Summary

Establish the foundational repository for **Mailgunner**, a modern, dependency-light .NET
library, so that a clean clone restores, builds, and tests green with zero manual setup,
and is ready for distribution as a NuGet package. The scaffold realizes the project
constitution: an XML-based `slnx` solution grouping a multi-targeted (`net8.0;netstandard2.0`)
library project and a network-free xUnit test project; centrally managed build properties
(`Directory.Build.props`), package versions (`Directory.Packages.props`, CPM), and analyzer/
style rules (`.editorconfig`); a pinned SDK (`global.json`); SDK-implicit SourceLink with
deterministic, CI-marked builds; and complete package metadata anchored to
`https://github.com/gberikov/Mailgunner`. No library functionality (sending mail,
suppressions, webhooks) is built here — only the foundation and a smoke test that proves
the toolchain.

## Technical Context

**Language/Version**: C# (latest, `LangVersion=latest`), nullable enabled.

**Primary Dependencies**: None required at runtime for the scaffold itself. The permitted
runtime dependency catalog (constitution) — `System.Text.Json`, `Polly`,
`Microsoft.Extensions.Http` — is pinned centrally and ready for later features. Test stack:
xUnit, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `coverlet.collector`.

**Storage**: N/A (library; no persistence).

**Testing**: xUnit, executed offline via a fake `HttpMessageHandler` pattern (no real network).
The scaffold ships a single smoke test asserting the toolchain/packaging is wired; the test
project multi-targets to run on both `net8.0` and `netstandard2.0`-compatible runtime.

**Target Platform**: Cross-platform .NET. Library targets `net8.0` (primary) and
`netstandard2.0` (compatibility). Build/CI agents and dev workstations with the pinned SDK.

**Project Type**: Single class-library + test project (NuGet-distributable library).

**Performance Goals**: N/A for scaffold. Clean `restore+build+test` completes promptly on a
standard dev machine; build is deterministic.

**Constraints**: Offline tests (no network, no credentials); warnings-as-errors; XML docs
required on public members; deterministic + source-linked builds; English-only artifacts;
minimal dependency footprint; no secrets in VCS.

**Scale/Scope**: Repository scaffold only — solution, library project, test project, and
shared configuration. CI pipelines, NuGet publishing automation, and library features are
out of scope (separate features).

**Key environment facts (verified 2026-06):**
- `slnx` is GA in the `dotnet` CLI from **SDK 9.0.200+**; **.NET 10** makes it the
  `dotnet new sln` default. → pin SDK ≥ a `slnx`-capable release in `global.json`.
- **SourceLink is bundled in the SDK since .NET 8** for GitHub/GitLab/etc. **Do NOT add a
  `Microsoft.SourceLink.GitHub` PackageReference** — on SDK 8+ an explicit reference
  *silently disables* implicit SourceLink. Set MSBuild properties only.
- `System.Text.Json`, `Polly` (8.x, dedicated `netstandard2.0` target), and
  `Microsoft.Extensions.Http` all support `netstandard2.0`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Constitution v1.0.0 (2026-06-22). Gates derived from its principles:

| Principle | Gate for this scaffold | Status |
|-----------|------------------------|--------|
| I. Minimal Dependencies & Modern .NET | Library multi-targets `net8.0;netstandard2.0`; runtime dependency catalog limited to the 3 permitted packages, centrally pinned; no `Newtonsoft.Json`/FluentEmail. | ✅ PASS |
| II. Managed HTTP & Resilience | No HTTP code in scaffold; `Microsoft.Extensions.Http`/`Polly` versions provisioned for later. | ✅ PASS (N/A now, provisioned) |
| III. Test-First, Network-Free (NON-NEGOTIABLE) | xUnit test project exists, runs offline, no credentials; a smoke test lands with the scaffold. | ✅ PASS |
| IV. Documented, Strict Public API | `Nullable=enable`, `LangVersion=latest`, `GenerateDocumentationFile=true`, `TreatWarningsAsErrors=true` set centrally; placeholder public type is XML-documented; SemVer/CHANGELOG seeded. | ✅ PASS |
| V. Security & Scope Discipline | `.gitignore` excludes secrets/build output; no keys committed; scope strictly the scaffold. | ✅ PASS |
| Mailgun API Fidelity | N/A for scaffold (no endpoints implemented). | ✅ PASS (N/A) |
| Dev Workflow & Quality Gates | Conventional Commits; `dotnet build`/`dotnet test` green; `ContinuousIntegrationBuild` honored in CI; CHANGELOG (Keep a Changelog). | ✅ PASS |

**Result:** No violations. Complexity Tracking not required.

**Note on netstandard2.0 tension (Principle I):** Multi-targeting pulls *transitive* polyfills
(e.g., `System.Memory`) through the already-permitted packages on `netstandard2.0`; these are
not new *direct* dependencies. The one BCL gap (`CryptographicOperations.FixedTimeEquals`,
absent on `netstandard2.0`) will be covered by an internal constant-time polyfill behind
`#if NETSTANDARD2_0` with **no** added package. This preserves the principle.

## Project Structure

### Documentation (this feature)

```text
specs/001-project-scaffold/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (config artifacts as the "model")
├── quickstart.md        # Phase 1 output (validation/run guide)
├── contracts/
│   └── build-package-contract.md   # Phase 1 output (build property + package metadata contract)
└── tasks.md             # Phase 2 output (/speckit-tasks - NOT created here)
```

### Source Code (repository root)

```text
Mailgunner.slnx                 # XML solution grouping both projects
global.json                     # Pins a slnx-capable SDK (rollForward)
Directory.Build.props           # Shared MSBuild props (nullable, langver, docs, WarnAsError, SourceLink, package metadata)
Directory.Packages.props        # Central Package Management: pinned versions (ManagePackageVersionsCentrally)
.editorconfig                   # Editor-agnostic style + analyzer severities (enforced at build)
.gitignore                      # Excludes bin/obj, IDE folders, user files, secrets
.gitattributes                  # Line-ending normalization
LICENSE                         # Existing — authoritative for package license
README.md                       # Existing — expanded; associated into the package
CHANGELOG.md                    # Keep a Changelog format, seeded with Unreleased

src/
└── Mailgunner/
    ├── Mailgunner.csproj        # Library: <TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>, IsPackable=true
    └── (placeholder public, XML-documented type to make the package non-empty & validate docs gate)

tests/
└── Mailgunner.Tests/
    ├── Mailgunner.Tests.csproj  # xUnit; references src/Mailgunner; IsPackable=false; multi-target test
    └── (smoke test proving build/test/packaging wiring; offline)
```

**Structure Decision**: Single-library layout with conventional `src/` and `tests/`
separation. The `slnx` solution references both projects. All cross-cutting configuration
lives in repository-root `Directory.*.props`, `.editorconfig`, and `global.json` so every
current and future project inherits identical settings without duplication (FR-005, FR-015).

## Complexity Tracking

> No constitution violations — section intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
