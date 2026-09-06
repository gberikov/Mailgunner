using Mailgunner.Internal;
using Microsoft.Extensions.Options;

namespace Mailgunner;

/// <summary>
/// Default <see cref="IMailgunnerClient"/> implementation. Constructed by the HTTP client
/// factory as a typed client whose underlying <see cref="HttpClient"/> is
/// pre-configured with the regional base URL and HTTP Basic authentication.
/// </summary>
internal sealed class MailgunnerClient : IMailgunnerClient
{
    /// <summary>The sending domain, trimmed and percent-encoded for use in request paths.</summary>
    private readonly string _domain;

    /// <summary>
    /// Initializes a new instance of the <see cref="MailgunnerClient"/> class.
    /// </summary>
    /// <param name="httpClient">The configured typed HTTP client.</param>
    /// <param name="options">The configured Mailgunner options supplying the sending domain, trimmed and percent-encoded for use in request paths.</param>
    public MailgunnerClient(HttpClient httpClient, IOptions<MailgunnerOptions> options)
    {
        Guard.NotNull(options, nameof(options));
        HttpClient = httpClient;
        _domain = Uri.EscapeDataString(options.Value.Domain.Trim());
        Suppressions = new MailgunSuppressions(HttpClient, _domain);
        Webhooks = new MailgunWebhooks(HttpClient, _domain);
    }

    /// <inheritdoc />
    public IMailgunSuppressions Suppressions { get; }

    /// <inheritdoc />
    public IMailgunWebhooks Webhooks { get; }

    /// <summary>
    /// Gets the configured typed HTTP client backing this client. Exposed to the test project
    /// (via <c>InternalsVisibleTo</c>) so routing and authentication can be asserted; not part
    /// of the public surface.
    /// </summary>
    internal HttpClient HttpClient { get; }

    /// <inheritdoc />
    public async Task<SendResult> SendAsync(
        MailgunMessage message,
        CancellationToken cancellationToken = default)
    {
        using var content = MailgunMessageContent.Build(message);
        return await SendContentAsync(content, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SendResult>> SendBatchAsync(
        MailgunBatchMessage message,
        CancellationToken cancellationToken = default)
    {
        MailgunBatchContent.Validate(message);

        var results = new List<SendResult>();

        var chunkIndex = 0;
        foreach (var chunk in MailgunBatchContent.Chunk(message.Recipients, MailgunBatchContent.MaxRecipientsPerRequest))
        {
            var requestStarted = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var content = MailgunBatchContent.BuildChunk(message, chunk);
                cancellationToken.ThrowIfCancellationRequested();
                requestStarted = true;
                results.Add(await SendContentAsync(content, cancellationToken).ConfigureAwait(false));
            }
            catch (MailgunnerException ex)
            {
                ex.SetBatchProgress(chunkIndex, results);
                BatchSendProgress.Attach(ex, chunkIndex, results, requestStarted);
                throw;
            }
            catch (Exception ex)
            {
                BatchSendProgress.Attach(ex, chunkIndex, results, requestStarted);
                throw;
            }

            chunkIndex++;
        }

        return results;
    }

    /// <summary>
    /// POSTs <paramref name="content"/> to the domain's messages endpoint and parses the response into
    /// a <see cref="SendResult"/>, throwing <see cref="MailgunnerException"/> on a non-success response
    /// or an unparseable success body. Shared by single and batch send so both honor the same error
    /// contract.
    /// </summary>
    private async Task<SendResult> SendContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri($"v3/{_domain}/messages", UriKind.Relative))
        {
            Content = content,
        };
        MailgunRequestMarkers.MarkAsSend(request);

        var (status, body) = await MailgunHttp.SendAsync(HttpClient, request, cancellationToken).ConfigureAwait(false);

        if (TryParseResult(body, out var result))
        {
            return result;
        }

        throw new MailgunnerException(status, body);
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
