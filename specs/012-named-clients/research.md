# Phase 0 Research: Named Mailgunner Clients

All decisions resolve the spec's clarified scope into an implementation strategy that stays additive and constitution-compliant. There were no open `NEEDS CLARIFICATION` markers entering Phase 0 (the three ambiguities were resolved in `/speckit-clarify`); the research below records the design choices those clarifications imply.

## D1 — Resolution mechanism: factory only

- **Decision**: Expose a single public interface `IMailgunnerClientFactory` with `IMailgunnerClient Get(string name)`. No `[FromKeyedServices]` / keyed-service registration.
- **Rationale**: Clarified answer (factory only). Keyed DI requires `Microsoft.Extensions.DependencyInjection` 8+ APIs that do **not** exist on `netstandard2.0`, a current target framework — a keyed path could not compile for all targets without `#if` divergence in the public surface. A single factory is a small, stable, framework-version-independent, easily-testable contract (Principle IV) and is what the original request led with.
- **Alternatives considered**: Keyed services only (rejected: netstandard2.0 gap, awkward typed-`HttpClient` interplay, attribute-based injection); both factory + keyed (rejected: larger public surface to document/support for no clarified need).

## D2 — Per-name wiring: named options + named typed HttpClient

- **Decision**: Each named registration calls `services.AddOptions<MailgunnerOptions>(name).Configure(...)/.Bind(...).ValidateOnStart()` and `services.AddHttpClient(NamedClientRegistry.HttpClientName(name), (sp, client) => { /* base URL + Basic auth from monitor.Get(name) */ }).AddHttpMessageHandler(sp => /* per-name resilience handler */)`. The factory builds a `MailgunnerClient` from `IHttpClientFactory.CreateClient(HttpClientName(name))` and `IOptionsMonitor<MailgunnerOptions>.Get(name)`.
- **Rationale**: This is the idiomatic IHttpClientFactory pattern for "many clients of the same type" (Principle II). Named options give per-name isolation of domain/key/region/retry for free; `IOptionsMonitor.Get(name)` reads the right instance. Reusing the existing `MailgunnerClient` and `MailgunnerOptionsValidator` keeps behavior and wire format unchanged (FR-019).
- **HttpClient name mapping**: Logical name `n` → HttpClient name `"Mailgunner:" + n`, to avoid colliding with any client name the consumer registers themselves and with the unnamed typed-client registration.
- **Alternatives considered**: Registering `IMailgunnerClient` as a keyed/enumerable service (rejected: last-wins or netstandard2.0 gaps); a custom per-name `HttpClient` built by hand (rejected: violates Principle II).

## D3 — Per-name resilience without disturbing the unnamed path

- **Decision**: Add an internal constructor `MailgunResilienceHandler(TimeProvider, RetryPolicyOptions, ILogger<MailgunResilienceHandler>, IRetryRandom)`; the existing `IOptions<MailgunnerOptions>` constructor delegates to it (`options.Value.Retry`). The unnamed registration keeps using the `IOptions` constructor via `TryAddTransient` + `AddHttpMessageHandler<MailgunResilienceHandler>` **unchanged**. Named registrations construct the handler in a lambda passing `monitor.Get(name).Retry`.
- **Rationale**: Keeps the unnamed code path byte-for-byte identical (lowest regression risk, backward compatibility FR-009) while letting each name honor its own `RetryPolicyOptions` (FR-005, FR-007). `RetryPolicyOptions` is not a DI-registered service, so DI activation of the handler unambiguously selects the `IOptions` constructor — no constructor-selection conflict.
- **Alternatives considered**: A name-aware handler that reads `IOptionsMonitor` itself (rejected: the handler can't know "its" name when resolved by `AddHttpMessageHandler<T>`); per-name `IOptionsSnapshot` injection (rejected: snapshot is scoped, handlers are not inherently scoped).

## D4 — Name registry, duplicate/blank/unknown handling

- **Decision**: Introduce an internal `NamedClientRegistry` (a singleton holding an `ordinal` `HashSet<string>`). It is get-or-added to the `IServiceCollection` during the first named registration and mutated as names are added.
  - **Blank name** → `ArgumentException` thrown immediately by the registration overload (FR-011).
  - **Duplicate name** → registry `Add` returns false → `ArgumentException` thrown immediately by the registration overload, naming the duplicate (FR-012). This is "at composition time", before the provider is built.
  - **Unknown name at `Get`** → registry lookup misses → `ArgumentException` naming the unknown name and (where helpful) listing registered names (FR-014). Never returns null/default.
- **Rationale**: A registry is the only reliable way to distinguish "registered" from "never registered" because `IHttpClientFactory.CreateClient(unknownName)` silently returns a default client rather than failing. Immediate throw on duplicate/blank gives the clearest, earliest feedback and is simpler than a custom `IValidateOptions` cross-check. Name comparison is `StringComparer.Ordinal` (FR-017).
- **Alternatives considered**: Detect duplicates via a marker options/validator at `ValidateOnStart` (rejected: later and less direct than an immediate throw; equivalent guarantee with more moving parts). Returning null for unknown names (rejected by spec).

## D5 — No implicit default; bare unnamed request fails

- **Decision**: Named registrations never register the default (unnamed) `IMailgunnerClient` service. If only named clients are registered and code injects a bare `IMailgunnerClient`, the standard DI "no service registered" error occurs (FR-022). No "default name" concept exists.
- **Rationale**: Clarified answer. Honors isolation (FR-008) and avoids accidental cross-use of transactional/marketing identities. The default DI resolution error is already clear and actionable; no extra machinery needed.
- **Alternatives considered**: Designating a default name / first-registered-as-default (both rejected in clarification for surprise and isolation-violation risk).

## D6 — Configuration-section binding overload (and its dependency)

- **Decision**: Add `AddMailgunner(this IServiceCollection, string name, IConfiguration configuration)` that does `AddOptions<MailgunnerOptions>(name).Bind(configuration).ValidateOnStart()` and wires the named HttpClient/handler. This requires adding the `Microsoft.Extensions.Options.ConfigurationExtensions` package reference.
- **Rationale**: Clarified answer (config-binding IN scope). Binding via `OptionsBuilder.Bind` is the idiomatic path and reuses the existing validator and `MailgunnerOptions` shape (including the nested `Retry` section). The package is first-party and tiny.
- **Constitution note**: This is a new runtime dependency (Principle I) — flagged in the plan's Constitution Check and justified in Complexity Tracking. It must be surfaced for explicit reviewer/user acceptance. If rejected, this single overload is dropped and the feature still satisfies FR-001/FR-002 (explicit + callback forms) with no new dependency.
- **Alternatives considered**: Manual binding from `IConfiguration` without the binder package (rejected: still needs a Configuration abstractions package and re-implements binder/enum/type-conversion semantics by hand).

## D7 — Public API shape (overloads, return type)

- **Decision**: Three named overloads, each mirroring an unnamed counterpart and each returning `IHttpClientBuilder` (the named HttpClient builder):
  1. `AddMailgunner(string name, string domain, string sendingKey, MailgunRegion region)`
  2. `AddMailgunner(string name, Action<MailgunnerOptions> configure)`
  3. `AddMailgunner(string name, IConfiguration configuration)`
- **Rationale**: Arities are distinct from the unnamed overloads, so there is no resolution ambiguity. Returning `IHttpClientBuilder` lets consumers (and tests) further configure the per-name transport — crucially `ConfigurePrimaryHttpMessageHandler(...)` to attach a fake handler per name for network-free assertions (Principle III, FR-018, SC-008).
- **Alternatives considered**: A fluent `MailgunnerBuilder` wrapper (rejected: larger surface than needed); returning `IServiceCollection` (rejected: blocks per-name transport customization and per-name fake-handler tests).
