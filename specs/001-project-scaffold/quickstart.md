# Quickstart: Validate the Project Scaffold

This guide proves the scaffold works end-to-end from a clean clone. It validates the
contract in [contracts/build-package-contract.md](./contracts/build-package-contract.md) and
the success criteria in [spec.md](./spec.md). It is a validation/run guide — implementation
details live in `tasks.md` and the implementation phase.

## Prerequisites

- A `slnx`-capable .NET SDK installed (floor pinned in `global.json`; target a .NET 10 SDK).
  Verify: `dotnet --version` (must satisfy the `global.json` floor).
- Git, with the remote `https://github.com/gberikov/Mailgunner` configured (required for
  SourceLink to resolve repository metadata).
- No Mailgun credentials or secrets needed at any point.

## Scenario 1 — Clean build & offline tests (SC-001, SC-004; FR-001..FR-003)

```bash
git clone https://github.com/gberikov/Mailgunner.git
cd Mailgunner
dotnet restore
dotnet build Mailgunner.slnx -c Release
dotnet test Mailgunner.slnx -c Release
```

**Expected**: restore/build succeed with **no warnings and no errors**; the test project is
discovered and all tests pass. No network access or credentials are required by tests.

## Scenario 2 — Quality gates fail the build (SC-003; FR-006, Principle IV)

Temporarily introduce a violation, then build:

- Add a `public` member to the library **without** an XML doc comment → build FAILS (CS1591
  treated as error).
- Introduce a formatting/naming violation that contradicts `.editorconfig` → build FAILS.
- Add a `Version="x"` attribute to any `PackageReference` → restore/build FAILS (CPM active).

**Expected**: each case fails the build rather than being silently accepted. Revert after.

## Scenario 3 — Inheritance & adding a project (SC-002; FR-005, FR-015)

- Inspect `src/Mailgunner/Mailgunner.csproj`: it does NOT redeclare nullable, lang version,
  docs, warnings-as-errors, or package metadata — these come from `Directory.Build.props`.
- (Optional) `dotnet new classlib` into the solution and build: it inherits the shared
  style/build/version settings without copying configuration.

**Expected**: 100% of projects inherit shared settings; no project re-declares them.

## Scenario 4 — Package & SourceLink (SC-005, SC-006; FR-009, FR-010)

```bash
dotnet pack src/Mailgunner/Mailgunner.csproj -c Release -o ./artifacts
```

**Expected**: `./artifacts` contains `Mailgunner.<version>.nupkg` and a matching `.snupkg`.
Inspect the `.nupkg` (e.g., NuGet Package Explorer):

- Identity = `Mailgunner`; authors = `Gany Berikov`; license expression matches `LICENSE`;
  README present; `RepositoryUrl`/`PackageProjectUrl` = `https://github.com/gberikov/Mailgunner`.
- `lib/net8.0/` and `lib/netstandard2.0/` assemblies present.
- SourceLink/Repository CDI present in the PDBs.

Determinism: build the same commit on a second machine (with `ContinuousIntegrationBuild=true`)
and confirm equivalent output.

## Scenario 5 — VCS hygiene (SC-007; FR-011)

```bash
dotnet build -c Release
git status
```

**Expected**: no `bin/`, `obj/`, IDE folders, user files, or secret files appear as
to-be-committed. `.gitignore` excludes them.

## Done / Acceptance

All five scenarios pass ⇒ the scaffold satisfies the spec's mandatory FRs and success
criteria, and the repository is ready for the first functional feature.
