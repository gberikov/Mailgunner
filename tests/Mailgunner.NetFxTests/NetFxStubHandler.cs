using System.Net;
using System.Net.Http;

namespace Mailgunner.NetFxTests;

/// <summary>Minimal scripted transport: returns the queued (status, body) pairs in order and records each request body.</summary>
internal sealed class NetFxStubHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses;

    public NetFxStubHandler(params (HttpStatusCode Status, string Body)[] responses) =>
        _responses = new Queue<(HttpStatusCode, string)>(responses);

    public List<string> Bodies { get; } = new();

    public int Requests { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync().ConfigureAwait(false));
        var (status, body) = _responses.Dequeue();
        return new HttpResponseMessage(status) { Content = new StringContent(body), RequestMessage = request };
    }
}
