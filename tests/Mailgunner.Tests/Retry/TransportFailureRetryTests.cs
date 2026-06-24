using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class TransportFailureRetryTests
{
    [Fact]
    public async Task A_transient_transport_failure_then_success_is_retried_like_a_5xx()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            // First attempt throws (timeout / connection reset / DNS); the retry returns 200.
            TransientFailureSelector = index => index == 0,
        };
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(stub, time);

        var result = await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.NotNull(result);
        Assert.Equal(2, stub.Requests.Count);
        Assert.Single(time.Delays);
    }

    [Fact]
    public async Task A_transient_transport_failure_on_every_attempt_exhausts_the_budget()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            TransientFailureSelector = _ => true, // every attempt fails at the transport level
        };
        var time = new RecordingTimeProvider();
        var logger = new CapturingLoggerProvider();
        var client = RetryTestHarness.BuildClient(
            stub, time, configure: o => o.Retry.MaxRetryAttempts = 3, loggerProvider: logger);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(RetryTestHarness.NewMessage()));

        Assert.Equal(4, stub.Requests.Count); // 1 initial + 3 retries (finite budget)
        Assert.Equal(3, time.Delays.Count);
        Assert.Contains(logger.Records, r => r.EventId == 1 && r.Message.Contains('4'));
    }
}
