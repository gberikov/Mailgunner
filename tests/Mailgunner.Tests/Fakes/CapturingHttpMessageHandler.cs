using System.Net;

namespace Mailgunner.Tests.Fakes;

/// <summary>
/// A fake transport that records the last outbound request and returns a canned 200 response,
/// so routing and authentication can be asserted offline without any real network call.
/// </summary>
internal sealed class CapturingHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
