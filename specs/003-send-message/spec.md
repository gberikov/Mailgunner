# Feature Specification: Send a Single Email

**Feature Branch**: `003-send-message`

**Created**: 2026-06-22

**Status**: Draft

**Input**: User description: "A consumer sends a single email through Mailgun: sender, one or more recipients (to/cc/bcc), a subject, and a plain-text and/or HTML body. On success they receive a result with Mailgun's message id and status. On any non-success response they receive a typed error exposing the HTTP status code and raw response body. This is the core everything else extends. External constraints (requirements): the Mailgun messages endpoint accepts only multipart/form-data; multiple recipients are expressed as repeated \"to\" fields, not a comma-joined string. Acceptance criteria: Several recipients produce several distinct recipient fields. A success response is parsed into id + message. A 4xx/5xx response raises the typed error carrying status and body. Cancellation is honored. Verified entirely against a fake transport."

## Clarifications

### Session 2026-06-22

- Q: How should email addresses (sender and recipients) be represented in the message? → A: A dedicated address value type (an email address plus an optional display name) that the library formats into the wire value.
- Q: What should happen on a 2xx response whose body cannot be parsed into an id + message? → A: Raise the typed `MailgunnerException` (carrying the status code and the raw body), the same single error path as a non-success response.
- Q: How should client-side validation failures (missing sender, no recipient, no body) surface? → A: A standard `ArgumentException`, thrown before any request; `MailgunnerException` stays reserved for actual HTTP responses.

## User Scenarios & Testing *(mandatory)*

<!--
  The "user" of this feature is the .NET application developer who has already registered the
  Mailgunner client (feature 002) and now wants to send an email through it. Stories are ordered
  by importance; each is independently testable offline.
-->

### User Story 1 - Send a single email and get a success result (Priority: P1)

A developer composes a message — a sender, a recipient, a subject, and a body (plain text and/or
HTML) — hands it to the client, and receives a success result containing Mailgun's message id and
its accompanying status message.

**Why this priority**: This is the core capability of the whole library — "send an email and know
it was accepted." Every later capability (multiple recipients, templates, batches, attachments,
options) extends this single send. On its own it delivers the product's primary value.

**Independent Test**: Hand the client a message with one recipient and a body, run it against a
fake transport returning a success payload, and confirm the returned result exposes the id and
message — verifiable entirely offline.

**Acceptance Scenarios**:

1. **Given** a message with a sender, one recipient, a subject, and a text body, **When** it is sent and the service returns a success response carrying an id and a message, **Then** the caller receives a result exposing that id and that message.
2. **Given** a message whose body is provided as HTML (with or without a text part), **When** it is sent, **Then** the request carries the HTML body and the success result is returned the same way.
3. **Given** any send, **When** the request is issued, **Then** it targets the messages endpoint of the registered domain using `multipart/form-data` content, and no real network call is made (a fake transport stands in).

---

### User Story 2 - Address multiple recipients across to/cc/bcc (Priority: P2)

A developer sends one email to several recipients — some on the "to" line, optionally some carbon-
copied (cc) and blind-carbon-copied (bcc) — and trusts that each recipient is represented as its
own distinct field rather than being merged into a single comma-joined value.

**Why this priority**: Multi-recipient delivery is the common real-world case and is governed by an
explicit external constraint (repeated fields, not comma-joining) that, if violated, silently
breaks delivery. It builds directly on US1.

**Independent Test**: Send a message with three "to" recipients (and optionally cc/bcc), capture
the outgoing request via a fake transport, and confirm there are three distinct "to" fields (and
the corresponding cc/bcc fields) — not one combined field.

**Acceptance Scenarios**:

1. **Given** a message with three "to" recipients, **When** it is sent, **Then** the outgoing request contains three distinct "to" fields, one per recipient, and none of them is a comma-joined list.
2. **Given** a message that also has cc and bcc recipients, **When** it is sent, **Then** each cc and each bcc recipient likewise appears as its own distinct field.
3. **Given** recipients on multiple lines, **When** the request is captured, **Then** the count of recipient fields equals the total number of distinct recipients supplied.

---

### User Story 3 - Non-success responses raise a typed error (Priority: P2)

A developer whose send is rejected by the service (for example, a 400 for a malformed request or a
500 for a server fault) receives a single, predictable typed error that exposes the HTTP status
code and the raw response body, so they can log and react to it without guessing.

**Why this priority**: Reliable, uniform error reporting is essential for any production integration
and is a core promise of the library (one typed error for all API failures). It pairs with US1 to
make the send trustworthy.

**Independent Test**: Run a send against a fake transport returning a 4xx and, separately, a 5xx
with a known body; confirm a single typed error is raised that exposes the exact status code and
the exact raw body — offline.

**Acceptance Scenarios**:

1. **Given** the service returns a 4xx response with a body, **When** the send is attempted, **Then** the caller receives the library's typed error exposing that status code and that raw body.
2. **Given** the service returns a 5xx response with a body, **When** the send is attempted, **Then** the same typed error type is raised, exposing the 5xx status code and raw body.
3. **Given** any non-success response, **When** the error is raised, **Then** it is the one Mailgunner error type (not a generic transport exception and not multiple bespoke types), and a success result is never returned.

---

### User Story 4 - Cancellation is honored (Priority: P3)

A developer who passes a cancellation signal (for example, because the user navigated away or a
request timeout elapsed) has the in-flight send stop promptly rather than running to completion.

**Why this priority**: Cooperative cancellation is expected of every asynchronous .NET operation and
prevents wasted work, but it is a refinement of the core send rather than the core value itself.

**Independent Test**: Begin a send with an already-canceled (or promptly-canceled) signal against a
fake transport and confirm the operation reports cancellation rather than returning a success
result.

**Acceptance Scenarios**:

1. **Given** a send is started with an already-canceled signal, **When** it runs, **Then** it reports cancellation and does not return a success result.
2. **Given** a send is started with a signal that is canceled while the request is in flight, **When** the cancellation occurs, **Then** the operation stops cooperatively and surfaces the cancellation.

---

### Edge Cases

- **No body at all**: A message with neither a text part nor an HTML part is invalid; the message must carry at least one body part.
- **No sender or no recipient**: A message without a sender, or without at least one recipient, cannot be sent and is rejected before any request is issued.
- **Single recipient**: One recipient still produces exactly one recipient field (the repeated-field rule degenerates correctly to a single field).
- **Both body parts**: When both text and HTML are supplied, both are included in the request.
- **Empty/whitespace recipient entries**: Blank recipient values are not turned into empty recipient fields.
- **Non-success with an empty body**: A non-success response that has no body still raises the typed error, exposing the status code and an empty (not null-crashing) body.
- **Secret hygiene**: Nothing in the result or the error exposes the sending key.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let a consumer send a single email described by a sender, one or more recipients (across "to", "cc", and "bcc"), an optional subject, and a body consisting of a plain-text part and/or an HTML part.
- **FR-002**: The system MUST require at least one body part (text or HTML) and at least one recipient and a sender; a message missing any of these MUST be rejected before a request is issued, by throwing a standard `ArgumentException` (the typed `MailgunnerException` is reserved for actual HTTP responses).
- **FR-003**: The system MUST send the request to the registered domain's messages endpoint using `multipart/form-data` content (the only content type the endpoint accepts).
- **FR-004**: The system MUST express multiple recipients as repeated, distinct fields (one field per recipient for each of "to", "cc", and "bcc") and MUST NOT combine recipients into a single comma-joined value.
- **FR-005**: On a success (2xx) response, the system MUST return a result that exposes Mailgun's message id and its accompanying status message.
- **FR-006**: On any non-success response (4xx or 5xx), the system MUST raise exactly one typed error (`MailgunnerException`) that exposes the HTTP status code and the raw response body, and MUST NOT return a success result.
- **FR-006a**: If a success (2xx) response body cannot be parsed into an id and message, the system MUST raise the same typed `MailgunnerException` (exposing the status code and the raw body) rather than returning a partial or empty result.
- **FR-007**: The system MUST surface all Mailgun API send failures through that single typed error type, not through multiple bespoke exception types or an unwrapped transport exception.
- **FR-008**: The send operation MUST accept a cancellation signal and honor it cooperatively, stopping an in-flight send when cancellation is requested.
- **FR-009**: The behavior MUST be verifiable without any real network call, by substituting a fake transport that captures the outgoing request and supplies a chosen response.
- **FR-010**: Neither the success result nor the typed error MUST expose the sending key.
- **FR-011**: A non-success response that has no body MUST still raise the typed error with the status code and a non-null (possibly empty) body.

### Key Entities *(include if data involved)*

- **Email address**: A dedicated value type representing one address — the email address plus an optional display name — which the library formats into the wire value (e.g., `Name <email@x.com>`). Used for the sender and every recipient.
- **Outgoing message**: The email a consumer wants to send — its sender (an email address), its recipients (email addresses grouped into "to", "cc", and "bcc"), an optional subject, and its body (a text part and/or an HTML part).
- **Send result**: The success outcome — Mailgun's message id plus the accompanying status message.
- **Send error**: The single typed failure (`MailgunnerException`) raised on any non-success response, exposing the HTTP status code and the raw response body.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of sends that receive a 2xx response return a result exposing both the message id and the status message.
- **SC-002**: For a message with N distinct recipients (across to/cc/bcc), the outgoing request contains exactly N distinct recipient fields — never a single comma-joined field — for every N ≥ 1 tested.
- **SC-003**: 100% of non-success (4xx/5xx) responses raise the one typed error, exposing the exact HTTP status code and the exact raw response body received.
- **SC-004**: A send started with a canceled signal reports cancellation in 100% of cases and never returns a success result.
- **SC-005**: 100% of the feature's behavior is exercised by automated tests that make no real network call (a fake transport stands in).
- **SC-006**: Every outgoing send request uses `multipart/form-data` content.
- **SC-007**: The sending key never appears in a returned result or a raised error.

## Assumptions

- This feature builds on the registered client from feature 002 (client registration & regional bootstrap): the domain, region routing, and HTTP Basic authentication are already configured and are reused as-is.
- The "status" returned on success refers to Mailgun's accompanying status message (e.g., a "Queued" acknowledgment) paired with the message id; it is not a separate delivery-state lookup.
- The subject is optional (an empty or absent subject is permitted by the service); the sender, at least one recipient, and at least one body part are the minimum required to send.
- "At least one recipient" means at least one address across the "to", "cc", and "bcc" groups; callers are expected to supply a "to" recipient for normal delivery.
- This feature covers the core single-message send only. Out of scope (separate features): stored templates and per-recipient variables, batch sending with auto-chunking at the recipient limit, attachments and inline files, sending options (tags, test mode, open/click tracking, scheduled delivery), custom headers and custom variables, suppressions, and webhooks.
- Input validation failures (missing sender/recipient/body) are surfaced as standard argument errors before a request is issued, consistent with how configuration is validated at registration; the typed `MailgunnerException` is reserved for non-success HTTP responses from the service.
- Transient-fault resilience (retry/backoff policy) is governed by the shared HTTP pipeline and is not specified or re-verified by this feature.
