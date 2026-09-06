# Mailgunner — contributor and agent guide

Unofficial .NET client for the Mailgun (Sinch) REST API. This file is the working agreement for
anyone — human or coding agent — touching this repository.

## Where things are

| Path | Purpose |
|------|---------|
| `src/Mailgunner/` | The publishable library; the only project that ships to NuGet. |
| `tests/Mailgunner.Tests/` | Offline xUnit suite. No network, no credentials — this is non-negotiable. |
| `tests/Mailgunner.NetFxTests/` | net48 tests exercising the netstandard2.0 build. |
| `tests/Mailgunner.IntegrationTests/` | Opt-in live tests; skipped unless `Mailgun__*` is set. |
| `samples/Mailgunner.Sample/` | Runnable personalized-batch sample, also the live smoke check. |
| `docs/` | User documentation, split by topic; `README.md` is the index. |
| `Directory.Build.props` | Shared build, quality and package settings. |
| `Directory.Packages.props` | Central Package Management — pinned versions, no floating refs. |
| `.editorconfig` | Build-enforced style and analyzer rules. |

Build and test:

```bash
dotnet build Mailgunner.slnx -c Release
dotnet test Mailgunner.slnx -c Release
```

Warnings are errors and nullable reference types are on. A change that only compiles with a
suppression needs a reason in the diff, not just a pragma.

## Documentation

User-facing documentation lives in `docs/` and is linked from `README.md`. `README.md` is also
packed into the NuGet package, where it is rendered outside the repository — **every link in
`README.md` and in `docs/` must be absolute** (`https://github.com/gberikov/Mailgunner/blob/master/...`),
because relative links 404 on nuget.org. When you add a guide, add its row to the README index.

## Git flow

The repository follows git flow:

| Branch | Purpose |
|--------|---------|
| `master` | Stable releases; merges from `release/*` and `hotfix/*` only |
| `develop` | Integration branch; base for all features |
| `feature/*` | New functionality; branches off `develop` |
| `bugfix/*` | Fixes in `develop` |
| `release/*` | Release preparation: `develop` → `master` |
| `hotfix/*` | Urgent fixes off `master` |

- **No direct commits to `master` or `develop`** — branches and merges only.
- Branches are pushed to `origin` (`https://github.com/gberikov/Mailgunner`) and land in
  `develop` through a pull request.
- Version tags carry the **`v` prefix**: `v0.1.0` (matches `MinVerTagPrefix` in
  `Directory.Build.props`, which derives the package version from the tag, and the `v*` trigger
  of the release job in `.github/workflows/ci.yml`). The tag goes on `master` at release time
  via `release/*`.
- The git-flow extension matches this layout (`master`/`develop`, `feature/`, `bugfix/`,
  `release/`, `hotfix/`, version tag prefix `v`), so `git flow feature start <name>` works;
  by hand it is `git checkout -b feature/<name> develop`.

The release procedure itself is in
[docs/RELEASING.md](https://github.com/gberikov/Mailgunner/blob/master/docs/RELEASING.md).

### Protecting master, develop and tags

The repository is public, so protection lives on the GitHub side — two active rulesets:

| Ruleset | Target | Forbids |
|---------|--------|---------|
| `protected branches` | `refs/heads/master`, `refs/heads/develop` | deletion, non-fast-forward push |
| `version tags` | `refs/tags/**` | deletion, update, non-fast-forward |

Creating a tag is allowed; changing an existing one is not — a released version is immutable, like
the package on nuget.org. Neither ruleset has bypass actors, so they apply to the owner too. A
force-push to `master` therefore requires disabling the ruleset in the UI: a visible action, unlike
a quiet `--no-verify`. Feature branches are untouched — `gh api repos/gberikov/Mailgunner/rules/branches/<name>`
returns the rules in force for any branch.

Neither ruleset requires a pull request, deliberately: the release flow merges `release/*` into
`master` locally, and a required-PR rule would break it.

What the rulesets cannot forbid is an ordinary push of new commits to `master`/`develop` — allow
it they must, or no release could land. That gap is covered locally by `.githooks/pre-push`, which
rejects a push leaving `master` or `develop` on a non-merge commit (i.e. a commit made straight on
the branch, bypassing `feature/*`, `release/*` and friends). It is local, so re-run this after
every clone — git does not carry the config over:

```bash
git config core.hooksPath .githooks
```

`git push --no-verify` is the deliberate escape hatch.

## Commits

Conventional commits, in English:

```
feat(webhooks): fluent builder for event filters
fix(messages): do not fail on an unknown event type
docs(design): decision on idempotency
refactor(json): extract the event converter
test(messages): golden JSON for the request
```

Every user-visible change gets a `CHANGELOG.md` entry (Keep a Changelog format) in the same
commit as the change.
