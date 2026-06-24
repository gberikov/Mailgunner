# Feature Specification: Named Mailgunner Clients

**Feature Branch**: `012-named-clients`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "Add support for registering and resolving MULTIPLE named Mailgunner clients in one application, so a consumer can talk to several Mailgun domains (e.g. profitday.kz and profit.kz) or split transactional vs marketing traffic across subdomains, each with its own domain, sending key, region, and retry settings — from a single DI container."

## Clarifications

### Session 2026-06-24

- Q: How should a consumer resolve a named client at runtime? → A: A factory `IMailgunnerClientFactory.Get(name)` only — a single, documented, framework-version-stable resolution mechanism (no keyed-service resolution in this feature).
- Q: When only named clients are registered (no unnamed registration) and code requests a bare, unnamed client, what happens? → A: The bare unnamed client is not resolvable and the request fails with a clear error — no implicit default is selected.
- Q: Is binding named clients from a configuration section (e.g. appsettings.json) in scope, beyond the explicit-arguments and `Action<MailgunnerOptions>` forms? → A: Yes — a configuration-section binding overload for named clients is in scope.

## User Scenarios & Testing *(mandatory)*

<!--
  The "user" of this feature is the .NET application developer who consumes the
  Mailgunner library and wires it into their application's dependency-injection
  container. Stories are ordered by importance; each is independently testable
  and verifiable entirely offline.
-->

### User Story 1 - Register several named clients side by side (Priority: P1)

A developer needs to send mail from more than one Mailgun identity in the same application — for example one domain for transactional mail and another for marketing — so they register multiple named Mailgunner clients in a single container, each with its own domain, sending key, region, and retry settings. All registrations coexist; none silently overwrites another.

**Why this priority**: This is the core value of the feature. Today a second registration overwrites the first ("last-call-wins"), so two identities cannot be active simultaneously. Without independent coexisting registrations, none of the multi-domain scenarios are possible.

**Independent Test**: Register two named clients (e.g. "transactional" and "marketing") with distinct domains, keys, and regions into one container, build the container, and confirm both names are independently resolvable as ready clients — verifiable entirely offline.

**Acceptance Scenarios**:

1. **Given** a container, **When** the developer registers two clients under distinct names — each with its own domain, sending key, region, and retry settings — and builds the container, **Then** both named clients are independently resolvable as ready instances.
2. **Given** two named clients are registered, **When** the developer registers them using the explicit (domain, sending key, region) form, the configuration-callback form, or the configuration-section binding form, **Then** all three registration styles are available for named clients and produce equivalent, resolvable results.
3. **Given** a named client is registered, **When** it is resolved more than once, **Then** resolution succeeds consistently and yields a usable instance each time.

---

### User Story 2 - Resolve a specific client by name at runtime (Priority: P1)

At the point of sending, application code needs to pick which configured identity to use. The developer asks for a client by its registration name and receives a full Mailgunner client (sending and suppression capabilities) bound to that name's configuration.

**Why this priority**: Registering multiple clients is only useful if code can deterministically select one at runtime. This is the consumption half of Story 1 and is equally essential to the MVP.

**Independent Test**: Register two named clients, then at runtime resolve each by name and confirm the returned instance exposes the full client capability set (send + suppressions) — verifiable offline.

**Acceptance Scenarios**:

1. **Given** clients registered under names "transactional" and "marketing", **When** the developer calls the factory's `Get("transactional")`, **Then** a full Mailgunner client (send + suppressions) configured for that name is returned.
2. **Given** a resolved named client, **When** its capabilities are inspected, **Then** it offers the same complete capability surface as a client obtained from the existing unnamed registration.
3. **Given** clients registered under several names, **When** the developer resolves each name in turn, **Then** each resolution returns the client configured for exactly that name and no other.

---

### User Story 3 - Named clients are fully isolated from each other and from the unnamed client (Priority: P1)

A developer relies on each named client behaving strictly according to its own configuration: its own regional host and domain, its own authentication, and its own retry behavior. No setting from one named client — and nothing from the existing unnamed registration — leaks into another.

**Why this priority**: The deliverability motivation (separating transactional from marketing identities) collapses if authentication, region, or retry settings bleed across clients. Isolation is what makes multiple identities trustworthy, so it ranks alongside registration and resolution.

**Independent Test**: Register two named clients targeting different domains/regions with different keys (and optionally a separate unnamed client), drive a request from each via a fake transport, and confirm each request carries that client's own host, domain path, and authentication — verifiable offline.

**Acceptance Scenarios**:

1. **Given** two named clients configured for different regions and domains, **When** each issues a request observed through a fake transport, **Then** each request targets that client's own regional host and domain and carries HTTP Basic authentication derived from that client's own sending key.
2. **Given** named clients with different retry settings, **When** each client handles transient failures, **Then** each follows its own retry settings and one client's retry configuration does not affect another's.
3. **Given** both an unnamed registration and one or more named registrations in the same container, **When** each is resolved and exercised, **Then** the unnamed client and every named client operate independently with no cross-contamination of domain, region, authentication, or retry settings.

---

### User Story 4 - Backward compatibility for the existing unnamed registration (Priority: P2)

A developer who already uses the single unnamed registration upgrades the library and finds their existing code unchanged and fully working, optionally adding named clients alongside it without disturbing the unnamed one.

**Why this priority**: The change must be purely additive (SemVer MINOR). Existing consumers must not be forced to change anything. It is P2 because it constrains the design rather than delivering a new capability on its own.

**Independent Test**: Use the existing unnamed registration exactly as before, confirm the unnamed client resolves and behaves identically, then add a named registration and confirm the unnamed client is still resolvable and unchanged — verifiable offline.

**Acceptance Scenarios**:

1. **Given** existing code that uses the unnamed registration, **When** the library is upgraded, **Then** that code compiles and behaves exactly as before with no required changes.
2. **Given** an unnamed registration, **When** one or more named registrations are added in the same container, **Then** the unnamed client remains resolvable and unchanged.
3. **Given** only named registrations and no unnamed registration, **When** the container is built, **Then** the named clients work (resolvable by name via the factory) without requiring an unnamed client to also be registered, and a bare unnamed-client request fails with a clear error rather than resolving to a named client.

---

### User Story 5 - Invalid named configuration fails fast with a clear, secret-safe error (Priority: P2)

A developer who mistypes a name, reuses a name, leaves a name blank, gives a named client a bad domain/key/region, or asks for a name that was never registered is told clearly and early — without ever seeing a sending key in the message.

**Why this priority**: Named registration multiplies the ways configuration can go wrong (duplicate names, blank names, unknown lookups). Clear fail-fast errors prevent shipping a silently broken multi-identity setup, but it guards the core paths rather than being the core path, hence P2.

**Independent Test**: Attempt each invalid case — duplicate name, blank name, invalid per-name settings, and resolving an unknown name — and confirm each produces a clear error that names the problem and never reveals a sending key — verifiable offline.

**Acceptance Scenarios**:

1. **Given** two registrations using the same name, **When** the container is built, **Then** startup fails with a clear error identifying the duplicate name.
2. **Given** a registration with an empty, whitespace-only, or missing name, **When** the registration is made, **Then** it is rejected with a clear error identifying the name as the problem.
3. **Given** a named registration whose domain, sending key, or region is missing/blank/unrecognized, **When** the container is built, **Then** startup fails with a clear error that identifies both the offending name and the offending setting, and validation runs eagerly (validate-on-start) before any client is resolved or any network request is attempted.
4. **Given** a set of registered names, **When** the developer resolves a name that was never registered, **Then** resolution fails with a clear error that identifies the unknown name and, where helpful, distinguishes it from the available names.
5. **Given** any validation or resolution failure above, **When** the error is produced, **Then** the message, logs, and diagnostics never expose any sending key value.

---

### Edge Cases

- **Duplicate name**: Two registrations under the same name are a configuration error surfaced at container/host build time, not a silent last-call-wins overwrite (which remains the behavior only for the single unnamed registration).
- **Blank or whitespace-only name**: A name that is empty or only whitespace is treated as missing and rejected; surrounding whitespace around an otherwise valid name is not treated as part of the name.
- **Name casing / equivalence**: Names are compared as exact ordinal strings (case-sensitive); "Transactional" and "transactional" are two different names. This is documented so lookups are predictable.
- **Unknown name lookup**: Resolving a name that was never registered fails clearly rather than returning a null, empty, or default client.
- **Named vs unnamed collision**: The unnamed registration and named registrations occupy independent identities; a named client never shadows or is shadowed by the unnamed one.
- **Bare unnamed request with only named clients**: When no unnamed client is registered but one or more named clients are, requesting a bare unnamed client fails with a clear error rather than falling back to any named client; the consumer must resolve by name via the factory.
- **Region/domain mismatch (per name)**: As with the unnamed client, configuring a region that does not match a named client's domain is a documented runtime failure mode (the domain is not found on the mismatched host), not a registration-time error.
- **Secret hygiene across many keys**: With several sending keys present, no error, log, or diagnostic for any name may expose any key value.
- **Retry isolation**: A name configured with aggressive retries and a name configured with no/limited retries must each honor only their own retry budget.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The library MUST expose a named registration call that adds a Mailgunner client under a caller-supplied name, accepting a Mailgun domain, a sending key, and a region.
- **FR-002**: The library MUST also expose a configuration-callback variant of the named registration (a name plus a settings-configuration action) equivalent in capability to the explicit-arguments form.
- **FR-003**: The named registration MUST be callable multiple times with distinct names, and every such registration MUST coexist in the same container without overwriting another.
- **FR-004**: The library MUST provide a factory abstraction (`IMailgunnerClientFactory`) with a `Get(name)` operation to resolve a specific client by its registration name at runtime, returning a full Mailgunner client that exposes the complete capability surface (sending and suppressions). This factory is the single supported resolution mechanism for named clients; keyed-service resolution is NOT part of this feature.
- **FR-005**: Each named client MUST use its own managed HTTP transport with its own base URL and authentication, and its own retry settings, derived solely from that name's configuration.
- **FR-006**: A named client's outbound requests MUST target the regional host and domain configured for that name and MUST carry HTTP Basic authentication derived from that name's sending key (username `api`, password = that name's sending key).
- **FR-007**: Named clients MUST be mutually isolated: no domain, region, authentication, or retry setting from one named client may affect another named client.
- **FR-008**: Named clients and the existing unnamed client MUST be mutually isolated in the same way; registering named clients MUST NOT change the behavior of the unnamed client and vice versa.
- **FR-009**: The existing unnamed registration MUST continue to work unchanged; this feature MUST be purely additive (no breaking change to existing public types or signatures).
- **FR-010**: Per-name configuration MUST be validated eagerly when the container/host is built (validate-on-start), failing startup before any client is resolved or any network request is attempted.
- **FR-011**: The library MUST reject a registration whose name is missing, empty, or whitespace-only, with a clear error identifying the name as the cause.
- **FR-012**: The library MUST reject two registrations that use the same name, with a clear error identifying the duplicated name.
- **FR-013**: The library MUST reject a named registration whose domain, sending key, or region is missing, blank, or unrecognized, with a clear error that identifies both the offending name and the offending setting.
- **FR-014**: Resolving a name that was never registered MUST fail with a clear error identifying the unknown name, rather than returning a null/empty/default client.
- **FR-015**: All error messages, logs, and diagnostics produced by this feature MUST NOT expose any sending key value, for any name.
- **FR-016**: Configuration-validation failures (FR-011, FR-012, FR-013) MUST surface as standard .NET configuration/validation errors, NOT as the library's `MailgunnerException`, which remains reserved exclusively for HTTP API responses (carrying an HTTP status code and response body).
- **FR-017**: Name matching for registration and resolution MUST be exact and case-sensitive (ordinal), and this behavior MUST be documented.
- **FR-018**: The behavior of this feature MUST be verifiable without any real network calls, by substituting a fake transport that captures outbound requests per name.
- **FR-019**: Sending and suppression behavior and the on-the-wire request format MUST NOT change; this feature adds no new Mailgun endpoints and only introduces named registration and resolution.
- **FR-020**: The public surface added by this feature MUST be small, documented (XML docs on every public type and member), and recorded in the CHANGELOG as a SemVer MINOR (additive) change.
- **FR-021**: The library MUST also expose a named registration variant that binds a name's settings (domain, sending key, region, retry settings) from a configuration section (e.g. an `appsettings.json` section), in addition to the explicit-arguments (FR-001) and configuration-callback (FR-002) forms; all three forms MUST be subject to the same per-name validation (FR-010 through FR-013) and secret-hygiene rules (FR-015).
- **FR-022**: When only named clients are registered (no unnamed registration exists) and application code requests a bare, unnamed Mailgunner client, that request MUST NOT resolve to any named client; it MUST fail with a clear error rather than silently selecting a default. No implicit "default" named client is designated.

### Key Entities *(include if feature involves data)*

- **Client name**: A caller-supplied, non-blank, case-sensitive identifier under which a client's configuration is registered and by which it is later resolved. Names are unique within a container.
- **Named client configuration**: The per-name set of settings — domain, sending key (secret), region, and retry settings — that fully determines one named client's routing, authentication, and resilience, independently of any other client.
- **Named client resolver**: The factory abstraction (`IMailgunnerClientFactory`) whose `Get(name)` operation is the single supported way application code obtains the full Mailgunner client associated with a given name.
- **Mailgunner client**: The resolvable instance consumers obtain (named or unnamed); the single entry point that carries the configured authentication, regional routing, and retry behavior into every request, exposing both sending and suppression capabilities.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can register two or more clients under distinct names in one container and resolve each independently, with no registration overwriting another.
- **SC-002**: 100% of requests issued by a given named client target that name's configured regional host and domain and carry HTTP Basic authentication derived from that name's sending key.
- **SC-003**: For any pair of registered clients (named/named or named/unnamed) configured differently, 0% of one client's domain, region, authentication, or retry settings leak into the other.
- **SC-004**: 100% of existing unnamed-registration usage continues to compile and behave identically after upgrading, with no required code changes.
- **SC-005**: Every invalid case — blank name, duplicate name, invalid per-name domain/key/region — is rejected at container/host build time with an error that names the offending name (and setting, where applicable), before any network request is attempted.
- **SC-006**: Resolving an unregistered name fails 100% of the time with a clear error identifying the unknown name, never returning a null/empty/default client.
- **SC-007**: No sending key value, for any name, ever appears in any error message, log, or diagnostic emitted by the feature.
- **SC-008**: 100% of the feature's behavior is exercised by automated tests that make no real network calls (a fake transport stands in for the network), including assertions that each named client targets its own host/domain with its own authentication.

## Assumptions

- The consuming application uses the standard .NET dependency-injection container as its composition root; "registration" means an extension call against that container's service collection, consistent with the existing unnamed registration.
- "Region" remains the same closed set of supported values (US and EU) used by the unnamed client, each mapped to a distinct regional host; supplying a raw host string is out of scope.
- "Retry settings" refers to the same per-client retry/backoff configuration already supported for the unnamed client (introduced in the retry-with-backoff feature); each named client carries its own instance of those settings.
- The full capability surface returned by name resolution is the same Mailgunner client capability set already offered by the unnamed registration (sending and suppressions); this feature does not add or remove capabilities from that surface.
- "Validate-on-start" means validation runs eagerly when the service provider/host is built, throwing before any client is resolved — the same timing already used for unnamed-client validation.
- Last-call-wins remains the behavior of the single unnamed registration only; for named registrations, a repeated name is a duplicate-name error rather than an overwrite.
- Name comparison is ordinal (case-sensitive); this is a deliberate choice for predictable lookups and is documented.
- The sending key for each name is supplied through configuration/environment by the consumer and is treated as a secret; this feature does not persist or transmit it anywhere except as that name's request credential.
- This feature builds directly on the existing client-registration, regional-routing, authentication, and retry foundations; it reuses their validation rules per name rather than redefining them.
- Named clients are resolved exclusively through the `IMailgunnerClientFactory.Get(name)` factory abstraction; .NET keyed-service resolution is intentionally not offered in this feature, keeping a single documented resolution path.
- A configuration-section binding overload for named clients is in scope; it reads the same settings (domain, sending key, region, retry) a consumer would otherwise pass explicitly, and is governed by the same per-name validation and secret-hygiene rules. It introduces no new Mailgun endpoints or wire-format changes.
- There is no implicit "default" named client: when only named clients are registered, a bare unnamed-client request is a clear error, not a fallback to some default name.
