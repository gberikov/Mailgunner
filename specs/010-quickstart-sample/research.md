# Phase 0 Research: Quickstart & First-Release Readiness

All NEEDS CLARIFICATION from the Technical Context are resolved below. Each decision lists the
choice, why it was chosen, and the alternatives rejected.

## 1. The runnable sample is the single environment-gated live check

- **Decision**: Implement the headline artifact as a standalone **console application**
  (`samples/Mailgunner.Sample`) that doubles as the constitution's *single* environment-gated live
  check. There is **no** separate xUnit live-send integration test.
- **Rationale**: The spec clarification (2026-06-24) makes this explicit, and constitution
  Principle III permits *exactly one* environment-gated live check that (a) targets a Mailgun
  **sandbox** domain, (b) reads credentials from configuration/environment, and (c) is **skipped —
  not failed** — when credentials are absent. A console app the developer runs directly satisfies all
  three and is simultaneously the strongest adoption artifact ("time to first successful send").
  Folding the live check into the sample avoids maintaining two near-duplicate live-send code paths.
- **Alternatives considered**:
  - *A `[Fact(Skip=…)]` xUnit integration test plus a separate sample* — rejected: duplicates the
    live-send path and the principle allows only one live check.
  - *A sample with no live send at all (print-only)* — rejected: fails US1/SC-001 ("a confirmed
    personalized batch send").

## 2. Clean no-credentials skip vs. `ValidateOnStart()`

- **Decision**: The sample resolves credential **presence in a pure pre-check** (`SampleConfiguration`)
  **before** building/starting the host. If any required setting is missing it prints the exact
  missing keys and where to supply them, then exits `0`. It calls `AddMailgunner(...)` only on the
  path where all credentials are present.
- **Rationale**: `AddMailgunner` registers `ValidateOnStart()`, so a missing domain/key/region throws
  `OptionsValidationException` at host start. That is a *crash*, not the clean, actionable skip FR-003
  / SC-002 demand ("name the missing settings 100% of the time, never a partial/silent send"). A pure
  pre-check guarantees a deterministic, friendly message and a non-error exit, and is unit-testable
  offline.
- **Alternatives considered**:
  - *Catch `OptionsValidationException` and reformat it* — rejected: the validator's message names one
    setting at a time and is not designed as user-facing onboarding copy; harder to test deterministically.
  - *Rely on Mailgun returning an auth error* — rejected: would attempt a send (violates "never a
    silent/partial send") and requires network.

## 3. Personalization mechanism — Handlebars template ↔ recipient-variables bridge

- **Decision**: The sample sends via `SendBatchAsync` against a **stored Handlebars template** the
  developer creates once in their sandbox (named e.g. `conference-invitation`, body referencing
  `{{name}}`, `{{ticket}}`, `{{link}}`). The sample supplies **per-recipient** values through
  `BatchRecipient.Variables` (which the library emits as `recipient-variables` keyed by bare address)
  **and** sets the **global** `MailgunBatchMessage.TemplateVariables` to the bridge mapping
  `name → "%recipient.name%"`, `ticket → "%recipient.ticket%"`, `link → "%recipient.link%"` (emitted
  as `t:variables`). Mailgun substitutes each recipient's value into the `%recipient.*%` tokens and the
  Handlebars template renders `{{name}}`/`{{ticket}}`/`{{link}}` per recipient.
- **Rationale**: The library's batch API is **stored-template-only** (`Template` is required; there is
  no inline-body batch overload), so a stored template is unavoidable. Mailgun exposes **two distinct**
  substitution channels that do not auto-compose: stored Handlebars templates read `{{var}}` from
  `t:variables` (global), while batch per-recipient data arrives as `recipient-variables`
  (`%recipient.var%`). Mailgun support and the battle-tested Anymail library confirm that **simple
  string/number** recipient values can be surfaced to a stored template, and the documented bridge is
  to map template variables to `%recipient.*%` tokens via `t:variables`. name/ticket/link are all
  simple strings, so this works with the library **unchanged**. Because the sample *is* the live check,
  a real sandbox run verifies the rendering end-to-end (US1 Independent Test).
- **Risk & mitigation**: Mailgun's docs on template↔recipient-variables integration are
  version-dependent and historically contradictory (complex types like arrays are explicitly
  unsupported through recipient-variables). Mitigation: keep recipient values to **flat strings**;
  document the exact template body in the quickstart; let the gated live run confirm it; and (fallback)
  if a given sandbox does not honor the `%recipient.*%`-in-`t:variables` bridge, the same flat values
  also render when the template references `{{name}}` directly against `recipient-variables` — the
  quickstart notes both forms so the developer can self-correct without code changes.
- **Alternatives considered**:
  - *Loop `SendAsync` once per recipient with an inline body* — rejected: not "a personalized **batch**"
    and bypasses the library's headline `recipient-variables` mechanism the spec asks to demonstrate.
  - *Add an inline-body batch overload to the library* — rejected: out of scope (this feature changes
    no public API) and unnecessary given the bridge works as-is.

## 4. Configuration source and key names

- **Decision**: The sample builds an `IConfiguration` from **environment variables** plus optional
  **user-secrets** (Development) and an **appsettings.json** that contains only non-secret placeholders.
  Credentials/region bind from a `Mailgun` section: `Mailgun:Domain`, `Mailgun:SendingKey`,
  `Mailgun:Region` (`Us`/`Eu`). The **sender** defaults to `postmaster@{Domain}` (sandbox-authorized)
  unless `Mailgun:From` overrides it. **Recipient addresses** are configurable
  (`Mailgun:Recipients:0:Address`, …) because a sandbox only delivers to *authorized recipients*; the
  per-recipient **personalization** (name/ticket/link) is illustrative, in-source, and visible. The
  stored template name defaults to `conference-invitation` and is overridable via `Mailgun:Template`.
- **Rationale**: Environment variables + user-secrets are the platform-standard, no-new-system way to
  supply secrets without editing source (Principle V, SC-001). Binding to the same `MailgunnerOptions`
  shape mirrors the README quickstart, satisfying FR-011 (quickstart and sample demonstrate the same
  scenario). Making recipient *addresses* configurable respects the sandbox "authorized recipients"
  limitation while keeping the *personalization* fields visible per US1 #3.
- **Alternatives considered**:
  - *Hard-code recipient addresses* — rejected: a sandbox would reject unauthorized recipients, so the
    sample would fail for most developers; addresses must be supplied.
  - *Read everything (incl. names/tickets/links) from config* — rejected: hurts SC-001 ("within
    minutes") and US1 #3 wants the per-recipient fields **visible in source**.
  - *A bespoke env-var scheme (`MG_KEY`, …)* — rejected in favor of standard `Mailgun:`-section binding
    that matches the library's `MailgunnerOptions` and the README.

## 5. Sample dependencies vs. Principle I

- **Decision**: The sample uses `Microsoft.Extensions.Hosting` (transitively DI + Configuration +
  Logging), pinned centrally in `Directory.Packages.props`. These are **sample-only** and never ship.
- **Rationale**: Principle I's permitted-dependency law governs the **publishable library**'s runtime
  dependency graph (supply-chain/version-conflict risk for *consumers*). The sample is a separate,
  `IsPackable=false` executable that no consumer references, so its host dependencies are out of scope
  for Principle I. `Hosting` also lets the sample demonstrate the *real* DI registration path and
  surface the resilience exhaustion log, strengthening the onboarding story.
- **Alternatives considered**:
  - *Zero-dependency sample (raw `Environment.GetEnvironmentVariable`, manual `ServiceCollection`)* —
    viable and even leaner, rejected only because `Hosting` better mirrors how a real app wires the
    client; kept as a fallback if minimizing sample deps is later preferred.

## 6. Offline test scope (default build/test stays green)

- **Decision**: Add exactly one net-new automated test class — an **offline** unit test of
  `SampleConfiguration` — and **no** automated live-send test. The resolver is **public** on the
  non-packable sample, and the test project references the sample by `ProjectReference` (no
  `InternalsVisibleTo` needed).
- **Rationale**: SC-002 requires the missing-settings message "100% of the time", which is pure logic
  best pinned by a deterministic offline test (Principle III: new behavior lands with tests). SC-006 /
  FR-004 require the default `dotnet test` to pass without credentials; an offline resolver test keeps
  the suite green and credential-free. Testing the live send is *not* done in CI — that path is the
  gated sample.
- **Alternatives considered**:
  - *No test for the resolver* — rejected: leaves SC-002's "100%" guarantee unverified.
  - *An integration test that runs the exe* — rejected: heavier, and the offline unit test covers the
    contract that matters (which keys are missing and the guidance text).

## 7. Items already satisfied by features 002–009 (verification only)

- **Decision**: Treat FR-006 (regions), FR-007 (suppression/unsubscribe), FR-008 (disclaimer), and
  FR-009 (README in package + `mailgun` tag) as **already present**; this feature **verifies and
  aligns** them rather than re-implementing.
- **Rationale**: The current `README.md` already contains a **Regions** section, a **Suppression
  lists** section, and a **Disclaimer**; `Directory.Build.props` already sets
  `PackageReadmeFile=README.md` and `PackageTags=mailgun;sinch;email;transactional-email;smtp;mail`.
  The spec's Assumptions confirm these pre-exist. Net-new work is the quickstart block, the runnable
  sample, and the `0.1.0` changelog entry.
- **Alternatives considered**: *Rewrite the sections* — rejected: unnecessary churn; consistency pass
  is sufficient (FR-011).

## 8. Changelog: promote `[Unreleased]` → `[0.1.0]`

- **Decision**: Convert the open `[Unreleased]` section into `## [0.1.0] - 2026-06-24`, enumerating
  the shipped capabilities (registration/regions/auth, single & templated send, personalized batch,
  send options, suppressions, webhook verification, retry/backoff, **and the new quickstart + runnable
  sample**). Add a fresh empty `[Unreleased]` above it, and update the link references to
  `[Unreleased]: …/compare/v0.1.0...HEAD` and `[0.1.0]: …/releases/tag/v0.1.0` (Keep a Changelog).
- **Rationale**: FR-010 names version **`0.1.0`** (matching `VersionPrefix` and the pre-1.0 foundation
  phase) and requires a dated, versioned entry with the corresponding version-link reference, per the
  project's existing Keep a Changelog format.
- **Alternatives considered**: *Leave an open Unreleased only* — rejected: FR-010/SC-005 require a
  dated, versioned first-release entry, not just an open section.
