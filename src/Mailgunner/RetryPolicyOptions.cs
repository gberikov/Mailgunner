namespace Mailgunner;

/// <summary>
/// Retry and backoff tuning for outbound Mailgun requests. Every value has a
/// constitution-compliant default, so retry is on by default and existing registrations need no
/// changes. Exposed through <see cref="MailgunnerOptions.Retry"/>.
/// </summary>
public sealed class RetryPolicyOptions
{
    /// <summary>
    /// Gets or sets the number of <em>retries</em> attempted after the first send (so the total
    /// number of attempts is at most <c>MaxRetryAttempts + 1</c>). Must be between <c>0</c> and
    /// <see cref="MaxAllowedRetryAttempts"/> inclusive; <c>0</c> disables retry. Bounds the retry
    /// budget. Defaults to <c>3</c>.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>The largest accepted <see cref="MaxRetryAttempts"/>; bounds the exponential schedule.</summary>
    public const int MaxAllowedRetryAttempts = 10;

    /// <summary>
    /// Gets or sets the starting backoff used for the first retry; the computed backoff grows
    /// exponentially with each subsequent retry. Must be <c>&gt; <see cref="TimeSpan.Zero"/></c>.
    /// Defaults to 500&#160;milliseconds.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets the mandatory upper bound applied to <em>every single</em> wait, including a wait
    /// derived from a server <c>Retry-After</c> header. Must be <c>&gt;= <see cref="BaseDelay"/></c>
    /// and at most one day &#8212; above that, .NET Framework 4.8's ~24.9-day timer ceiling (the
    /// lower of the two ceilings across the frameworks this library targets) is exceeded and the
    /// wait cannot be scheduled. Guarantees a hostile or far-future value cannot stall a send
    /// indefinitely. Defaults to 30&#160;seconds.
    /// </summary>
    public TimeSpan MaxSingleWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether a bounded additive random component (a fraction less
    /// than one of the current computed backoff) is added to each computed wait, so retries from many
    /// callers are not synchronized while each later wait remains strictly greater than an earlier
    /// one. Defaults to <see langword="true"/>.
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Gets or sets how a message send is retried. Defaults to <see cref="SendRetryMode.Safe"/>
    /// (retry only on <c>429</c>) because a send is not idempotent. See <see cref="Mailgunner.SendRetryMode"/>.
    /// </summary>
    public SendRetryMode SendRetryMode { get; set; } = SendRetryMode.Safe;

    /// <summary>
    /// Gets or sets the maximum duration of a <em>single</em> attempt (connect, send, and read of the
    /// response). An attempt exceeding it is abandoned and surfaces as <see cref="TimeoutException"/>,
    /// which the retry policy treats as a transient transport fault. Replaces the typed client's overall
    /// <c>HttpClient.Timeout</c>, which the library sets to infinite so backoff waits are never cut short.
    /// Must be <c>&gt; <see cref="TimeSpan.Zero"/></c> and at most one day &#8212; above that,
    /// .NET Framework 4.8's ~24.9-day timer ceiling (the lower of the two ceilings across the
    /// frameworks this library targets) is exceeded and the <em>first</em> attempt of every request
    /// fails, not only a retry. Defaults to 100&#160;seconds.
    /// </summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(100);
}
