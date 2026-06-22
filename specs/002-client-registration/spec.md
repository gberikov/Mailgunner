# Feature Specification: Client Registration & Regional Bootstrap

**Feature Branch**: `002-client-registration`

**Created**: 2026-06-22

**Status**: Draft

**Input**: User description: "A consumer registers the Mailgunner client into a .NET DI container with a single call, supplying their Mailgun domain, a sending key, and a region (US or EU). After registration, resolving the client yields a ready instance whose requests target the correct regional base URL and carry HTTP Basic authentication derived from the sending key. This is the entry point every other capability builds on. Acceptance criteria: Valid settings make the client resolvable from the container. EU region routes to the EU host, US to the US host; mismatching a domain's region is a documented failure mode. Empty/missing domain or sending key is rejected at startup with a clear error. No real network calls; behavior verified with a fake transport. Out of scope: sending a message."

## Clarifications

### Session 2026-06-22

- Q: When exactly should invalid configuration be rejected? → A: Eagerly when the DI container/host is built (validate-on-start); building the provider throws before anything is resolved.
- Q: What kind of error should invalid configuration throw? → A: A standard .NET configuration/validation error; the library's single `MailgunnerException` stays reserved for HTTP API responses only.
- Q: What happens if the registration call is made more than once? → A: Last call wins — the most recent registration's settings take effect and the client stays resolvable.

## User Scenarios & Testing *(mandatory)*

<!--
  The "user" of this feature is the .NET application developer who consumes the
  Mailgunner library and wires it into their application's dependency-injection
  container. Stories are ordered by importance; each is independently testable.
-->

### User Story 1 - Register and resolve a ready client (Priority: P1)

A developer adds Mailgunner to their application by making a single registration call, supplying their Mailgun domain, a sending key, and a region. When the application later resolves the Mailgunner client from its container, it receives a fully configured, ready-to-use instance without any additional manual wiring.

**Why this priority**: This is the entry point every other Mailgunner capability builds on. Without a resolvable, correctly configured client, no sending, suppression, or webhook feature can function. It alone delivers the foundational value: "the client is installed and ready."

**Independent Test**: Register the client with valid settings into a container, build the container, resolve the client, and confirm a non-null, ready instance is returned — verifiable entirely offline.

**Acceptance Scenarios**:

1. **Given** a container and valid settings (a non-empty domain, a non-empty sending key, and a supported region), **When** the developer makes the single registration call and then resolves the client, **Then** a ready client instance is returned.
2. **Given** the client has been registered, **When** it is resolved more than once, **Then** resolution succeeds consistently and yields a usable instance each time.
3. **Given** a registered client, **When** its outbound requests are inspected via a fake transport, **Then** each request carries HTTP Basic authentication derived from the configured sending key (username `api`, password = sending key) and no request is sent over a real network.

---

### User Story 2 - Requests target the correct regional host (Priority: P2)

A developer whose Mailgun account is hosted in a specific region (US or EU) configures that region at registration time, and trusts that every request the client makes is addressed to the matching regional host.

**Why this priority**: Routing to the wrong region is a silent-but-fatal misconfiguration: the account's domain will not be found on the other region's host. Correct regional routing is essential for any real request to succeed, but it depends on Story 1 (a resolvable client) existing first.

**Independent Test**: Register the client with the EU region and, separately, with the US region; in each case inspect the request target via a fake transport and confirm it points at the corresponding regional host — verifiable offline.

**Acceptance Scenarios**:

1. **Given** the client is registered with the EU region, **When** it issues a request, **Then** the request targets the EU host.
2. **Given** the client is registered with the US region, **When** it issues a request, **Then** the request targets the US host.
3. **Given** a developer's domain belongs to one region but they configure the other region, **When** they consult the documentation, **Then** the mismatch is described as a known failure mode (the domain will not be found on the mismatched host), so the developer can diagnose it.

---

### User Story 3 - Invalid configuration fails fast with a clear error (Priority: P2)

A developer who forgets to supply a domain or sending key, or supplies an empty/blank value, is told clearly and early — before any request is attempted — that the configuration is invalid, rather than discovering the problem through an obscure runtime failure.

**Why this priority**: Fail-fast validation prevents shipping a silently broken integration and turns a confusing late failure into an obvious early one. It is a quality-of-life guard around Story 1 rather than the core path, hence P2.

**Independent Test**: Attempt to register/activate the client with a missing or blank domain, and separately with a missing or blank sending key; confirm each attempt is rejected with a clear, actionable error that names the offending setting — verifiable offline.

**Acceptance Scenarios**:

1. **Given** settings with an empty, whitespace-only, or missing domain, **When** the developer registers and the configuration is validated at startup, **Then** registration/activation fails with a clear error identifying the domain as the problem.
2. **Given** settings with an empty, whitespace-only, or missing sending key, **When** the developer registers and the configuration is validated at startup, **Then** registration/activation fails with a clear error identifying the sending key as the problem.
3. **Given** invalid configuration, **When** the failure occurs, **Then** it occurs before any network request is attempted.

---

### Edge Cases

- **Region/domain mismatch**: A developer configures a region that does not match where their domain is hosted. The client is still constructed (the values are individually valid); requests are routed to the configured region's host, where the domain will not be found. This is a documented failure mode, not a registration-time error.
- **Whitespace-only values**: A domain or sending key consisting only of whitespace is treated as missing and rejected, not accepted as a valid value.
- **Surrounding whitespace**: Leading/trailing whitespace around an otherwise valid domain or sending key is not treated as part of the value.
- **Region not specified or unrecognized**: If a region value cannot be interpreted as a supported region, this is surfaced as a clear configuration error rather than silently defaulting.
- **Repeated registration**: If a developer calls the registration more than once, the most recent call's settings take effect (last-call-wins); the client remains resolvable with those values rather than ending in a broken or ambiguous state.
- **Secret hygiene**: The sending key is a secret; it must not be exposed in error messages, logs, or diagnostic output produced by this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The library MUST expose a single registration call that adds the Mailgunner client to a consumer's dependency-injection container, accepting a Mailgun domain, a sending key, and a region.
- **FR-002**: After a successful registration, the consumer MUST be able to resolve a ready-to-use Mailgunner client from the container with no further manual configuration.
- **FR-003**: The resolved client MUST attach HTTP Basic authentication to its outbound requests, derived from the configured sending key (username `api`, password = the sending key).
- **FR-004**: The client MUST direct its requests to the regional host that corresponds to the configured region — the EU host for the EU region and the US host for the US region.
- **FR-005**: The library MUST reject registration/activation when the domain is missing, empty, or whitespace-only, surfacing a clear error that identifies the domain as the cause.
- **FR-006**: The library MUST reject registration/activation when the sending key is missing, empty, or whitespace-only, surfacing a clear error that identifies the sending key as the cause.
- **FR-007**: Configuration validation MUST run eagerly when the dependency-injection container/host is built (validate-on-start), causing container/host startup to fail before any client is resolved and before any network request is attempted.
- **FR-008**: The library MUST surface an unrecognized or unspecified region as a clear configuration error rather than silently choosing a default region.
- **FR-009**: The behavior of this feature MUST be verifiable without any real network calls, by substituting a fake transport that captures outbound requests.
- **FR-010**: The published documentation MUST describe the region/domain mismatch as a known failure mode and explain how it manifests (the domain is not found on the mismatched regional host).
- **FR-011**: Error messages, logs, and diagnostics produced by this feature MUST NOT expose the sending key value.
- **FR-012**: Sending an actual message is OUT OF SCOPE for this feature; this feature delivers only registration, configuration validation, regional routing, and authentication wiring as the foundation other capabilities build on.
- **FR-013**: Configuration-validation failures (FR-005, FR-006, FR-008) MUST surface as a standard .NET configuration/validation error, NOT as the library's `MailgunnerException`; `MailgunnerException` remains reserved exclusively for HTTP API responses (carrying an HTTP status code and response body).
- **FR-014**: If the registration call is made more than once, the most recent registration's settings MUST take effect (last-call-wins) and the resolved client MUST remain usable.

### Key Entities *(include if feature involves data)*

- **Client settings**: The set of values a consumer supplies at registration — the Mailgun domain (the sending domain), the sending key (a secret credential), and the region. These together determine how the client authenticates and where it routes.
- **Region**: A bounded choice between the two supported Mailgun hosting regions, US and EU, each mapped to a distinct regional host.
- **Mailgunner client**: The resolvable instance consumers obtain from the container; it is the single entry point that carries the configured authentication and regional routing into every request.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can go from zero to a resolvable, ready client with exactly one registration call supplying domain, sending key, and region.
- **SC-002**: 100% of requests issued by a US-configured client target the US host, and 100% of requests issued by an EU-configured client target the EU host.
- **SC-003**: 100% of requests issued by the client carry HTTP Basic authentication derived from the configured sending key.
- **SC-004**: Every invalid-configuration case (missing/blank domain, missing/blank sending key, unrecognized region) is rejected at startup with an error that names the offending setting, before any network request is attempted.
- **SC-005**: 100% of the feature's behavior is exercised by automated tests that make no real network calls (a fake transport stands in for the network).
- **SC-006**: The sending key value never appears in any error message, log, or diagnostic emitted by the feature.

## Assumptions

- The consuming application uses the standard .NET dependency-injection container as its composition root; "registration" means an extension call against that container's service collection.
- "Region" is modeled as a closed set of supported values (US and EU) rather than a free-form host string; supplying a host directly is out of scope for this feature.
- The two regional hosts are the current Mailgun endpoints: US `https://api.mailgun.net` and EU `https://api.eu.mailgun.net` (per the project's API-fidelity baseline).
- A region/domain mismatch is a configuration error on the consumer's side that the service surfaces at request time (as a not-found response from the mismatched host); it is therefore documented rather than blocked at registration, since the individual values are each well-formed.
- "Rejected at startup" means validation runs eagerly when the container/host is built (validate-on-start): building the service provider / starting the host throws, before any client is resolved.
- A single default client registration is assumed; multiple independently-configured clients (named/keyed registrations) are out of scope for this feature.
- The sending key is supplied through configuration/environment by the consumer and is treated as a secret; this feature does not persist or transmit it anywhere except as the request credential.
