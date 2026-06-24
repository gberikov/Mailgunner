using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class RetryOnTransientStatusTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)] // 429 (C1)
    [InlineData(HttpStatusCode.InternalServerError)] // 500 (C2)
    [InlineData(HttpStatusCode.RequestTimeout)] // 408 (C2)
    public async Task Retryable_status_then_success_ultimately_succeeds(HttpStatusCode transient)
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            // First attempt is the transient rejection; the retry sees the default 200.
            ResponseSelector = index => index == 0 ? (transient, "{\"message\":\"transient\"}") : null,
        };
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(stub, time);

        var result = await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.NotNull(result);
        Assert.Equal(2, stub.Requests.Count); // exactly one retry was needed
        Assert.Single(time.Delays); // one inter-attempt wait
    }

    [Fact]
    public async Task The_consumer_never_observes_the_intermediate_rejection()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.TooManyRequests, "{\"message\":\"slow down\"}") : null,
        };
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider());

        // No MailgunnerException is thrown; the 429 is absorbed and a SendResult is returned.
        var result = await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.NotNull(result.Id);
        Assert.NotNull(result.Message);
    }
}
