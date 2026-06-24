# Quickstart & Validation Guide: Quickstart & First-Release Readiness

A run/validation guide proving the feature works end-to-end. Implementation details live in
`tasks.md` and the code; this guide shows how each acceptance scenario is demonstrated.

## Prerequisites

- A .NET SDK matching `global.json` (slnx-capable; .NET 10 recommended).
- For the **live** path only: a Mailgun **sandbox** domain, a sending key (prefer a Domain Sending
  Key), one or more **authorized recipients** on that sandbox, and a one-time **stored template**.
- No credentials are needed to build the repo or run the default test suite.

## One-time setup for the live run

1. In the Mailgun dashboard, add your test addresses as **authorized recipients** of the sandbox.
2. Create a **stored Handlebars template** named `conference-invitation` whose body references the
   per-recipient fields, for example:

   ```handlebars
   <p>Hi {{name}}, your ticket is <strong>{{ticket}}</strong>.</p>
   <p>Your personal link: <a href="{{link}}">{{link}}</a></p>
   ```

3. Supply credentials via environment or user-secrets (never edit source):

   ```bash
   # environment variables (note the __ section separator)
   export Mailgun__Domain="sandboxXXXX.mailgun.org"
   export Mailgun__SendingKey="key-…"          # a Domain Sending Key
   export Mailgun__Region="Us"                  # Us or Eu (must match the domain)
   export Mailgun__Recipients__0__Address="you@example.com"
   export Mailgun__Recipients__1__Address="teammate@example.com"
   ```

## Validate — Build & default tests stay green without credentials (SC-006 / FR-004)

```bash
dotnet build Mailgunner.slnx -c Release
dotnet test  Mailgunner.slnx -c Release
```

**Expected**: build succeeds (including `samples/Mailgunner.Sample`); all tests pass; no network
access and no Mailgun credentials required. The new offline `SampleConfigurationTests` are included
and green.

## Validate US1 — Personalized send from the sample (P1)

### With credentials present → confirmed personalized batch (SC-001, SC-004)

```bash
dotnet run --project samples/Mailgunner.Sample -c Release
```

**Expected**: the client registers, one batch request is sent, and the sample prints a success line
per chunk (Mailgun id + status). Each authorized recipient receives a message addressed to them with
their **own** name, ticket, and link. (Set the sample to test mode, or inspect the sent messages, to
confirm the per-recipient values differ.) Total time from opening the repo to confirmed send: under
5 minutes.

### With credentials absent → clean skip, no send (SC-002 / FR-003)

```bash
# unset any Mailgun__* vars first
dotnet run --project samples/Mailgunner.Sample -c Release
```

**Expected**: the sample prints exactly which settings are missing and where to supply them (env var
name + user-secrets command), makes **no** HTTP request, and exits `0`. No partial or silent send.

### Reading the source (US1 #3)

Open `samples/Mailgunner.Sample/ConferenceInvitation.cs`: the per-recipient `name`, `ticket`, and
`link` are visible and obviously mapped to each recipient.

## Validate US2 — README quickstart & orienting sections (P2, SC-003)

Open `README.md` and confirm all four are present and answerable without leaving the README:

1. A **single copy-paste quickstart** from registration through a personalized batch send.
2. A **Regions** section (region selects the host; must match the domain).
3. A **Suppression/unsubscribe** section (how `client.Suppressions` is used).
4. A **Disclaimer** (not affiliated with or endorsed by Mailgun or Sinch).

Consistency check (FR-011): the quickstart uses the same template variable names
(`name`/`ticket`/`link`) and scenario as the sample.

## Validate US3 — Package presentation & changelog (P3, SC-005)

- `Directory.Build.props` sets `PackageReadmeFile=README.md` and `PackageTags` include `mailgun`
  (README renders on the package page; discovery tag present).
- `CHANGELOG.md` contains a dated, versioned `## [0.1.0] - 2026-06-24` entry enumerating the shipped
  capabilities, with `[0.1.0]` and `[Unreleased]` link references (Keep a Changelog).

Optional package check:

```bash
dotnet pack src/Mailgunner/Mailgunner.csproj -c Release
# inspect the .nupkg: README.md present; nuspec <tags> include "mailgun".
```

## Acceptance → verification map

| Scenario | Verified by |
|----------|-------------|
| US1 #1 (creds → personalized batch) | `dotnet run` live; per-recipient output (SC-001/004) |
| US1 #2 (no creds → named missing settings) | `dotnet run` with vars unset; `SampleConfigurationTests` (SC-002) |
| US1 #3 (personalization visible in source) | read `ConferenceInvitation.cs` |
| US2 #1–4 (quickstart, regions, suppression, disclaimer) | README review (SC-003) |
| US3 #1–2 (README+tags, changelog entry) | `Directory.Build.props` + `CHANGELOG.md` (SC-005) |
| No-credentials green build/test | `dotnet build` + `dotnet test` (SC-006) |
