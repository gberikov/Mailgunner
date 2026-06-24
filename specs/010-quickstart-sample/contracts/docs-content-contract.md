# Contract: Documentation Content (README + CHANGELOG)

What the published docs must contain after this feature. "Present" items already exist from features
002–009 and are verified/aligned, not re-created.

## README.md

| Section | Must contain | FR | Status |
|---------|--------------|----|--------|
| Quickstart | A **single copy-paste** block taking a reader from `AddMailgunner` registration → resolve `IMailgunnerClient` → build the conference-invitation `MailgunBatchMessage` (template + per-recipient name/ticket/link + global bridge) → `SendBatchAsync`, with only domain/key/region/recipients to adapt. | FR-005, FR-011 | **NEW** |
| Run the sample | How to run `samples/Mailgunner.Sample` with sandbox credentials supplied via env/user-secrets, the one-time **stored Handlebars template** prerequisite (template body referencing `{{name}}`/`{{ticket}}`/`{{link}}`), and the no-credentials skip behavior. | FR-001, FR-003, FR-012 | **NEW** |
| Regions | Region selects the API host (US→`api.mailgun.net`, EU→`api.eu.mailgun.net`) and **must match** the domain's region (mismatch → 404). | FR-006 | **Present** |
| Suppression / unsubscribe | How unsubscribes and the other suppression lists (bounces, complaints) are accessed and used via `client.Suppressions`. | FR-007 | **Present** |
| Disclaimer | States the library is **community-maintained and not affiliated with, authorized by, or endorsed by Mailgun or Sinch**. | FR-008 | **Present** |
| Status banner | Refreshed to reflect the `0.1.0` first release (no longer "foundation scaffold, functionality delivered later"). | — | **CHANGED** |

**Consistency rule (FR-011)**: the README quickstart and the runnable sample MUST demonstrate the
**same** conference-invitation scenario (same template variable names, same per-recipient fields), so
a reader who copies one and runs the other is not surprised.

## CHANGELOG.md (Keep a Changelog)

| Element | Requirement | FR |
|---------|-------------|----|
| Versioned entry | `## [0.1.0] - 2026-06-24` with `### Added` enumerating the shipped capabilities (DI registration/regions/auth, single + templated send, personalized batch, send options, suppressions, webhook verification, retry/backoff) **plus** the new quickstart + runnable conference-invitation sample. | FR-010 |
| Promotion | The previous open `[Unreleased]` items are promoted into `[0.1.0]`; a fresh empty `[Unreleased]` remains at the top. | FR-010 |
| Version links | `[Unreleased]: …/compare/v0.1.0...HEAD` and `[0.1.0]: …/releases/tag/v0.1.0`. | FR-010 |

## Package metadata (verify only — already satisfied)

| Element | Where | FR |
|---------|-------|----|
| `PackageReadmeFile=README.md` (README renders on the package page) | `Directory.Build.props` | FR-009 |
| `PackageTags` include `mailgun` | `Directory.Build.props` | FR-009 |
