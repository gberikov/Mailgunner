using System.Net;

namespace Mailgunner.Tests.Fakes;

/// <summary>
/// A single captured <c>multipart/form-data</c> field: its name and string value.
/// </summary>
internal readonly record struct FormField(string Name, string Value);

/// <summary>
/// A configurable fake transport for sending tests. Returns a chosen status code and body, captures
/// the last outgoing request (buffering its multipart fields and content type so they can be
/// inspected after the request is disposed), and honors the <see cref="CancellationToken"/> so
/// cancellation can be verified entirely offline.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    /// <summary>
    /// Initializes a new instance of the <see cref="StubHttpMessageHandler"/> class.
    /// </summary>
    /// <param name="statusCode">The status code the fake returns.</param>
    /// <param name="responseBody">The response body the fake returns (empty by default).</param>
    public StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody = "")
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    /// <summary>Gets the last request the fake received.</summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>Gets the request URI of the last request.</summary>
    public Uri? LastRequestUri { get; private set; }

    /// <summary>Gets the HTTP method of the last request.</summary>
    public HttpMethod? LastMethod { get; private set; }

    /// <summary>Gets the media type of the last request's content (for example, <c>multipart/form-data</c>).</summary>
    public string? LastContentMediaType { get; private set; }

    /// <summary>Gets the captured multipart form fields of the last request, in order.</summary>
    public IReadOnlyList<FormField> LastFormData { get; private set; } = Array.Empty<FormField>();

    /// <summary>
    /// An optional hook invoked inside the send, after the request is captured but before the
    /// response is produced. Lets a test cancel mid-flight by canceling the source backing
    /// <paramref name="cancellationToken"/>.
    /// </summary>
    public Action<CancellationToken>? OnSend { get; set; }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestUri = request.RequestUri;
        LastMethod = request.Method;

        if (request.Content is not null)
        {
            LastContentMediaType = request.Content.Headers.ContentType?.MediaType;
        }

        if (request.Content is MultipartFormDataContent multipart)
        {
            var fields = new List<FormField>();
            foreach (var part in multipart)
            {
                var name = Unquote(part.Headers.ContentDisposition?.Name);
                var value = await part.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
                fields.Add(new FormField(name, value));
            }

            LastFormData = fields;
        }

        OnSend?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody),
            RequestMessage = request,
        };
    }

    private static string Unquote(string? name) =>
        string.IsNullOrEmpty(name) ? string.Empty : name!.Trim('"');
}
