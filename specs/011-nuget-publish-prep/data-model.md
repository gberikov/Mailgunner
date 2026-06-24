# Phase 1 Data Model: NuGet Publication Readiness

**Feature**: `011-nuget-publish-prep` | **Date**: 2026-06-24

This feature has no runtime domain data. The "entities" are **configuration artifacts** —
the things produced and consumed by the packaging/release process. Each is described by its
fields, the source that supplies them, validation rules (from the spec FRs), and lifecycle.

## Entity: Package Artifact (`.nupkg`)

The distributable unit uploaded to nuget.org.

| Field | Source | Validation (FR) |
|-------|--------|-----------------|
| `lib/net8.0/Mailgunner.dll` + `.xml` | `dotnet pack` | MUST be present and loadable (FR-006) |
| `lib/netstandard2.0/Mailgunner.dll` + `.xml` | `dotnet pack` | Present iff deps restore (verified: yes) (FR-006) |
| `README.md` | packed via `PackageReadmeFile` | MUST render off-repo; links absolute (FR-004, FR-013) |
| `icon.png` | packed via `PackageIcon` | PNG/JPG ≤1 MB, 128×128 recommended (FR-020) |
| `Mailgunner.nuspec` | generated | Carries all metadata fields below (FR-003) |
| dependency groups (per TFM) | `Directory.Packages.props` | Only `System.Text.Json`, `Polly`, `Microsoft.Extensions.Http`; **no MinVer** (FR-017) |

**Lifecycle**: produced by `dotnet pack` → inspected/validated offline → (later, gated)
pushed to nuget.org. No publish during this feature (FR-016).

## Entity: Symbol Artifact (`.snupkg`)

| Field | Source | Validation |
|-------|--------|------------|
| portable PDBs per TFM | `IncludeSymbols` + `SymbolPackageFormat=snupkg` | Produced alongside `.nupkg` (FR-001, FR-007) |

**Lifecycle**: produced by `dotnet pack`; pushed with the `.nupkg` to the NuGet symbol
server on release. Already wired in `Directory.Build.props`.

## Entity: Package Metadata

The identity/descriptive fields shown on the listing and used for search.

| Field | Current value / source | Validation (FR) |
|-------|------------------------|-----------------|
| `id` | `Mailgunner` (assembly name) | unique; **available** on nuget.org (FR-003, FR-014) |
| `version` | **MinVer** from `v*` tag | SemVer; single source of truth; first = `0.1.0-preview.1` (FR-008) |
| `authors` / `company` | `Directory.Build.props` | present (FR-003) |
| `description` | `Directory.Build.props` | present (FR-003) |
| `license` | `PackageLicenseExpression=MIT` | renders MIT on listing (FR-004) |
| `projectUrl` / `repositoryUrl` | `Directory.Build.props` | present, consistent (FR-003) |
| `tags` | `mailgun;sinch;email;transactional-email;smtp;mail` | discoverable (FR-003) |
| `icon` | `PackageIcon=icon.png` | **NEW** (FR-020) |
| `releaseNotes` | `PackageReleaseNotes` = CHANGELOG URL | **NEW** (FR-020) |
| non-affiliation notice | README + package (description/notes) | present in both (FR-005, SC-008) |
| `repository.commit` / `branch` | SourceLink / git | embedded deterministically (FR-007) |

**Validation rules**:
- No metadata field may be blank (FR-003); confirmed by nuspec inspection.
- `version` MUST NOT be hard-coded (remove `VersionPrefix`) — drift forbidden (FR-008).
- MinVer MUST NOT appear in any dependency group (FR-017).

## Entity: Version Source of Truth

| Field | Value |
|-------|-------|
| origin | `v*` git tag, resolved by MinVer at build time |
| tag prefix | `v` (`MinVerTagPrefix`) |
| minimum major.minor | `0.1` (`MinVerMinimumMajorMinor`) |
| first release tag | `v0.1.0-preview.1` → version `0.1.0-preview.1` |
| changelog linkage | `CHANGELOG.md` entry MUST exist for the released version (FR-008) |

**State transitions**: untagged commit → height pre-release `0.1.0-*` (local/dev) →
tagged `v0.1.0-preview.1` → exact `0.1.0-preview.1` (release).

## Entity: Release Procedure

The documented, gated sequence turning a reviewed commit into a published version.

| Field | Value |
|-------|-------|
| trigger | push a `v*` git tag (FR-009) |
| credential | `NUGET_API_KEY` GitHub secret, supplied externally (FR-010) |
| gate behavior | push step skipped cleanly when secret absent (FR-010, FR-019) |
| idempotency | `--skip-duplicate` on push (FR-015) |
| pre-publish check | maintainer confirms id ownership/availability (FR-014) |
| documentation | `docs/RELEASING.md` — explicit, minimal, reproducible steps (FR-012) |

**Validation rules**: ordinary commits/PRs never publish (FR-009); absent the secret the
release halts before upload with a clear skip, not an obscure failure (FR-019, SC-006).

## Entity: CI Pipeline (integration)

| Field | Value |
|-------|-------|
| triggers | `push`, `pull_request` (FR-018) |
| steps | checkout (`fetch-depth: 0`) → setup-dotnet (`global.json`) → restore → build → test |
| credentials | none; green with no secrets (FR-018, Principle III) |
| determinism | `ContinuousIntegrationBuild=true` via existing `GITHUB_ACTIONS` condition (FR-007) |
