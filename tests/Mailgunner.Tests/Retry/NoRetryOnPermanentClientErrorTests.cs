using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class NoRetryOnPermanentClientErrorTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)] // 400
    [InlineData(HttpStatusCode.Unauthorized)] // 401
    [InlineData(HttpStatusCode.Forbidden)] // 403
    [InlineData(HttpStatusCode.NotFound)] // 404
    public async Task A_non_429_4xx_surfaces_immediately_after_one_attempt(HttpStatusCode permanent)
    {
        const string body = "{\"message\":\"permanent failure\"}";
        var stub = new StubHttpMessageHandler(permanent, body);
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(stub, time);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.SendAsync(RetryTestHarness.NewMessage()));

        Assert.Equal((int)permanent, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
        Assert.Single(stub.Requests); // exactly one attempt — never retried
        Assert.Empty(time.Delays); // no wait
    }

    [Fact]
    public async Task A_429_is_treated_as_retryable()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.TooManyRequests, "{\"message\":\"rate limited\"}") : null,
        };
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider());

        var result = await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.NotNull(result); // the 429 was retried, not surfaced as a permanent failure
        Assert.Equal(2, stub.Requests.Count);
    }
}
