using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class SendRetryModeTests
{
    private const string Busy = "{\"message\":\"busy\"}";

    [Fact]
    public void Safe_is_the_default_mode()
    {
        Assert.Equal(SendRetryMode.Safe, new RetryPolicyOptions().SendRetryMode);
    }

    [Fact]
    public async Task Safe_mode_does_not_retry_a_send_on_503()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, Busy);
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(stub, time, configure: o => o.Retry.SendRetryMode = SendRetryMode.Safe);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(RetryTestHarness.NewMessage()));

        Assert.Equal(503, ex.StatusCode);
        Assert.Single(stub.Requests);
        Assert.Empty(time.Delays);
    }

    [Fact]
    public async Task Safe_mode_does_not_retry_a_send_on_a_transport_failure()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            TransientFailureSelector = index => index == 0,
        };
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider(), configure: o => o.Retry.SendRetryMode = SendRetryMode.Safe);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(RetryTestHarness.NewMessage()));

        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Safe_mode_still_retries_a_send_on_429()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.TooManyRequests, Busy) : null,
        };
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(stub, time, configure: o => o.Retry.SendRetryMode = SendRetryMode.Safe);

        var result = await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.NotNull(result);
        Assert.Equal(2, stub.Requests.Count);
        Assert.Single(time.Delays);
    }

    [Fact]
    public async Task Safe_mode_keeps_the_full_policy_for_non_send_requests()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"items\":[],\"paging\":{}}")
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.ServiceUnavailable, Busy) : null,
        };
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider(), configure: o => o.Retry.SendRetryMode = SendRetryMode.Safe);

        await client.Suppressions.Bounces.ListPageAsync();

        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Safe_mode_logs_no_exhaustion_record_for_an_unretried_send()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, Busy);
        var logger = new CapturingLoggerProvider();
        var client = RetryTestHarness.BuildClient(
            stub, new RecordingTimeProvider(), configure: o => o.Retry.SendRetryMode = SendRetryMode.Safe, loggerProvider: logger);

        await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(RetryTestHarness.NewMessage()));

        Assert.DoesNotContain(logger.Records, r => r.EventId == 1);
    }
}
