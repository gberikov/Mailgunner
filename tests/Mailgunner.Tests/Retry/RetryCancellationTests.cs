using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class RetryCancellationTests
{
    [Fact]
    public async Task Cancelling_during_a_pending_backoff_wait_surfaces_promptly_with_no_further_attempts()
    {
        using var cts = new CancellationTokenSource();

        // The wait never completes on its own; cancelling it the moment it is scheduled models a
        // caller cancelling mid-wait.
        var time = new RecordingTimeProvider { AutoAdvance = false };
        time.OnDelayScheduled = () => cts.Cancel();

        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}") : null,
        };
        var client = RetryTestHarness.BuildClient(stub, time);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync(RetryTestHarness.NewMessage(), cts.Token));

        Assert.Single(stub.Requests); // only the first attempt ran; no retry after cancellation
        Assert.Single(time.Delays); // a single wait was scheduled, then abandoned
    }
}
