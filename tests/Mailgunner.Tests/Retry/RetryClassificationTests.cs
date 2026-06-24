using System.Net.Http.Headers;
using Mailgunner.Internal;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class RetryClassificationTests
{
    [Theory]
    [InlineData(429, true)]
    [InlineData(408, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(599, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(428, false)]
    [InlineData(200, false)]
    [InlineData(301, false)]
    public void IsRetryableStatus_matches_the_retryable_set(int status, bool expected) =>
        Assert.Equal(expected, RetryClassification.IsRetryableStatus(status));

    [Fact]
    public void IsTransientTransport_true_for_http_request_exception()
    {
        var transient = RetryClassification.IsTransientTransport(
            new HttpRequestException("boom"), CancellationToken.None);

        Assert.True(transient);
    }

    [Fact]
    public void IsTransientTransport_true_for_http_client_timeout()
    {
        // A TaskCanceledException not tied to the caller's token models an HttpClient timeout.
        var transient = RetryClassification.IsTransientTransport(
            new TaskCanceledException(), CancellationToken.None);

        Assert.True(transient);
    }

    [Fact]
    public void IsTransientTransport_false_for_caller_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var transient = RetryClassification.IsTransientTransport(
            new TaskCanceledException(), cts.Token);

        Assert.False(transient);
    }

    [Fact]
    public void IsTransientTransport_false_for_unrelated_exception() =>
        Assert.False(RetryClassification.IsTransientTransport(
            new InvalidOperationException(), CancellationToken.None));

    [Fact]
    public void ParseRetryAfter_reads_positive_delta_seconds()
    {
        var header = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        var wait = RetryClassification.ParseRetryAfter(header, DateTimeOffset.UnixEpoch);

        Assert.Equal(TimeSpan.FromSeconds(7), wait);
    }

    [Fact]
    public void ParseRetryAfter_converts_future_http_date_to_relative_wait()
    {
        var now = DateTimeOffset.UnixEpoch;
        var header = new RetryConditionHeaderValue(now.AddSeconds(10));

        var wait = RetryClassification.ParseRetryAfter(header, now);

        Assert.Equal(TimeSpan.FromSeconds(10), wait);
    }

    [Fact]
    public void ParseRetryAfter_returns_null_for_past_http_date()
    {
        var now = DateTimeOffset.UnixEpoch.AddMinutes(5);
        var header = new RetryConditionHeaderValue(DateTimeOffset.UnixEpoch);

        Assert.Null(RetryClassification.ParseRetryAfter(header, now));
    }

    [Fact]
    public void ParseRetryAfter_returns_null_when_header_absent() =>
        Assert.Null(RetryClassification.ParseRetryAfter(null, DateTimeOffset.UnixEpoch));

    [Fact]
    public void Cap_clamps_to_the_maximum()
    {
        var capped = RetryClassification.Cap(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), capped);
    }

    [Fact]
    public void Cap_leaves_a_value_below_the_maximum_unchanged()
    {
        var capped = RetryClassification.Cap(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(5), capped);
    }
}
