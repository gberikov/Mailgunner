# Feature Specification: Send a Templated Email

**Feature Branch**: `004-template-message`

**Created**: 2026-06-22

**Status**: Draft

**Input**: User description: "A consumer sends an email rendered from a template stored server-side in Mailgun, referencing it by name, optionally pinning a version, optionally requesting a generated plain-text part, and supplying global variables that apply to the whole send. Storing the template server-side means shipping only the template name plus a small set of variables instead of a fully rendered body — the efficiency goal motivating this library. Acceptance criteria: The request references the template by name and, when provided, the version. Global variables are sent as a single JSON-encoded structure in the documented template-variables field. Requesting a generated text part sets the documented flag. Plain sending from the previous phase still works unchanged. Verified against a fake transport, asserting the variables payload is valid JSON of the expected shape."

## Clarifications

### Session 2026-06-22

- Q: May a caller supply both a stored template name and inline body parts (text/HTML) on the same message? → A: No — reject before any request with a standard `ArgumentException`; a message carries a template **or** inline body parts, never both.
- Q: What value types may global template variables hold? → A: Arbitrary JSON-representable values (string, number, boolean, array, nested object) supplied as a map of name → value; the library serializes them once into a single JSON object.
- Q: How should an explicitly empty variables map be handled on the wire? → A: Omit the template-variables field entirely — the same as supplying no variables; no empty `{}` is emitted.

## User Scenarios & Testing *(mandatory)*

<!--
  The "user" of this feature is the .NET application developer who has already registered the
  Mailgunner client (feature 002) and can already send a plain single email (feature 003). They
  now want to render that email from a template that lives server-side in Mailgun, shipping only
  the template name plus a small set of variables instead of a fully composed body. Stories are
  ordered by importance; each is independently testable offline against a fake transport.
-->

### User Story 1 - Send an email from a stored template with global variables (Priority: P1)

A developer has a template already stored in Mailgun (for example, a "welcome" email). Instead of
composing and shipping a fully rendered body, they hand the client a message that names that
template and supplies a small set of global variables that apply to the whole send (for example, a
product name and a support URL). The service renders the body from the named template and those
variables, and the developer receives the same success result (message id and status) as a plain
send.

**Why this priority**: This is the core value of the feature and the efficiency goal that motivates
the library — shipping a template name plus a few variables instead of a fully rendered body. Every
other capability in this phase (version pinning, generated text part) is a refinement of this one
templated send. On its own it delivers the feature's primary value.

**Independent Test**: Hand the client a message that names a template and supplies a set of global
variables, run it against a fake transport returning a success payload, capture the outgoing
request, and confirm it carries the template name and a single template-variables field whose value
is valid JSON of the expected shape — verifiable entirely offline.

**Acceptance Scenarios**:

1. **Given** a message that names a stored template, a sender, and one recipient, **When** it is sent and the service returns a success response carrying an id and a message, **Then** the outgoing request carries the template name and the caller receives a result exposing that id and message.
2. **Given** a templated message that also supplies a set of global variables, **When** it is sent, **Then** the request carries exactly one template-variables field whose value is a single JSON-encoded structure containing those variables (not multiple fields and not one field per variable).
3. **Given** a templated message with no global variables supplied, **When** it is sent, **Then** no template-variables field is emitted and the template name is still carried.
4. **Given** any templated send, **When** the request is issued, **Then** it targets the messages endpoint of the registered domain using `multipart/form-data` content, and no real network call is made (a fake transport stands in).

---

### User Story 2 - Pin a specific template version (Priority: P2)

A developer who maintains multiple versions of a stored template (for example, while rolling out a
redesign) optionally pins the exact version to render, so the send is reproducible and unaffected by
later edits to the template's active version.

**Why this priority**: Version pinning is a common, valuable safeguard for teams iterating on
templates, but it is optional and builds directly on US1 — a templated send works without it.

**Independent Test**: Send a templated message with a pinned version against a fake transport,
capture the request, and confirm it carries the version field with the supplied value; send another
without a version and confirm no version field is present — offline.

**Acceptance Scenarios**:

1. **Given** a templated message that pins a version, **When** it is sent, **Then** the outgoing request carries the documented template-version field set to the supplied version.
2. **Given** a templated message that does not pin a version, **When** it is sent, **Then** no template-version field is emitted and the service's active version is used by default.

---

### User Story 3 - Request a generated plain-text part (Priority: P3)

A developer sending an HTML template optionally asks the service to generate a plain-text part from
the template, so recipients on plain-text clients still receive a readable message without the
developer authoring a separate text body.

**Why this priority**: A generated text part improves deliverability and accessibility, but it is an
optional refinement of the core templated send and is the least critical of the three capabilities.

**Independent Test**: Send a templated message that requests a generated text part against a fake
transport, capture the request, and confirm the documented text flag is set to the documented value;
send another without the request and confirm no such flag is present — offline.

**Acceptance Scenarios**:

1. **Given** a templated message that requests a generated plain-text part, **When** it is sent, **Then** the outgoing request carries the documented template-text flag set to its documented "on" value.
2. **Given** a templated message that does not request a generated text part, **When** it is sent, **Then** no template-text flag is emitted.

---

### User Story 4 - Plain (non-templated) sending still works unchanged (Priority: P2)

A developer who already sends plain emails (feature 003) continues to do so with no change in
behavior after templated sending is added. A plain send carries its body parts and no template
fields; a templated send carries the template fields and needs no inline body.

**Why this priority**: The templated path is additive and must not regress the existing core send.
This coexistence guarantee protects every consumer already relying on plain sends and is therefore
ranked above the optional refinements.

**Independent Test**: Run the existing plain-send scenarios against a fake transport and confirm they
still produce the same outgoing request (body parts, no template fields) and the same success/error
behavior as before; run a templated send and confirm it produces template fields and no inline body
requirement — offline.

**Acceptance Scenarios**:

1. **Given** a plain message with body parts and no template, **When** it is sent, **Then** the outgoing request carries the body parts and carries none of the template, template-version, template-text, or template-variables fields.
2. **Given** a templated message, **When** it is sent, **Then** the request carries the template fields and is accepted without an inline body part being required.
3. **Given** the existing plain-send acceptance scenarios from the previous phase, **When** they are re-run, **Then** they pass unchanged.

---

### Edge Cases

- **Missing template name on a templated send**: A message that supplies template variables, a version, or the generated-text request but no template name is invalid and is rejected before any request is issued.
- **No template and no body**: A message with neither a template name nor any body part cannot be sent and is rejected before any request is issued (consistent with the previous phase's "at least one body part" rule, now satisfied by either a body part or a template).
- **Both template and inline body**: A message that supplies both a template name and inline body parts is invalid and is rejected before any request is issued; a message is either templated or inline, never both.
- **Empty variables structure**: An explicitly empty set of global variables is treated the same as supplying none — the template-variables field is omitted entirely (no empty `{}` is emitted, and never invalid or non-JSON content).
- **Variable values of mixed types**: Global variable values that are not plain strings (numbers, booleans, nested structures) are still encoded into the single JSON structure and remain valid JSON.
- **Version supplied but empty/whitespace**: A blank version value is treated as "no version pinned" rather than emitting an empty version field.
- **Generated-text request not made**: When the generated-text part is not requested, the flag is omitted entirely rather than emitted with an "off" value.
- **Sender / recipient still required**: A templated message, like a plain one, still requires a sender and at least one recipient and is rejected before any request otherwise.
- **Non-success and cancellation behavior**: A templated send surfaces non-success responses through the same single typed error and honors cancellation exactly as a plain send does (no new error path is introduced).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let a consumer send an email rendered from a server-side stored template by referencing the template by name, in place of supplying an inline body.
- **FR-002**: A templated send MUST be valid when it supplies a sender, at least one recipient, and a template name; an inline body part MUST NOT be required for a templated message.
- **FR-003**: The system MUST treat "at least one body part OR a template name" as the body requirement: a message with neither a body part nor a template name MUST be rejected before any request is issued (by throwing a standard `ArgumentException`, consistent with the previous phase's validation).
- **FR-003a**: A message MUST be either templated or inline, never both: a message that supplies both a template name and one or more inline body parts (text and/or HTML) MUST be rejected before any request is issued by throwing a standard `ArgumentException`.
- **FR-004**: When global variables are supplied, the system MUST send them as a single JSON-encoded structure in the documented template-variables field — exactly one field carrying one JSON value, never one field per variable and never repeated fields.
- **FR-005**: The JSON-encoded global variables MUST be valid JSON of the expected shape (a single JSON object keyed by variable name), and MUST preserve non-string values (numbers, booleans, nested structures) as their JSON equivalents.
- **FR-005a**: The system MUST accept global variables as a map of variable name → arbitrary JSON-representable value (string, number, boolean, array, or nested object); callers MUST NOT be required to pre-serialize values to strings, and the library MUST serialize the whole map once into the single template-variables JSON object.
- **FR-006**: When no global variables are supplied, or the supplied variables map is empty, the system MUST omit the template-variables field entirely (the two cases are treated identically); when variables are present it MUST emit valid JSON and MUST NOT emit invalid or non-JSON content.
- **FR-007**: When a template version is supplied, the system MUST include the documented template-version field set to that value; when no version is supplied (or it is blank), the system MUST omit the field so the service's active version is used.
- **FR-008**: When a generated plain-text part is requested, the system MUST set the documented template-text flag to its documented "on" value; when it is not requested, the system MUST omit the flag.
- **FR-009**: A templated send MUST otherwise behave identically to a plain send: it MUST use the registered domain's messages endpoint with `multipart/form-data` content, express multiple recipients as repeated distinct fields, return the same success result (id and status message), surface non-success responses through the same single typed `MailgunnerException` (exposing status code and raw body), and honor cancellation.
- **FR-010**: Plain (non-templated) sending from the previous phase MUST continue to work unchanged: a plain message MUST carry its body parts and none of the template, template-version, template-text, or template-variables fields, and its existing success and error behavior MUST be preserved.
- **FR-011**: The behavior MUST be verifiable without any real network call, by substituting a fake transport that captures the outgoing request and supplies a chosen response; the captured template-variables payload MUST be assertable as valid JSON of the expected shape.
- **FR-012**: Neither the success result nor the typed error MUST expose the sending key, consistent with the previous phase.

### Key Entities *(include if feature involves data)*

- **Template reference**: The identification of a server-side stored template for a send — the template name (required for a templated send) and an optional pinned version. When no version is given, the service's active version is used.
- **Global template variables**: A map of name → value pairs that apply to the whole send (not per recipient), serialized once into a single JSON structure carried in the template-variables field. Values may be arbitrary JSON-representable types (string, number, boolean, array, or nested object); callers do not pre-serialize values.
- **Generated-text request**: An optional flag asking the service to generate a plain-text part from the template; when set, the documented template-text flag is emitted at its "on" value, otherwise it is omitted.
- **Outgoing message (extended)**: The feature-003 outgoing message, now able to carry a template reference, global template variables, and a generated-text request in place of (or alongside) its body parts, while keeping its sender, recipients, and subject unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of templated sends that receive a 2xx response return a result exposing both the message id and the status message — the same result shape as a plain send.
- **SC-002**: For every templated send that supplies global variables, the outgoing request contains exactly one template-variables field whose value parses as valid JSON of the expected shape (a single object keyed by variable name).
- **SC-003**: When a version is supplied, 100% of outgoing requests carry the template-version field with the exact supplied value; when none is supplied, 0% of requests carry that field.
- **SC-004**: When a generated text part is requested, 100% of outgoing requests carry the template-text flag at its documented "on" value; when it is not requested, 0% of requests carry that flag.
- **SC-005**: 100% of the previous phase's plain-send acceptance scenarios pass unchanged after this feature is added, and a plain send carries none of the four template fields.
- **SC-006**: A templated send with neither a body part nor a template name, or with template data but no template name, is rejected before any request is issued in 100% of cases.
- **SC-007**: 100% of the feature's behavior is exercised by automated tests that make no real network call (a fake transport stands in).
- **SC-008**: The sending key never appears in a returned result or a raised error for any templated send.

## Assumptions

- This feature builds on feature 002 (client registration & regional bootstrap) and feature 003 (send a single email): the registered client, domain, region routing, HTTP Basic authentication, multipart construction, repeated-recipient fields, success-result parsing, the single typed `MailgunnerException`, and cancellation are all reused as-is.
- "Global variables that apply to the whole send" map to Mailgun's documented template-variables field (`t:variables`), carried once as a single JSON object — distinct from per-recipient personalization (`recipient-variables`), which is a separate, later feature (batch sending) and is out of scope here.
- The "expected shape" of the variables payload is a single JSON object keyed by variable name; values may be any JSON-representable type (string, number, boolean, array, or nested object). The library accepts a name → value map and serializes it once into this structure.
- The template-version and generated-text capabilities map to Mailgun's documented `t:version` and `t:text` fields respectively; the generated-text flag's documented "on" value is the documented affirmative ("yes").
- A templated message satisfies the body requirement by virtue of naming a template; an inline text/HTML body is not required for a templated send. A message is either templated or inline, never both: supplying both a template name and inline body parts is rejected before any request (see FR-003a). The minimum valid templated message is sender + recipient(s) + template name.
- Subject remains optional, as in the previous phase. Sender and at least one recipient remain required for any send, templated or plain.
- Validation failures (missing template name when template data is present, or neither body nor template) surface as standard argument errors before a request is issued, consistent with feature 003; the typed `MailgunnerException` stays reserved for non-success HTTP responses.
- Out of scope for this feature (separate, later features): per-recipient `recipient-variables` and batch sending with auto-chunking, attachments and inline files, sending options (tags, test mode, tracking, scheduled delivery), custom headers, and custom variables.
