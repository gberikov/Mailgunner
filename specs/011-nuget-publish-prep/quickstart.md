# Quickstart: Validate NuGet Publication Readiness

**Feature**: `011-nuget-publish-prep`

This guide proves the repository is *ready to publish* — **without publishing**. It is the
end-to-end validation for the feature. All steps are offline and credential-free.

## Prerequisites

- .NET SDK matching `global.json` (`dotnet --version`).
- A clean working tree on branch `011-nuget-publish-prep`.
- No `NUGET_API_KEY` configured (that is the point — the gate must hold).

## 1. Build & test stay green with no credentials (FR-018, Principle III)

```bash
dotnet build -c Release
dotnet test  -c Release
```

**Expected**: both succeed, 0 warnings / 0 errors; tests pass without any Mailgun or NuGet
credentials.

## 2. Produce the package and symbol artifacts (US1, FR-001)

```bash
dotnet pack src/Mailgunner/Mailgunner.csproj -c Release -o ./artifacts
```

**Expected**: `./artifacts/Mailgunner.<version>.nupkg` **and** `…snupkg` created, 0
warnings. With a release tag checked out (`v0.1.0-preview.1`), `<version>` is
`0.1.0-preview.1` (MinVer); on an untagged commit it is a `0.1.0-*` height version.

## 3. Inspect package contents against the contract (US1, US3)

Open the `.nupkg` (it is a zip) and confirm — see
[contracts/package-metadata.md](./contracts/package-metadata.md):

- `lib/net8.0/Mailgunner.dll` + `.xml` **and** `lib/netstandard2.0/Mailgunner.dll` + `.xml` (FR-006)
- `README.md` present (FR-004)
- `icon.png` present (FR-020)
- `Mailgunner.nuspec` contains `<icon>`, `<releaseNotes>`, `<readme>`, `<license>`, `<tags>`,
  `<projectUrl>`, `<repository>` (FR-003, FR-020)
- `<dependencies>` lists **only** `System.Text.Json`, `Polly`, `Microsoft.Extensions.Http`
  — **no MinVer** (FR-017)

Quick metadata dump (PowerShell):

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$pkg = Get-ChildItem ./artifacts/*.nupkg | Select-Object -First 1
$zip = [IO.Compression.ZipFile]::OpenRead($pkg.FullName)
$zip.Entries.FullName            # list payload
$zip.Dispose()
```

## 4. Verify README renders off-repository (FR-013, SC-007)

Confirm the packed `README.md` has **no** repository-relative file links — `LICENSE`,
`CHANGELOG.md`, and `samples/Mailgunner.Sample` must be absolute `https://github.com/…`
URLs. Anchor links (`#quickstart`) are fine.

```bash
grep -nE '\]\((LICENSE|CHANGELOG\.md|samples/)' README.md   # expect: no matches
```

## 5. Verify the non-affiliation notice (FR-005, SC-008)

```bash
grep -in "not affiliated" README.md
```

**Expected**: present in README; and present in package metadata (description/release
notes) so it shows on the listing.

## 6. Confirm the release path is inert and gated (US2, FR-009/010/016/019)

- `.github/workflows/release.yml` triggers **only** on `push` of `v*` tags.
- The publish step is gated: `env: NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}` with
  `if: env.NUGET_API_KEY != ''`.
- No `NUGET_API_KEY` secret exists and no `v*` tag is pushed → **nothing is published**.

Review against [contracts/release-pipeline.md](./contracts/release-pipeline.md).

## 7. Confirm package id is available (FR-014)

```bash
curl -s -o /dev/null -w "%{http_code}\n" \
  https://api.nuget.org/v3/registration5-semver1/mailgunner/index.json   # expect 404 (unclaimed)
```

## 8. Read the release procedure (FR-012)

Open `docs/RELEASING.md`: it must give the exact, minimal steps to publish later — set the
`NUGET_API_KEY` secret, push `v0.1.0-preview.1`, confirm id ownership — and make clear that
following it without the secret stops before upload.

## Done / Definition of validated

- [ ] build + test green, no credentials (step 1)
- [ ] `.nupkg` + `.snupkg` produced, 0 warnings (step 2)
- [ ] both TFM assemblies, README, icon, complete metadata, MinVer-free deps (step 3)
- [ ] README links absolute (step 4); non-affiliation present (step 5)
- [ ] release workflow inert + gated; nothing published (step 6)
- [ ] package id available (step 7); release procedure documented (step 8)
