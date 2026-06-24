# Feature Specification: Suppression Lists Management (Bounces, Unsubscribes, Complaints)

**Feature Branch**: `007-suppression-lists`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "A consumer manages Mailgun's suppression lists for a domain — bounces, unsubscribes, complaints — by listing entries (paginating through large lists), adding an address, and removing an address. Built-in suppression handling is the reason the consumer chose Mailgun over building unsubscribe management themselves, so the library must expose it cleanly. External constraints (requirements): these are JSON endpoints (unlike sending); large lists paginate via a cursor/next link in the response. Acceptance criteria: Listing parses a page and follows pagination to retrieve subsequent pages. Add and remove issue the correct create/delete operations per list type. Responses deserialize into typed models for each list type. Independent of the sending pipeline; developable and testable on its own. Verified against a fake transport."

## Clarifications

### Session 2026-06-24

- Q: What public API shape should listing expose (pagination)? → A: Both — an auto-streaming "all entries" path (the ergonomic default, transparently following the next pointer) built on top of a public single-page primitive (one page + opaque cursor) that callers can drive themselves.
- Q: Should the caller be able to set the listing page size? → A: Yes — an optional page-size parameter; the library applies it to the first request only, then follows the service's next pointers unchanged. When omitted, the service default page size applies.
- Q: What should "remove" cover — single address only, or also clearing a whole list? → A: Both — remove a single address from a list type, and clear an entire list (delete all entries) in one call. The two are distinct operations.
- Q: What may "add" carry beyond the address? → A: The address plus each list type's documented optional fields — bounce: code/error (and optional timestamp); unsubscribe: tag(s); complaint: address only. All extra fields are optional.
- Q: Should a single-entry lookup (get one record by address) be in scope? → A: Yes — expose "get one entry by address" for each list type, returning that type's typed model; a not-found response surfaces the single typed error.

## User Scenarios & Testing *(mandatory)*

<!--
  The "user" of this feature is the .NET application developer who has already registered the
  Mailgunner client (feature 002). This feature is a NEW capability area that stands apart from
  the sending pipeline (003–006): instead of composing and delivering messages, it manages the
  three suppression lists Mailgun maintains per domain — bounces, unsubscribes, and complaints.
  These are JSON endpoints (not the multipart sends), and large lists are read by following a
  pagination cursor/next link returned in each response. Stories are ordered by importance; each
  is independently testable offline against a fake transport.
-->

### User Story 1 - List a suppression list, following pagination through large lists (Priority: P1)

A developer needs to read what is on a domain's suppression list — every bounced address, every
unsubscribe, or every complaint — so the application can reconcile its own records, build a
suppression report, or audit who will not receive mail. Because a busy domain can accumulate tens
of thousands of entries, the list comes back one page at a time with a pointer to the next page;
the developer needs every entry without hand-rolling the paging loop. They name the list type
(bounces, unsubscribes, or complaints) and receive the entries as typed models, with the library
transparently following the pagination pointer to retrieve subsequent pages until the list is
exhausted.

**Why this priority**: Reading the lists is the core reason the consumer adopted Mailgun's built-in
suppression handling instead of building their own, and it carries the most explicit acceptance
criteria (parse a page, follow pagination, deserialize into typed models). The add and remove
operations are only meaningful once a developer can see and trust the list contents.

**Independent Test**: Against a fake transport primed with a multi-page response (page 1 carries
entries plus a next pointer; later pages carry more entries; the final page carries no next
pointer), list each of the three list types and confirm that all entries from every page are
returned as the list type's typed model, and that enumeration stops cleanly after the final page.

**Acceptance Scenarios**:

1. **Given** a suppression list whose response fits in a single page (no next pointer), **When** the developer lists that list type, **Then** the library parses the page into typed models and returns exactly those entries, making no further page request.
2. **Given** a suppression list that spans multiple pages (each non-final page carries a next pointer), **When** the developer lists that list type, **Then** the library follows the next pointer from each page to the subsequent page and returns the entries from every page in order, with no entry omitted or duplicated.
3. **Given** a multi-page list, **When** the final page is reached (no next pointer, or an empty page), **Then** the library stops requesting further pages and completes enumeration.
4. **Given** a suppression list with no entries, **When** the developer lists that list type, **Then** the library returns an empty result and makes no follow-up page request.
5. **Given** each of the three list types (bounces, unsubscribes, complaints), **When** its page is parsed, **Then** entries deserialize into that list type's typed model carrying its distinct fields (e.g. a bounce's failure code/error and timestamp; an unsubscribe's tag(s) and timestamp; a complaint's address and timestamp).
6. **Given** a single address known to be on a list type, **When** the developer fetches that one entry by address, **Then** the library returns that list type's typed model for the address; **and given** an address not on the list, **Then** it surfaces the single typed error (status + body).

---

### User Story 2 - Add an address to a suppression list (Priority: P2)

A developer needs to place an address onto a suppression list directly — for example, recording an
unsubscribe captured on the application's own preference page, pre-seeding a known bad address onto
the bounce list, or importing complaints from another system — so Mailgun will honor that
suppression on future sends. They name the list type and supply the address (plus any
list-appropriate detail), and the library issues the correct create operation for that list type.

**Why this priority**: Programmatically adding suppressions — especially recording unsubscribes the
application collects itself — is the most common write operation and the heart of "managing"
suppression rather than only reading it. It depends on the typed models established by Story 1.

**Independent Test**: Against a fake transport, add an address to each of the three list types and
capture the outgoing request; confirm each targets that list type's documented create endpoint with
the supplied address (and any list-appropriate fields) carried in a JSON request body, and that a
success response is surfaced to the caller.

**Acceptance Scenarios**:

1. **Given** an address to suppress, **When** the developer adds it to a named list type, **Then** the library issues the create operation against that list type's documented endpoint carrying the address in a JSON request.
2. **Given** an add to a list type that accepts extra detail (e.g. a tag on an unsubscribe, or a code/error on a bounce), **When** the developer supplies that detail, **Then** it is carried alongside the address in the request; **and** when the developer omits it, only the address is sent and service defaults apply.
3. **Given** a successful add, **When** the response returns, **Then** the library reports success to the caller; **and given** a non-success response, **Then** the library surfaces the single typed error (status code plus raw body).

---

### User Story 3 - Remove an address from, or clear, a suppression list (Priority: P3)

A developer needs to take a single address off a suppression list — for example, clearing a bounce
after a recipient confirms their mailbox is fixed, or honoring a re-subscribe — so that address can
receive mail again. They may also need to clear an entire list in one call — for example, resetting
a domain's bounce list during maintenance. They name the list type and (for a single removal) the
address, and the library issues the correct delete operation: deleting one address, or deleting all
entries of that list type.

**Why this priority**: Removal is the least frequent of the three operations (most suppression
churn is reads and adds) but completes the management lifecycle and rounds out the feature. It
reuses the same per-list-type routing as add.

**Independent Test**: Against a fake transport, (a) remove an address from each of the three list
types and confirm each issues the delete operation for that list type targeting the specific
address; and (b) clear each list type and confirm the library issues the delete-all operation for
that list type targeting no specific address. Confirm success and not-found responses are handled
per the library's error contract.

**Acceptance Scenarios**:

1. **Given** an address present on a list type, **When** the developer removes it, **Then** the library issues the delete operation for that list type targeting exactly that address.
2. **Given** a list type with entries, **When** the developer clears the whole list, **Then** the library issues the delete-all operation for that list type targeting no specific address.
3. **Given** a successful removal or clear, **When** the response returns, **Then** the library reports success to the caller.
4. **Given** an address that is not on the list (service responds not-found), **When** the developer removes it, **Then** the library surfaces the single typed error carrying the status code and raw body rather than reporting success.

---

### Edge Cases

- **Empty list**: listing a list type with no entries returns an empty result and triggers no follow-up page request (no error, no infinite loop).
- **Single page vs. many pages**: a response with no next pointer ends enumeration immediately; a response with a next pointer is followed to the next page, repeating until a page carries no next pointer (or is empty).
- **Opaque pagination pointer**: the next pointer/cursor is treated as opaque and followed exactly as returned by the service; the library does not synthesize or mutate paging parameters itself.
- **Distinct typed models**: the three list types carry different fields; an entry from one list type deserializes into that type's model and is never conflated with another's (e.g. a complaint has no failure code, an unsubscribe carries tag information).
- **JSON, not multipart**: these endpoints exchange JSON request/response bodies, unlike the multipart sends (003–006); content handling here is independent of the message-building pipeline.
- **Address not found on remove**: surfaced as the standard typed error (status + body), not swallowed as a success.
- **Clearing a list**: clearing an entire list type targets no specific address; whatever the service returns (including clearing an already-empty list) is surfaced per the standard contract — success as success, non-2xx as the typed error.
- **Single-entry lookup miss**: fetching one entry for an address that is not on the list surfaces the typed error (not-found), rather than returning a null or empty model.
- **Add of an already-present address**: the library issues the create operation as requested and surfaces whatever the service returns; it does not pre-check membership or de-duplicate client-side.
- **Cancellation mid-pagination**: a cancellation requested while pages are still being fetched stops the enumeration promptly without retrieving further pages.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The library MUST support all three of a domain's suppression list types — bounces, unsubscribes, and complaints — for listing, adding an address, and removing an address.
- **FR-002**: Listing a list type MUST parse each response page into the list type's typed models. The library MUST expose two listing paths over the same parsing: (a) a single-page primitive that returns one parsed page plus the opaque pagination pointer, letting callers drive paging themselves; and (b) an auto-following path — the ergonomic default — that transparently follows the pagination pointer from each page to retrieve and yield the entries from every page. Path (b) MUST be built on top of path (a).
- **FR-003**: Pagination MUST be driven solely by the next pointer/cursor returned in the response; the pointer is treated as opaque and followed exactly as provided, and the library MUST NOT fabricate or mutate paging parameters of its own. The sole exception is a caller-supplied page size (FR-015), which the library MAY place on the first request only — every subsequent page MUST still be fetched by following the service's next pointer unchanged.
- **FR-015**: The library MUST accept an optional page-size parameter on a listing call (on both the single-page primitive and the auto-following path). When supplied, it MUST be applied to the first request; when omitted, the service's default page size applies. The page size MUST NOT be re-applied to subsequent pages, which are fetched via the next pointer.
- **FR-004**: Listing MUST terminate when the service indicates no further pages — when the response carries no next pointer or returns an empty page — without requesting additional pages and without entering an unbounded loop.
- **FR-005**: A list type with no entries MUST yield an empty result, with no follow-up page request and no error.
- **FR-006**: Each list type's entries MUST deserialize into a typed model specific to that list type, carrying that type's distinct fields (at minimum: a bounce's address, failure code, error detail, and timestamp; an unsubscribe's address, tag information, and timestamp; a complaint's address and timestamp). Models for different list types MUST NOT be conflated.
- **FR-007**: Adding an address MUST issue the create operation against the named list type's documented endpoint, carrying the address (and any supplied list-appropriate detail) in a JSON request body.
- **FR-008**: Add MUST support each list type's documented optional fields in addition to the address: for a bounce, a failure code and error detail (and an optional timestamp); for an unsubscribe, tag(s); for a complaint, the address only. Every optional field MUST be included when supplied and omitted when not, leaving service defaults in effect.
- **FR-009**: Removing an address MUST issue the delete operation for the named list type targeting exactly the supplied address.
- **FR-016**: The library MUST also support clearing an entire list type in a single call (delete all entries), issued as a delete-all operation against the list type and targeting no specific address. This is a distinct operation from single-address removal (FR-009).
- **FR-017**: The library MUST support fetching a single entry by address from a given list type, returning that list type's typed model (FR-006). A not-found response MUST surface the single typed error (status code + raw body), not a null or empty success.
- **FR-010**: All suppression-list endpoints MUST be treated as JSON (request and/or response bodies are JSON), distinct from the multipart content used by the sending pipeline.
- **FR-011**: This capability MUST be independent of the sending pipeline (003–006): it MUST be developable, usable, and testable without invoking or depending on any message-sending path, sharing only the registered client's base configuration (region/base URL and authentication) from feature 002.
- **FR-012**: A non-success (non-2xx) response from any operation in this feature (list, get-single, add, remove, or clear) MUST surface the library's single typed error exposing the HTTP status code and the raw response body, consistent with the rest of the library.
- **FR-013**: Every operation MUST be cancelable; a cancellation requested during a paginated listing MUST stop enumeration promptly without fetching further pages.
- **FR-014**: All behavior in this feature MUST be verifiable offline against a fake transport, with assertions on: the per-page parse and pagination-following (including stop conditions and full-list aggregation) and the optional first-request page size; the typed model fields per list type; the single-entry lookup; the create operation per list type with its optional fields; the single-address delete and the delete-all (clear) operation and their targets; and the typed-error surfacing on non-success responses.

### Key Entities *(include if feature involves data)*

- **Suppression list type**: One of bounces, unsubscribes, or complaints — the three lists Mailgun maintains per domain; each has its own listing, add, and remove operations and its own entry model.
- **Bounce entry**: A typed record of an address that hard-bounced, carrying the address, a failure code, an error detail, and a timestamp.
- **Unsubscribe entry**: A typed record of an address that unsubscribed, carrying the address, tag information indicating what was unsubscribed from, and a timestamp.
- **Complaint entry**: A typed record of an address that filed a spam complaint, carrying the address and a timestamp.
- **List page**: A single page of a list response — a set of entries plus an optional pagination pointer to the next page.
- **Pagination pointer**: The opaque cursor/next link returned with a page that, when present, locates the next page; its absence (or an empty page) marks the end of the list.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Listing a single-page list returns exactly the entries on that page and then stops at the end of the list — recognizing the final/empty page — yielding no further entries. (Because the service may include a next pointer even on the last page, enumeration may issue one trailing fetch that returns an empty page to confirm the end; it never yields or skips entries.)
- **SC-002**: Listing a multi-page list (e.g. three pages) returns every entry from every page, in order, with none omitted or duplicated, and stops after the final page.
- **SC-003**: Listing an empty list returns zero entries and makes no follow-up page request.
- **SC-004**: For each of the three list types, parsed entries populate that type's distinct typed-model fields (bounce code/error/timestamp; unsubscribe tag/timestamp; complaint address/timestamp).
- **SC-005**: Adding an address issues the create operation to the correct per-list-type endpoint carrying the address (and any supplied detail) as JSON.
- **SC-006**: Removing an address issues the delete operation for the correct per-list-type endpoint targeting exactly that address; clearing a list issues the delete-all operation for that list type targeting no specific address.
- **SC-007**: A non-2xx response on any list/get/add/remove/clear operation surfaces the single typed error exposing the status code and raw response body.
- **SC-008**: Fetching a single entry by address returns that list type's typed model for a present address, and surfaces the typed error for an absent one.
- **SC-009**: The entire feature is exercised by automated tests running offline against a fake transport, with no real network access and no dependency on the sending pipeline.

## Assumptions

- This feature builds only on the registered client (002), reusing its region/base-URL selection and authentication; it does not depend on the single-send (003), template (004), batch (005), or send-options (006) features, and is exercised on its own.
- Listing exposes both a caller-driven single-page primitive (one parsed page + opaque next pointer) and an auto-following path that yields all entries across pages (resolved by clarification, Session 2026-06-24). The auto-following path is the ergonomic default — it streams large lists rather than materializing the whole list at once, so callers get every entry without writing a paging loop — and it is implemented on top of the single-page primitive (the per-page entries + opaque next pointer is the shared unit).
- Removal supports two distinct operations (resolved by clarification, Session 2026-06-24): removing a single address from a list type, and clearing an entire list type (delete all entries) in one call. The clear-all operation targets the list type with no specific address.
- On add, the address plus any list-appropriate optional detail is sent as a JSON request; the library does not pre-validate list membership, de-duplicate, or infer fields — service-side validation and semantics are the service's responsibility.
- The documented per-list-type endpoints and JSON field names are those fixed by the project's API-fidelity rules; this spec refers to them as "documented" rather than restating wire tokens, consistent with the other feature specs.
- JSON (de)serialization uses the library's standard JSON stack (per the project constitution); these endpoints exchange JSON, in contrast to the multipart sends.
- The pagination pointer/cursor is opaque: the library follows whatever next link the service returns and makes no assumptions about its internal format or stability across pages.
