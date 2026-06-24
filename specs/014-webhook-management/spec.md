# Feature Specification: Domain Webhook Management (Register, List, Read, Update, Delete)

**Feature Branch**: `014-webhook-management`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "Add domain webhook management to the client so a consumer can register, list, read, update, and delete Mailgun webhook endpoints, completing the push-based delivery-tracking story alongside the existing MailgunWebhookSignature.Verify primitive (so you can know whether an invitation/ticket was delivered, opened, failed, complained, or unsubscribed, and keep marketing lists clean). In scope: Webhook CRUD exposed on the client, mirroring the client.Suppressions shape (e.g. client.Webhooks): list, get, create, update, delete domain webhooks by event type (delivered, opened, clicked, failed/permanent_fail, temporary_fail, complained, unsubscribed). Prefer the v4 surface (one URL associated with multiple event types per call). JSON request/response handling via System.Text.Json source generation (trim/AOT-safe), regional host + Basic auth via the shared typed HttpClient, and the single MailgunnerException error contract on non-2xx. Out of scope: the Events/Logs/Metrics pull APIs, account-level (v1) webhooks, domain management, and parsing of inbound webhook payloads (verification stays the existing primitive; typed payload parsing can be a separate later feature). Key requirements / edge cases: every public async method takes a CancellationToken and uses ConfigureAwait(false); network-free unit tests with a fake handler assert the CRUD wire format, region/domain routing, and the error contract; the public surface is documented with a CHANGELOG entry (SemVer MINOR)."

## Clarifications

### Session 2026-06-24

- Q: Which webhook wire surface should the library target — v3 or v4? → A: **v3** — target `{base}/v3/{domain}/webhooks`. (Revised during `/speckit-plan` after verifying the live Mailgun 2026 contract: the event-type-centric CRUD model these stories describe — one webhook keyed by a single event type, associating one or more callback URLs, with per-event-type `.../{name}` endpoints and a `GET` list/read — is the **v3** API. The real **v4** surface is URL-centric, on the collection root, with no `GET` and no `.../{name}` endpoints, and cannot back these stories as written. The initial answer had named v4 and the constitution was briefly amended v3 → v4; both were corrected back to v3 — constitution v1.4.0.) v3 supports one or more callback URLs per event type (Mailgun caps it at 3).
- Q: When registering one callback URL across several event types in a single call (which fans out to one v3 create request per event type), what is the partial-failure behavior if one event type returns non-2xx? → A: Fail-fast, no rollback — issue the creates sequentially; the first non-2xx throws the single `MailgunnerException` and stops the remaining creates; registrations already created are left in place (state may be partial).

## User Scenarios & Testing *(mandatory)*

<!--
  The "user" of this feature is the .NET application developer who has already registered the
  Mailgunner client (feature 002). This feature is a NEW capability area that pairs with the
  existing webhook signature-verification primitive (feature 008): verification confirms that an
  inbound callback is genuine, but the consumer still needs a way to REGISTER the endpoints that
  cause Mailgun to send those callbacks in the first place. This feature manages a domain's
  webhook registrations — the callback URLs Mailgun invokes when a message is delivered, opened,
  clicked, fails, draws a complaint, or is unsubscribed — so the push-based delivery-tracking story
  becomes self-service end to end. These endpoints return JSON responses (with form-encoded create/update
  requests), distinct from the multipart message sends, and the
  capability mirrors the shape of the existing suppressions area (e.g. client.Webhooks alongside
  client.Suppressions). Stories are ordered by importance; each is independently testable offline
  against a fake transport.
-->

### User Story 1 - Register a webhook endpoint for one or more event types (Priority: P1)

A developer needs Mailgun to start notifying their application about delivery events — so that, for
example, an invitation or support ticket email can be tracked as delivered, opened, failed,
complained, or unsubscribed, and the marketing list can be kept clean. They name the event type(s)
they care about and supply the callback URL Mailgun should invoke, and the library registers the
webhook for the domain so that Mailgun begins sending those callbacks. This is the capability that
makes webhooks fire; without it, the verification primitive (feature 008) has nothing to verify.

**Why this priority**: Registration is the headline value of the feature and the missing half of the
push-delivery story — verification already exists, but a consumer cannot make webhooks fire without
it. It is the operation that turns "we can check a signature" into "we receive and can act on
delivery events," and every other operation in this feature (inspect, update, delete) only matters
once a registration exists.

**Independent Test**: Against a fake transport, register a webhook for a single supported event type
with a callback URL and confirm the outgoing request targets the domain's webhook-creation endpoint
on the correct regional host, carries the event type and URL in a form-encoded request, and surfaces the
created registration to the caller; then register one URL across several event types in a single
call and confirm a registration is established for each named event type.

**Acceptance Scenarios**:

1. **Given** a supported event type and a callback URL, **When** the developer registers a webhook, **Then** the library issues the create operation against the domain's webhook endpoint on the region-appropriate host, carrying the event type and URL in a form-encoded request, and returns the created registration.
2. **Given** a single callback URL and several supported event types, **When** the developer registers them in one call, **Then** the library establishes a registration associating that URL with each named event type, and reports the result to the caller.
3. **Given** more than one callback URL for a single event type (where the service supports multiple URLs per event), **When** the developer registers them, **Then** all supplied URLs are carried in the create request for that event type.
4. **Given** a successful registration, **When** the response returns, **Then** the library reports success and exposes the created registration; **and given** a non-success response (e.g. the event type is already registered, or the URL is rejected), **Then** the library surfaces the single typed error carrying the status code and raw body.

---

### User Story 2 - List all registrations and read a single one (Priority: P2)

A developer needs to see what is currently configured for a domain — which event types have webhooks,
and where each one points — so the application can audit its delivery-tracking setup, verify a
registration took effect, or reconcile configuration during deployment. They ask for the full set of
the domain's webhook registrations, or for the one registration belonging to a specific event type,
and receive the callback URL(s) as a typed result.

**Why this priority**: Inspection is how a developer confirms that registration (Story 1) worked and
how they audit existing configuration before changing it. It depends on the typed registration model
established by Story 1 and is the natural second step, but it delivers no push events on its own, so
it ranks below registration.

**Independent Test**: Against a fake transport primed with a domain that has several event types
registered, list all registrations and confirm each event type's callback URL(s) deserialize into
the typed registration model; then read a single event type's registration and confirm it returns
that registration, while reading an event type with no registration surfaces the single typed error.

**Acceptance Scenarios**:

1. **Given** a domain with one or more registered event types, **When** the developer lists registrations, **Then** the library issues the list operation against the domain's webhook endpoint and returns each event type's registration (event type plus its callback URL(s)) as typed models.
2. **Given** a domain with no registered webhooks, **When** the developer lists registrations, **Then** the library returns an empty result without error.
3. **Given** an event type that has a registration, **When** the developer reads that one event type, **Then** the library returns that event type's registration (its callback URL(s)) as a typed model.
4. **Given** an event type with no registration, **When** the developer reads it, **Then** the library surfaces the single typed error (status code plus raw body) rather than a null or empty success.

---

### User Story 3 - Update an existing registration's callback URL(s) (Priority: P3)

A developer needs to repoint an existing webhook — for example, when the receiving service moves to a
new URL, or when an additional endpoint must also receive the same event — so callbacks continue
arriving at the right place without first deleting and recreating the registration. They name the
event type and supply the new callback URL(s), and the library updates that registration in place.

**Why this priority**: Updating completes the management lifecycle and is more convenient than
delete-then-recreate, but it is less frequent than the initial registration and inspection. It
reuses the same per-event-type routing and typed model as the earlier stories.

**Independent Test**: Against a fake transport, update an existing event type's registration with a
new callback URL and confirm the library issues the update operation against that event type's
endpoint carrying the new URL(s) in a form-encoded request, and that the updated registration is returned;
confirm that updating an event type with no existing registration surfaces the typed error.

**Acceptance Scenarios**:

1. **Given** an event type with an existing registration, **When** the developer updates it with new callback URL(s), **Then** the library issues the update operation against that event type's endpoint carrying the new URL(s) in a form-encoded request and returns the updated registration.
2. **Given** a successful update, **When** the response returns, **Then** the library reports success and exposes the updated registration.
3. **Given** an event type with no existing registration (service responds not-found), **When** the developer updates it, **Then** the library surfaces the single typed error rather than reporting success.

---

### User Story 4 - Delete a registration (Priority: P3)

A developer needs to stop Mailgun from sending callbacks for a given event type — for example, when a
tracking integration is retired or an endpoint is decommissioned — so the application no longer
receives those events. They name the event type and the library removes its registration.

**Why this priority**: Deletion rounds out the CRUD lifecycle and is the least frequent operation
(most webhook churn is registration and inspection), but it is needed to cleanly retire an
integration. It reuses the same per-event-type routing as the other operations.

**Independent Test**: Against a fake transport, delete an existing event type's registration and
confirm the library issues the delete operation against that event type's endpoint; confirm that a
successful delete is reported as success and that deleting an event type with no registration
surfaces the typed error per the library's contract.

**Acceptance Scenarios**:

1. **Given** an event type with an existing registration, **When** the developer deletes it, **Then** the library issues the delete operation against exactly that event type's endpoint.
2. **Given** a successful deletion, **When** the response returns, **Then** the library reports success to the caller.
3. **Given** an event type with no registration (service responds not-found), **When** the developer deletes it, **Then** the library surfaces the single typed error carrying the status code and raw body.

---

### Edge Cases

- **No registrations configured**: listing a domain with no webhooks returns an empty result (no error, no follow-up request).
- **Read/update/delete of an unregistered event type**: surfaced as the standard typed error (status + body), never swallowed as a success or returned as a null/empty model.
- **Already-registered event type on create**: the library issues the create as requested and surfaces whatever the service returns (e.g. a conflict); it does not pre-check existence or de-duplicate client-side.
- **Supported event types only**: the library targets the closed set of supported event types — delivered, opened, clicked, permanent_fail (failed), temporary_fail, complained, unsubscribed; a caller cannot silently target an unrecognized event type.
- **Multiple URLs per event type**: where the service supports more than one callback URL for an event type, create and update carry all supplied URLs; supplying a single URL is the common case.
- **One URL across several event types**: registering a single URL for several event types in one call fans out to one create per event type, issued sequentially; on full success a registration exists for each named event type. **Partial failure** is fail-fast with no rollback — the first event type that returns non-2xx throws the single typed error and stops the remaining creates, leaving any already-created registrations in place (state may be partial).
- **Region/domain routing**: every operation is routed to the region-appropriate host and the configured domain, reusing the registered client's base configuration and authentication (feature 002); a region/domain mismatch surfaces as the service's response per the standard error contract.
- **Form requests, JSON responses**: create/update carry form-encoded fields (`id`/`url`) and every operation returns a JSON response body (deserialized via source generation); list, read-one, and delete carry no request body. This is distinct from the multipart message sends (003–006), and content handling here is independent of the message-building pipeline.
- **Cancellation**: any operation requested with a cancellation token that is cancelled stops promptly without completing the request.
- **Empty/invalid URL on create or update**: the library carries the caller-supplied URL(s) as given and surfaces whatever the service returns (e.g. validation rejection) as the typed error; it does not itself validate URL format beyond requiring at least one URL.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The library MUST expose domain webhook management as a distinct capability area on the client, mirroring the shape of the existing suppressions area (e.g. `client.Webhooks` alongside `client.Suppressions`), covering list, read-one, create, update, and delete of a domain's webhook registrations.
- **FR-002**: The library MUST support the closed set of supported event types — delivered, opened, clicked, permanent_fail (a.k.a. failed), temporary_fail, complained, and unsubscribed — and MUST expose them as an enumerated/typed set so callers select a supported event type rather than passing an arbitrary free-form string.
- **FR-003**: Creating a registration MUST issue the create operation against the domain's webhook endpoint, carrying the target event type and one or more callback URLs in a form-encoded request body (the v3 `id`/`url` fields), and MUST return the created registration to the caller.
- **FR-004**: The library MUST support registering a single callback URL across several event types in one call (the ergonomic default of the preferred surface). Because each create targets one event type, this call MUST fan out to one create operation per named event type, issued sequentially. The behavior on partial failure MUST be fail-fast with no rollback: the first non-2xx response MUST throw the single `MailgunnerException` and stop the remaining creates; registrations already created in that call MUST be left in place (the resulting state may be partial). On full success the result MUST reflect a registration established for each named event type.
- **FR-005**: The library MUST target the v3 webhook surface (`{base}/v3/{domain}/webhooks`), which supports more than one callback URL for a single event type (Mailgun caps it at 3); create and update MUST carry all supplied URLs, and the common single-URL case MUST also be supported. At least one URL MUST be supplied on create and update. (The wire surface is v3 per the constitution's Mailgun API Fidelity section — see Clarifications for why v3, not v4.)
- **FR-006**: Listing MUST issue the list operation against the domain's webhook endpoint and MUST return every configured event type's registration (event type plus its callback URL(s)) as typed models. A domain with no registrations MUST yield an empty result without error.
- **FR-007**: Reading a single registration MUST return the named event type's registration (its callback URL(s)) as a typed model. An event type with no registration MUST surface the single typed error (status code + raw body), not a null or empty success.
- **FR-008**: Updating a registration MUST issue the update operation against the named event type's endpoint carrying the new callback URL(s) in a form-encoded request body (the v3 `url` field(s)), and MUST return the updated registration. Updating an unregistered event type MUST surface the single typed error.
- **FR-009**: Deleting a registration MUST issue the delete operation against the named event type's endpoint. Deleting an unregistered event type MUST surface the single typed error rather than reporting success.
- **FR-010**: Webhook-management responses are JSON and MUST be deserialized with the library's standard `System.Text.Json` source-generation stack (trim/AOT-safe) per the project constitution. Create and update requests are form-encoded (the v3 contract's `id`/`url` fields), consistent with the library's existing form/multipart request handling; list, read-one, and delete carry no request body. Response (de)serialization MUST NOT use reflection-based `System.Text.Json`.
- **FR-011**: Every operation MUST route to the region-appropriate base host and the configured domain, and MUST authenticate using the shared typed HTTP client and the same Basic authentication as the rest of the library, reusing the registered client's base configuration (feature 002).
- **FR-012**: A non-success (non-2xx) response from any operation in this feature (list, read-one, create, update, delete) MUST surface the library's single typed error (`MailgunnerException`) exposing the HTTP status code and the raw response body, consistent with the rest of the library; the library MUST NOT introduce additional bespoke exception types.
- **FR-013**: Every public asynchronous method in this feature MUST accept a `CancellationToken` and MUST use `ConfigureAwait(false)` on awaits; a cancellation requested before completion MUST stop the operation promptly.
- **FR-014**: This capability MUST be independent of the sending pipeline (003–006) and of the signature-verification primitive (008): it MUST be developable, usable, and testable on its own, sharing only the registered client's base configuration (region/base URL and authentication) from feature 002.
- **FR-015**: All behavior in this feature MUST be verifiable offline against a fake transport, with assertions on: the create/list/read-one/update/delete wire format (method, path, form fields on create/update, and JSON-response projection) per event type; region and domain routing; registering one URL across multiple event types; the typed registration model fields; and the typed-error surfacing on non-success responses. No test may touch the real Mailgun service or any network.
- **FR-016**: The public surface added by this feature MUST carry XML documentation on every public type and member, and the change MUST be recorded in the CHANGELOG as a backward-compatible addition (SemVer MINOR).
- **FR-017**: This feature MUST NOT implement any out-of-scope surface: the Events/Logs/Metrics pull APIs, account-level (v1) webhooks, domain management (creating/configuring/deleting sending domains or their DNS), or parsing of inbound webhook payloads (signature verification remains the existing primitive in feature 008; typed payload parsing is a separate later feature).

### Key Entities *(include if feature involves data)*

- **Webhook registration**: A typed record associating a single supported event type with one or more callback URLs that Mailgun invokes when that event occurs for the domain. This is the unit returned by read-one and update, and the per-event-type element of a list result.
- **Event type**: One of the closed set of supported delivery events — delivered, opened, clicked, permanent_fail (failed), temporary_fail, complained, unsubscribed — that names a registration and routes per-event-type operations.
- **Callback URL**: A destination URL Mailgun invokes for an event type; a registration carries one or more. At least one is required on create and update.
- **Registration set**: The collection of all of a domain's event-type registrations returned by a list operation; may be empty when nothing is configured.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Registering a webhook for a single supported event type issues the create operation to the domain's webhook endpoint on the correct regional host, carries the event type and callback URL as form fields, and returns the created registration.
- **SC-002**: Registering one callback URL across several event types in a single call fans out to one create per event type and, on full success, establishes a registration for each named event type; on a partial failure it is fail-fast — the first non-2xx throws the single typed error, no further creates are issued, and earlier registrations are left in place (verified offline by asserting the number and order of create requests issued before the failure).
- **SC-003**: Listing a domain with several registered event types returns each event type's registration (with its callback URL(s)) as typed models; listing a domain with none returns an empty result without error.
- **SC-004**: Reading a single event type returns that registration's typed model for a registered event type and surfaces the typed error (status + body) for an unregistered one.
- **SC-005**: Updating an event type's registration issues the update operation to that event type's endpoint carrying the new URL(s) as form fields and returns the updated registration; updating an unregistered event type surfaces the typed error.
- **SC-006**: Deleting an event type issues the delete operation to that event type's endpoint and reports success; deleting an unregistered event type surfaces the typed error.
- **SC-007**: A non-2xx response on any list/read-one/create/update/delete operation surfaces the single typed error exposing the status code and raw response body, with no additional exception types introduced.
- **SC-008**: Every operation routes to the region-appropriate host and the configured domain and authenticates via the shared typed HTTP client, with no separate HTTP client constructed for this feature.
- **SC-009**: The entire feature is exercised by automated tests running offline against a fake transport, with no real network access and no dependency on the sending pipeline or the signature-verification primitive.
- **SC-010**: The added public surface is fully XML-documented and the change is recorded in the CHANGELOG as a SemVer MINOR (backward-compatible) addition.

## Assumptions

- This feature builds only on the registered client (feature 002), reusing its region/base-URL selection and Basic authentication; it does not depend on single-send (003), template (004), batch (005), send-options (006), suppressions (007), or signature verification (008), and is exercised on its own.
- The feature targets the **v3** webhook-management surface (`{base}/v3/{domain}/webhooks`), which associates one or more callback URLs with a single event type (Mailgun caps it at 3), with per-event-type `.../{name}` endpoints for read-one/update/delete and a `GET .../webhooks` list (resolved by clarification, Session 2026-06-24, and confirmed against the live Mailgun 2026 contract during `/speckit-plan`). Registering a single URL across multiple event types in one call is a client-side convenience that fans out to one v3 create per event type, sequentially, with fail-fast (no rollback) partial-failure semantics. **Constitution alignment**: the constitution (v1.4.0) mandates the v3 webhook endpoints. (A brief v3 → v4 amendment was reverted once verification showed the real v4 surface is URL-centric and lacks `GET`/`.../{name}` endpoints, so it cannot back these stories — see Clarifications.) Beyond the path version, this spec refers to the operations by capability ("create / list / read-one / update / delete a registration") rather than restating wire tokens, consistent with the other feature specs.
- Supported event types are a closed, enumerated set (delivered, opened, clicked, permanent_fail/failed, temporary_fail, complained, unsubscribed) exposed in a typed form so callers cannot silently target an unrecognized event type; "failed" and "permanent_fail" denote the same permanent-failure event.
- These endpoints return JSON response bodies, deserialized with the library's standard `System.Text.Json` source-generation stack per the constitution; create and update send form-encoded request fields (`id`/`url`) and list/read-one/delete send no body — distinct from the multipart message sends.
- The library does not pre-validate URL format, registration existence, or de-duplicate event types client-side beyond requiring at least one URL on create/update; service-side validation and semantics (including conflicts on an already-registered event type and not-found on read/update/delete) are surfaced through the standard typed-error contract.
- Delivery-status tracking is achieved through the push (webhooks) model registered here, never the pull (Events/Logs) model, consistent with the project constitution's scope discipline.
- The change is a backward-compatible public-surface addition (SemVer MINOR) with XML docs and a Keep a Changelog entry; it introduces no new runtime dependencies and no new exception types.
