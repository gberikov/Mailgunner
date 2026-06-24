using System.Net;

namespace Mailgunner.Tests.Fakes;

/// <summary>
/// A single captured <c>multipart/form-data</c> part: its field name and string value, plus — for file
/// parts — the <see cref="FileName"/> from its <c>Content-Disposition</c> and the <see cref="ContentType"/>
/// media type. String parts carry <see langword="null"/> for both file metadata fields.
/// </summary>
internal readonly record struct FormField(string Name, string Value, string? FileName, string? ContentType);

/// <summary>
/// A single captured request: its URI, method, content media type, and multipart fields. Lets batch
/// tests assert per-chunk request count and per-chunk field membership after the requests are disposed.
/// </summary>
internal sealed class CapturedRequest
{
    /// <summary>Initializes a new instance of the <see cref="CapturedRequest"/> class.</summary>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="contentMediaType">The content media type, if any.</param>
    /// <param name="formData">The captured multipart fields, in order.</param>
    public CapturedRequest(
        Uri? requestUri, HttpMethod? method, string? contentMediaType, IReadOnlyList<FormField> formData,
        string? body = null)
    {
        RequestUri = requestUri;
        Method = method;
        ContentMediaType = contentMediaType;
        FormData = formData;
        Body = body;
    }

    /// <summary>Gets the request URI.</summary>
    public Uri? RequestUri { get; }

    /// <summary>Gets the HTTP method.</summary>
    public HttpMethod? Method { get; }

    /// <summary>Gets the content media type (for example, <c>multipart/form-data</c>).</summary>
    public string? ContentMediaType { get; }

    /// <summary>Gets the captured multipart fields, in order.</summary>
    public IReadOnlyList<FormField> FormData { get; }

    /// <summary>Gets the raw request body for non-multipart (for example JSON) requests; null for multipart or bodyless requests.</summary>
    public string? Body { get; }

    /// <summary>Returns the values of every field named <paramref name="name"/>, in order.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The matching field values.</returns>
    public IReadOnlyList<string> Values(string name)
    {
        var values = new List<string>();
        foreach (var field in FormData)
        {
            if (field.Name == name)
            {
                values.Add(field.Value);
            }
        }

        return values;
    }

    /// <summary>Returns the value of the first field named <paramref name="name"/>, or null.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The first matching value, or null when absent.</returns>
    public string? Value(string name)
    {
        foreach (var field in FormData)
        {
            if (field.Name == name)
            {
                return field.Value;
            }
        }

        return null;
    }

    /// <summary>Returns how many fields are named <paramref name="name"/>.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The count of matching fields.</returns>
    public int Count(string name) => Values(name).Count;

    /// <summary>Returns every captured part named <paramref name="name"/>, in order (incl. file metadata).</summary>
    /// <param name="name">The field name (for example, <c>attachment</c> or <c>inline</c>).</param>
    /// <returns>The matching parts.</returns>
    public IReadOnlyList<FormField> Fields(string name)
    {
        var fields = new List<FormField>();
        foreach (var field in FormData)
        {
            if (field.Name == name)
            {
                fields.Add(field);
            }
        }

        return fields;
    }
}

/// <summary>
/// A configurable fake transport for sending tests. Returns a chosen status code and body, captures
/// <em>every</em> outgoing request (buffering each request's multipart fields, URI, method, and media
/// type so they can be inspected after the request is disposed), and honors the
/// <see cref="CancellationToken"/> so cancellation can be verified entirely offline. A
/// <see cref="ResponseSelector"/> can override the response for a chosen request index (for example, to
/// make the second chunk of a batch return 500 and exercise fail-fast). The <see cref="LastRequest"/>
/// and <see cref="LastFormData"/> members continue to point at the most recent request.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;
    private readonly List<CapturedRequest> _requests = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="StubHttpMessageHandler"/> class.
    /// </summary>
    /// <param name="statusCode">The default status code the fake returns.</param>
    /// <param name="responseBody">The default response body the fake returns (empty by default).</param>
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

    /// <summary>Gets the raw body of the last non-multipart (for example JSON) request; null otherwise.</summary>
    public string? LastBody { get; private set; }

    /// <summary>Gets every captured request, in the order they were sent.</summary>
    public IReadOnlyList<CapturedRequest> Requests => _requests;

    /// <summary>
    /// An optional per-request-index response override. Invoked with the zero-based index of the
    /// request about to be answered; when it returns a non-null tuple, that status and body are
    /// returned instead of the defaults. Lets a test fail a specific chunk to exercise fail-fast.
    /// </summary>
    public Func<int, (HttpStatusCode StatusCode, string Body)?>? ResponseSelector { get; set; }

    /// <summary>
    /// An optional hook invoked inside the send, after the request is captured but before the
    /// response is produced. Lets a test cancel mid-flight by canceling the source backing
    /// <paramref name="cancellationToken"/>. Invoked once per request, so a counter in the hook can
    /// target a specific chunk.
    /// </summary>
    public Action<CancellationToken>? OnSend { get; set; }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestUri = request.RequestUri;
        LastMethod = request.Method;
        LastContentMediaType = null;
        LastBody = null;

        var fields = Array.Empty<FormField>() as IReadOnlyList<FormField>;
        string? requestBody = null;

        if (request.Content is not null)
        {
            LastContentMediaType = request.Content.Headers.ContentType?.MediaType;
        }

        if (request.Content is not null and not MultipartFormDataContent)
        {
            requestBody = await request.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
            LastBody = requestBody;
        }

        if (request.Content is MultipartFormDataContent multipart)
        {
            var captured = new List<FormField>();
            foreach (var part in multipart)
            {
                var name = Unquote(part.Headers.ContentDisposition?.Name);
                var value = await part.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
                var rawFileName = part.Headers.ContentDisposition?.FileName;
                var fileName = rawFileName is null ? null : rawFileName.Trim('"');
                var contentType = part.Headers.ContentType?.MediaType;
                captured.Add(new FormField(name, value, fileName, contentType));
            }

            fields = captured;
        }

        LastFormData = fields;

        var index = _requests.Count;
        _requests.Add(new CapturedRequest(request.RequestUri, request.Method, LastContentMediaType, fields, requestBody));

        OnSend?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var (statusCode, body) = ResponseSelector?.Invoke(index) ?? (_statusCode, _responseBody);

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body),
            RequestMessage = request,
        };
    }

    private static string Unquote(string? name) =>
        string.IsNullOrEmpty(name) ? string.Empty : name!.Trim('"');
}
