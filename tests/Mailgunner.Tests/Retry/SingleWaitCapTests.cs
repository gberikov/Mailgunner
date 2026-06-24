using System.Globalization;
using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class SingleWaitCapTests
{
    [Fact]
    public async Task A_huge_retry_after_delta_is_clamped_to_the_cap()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.TooManyRequests, "{\"message\":\"slow\"}") : null,
            RetryAfterSelector = index => index == 0 ? "100000" : null, // ~27.7 hours requested
        };
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(
            stub, time, configure: o => o.Retry.MaxSingleWait = TimeSpan.FromSeconds(30));

        await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(time.Delays));
    }

    [Fact]
    public async Task A_far_future_retry_after_date_is_clamped_to_the_cap()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var farFuture = now.AddDays(365).ToString("R", CultureInfo.InvariantCulture);
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.ServiceUnavailable, "{\"message\":\"down\"}") : null,
            RetryAfterSelector = index => index == 0 ? farFuture : null,
        };
        var time = new RecordingTimeProvider(now);
        var client = RetryTestHarness.BuildClient(
            stub, time, configure: o => o.Retry.MaxSingleWait = TimeSpan.FromSeconds(30));

        await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(time.Delays));
    }
}
