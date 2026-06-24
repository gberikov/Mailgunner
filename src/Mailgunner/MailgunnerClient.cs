using Mailgunner.Internal;
using Microsoft.Extensions.Options;

namespace Mailgunner;

/// <summary>
/// Default <see cref="IMailgunnerClient"/> implementation. Constructed by the HTTP client
/// factory as a typed client whose underlying <see cref="System.Net.Http.HttpClient"/> is
/// pre-configured with the regional base URL and HTTP Basic authentication.
/// </summary>
internal sealed class MailgunnerClient : IMailgunnerClient
{
    private readonly string _domain;

    /// <summary>
    /// Initializes a new instance of the <see cref="MailgunnerClient"/> class.
    /// </summary>
    /// <param name="httpClient">The configured typed HTTP client.</param>
    /// <param name="options">The configured Mailgunner options supplying the sending domain.</param>
    public MailgunnerClient(System.Net.Http.HttpClient httpClient, IOptions<MailgunnerOptions> options)
    {
        Guard.NotNull(options, nameof(options));
        HttpClient = httpClient;
        _domain = options.Value.Domain.Trim();
    }

    /// <summary>
    /// Gets the configured typed HTTP client backing this client. Exposed to the test project
    /// (via <c>InternalsVisibleTo</c>) so routing and authentication can be asserted; not part
    /// of the public surface.
    /// </summary>
    internal System.Net.Http.HttpClient HttpClient { get; }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<SendResult> SendAsync(
        MailgunMessage message,
        System.Threading.CancellationToken cancellationToken = default)
    {
        using var content = MailgunMessageContent.Build(message);
        return await SendContentAsync(content, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<SendResult>> SendBatchAsync(
        MailgunBatchMessage message,
        System.Threading.CancellationToken cancellationToken = default)
    {
        MailgunBatchContent.Validate(message);

        var results = new System.Collections.Generic.List<SendResult>();

        foreach (var chunk in MailgunBatchContent.Chunk(message.Recipients, MailgunBatchContent.MaxRecipientsPerRequest))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var content = MailgunBatchContent.BuildChunk(message, chunk);
            var result = await SendContentAsync(content, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// POSTs <paramref name="content"/> to the domain's messages endpoint and parses the response into
    /// a <see cref="SendResult"/>, throwing <see cref="MailgunnerException"/> on a non-success response
    /// or an unparseable success body. Shared by single and batch send so both honor the same error
    /// contract.
    /// </summary>
    private async System.Threading.Tasks.Task<SendResult> SendContentAsync(
        System.Net.Http.HttpContent content,
        System.Threading.CancellationToken cancellationToken)
    {
        var requestUri = new Uri($"v3/{_domain}/messages", UriKind.Relative);
        using var response = await HttpClient
            .PostAsync(requestUri, content, cancellationToken)
            .ConfigureAwait(false);

#if NET8_0_OR_GREATER
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

        if (response.IsSuccessStatusCode && TryParseResult(body, out var result))
        {
            return result;
        }

        throw new MailgunnerException((int)response.StatusCode, body);
    }

    private static bool TryParseResult(string body, out SendResult result)
    {
        result = null!;

        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object
                || !root.TryGetProperty("id", out var id)
                || !root.TryGetProperty("message", out var messageElement)
                || id.ValueKind != System.Text.Json.JsonValueKind.String
                || messageElement.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return false;
            }

            result = new SendResult(id.GetString()!, messageElement.GetString()!);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
