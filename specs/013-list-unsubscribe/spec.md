# Feature Specification: One-Click List-Unsubscribe (RFC 8058)

**Feature Branch**: `013-list-unsubscribe`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "Add first-class, correct support for one-click List-Unsubscribe on a send, so marketing mail meets the Gmail/Yahoo bulk-sender requirement (RFC 8058: List-Unsubscribe + List-Unsubscribe-Post headers) without the consumer hand-assembling raw headers and getting the format subtly wrong."

## Clarifications

### Session 2026-06-24

- Q: For the FR-009 duplicate guard, by what casing must the library detect a manually set
  `List-Unsubscribe` / `List-Unsubscribe-Post` header in `CustomHeaders`? → A: Case-insensitive
  (ordinal-ignore-case) — HTTP header names are case-insensitive, so a manual header in any casing must
  be detected to prevent a real on-the-wire duplicate.
- Q: In what form does the consumer supply the mailto unsubscribe target, and how is it validated? → A:
  A bare email address (e.g. `unsub@example.com`); the library forms the `mailto:` URI itself and
  validates the address with the existing `EmailAddress` rules. Full `mailto:` URIs with `subject`/`body`
  query parameters are out of scope for v1.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One-click unsubscribe on a marketing blast (Priority: P1)

A consumer is sending a marketing campaign (single, templated, or batched) to a large audience that
will cross the bulk-sender threshold. They want the message to satisfy the Gmail/Yahoo one-click
unsubscribe requirement. They declare, in one typed place on the send options, an `https` unsubscribe
endpoint and flag it as one-click. The library emits a correctly formatted `List-Unsubscribe` header
pointing at that endpoint plus the `List-Unsubscribe-Post: List-Unsubscribe=One-Click` header, with no
hand-assembly of raw header strings by the consumer.

**Why this priority**: This is the core reason the feature exists — meeting the bulk-sender mandate
without the consumer getting the two coordinated headers or their exact tokens subtly wrong. It is the
minimum viable slice: implementing only this story already delivers compliant one-click mail.

**Independent Test**: Configure a send with a one-click `https` unsubscribe target, build the request
offline, and assert the exact emitted `List-Unsubscribe` and `List-Unsubscribe-Post` header values.

**Acceptance Scenarios**:

1. **Given** send options with an `https` unsubscribe URL flagged one-click, **When** the request is
   built, **Then** the body carries `List-Unsubscribe: <https://…>` and
   `List-Unsubscribe-Post: List-Unsubscribe=One-Click`, each exactly once.
2. **Given** the same one-click options applied to a batch send, **When** each chunk is built, **Then**
   every chunk carries the identical pair of headers.
3. **Given** one-click is requested but no `https` URL is supplied (e.g. only a mailto), **When** the
   request is built, **Then** the send is rejected with a clear validation error and no request is
   issued.

---

### User Story 2 - Declare a non-one-click unsubscribe target (Priority: P2)

A consumer wants to add a standard (two-step) `List-Unsubscribe` header without one-click POST — using
a `mailto:` target only, an `https` URL only, or both. The library emits a single correctly formatted
`List-Unsubscribe` header listing the supplied target(s), and does **not** emit `List-Unsubscribe-Post`.

**Why this priority**: Covers the mailto-only, url-only, and both combinations that senders use for the
classic List-Unsubscribe convention, and keeps the feature useful outside the strict one-click case.

**Independent Test**: Configure each of {mailto-only, url-only, both} without the one-click flag, build
offline, and assert the single `List-Unsubscribe` value and the absence of `List-Unsubscribe-Post`.

**Acceptance Scenarios**:

1. **Given** a mailto-only unsubscribe target, **When** the request is built, **Then** the body carries
   `List-Unsubscribe: <mailto:…>` and no `List-Unsubscribe-Post`.
2. **Given** an `https` URL-only target (not one-click), **When** the request is built, **Then** the
   body carries `List-Unsubscribe: <https://…>` and no `List-Unsubscribe-Post`.
3. **Given** both a URL and a mailto target, **When** the request is built, **Then** the body carries a
   single `List-Unsubscribe` header listing both in angle brackets, comma-separated, in a deterministic
   order.

---

### User Story 3 - Safe, validated, opt-in behavior (Priority: P3)

A consumer relies on the feature being safe by construction: invalid inputs are rejected before any
network call, the feature is off unless explicitly set (so transactional mail is unaffected), and the
library never produces a duplicate `List-Unsubscribe` header if the consumer already set one manually.

**Why this priority**: Protects existing transactional senders from behavior change and guarantees the
emitted headers cannot be malformed or injected — but it layers onto the value delivered by P1/P2.

**Independent Test**: Build sends with each invalid input (non-`https` URL, control characters/line
breaks, one-click without URL) and assert a validation error with no request; build a send with the
feature unset and assert no unsubscribe headers appear.

**Acceptance Scenarios**:

1. **Given** an unsubscribe URL whose scheme is not `https` (e.g. `http`), **When** the request is
   built, **Then** the send is rejected with a validation error and no request is issued.
2. **Given** an unsubscribe URL or mailto containing a control character or line break, **When** the
   request is built, **Then** the send is rejected with a validation error and no request is issued.
3. **Given** send options with the unsubscribe target unset, **When** the request is built, **Then** no
   `List-Unsubscribe` or `List-Unsubscribe-Post` header is emitted.

---

### Edge Cases

- **One-click without an https endpoint**: one-click requires an `https` POST endpoint; requesting
  one-click with only a mailto (or no URL at all) is a rejected configuration, not a silent downgrade.
- **Manual header already present**: the consumer set `List-Unsubscribe` (and/or `List-Unsubscribe-Post`)
  through the existing custom-headers mechanism *and* also set the typed unsubscribe target — this is a
  conflicting configuration and the send is rejected with a validation error before any request (FR-009);
  the library never emits a duplicate header.
- **Empty/whitespace target**: a blank URL or blank mailto that is technically "set" is treated as not a
  valid target and rejected (consistent with existing address/header validation).
- **Both targets supplied with one-click**: the one-click POST applies to the `https` URL; the mailto is
  still listed in `List-Unsubscribe` for clients that prefer it.
- **Combined parameter size**: the emitted headers count toward Mailgun's combined 16KB `o:`/`h:`/`v:`/`t:`
  cap; the library does not enforce that cap (the service rejects oversize requests, surfaced normally).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The send options MUST expose a typed, opt-in way to declare an unsubscribe target that is
  unset by default; when unset, the feature emits nothing and existing sends are unaffected.
- **FR-002**: The unsubscribe target MUST support three shapes: an `https` URL only, a `mailto` address
  only, or both together. The mailto form MUST be supplied as a bare email address; the library forms
  the `mailto:` URI itself and validates the address using the existing `EmailAddress` rules. Full
  `mailto:` URIs with `subject`/`body` query parameters are out of scope for v1.
- **FR-003**: When a target is set, the library MUST emit exactly one `List-Unsubscribe` header whose
  value lists each supplied target enclosed in angle brackets, multiple targets separated by a comma,
  formatted per RFC 8058 / RFC 2369.
- **FR-004**: When both a URL and a mailto are supplied, the two MUST be listed in a single
  `List-Unsubscribe` header in a deterministic, documented order so tests can assert the exact value.
- **FR-005**: The target MAY be flagged one-click; when flagged, the library MUST additionally emit
  exactly one `List-Unsubscribe-Post: List-Unsubscribe=One-Click` header.
- **FR-006**: One-click MUST require an `https` URL; flagging one-click without a valid `https` URL MUST
  be rejected with a clear validation error before any request is issued.
- **FR-007**: The URL form MUST be `https`; a URL with any other scheme (e.g. `http`) MUST be rejected
  with a validation error before any request is issued.
- **FR-008**: The library MUST reject control characters and line breaks anywhere in the URL or mailto
  value, consistent with the existing address/header sanitization, before any request is issued.
- **FR-009**: The library MUST NOT emit a duplicate `List-Unsubscribe` (or `List-Unsubscribe-Post`)
  header. When the consumer has set a typed unsubscribe target **and** also set a `List-Unsubscribe` (or
  `List-Unsubscribe-Post`) header manually through the existing custom-headers mechanism, this is a
  conflicting configuration: the send MUST be rejected with a clear validation error before any request
  is issued (fail-fast), rather than silently preferring one source over the other. The consumer is
  expected to use exactly one mechanism for these headers. The manual-header match MUST be
  case-insensitive (the header name compared ordinal-ignore-case), so a manually set `list-unsubscribe`
  in any casing is detected.
- **FR-010**: Invalid configuration (non-`https` URL, control characters/line breaks, one-click without
  URL) MUST surface as the library's standard input-validation error raised before any network call —
  not as the HTTP-response exception type. No new bespoke exception type is introduced.
- **FR-011**: The behavior MUST apply uniformly to single, templated, and batch sends (the same options
  shape is shared); on a batch the headers are repeated identically on every chunk.
- **FR-012**: The change MUST be purely additive and backward compatible (SemVer MINOR) and MUST be
  recorded with a CHANGELOG entry.
- **FR-013**: The feature MUST be covered by network-free unit tests that assert the exact emitted
  header name(s) and value(s) for every combination (mailto-only, url-only, both; one-click on/off) and
  every rejection path.

### Key Entities *(include if feature involves data)*

- **Unsubscribe target**: An opt-in declaration attached to a send, composed of an optional `https`
  unsubscribe URL, an optional `mailto` unsubscribe address, and a one-click flag. At least one of URL
  or mailto must be present for the target to be valid; one-click validity additionally depends on the
  URL being present and `https`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A one-click marketing send produces both required headers — `List-Unsubscribe` with an
  `https` target and `List-Unsubscribe-Post: List-Unsubscribe=One-Click` — matching the RFC 8058 format
  byte-for-byte, verifiable by an offline test, satisfying the Gmail/Yahoo bulk-sender one-click mandate.
- **SC-002**: 100% of sends that do not set the unsubscribe target emit zero unsubscribe-related
  headers, so existing transactional behavior is unchanged (no regression).
- **SC-003**: 100% of invalid unsubscribe configurations are rejected before any network request, so no
  malformed or injected `List-Unsubscribe`/`List-Unsubscribe-Post` header can ever reach the service.
- **SC-004**: A consumer declares one-click unsubscribe in a single typed assignment instead of two
  hand-coordinated raw headers, eliminating the class of "two-header / exact-token" mistakes the manual
  approach is prone to.

## Assumptions

- The mailto form is supplied as a bare unsubscribe email address (validated with the existing
  `EmailAddress` rules); the library produces the `mailto:` URI form in the header. Arbitrary `mailto:`
  query parameters (subject/body) are out of scope for v1 (confirmed in Clarifications).
- When both URL and mailto are present, a fixed emission order (URL first, then mailto) is chosen for
  deterministic, assertable output; the order is immaterial to receiving mail clients.
- Validation errors use the same standard .NET input-validation exception already used for invalid
  addresses, custom headers, and custom variables — there is no new public exception type, preserving
  the single HTTP-error contract.
- The headers are emitted as custom message headers on the same `multipart/form-data` request used by
  every send; no new Mailgun endpoint or transport is involved, keeping the work inside the messages
  scope.
- Inbound handling of the unsubscribe click/POST and management of the suppression list are explicitly
  out of scope (the former is the consumer's endpoint; the latter is already covered by the existing
  suppressions surface).

## Alternatives Considered

- **Document manual `CustomHeaders` usage instead of adding API**: The consumer can already set
  `List-Unsubscribe` and `List-Unsubscribe-Post` via the existing custom-headers dictionary. This avoids
  any new public surface. It was weighed and is **not** preferred as the primary solution because it
  leaves the error-prone parts on the consumer: coordinating two headers, getting the exact
  `List-Unsubscribe=One-Click` token right, wrapping targets in angle brackets, and enforcing `https`
  for one-click. Those are precisely the "subtly wrong" failure modes the feature exists to remove, and
  a typed, validated target makes the compliant path the easy path. The manual route remains available
  and documented for advanced cases; the typed API is additive on top of it (hence the duplicate-header
  guard in FR-009).
