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
    /// Initializes a new instance of the <see cref="MailgunResilienceHandler"/> class.
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
    {
        Guard.NotNull(timeProvider, nameof(timeProvider));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(logger, nameof(logger));
        Guard.NotNull(random, nameof(random));

        _timeProvider = timeProvider;
        _options = options.Value.Retry;
        _logger = logger;
        _random = random;
        _pipeline = BuildPipeline();
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Guard.NotNull(request, nameof(request));

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        var counter = new AttemptCounter();
        context.Properties.Set(AttemptCounterKey, counter);

        try
        {
            var response = await _pipeline
                .ExecuteAsync(
                    async ctx => await base.SendAsync(request, ctx.CancellationToken).ConfigureAwait(false),
                    context)
                .ConfigureAwait(false);

            if (_options.MaxRetryAttempts > 0 && RetryClassification.IsRetryableStatus((int)response.StatusCode))
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

    private ResiliencePipeline<HttpResponseMessage> BuildPipeline()
    {
        // A zero budget disables retry; Polly requires at least one attempt, so use a pass-through.
        if (_options.MaxRetryAttempts <= 0)
        {
            return ResiliencePipeline<HttpResponseMessage>.Empty;
        }

        var retry = new RetryStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = args =>
                new ValueTask<bool>(ShouldRetry(args.Outcome, args.Context.CancellationToken)),
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

    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome, CancellationToken cancellationToken)
    {
        if (outcome.Exception is { } exception)
        {
            return RetryClassification.IsTransientTransport(exception, cancellationToken);
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
        var baseDelay = Multiply(_options.BaseDelay, Math.Pow(2, attemptNumber));
        var jitter = _options.UseJitter
            ? Multiply(baseDelay, _random.NextDouble() * JitterFraction)
            : TimeSpan.Zero;

        return RetryClassification.Cap(baseDelay + jitter, _options.MaxSingleWait);
    }

    private static TimeSpan Multiply(TimeSpan value, double factor) =>
        TimeSpan.FromTicks((long)(value.Ticks * factor));

    /// <summary>Per-execution mutable retry count carried through the resilience context.</summary>
    private sealed class AttemptCounter
    {
        public int Retries;
    }
}
