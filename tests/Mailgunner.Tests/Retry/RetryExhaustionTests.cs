using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class RetryExhaustionTests
{
    [Fact]
    public async Task Retryable_on_every_attempt_gives_up_with_the_final_error_and_logs_exhaustion()
    {
        const string failureBody = "{\"message\":\"overloaded\"}";
        var stub = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, failureBody);
        var time = new RecordingTimeProvider();
        var logger = new CapturingLoggerProvider();
        var client = RetryTestHarness.BuildClient(
            stub, time, configure: o => o.Retry.MaxRetryAttempts = 3, loggerProvider: logger);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.SendAsync(RetryTestHarness.NewMessage()));

        // Bounded, finite attempts (1 initial + 3 retries) and the final status + body surface.
        Assert.Equal(4, stub.Requests.Count);
        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(failureBody, ex.ResponseBody);

        // Exactly one Warning exhaustion record, naming the status and attempt count only.
        var record = Assert.Single(logger.Records, r => r.EventId == 1);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, record.Level);
        Assert.Contains("503", record.Message);
        Assert.Contains("4", record.Message);
        Assert.DoesNotContain(RetryTestHarness.SendingKey, record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(failureBody, record.Message, StringComparison.Ordinal);
    }
}
