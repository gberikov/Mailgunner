using Microsoft.Extensions.Options;

namespace Mailgunner.Internal;

/// <summary>
/// Default <see cref="IMailgunnerClientFactory"/>. Resolves a named client by combining that name's
/// typed <see cref="HttpClient"/> (from <see cref="IHttpClientFactory"/>) with its named
/// <see cref="MailgunnerOptions"/>, after confirming the name was registered. Registered as a
/// singleton; the clients it returns are lightweight wrappers over factory-managed HTTP clients.
/// </summary>
internal sealed class MailgunnerClientFactory : IMailgunnerClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<MailgunnerOptions> _options;
    private readonly NamedClientRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="MailgunnerClientFactory"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The factory that builds each name's configured typed client.</param>
    /// <param name="options">The monitor used to read each name's options.</param>
    /// <param name="registry">The registry of names that were registered.</param>
    public MailgunnerClientFactory(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<MailgunnerOptions> options,
        NamedClientRegistry registry)
    {
        Guard.NotNull(httpClientFactory, nameof(httpClientFactory));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(registry, nameof(registry));

        _httpClientFactory = httpClientFactory;
        _options = options;
        _registry = registry;
    }

    /// <inheritdoc />
    public IMailgunnerClient Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new System.ArgumentException(
                "A Mailgunner client name must be provided and cannot be blank.", nameof(name));
        }

        if (!_registry.Contains(name))
        {
            var registered = _registry.RegisteredNames;
            var detail = registered.Count == 0
                ? "No named Mailgunner clients are registered."
                : $"Registered names: {string.Join(", ", registered)}.";
            throw new System.ArgumentException(
                $"No Mailgunner client is registered under the name '{name}'. {detail}", nameof(name));
        }

        var httpClient = _httpClientFactory.CreateClient(NamedClientRegistry.HttpClientName(name));
        var options = _options.Get(name);
        return new MailgunnerClient(httpClient, Options.Create(options));
    }
}
