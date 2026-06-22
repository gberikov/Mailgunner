# Implementation Plan: Client Registration & Regional Bootstrap

**Branch**: `002-client-registration` (feature dir `002-client-registration`) | **Date**: 2026-06-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/002-client-registration/spec.md`

## Summary

Deliver the **entry point** of the Mailgunner library: a single dependency-injection
registration call that accepts a Mailgun domain, a sending key, and a region (US or EU),
and yields a resolvable, ready-to-use client. The resolved client is a typed `HttpClient`
(obtained via `IHttpClientFactory`) whose `BaseAddress` is the region's host and whose
requests carry HTTP Basic authentication (`api` : sending key). Configuration is validated
eagerly at host/container startup using options validation (`ValidateOnStart`); invalid
domain/key/region fails startup with a standard .NET configuration error, never the
library's `MailgunnerException` (reserved for HTTP responses). All behavior is verified
offline through a fake `HttpMessageHandler`. Sending a message is out of scope; this
feature is the foundation later capabilities build on.

## Technical Context

**Language/Version**: C# (`LangVersion=latest`), `Nullable=enable` — inherited from
`Directory.Build.props`.

**Primary Dependencies**: `Microsoft.Extensions.Http` (permitted by constitution Principle I;
already pinned at 10.0.9 in `Directory.Packages.props`). It transitively provides
`Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options`
(where `ValidateOnStart`/`IStartupValidator` now live — see research). **No new dependency is
introduced.** `Polly` is NOT referenced by this feature (no outbound HTTP/retry behavior yet);
it is provisioned for the sending feature.

**Storage**: N/A (library; no persistence).

**Testing**: xUnit, fully offline. HTTP routing/auth is exercised via a fake
`HttpMessageHandler` injected through `IHttpClientBuilder.ConfigurePrimaryHttpMessageHandler`.
Startup validation is exercised by resolving `IStartupValidator` and calling `Validate()`
(exactly what a host does) — no real host and no network required.

**Target Platform**: Cross-platform .NET. Library multi-targets `net8.0` (primary) and
`netstandard2.0` (compatibility); the test project runs on `net8.0`.

**Project Type**: Single class-library + test project (NuGet-distributable library).

**Performance Goals**: N/A. Registration is allocation-light and synchronous; validation is
O(1) over three fields.

**Constraints**: Offline tests (no network, no credentials); warnings-as-errors; XML docs
required on every public member; English-only artifacts; minimal dependency footprint; the
sending key MUST NOT appear in any error/log/diagnostic; multi-target compatible
(`net8.0;netstandard2.0`).

**Scale/Scope**: One default client registration. Named/keyed multi-client registrations,
message sending, suppressions, and webhooks are out of scope (separate features).

**Key environment facts (verified 2026-06):**
- `ValidateOnStart()` and `IStartupValidator` were decoupled from hosting
  (dotnet/runtime #84347) and ship in the **`Microsoft.Extensions.Options`** assembly as of
  .NET 8 — transitively available via `Microsoft.Extensions.Http`. No
  `Microsoft.Extensions.Hosting` reference is needed.
- A host triggers startup validation by resolving `IStartupValidator` and calling
  `Validate()`, which throws `OptionsValidationException` on failure. Tests do the same to
  assert fail-fast deterministically.
- `Microsoft.Extensions.Http` 10.0.x targets `netstandard2.0`; `AuthenticationHeaderValue`,
  `Convert.ToBase64String`, `Encoding.ASCII`, and `Enum.IsDefined` are all available on
  `netstandard2.0` — the design holds on both TFMs with no polyfill.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Constitution v1.1.0 (2026-06-22). Gates derived from its principles:

| Principle | Gate for this feature | Status |
|-----------|------------------------|--------|
| I. Minimal Dependencies & Modern .NET | Adds only `Microsoft.Extensions.Http` (already permitted & pinned); no `Newtonsoft.Json`/FluentEmail; JSON not used here. | ✅ PASS |
| II. Managed HTTP & Resilience | Outbound HTTP flows through a typed `HttpClient` via `IHttpClientFactory` (`AddHttpClient<IMailgunnerClient, MailgunnerClient>`); library never `new`s `HttpClient`. No public async method or outbound call exists yet, so Polly resilience and `CancellationToken`/`ConfigureAwait(false)` obligations attach to the sending feature. | ✅ PASS (typed-client foundation; resilience provisioned) |
| III. Test-First, Network-Free (NON-NEGOTIABLE) | All behavior covered by xUnit via a fake `HttpMessageHandler`; this feature satisfies the constitution's explicitly-required "region-based base-URL selection" coverage. No credentials, no network. | ✅ PASS |
| IV. Documented, Strict Public API | Every public type/member (`MailgunRegion`, `MailgunnerOptions`, `IMailgunnerClient`, `AddMailgunner`) carries XML docs; `Nullable`/`TreatWarningsAsErrors`/docs inherited. Config failures use the framework `OptionsValidationException`, NOT `MailgunnerException` — preserving the single-typed-exception contract (one exception for HTTP responses, which this feature does not introduce). | ✅ PASS |
| V. Security & Scope Discipline | No secrets in code/tests/fixtures; sending key supplied only via options/config; validator messages and diagnostics never echo the key; scope strictly registration/validation/routing/auth. | ✅ PASS |
| Mailgun API Fidelity | HTTP Basic auth username `api`, password = sending key; base URL by region — US `https://api.mailgun.net`, EU `https://api.eu.mailgun.net`; region/base-URL mismatch yields 404 and is documented as a failure mode. | ✅ PASS |
| Dev Workflow & Quality Gates | Conventional Commits; `dotnet build`/`dotnet test` green offline; no secrets committed. | ✅ PASS |

**Result:** No violations. Complexity Tracking not required.

**Note on the empty `IMailgunnerClient` surface (Principle IV):** The client interface
intentionally exposes no operational members in this feature — it is the resolvable entry
point that later features (sending, suppressions, webhooks) extend. It is XML-documented as
the foundation type; the underlying typed `HttpClient` is exposed only `internal`ly (via
`InternalsVisibleTo`) so tests can assert routing/auth without a public network method.

## Project Structure

### Documentation (this feature)

```text
specs/002-client-registration/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (config/types model)
├── quickstart.md        # Phase 1 output (validation/run guide)
├── contracts/
│   └── registration-contract.md   # Phase 1 output (public DI surface + observable HTTP contract)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
└── Mailgunner/
    ├── MailgunRegion.cs                 # public enum { Us, Eu }
    ├── MailgunnerOptions.cs             # public options: Domain, SendingKey, Region
    ├── IMailgunnerClient.cs             # public resolvable entry point (foundation marker)
    ├── MailgunnerClient.cs              # internal typed-client impl wrapping HttpClient
    ├── Internal/
    │   ├── MailgunRegionEndpoints.cs    # internal region → base Uri mapping
    │   └── MailgunnerOptionsValidator.cs# internal IValidateOptions<MailgunnerOptions>
    ├── DependencyInjection/
    │   └── MailgunnerServiceCollectionExtensions.cs  # public AddMailgunner (ns Microsoft.Extensions.DependencyInjection)
    └── (existing) MailgunnerInfo.cs     # scaffold placeholder; add [assembly: InternalsVisibleTo("Mailgunner.Tests")]

tests/
└── Mailgunner.Tests/
    ├── Fakes/
    │   └── CapturingHttpMessageHandler.cs   # fake transport: captures the outbound request, returns a canned response
    └── Registration/
        ├── ClientResolutionTests.cs         # US1: resolvable, ready, repeated-resolve, last-call-wins
        ├── AuthenticationTests.cs           # US1: HTTP Basic auth header on requests
        ├── RegionRoutingTests.cs            # US2: EU→EU host, US→US host
        └── ConfigurationValidationTests.cs  # US3: missing/blank domain, missing/blank key, unrecognized region fail at startup
```

**Structure Decision**: Keep the established single-library `src/`+`tests/` layout. Public
types live at the `Mailgunner` namespace root; the DI extension lives in the
`Microsoft.Extensions.DependencyInjection` namespace (idiomatic discoverability —
`AddMailgunner` surfaces wherever `IServiceCollection` is in scope). Implementation details
(`MailgunRegionEndpoints`, the options validator, `MailgunnerClient`) are `internal`, with
`InternalsVisibleTo("Mailgunner.Tests")` granting the test project access to assert routing
and auth on the underlying `HttpClient`. `Mailgunner.csproj` gains a single
`PackageReference Include="Microsoft.Extensions.Http"` (version centrally managed).

## Complexity Tracking

> No constitution violations — section intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
