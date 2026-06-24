namespace Mailgunner.Tests.Fakes;

/// <summary>
/// A test <see cref="TimeProvider"/> that makes Polly's inter-attempt waits both instantaneous and
/// observable. Every scheduled delay's requested duration is recorded (exposed in order via
/// <see cref="Delays"/>); by default the underlying timer fires immediately so no real time elapses.
/// <see cref="GetUtcNow"/> returns a controllable instant for HTTP-date math, and
/// <see cref="AutoAdvance"/> can be turned off so a delay stays pending (used to verify cancellation).
/// </summary>
internal sealed class RecordingTimeProvider : TimeProvider
{
    private readonly List<TimeSpan> _delays = new();
    private DateTimeOffset _utcNow;

    /// <summary>Initializes a new instance of the <see cref="RecordingTimeProvider"/> class.</summary>
    /// <param name="utcNow">The initial instant returned by <see cref="GetUtcNow"/>.</param>
    public RecordingTimeProvider(DateTimeOffset? utcNow = null) =>
        _utcNow = utcNow ?? DateTimeOffset.UnixEpoch;

    /// <summary>Gets every scheduled delay's requested duration, in order.</summary>
    public IReadOnlyList<TimeSpan> Delays => _delays;

    /// <summary>
    /// Gets or sets a value indicating whether a scheduled delay completes immediately. When
    /// <see langword="false"/>, scheduled delays never fire on their own, so a wait stays pending
    /// until the caller's token cancels it.
    /// </summary>
    public bool AutoAdvance { get; set; } = true;

    /// <summary>
    /// Gets or sets a callback invoked synchronously each time a delay is scheduled (after it is
    /// recorded). Lets a test cancel a token during a pending wait.
    /// </summary>
    public Action? OnDelayScheduled { get; set; }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>Sets the instant returned by <see cref="GetUtcNow"/>.</summary>
    /// <param name="value">The new current instant.</param>
    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // A finite, non-negative due time is a scheduled inter-attempt delay (Polly / Task.Delay).
        if (dueTime >= TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
        {
            _delays.Add(dueTime);
            OnDelayScheduled?.Invoke();

            var effectiveDue = AutoAdvance ? TimeSpan.Zero : Timeout.InfiniteTimeSpan;
            return base.CreateTimer(callback, state, effectiveDue, period);
        }

        return base.CreateTimer(callback, state, dueTime, period);
    }
}
