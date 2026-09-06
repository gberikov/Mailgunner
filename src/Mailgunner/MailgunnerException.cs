namespace Mailgunner;

/// <summary>
/// The single typed error raised when a Mailgun request does not yield a usable result: any
/// non-success (4xx/5xx) response, or a success (2xx) response whose body cannot be parsed into a
/// result. Exposes the HTTP status code and the raw response body. The sending key is never
/// included.
/// </summary>
/// <remarks>
/// CA1032 (provide the standard exception constructors) is intentionally suppressed: this
/// exception always carries an HTTP status code and a response body, so the parameterless and
/// message-only constructors are omitted — they would allow constructing an instance in an
/// invalid state.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "This exception must always carry an HTTP status code and response body; the parameterless and message-only constructors would permit an invalid instance.")]
public sealed class MailgunnerException : Exception
{
    private const int MaxServiceMessageLength = 200;

    /// <summary>Initializes a new instance for a single failed request.</summary>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="responseBody">The raw response body (never null; empty when the response had no body).</param>
    public MailgunnerException(int statusCode, string responseBody)
        : this(statusCode, responseBody, null, Array.Empty<SendResult>())
    {
    }

    /// <summary>Initializes a new instance for a batch chunk that failed after earlier chunks were accepted.</summary>
    /// <param name="statusCode">The HTTP status code of the failing chunk's response.</param>
    /// <param name="responseBody">The raw response body of the failing chunk.</param>
    /// <param name="failedChunkIndex">The zero-based index of the chunk that failed, or <see langword="null"/> outside a batch.</param>
    /// <param name="acceptedResults">The results of the chunks accepted before the failure, in order; empty outside a batch.</param>
    public MailgunnerException(
        int statusCode,
        string responseBody,
        int? failedChunkIndex,
        IReadOnlyList<SendResult> acceptedResults)
        : base(BuildMessage(statusCode, responseBody))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        FailedChunkIndex = failedChunkIndex;
        AcceptedResults = acceptedResults is null
            ? Array.Empty<SendResult>()
            : new List<SendResult>(acceptedResults).AsReadOnly();
    }

    /// <summary>
    /// Gets the HTTP status code of the response.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Gets the raw response body. Never null; empty when the response had no body.
    /// </summary>
    public string ResponseBody { get; }

    /// <summary>
    /// Gets the zero-based index of the batch chunk that failed, or <see langword="null"/> when the error
    /// did not occur inside <see cref="IMailgunnerClient.SendBatchAsync"/>.
    /// </summary>
    public int? FailedChunkIndex { get; private set; }

    /// <summary>
    /// Gets the results of the batch chunks Mailgun accepted before the failure (chunks
    /// <c>0..FailedChunkIndex-1</c>). Empty outside a batch. Those messages have been accepted and are
    /// not rolled back. The failing chunk may also have been accepted; inspect the failure before retrying.
    /// </summary>
    public IReadOnlyList<SendResult> AcceptedResults { get; private set; }

    internal void SetBatchProgress(int chunkIndex, IReadOnlyList<SendResult> results)
    {
        FailedChunkIndex = chunkIndex;
        AcceptedResults = new List<SendResult>(results).AsReadOnly();
    }

    private static string BuildMessage(int statusCode, string responseBody)
    {
        var serviceMessage = TryExtractServiceMessage(responseBody);
        return serviceMessage is null
            ? $"The Mailgun request did not yield a usable result (HTTP {statusCode})."
            : $"The Mailgun request failed (HTTP {statusCode}): {serviceMessage}";
    }

    /// <summary>Reads the <c>message</c> string of a JSON object body; null for anything else.</summary>
    private static string? TryExtractServiceMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                || !document.RootElement.TryGetProperty("message", out var message)
                || message.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return null;
            }

            var text = message.GetString()!;
            if (text.Length <= MaxServiceMessageLength)
            {
                return text;
            }

#if NET8_0_OR_GREATER
            return string.Concat(text.AsSpan(0, MaxServiceMessageLength), "…");
#else
            return text.Substring(0, MaxServiceMessageLength) + "…";
#endif
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
