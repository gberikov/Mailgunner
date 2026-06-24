# Feature Specification: Personalized Mass Send (Batched Recipient Variables)

**Feature Branch**: `005-batch-send`

**Created**: 2026-06-22

**Status**: Draft

**Input**: User description: "A consumer sends one templated email to a large recipient list where each recipient receives their own personalized values (name, ticket number, personal link), in as few API calls as possible, with each recipient seeing only their own address. Personalized mass sending (conference invitations to thousands) is the library's headline capability. External constraints (requirements): Mailgun accepts at most 1000 recipients per request; per-recipient values are delivered as a single JSON map keyed by recipient email address. Acceptance criteria: 2500 recipients result in exactly 3 requests split 1000/1000/500. Exactly 1000 recipients result in a single request. An empty list results in zero requests. The per-recipient payload is a JSON object keyed by email, values being that recipient's variables. Each chunk reuses the same template and global variables. Verified against a fake transport."

## Clarifications

### Session 2026-06-22

- Q: When a batch spans multiple requests and one request is rejected (non-2xx), how should the operation behave? → A: Fail-fast — issue chunks in order; on the first non-2xx, throw the single typed error immediately and issue no further requests; chunks already accepted have been sent and are not rolled back.
- Q: If the same email address appears more than once in the recipient list, how should the library treat it? → A: Reject — throw a standard `ArgumentException` before any request (consistent with feature 003's client-side validation contract).

## User Scenarios & Testing *(mandatory)*

<!--
  The "user" of this feature is the .NET application developer who has already registered the
  Mailgunner client (feature 002) and can send a single email (003) from a stored template with
  global variables (004). This feature lets them send ONE personalized templated message to a
  large recipient list in as few requests as possible. Stories are ordered by importance; each is
  independently testable offline against a fake transport.
-->

### User Story 1 - Send a personalized templated email to thousands in as few requests as possible (Priority: P1)

A developer running a conference has a stored template (the invitation) and a list of thousands of
recipients, each with their own values — name, ticket number, a personal link. They hand the whole
list to the client in one call and the library delivers a personalized message to every recipient,
automatically splitting the work into the fewest possible requests so the send stays within the
service's per-request recipient limit.

**Why this priority**: This is the library's headline capability — "invite thousands, each
personalized, with one call." It is the reason the higher-level template feature exists and the
primary value the library promises. Everything else in this feature supports it.

**Independent Test**: Hand the client a template, a set of global variables, and a recipient list of
several thousand entries each with its own variables; run it against a fake transport; confirm the
recipients are covered by the minimum number of requests (each within the per-request limit) and
that every recipient is included exactly once — verifiable entirely offline.

**Acceptance Scenarios**:

1. **Given** a stored template, a set of global variables, and a list of 2500 recipients each with their own variables, **When** the developer issues a single batch send, **Then** the library performs exactly 3 requests covering 1000, 1000, and 500 recipients respectively, and every recipient appears in exactly one request.
2. **Given** a list of exactly 1000 recipients, **When** the batch send is issued, **Then** the library performs exactly 1 request.
3. **Given** any batch send, **When** the requests are issued, **Then** each one targets the messages endpoint of the registered domain using `multipart/form-data`, reuses the same template reference, and carries the same global variables — and no real network call is made (a fake transport stands in).

---

### User Story 2 - Each recipient is personalized and sees only their own address (Priority: P2)

A developer needs every recipient to receive their own values (their name, their ticket number,
their personal link) and to see only their own address in the delivered message — never the rest of
the list. They supply, per recipient, a small set of named values, and trust the library to deliver
each person their own copy.

**Why this priority**: Personalization and address privacy are what make a mass send acceptable for
real invitations; a send that leaked the recipient list or used the wrong person's values would be
unusable. This rides directly on US1 and is governed by an explicit external constraint (per-
recipient values delivered as one JSON map keyed by email address).

**Independent Test**: Send a batch with three recipients each having distinct variables, capture the
outgoing request via a fake transport, and confirm the per-recipient values are carried as a single
JSON object keyed by each recipient's email address, with each entry holding exactly that
recipient's variables — verifiable offline.

**Acceptance Scenarios**:

1. **Given** a batch where each recipient has their own variables, **When** a request is captured, **Then** the per-recipient values are delivered as a single JSON object whose keys are the recipient email addresses and whose values are that recipient's variables.
2. **Given** a recipient in the list, **When** their delivered message is rendered, **Then** it carries only that recipient's address as the visible recipient, not the addresses of others in the same request.
3. **Given** a recipient whose variables reference values absent from the global variables, **When** the request is built, **Then** the recipient's own values are present in the per-recipient map for that recipient's key (per-recipient values are independent of the shared global variables).

---

### User Story 3 - Boundary behavior is deterministic and predictable (Priority: P3)

A developer relies on the splitting being exact and predictable so they can reason about how many
requests a given list will produce — including the empty-list and exact-multiple cases — without
surprises such as a stray empty request.

**Why this priority**: Predictable, exact chunking is what makes the capability trustworthy at
scale and is the part most likely to harbor off-by-one defects. It refines US1 with the precise
edge behavior.

**Independent Test**: Run batch sends for an empty list, a list of exactly 1000, a list of 2500, and
a list of exactly 2000, each against a fake transport, and confirm the request counts are 0, 1, 3,
and 2 respectively with no empty trailing request.

**Acceptance Scenarios**:

1. **Given** an empty recipient list, **When** a batch send is issued, **Then** the library performs zero requests and reports a successful no-op (no error).
2. **Given** a list whose size is an exact multiple of the per-request limit (e.g. 2000), **When** the batch send is issued, **Then** the library performs exactly that many full requests (e.g. 2) with no trailing empty request.
3. **Given** a list of 2500, **When** the batch send is issued, **Then** the requests cover 1000, 1000, and 500 recipients in order, preserving the supplied recipient order.

---

### Edge Cases

- **Empty list**: zero requests, treated as a successful no-op rather than an error.
- **Exact-multiple list** (e.g. 1000, 2000): no stray trailing empty request.
- **Recipient with no variables**: the recipient is still sent to; their entry in the per-recipient map is an empty value set (they simply receive the template with only global/default values).
- **Duplicate email addresses in the list**: rejected up front with `ArgumentException` before any request (see FR-014).
- **One chunk's request is rejected by the service** (non-2xx): the send stops at the first failing request and surfaces the single typed error; chunks already accepted have already been sent (see Assumptions — partial-failure semantics).
- **Cancellation requested mid-batch**: no further requests are issued once cancellation is observed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The library MUST accept, in a single batch-send call, a template reference, an optional set of global variables shared by all recipients, and a recipient list where each entry pairs a recipient email address with that recipient's own set of named values.
- **FR-002**: The library MUST automatically split the recipient list into the fewest possible chunks such that no chunk exceeds the per-request recipient limit of 1000.
- **FR-003**: For a list of N recipients, the number of requests MUST equal ceil(N / 1000); specifically 2500 recipients MUST produce exactly 3 requests sized 1000/1000/500, exactly 1000 recipients MUST produce exactly 1 request, and an empty list MUST produce zero requests.
- **FR-004**: An empty recipient list MUST result in zero requests and MUST be reported as a successful no-op, not an error.
- **FR-005**: Each chunk's request MUST reuse the same template reference and the same global variables supplied to the batch-send call.
- **FR-006**: Within each request, the per-recipient values MUST be delivered as a single JSON object keyed by recipient email address, where each value is that recipient's own set of named values.
- **FR-007**: Each request MUST list, as its recipients, exactly the email addresses contained in that chunk and MUST be constructed so each recipient's delivered message shows only their own address.
- **FR-008**: The library MUST preserve the supplied recipient order when partitioning into chunks (chunk k contains recipients [k·1000, (k+1)·1000)).
- **FR-009**: Each request MUST target the registered domain's messages endpoint using `multipart/form-data`, consistent with the single-send and template-send capabilities.
- **FR-010**: The batch-send method MUST accept a cancellation token, honor it between and during requests, and stop issuing further requests once cancellation is observed.
- **FR-011**: On a non-success (non-2xx) response from any request, the library MUST surface the single typed error exposing the HTTP status code and raw response body, consistent with the rest of the library. A 2xx response whose body cannot be parsed into a result MUST surface the same single typed error (inherited unchanged from feature 003's single-send error contract).
- **FR-011a**: Requests MUST be issued sequentially in chunk order, and the operation MUST be fail-fast: on the first non-2xx response the library throws the single typed error immediately and issues no further requests. Chunks already accepted have been sent and are not rolled back; the library does not report which chunks succeeded beyond the thrown error's status and body.
- **FR-012**: The library MUST report, on success, a per-request result so the caller can observe the outcome (message id and status) of each request that was sent; an empty list yields an empty set of results.
- **FR-013**: All behavior in this feature MUST be verifiable offline against a fake transport, with assertions on request count, per-request recipient membership, and the shape of the per-recipient values map.
- **FR-014**: If the recipient list contains a duplicate email address, the library MUST reject the call with a standard `ArgumentException` thrown before any request is issued (consistent with feature 003's client-side validation contract; `MailgunnerException` remains reserved for actual HTTP responses).

### Key Entities *(include if data involves data)*

- **Batch send request**: One developer-issued operation comprising a template reference, optional global variables, and an ordered recipient list. Produces zero or more service requests.
- **Recipient entry**: A pairing of one recipient email address with that recipient's own named values (e.g. name, ticket number, personal link).
- **Global variables**: A set of named values shared by every recipient in the batch, identical across all chunks.
- **Recipient-variables map**: The per-request JSON object keyed by recipient email address; each value is the corresponding recipient's named values for that request.
- **Per-request result**: The outcome of one issued request (message id and status), one per chunk actually sent.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A list of 2500 recipients results in exactly 3 requests sized 1000, 1000, and 500.
- **SC-002**: A list of exactly 1000 recipients results in exactly 1 request; a list of exactly 2000 results in exactly 2 requests with no empty trailing request.
- **SC-003**: An empty recipient list results in zero requests and no error.
- **SC-004**: For any list of N recipients, the number of requests equals ceil(N / 1000) and every recipient appears in exactly one request.
- **SC-005**: In every request, the per-recipient values are a JSON object keyed by recipient email address, each value being that recipient's own variables, and no recipient's request exposes another recipient's address.
- **SC-006**: Every request in a batch reuses the same template reference and the same global variables.
- **SC-007**: All of the above are demonstrated by automated tests running entirely against a fake transport, with no real network access.

## Assumptions

- This feature builds on the registered client (002), single-send (003), and stored-template-with-global-variables (004) capabilities; the template reference and global-variable semantics are those already established by feature 004.
- The per-request recipient limit is fixed at 1000, matching the documented external constraint; it is not configurable in this feature.
- **Partial-failure semantics**: resolved by clarification — fail-fast (see FR-011a and the Clarifications section).
- **Duplicate addresses**: resolved by clarification — rejected with `ArgumentException` before any request (see FR-014).
- The result of a batch send is an ordered collection of per-request results (one per chunk sent), enabling the caller to correlate outcomes; an empty list yields an empty collection.
- Recipient ordering supplied by the caller is significant only insofar as it determines chunk membership; delivery order across the service is not guaranteed by the library.
- Validation of individual addresses and message content beyond what single-send (003) already enforces is out of scope for this feature.
