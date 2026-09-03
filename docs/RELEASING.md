# Releasing Mailgunner to NuGet

This is the complete, minimal procedure to publish a release. **Publishing is deliberate and
credential-free**: nothing is ever published by ordinary development, and no long-lived NuGet API
key exists anywhere. A release happens only when a `v*` tag is pushed; the `release` job in
`.github/workflows/ci.yml` then obtains a short-lived key through **NuGet Trusted Publishing**
(GitHub OIDC) and pushes the package.

The package version is the single source of truth: it is derived from the git tag by
**MinVer** (`v0.1.0` → `0.1.0`, `v0.2.0-preview.1` → `0.2.0-preview.1`). Do not hard-code a version
anywhere.

## Branch model

`develop` is the integration branch, `master` the release branch. Feature branches merge into
`develop`; a release merges `develop` into `master` and is tagged there.

## One-time setup (done)

1. The `Mailgunner` package id is claimed by the first successful push (`v0.1.0`).
2. A **Trusted Publishing policy** exists on nuget.org (Account → Trusted Publishing) for
   repository owner `gberikov`, repository `Mailgunner`, workflow file `ci.yml`, no environment.
   The `release` job logs in with `NuGet/login` as user `gberikov`; if the policy owner, the
   workflow file name, or an environment ever changes, update the job to match or the login step
   fails with an authorization error and nothing is pushed.

## Cutting a release

1. Merge `develop` into `master` and ensure `master` is green in CI. `CHANGELOG.md` must have an
   entry for the version you are about to ship (e.g. `## [0.1.0]`), with `## [Unreleased]` empty
   above it.
2. Create and push the tag on `master` (the `v` prefix is required by `MinVerTagPrefix`):

   ```bash
   git tag v0.1.0
   git push origin v0.1.0
   ```

3. CI runs on the tag: the `build-test` matrix (Linux + Windows, net8.0 and net48 legs) must pass,
   then `release` packs `Mailgunner.<version>.nupkg` + `.snupkg` (package validation runs on pack),
   logs in to NuGet via trusted publishing, and pushes both (`--skip-duplicate` makes a re-run
   safe). If the run fails before the push, nothing was published: fix the failure on `master`,
   delete the tag locally and on origin (`git tag -d vX.Y.Z && git push origin :refs/tags/vX.Y.Z`),
   and re-push it to retry.
4. Create the GitHub release for the tag with the changelog section as notes:

   ```bash
   gh release create v0.1.0 --title "0.1.0" --notes-file <notes>
   ```

## Versioning notes

- A pre-release tag (for example `v0.2.0-preview.1`) is not surfaced by nuget.org as the latest
  stable version.
- Untagged local builds produce a `0.1.0-*` height version via `MinVerMinimumMajorMinor`;
  this is expected and never published.
- After a published tag, set `PackageValidationBaselineVersion` in `Mailgunner.csproj` to that
  version so later packs fail on breaking API changes.

## Safety guarantees

- No publish on ordinary commits or pull requests: the `release` job runs only on `v*` tags and
  only after the full test matrix is green.
- No credentials anywhere: the trusted-publishing key is minted per run and expires within minutes.
- Re-pushing an already-published version is a no-op (`--skip-duplicate`), not a failure.
