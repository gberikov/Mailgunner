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
public sealed class MailgunnerException : System.Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MailgunnerException"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="responseBody">The raw response body (never null; empty when the response had no body).</param>
    public MailgunnerException(int statusCode, string responseBody)
        : base(BuildMessage(statusCode))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>
    /// Gets the HTTP status code of the response.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Gets the raw response body. Never null; empty when the response had no body.
    /// </summary>
    public string ResponseBody { get; }

    private static string BuildMessage(int statusCode) =>
        $"The Mailgun request did not yield a usable result (HTTP status {statusCode}).";
}
