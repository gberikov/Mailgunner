using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class AttachmentCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_interrupts_source_read_and_disposes_the_stream(bool inline)
    {
        using var source = new WaitingStream();
        using var cts = new CancellationTokenSource();
        using var provider = BuildProvider(TimeSpan.FromSeconds(30));
        var message = NewMessage(source, inline);
        var sending = provider.GetRequiredService<IMailgunnerClient>().SendAsync(message, cts.Token);
        try
        {
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(source.IsDisposed);
        }
        finally
        {
            source.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task Attempt_timeout_interrupts_source_read_without_retrying_a_safe_send()
    {
        using var source = new WaitingStream();
        using var provider = BuildProvider(TimeSpan.FromMilliseconds(200));
        var sending = provider.GetRequiredService<IMailgunnerClient>().SendAsync(NewMessage(source, inline: false));
        try
        {
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var ex = await Assert.ThrowsAsync<TimeoutException>(() => sending.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Contains("attempt timeout", ex.Message, StringComparison.Ordinal);
            Assert.True(source.IsDisposed);
        }
        finally
        {
            source.Release.TrySetResult();
        }
    }

    private static MailgunMessage NewMessage(WaitingStream source, bool inline)
    {
        var message = new MailgunMessage { From = "a@example.com", Text = "Hello" };
        message.To.Add("b@example.com");
        var files = inline ? message.InlineFiles : message.Attachments;
        files.Add(new MailgunFile("waiting.bin", () => source));
        return message;
    }

    private static ServiceProvider BuildProvider(TimeSpan attemptTimeout)
    {
        var services = new ServiceCollection();
        services.AddMailgunner(o =>
        {
            o.Domain = "mg.example.com";
            o.SendingKey = "key-test";
            o.Region = MailgunRegion.Us;
            o.Retry.AttemptTimeout = attemptTimeout;
        }).ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(HttpStatusCode.OK));
        return services.BuildServiceProvider();
    }

    private sealed class WaitingStream : MemoryStream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsDisposed { get; private set; }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
