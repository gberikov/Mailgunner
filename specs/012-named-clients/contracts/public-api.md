# Public API Contract: Named Mailgunner Clients

Additive surface only (SemVer MINOR). Nothing existing is removed or changed. All new public types/members carry XML docs (Principle IV).

## New public type: `IMailgunnerClientFactory`

Namespace: `Mailgunner`

```csharp
namespace Mailgunner;

/// <summary>
/// Resolves a registered, named <see cref="IMailgunnerClient"/> at runtime. Obtain it from the
/// dependency-injection container when more than one Mailgunner client is registered.
/// </summary>
public interface IMailgunnerClientFactory
{
    /// <summary>
    /// Gets the fully configured client registered under <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The registration name (compared case-sensitively, ordinal).</param>
    /// <returns>A ready <see cref="IMailgunnerClient"/> (send + suppressions) bound to that name's
    /// domain, region, authentication, and retry settings.</returns>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="name"/> is null/empty/whitespace, or no client is registered under it.
    /// </exception>
    IMailgunnerClient Get(string name);
}
```

**Contract notes**

- `Get` NEVER returns null and NEVER returns a default/fallback client.
- Errors are `ArgumentException` (a standard .NET error), NOT `MailgunnerException` (reserved for HTTP responses).
- The error message for an unknown name names the unknown name and lists registered names; it NEVER contains a sending key.

## New public registration overloads

Namespace: `Microsoft.Extensions.DependencyInjection` (same class `MailgunnerServiceCollectionExtensions` as the existing unnamed methods)

```csharp
// 1. Explicit settings
public static IHttpClientBuilder AddMailgunner(
    this IServiceCollection services,
    string name, string domain, string sendingKey, MailgunRegion region);

// 2. Configuration callback
public static IHttpClientBuilder AddMailgunner(
    this IServiceCollection services,
    string name, System.Action<MailgunnerOptions> configure);

// 3. Configuration-section binding  (requires Microsoft.Extensions.Options.ConfigurationExtensions)
public static IHttpClientBuilder AddMailgunner(
    this IServiceCollection services,
    string name, Microsoft.Extensions.Configuration.IConfiguration configuration);
```

**Contract notes**

- All three register the factory (`TryAddSingleton<IMailgunnerClientFactory, MailgunnerClientFactory>()`), an entry in the name registry, named `MailgunnerOptions` with `ValidateOnStart`, and a named typed `HttpClient` with the per-name resilience handler.
- Return type `IHttpClientBuilder` is the **named** client's builder, enabling per-name transport customization (e.g. `ConfigurePrimaryHttpMessageHandler` for tests).
- `name` null/empty/whitespace → `ArgumentException` immediately.
- Duplicate `name` (any overload) → `ArgumentException` immediately, naming the duplicate.
- `services`/`configure`/`configuration` null → `ArgumentNullException`.
- Per-name validation failures (blank domain/key, undefined region, bad retry bounds) → `OptionsValidationException` at host start (`ValidateOnStart`), secret-safe.
- These overloads do NOT register the unnamed `IMailgunnerClient`; injecting a bare `IMailgunnerClient` when only named clients exist fails with the standard DI resolution error.

## Backward-compatibility invariants

- Existing `AddMailgunner(domain, sendingKey, region)` and `AddMailgunner(Action<MailgunnerOptions>)` are unchanged in signature and behavior (including last-call-wins for the unnamed registration).
- Unnamed and named registrations coexist with full mutual isolation of domain, region, auth, and retry.

## Test contract (network-free)

For each named client, attach a fake `HttpMessageHandler` via the returned builder and assert:
- request `RequestUri` host matches the name's region (`api.mailgun.net` vs `api.eu.mailgun.net`) and path carries the name's domain;
- `Authorization` is `Basic base64("api:" + sendingKey)` for that name;
- two names configured differently do not bleed host/domain/auth/retry into each other;
- duplicate/blank name throws at registration; unknown name throws at `Get`; no error string contains a sending key.
