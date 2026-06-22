using System.Net.Http.Headers;
using System.Text;
using Mailgunner;
using Mailgunner.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        return services.AddHttpClient<IMailgunnerClient, MailgunnerClient>(static (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<MailgunnerOptions>>().Value;
            client.BaseAddress = MailgunRegionEndpoints.ForRegion(options.Region);

            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{options.SendingKey.Trim()}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        });
    }
}
