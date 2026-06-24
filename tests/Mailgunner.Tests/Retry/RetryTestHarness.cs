using Mailgunner.Internal;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mailgunner.Tests.Retry;

/// <summary>
/// Shared offline wiring for the retry tests: builds a real DI container via <c>AddMailgunner</c>,
/// overrides the primary transport with a <see cref="StubHttpMessageHandler"/>, and optionally
/// substitutes a recording <see cref="RecordingTimeProvider"/>, a seeded jitter source, and a
/// capturing logger — so every timing property is asserted deterministically with no real time and
/// no network. The resilience handler runs above the stub exactly as in production.
/// </summary>
internal static class RetryTestHarness
{
    public const string SendingKey = "key-test-secret-abc123";

    /// <summary>A minimal, valid single-recipient text message used across the retry tests.</summary>
    public static MailgunMessage NewMessage()
    {
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com"),
            Text = "Hi",
        };
        message.To.Add("alice@example.com");
        return message;
    }

    /// <summary>A parseable success body matching the client's <c>SendResult</c> contract.</summary>
    public const string SuccessBody = "{\"id\":\"<20240101.1@mg.example.com>\",\"message\":\"Queued. Thank you.\"}";

    public static ServiceProvider BuildProvider(
        StubHttpMessageHandler stub,
        RecordingTimeProvider? time = null,
        Action<MailgunnerOptions>? configure = null,
        IRetryRandom? random = null,
        ILoggerProvider? loggerProvider = null)
    {
        var services = new ServiceCollection();

        var builder = services.AddMailgunner(options =>
        {
            options.Domain = "mg.example.com";
            options.SendingKey = SendingKey;
            options.Region = MailgunRegion.Us;
            configure?.Invoke(options);
        });
        builder.ConfigurePrimaryHttpMessageHandler(() => stub);

        // Registered after AddMailgunner so these win over its TryAdd defaults.
        if (time is not null)
        {
            services.AddSingleton<TimeProvider>(time);
        }

        if (random is not null)
        {
            services.AddSingleton(random);
        }

        if (loggerProvider is not null)
        {
            services.AddLogging(logging => logging.AddProvider(loggerProvider));
        }

        return services.BuildServiceProvider();
    }

    public static IMailgunnerClient BuildClient(
        StubHttpMessageHandler stub,
        RecordingTimeProvider? time = null,
        Action<MailgunnerOptions>? configure = null,
        IRetryRandom? random = null,
        ILoggerProvider? loggerProvider = null) =>
        BuildProvider(stub, time, configure, random, loggerProvider)
            .GetRequiredService<IMailgunnerClient>();
}
