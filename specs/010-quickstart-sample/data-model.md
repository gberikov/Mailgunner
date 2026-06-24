# Phase 1 Data Model: Quickstart & First-Release Readiness

This feature introduces **no new public library types**. The "entities" below are the sample's
configuration/data shapes and the two documentation artifacts. They map directly to the spec's Key
Entities.

## 1. Sample run configuration (`SampleConfiguration`)

The externally supplied settings the sample needs, resolved from configuration/environment only.

| Field | Source (config key) | Required | Validation | Notes |
|-------|---------------------|----------|------------|-------|
| `Domain` | `Mailgun:Domain` | Yes | non-blank | Mailgun **sandbox** sending domain. |
| `SendingKey` | `Mailgun:SendingKey` | Yes | non-blank | Secret; never logged, never in source/committed config. Prefer a **Domain Sending Key**. |
| `Region` | `Mailgun:Region` | Yes | parses to `MailgunRegion` (`Us`/`Eu`) | Must match the domain's region or Mailgun returns 404. |
| `From` | `Mailgun:From` | No | valid address when present | Defaults to `postmaster@{Domain}` (sandbox-authorized sender). |
| `Template` | `Mailgun:Template` | No | non-blank when present | Stored Handlebars template name; defaults to `conference-invitation`. |
| `RecipientAddresses` | `Mailgun:Recipients:N:Address` | Yes (≥ 1, ≤ 3 illustrative) | each non-blank, no duplicates | Must be **authorized recipients** in the sandbox. Personalization (name/ticket/link) is paired in-source. |

**Resolution outcomes** (pure, no I/O):
- *Resolved* — all required settings present and parseable → returns a typed configuration ready for
  `AddMailgunner` + a batch send.
- *Missing* — one or more required settings absent/blank/unparseable → returns an **ordered list** of
  the missing keys plus the guidance "where to supply them" (env var name and user-secrets path). The
  sample prints this and exits cleanly **without sending** (FR-003 / SC-002). A *partial* configuration
  never resolves (it lists exactly what is missing).

## 2. Conference invitation recipient (`BatchRecipient` + in-source personalization)

One addressee in the batch: an email address (from config) paired with that recipient's own
illustrative, **in-source, visible** personalization data (US1 #3).

| Field | Channel | Example | Notes |
|-------|---------|---------|-------|
| `Address` | config (`Mailgun:Recipients:N:Address`) | `dev1@example.com` | Becomes the `recipient-variables` **key** (bare address) and the `to` value. |
| `name` | in-source → `BatchRecipient.Variables["name"]` | `"Ada Lovelace"` | Per-recipient; rendered as `{{name}}`. |
| `ticket` | in-source → `BatchRecipient.Variables["ticket"]` | `"A-1024"` | Per-recipient; rendered as `{{ticket}}`. |
| `link` | in-source → `BatchRecipient.Variables["link"]` | `"https://conf.example/t/A-1024"` | Per-recipient; rendered as `{{link}}`. |

**Personalization bridge** (global `MailgunBatchMessage.TemplateVariables`, emitted as `t:variables`):

```
name   → "%recipient.name%"
ticket → "%recipient.ticket%"
link   → "%recipient.link%"
```

Each recipient's `Variables` (above) supply the per-recipient values for the `%recipient.*%` tokens;
the stored Handlebars template references `{{name}}`/`{{ticket}}`/`{{link}}`. Values are **flat
strings only** (see research §3). Rules enforced by the library, relied on here: recipient order is
preserved, duplicate addresses throw before any request, ≤ 1000 recipients per chunk (2–3 here → one
request).

## 3. README quickstart content

The canonical copy-paste onboarding content plus the orienting sections. State is "present &
consistent", not data per se.

| Element | Requirement | Status |
|---------|-------------|--------|
| Single copy-paste quickstart (registration → personalized batch send) | FR-005, FR-011 | **NEW** — must mirror the sample's scenario. |
| Run-the-sample section + one-time sandbox-template note | FR-001, FR-012, SC-001 | **NEW**. |
| Regions section (region selects host; must match domain) | FR-006 | **Present** (verify). |
| Suppression/unsubscribe section | FR-007 | **Present** (verify). |
| Non-affiliation disclaimer (not affiliated/endorsed by Mailgun or Sinch) | FR-008 | **Present** (verify). |
| Status banner reflecting the 0.1.0 release | — | **CHANGED** (refresh "Foundation scaffold" wording). |

## 4. Changelog first-release entry

The dated, versioned record of the initial release.

| Field | Value | Notes |
|-------|-------|-------|
| Version | `0.1.0` | Matches `VersionPrefix`; FR-010. |
| Date | `2026-06-24` | Today; ISO date per Keep a Changelog. |
| Section | `### Added` | Promote the existing `[Unreleased]` Added items. |
| New line item | Quickstart + runnable conference-invitation sample (the gated live check) | The net-new capability this feature ships. |
| Link refs | `[0.1.0]: …/releases/tag/v0.1.0`; reset `[Unreleased]: …/compare/v0.1.0...HEAD` | FR-010 "corresponding version-link reference". |

## Package metadata (already satisfied — verification only)

| Field | Location | Value | FR |
|-------|----------|-------|----|
| README in package | `Directory.Build.props` | `PackageReadmeFile=README.md` | FR-009 |
| Discovery tags incl. `mailgun` | `Directory.Build.props` | `PackageTags=mailgun;sinch;email;transactional-email;smtp;mail` | FR-009 |
