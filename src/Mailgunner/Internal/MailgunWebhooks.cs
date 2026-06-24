namespace Mailgunner.Internal;

/// <summary>
/// Default <see cref="IMailgunWebhooks"/> implementation. Issues the v3 domain-webhook requests over the
/// client's configured <see cref="System.Net.Http.HttpClient"/> (region base URL + Basic auth) and the
/// trimmed sending domain. Create and update send <c>multipart/form-data</c> (<c>id</c>/<c>url</c>) parts;
/// list, read-one, and delete carry no body. Responses are JSON, deserialized with the source-generated
/// <see cref="WebhookJsonContext"/> and projected to <see cref="WebhookRegistration"/>. Any non-success
/// response surfaces the single <see cref="MailgunnerException"/>.
/// </summary>
internal sealed class MailgunWebhooks : IMailgunWebhooks
{
    private readonly System.Net.Http.HttpClient _httpClient;
    private readonly string _domain;

    /// <summary>Initializes a new instance of the <see cref="MailgunWebhooks"/> class.</summary>
    /// <param name="httpClient">The configured typed HTTP client (region base URL + Basic auth).</param>
    /// <param name="domain">The sending domain (already trimmed).</param>
    public MailgunWebhooks(System.Net.Http.HttpClient httpClient, string domain)
    {
        _httpClient = httpClient;
        _domain = domain;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<WebhookRegistration>> ListAsync(
        System.Threading.CancellationToken cancellationToken = default)
    {
        var (_, body) = await SendCoreAsync(
            new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, RootUri()),
            cancellationToken).ConfigureAwait(false);

        var dto = System.Text.Json.JsonSerializer.Deserialize(body, WebhookJsonContext.Default.WebhookListDto);
        var result = new System.Collections.Generic.List<WebhookRegistration>();
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
    public async System.Threading.Tasks.Task<WebhookRegistration> GetAsync(
        WebhookEventType eventType,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var uri = ItemUri(eventType);
        var (status, body) = await SendCoreAsync(
            new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri),
            cancellationToken).ConfigureAwait(false);

        return ProjectEnvelope(eventType, status, body);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<WebhookRegistration> CreateAsync(
        WebhookEventType eventType,
        System.Collections.Generic.IEnumerable<string> urls,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var list = ValidateUrls(urls, nameof(urls));
        var token = WebhookEventTypes.ToToken(eventType);

        var content = new System.Net.Http.MultipartFormDataContent
        {
            { new System.Net.Http.StringContent(token), "id" },
        };
        foreach (var url in list)
        {
            content.Add(new System.Net.Http.StringContent(url), "url");
        }

        var (status, body) = await SendCoreAsync(
            new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, RootUri()) { Content = content },
            cancellationToken).ConfigureAwait(false);

        return ProjectEnvelope(eventType, status, body, fallbackUrls: list);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<WebhookRegistration>> CreateAsync(
        System.Collections.Generic.IEnumerable<WebhookEventType> eventTypes,
        string url,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (eventTypes is null)
        {
            throw new System.ArgumentException("At least one event type is required.", nameof(eventTypes));
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new System.ArgumentException("A callback URL is required.", nameof(url));
        }

        var types = new System.Collections.Generic.List<WebhookEventType>(eventTypes);
        if (types.Count == 0)
        {
            throw new System.ArgumentException("At least one event type is required.", nameof(eventTypes));
        }

        var single = new[] { url };
        var results = new System.Collections.Generic.List<WebhookRegistration>(types.Count);
        foreach (var eventType in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await CreateAsync(eventType, single, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<WebhookRegistration> UpdateAsync(
        WebhookEventType eventType,
        System.Collections.Generic.IEnumerable<string> urls,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var list = ValidateUrls(urls, nameof(urls));
        var uri = ItemUri(eventType);

        var content = new System.Net.Http.MultipartFormDataContent();
        foreach (var url in list)
        {
            content.Add(new System.Net.Http.StringContent(url), "url");
        }

        var (status, body) = await SendCoreAsync(
            new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Put, uri) { Content = content },
            cancellationToken).ConfigureAwait(false);

        return ProjectEnvelope(eventType, status, body, fallbackUrls: list);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task DeleteAsync(
        WebhookEventType eventType,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var uri = ItemUri(eventType);
        await SendCoreAsync(
            new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Delete, uri),
            cancellationToken).ConfigureAwait(false);
    }

    private System.Uri RootUri() =>
        new System.Uri($"v3/{_domain}/webhooks", System.UriKind.Relative);

    private System.Uri ItemUri(WebhookEventType eventType) =>
        new System.Uri($"v3/{_domain}/webhooks/{WebhookEventTypes.ToToken(eventType)}", System.UriKind.Relative);

    /// <summary>
    /// Materializes the supplied URLs, dropping null/blank entries, and requires at least one to remain.
    /// The library does not validate URL format beyond requiring a non-blank URL; service-side validation
    /// is surfaced via <see cref="MailgunnerException"/>.
    /// </summary>
    private static System.Collections.Generic.List<string> ValidateUrls(
        System.Collections.Generic.IEnumerable<string> urls, string paramName)
    {
        if (urls is null)
        {
            throw new System.ArgumentException("At least one callback URL is required.", paramName);
        }

        var list = new System.Collections.Generic.List<string>();
        foreach (var url in urls)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                list.Add(url);
            }
        }

        if (list.Count == 0)
        {
            throw new System.ArgumentException("At least one callback URL is required.", paramName);
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
        System.Collections.Generic.IReadOnlyList<string>? fallbackUrls = null)
    {
        var envelope = System.Text.Json.JsonSerializer.Deserialize(body, WebhookJsonContext.Default.WebhookEnvelopeDto);
        if (envelope?.Webhook is null)
        {
            if (fallbackUrls is not null)
            {
                return new WebhookRegistration(eventType, fallbackUrls);
            }

            throw new MailgunnerException(status, body);
        }

        var urls = envelope.Webhook.Urls is { Count: > 0 }
            ? (System.Collections.Generic.IReadOnlyList<string>)envelope.Webhook.Urls
            : fallbackUrls ?? (System.Collections.Generic.IReadOnlyList<string>)System.Array.Empty<string>();

        return new WebhookRegistration(eventType, urls);
    }

    /// <summary>
    /// Issues <paramref name="request"/>, reads the body, and throws <see cref="MailgunnerException"/> on
    /// any non-success response (mirroring the send and suppression paths). Returns the status code and
    /// raw body on success.
    /// </summary>
    private async System.Threading.Tasks.Task<(int Status, string Body)> SendCoreAsync(
        System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
        {
#if NET8_0_OR_GREATER
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
            if (!response.IsSuccessStatusCode)
            {
                throw new MailgunnerException((int)response.StatusCode, body);
            }

            return ((int)response.StatusCode, body);
        }
    }
}
