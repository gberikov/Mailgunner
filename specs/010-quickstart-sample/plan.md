# Implementation Plan: Quickstart & First-Release Readiness

**Branch**: `010-quickstart-sample` (feature dir `010-quickstart-sample`) | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/010-quickstart-sample/spec.md`

## Summary

Make a developer's first session with Mailgunner succeed: ship a **minimal runnable console
sample** that registers the client and performs a **personalized conference-invitation batch
send** (each of a few recipients gets their own name, ticket number, and personal link), and
align the **README** and **CHANGELOG** so a reader can self-qualify the library without running
anything.

Two artifacts are net-new — the runnable sample and the `0.1.0` changelog entry — while the
README's regions, suppression/unsubscribe, disclaimer sections and the `mailgun` package tag
already exist from features 002–009 and only need a consistency pass plus a single copy-paste
quickstart that demonstrates the same scenario the sample does (FR-011).

Per the clarification (and constitution **Principle III**), the console sample **is** the
project's single environment-gated live check: one live-send code path, targeting a Mailgun
**sandbox** domain, that reads credentials from configuration/environment and is **skipped — not
failed** — when they are absent. No separate live-send integration test is added. The default
`dotnet build` / `dotnet test` run stays green with no credentials present; the only net-new
*automated* test is an **offline** unit test of the sample's credential-presence resolver (the
FR-003 "name the missing settings" behavior), which never touches the network.

The headline technical constraint discovered in research: the library's `SendBatchAsync` is
**stored-template-only** and emits `template` + global `t:variables` + per-recipient
`recipient-variables` (keyed by bare address). Mailgun's stored **Handlebars** templates read
`{{var}}` from `t:variables`, whereas batch per-recipient values arrive as `recipient-variables`
(`%recipient.var%`). The sample bridges the two with the library **as-is** (no library change) by
mapping each template variable to its recipient token in the global `TemplateVariables`
(`name → %recipient.name%`, etc.) so Mailgun resolves a distinct value per recipient. This bridge,
and the one-time "create a sandbox template" prerequisite it implies, are the only real design
decisions; everything else is documentation and project wiring.

## Technical Context

**Language/Version**: C# (`LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`) — inherited from `Directory.Build.props`. The library is unchanged by this feature.

**Primary Dependencies**: **No change to the publishable library or its dependency set.** The
new **sample** is a separate, non-packable console app that references the library by
`ProjectReference` and uses `Microsoft.Extensions.Hosting` (which transitively brings
`DependencyInjection`, `Configuration`, `Configuration.EnvironmentVariables`,
`Configuration.UserSecrets`, and `Logging`) to demonstrate the real `AddMailgunner` registration
path and to surface the resilience exhaustion log. These are **sample-only** dependencies, added
to `Directory.Packages.props` (Central Package Management); they are *not* runtime dependencies of
the `Mailgunner` package, so constitution Principle I's permitted-dependency law (which scopes to
the library) is preserved.

**Storage**: N/A (stateless).

**Testing**: xUnit, fully offline. One net-new unit test exercises the sample's credential-presence
resolver — given a set of present/absent settings it returns either the resolved configuration or
the exact list of missing keys and where to supply them (FR-003 / SC-002), with **zero** network
access. The resolver is **public** on the non-packable sample (no API-surface concern) and the test
project references the sample by `ProjectReference`, so the offline behavior is covered with no
`InternalsVisibleTo` plumbing and no live send. The live send path is *not* unit-tested — it is the
gated live check.

**Target Platform**: Cross-platform .NET. The sample is an executable targeting **`net8.0`**
(`OutputType=Exe`); the library continues to multi-target `net8.0;netstandard2.0`; tests run on `net8.0`.

**Project Type**: Single class-library + offline test project, **plus** a new non-packable console
sample project (NuGet-distributable library + runnable example).

**Performance Goals**: Adoption metric, not throughput: a developer with valid sandbox credentials
reaches a confirmed personalized send in **under 5 minutes** (SC-001). The sample sends the fewest
possible requests (one chunk for 2–3 recipients) and exits promptly.

**Constraints**: Offline/deterministic default build & test (SC-006); warnings-as-errors (applies to
the sample too); no secret in code, sample source, README snippets, or committed configuration
(Principle V, Edge "Secrets in source"); credentials supplied **only** via configuration/environment;
sandbox-only live send; English-only; the sample must **compile without credentials** and only
*send* when they are supplied (FR-004).

**Key environment facts (verified against the 002–009 code):**
- `services.AddMailgunner(...)` registers `IMailgunnerClient` as a typed `HttpClient` and calls
  `ValidateOnStart()`, so a missing/blank domain or sending key (or unspecified region) throws
  `OptionsValidationException` **at host start**. To skip *cleanly* (FR-003) rather than crash, the
  sample MUST pre-check credential presence **before** building/starting the host, print the missing
  settings, and exit `0` — it must not rely on the validator's exception for the no-credentials path.
- `SendBatchAsync(MailgunBatchMessage)` requires a non-blank `Template`, preserves recipient order,
  rejects duplicate addresses, and emits one `recipient-variables` JSON object keyed by each
  recipient's bare `EmailAddress.Address`. There is **no** inline-body batch overload, so the sample
  must use a **stored template** — hence the one-time sandbox-template prerequisite.
- `BatchRecipient.Variables` and `MailgunBatchMessage.TemplateVariables` are the two personalization
  channels the library exposes; the sample uses both (per-recipient values + the global mapping bridge).
- `MailgunRegion` is `Us`/`Eu`; region must match the sandbox domain's region or Mailgun returns 404
  (already documented in the README Regions section — reused, not rewritten).
- Package metadata already carries `PackageReadmeFile=README.md` and `PackageTags=mailgun;sinch;...`
  in `Directory.Build.props`, so **FR-009 is already satisfied**; this feature verifies it, it does
  not re-implement it.
- `VersionPrefix` is already `0.1.0`, matching the changelog target version (FR-010).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Constitution v1.1.0 (2026-06-22). Gates derived from its principles:

| Principle | Gate for this feature | Status |
|-----------|------------------------|--------|
| I. Minimal Dependencies & Modern .NET | **No change to the library or the `Mailgunner` package dependency set.** The sample is a separate, **non-packable** console app; its `Microsoft.Extensions.Hosting` dependency is sample-only and never ships in the package. Principle I scopes the permitted-dependency law to the publishable library, which is untouched. | ✅ PASS |
| II. Managed HTTP & Resilience | The sample registers the client via `AddMailgunner`, so every send still flows through the `IHttpClientFactory` typed client with the existing Polly resilience handler; the sample constructs no `HttpClient`. No HTTP code is added. | ✅ PASS |
| III. Test-First, Network-Free (NON-NEGOTIABLE) | The sample **is** the constitution's single environment-gated live check: sandbox-only, credentials from config/env, **skipped (not failed)** when absent — exactly the carve-out the principle permits, now embodied by the sample instead of a separate xUnit integration test. The only net-new automated test is **offline** (the credential resolver), so the default `dotnet test` run stays green without credentials (SC-006). | ✅ PASS (fulfills the gated-check carve-out) |
| IV. Documented, Strict Public API | **No public type or signature changes** → no SemVer impact from code. The changelog promotes `[Unreleased]` → `[0.1.0]` (Keep a Changelog) per FR-010; README + NuGet metadata already carry the non-affiliation disclaimer. Warnings-as-errors applies to the sample. | ✅ PASS |
| V. Security & Scope Discipline | No secret in sample source, README snippets, or any committed config; credentials only via `IOptions`/configuration/environment; the sample recommends a **Domain Sending Key** and a **sandbox** domain. Adds no endpoint — stays within v1 scope (messages/suppressions/webhooks). | ✅ PASS |
| Mailgun API Fidelity | The sample uses documented batch personalization: `template` + global `t:variables` + per-recipient `recipient-variables` keyed by plain address, ≤ 1000 recipients per request. The README Regions section already documents the region/base-URL contract and 404 mismatch. | ✅ PASS |
| Dev Workflow & Quality Gates | Conventional Commits; `dotnet build`/`dotnet test` green offline without credentials; no secret committed; CI unaffected (sample builds, no live send in CI). | ✅ PASS |

**Result:** **No deviations.** The sample *fulfills* the gated-live-check carve-out rather than
straining any principle, and the library's public surface and dependency set are untouched.
Complexity Tracking is empty.

**Post-Phase-1 re-check:** The design adds one non-packable console project, one offline unit test
(credential resolver), edits to README and CHANGELOG, and central-package entries for the sample's
host dependencies. It changes no public library type, adds no library/package dependency, introduces
no new exception type, and keeps the default build/test offline and green. No principle status
changes. Gate still passes.

## Project Structure

### Documentation (this feature)

```text
specs/010-quickstart-sample/
├── plan.md                       # This file (/speckit-plan output)
├── research.md                   # Phase 0 output (decisions: sample = gated live check; Handlebars↔recipient-variables
│                                  #   bridge; clean no-credentials skip vs ValidateOnStart; config source & keys; sample
│                                  #   deps scoped out of Principle I; offline resolver test; FR-009 already satisfied)
├── data-model.md                 # Phase 1 output (Sample run configuration, Conference invitation recipient,
│                                  #   README quickstart content, Changelog 0.1.0 entry — fields, validation, mappings)
├── quickstart.md                 # Phase 1 output (run/validation guide: run with sandbox creds → success; run without →
│                                  #   skip message; verify README sections, changelog entry, offline green build)
├── contracts/
│   ├── sample-runtime-contract.md# Phase 1 output (the sample's observable CLI contract: config keys, missing-settings
│   │                              #   message, exit codes, success output, no-send-without-creds guarantee)
│   └── docs-content-contract.md  # Phase 1 output (required README sections + the 0.1.0 changelog entry shape & version links)
└── tasks.md                      # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
samples/                                           # NEW top-level folder (added to Mailgunner.slnx under /samples/)
└── Mailgunner.Sample/
    ├── Mailgunner.Sample.csproj                   # NEW: OutputType=Exe; TargetFramework=net8.0; IsPackable=false;
    │                                              #   GenerateDocumentationFile=false; ProjectReference to the library;
    │                                              #   PackageReference Microsoft.Extensions.Hosting (sample-only).
    ├── Program.cs                                 # NEW: build configuration (env vars + optional user-secrets/appsettings) →
    │                                              #   resolve credentials; if missing, print which settings are missing and
    │                                              #   where, exit 0 (clean skip); else AddMailgunner, resolve IMailgunnerClient,
    │                                              #   build the conference-invitation MailgunBatchMessage and SendBatchAsync;
    │                                              #   print a clear success indication (ids/status per chunk).
    ├── SampleConfiguration.cs                     # NEW: pure, public resolver — maps config → resolved settings OR an ordered list of
    │                                              #   missing keys + guidance; no I/O, no network. The unit-tested seam (FR-003).
    ├── ConferenceInvitation.cs                    # NEW: the in-source, VISIBLE sample data — 2–3 recipients each mapped to their
    │                                              #   own name/ticket/link (US1 #3), built into BatchRecipient.Variables, plus the
    │                                              #   global TemplateVariables bridge (name→%recipient.name%, etc.).
    └── appsettings.json                           # NEW (NO SECRETS): non-secret defaults/placeholders only (e.g. Region), with
                                                   #   empty Domain/SendingKey documented as "supply via env/user-secrets".

src/                                               # UNCHANGED — no library code edits in this feature.
└── Mailgunner/ …

tests/
└── Mailgunner.Tests/
    └── Sample/
        └── SampleConfigurationTests.cs            # NEW (offline): missing-credentials cases name the exact missing settings and
                                                   #   where to supply them; complete config resolves; partial config never resolves.
                                                   #   No network, no live send. The ONLY net-new automated test.

# Repository-root docs/metadata:
README.md                                          # CHANGED: single copy-paste conference-invitation quickstart (registration →
                                                   #   SendBatchAsync), a "Run the sample" section + one-time sandbox-template note,
                                                   #   status banner refreshed for the 0.1.0 release. Regions/Suppression/Disclaimer
                                                   #   sections already present — reused & checked for consistency (FR-006/7/8).
CHANGELOG.md                                       # CHANGED: promote [Unreleased] → [0.1.0] - 2026-06-24 enumerating shipped
                                                   #   capabilities incl. the sample & quickstart; add [0.1.0] + reset [Unreleased]
                                                   #   compare links (Keep a Changelog).
Mailgunner.slnx                                    # CHANGED: add /samples/ folder with Mailgunner.Sample.csproj.
Directory.Packages.props                           # CHANGED: add centrally-pinned sample-only PackageVersion(s)
                                                   #   (Microsoft.Extensions.Hosting). Library catalog unchanged.
Directory.Build.props                              # UNCHANGED: PackageReadmeFile + mailgun tag already present (FR-009 satisfied).
```

**Structure Decision**: Keep the established single-library + offline-test layout and add a sibling
**`samples/Mailgunner.Sample/`** console project, wired into `Mailgunner.slnx` under a `/samples/`
folder so it builds with the solution (FR-004) yet ships nothing (`IsPackable=false`). The
credential-presence logic is isolated in a **pure `SampleConfiguration` resolver** so FR-003/SC-002
is verifiable **offline** while the actual send remains the single gated live check (Principle III).
The conference-invitation personalization data lives **in-source and visible** (`ConferenceInvitation`)
per US1 #3, mapped to each recipient via `BatchRecipient.Variables` with the global
`TemplateVariables` bridge that makes the library's existing batch API render per-recipient values
against a stored Handlebars template — no library change required. README and CHANGELOG are edited
in place; the regions/suppression/disclaimer sections and the `mailgun` package tag are reused, not
re-created.

## Complexity Tracking

> No constitutional deviations for this feature — this section is intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | — | — |
