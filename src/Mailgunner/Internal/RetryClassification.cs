using System.Net.Http.Headers;

namespace Mailgunner.Internal;

/// <summary>
/// Pure, side-effect-free classification helpers shared by the resilience handler. Kept separate so
/// the retryable-status set, transient-transport detection, <c>Retry-After</c> parsing, and the
/// single-wait cap can be unit-tested in isolation.
/// </summary>
internal static class RetryClassification
{
    /// <summary>
    /// Returns <see langword="true"/> for a status code that should be retried: <c>429</c> (rate
    /// limited), <c>408</c> (request timeout), or any <c>5xx</c> server error. Every other code —
    /// including a non-429 <c>4xx</c> and any <c>2xx</c>/<c>3xx</c> — is not retryable.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <returns><see langword="true"/> when the status is retryable.</returns>
    public static bool IsRetryableStatus(int statusCode) =>
        statusCode == 429 || statusCode == 408 || (statusCode >= 500 && statusCode <= 599);

    /// <summary>
    /// Returns <see langword="true"/> for a transient transport-level failure with no usable HTTP
    /// response (a connection reset/refused, DNS failure, or an <see cref="HttpClient"/> timeout),
    /// which should be retried like a <c>5xx</c>. A failure caused by the caller's own
    /// <paramref name="cancellationToken"/> is never transient — it is a cancellation, not a fault.
    /// </summary>
    /// <param name="exception">The exception thrown while sending.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns><see langword="true"/> when the failure is a transient transport fault.</returns>
    public static bool IsTransientTransport(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is null || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception switch
        {
            HttpRequestException => true,
            TaskCanceledException => true,
            TimeoutException => true,
            _ => false,
        };
    }

    /// <summary>
    /// Converts a <c>Retry-After</c> header to a relative wait. Honors both forms — delta-seconds and
    /// an HTTP-date (converted to a wait relative to <paramref name="now"/>). A non-positive or past
    /// value yields <see langword="null"/>, meaning no server-requested wait is enforced.
    /// </summary>
    /// <param name="retryAfter">The parsed <c>Retry-After</c> header value, or <see langword="null"/>.</param>
    /// <param name="now">The current time used to convert an HTTP-date form.</param>
    /// <returns>The requested wait, or <see langword="null"/> when none applies.</returns>
    public static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : (TimeSpan?)null;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - now;
            return wait > TimeSpan.Zero ? wait : (TimeSpan?)null;
        }

        return null;
    }

    /// <summary>
    /// Clamps a single wait to the mandatory upper bound, returning <c>min(wait, maxSingleWait)</c>.
    /// </summary>
    /// <param name="wait">The proposed wait.</param>
    /// <param name="maxSingleWait">The mandatory per-wait cap.</param>
    /// <returns>The capped wait.</returns>
    public static TimeSpan Cap(TimeSpan wait, TimeSpan maxSingleWait) =>
        wait < maxSingleWait ? wait : maxSingleWait;
}
