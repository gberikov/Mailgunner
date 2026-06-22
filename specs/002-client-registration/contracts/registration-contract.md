# Contract: Registration Surface & Observable HTTP Behavior

This is the public contract this feature must satisfy. It is the interface other features and
consumers build on. Method bodies are intentionally omitted (implementation belongs to
`tasks.md`); signatures, namespaces, and observable behavior are the contract.

---

## 1. Public types

### `Mailgunner.MailgunRegion` (enum)

```csharp
namespace Mailgunner;

/// <summary>The Mailgun hosting region that determines the API base URL.</summary>
public enum MailgunRegion
{
    /// <summary>United States region — https://api.mailgun.net.</summary>
    Us,
    /// <summary>European Union region — https://api.eu.mailgun.net.</summary>
    Eu,
}
```

### `Mailgunner.MailgunnerOptions` (options POCO)

```csharp
namespace Mailgunner;

/// <summary>Settings used to register and configure the Mailgunner client.</summary>
public sealed class MailgunnerOptions
{
    /// <summary>The Mailgun sending domain (for example, "mg.example.com"). Required.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>The Mailgun sending key used for HTTP Basic authentication. Required; treated as a secret.</summary>
    public string SendingKey { get; set; } = string.Empty;

    /// <summary>The Mailgun hosting region that selects the API base URL. Required.</summary>
    public MailgunRegion Region { get; set; }
}
```

### `Mailgunner.IMailgunnerClient` (entry point)

```csharp
namespace Mailgunner;

/// <summary>
/// The Mailgunner client resolved from the dependency-injection container. This is the entry
/// point every other Mailgunner capability builds on; operational members are added by later
/// features.
/// </summary>
public interface IMailgunnerClient
{
}
```

### `Microsoft.Extensions.DependencyInjection.MailgunnerServiceCollectionExtensions`

```csharp
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods that register the Mailgunner client into an <see cref="IServiceCollection"/>.</summary>
public static class MailgunnerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Mailgunner client using explicit settings. Configuration is validated when
    /// the container/host starts; invalid settings throw <see cref="Microsoft.Extensions.Options.OptionsValidationException"/>.
    /// </summary>
    /// <returns>An <see cref="IHttpClientBuilder"/> for further configuration of the underlying typed client.</returns>
    public static IHttpClientBuilder AddMailgunner(
        this IServiceCollection services, string domain, string sendingKey, MailgunRegion region);

    /// <summary>
    /// Registers the Mailgunner client, configuring <see cref="MailgunnerOptions"/> via a delegate
    /// (supports binding from configuration/environment). Validated at startup.
    /// </summary>
    /// <returns>An <see cref="IHttpClientBuilder"/> for further configuration of the underlying typed client.</returns>
    public static IHttpClientBuilder AddMailgunner(
        this IServiceCollection services, Action<MailgunnerOptions> configure);
}
```

---

## 2. Behavioral contract

| ID | Given | When | Then |
|----|-------|------|------|
| C-01 | Valid domain, key, region | `AddMailgunner(...)` then build provider then resolve `IMailgunnerClient` | A non-null, ready client is returned (FR-001, FR-002). |
| C-02 | Registered client | Resolve more than once | Each resolution yields a usable client (US1-2). |
| C-03 | Region = `Eu` | Client issues a request | Request URI host is `api.eu.mailgun.net` (FR-004). |
| C-04 | Region = `Us` | Client issues a request | Request URI host is `api.mailgun.net` (FR-004). |
| C-05 | Any valid registration | Client issues a request | Request carries `Authorization: Basic base64("api:" + sendingKey)` (FR-003). |
| C-06 | `Domain` null/empty/whitespace | Container/host is built (startup validation) | `OptionsValidationException` whose message identifies the **domain**; no resolution, no network (FR-005, FR-007, FR-013). |
| C-07 | `SendingKey` null/empty/whitespace | Container/host is built | `OptionsValidationException` identifying the **sending key**; the key value is **absent** from the message (FR-006, FR-011, FR-013). |
| C-08 | `Region` undefined value | Container/host is built | `OptionsValidationException` identifying the **region** (FR-008, FR-013). |
| C-09 | `AddMailgunner` called twice with different settings | Build & resolve | The second call's settings take effect; client resolvable (FR-014). |
| C-10 | Any registration/validation failure | — | The error is `OptionsValidationException`, never `MailgunnerException` (clarification Q2, FR-013). |
| C-11 | Any of the above | Test run | No real network call occurs; a fake `HttpMessageHandler` stands in (FR-009). |

---

## 3. Documented failure mode (not enforced here)

**Region/domain mismatch** — a domain hosted in one region configured with the other region is
accepted at registration (both values are individually valid) and routed to the configured
region's host, where Mailgun returns **404** because the domain is not found there. This is
documented (README / XML docs) as a known misconfiguration, per FR-010. It is **not** a
startup validation failure.

---

## 4. Out of scope (explicit)

- Sending a message (no `Send*` members in this feature).
- Polly resilience / retry wiring (provisioned for the sending feature).
- Named/keyed multiple client registrations.
- Suppressions and webhooks.
