using System.Globalization;
using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class RetryAfterHonoredTests
{
    private static StubHttpMessageHandler TransientWithRetryAfter(string retryAfter) =>
        new(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.TooManyRequests, "{\"message\":\"slow\"}") : null,
            RetryAfterSelector = index => index == 0 ? retryAfter : null,
        };

    [Fact]
    public async Task Retry_after_delta_seconds_is_honored()
    {
        var stub = TransientWithRetryAfter("2");
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(stub, time);

        await client.SendAsync(RetryTestHarness.NewMessage());

        var wait = Assert.Single(time.Delays);
        Assert.True(wait >= TimeSpan.FromSeconds(2), $"wait {wait} should be >= 2s");
        Assert.True(wait <= TimeSpan.FromSeconds(30), $"wait {wait} should be <= cap");
    }

    [Fact]
    public async Task Retry_after_http_date_is_converted_to_a_relative_wait()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var httpDate = now.AddSeconds(10).ToString("R", CultureInfo.InvariantCulture);
        var stub = TransientWithRetryAfter(httpDate);
        var time = new RecordingTimeProvider(now);
        var client = RetryTestHarness.BuildClient(stub, time);

        await client.SendAsync(RetryTestHarness.NewMessage());

        var wait = Assert.Single(time.Delays);
        Assert.True(wait >= TimeSpan.FromSeconds(10), $"wait {wait} should be >= 10s");
        Assert.True(wait <= TimeSpan.FromSeconds(30), $"wait {wait} should be <= cap");
    }

    [Fact]
    public async Task A_past_http_date_falls_back_to_computed_backoff()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var pastDate = now.AddSeconds(-60).ToString("R", CultureInfo.InvariantCulture);
        var stub = TransientWithRetryAfter(pastDate);
        var time = new RecordingTimeProvider(now);
        var client = RetryTestHarness.BuildClient(
            stub, time, configure: o => o.Retry.UseJitter = false);

        await client.SendAsync(RetryTestHarness.NewMessage());

        // No usable server value ⇒ the computed exponential backoff (= BaseDelay for the first retry).
        var wait = Assert.Single(time.Delays);
        Assert.Equal(TimeSpan.FromMilliseconds(500), wait);
    }

    [Fact]
    public async Task An_absent_retry_after_uses_computed_backoff()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}") : null,
        };
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(
            stub, time, configure: o => o.Retry.UseJitter = false);

        await client.SendAsync(RetryTestHarness.NewMessage());

        var wait = Assert.Single(time.Delays);
        Assert.Equal(TimeSpan.FromMilliseconds(500), wait);
    }
}
