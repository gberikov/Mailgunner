using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class BatchProgressTests
{
    private const string Accepted = "{\"id\":\"accepted-first\",\"message\":\"Queued\"}";

    [Fact]
    public async Task Transport_failure_preserves_accepted_chunks_and_original_exception_type()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, Accepted)
        {
            TransientFailureSelector = index => index == 1,
        };
        using var provider = BuildProvider(stub);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Client(provider).SendBatchAsync(NewBatch()));

        AssertProgress(ex, requestStarted: true);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Attempt_timeout_preserves_accepted_chunks()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, Accepted)
        {
            BeforeResponse = (index, ct) => index == 1 ? Task.Delay(Timeout.InfiniteTimeSpan, ct) : Task.CompletedTask,
        };
        using var provider = BuildProvider(stub);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => Client(provider).SendBatchAsync(NewBatch()));

        AssertProgress(ex, requestStarted: true);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Cancellation_during_second_chunk_preserves_progress_and_cancellation_semantics()
    {
        using var cts = new CancellationTokenSource();
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, Accepted)
        {
            BeforeResponse = (index, ct) =>
            {
                if (index == 1)
                {
                    cts.Cancel();
                    ct.ThrowIfCancellationRequested();
                }

                return Task.CompletedTask;
            },
        };
        using var provider = BuildProvider(stub);
        var sending = Client(provider).SendBatchAsync(NewBatch(), cts.Token);

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);

        Assert.True(sending.IsCanceled);
        AssertProgress(ex, requestStarted: true);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Pre_canceled_batch_reports_that_no_request_started()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, Accepted);
        using var provider = BuildProvider(stub);

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(provider).SendBatchAsync(NewBatch(), cts.Token));

        var progress = Assert.IsType<BatchSendProgress>(BatchSendProgress.FromException(ex));
        Assert.Equal(0, progress.FailedChunkIndex);
        Assert.False(progress.RequestStarted);
        Assert.Empty(progress.AcceptedResults);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Serialization_failure_before_second_send_keeps_progress_without_marking_request_started()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, Accepted);
        using var provider = BuildProvider(stub);
        var batch = NewBatch();
        // A delegate cannot be serialized by System.Text.Json. Only the second chunk contains it.
        batch.Recipients[1000].Variables["invalid"] = (Action)(() => { });

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => Client(provider).SendBatchAsync(batch));

        AssertProgress(ex, requestStarted: false);
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData(400, "{\"message\":\"bad request\"}")]
    [InlineData(200, "{}")]
    public async Task Http_and_protocol_errors_keep_legacy_properties_and_progress(int status, string body)
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, Accepted)
        {
            ResponseSelector = index => index == 1 ? ((HttpStatusCode)status, body) : null,
        };
        using var provider = BuildProvider(stub);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => Client(provider).SendBatchAsync(NewBatch()));

        AssertProgress(ex, requestStarted: true);
        Assert.Equal(1, ex.FailedChunkIndex);
        Assert.Single(ex.AcceptedResults);
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }

    private static void AssertProgress(Exception ex, bool requestStarted)
    {
        var progress = Assert.IsType<BatchSendProgress>(BatchSendProgress.FromException(ex));
        Assert.Equal(1, progress.FailedChunkIndex);
        Assert.Equal("accepted-first", Assert.Single(progress.AcceptedResults).Id);
        Assert.Equal(requestStarted, progress.RequestStarted);
        var list = Assert.IsAssignableFrom<IList<SendResult>>(progress.AcceptedResults);
        Assert.Throws<NotSupportedException>(() => list.Clear());
    }

    private static ServiceProvider BuildProvider(StubHttpMessageHandler stub)
    {
        var services = new ServiceCollection();
        services.AddMailgunner(o =>
        {
            o.Domain = "mg.example.com";
            o.SendingKey = "key-test";
            o.Region = MailgunRegion.Us;
            o.Retry.AttemptTimeout = TimeSpan.FromMilliseconds(200);
        }).ConfigurePrimaryHttpMessageHandler(() => stub);
        return services.BuildServiceProvider();
    }

    private static IMailgunnerClient Client(ServiceProvider provider) => provider.GetRequiredService<IMailgunnerClient>();

    private static MailgunBatchMessage NewBatch()
    {
        var batch = new MailgunBatchMessage { From = "a@example.com", Text = "Hello" };
        for (var i = 0; i < 2001; i++)
        {
            batch.Recipients.Add(new BatchRecipient($"user{i}@example.com"));
        }

        return batch;
    }
}
