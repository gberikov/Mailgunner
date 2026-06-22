namespace Mailgunner;

/// <summary>
/// Default <see cref="IMailgunnerClient"/> implementation. Constructed by the HTTP client
/// factory as a typed client whose underlying <see cref="System.Net.Http.HttpClient"/> is
/// pre-configured with the regional base URL and HTTP Basic authentication.
/// </summary>
internal sealed class MailgunnerClient : IMailgunnerClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MailgunnerClient"/> class.
    /// </summary>
    /// <param name="httpClient">The configured typed HTTP client.</param>
    public MailgunnerClient(System.Net.Http.HttpClient httpClient) => HttpClient = httpClient;

    /// <summary>
    /// Gets the configured typed HTTP client backing this client. Exposed to the test project
    /// (via <c>InternalsVisibleTo</c>) so routing and authentication can be asserted; not part
    /// of the public surface.
    /// </summary>
    internal System.Net.Http.HttpClient HttpClient { get; }
}
