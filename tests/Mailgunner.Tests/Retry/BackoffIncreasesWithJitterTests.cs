using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class BackoffIncreasesWithJitterTests
{
    [Fact]
    public async Task Consecutive_transients_produce_strictly_increasing_jittered_waits()
    {
        // 503 on the first four attempts, success on the fifth ⇒ four recorded waits, all below the cap.
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            ResponseSelector = index => index < 4 ? (HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}") : null,
        };
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(
            stub,
            time,
            configure: o =>
            {
                o.Retry.MaxRetryAttempts = 5;
                o.Retry.BaseDelay = TimeSpan.FromMilliseconds(100);
                o.Retry.MaxSingleWait = TimeSpan.FromSeconds(30);
                o.Retry.UseJitter = true;
            },
            random: new SeededRetryRandom(12345));

        await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.Equal(4, time.Delays.Count);

        // Strictly increasing: each later wait exceeds the earlier one regardless of the random draw.
        for (var i = 1; i < time.Delays.Count; i++)
        {
            Assert.True(
                time.Delays[i] > time.Delays[i - 1],
                $"wait[{i}]={time.Delays[i]} should exceed wait[{i - 1}]={time.Delays[i - 1]}");
        }

        // Jitter is observable: at least one wait exceeds its pure exponential base (not bare exponential).
        var pureBaseTicks = TimeSpan.FromMilliseconds(100).Ticks;
        var jitterObserved = false;
        for (var i = 0; i < time.Delays.Count; i++)
        {
            var pureBase = TimeSpan.FromTicks(pureBaseTicks * (1L << i));
            if (time.Delays[i] > pureBase)
            {
                jitterObserved = true;
            }
        }

        Assert.True(jitterObserved, "expected at least one wait to carry observable jitter above its exponential base");
    }

    [Fact]
    public async Task A_retry_after_takes_precedence_over_computed_backoff_for_that_attempt()
    {
        // 503 on attempts 0..2, success on 3; the 2nd attempt's response carries Retry-After: 5.
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            ResponseSelector = index => index < 3 ? (HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}") : null,
            RetryAfterSelector = index => index == 1 ? "5" : null,
        };
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(
            stub,
            time,
            configure: o =>
            {
                o.Retry.MaxRetryAttempts = 5;
                o.Retry.BaseDelay = TimeSpan.FromMilliseconds(100);
                o.Retry.UseJitter = false; // isolate the precedence assertion
            });

        await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.Equal(3, time.Delays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(100), time.Delays[0]); // computed: base * 2^0
        Assert.Equal(TimeSpan.FromSeconds(5), time.Delays[1]); // Retry-After takes precedence
        Assert.Equal(TimeSpan.FromMilliseconds(400), time.Delays[2]); // computed: base * 2^2
    }

    [Fact]
    public async Task A_large_base_delay_saturates_at_the_cap_instead_of_overflowing()
    {
        // BaseDelay == MaxSingleWait so even attempt 0's pre-jitter value already meets the cap:
        // every one of the 10 waits (2^9x growth by the last) must clamp to the same cap, which
        // (unprotected) would multiply base-delay ticks past long.MaxValue and go negative. The cap
        // is kept under .NET's own ~49.7-day Task.Delay ceiling so the wait can actually be scheduled
        // through the real pipeline (a day-scale cap is still ~86,400x anything the other retry
        // tests use, which stay in the millisecond/second range).
        var stub = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}");
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(stub, time, configure: o =>
        {
            o.Retry.MaxRetryAttempts = 10;
            o.Retry.BaseDelay = TimeSpan.FromDays(30);
            o.Retry.MaxSingleWait = TimeSpan.FromDays(30);
            o.Retry.UseJitter = true;
        });

        await Assert.ThrowsAsync<MailgunnerException>(() => client.Suppressions.Bounces.ListPageAsync());

        Assert.Equal(10, time.Delays.Count);
        Assert.All(time.Delays, d => Assert.Equal(TimeSpan.FromDays(30), d));
    }
}
