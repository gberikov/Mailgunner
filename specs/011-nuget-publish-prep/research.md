# Phase 0 Research: NuGet Publication Readiness

**Feature**: `011-nuget-publish-prep` | **Date**: 2026-06-24

All decisions below were verified against the actual repository (build/pack runs, nuspec
inspection, nuget.org API) rather than assumed.

## R1. Retain `netstandard2.0` as a published target (resolves FR-006 conditional)

- **Decision**: Keep both `net8.0` and `netstandard2.0` as published targets.
- **Evidence**: `dotnet build -c Release` and `dotnet pack -c Release` both succeed with
  **0 warnings / 0 errors**; the produced `Mailgunner.0.1.0.nupkg` contains
  `lib/net8.0/Mailgunner.dll` **and** `lib/netstandard2.0/Mailgunner.dll`, and the nuspec
  emits a `.NETStandard2.0` dependency group for all three pinned packages
  (`Microsoft.Extensions.Http 10.0.9`, `Polly 8.7.0`, `System.Text.Json 10.0.9`). The
  clarified condition ("retain `netstandard2.0` only if the pinned dependencies restore")
  is therefore satisfied.
- **Consequence**: No fallback to `net8.0`-only is needed. The `net8.0`-only fallback path
  in FR-006 remains documented but is **not exercised**.
- **Alternatives considered**: `net8.0`-only (rejected — unnecessarily narrows reach when
  the broader target builds cleanly); adding `net10.0` (deferred — out of clarified scope).

## R2. Version source of truth: MinVer from `v*` git tags (FR-008)

- **Decision**: Adopt **MinVer 7.0.0** (latest stable; 8.0.0 is still pre-release) as a
  build-time-only dependency that derives the package version from the `v*` git tag.
- **Configuration**:
  - Add `MinVer` to `Directory.Packages.props` and reference it from the library project
    with `PrivateAssets="all"` (MinVer also marks itself a development dependency), so it
    **never appears in the consumer dependency graph** — constitution Principle I (runtime
    dependencies = `System.Text.Json`, `Polly`, `Microsoft.Extensions.Http`) is preserved.
  - Set `<MinVerTagPrefix>v</MinVerTagPrefix>` (tags look like `v0.1.0-preview.1`).
  - Set `<MinVerMinimumMajorMinor>0.1</MinVerMinimumMajorMinor>` so untagged local builds
    produce a sane `0.1.0-*` height version.
  - **Remove** the hard-coded `<VersionPrefix>0.1.0</VersionPrefix>` from
    `Directory.Build.props` — the tag becomes the single source of truth and a stray
    `VersionPrefix` would silently shadow MinVer.
- **Rationale**: One authoritative origin (the tag), no manual edits, no tag/props drift.
- **Alternatives considered**: extract the tag in the workflow and pass `-p:Version=`
  (rejected — version logic lives in CI yaml, invisible to local `dotnet pack`);
  Nerdbank.GitVersioning (rejected — heavier, JSON-config, more than needed);
  manual `VersionPrefix` (rejected — drift risk, the very thing FR-008 forbids).

## R3. First publish is a pre-release: `0.1.0-preview.1` (FR-008, edge case)

- **Decision**: The first (later, deliberate) publish is the pre-release
  `0.1.0-preview.1`, cut from git tag `v0.1.0-preview.1`. MinVer renders SemVer 2.0
  pre-release versions verbatim from the tag, so nuget.org marks it as a pre-release and
  does **not** surface it as the latest stable version.
- **CHANGELOG**: add a `## [0.1.0-preview.1]` entry (or annotate that the pre-release
  ships the `0.1.0` content) so the changelog and the published version agree.
- **Alternatives considered**: stable `0.1.0` (rejected per clarification — premature for a
  still-evolving pre-1.0 public API).

## R4. Neutral, original package icon (FR-020)

- **Decision**: Embed a **neutral, original** icon in the package via `PackageIcon` (not the
  deprecated, network-dependent `PackageIconUrl`).
- **Trademark finding (implementation)**: the maintainer-provided source URL resolved to the
  official **Sinch Mailgun** wordmark — a third-party trademark. Using it as the icon of an
  *unofficial, non-affiliated* package contradicts FR-005 / constitution Principle IV and
  carries trademark risk on a public, hard-to-reverse listing. Per maintainer decision it
  was rejected in favor of an original envelope-on-blue-tile mark generated in-repo.
- **Constraints (nuget.org)**: icon must be a PNG or JPG, **≤ 1 MB**, recommended
  **128×128**. The asset is committed at repo root as `icon.png` (128×128, ~2 KB) and packed
  with `<None Include="..\..\icon.png" Pack="true" PackagePath="\" />` plus
  `<PackageIcon>icon.png</PackageIcon>` in shared metadata.
- **Alternatives considered**: the upstream logo (rejected — trademark/affiliation conflict);
  `PackageIconUrl` (rejected — deprecated, breaks offline render); no icon (rejected — FR-020
  requires one).

## R5. README must use absolute links for off-repository rendering (FR-013)

- **Decision**: Rewrite repository-relative **file** links in `README.md` to absolute
  GitHub URLs before packing.
- **Findings (grep of `README.md`)**: relative links that break on nuget.org —
  `](samples/Mailgunner.Sample)`, `](LICENSE)`, `](CHANGELOG.md)`. Intra-document anchor
  links (`](#quickstart)`, `](#run-the-sample)`, `](#building-from-source)`) render
  correctly on nuget.org and are left as-is. No `<img>` tags or relative image paths exist.
- **Rewrite targets** (using the default branch `master`):
  - `](LICENSE)` → `](https://github.com/gberikov/Mailgunner/blob/master/LICENSE)`
  - `](CHANGELOG.md)` → `](https://github.com/gberikov/Mailgunner/blob/master/CHANGELOG.md)`
  - `](samples/Mailgunner.Sample)` →
    `](https://github.com/gberikov/Mailgunner/tree/master/samples/Mailgunner.Sample)`
- **Rationale**: nuget.org does not rewrite relative links in the rendered README; they
  resolve against `nuget.org` and 404.
- **Alternatives considered**: leave relative (rejected — FR-013 / SC-007 violation).

## R6. Per-version release notes (FR-020)

- **Decision**: Set `PackageReleaseNotes` to a **reachable changelog reference** — the
  absolute URL of `CHANGELOG.md` on GitHub — rather than duplicating notes into the project
  file. FR-020 explicitly permits "a reachable changelog reference."
- **Rationale**: single source of truth for notes (the changelog), no per-release csproj
  edits, survives MinVer-driven versions.
- **Alternatives considered**: inline the version's notes via a build step that slices the
  changelog (rejected — added complexity for marginal benefit on a pre-release).

## R7. CI: build + test on push/PR (FR-018)

- **Decision**: Add `.github/workflows/ci.yml` running on `push` and `pull_request`:
  checkout (full history — `fetch-depth: 0` so MinVer sees tags), `actions/setup-dotnet`
  honoring `global.json`, `dotnet restore`, `dotnet build -c Release`, `dotnet test -c
  Release`. No secrets referenced; the default run is green with no Mailgun and no NuGet
  credentials (constitution Principle III).
- **Determinism**: the existing `Directory.Build.props` already flips
  `ContinuousIntegrationBuild` on when `GITHUB_ACTIONS=true`, so CI builds are
  deterministic and SourceLink-correct with no extra wiring.
- **Alternatives considered**: GitLab CI (rejected — repo is hosted on GitHub); a build
  matrix across OSes (deferred — single `ubuntu-latest` is sufficient for a pure library).

## R8. Release: tag-triggered, credential-gated publish (FR-009, FR-010, FR-015, FR-019)

- **Decision**: Add `.github/workflows/release.yml` triggered only on `push` of tags
  matching `v*`. Steps: checkout (`fetch-depth: 0`), setup-dotnet, `dotnet pack -c Release`,
  then a **gated** `dotnet nuget push` of both `.nupkg` and `.snupkg`.
- **Gating pattern** (the key nuance): GitHub Actions cannot reference `secrets` in a
  job-level `if`. Bind the secret to a step-level `env` and gate on it:

  ```yaml
  - name: Push to NuGet
    env:
      NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}
    if: ${{ env.NUGET_API_KEY != '' }}
    run: >
      dotnet nuget push "artifacts/*.nupkg"
      --api-key "$NUGET_API_KEY"
      --source https://api.nuget.org/v3/index.json
      --skip-duplicate
  ```

  When `NUGET_API_KEY` is **absent** (the state this feature delivers — the secret is never
  created here), the push step is **skipped cleanly**; pack still succeeds, the job is green,
  and nothing is uploaded (FR-010, FR-019). `--skip-duplicate` makes re-publishing an
  already-present version a no-op instead of a failure (FR-015).
- **No publish occurs during this feature**: we neither create the `NUGET_API_KEY` secret
  nor push a `v*` tag, so the release path stays inert (FR-009, FR-016).
- **Symbols**: `dotnet nuget push` of the `.nupkg` plus the co-located `.snupkg` publishes
  symbols to the NuGet symbol server automatically.
- **Alternatives considered**: `nuget push` via NuGet.exe (rejected — `dotnet` CLI is the
  SDK-native path); manual `gh release`-only (rejected — constitution mandates automated
  release on `v*` tags).

## R9. Package identity is available to claim (FR-014)

- **Decision**: Document, in the release procedure, that the maintainer must confirm/own the
  `Mailgunner` id on nuget.org before the first push (it is claimed by first publish).
- **Evidence**: nuget.org registration API returns **404** for `mailgunner` and the search
  API returns **`totalHits: 0`** (including pre-release) — the id is currently **unclaimed
  and available**. No id collision risk for the first publish.
- **Alternatives considered**: prefixing/reserving an id namespace (rejected — unnecessary;
  the plain id is free).

## R10. License presentation (FR-004)

- **Decision**: Keep `PackageLicenseExpression=MIT`. The nuspec already emits
  `<license type="expression">MIT</license>` and nuget.org renders the MIT license on the
  listing. No license **file** needs to be packed.
- **Alternatives considered**: pack the `LICENSE` file via `PackageLicenseFile` (rejected —
  the SPDX expression is the modern, preferred form and already satisfies FR-004).

## Summary of repository changes implied (for Phase 1 / tasks)

| Area | Change |
|------|--------|
| `Directory.Packages.props` | add `MinVer` 7.0.0 version entry |
| `src/Mailgunner/Mailgunner.csproj` | reference `MinVer` (`PrivateAssets=all`); pack `icon.png` |
| `Directory.Build.props` | remove `VersionPrefix`; add MinVer props, `PackageIcon`, `PackageReleaseNotes` |
| `icon.png` (repo root) | new 128×128 icon derived from the provided logo |
| `README.md` | rewrite 3 relative file links to absolute GitHub URLs |
| `CHANGELOG.md` | add `## [0.1.0-preview.1]` entry |
| `.github/workflows/ci.yml` | new — build/test on push + PR |
| `.github/workflows/release.yml` | new — tag-triggered, secret-gated pack + push |
| `docs/RELEASING.md` (or README section) | new — documented, gated release procedure |

No library source, public API, or runtime dependency changes (FR-017).
