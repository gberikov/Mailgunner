# Phase 0 Research: Client Registration & Regional Bootstrap

All Technical Context unknowns are resolved below. No `NEEDS CLARIFICATION` markers remain.

---

## R1. Where do `ValidateOnStart` / `IStartupValidator` live, and can we use them without `Microsoft.Extensions.Hosting`?

**Decision**: Use `services.AddOptions<MailgunnerOptions>().Configure(...).Validate(...).ValidateOnStart()`.
Rely on `IStartupValidator` (resolved and invoked by the host at startup; invoked directly by
tests) to enforce fail-fast configuration validation.

**Rationale**: As of .NET 8, `ValidateOnStart()` and the `IStartupValidator` interface were
decoupled from the hosting layer (dotnet/runtime issue #84347) and now ship in the
**`Microsoft.Extensions.Options`** assembly (`Assembly: Microsoft.Extensions.Options.dll`).
`Microsoft.Extensions.Http` (a permitted, already-pinned dependency) depends transitively on
`Microsoft.Extensions.Options`, so these APIs are available **with no new dependency** and
**without** referencing `Microsoft.Extensions.Hosting`. A host triggers validation by
resolving `IStartupValidator` and calling `Validate()`, which throws
`OptionsValidationException` if any registered `IValidateOptions<T>` fails. Tests reproduce
exactly this to assert startup failure deterministically and offline.

**Alternatives considered**:
- *Validate inside `AddMailgunner` synchronously* — rejected: when settings are bound from
  `IConfiguration`/environment the values are not known at registration time; it also bypasses
  the standard options pipeline. (The clarification chose validate-on-start, not at the call.)
- *Validate lazily on first resolve via the factory* — rejected by the spec clarification
  (must fail when the container/host is built, before resolution).
- *Reference `Microsoft.Extensions.Hosting` for `ValidateOnStart`* — rejected: unnecessary
  on .NET 8+ and would violate the minimal-dependency principle.

---

## R2. How should the typed client be registered and authenticated?

**Decision**: Register `AddHttpClient<IMailgunnerClient, MailgunnerClient>((sp, client) => { … })`.
In the configure delegate, read the validated `MailgunnerOptions`, set
`client.BaseAddress` to the region's host, and set
`client.DefaultRequestHeaders.Authorization` to
`new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{key}")))`.
`AddMailgunner` returns the `IHttpClientBuilder` so consumers (and tests) can chain further
handler configuration.

**Rationale**: Constitution Principle II requires all outbound HTTP to flow through a typed
`HttpClient` from `IHttpClientFactory`; `AddHttpClient<TClient,TImpl>` is the canonical
pattern. Setting `BaseAddress` + `DefaultRequestHeaders.Authorization` means **every** request
the client issues inherits the correct host and Basic auth — these defaults are copied onto
each `HttpRequestMessage` before it enters the handler pipeline, so a fake primary handler
captures them. Returning `IHttpClientBuilder` is the idiomatic seam that lets tests inject a
fake transport via `ConfigurePrimaryHttpMessageHandler`.

**Alternatives considered**:
- *A `DelegatingHandler` that stamps the `Authorization` header per request* — viable and
  slightly "purer", but adds a type and indirection for no behavioral gain here; the default
  header is simpler and equally testable. May be revisited if per-request auth variation is
  ever needed.
- *Manually constructing `HttpClient`* — forbidden by Principle II.

---

## R3. How is the region mapped to a base URL?

**Decision**: An internal static map `MailgunRegionEndpoints` resolves
`MailgunRegion.Us → https://api.mailgun.net` and `MailgunRegion.Eu → https://api.eu.mailgun.net`
(trailing slash included so relative request URIs combine correctly). An unrecognized/undefined
region value is treated as a validation failure (see R4), not a silent default.

**Rationale**: Matches the constitution's Mailgun API Fidelity section verbatim. Keeping the
map internal and centralized means later features reuse one source of truth. The
region/domain mismatch (e.g., an EU domain configured as US) is **not** a registration error —
the values are individually valid — so it surfaces at request time as a Mailgun 404 and is
documented as a known failure mode (spec FR-010).

**Alternatives considered**:
- *Accept a free-form base-URL string* — out of scope per spec Assumptions; a closed enum
  prevents typos and keeps the surface small.

---

## R4. How are domain / sending key / region validated, with clear, secret-safe messages?

**Decision**: An internal `MailgunnerOptionsValidator : IValidateOptions<MailgunnerOptions>`:
- Fails when `Domain` is null/empty/whitespace → message names the **domain**.
- Fails when `SendingKey` is null/empty/whitespace → message names the **sending key** and
  **never includes its value**.
- Fails when `Region` is not a defined `MailgunRegion` (`Enum.IsDefined`) → message names the
  **region**.
Trim surrounding whitespace before use so a padded-but-otherwise-valid value is accepted; a
whitespace-only value is treated as missing. Aggregate all failures into one
`ValidateOptionsResult` so the consumer sees every problem at once.

**Rationale**: A dedicated `IValidateOptions<T>` gives precise, testable messages and runs
through `ValidateOnStart`. Naming the offending setting satisfies FR-005/006/008; excluding the
key value satisfies FR-011/Principle V. The resulting failure is the framework's
`OptionsValidationException` (clarification Q2), keeping `MailgunnerException` reserved for HTTP
responses.

**Alternatives considered**:
- *DataAnnotations (`[Required]`)* — rejected: weaker message control, no native
  whitespace-only/enum-defined semantics, and risks reflecting member values into messages.

---

## R5. What does "last-call-wins" require?

**Decision**: No special handling. Both the options `Configure` actions and the typed-client
configure delegates registered by repeated `AddMailgunner` calls run in registration order;
later writes overwrite earlier ones for the same properties, so the most recent call's domain,
key, and region take effect and the client stays resolvable.

**Rationale**: This is the default behavior of the options and `IHttpClientFactory` pipelines;
it satisfies FR-014 (last-call-wins) without guard code. A test asserts it explicitly.

**Alternatives considered**:
- *Throw on duplicate registration* — rejected by clarification Q3 (last-call-wins chosen as
  least-surprising for DI `Add…` semantics).

---

## R6. How do offline tests observe routing and auth without a public send method?

**Decision**: A `CapturingHttpMessageHandler` (test fake) records the `HttpRequestMessage` and
returns a canned `200`. Tests inject it via
`AddMailgunner(...).ConfigurePrimaryHttpMessageHandler(() => fake)`, resolve
`IMailgunnerClient`, and have the test drive a throwaway request through the client's
`internal HttpClient` (exposed via `InternalsVisibleTo`). They then assert the captured
request's absolute URI host (EU/US) and `Authorization` header. Validation tests resolve
`IStartupValidator` and assert `Validate()` throws `OptionsValidationException`.

**Rationale**: Sending is out of scope, so there is no public network method to call; inspecting
the configured `HttpClient` and a captured request via a fake transport proves the exact
externally-observable contract (FR-003/004/009) with zero network and zero credentials —
satisfying Principle III.

**Alternatives considered**:
- *Add a temporary public "ping" method* — rejected: pollutes the public surface for a test
  affordance. `InternalsVisibleTo` keeps the surface clean.
