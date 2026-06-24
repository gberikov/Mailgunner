# Contract: CI & Release Pipeline (GitHub Actions)

**Feature**: `011-nuget-publish-prep`

Defines the observable behavior the two workflows MUST satisfy. These are behavioral
contracts (triggers, gates, outcomes), not full YAML — implementation lives in tasks.

## Workflow A — `ci.yml` (integration)

| Aspect | Contract | FR |
|--------|----------|----|
| Triggers | `on: [push, pull_request]` | FR-018 |
| Checkout | `fetch-depth: 0` (MinVer needs tags/history) | FR-008 |
| SDK | `actions/setup-dotnet` honoring `global.json` | — |
| Steps | restore → `build -c Release` → `test -c Release` | FR-018 |
| Credentials | none referenced | FR-018, Principle III |
| Outcome | green on a clean checkout with **no** secrets | SC-? (CI green) |
| Determinism | `ContinuousIntegrationBuild` auto-on via `GITHUB_ACTIONS` | FR-007 |

**MUST NOT**: reference `NUGET_API_KEY`; push any package; require Mailgun credentials.

## Workflow B — `release.yml` (gated publish)

| Aspect | Contract | FR |
|--------|----------|----|
| Trigger | `on: push: tags: ['v*']` only | FR-009 |
| Checkout | `fetch-depth: 0` | FR-008 |
| Pack | `dotnet pack -c Release` → `.nupkg` + `.snupkg` | FR-001 |
| Version | derived by MinVer from the pushed tag | FR-008 |
| Publish gate | step-level `env: NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}` + `if: env.NUGET_API_KEY != ''` | FR-010, FR-019 |
| Publish cmd | `dotnet nuget push '*.nupkg' --api-key … --source nuget.org --skip-duplicate` | FR-015 |
| Absent secret | pack succeeds, push **skipped cleanly**, job green, nothing uploaded | FR-010, FR-019 |
| Idempotency | `--skip-duplicate` → re-tag/re-run does not fail or corrupt listing | FR-015 |

**MUST NOT** (this feature's delivered state): create the `NUGET_API_KEY` secret; push a
`v*` tag; perform any upload (FR-016).

## Gate rationale (implementation note)

GitHub Actions forbids `secrets.*` in job-level `if`. The contract therefore requires the
secret be bound to a **step-level `env`** and the gate expressed as `if: env.NUGET_API_KEY
!= ''`. This is the mechanism that makes "absent credential ⇒ clean skip" true rather than
a hard failure.

## Acceptance mapping

- US2 AC1 (no publish on ordinary dev) ⇐ Workflow B triggers only on `v*` tags.
- US2 AC2 (halts before upload without credential) ⇐ publish gate.
- US2 AC3 (documented minimal steps) ⇐ `docs/RELEASING.md` (FR-012).
- US2 AC4 (single version source) ⇐ MinVer from tag.
- Edge "already-published version" ⇐ `--skip-duplicate`.
