using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class BatchCancellationTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";
    private const string SuccessBody = "{\"id\":\"<x@mg>\",\"message\":\"Queued.\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, SendingKey, MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    private static MailgunBatchMessage NewBatch(int recipientCount)
    {
        var batch = new MailgunBatchMessage
        {
            From = new EmailAddress("invites@mg.example.com"),
            Template = "conference-invite",
        };

        for (var i = 0; i < recipientCount; i++)
        {
            batch.Recipients.Add(new BatchRecipient(new EmailAddress($"user{i}@example.com")));
        }

        return batch;
    }

    [Fact]
    public async Task Already_canceled_token_issues_no_requests()
    {
        var (client, stub) = BuildClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendBatchAsync(NewBatch(2500), cts.Token));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Cancellation_observed_mid_batch_stops_remaining_chunks()
    {
        var (client, stub) = BuildClient();
        using var cts = new CancellationTokenSource();

        // Cancel while the first chunk is in flight; the remaining chunks of the 3-chunk batch
        // must not be issued.
        stub.OnSend = _ => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendBatchAsync(NewBatch(2500), cts.Token));

        Assert.Single(stub.Requests);
    }
}
