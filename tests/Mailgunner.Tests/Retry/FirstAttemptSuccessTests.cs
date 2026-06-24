using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class FirstAttemptSuccessTests
{
    [Fact]
    public async Task A_first_attempt_success_makes_exactly_one_attempt_with_no_waiting()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody);
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(stub, time);

        var result = await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.NotNull(result);
        Assert.Single(stub.Requests); // exactly one transport attempt
        Assert.Empty(time.Delays); // zero recorded waits
    }

    [Fact]
    public async Task The_result_is_identical_to_a_non_retry_success()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody);
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider());

        var result = await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.Equal("<20240101.1@mg.example.com>", result.Id);
        Assert.Equal("Queued. Thank you.", result.Message);
    }
}
