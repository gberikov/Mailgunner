# Implementation Plan: NuGet Publication Readiness

**Branch**: `011-nuget-publish-prep` | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/011-nuget-publish-prep/spec.md`

## Summary

Bring the Mailgunner repository to a state where publishing to nuget.org is a single,
deliberate, credential-gated action — **without performing the publish**. The library
itself is frozen (no public-API, behavior, or runtime-dependency changes). The work is
packaging/release wiring and documentation: adopt **MinVer** so the package version is
derived solely from `v*` git tags; embed a package **icon** (from the maintainer-provided
logo) and a **release-notes** changelog pointer; rewrite three repository-relative README
links to absolute GitHub URLs so the listing renders correctly; and add two **GitHub
Actions** workflows — `ci.yml` (build + test on push/PR, green with no credentials) and
`release.yml` (tag-triggered `pack` + a `NUGET_API_KEY`-gated `push` that is skipped
cleanly while the secret is absent). The first release will be the pre-release
`0.1.0-preview.1`. Research verified that both `net8.0` and `netstandard2.0` pack cleanly
with the pinned dependencies, so both targets ship.

## Technical Context

**Language/Version**: C# (`LangVersion=latest`, `Nullable=enable`) — unchanged; the library
is not modified.

**Primary Dependencies**: No change to the publishable library's runtime dependency set
(`System.Text.Json`, `Polly`, `Microsoft.Extensions.Http`). One **build-time-only** tool is
added: **MinVer 7.0.0** (`PrivateAssets=all`, development dependency) — it never enters the
consumer dependency graph, preserving constitution Principle I.

**Storage**: N/A.

**Testing**: Existing offline xUnit suite (`tests/Mailgunner.Tests`) must stay green with no
credentials. No new unit tests are required (no runtime behavior changes); verification is
via `dotnet pack` + artifact inspection and the CI workflow itself.

**Target Platform**: NuGet package consumers on `net8.0` and `netstandard2.0`; CI on
`ubuntu-latest` (GitHub Actions).

**Project Type**: Single .NET library + repository tooling (CI/CD, packaging metadata,
docs).

**Performance Goals**: N/A (packaging/release readiness, not a runtime feature).

**Constraints**:
- No publish may occur as part of this feature; the release path stays inert (no
  `NUGET_API_KEY` secret created, no `v*` tag pushed).
- No secrets in the repository; the publish credential lives only in CI secrets.
- Default `dotnet build`/`dotnet test` and CI must be green with no Mailgun/NuGet
  credentials present.
- Package version must come solely from the `v*` git tag (no hard-coded version).

**Scale/Scope**: ~9 touch points (see research.md summary table); two new workflow files,
one new icon asset, one new docs section, edits to `Directory.Build.props`,
`Directory.Packages.props`, the library `.csproj`, `README.md`, and `CHANGELOG.md`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Status |
|-----------|------|--------|
| I. Minimal Dependencies & Modern .NET | No new **runtime** dependency; JSON stays `System.Text.Json` | ✅ PASS — MinVer is build-time-only (`PrivateAssets=all`), absent from the consumer graph; runtime deps unchanged |
| II. Managed HTTP & Resilience | No HTTP/`HttpClient` code changes | ✅ PASS — library untouched |
| III. Test-First, Network-Free Tests | Suite stays offline & green without credentials; new behavior gets tests | ✅ PASS — no runtime behavior added; CI runs the existing offline suite with no secrets |
| IV. Documented, Strict Public API + deterministic, source-linked packages + non-affiliation + CHANGELOG | Packages deterministic w/ SourceLink & symbols; SemVer; non-affiliation in README + package; Keep a Changelog | ✅ PASS — this feature *fulfils* these (icon, release notes, tag-driven SemVer, CHANGELOG entry); determinism/SourceLink already wired and CI sets `ContinuousIntegrationBuild` |
| V. Security & Scope Discipline | No secrets committed; credentials only via CI secrets; v1 scope unchanged | ✅ PASS — `NUGET_API_KEY` lives only in CI secrets and is never created here; no endpoint scope change |
| Dev Workflow & Quality Gates | CI builds/tests on push+PR; release runs `dotnet pack` + publishes on `v*` tags; CI sets `ContinuousIntegrationBuild` | ✅ PASS — exactly what `ci.yml` + `release.yml` implement (publish gated/inert) |

**Result**: No violations. Complexity Tracking is intentionally empty.

## Project Structure

### Documentation (this feature)

```text
specs/011-nuget-publish-prep/
├── plan.md              # This file (/speckit-plan output)
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (configuration "entities")
├── quickstart.md        # Phase 1 output (validation guide)
├── contracts/           # Phase 1 output
│   ├── package-metadata.md   # Required nuspec/listing fields contract
│   └── release-pipeline.md   # CI + release workflow trigger/gate contract
└── checklists/
    └── requirements.md  # Spec quality checklist (already passing)
```

### Source Code (repository root)

This feature edits repository configuration and adds tooling; it does **not** change
library source under `src/Mailgunner/`. Affected paths:

```text
.
├── Directory.Build.props          # EDIT: drop VersionPrefix; add MinVer props, PackageIcon, PackageReleaseNotes
├── Directory.Packages.props       # EDIT: add MinVer 7.0.0 version entry
├── icon.png                       # NEW: 128×128 package icon derived from the provided logo
├── README.md                      # EDIT: rewrite 3 relative file links to absolute GitHub URLs
├── CHANGELOG.md                   # EDIT: add [0.1.0-preview.1] entry
├── docs/
│   └── RELEASING.md               # NEW: documented, gated release procedure (or a README section)
├── .github/
│   └── workflows/
│       ├── ci.yml                 # NEW: build + test on push/PR
│       └── release.yml            # NEW: v*-tag-triggered pack + secret-gated push
└── src/Mailgunner/
    └── Mailgunner.csproj          # EDIT: reference MinVer (PrivateAssets=all); pack icon.png
```

**Structure Decision**: Existing single-library repository layout is retained. No new
source projects are introduced; the only net-new code artifacts are two CI workflow files,
the icon asset, and the release documentation. All library projects continue to inherit
shared packaging metadata from `Directory.Build.props`, so the icon/release-notes wiring is
defined once and applied to the (single) packable project.

## Complexity Tracking

> No constitution violations — this section is intentionally empty.
