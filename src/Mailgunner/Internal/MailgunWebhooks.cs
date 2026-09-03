namespace Mailgunner.Internal;

/// <summary>
/// Default <see cref="IMailgunWebhooks"/> implementation. Issues the v3 domain-webhook requests over the
/// client's configured <see cref="HttpClient"/> (region base URL + Basic auth) and the
/// trimmed sending domain. Create and update send <c>multipart/form-data</c> (<c>id</c>/<c>url</c>) parts;
/// list, read-one, and delete carry no body. Responses are JSON, deserialized with the source-generated
/// <see cref="WebhookJsonContext"/> and projected to <see cref="WebhookRegistration"/>. Any non-success
/// response surfaces the single <see cref="MailgunnerException"/>.
/// </summary>
internal sealed class MailgunWebhooks : IMailgunWebhooks
{
    private readonly HttpClient _httpClient;
    private readonly string _domain;

    /// <summary>Initializes a new instance of the <see cref="MailgunWebhooks"/> class.</summary>
    /// <param name="httpClient">The configured typed HTTP client (region base URL + Basic auth).</param>
    /// <param name="domain">The sending domain (already trimmed).</param>
    public MailgunWebhooks(HttpClient httpClient, string domain)
    {
        _httpClient = httpClient;
        _domain = domain;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookRegistration>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var (status, body) = await MailgunHttp.SendAsync(
            _httpClient,
            new HttpRequestMessage(HttpMethod.Get, RootUri()),
            cancellationToken).ConfigureAwait(false);

        var dto = MailgunHttp.Deserialize(body, WebhookJsonContext.Default.WebhookListDto, status);
        var result = new List<WebhookRegistration>();
        if (dto?.Webhooks is not null)
        {
            foreach (var pair in dto.Webhooks)
            {
                var eventType = WebhookEventTypes.TryParseToken(pair.Key);
                var urls = pair.Value?.Urls;
                if (eventType is null || urls is null || urls.Count == 0)
                {
                    continue;
                }

                result.Add(new WebhookRegistration(eventType.Value, urls));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<WebhookRegistration> GetAsync(
        WebhookEventType eventType,
        CancellationToken cancellationToken = default)
    {
        var uri = ItemUri(eventType);
        var (status, body) = await MailgunHttp.SendAsync(
            _httpClient,
            new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken).ConfigureAwait(false);

        return ProjectEnvelope(eventType, status, body);
    }

    /// <inheritdoc />
    public async Task<WebhookRegistration> CreateAsync(
        WebhookEventType eventType,
        IEnumerable<string> urls,
        CancellationToken cancellationToken = default)
    {
        var list = ValidateUrls(urls, nameof(urls));
        var token = WebhookEventTypes.ToToken(eventType);

        var content = new MultipartFormDataContent
        {
            { new StringContent(token), "id" },
        };
        foreach (var url in list)
        {
            content.Add(new StringContent(url), "url");
        }

        var (status, body) = await MailgunHttp.SendAsync(
            _httpClient,
            new HttpRequestMessage(HttpMethod.Post, RootUri()) { Content = content },
            cancellationToken).ConfigureAwait(false);

        return ProjectEnvelope(eventType, status, body, fallbackUrls: list);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookRegistration>> CreateAsync(
        IEnumerable<WebhookEventType> eventTypes,
        string url,
        CancellationToken cancellationToken = default)
    {
        if (eventTypes is null)
        {
            throw new ArgumentException("At least one event type is required.", nameof(eventTypes));
        }

        var single = ValidateUrls(new[] { url }, nameof(url));

        // Distinct, in first-seen order: a second create for the same event type is rejected by the service.
        var types = new List<WebhookEventType>();
        var seen = new HashSet<WebhookEventType>();
        foreach (var eventType in eventTypes)
        {
            if (seen.Add(eventType))
            {
                types.Add(eventType);
            }
        }

        if (types.Count == 0)
        {
            throw new ArgumentException("At least one event type is required.", nameof(eventTypes));
        }

        var results = new List<WebhookRegistration>(types.Count);
        foreach (var eventType in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await CreateAsync(eventType, single, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<WebhookRegistration> UpdateAsync(
        WebhookEventType eventType,
        IEnumerable<string> urls,
        CancellationToken cancellationToken = default)
    {
        var list = ValidateUrls(urls, nameof(urls));
        var uri = ItemUri(eventType);

        var content = new MultipartFormDataContent();
        foreach (var url in list)
        {
            content.Add(new StringContent(url), "url");
        }

        var (status, body) = await MailgunHttp.SendAsync(
            _httpClient,
            new HttpRequestMessage(HttpMethod.Put, uri) { Content = content },
            cancellationToken).ConfigureAwait(false);

        return ProjectEnvelope(eventType, status, body, fallbackUrls: list);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        WebhookEventType eventType,
        CancellationToken cancellationToken = default)
    {
        var uri = ItemUri(eventType);
        await MailgunHttp.SendAsync(
            _httpClient,
            new HttpRequestMessage(HttpMethod.Delete, uri),
            cancellationToken).ConfigureAwait(false);
    }

    private Uri RootUri() =>
        new Uri($"v3/domains/{_domain}/webhooks", UriKind.Relative);

    private Uri ItemUri(WebhookEventType eventType) =>
        new Uri($"v3/domains/{_domain}/webhooks/{WebhookEventTypes.ToToken(eventType)}", UriKind.Relative);

    /// <summary>
    /// Materializes the supplied URLs, dropping null/blank entries, and requires at least one to remain.
    /// Each remaining URL must be an absolute <c>http</c> or <c>https</c> URI, so an obviously malformed
    /// callback fails fast under the <see cref="ArgumentException"/> contract instead of as a service
    /// rejection; any further service-side validation still surfaces via <see cref="MailgunnerException"/>.
    /// </summary>
    private static List<string> ValidateUrls(
        IEnumerable<string> urls, string paramName)
    {
        if (urls is null)
        {
            throw new ArgumentException("At least one callback URL is required.", paramName);
        }

        var list = new List<string>();
        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException(
                    $"A callback URL must be an absolute http or https URL; got '{url}'.", paramName);
            }

            list.Add(url);
        }

        if (list.Count == 0)
        {
            throw new ArgumentException("At least one callback URL is required.", paramName);
        }

        return list;
    }

    /// <summary>
    /// Deserializes a single-webhook envelope and projects it to a <see cref="WebhookRegistration"/>. On a
    /// success response whose body cannot be parsed into a webhook, throws <see cref="MailgunnerException"/>
    /// (mirroring the send path). When <paramref name="fallbackUrls"/> is supplied (create/update), it is
    /// used when the response omits the URL list.
    /// </summary>
    private static WebhookRegistration ProjectEnvelope(
        WebhookEventType eventType,
        int status,
        string body,
        IReadOnlyList<string>? fallbackUrls = null)
    {
        var envelope = MailgunHttp.Deserialize(body, WebhookJsonContext.Default.WebhookEnvelopeDto, status);
        if (envelope?.Webhook is null)
        {
            if (fallbackUrls is not null)
            {
                return new WebhookRegistration(eventType, fallbackUrls);
            }

            throw new MailgunnerException(status, body);
        }

        var urls = envelope.Webhook.Urls is { Count: > 0 }
            ? (IReadOnlyList<string>)envelope.Webhook.Urls
            : fallbackUrls ?? (IReadOnlyList<string>)Array.Empty<string>();

        return new WebhookRegistration(eventType, urls);
    }
}
