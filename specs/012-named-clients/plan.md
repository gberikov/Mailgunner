# Implementation Plan: Named Mailgunner Clients

**Branch**: `012-named-clients` | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/012-named-clients/spec.md`

## Summary

Add additive, named registration and resolution of multiple Mailgunner clients in one DI container so a consumer can serve several Mailgun domains / split transactional vs marketing traffic, each with its own domain, sending key, region, and retry settings. Technical approach: keep the existing unnamed `AddMailgunner` untouched and layer named registrations on top using **named** `IOptions<MailgunnerOptions>` + **named** typed `HttpClient` (`IHttpClientFactory.CreateClient(name)`), exposing a single public resolution mechanism — `IMailgunnerClientFactory.Get(name)`. A small internal name registry detects duplicate/blank names at registration and unknown names at resolution; per-name validation runs at host start (`ValidateOnStart`). No keyed-service resolution (incompatible with the `netstandard2.0` target and an unnecessary second public path).

## Technical Context

**Language/Version**: C# (latest lang version), `nullable` enabled, `TreatWarningsAsErrors`

**Primary Dependencies**: `Microsoft.Extensions.Http` (IHttpClientFactory, named clients), `Polly` (resilience), `System.Text.Json`. **New (this feature)**: `Microsoft.Extensions.Options.ConfigurationExtensions` — required only by the configuration-section binding overload (FR-021); see Constitution Check + Complexity Tracking.

**Storage**: N/A (stateless HTTP client library)

**Testing**: xUnit, network-free via a fake `HttpMessageHandler` attached per named client through the returned `IHttpClientBuilder`

**Target Platform**: `net8.0` and `netstandard2.0` (multi-target; keyed DI is unavailable on `netstandard2.0`, reinforcing the factory choice)

**Project Type**: Single .NET class library (`src/Mailgunner`) with a test project (`tests/Mailgunner.Tests`)

**Performance Goals**: N/A — registration/resolution wiring only; no hot path changes; on-the-wire format unchanged

**Constraints**: Purely additive (SemVer MINOR); no change to sending/suppression behavior or wire format; no new Mailgun endpoints; secret-safe diagnostics; one error contract (`MailgunnerException` reserved for HTTP responses; configuration/lookup failures are standard .NET exceptions)

**Scale/Scope**: Small — one new public interface, three named registration overloads, two internal types, one internal refactor of the resilience handler constructor

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Minimal Dependencies & Modern .NET | ⚠️ Flagged (justified) | The config-section overload (FR-021) needs `Microsoft.Extensions.Options.ConfigurationExtensions` — a new runtime dependency. Per Principle I this is a constitution-level decision; justified in Complexity Tracking. All other work uses only existing permitted deps. |
| II. Managed HTTP & Resilience | ✅ Pass | Every named client uses a typed `HttpClient` from `IHttpClientFactory.CreateClient(name)`; no manual `HttpClient` lifetime management. Each name keeps its own Polly resilience pipeline honoring its own `RetryPolicyOptions`; `CancellationToken` + `ConfigureAwait(false)` unchanged. |
| III. Test-First, Network-Free Tests | ✅ Pass | New behavior lands with xUnit tests using a fake `HttpMessageHandler` attached per name. No new live test; the single gated sandbox test is unaffected. CI stays green without credentials. |
| IV. Documented, Strict Public API | ✅ Pass | New public surface is small (`IMailgunnerClientFactory`, registration overloads) with full XML docs. SemVer MINOR + CHANGELOG entry. Config/lookup failures surface as `OptionsValidationException`/`ArgumentException`, NOT `MailgunnerException`. |
| V. Security & Scope Discipline | ✅ Pass | No secrets in code/tests/fixtures; per-name keys via options only. Validator and all new error messages contain names only — never a sending key. Scope stays within messages/suppressions; no new endpoints. |

**Gate result**: PASS with one justified dependency deviation (Principle I) recorded in Complexity Tracking. No unjustified violations.

## Project Structure

### Documentation (this feature)

```text
specs/012-named-clients/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── public-api.md     # Phase 1 output: the added public surface contract
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Mailgunner/
├── IMailgunnerClient.cs                         # (unchanged) full client surface returned by Get(name)
├── IMailgunnerClientFactory.cs                  # NEW public: Get(name) resolution
├── MailgunnerOptions.cs                          # (unchanged) per-name configuration shape
├── MailgunnerClient.cs                           # (unchanged) constructed per-name in the factory
├── DependencyInjection/
│   └── MailgunnerServiceCollectionExtensions.cs  # MODIFIED: add 3 named AddMailgunner overloads + factory registration
└── Internal/
    ├── MailgunnerClientFactory.cs                # NEW internal: IMailgunnerClientFactory impl
    ├── NamedClientRegistry.cs                    # NEW internal: registered-name set + HttpClient-name mapping
    ├── MailgunResilienceHandler.cs               # MODIFIED: add internal ctor taking RetryPolicyOptions (per-name)
    └── MailgunnerOptionsValidator.cs             # (unchanged) already validates by name

tests/Mailgunner.Tests/
└── Registration/                                 # NEW tests: named registration, resolution, isolation, errors
```

**Structure Decision**: Single-project library; the feature is concentrated in the DI extension class plus two small internal helpers and one public interface. The existing unnamed path in `MailgunnerServiceCollectionExtensions` is preserved verbatim; named overloads are added beside it.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| New runtime dependency `Microsoft.Extensions.Options.ConfigurationExtensions` (Principle I) | Required to implement the in-scope configuration-section binding overload (FR-021): the `IConfiguration` type and `OptionsBuilder.Bind` live outside the three currently permitted packages. | (a) Hand-roll binding without a package — still needs `Microsoft.Extensions.Configuration.Abstractions` for the `IConfiguration` type, and re-implementing binder semantics (nested `Retry` section, type conversion, enum parsing) is more code, more bugs, and non-idiomatic. (b) Drop the config-section overload — contradicts the clarified scope (config-binding is IN scope). The chosen package is a tiny, first-party Microsoft extension already present in virtually every host/DI app, preserves the public contract, and is the idiomatic binding path. |
