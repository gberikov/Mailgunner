# Releasing Mailgunner to NuGet

This is the complete, minimal procedure to publish a release. **Publishing is deliberate
and credential-gated**: nothing is ever published by ordinary development. A release happens
only when (a) the `NUGET_API_KEY` secret is configured **and** (b) a `v*` tag is pushed.

The package version is the single source of truth: it is derived from the git tag by
**MinVer** (`v0.1.0-preview.1` → `0.1.0-preview.1`). Do not hard-code a version anywhere.

## One-time setup

### 1. Confirm the package id is available or owned

The `Mailgunner` id is claimed by the **first** successful push. Before the first release,
verify nobody else owns it:

- **Browser**: open <https://www.nuget.org/packages/Mailgunner> — a 404 page means the id
  is unclaimed and available.
- **API**: a `404` from the registration endpoint means unclaimed:

  ```bash
  curl -s -o /dev/null -w "%{http_code}\n" \
    https://api.nuget.org/v3/registration5-semver1/mailgunner/index.json
  # 404 = available; 200 = already registered (check the owner before publishing)
  ```

> Tip: once you own the id, consider reserving an **id prefix** on nuget.org so future
> related package ids cannot be taken by others.

### 2. Create a NuGet API key and add it as a repository secret

1. Create an API key at <https://www.nuget.org/account/apikeys> scoped to push the
   `Mailgunner` package (a Domain/scoped key, not your full account key where possible).
2. In GitHub: **Settings → Secrets and variables → Actions → New repository secret**,
   name it exactly `NUGET_API_KEY`.

Until this secret exists, the release workflow's push step is **skipped** — you can push a
tag to dry-run the pack without any risk of publishing.

## Cutting a release

1. Ensure `master` is green in CI and the `CHANGELOG.md` has an entry for the version
   you are about to ship (e.g. `## [0.1.0-preview.1]`).
2. Create and push the tag (the `v` prefix is required by `MinVerTagPrefix`):

   ```bash
   git tag v0.1.0-preview.1
   git push origin v0.1.0-preview.1
   ```

3. The **Release** workflow runs: it packs `Mailgunner.<version>.nupkg` + `.snupkg` and,
   **if `NUGET_API_KEY` is present**, pushes both to nuget.org (`--skip-duplicate` makes a
   re-run safe). If the secret is absent, the job packs and then **stops before upload**.

## Versioning notes

- First release is a **pre-release**: `v0.1.0-preview.1`. nuget.org will not surface a
  pre-release as the latest stable version.
- The first **stable** release is a later decision: tag `v0.1.0` (no pre-release suffix).
- Untagged local builds produce a `0.1.0-*` height version via `MinVerMinimumMajorMinor`;
  this is expected and never published.

## Safety guarantees

- No publish on ordinary commits or pull requests — release triggers only on `v*` tags.
- No credentials in the repository — `NUGET_API_KEY` lives only in GitHub Actions secrets.
- Re-pushing an already-published version is a no-op (`--skip-duplicate`), not a failure.
