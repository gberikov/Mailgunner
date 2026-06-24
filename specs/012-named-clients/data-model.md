# Phase 1 Data Model: Named Mailgunner Clients

This feature is wiring, not persistence — the "entities" are configuration and resolution constructs, not stored records.

## Client name

- **What**: A caller-supplied identifier under which a client's configuration is registered and by which it is later resolved.
- **Representation**: `string`.
- **Rules**:
  - MUST NOT be null, empty, or whitespace-only (FR-011) → `ArgumentException` at registration.
  - MUST be unique within a container (FR-012) → duplicate → `ArgumentException` at registration.
  - Compared with `StringComparer.Ordinal` (case-sensitive) for both registration and resolution (FR-017).
- **Maps to**: the named-options name, and (via `"Mailgunner:" + name`) the named `HttpClient`.

## Named client configuration (`MailgunnerOptions`, per name)

- **What**: The full per-name settings determining one client's routing, auth, and resilience. Reuses the existing `MailgunnerOptions` type unchanged.
- **Fields** (existing): `Domain` (string, required, non-blank), `SendingKey` (string, required, non-blank, secret), `Region` (`MailgunRegion`, must be defined), `Retry` (`RetryPolicyOptions`, non-null; `MaxRetryAttempts >= 0`, `BaseDelay > 0`, `MaxSingleWait >= BaseDelay`).
- **Validation**: The existing `MailgunnerOptionsValidator` (`IValidateOptions<MailgunnerOptions>`) already validates **per name** and is secret-safe (never echoes `SendingKey`). Triggered eagerly via `.ValidateOnStart()` on each named options builder (FR-010, FR-013).
- **Isolation**: One `MailgunnerOptions` instance per name; read via `IOptionsMonitor<MailgunnerOptions>.Get(name)`. No sharing between names or with the unnamed instance (FR-007, FR-008).

## Named client registry (`NamedClientRegistry`, internal)

- **What**: The single source of truth for "which names are registered".
- **Representation**: Internal singleton holding a `HashSet<string>` (ordinal). Get-or-added to the `IServiceCollection` on first named registration; mutated as names are added.
- **Operations**:
  - `Add(name)` → returns false if already present (drives duplicate detection at registration).
  - `Contains(name)` → drives unknown-name detection at resolution.
  - `RegisteredNames` → ordered snapshot used to enrich the unknown-name error message.
  - `static HttpClientName(name)` → `"Mailgunner:" + name`.
- **Lifecycle**: Created at registration, consumed by the factory at runtime.

## Named client resolver (`IMailgunnerClientFactory`, public)

- **What**: The single supported runtime resolution mechanism.
- **Operation**: `IMailgunnerClient Get(string name)`.
  - Rejects null/blank name → `ArgumentException`.
  - Rejects unknown name (not in registry) → `ArgumentException` (lists registered names), never null/default (FR-014).
  - Otherwise returns a `MailgunnerClient` built from `IHttpClientFactory.CreateClient(HttpClientName(name))` + `IOptionsMonitor.Get(name)`.
- **Implementation**: internal `MailgunnerClientFactory` (singleton), depends on `IHttpClientFactory`, `IOptionsMonitor<MailgunnerOptions>`, `NamedClientRegistry`.

## Mailgunner client (`IMailgunnerClient` / `MailgunnerClient`)

- **What**: The resolvable instance (named or unnamed). Unchanged type; exposes the full surface (send + suppressions) (FR-004). For named use it is constructed per call from the named HttpClient and named options; the existing constructor `(HttpClient, IOptions<MailgunnerOptions>)` is reused by wrapping the named options via `Options.Create(monitor.Get(name))`.

## Relationships

```text
AddMailgunner(name, …)  ──registers──▶  Named MailgunnerOptions(name)  ──validated by──▶  MailgunnerOptionsValidator
        │                                        ▲
        ├──registers──▶ Named HttpClient("Mailgunner:"+name) ──+ per-name── MailgunResilienceHandler(RetryPolicyOptions)
        └──adds name to──▶ NamedClientRegistry
                                   ▲
IMailgunnerClientFactory.Get(name) ┘ ──reads──▶ HttpClient + Options ──builds──▶ IMailgunnerClient (send + suppressions)
```
