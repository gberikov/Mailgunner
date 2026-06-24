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
    /// number of attempts is at most <c>MaxRetryAttempts + 1</c>). Must be <c>&gt;= 0</c>; <c>0</c>
    /// disables retry. Bounds the retry budget. Defaults to <c>3</c>.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the starting backoff used for the first retry; the computed backoff grows
    /// exponentially with each subsequent retry. Must be <c>&gt; <see cref="System.TimeSpan.Zero"/></c>.
    /// Defaults to 500&#160;milliseconds.
    /// </summary>
    public System.TimeSpan BaseDelay { get; set; } = System.TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets the mandatory upper bound applied to <em>every single</em> wait, including a wait
    /// derived from a server <c>Retry-After</c> header. Must be <c>&gt;= <see cref="BaseDelay"/></c>.
    /// Guarantees a hostile or far-future value cannot stall a send indefinitely. Defaults to
    /// 30&#160;seconds.
    /// </summary>
    public System.TimeSpan MaxSingleWait { get; set; } = System.TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether a bounded additive random component (a fraction less
    /// than one of the current computed backoff) is added to each computed wait, so retries from many
    /// callers are not synchronized while each later wait remains strictly greater than an earlier
    /// one. Defaults to <see langword="true"/>.
    /// </summary>
    public bool UseJitter { get; set; } = true;
}
