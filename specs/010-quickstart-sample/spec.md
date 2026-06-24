# Feature Specification: Quickstart & First-Release Readiness

**Feature Branch**: `010-quickstart-sample`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "A developer evaluating Mailgunner reads a quickstart and runs a minimal runnable sample that registers the client and sends a personalized batch — a conference-invitation scenario with per-recipient name, ticket number, and personal link — and understands regions, suppression/unsubscribe usage, and the project's unaffiliated status from the README. Adoption depends on reaching a working send within minutes."

## Clarifications

### Session 2026-06-24

- Q: Is the runnable sample a standalone artifact separate from the constitution's single permitted gated live check, or the same one? → A: A standalone runnable console sample is the headline adoption artifact and *also* serves as the project's single environment-gated live check — one live-send code path, skipped (not failed) when sandbox credentials are absent. No separate live-send integration test is added.
- Q: What version string is the "upcoming first release" recorded in the changelog? → A: `0.1.0` — matches the existing `VersionPrefix`, honors the documented pre-1.0 foundation phase, and signals an initial-development API under SemVer.

## User Scenarios & Testing *(mandatory)*

A developer is evaluating Mailgunner as the email library for their product. Their first
session decides adoption: they want to read a short quickstart, run something that actually
sends, and confirm the project is trustworthy and unaffiliated with Mailgun/Sinch — all
within a few minutes. This feature makes that first session succeed.

### User Story 1 - Reach a working personalized send from a runnable sample (Priority: P1)

A developer clones or opens the repository, supplies their own Mailgun **sandbox**
credentials through configuration (never editing source), and runs the bundled sample. The
sample registers the client and sends a **personalized batch** modeling a real
conference-invitation mailing: each of a few recipients receives a message addressed to them
by name and carrying their own ticket number and personal link. The developer sees the send
succeed and understands, from the sample's shape, how to do the same in their own app.

**Why this priority**: "Time to first successful send" is the single strongest signal of
adoption for a client library. A runnable, credential-driven sample that demonstrates the
library's headline capability (personalized bulk delivery) is the highest-value artifact and
is independently demonstrable on its own.

**Independent Test**: With valid sandbox credentials provided via configuration, run the
sample and confirm it performs a real personalized batch send to a few addresses where each
recipient's name, ticket number, and personal link differ. With credentials absent, the
sample exits cleanly with a clear message explaining what to supply, rather than crashing or
sending nothing silently.

**Acceptance Scenarios**:

1. **Given** valid Mailgun sandbox credentials supplied via configuration, **When** the
   developer runs the sample, **Then** the client is registered and a personalized batch is
   delivered to a few distinct recipients, each receiving their own name, ticket number, and
   personal link, and the developer sees a clear success indication.
2. **Given** no credentials are present in the environment/configuration, **When** the
   developer runs the sample, **Then** it does not attempt a send and instead reports exactly
   which settings are missing and where to put them.
3. **Given** the developer reads only the sample's source, **When** they look for how
   personalization is expressed, **Then** the per-recipient fields (name, ticket number,
   personal link) are visible and obviously map to each recipient.

---

### User Story 2 - Understand the library from a copy-paste README quickstart (Priority: P2)

A developer who has not run anything yet skims the README. They find a single copy-paste
quickstart that takes them from registration to a personalized send, plus short sections that
explain regions (US/EU host selection) and suppression/unsubscribe usage, and a clearly
stated disclaimer that the project is not affiliated with or endorsed by Mailgun or Sinch.

**Why this priority**: Many evaluators decide from the README alone before running code. The
quickstart and the orienting sections (regions, suppression/unsubscribe, disclaimer) let a
reader self-qualify the library in under a minute. It builds on the same scenario as P1 but
delivers value to readers who never run the sample.

**Independent Test**: A first-time reader can copy the quickstart block, adapt only the
domain/key/recipients, and have a compiling personalized send; the regions,
suppression/unsubscribe, and disclaimer sections are each present and answer their question
without leaving the README.

**Acceptance Scenarios**:

1. **Given** a reader opens the README, **When** they look for how to get started, **Then** a
   single copy-paste quickstart shows registration through a personalized batch send.
2. **Given** a reader is unsure which region to use, **When** they read the regions section,
   **Then** they learn that the region selects the API host and that region must match the
   domain's region.
3. **Given** a reader needs to manage opt-outs, **When** they read the suppression/unsubscribe
   section, **Then** they learn how unsubscribes and the other suppression lists are accessed.
4. **Given** a reader checks the project's standing, **When** they reach the disclaimer,
   **Then** it states the library is not affiliated with or endorsed by Mailgun or Sinch.

---

### User Story 3 - Discover and trust the package, and see the first release recorded (Priority: P3)

A developer browsing a package registry finds Mailgunner via relevant search terms (including
"mailgun"), sees the README rendered on the package page, and finds a changelog entry that
records the upcoming first release.

**Why this priority**: Discoverability and a recorded first release reduce adoption risk and
signal that the project is maintained, but they matter after a developer has already found and
evaluated the library. Independently valuable for registry browsers.

**Independent Test**: The package presentation carries the README and a set of discovery tags
that includes "mailgun"; the changelog contains a dated, versioned entry for the first
release rather than only an open "Unreleased" section.

**Acceptance Scenarios**:

1. **Given** a developer searches a package registry for "mailgun", **When** Mailgunner
   appears, **Then** its listing carries the README content and discovery tags including
   "mailgun".
2. **Given** a developer opens the changelog, **When** they look for release history, **Then**
   they find an entry recording the upcoming first release and the features it ships.

---

### Edge Cases

- **Missing or partial credentials**: the sample must not silently no-op or leak a partial
  configuration error; it must name what is missing and where to supply it.
- **Wrong region for the domain**: a developer who selects a region that does not match their
  sandbox domain's region will see the send fail at the service; the README regions section
  must pre-empt this so the developer can self-correct.
- **Secrets in source**: no credential may be hard-coded in the sample, the README snippets,
  or any committed configuration; credentials come only from configuration/environment.
- **No-credentials build/CI run**: building the repository and running the default test suite
  must remain successful with no Mailgun credentials present; the sample's live send is opt-in
  and never part of the default green build.
- **README drift**: the quickstart and sample should demonstrate the same scenario so a reader
  who copies one and runs the other is not surprised.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The project MUST include a minimal, runnable sample that registers the client
  and performs a personalized batch send modeling a conference invitation, with per-recipient
  name, ticket number, and personal link, to a few distinct addresses.
- **FR-002**: The sample MUST obtain all Mailgun credentials and the sending domain/region
  from configuration or environment; it MUST NOT contain hard-coded secrets and MUST be
  designed for a Mailgun **sandbox** domain.
- **FR-003**: When required credentials are absent, the sample MUST NOT attempt a send and
  MUST report clearly which settings are missing and where to provide them.
- **FR-004**: The sample MUST build/compile as part of the repository without requiring any
  Mailgun credentials, and its live send MUST be opt-in (driven solely by supplied
  configuration) so the default build and test run stay successful without credentials.
- **FR-005**: The README MUST contain a single copy-paste quickstart that takes a reader from
  client registration to a personalized batch send for the conference-invitation scenario.
- **FR-006**: The README MUST contain a regions section explaining that the region selects the
  API host (US/EU) and that the region must match the sending domain's region.
- **FR-007**: The README MUST contain a suppression/unsubscribe section explaining how
  unsubscribes and the other suppression lists are accessed and used.
- **FR-008**: The README MUST contain a disclaimer stating, verbatim in substance, that the
  library is not affiliated with or endorsed by Mailgun or Sinch.
- **FR-009**: The published package metadata MUST carry the README so it renders on the
  package page, and MUST include discovery tags that include "mailgun".
- **FR-010**: The changelog MUST record the upcoming first release as a dated, versioned entry
  for version **`0.1.0`**, enumerating the capabilities it ships, promoting the open
  "Unreleased" section into that release per the project's Keep a Changelog format (including
  the corresponding version-link reference).
- **FR-011**: The quickstart and the runnable sample MUST demonstrate the same
  conference-invitation scenario so they remain consistent with each other.
- **FR-012**: The runnable sample MUST be a standalone executable artifact (the thing a
  developer runs directly) and MUST serve as the project's single environment-gated live
  check; no separate live-send integration test is added. When sandbox credentials are
  absent, the sample MUST be skipped/short-circuited cleanly (per FR-003), and the default
  build and offline test suite MUST stay green without it performing any send.

### Key Entities *(include if data involved)*

- **Sample run configuration**: the externally supplied settings needed to run the sample —
  sandbox domain, sending key, region, and the sender/recipient details — sourced only from
  configuration/environment.
- **Conference invitation recipient**: a single addressee in the batch, characterized by an
  email address plus their personal data — display name, ticket number, and personal link.
- **README quickstart**: the canonical copy-paste orientation content (registration →
  personalized send) plus the regions, suppression/unsubscribe, and disclaimer sections.
- **Changelog first-release entry**: the dated, versioned record of the initial release and
  the features it includes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer with valid sandbox credentials can go from opening the repository to
  a confirmed personalized batch send using the sample in under 5 minutes, supplying
  credentials through configuration only (no source edits).
- **SC-002**: Running the sample without credentials produces a clear, actionable message
  naming the missing settings 100% of the time, and never performs a partial or silent send.
- **SC-003**: A first-time reader can find, in the README, the quickstart, a regions
  explanation, a suppression/unsubscribe explanation, and the non-affiliation disclaimer —
  all four present and each answerable without leaving the README.
- **SC-004**: The personalized send demonstrably differs per recipient: each of the few
  recipients receives their own name, ticket number, and personal link (verifiable from the
  sent messages or test-mode output).
- **SC-005**: The package listing shows the README and a discovery tag set that includes
  "mailgun", and the changelog contains a dated, versioned first-release entry.
- **SC-006**: The repository builds and its default test suite passes with no Mailgun
  credentials present (the sample's live send is excluded from the default green build).

## Assumptions

- The "first release" is version **`0.1.0`** (see Clarifications / FR-010), consistent with the
  current `VersionPrefix` and the pre-1.0 foundation versioning, recorded in Keep a Changelog /
  SemVer format.
- "A few addresses" means a small, illustrative recipient count (on the order of 2–3) chosen
  to demonstrate per-recipient personalization without resembling a real bulk campaign.
- The sample targets a Mailgun **sandbox** domain and *is* the project's single permitted,
  environment-gated live check (see Clarifications / FR-012); it is not intended for
  production sending, and no additional live-send integration test is introduced.
- Several README elements (regions, suppression, disclaimer, and the "mailgun" package tag)
  already exist from prior features; this feature ensures all four are present, consistent,
  and aligned with the new conference-invitation quickstart, and adds the runnable sample and
  first-release changelog entry as the net-new artifacts.
- The conference-invitation scenario's per-recipient fields (name, ticket number, personal
  link) are conveyed via the library's existing personalization mechanism for batch sends.
- Credentials are supplied via the platform's standard configuration/environment mechanisms;
  no new secret-storage system is introduced.
