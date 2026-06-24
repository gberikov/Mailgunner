using System.Net.Http.Headers;
using System.Text;
using Mailgunner;
using Mailgunner.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods that register the Mailgunner client into an <see cref="IServiceCollection"/>.
/// </summary>
public static class MailgunnerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Mailgunner client using explicit settings. Configuration is validated when
    /// the container/host starts; invalid settings fail startup with an
    /// <see cref="OptionsValidationException"/>.
    /// </summary>
    /// <param name="services">The service collection to add the client to.</param>
    /// <param name="domain">The Mailgun sending domain.</param>
    /// <param name="sendingKey">The Mailgun sending key (treated as a secret).</param>
    /// <param name="region">The Mailgun hosting region that selects the API base URL.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> for further configuration of the underlying typed client.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IHttpClientBuilder AddMailgunner(
        this IServiceCollection services, string domain, string sendingKey, MailgunRegion region)
    {
        Guard.NotNull(services, nameof(services));

        return services.AddMailgunner(options =>
        {
            options.Domain = domain;
            options.SendingKey = sendingKey;
            options.Region = region;
        });
    }

    /// <summary>
    /// Registers the Mailgunner client, configuring <see cref="MailgunnerOptions"/> via a delegate
    /// (which supports binding from configuration or environment). Configuration is validated when
    /// the container/host starts; invalid settings fail startup with an
    /// <see cref="OptionsValidationException"/>. Calling this more than once is allowed; the most
    /// recent settings take effect (last call wins).
    /// </summary>
    /// <remarks>
    /// The configured region and sending domain are independent. A region that does not match
    /// where the domain is hosted is accepted here but routes requests to a host where the domain
    /// returns HTTP 404.
    /// </remarks>
    /// <param name="services">The service collection to add the client to.</param>
    /// <param name="configure">A delegate that populates the <see cref="MailgunnerOptions"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> for further configuration of the underlying typed client.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IHttpClientBuilder AddMailgunner(
        this IServiceCollection services, System.Action<MailgunnerOptions> configure)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configure, nameof(configure));

        services.AddOptions<MailgunnerOptions>()
            .Configure(configure)
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MailgunnerOptions>, MailgunnerOptionsValidator>());

        services.TryAddSingleton(System.TimeProvider.System);
        services.TryAddSingleton<IRetryRandom, DefaultRetryRandom>();
        services.TryAddTransient<MailgunResilienceHandler>();

        return services.AddHttpClient<IMailgunnerClient, MailgunnerClient>(static (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<MailgunnerOptions>>().Value;
            client.BaseAddress = MailgunRegionEndpoints.ForRegion(options.Region);

            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{options.SendingKey.Trim()}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        })
        .AddHttpMessageHandler<MailgunResilienceHandler>();
    }

    /// <summary>
    /// Registers a <em>named</em> Mailgunner client using explicit settings, so several independently
    /// configured clients can coexist in one container (for example, different Mailgun domains or a
    /// transactional/marketing split). Resolve it at runtime with
    /// <see cref="IMailgunnerClientFactory.Get(string)"/>. Configuration is validated when the
    /// container/host starts; invalid settings fail startup with an
    /// <see cref="OptionsValidationException"/>.
    /// </summary>
    /// <param name="services">The service collection to add the client to.</param>
    /// <param name="name">The unique, non-blank, case-sensitive name to register the client under.</param>
    /// <param name="domain">The Mailgun sending domain.</param>
    /// <param name="sendingKey">The Mailgun sending key (treated as a secret).</param>
    /// <param name="region">The Mailgun hosting region that selects the API base URL.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> for further configuration of this name's typed client.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException"><paramref name="name"/> is blank, or already registered.</exception>
    public static IHttpClientBuilder AddMailgunner(
        this IServiceCollection services, string name, string domain, string sendingKey, MailgunRegion region)
    {
        Guard.NotNull(services, nameof(services));

        return services.AddMailgunner(name, options =>
        {
            options.Domain = domain;
            options.SendingKey = sendingKey;
            options.Region = region;
        });
    }

    /// <summary>
    /// Registers a <em>named</em> Mailgunner client, configuring its <see cref="MailgunnerOptions"/>
    /// via a delegate. Several distinct names can coexist in one container; resolve one at runtime with
    /// <see cref="IMailgunnerClientFactory.Get(string)"/>. Each name keeps its own typed
    /// <see cref="System.Net.Http.HttpClient"/>, base URL, authentication, and retry settings, fully
    /// isolated from other names and from the unnamed registration. Configuration is validated at
    /// startup.
    /// </summary>
    /// <param name="services">The service collection to add the client to.</param>
    /// <param name="name">The unique, non-blank, case-sensitive name to register the client under.</param>
    /// <param name="configure">A delegate that populates the <see cref="MailgunnerOptions"/> for this name.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> for further configuration of this name's typed client.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException"><paramref name="name"/> is blank, or already registered.</exception>
    public static IHttpClientBuilder AddMailgunner(
        this IServiceCollection services, string name, System.Action<MailgunnerOptions> configure)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configure, nameof(configure));

        ReserveName(services, name);
        services.AddOptions<MailgunnerOptions>(name).Configure(configure);
        return WireNamedClient(services, name);
    }

    /// <summary>
    /// Registers a <em>named</em> Mailgunner client, binding its <see cref="MailgunnerOptions"/> from a
    /// configuration section (for example an <c>appsettings.json</c> section). Several distinct names
    /// can coexist; resolve one at runtime with <see cref="IMailgunnerClientFactory.Get(string)"/>.
    /// Configuration is validated at startup.
    /// </summary>
    /// <param name="services">The service collection to add the client to.</param>
    /// <param name="name">The unique, non-blank, case-sensitive name to register the client under.</param>
    /// <param name="configuration">The configuration section bound to this name's <see cref="MailgunnerOptions"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> for further configuration of this name's typed client.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException"><paramref name="name"/> is blank, or already registered.</exception>
    public static IHttpClientBuilder AddMailgunner(
        this IServiceCollection services, string name, IConfiguration configuration)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configuration, nameof(configuration));

        ReserveName(services, name);
        services.AddOptions<MailgunnerOptions>(name).Bind(configuration);
        return WireNamedClient(services, name);
    }

    /// <summary>
    /// Validates a client name and reserves it, rejecting blank or already-registered names eagerly at
    /// registration time. The reservation lives in a shared <see cref="NamedClientRegistry"/> singleton.
    /// </summary>
    private static void ReserveName(IServiceCollection services, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new System.ArgumentException(
                "A Mailgunner client name must be provided and cannot be blank.", nameof(name));
        }

        var registry = GetOrAddRegistry(services);
        if (!registry.Add(name))
        {
            throw new System.ArgumentException(
                $"A Mailgunner client is already registered under the name '{name}'.", nameof(name));
        }
    }

    /// <summary>
    /// Wires the per-name validation, typed <see cref="System.Net.Http.HttpClient"/> (regional base URL
    /// + HTTP Basic auth from this name's options), per-name resilience handler, and the resolver
    /// factory. The name must already be reserved via <see cref="ReserveName"/>.
    /// </summary>
    private static IHttpClientBuilder WireNamedClient(IServiceCollection services, string name)
    {
        services.AddOptions<MailgunnerOptions>(name).ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MailgunnerOptions>, MailgunnerOptionsValidator>());

        services.TryAddSingleton(System.TimeProvider.System);
        services.TryAddSingleton<IRetryRandom, DefaultRetryRandom>();
        services.TryAddSingleton<IMailgunnerClientFactory, MailgunnerClientFactory>();

        return services.AddHttpClient(NamedClientRegistry.HttpClientName(name), (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<MailgunnerOptions>>().Get(name);
            client.BaseAddress = MailgunRegionEndpoints.ForRegion(options.Region);

            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{options.SendingKey.Trim()}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        })
        .AddHttpMessageHandler(provider =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<MailgunnerOptions>>().Get(name);
            var timeProvider = provider.GetRequiredService<System.TimeProvider>();
            var logger = provider.GetRequiredService<ILogger<MailgunResilienceHandler>>();
            var random = provider.GetRequiredService<IRetryRandom>();
            return new MailgunResilienceHandler(timeProvider, options.Retry, logger, random);
        });
    }

    /// <summary>
    /// Returns the container's shared <see cref="NamedClientRegistry"/>, adding one as a singleton on
    /// first use. Looked up on the <see cref="IServiceCollection"/> (not a built provider) so it is
    /// shared across every named registration call and resolvable by the factory at runtime.
    /// </summary>
    private static NamedClientRegistry GetOrAddRegistry(IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(NamedClientRegistry)
                && services[i].ImplementationInstance is NamedClientRegistry existing)
            {
                return existing;
            }
        }

        var registry = new NamedClientRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}
