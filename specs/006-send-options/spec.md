# Feature Specification: Send Enrichment Options (Attachments, Tags, Scheduling, Tracking, Custom Headers & Variables)

**Feature Branch**: `006-send-options`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "A consumer enriches any send with file attachments and inline (embedded) files; one or more tags; a test-mode flag that exercises the pipeline without delivering; open/click tracking toggles; a scheduled future delivery time; and arbitrary custom headers and custom variables. These are the everyday production knobs (attach a ticket PDF, tag a campaign, schedule a reminder). External constraints (requirements): a scheduled delivery time must be RFC 2822 with a numeric timezone offset (e.g., +0000), not a named zone; the combined size of option/header/variable/template parameters per request is capped at 16KB and must be documented. Acceptance criteria: An attachment appears as a file part carrying filename and content type. Tags can be supplied multiple times and all appear. The scheduled-time field is exactly RFC 2822 with a numeric offset. Custom headers and custom variables appear under their documented prefixes. Verified against a fake transport."

## Clarifications

### Session 2026-06-24

- Q: When the combined option/header/variable/template size exceeds the documented 16KB cap, how should the library behave? → A: Document only — no client-side size check; if exceeded, the service rejects the request and the library surfaces its single typed error (status + body). No client-side `ArgumentException` for size.
- Q: When an attachment is supplied without an explicit content type, what should the library do? → A: Default to `application/octet-stream` — always emit a valid content type, with no filename sniffing.
- Q: Should click tracking expose the service's `htmlonly` mode in addition to on/off? → A: Yes — click tracking is yes/no/htmlonly; open tracking remains on/off.
- Q: What value type should custom variables (`v:` prefix) accept? → A: String values only — name→string pairs emitted verbatim; callers JSON-encode structured data themselves.

## User Scenarios & Testing *(mandatory)*

<!--
  The "user" of this feature is the .NET application developer who has already registered the
  Mailgunner client (feature 002) and can send a single email (003), send from a stored template
  (004), and send a personalized mass batch (005). This feature adds the everyday production
  "knobs" that ride on top of ANY of those sends: attachments and inline files, tags, a test-mode
  flag, open/click tracking toggles, a scheduled delivery time, and arbitrary custom headers and
  custom variables. Stories are ordered by importance; each is independently testable offline
  against a fake transport.
-->

### User Story 1 - Attach files and embed inline files in a send (Priority: P1)

A developer sending a confirmation needs to attach a document — for example, a ticket PDF or an
invoice — so the recipient receives the file alongside the message. They may also need to embed an
image inline (a logo or banner referenced from the HTML body) rather than as a downloadable
attachment. They hand the client the file content together with a filename and content type, and the
library delivers it as a proper file part on the outgoing request.

**Why this priority**: Attaching a real document (the "attach a ticket PDF" case) is the single most
common production enrichment and the one with an explicit acceptance criterion: an attachment must
appear as a file part carrying filename and content type. Without it the other knobs have no
standalone value for transactional sends.

**Independent Test**: Send a message with one attachment (content, filename, content type) and one
inline file against a fake transport; capture the outgoing request and confirm each appears as a
distinct file part — the attachment under the documented attachment field and the inline file under
the documented inline field — each carrying its filename and declared content type.

**Acceptance Scenarios**:

1. **Given** a send with one attachment supplied as content plus a filename and content type, **When** the outgoing request is captured, **Then** the attachment appears as a file part carrying that filename and that content type.
2. **Given** a send with one inline (embedded) file, **When** the request is captured, **Then** the inline file appears as a file part under the documented inline field — distinct from attachments — so it can be referenced from the HTML body by its content id.
3. **Given** a send with multiple attachments and multiple inline files, **When** the request is captured, **Then** every attachment and every inline file appears as its own file part, each preserving its own filename and content type.

---

### User Story 2 - Tag, test, and toggle tracking on a campaign (Priority: P2)

A developer running a campaign needs to label the send with one or more tags (so opens and clicks
can be grouped and reported by campaign), to run the whole pipeline in test mode without actually
delivering while wiring things up, and to turn open and click tracking on or off for this particular
send.

**Why this priority**: Tags, test mode, and tracking toggles are the routine campaign-management
controls; tags carry an explicit acceptance criterion (supplied multiple times, all appear) and
test mode is what lets a developer exercise the send safely before going live.

**Independent Test**: Send a message supplying three tags, the test-mode flag, and both tracking
toggles against a fake transport; capture the request and confirm all three tags are present, the
test-mode option is present, and each tracking toggle is present with the requested value.

**Acceptance Scenarios**:

1. **Given** a send to which the same tag option is supplied three times with three distinct values, **When** the request is captured, **Then** all three tag values appear under the documented tag option (supplying a tag multiple times is additive, not overwriting).
2. **Given** a send with test mode enabled, **When** the request is captured, **Then** the documented test-mode option is present and set to enable the pipeline-without-delivery behavior.
3. **Given** a send with open tracking enabled and click tracking disabled, **When** the request is captured, **Then** the documented open-tracking and click-tracking options each appear carrying their requested on/off values.
4. **Given** a send that supplies none of these options, **When** the request is captured, **Then** no tag, test-mode, or tracking option appears (the request is unchanged from a plain send and the account defaults apply).

---

### User Story 3 - Schedule a send for a future time (Priority: P3)

A developer scheduling a reminder needs the message to be delivered at a specific future moment
rather than immediately, expressed unambiguously so the service interprets the instant correctly
regardless of locale.

**Why this priority**: Scheduled delivery is a high-value everyday knob (the "schedule a reminder"
case) and is governed by a strict external format constraint, making it the part most prone to
formatting defects and therefore worth its own slice.

**Independent Test**: Schedule a send for a known future instant with a known offset, capture the
request against a fake transport, and confirm the delivery-time field is exactly an RFC 2822
date-time with a numeric timezone offset (e.g. `+0000`) and never a named zone.

**Acceptance Scenarios**:

1. **Given** a send scheduled for a specific future instant, **When** the request is captured, **Then** the documented delivery-time option carries a value formatted exactly as RFC 2822 with a numeric timezone offset (e.g. `Thu, 25 Jun 2026 14:00:00 +0000`).
2. **Given** a scheduled instant expressed in a non-UTC offset, **When** the request is captured, **Then** the delivery-time value uses the corresponding numeric offset (e.g. `+0300`) and never a named zone abbreviation (e.g. never `EST` or `UTC`).
3. **Given** a send with no scheduled time, **When** the request is captured, **Then** no delivery-time option is present.

---

### User Story 4 - Attach custom headers and custom variables (Priority: P4)

A developer integrating with downstream systems needs to set arbitrary custom message headers (for
example a correlation header) and to attach arbitrary custom variables that travel with the message
and surface later in tracking/webhook data, without those values being confused with built-in
options.

**Why this priority**: Custom headers and variables are the most open-ended extension point and the
least commonly needed of the knobs, but they round out the feature and carry an explicit acceptance
criterion (each appears under its documented prefix).

**Independent Test**: Send a message supplying one custom header and one custom variable against a
fake transport; capture the request and confirm the header appears under the documented header
prefix and the variable appears under the documented variable prefix, each carrying the supplied
name and value.

**Acceptance Scenarios**:

1. **Given** a send with a custom header named `X-Correlation-Id`, **When** the request is captured, **Then** it appears under the documented custom-header prefix carrying the supplied name and value.
2. **Given** a send with a custom variable, **When** the request is captured, **Then** it appears under the documented custom-variable prefix carrying the supplied name and value.
3. **Given** a send with multiple custom headers and multiple custom variables, **When** the request is captured, **Then** each appears as its own entry under the appropriate prefix with no collision between headers, variables, and built-in options.

---

### Edge Cases

- **No options supplied**: a send with none of these enrichments produces a request equivalent to the corresponding plain send (no added parts) — no empty attachment parts, no stray option/header/variable fields.
- **Same tag supplied repeatedly**: additive — every supplied tag value appears; values are not de-duplicated or collapsed by the library.
- **Attachment vs inline file**: the two are delivered under distinct documented fields so an inline file is embeddable (referenceable from HTML) while an attachment is downloadable; the same file content may legitimately be used for either.
- **Attachment missing a content type**: the library still emits a file part with the filename and defaults the content type to `application/octet-stream` rather than failing the send (see FR-002 and Assumptions).
- **Scheduled time in the past**: the library does not reject a past delivery time; it formats and sends whatever instant is supplied and leaves accept/deliver-now behavior to the service (see Assumptions).
- **Combined option/header/variable/template size exceeds 16KB**: the documented service limit; the library documents it and the service surfaces any rejection as the standard typed error (see FR-014 and Assumptions).
- **These options applied to a batched mass send**: the same enrichments compose with a personalized batch (005) and are repeated on each chunk's request (see FR-015).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The library MUST allow any send — single (003), stored-template (004), and personalized batch (005) — to be optionally enriched with attachments, inline files, tags, a test-mode flag, open/click tracking toggles, a scheduled delivery time, custom headers, and custom variables. Every enrichment MUST be optional.
- **FR-002**: An attachment MUST be delivered as a file part carrying the supplied filename and content type, under the documented attachment field. When the caller omits the content type, the library MUST default it to `application/octet-stream` (no filename-based inference) so the part always carries a valid content type.
- **FR-003**: An inline (embedded) file MUST be delivered as a file part under the documented inline field — distinct from the attachment field — so it can be referenced from the HTML body by its content id, carrying its supplied filename and content type.
- **FR-004**: The library MUST support multiple attachments and multiple inline files on a single send, each emitted as its own file part with its own filename and content type.
- **FR-005**: The library MUST allow one or more tags to be supplied; when multiple tags are supplied they MUST all appear under the documented tag option (additive, not overwriting, and not de-duplicated by the library). A blank or whitespace-only tag entry MUST be skipped (not emitted).
- **FR-006**: The library MUST allow a test-mode flag; when enabled, the documented test-mode option MUST be present on the request so the send exercises the full pipeline without delivering.
- **FR-007**: The library MUST allow an open-tracking toggle and a click-tracking toggle to be set independently; when set, each MUST appear under its documented tracking option carrying the requested value. Open tracking is on/off; click tracking supports on, off, and an HTML-only mode (`htmlonly`) that tracks clicks in HTML parts only.
- **FR-008**: When a given enrichment (any tag, test mode, either tracking toggle, scheduled time, custom header, or custom variable) is not supplied, the corresponding field MUST be absent from the request, leaving the service's account-level defaults in effect.
- **FR-009**: The library MUST allow a scheduled future delivery time; when supplied it MUST appear under the documented delivery-time option.
- **FR-010**: The scheduled delivery-time value MUST be formatted exactly as an RFC 2822 date-time with a numeric timezone offset (e.g. `+0000`, `+0300`) and MUST NOT use a named timezone (e.g. `UTC`, `EST`).
- **FR-011**: The library MUST allow arbitrary custom headers; each MUST appear under the documented custom-header prefix carrying the supplied header name and value. Header names are unique — supplying the same name more than once replaces the earlier value rather than emitting duplicates — and the relative order in which headers are emitted is immaterial (each is an independent field).
- **FR-012**: The library MUST allow arbitrary custom variables as name→string pairs; each MUST appear under the documented custom-variable prefix carrying the supplied name and string value verbatim. The library does not serialize structured objects; callers that need structured data supply a pre-encoded string value. Variable names are unique (same name twice replaces the earlier value) and the relative emission order is immaterial.
- **FR-013**: Custom headers, custom variables, and built-in options MUST occupy distinct documented namespaces so they cannot collide with one another or with message fields.
- **FR-014**: The documented combined 16KB cap on the option, custom-header, custom-variable, and template parameters per request MUST be stated in the public documentation. The library is not required to pre-validate this size client-side; any service rejection MUST surface as the library's single typed error (see Assumptions).
- **FR-015**: These enrichments MUST compose with the personalized batch send (005): when a batch is enriched, every chunk's request MUST carry the same enrichments.
- **FR-016**: All behavior in this feature MUST be verifiable offline against a fake transport, with assertions on file parts (filename and content type), the presence and multiplicity of tag/test-mode/tracking options, the exact RFC 2822 delivery-time value, and the documented header/variable prefixes.
- **FR-017**: Failure handling MUST be unchanged: a non-success (non-2xx) response MUST surface the single typed error exposing the HTTP status code and raw response body, consistent with the rest of the library.

### Key Entities *(include if feature involves data)*

- **Attachment**: A file to deliver alongside the message, comprising file content, a filename, and a content type; emitted as a file part under the documented attachment field.
- **Inline file**: An embedded file referenceable from the HTML body by content id; comprising file content, a filename, and a content type; emitted as a file part under the documented inline field.
- **Tag**: A short label applied to the send for grouping/reporting; one send may carry several.
- **Tracking options**: The test-mode flag and the open- and click-tracking toggles that adjust how this send is processed and reported.
- **Scheduled delivery time**: A future instant at which delivery should occur, carried as an RFC 2822 date-time with a numeric offset.
- **Custom header**: An arbitrary message header (name and value) supplied by the caller, carried under the documented header prefix.
- **Custom variable**: An arbitrary name→string pair that travels with the message and surfaces in later tracking/webhook data, carried under the documented variable prefix; values are strings (callers pre-encode any structured data).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An attachment supplied with content, a filename, and a content type appears in the captured request as a file part carrying exactly that filename and content type.
- **SC-002**: An inline file appears as a file part under the documented inline field, distinct from attachments, and multiple attachments/inline files each appear as their own part.
- **SC-003**: When the same tag option is supplied N times, all N tag values are present in the captured request.
- **SC-004**: Test mode and the open/click tracking toggles, when supplied, each appear under their documented option carrying the requested value; when omitted, none of them appears.
- **SC-005**: A scheduled delivery time appears as a value formatted exactly as RFC 2822 with a numeric timezone offset, and never as a named zone.
- **SC-006**: Custom headers and custom variables each appear under their documented prefixes carrying the supplied names and values, with no collision between the two or with built-in options.
- **SC-007**: The 16KB combined limit on option/header/variable/template parameters is stated in the public documentation.
- **SC-008**: All of the above are demonstrated by automated tests running entirely against a fake transport, with no real network access.

## Assumptions

- This feature builds on the registered client (002), single-send (003), stored-template send (004), and personalized batch send (005); the enrichments here ride on top of those existing send capabilities rather than introducing a new send path.
- The documented field/prefix names for options, custom headers, and custom variables are those fixed by the project's API-fidelity rules (the `o:`, `h:`, and `v:` namespaces and the attachment/inline file fields); this spec refers to them as "documented" rather than restating the wire tokens.
- The scheduled delivery time is supplied to the library as a timestamp that already carries a numeric offset (a point-in-time value), and the library is responsible for rendering it to the exact RFC 2822 string; the caller does not hand-format the string. The library does not reject a past delivery time.
- The 16KB combined cap is a **documentation** requirement, per the external constraint ("must be documented") and confirmed by clarification (Session 2026-06-24): the library does not compute or enforce the combined size client-side. Should the limit be exceeded, the service rejects the request and the library surfaces its single typed error unchanged.
- Open tracking is on/off; click tracking additionally supports an HTML-only mode (`htmlonly`), resolved by clarification (Session 2026-06-24). No further tracking sub-modes are in scope.
- An attachment supplied without an explicit content type is still emitted (filename preserved) using a default content type of `application/octet-stream` (resolved by clarification, Session 2026-06-24) rather than failing the send; the library does not infer the type from the filename extension.
- Validation of message addresses and content beyond what single-send (003) already enforces, and the semantics of test mode and tracking on the service side, are outside the library's responsibility — the library only ensures the documented fields are present and correctly shaped.
