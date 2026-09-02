using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class AttemptTimeoutTests
{
    private static StubHttpMessageHandler HangingFirstAttempt() =>
        new(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            BeforeResponse = (index, ct) => index == 0 ? Task.Delay(Timeout.InfiniteTimeSpan, ct) : Task.CompletedTask,
        };

    [Fact]
    public async Task A_hanging_attempt_is_abandoned_after_the_attempt_timeout_and_retried_in_full_mode()
    {
        var stub = HangingFirstAttempt();
        var client = RetryTestHarness.BuildClient(
            stub, new RecordingTimeProvider(), configure: o => o.Retry.AttemptTimeout = TimeSpan.FromMilliseconds(50));

        var result = await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.NotNull(result);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task A_hanging_send_surfaces_a_TimeoutException_in_safe_mode()
    {
        var stub = HangingFirstAttempt();
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider(), configure: o =>
        {
            o.Retry.AttemptTimeout = TimeSpan.FromMilliseconds(50);
            o.Retry.SendRetryMode = SendRetryMode.Safe;
        });

        await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync(RetryTestHarness.NewMessage()));

        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Caller_cancellation_during_an_attempt_is_not_reported_as_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            BeforeResponse = (_, ct) => { cts.Cancel(); return Task.Delay(Timeout.InfiniteTimeSpan, ct); },
        };
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(RetryTestHarness.NewMessage(), cts.Token));

        Assert.Single(stub.Requests);
    }

    [Fact]
    public void The_typed_http_client_has_no_overall_timeout()
    {
        var client = (MailgunnerClient)RetryTestHarness.BuildClient(new StubHttpMessageHandler(HttpStatusCode.OK));

        Assert.Equal(Timeout.InfiniteTimeSpan, client.HttpClient.Timeout);
    }
}
