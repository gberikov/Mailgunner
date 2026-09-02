using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Mailgunner.Internal;

/// <summary>
/// A <see cref="DelegatingHandler"/> that wraps every outbound request in a Polly v8 resilience
/// pipeline: retryable statuses (<c>429</c>/<c>408</c>/<c>5xx</c>) and transient transport failures
/// are retried with a capped exponential backoff plus bounded additive jitter, a server
/// <c>Retry-After</c> header takes precedence for that attempt, and the caller's
/// <see cref="CancellationToken"/> abandons a pending wait promptly. When the finite retry budget is
/// spent and the outcome is still failing, a single Warning exhaustion record is logged (status or
/// exception type and attempt count only — never the sending key or request body).
/// </summary>
internal sealed class MailgunResilienceHandler : DelegatingHandler
{
    /// <summary>
    /// The fraction of the current computed backoff added as jitter. Kept strictly below one so the
    /// smallest possible wait for the next retry (<c>base * 2</c>) always exceeds the largest possible
    /// wait for the current retry (<c>base * (1 + JitterFraction)</c>), guaranteeing a strictly
    /// increasing wait schedule regardless of the random draw.
    /// </summary>
    private const double JitterFraction = 0.5;

    private static readonly ResiliencePropertyKey<AttemptCounter> AttemptCounterKey =
        new("Mailgunner.RetryAttemptCounter");

    private static readonly ResiliencePropertyKey<bool> IsSendKey = new("Mailgunner.IsSend");

    private static readonly Action<ILogger, int, string, Exception?> LogRetriesExhausted =
        LoggerMessage.Define<int, string>(
            LogLevel.Warning,
            new EventId(1, "RetriesExhausted"),
            "Mailgun request retries exhausted after {AttemptCount} attempt(s); final outcome {FinalOutcome}.");

    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private readonly TimeProvider _timeProvider;
    private readonly RetryPolicyOptions _options;
    private readonly ILogger<MailgunResilienceHandler> _logger;
    private readonly IRetryRandom _random;

    /// <summary>
    /// Initializes a new instance of the <see cref="MailgunResilienceHandler"/> class. Used by the
    /// dependency-injection container for the unnamed client (only this public constructor is visible
    /// to the activator, so there is no constructor-selection ambiguity).
    /// </summary>
    /// <param name="timeProvider">The time provider used for all waits and HTTP-date math.</param>
    /// <param name="options">The configured Mailgunner options supplying the retry tuning.</param>
    /// <param name="logger">The logger used to emit the exhaustion record.</param>
    /// <param name="random">The (seedable) jitter source.</param>
    public MailgunResilienceHandler(
        TimeProvider timeProvider,
        IOptions<MailgunnerOptions> options,
        ILogger<MailgunResilienceHandler> logger,
        IRetryRandom random)
        : this(timeProvider, RetryOf(options), logger, random)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MailgunResilienceHandler"/> class from explicit
    /// retry tuning. Used when constructing a per-name handler whose tuning comes from a named options
    /// instance (<c>IOptionsMonitor.Get(name).Retry</c>) rather than the unnamed
    /// <see cref="IOptions{TOptions}"/>.
    /// </summary>
    /// <param name="timeProvider">The time provider used for all waits and HTTP-date math.</param>
    /// <param name="retry">The retry tuning for this client.</param>
    /// <param name="logger">The logger used to emit the exhaustion record.</param>
    /// <param name="random">The (seedable) jitter source.</param>
    internal MailgunResilienceHandler(
        TimeProvider timeProvider,
        RetryPolicyOptions retry,
        ILogger<MailgunResilienceHandler> logger,
        IRetryRandom random)
    {
        Guard.NotNull(timeProvider, nameof(timeProvider));
        Guard.NotNull(retry, nameof(retry));
        Guard.NotNull(logger, nameof(logger));
        Guard.NotNull(random, nameof(random));

        _timeProvider = timeProvider;
        _options = retry;
        _logger = logger;
        _random = random;
        _pipeline = BuildPipeline();
    }

    private static RetryPolicyOptions RetryOf(IOptions<MailgunnerOptions> options)
    {
        Guard.NotNull(options, nameof(options));
        return options.Value.Retry;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Guard.NotNull(request, nameof(request));

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        var counter = new AttemptCounter();
        context.Properties.Set(AttemptCounterKey, counter);
        context.Properties.Set(IsSendKey, MailgunRequestMarkers.IsSend(request));

        try
        {
            var response = await _pipeline
                .ExecuteAsync(
                    async ctx => await SendAttemptAsync(request, ctx.CancellationToken).ConfigureAwait(false),
                    context)
                .ConfigureAwait(false);

            if (_options.MaxRetryAttempts > 0
                && counter.Retries >= _options.MaxRetryAttempts
                && RetryClassification.IsRetryableStatus((int)response.StatusCode))
            {
                LogRetriesExhausted(
                    _logger,
                    counter.Retries + 1,
                    ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                    null);
            }

            return response;
        }
        catch (Exception ex) when (
            _options.MaxRetryAttempts > 0
            && counter.Retries >= _options.MaxRetryAttempts
            && RetryClassification.IsTransientTransport(ex, cancellationToken))
        {
            LogRetriesExhausted(_logger, counter.Retries + 1, ex.GetType().Name, null);
            throw;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    /// <summary>
    /// Runs one attempt under <see cref="RetryPolicyOptions.AttemptTimeout"/>. A timeout is reported as
    /// <see cref="TimeoutException"/> (retryable under the full policy); the caller's own cancellation
    /// propagates unchanged.
    /// </summary>
    private async Task<HttpResponseMessage> SendAttemptAsync(HttpRequestMessage request, CancellationToken callerToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        attempt.CancelAfter(_options.AttemptTimeout);
        try
        {
            return await base.SendAsync(request, attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (attempt.IsCancellationRequested && !callerToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The Mailgun request attempt exceeded the attempt timeout of {_options.AttemptTimeout}.", ex);
        }
    }

    private ResiliencePipeline<HttpResponseMessage> BuildPipeline()
    {
        // A zero budget disables retry; Polly requires at least one attempt, so use a pass-through.
        if (_options.MaxRetryAttempts <= 0)
        {
            return ResiliencePipeline<HttpResponseMessage>.Empty;
        }

        var retry = new RetryStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = args => new ValueTask<bool>(ShouldRetry(args.Outcome, args.Context)),
            MaxRetryAttempts = _options.MaxRetryAttempts,
            DelayGenerator = args =>
                new ValueTask<TimeSpan?>(ComputeDelay(args.Outcome, args.AttemptNumber)),
            OnRetry = args =>
            {
                if (args.Context.Properties.TryGetValue(AttemptCounterKey, out var counter))
                {
                    counter.Retries++;
                }

                return default;
            },
        };

        return new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = _timeProvider }
            .AddRetry(retry)
            .Build();
    }

    private bool ShouldRetry(Outcome<HttpResponseMessage> outcome, ResilienceContext context)
    {
        var isSend = context.Properties.TryGetValue(IsSendKey, out var send) && send;
        if (isSend && _options.SendRetryMode == SendRetryMode.Safe)
        {
            // A send is not idempotent: only a rate-limit rejection is provably unaccepted.
            return outcome.Result is { } rejected && (int)rejected.StatusCode == 429;
        }

        if (outcome.Exception is { } exception)
        {
            return RetryClassification.IsTransientTransport(exception, context.CancellationToken);
        }

        return outcome.Result is { } response
            && RetryClassification.IsRetryableStatus((int)response.StatusCode);
    }

    private TimeSpan? ComputeDelay(Outcome<HttpResponseMessage> outcome, int attemptNumber)
    {
        // A server Retry-After on a retryable response takes precedence (clamped to the cap).
        if (outcome.Result is { } response)
        {
            var retryAfter = RetryClassification.ParseRetryAfter(
                response.Headers.RetryAfter, _timeProvider.GetUtcNow());
            if (retryAfter is { } requested)
            {
                return RetryClassification.Cap(requested, _options.MaxSingleWait);
            }
        }

        // Fallback: exponential base growth plus bounded additive jitter, then cap.
        return ComputeBackoffDelay(attemptNumber);
    }

    /// <summary>
    /// Computes the exponential-backoff-plus-jitter wait for the given 0-based retry attempt,
    /// saturated at <see cref="RetryPolicyOptions.MaxSingleWait"/>. Computed in <c>double</c> so a
    /// large <see cref="RetryPolicyOptions.BaseDelay"/> or attempt count cannot overflow the tick
    /// count the way multiplying <see cref="TimeSpan"/> values directly used to. <c>internal</c>
    /// (rather than <c>private</c>) so the saturating behavior can be pinned directly at
    /// configurations — such as <see cref="TimeSpan.MaxValue"/> — that a real wait can never reach
    /// end-to-end, since a wait that long exceeds what <see cref="Task.Delay(TimeSpan)"/> itself can
    /// schedule.
    /// </summary>
    /// <param name="attemptNumber">The 0-based retry attempt number.</param>
    /// <returns>The computed, capped wait.</returns>
    internal TimeSpan ComputeBackoffDelay(int attemptNumber)
    {
        var capTicks = (double)_options.MaxSingleWait.Ticks;
        var baseTicks = Math.Min(_options.BaseDelay.Ticks * Math.Pow(2, attemptNumber), capTicks);
        var jitterTicks = _options.UseJitter ? baseTicks * _random.NextDouble() * JitterFraction : 0d;
        var totalTicks = baseTicks + jitterTicks;

        // capTicks may itself be the double rounding of long.MaxValue up to exactly 2^63 (when
        // MaxSingleWait is TimeSpan.MaxValue), which does not fit back into a long. Comparing in
        // double space and returning the configured MaxSingleWait verbatim once saturated avoids
        // ever casting a value that could round up to (or past) that boundary — an unchecked cast
        // there would silently wrap to a large negative TimeSpan instead of throwing.
        return totalTicks >= capTicks
            ? _options.MaxSingleWait
            : TimeSpan.FromTicks((long)totalTicks);
    }

    /// <summary>Per-execution mutable retry count carried through the resilience context.</summary>
    private sealed class AttemptCounter
    {
        public int Retries;
    }
}
