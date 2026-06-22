# Phase 1 Data Model: Client Registration & Regional Bootstrap

This feature is configuration-centric; the "data model" is the small set of public
configuration types plus the internal helpers that turn them into a configured client.
Field names below are the planned public contract.

---

## Entity: `MailgunRegion` (public enum)

The closed set of supported Mailgun hosting regions.

| Member | Maps to base URL |
|--------|------------------|
| `Us` | `https://api.mailgun.net/` |
| `Eu` | `https://api.eu.mailgun.net/` |

- No default member is assumed; an undefined value is a validation failure (FR-008).
- Namespace: `Mailgunner`.

---

## Entity: `MailgunnerOptions` (public options class)

The settings a consumer supplies at registration. Bound through the options pipeline so it can
also be populated from `IConfiguration`/environment.

| Field | Type | Required | Validation rule | Notes |
|-------|------|----------|-----------------|-------|
| `Domain` | `string` | Yes | Non-null, not empty, not whitespace-only (trimmed) | The Mailgun sending domain. Naming it in errors is allowed (not a secret). |
| `SendingKey` | `string` | Yes | Non-null, not empty, not whitespace-only (trimmed) | Secret credential. **Value MUST NOT appear in errors/logs/diagnostics** (FR-011). |
| `Region` | `MailgunRegion` | Yes | Must be a defined `MailgunRegion` value (`Enum.IsDefined`) | Selects the base URL. |

- Surrounding whitespace on `Domain`/`SendingKey` is trimmed before use; whitespace-only is
  treated as missing.
- Namespace: `Mailgunner`.
- Mutable settability is required for options binding; the type is a simple POCO.

**Validation outcomes** (all surface as `OptionsValidationException` at startup — clarification Q2):

| Condition | Result |
|-----------|--------|
| `Domain` missing/blank | Fail; message identifies the **domain** |
| `SendingKey` missing/blank | Fail; message identifies the **sending key** (no value echoed) |
| `Region` undefined | Fail; message identifies the **region** |
| Multiple invalid | Single aggregated failure listing each problem |

---

## Entity: `IMailgunnerClient` (public interface) + `MailgunnerClient` (internal impl)

The resolvable entry point obtained from the container.

- `IMailgunnerClient` — public, XML-documented foundation marker. Exposes **no operational
  members in this feature**; later features (sending, suppressions, webhooks) add them.
- `MailgunnerClient : IMailgunnerClient` — `internal sealed`. Constructed by
  `IHttpClientFactory` as a typed client; receives the configured `HttpClient` whose
  `BaseAddress` and `Authorization` header are already set.
  - `internal HttpClient HttpClient { get; }` — exposed to the test project via
    `InternalsVisibleTo("Mailgunner.Tests")` so tests can assert routing/auth. Not public.
- Lifetime: transient (default for typed clients), recreated per resolution with a pooled
  handler — consistent on every resolve (FR-002, US1 scenario 2).

---

## Internal helpers (not public surface)

| Type | Responsibility |
|------|----------------|
| `MailgunRegionEndpoints` (`internal static`) | Single source of truth mapping `MailgunRegion → Uri` base address. |
| `MailgunnerOptionsValidator` (`internal`, `IValidateOptions<MailgunnerOptions>`) | Enforces the validation rules above with clear, secret-safe messages. |

---

## Derived request attributes (observable contract)

These are not stored fields but the externally-observable result of registration, verified via
the fake transport:

| Attribute | Derivation |
|-----------|------------|
| Request host | `BaseAddress` = `MailgunRegionEndpoints[Region]` (EU→EU host, US→US host) — FR-004 |
| `Authorization` header | `Basic base64("api:" + SendingKey)` — FR-003 |

---

## State / lifecycle

There is no entity lifecycle (no persistence, no state machine). The only "transition" is
the one-time startup gate:

```
build container/host
   └─ IStartupValidator.Validate()
        ├─ all settings valid  → client resolvable & ready
        └─ any setting invalid → OptionsValidationException (startup fails, nothing resolved)
```
