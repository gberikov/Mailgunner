using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class BatchSendResultTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        // A distinct id per request lets us confirm one result is returned per chunk, in order.
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK)
        {
            ResponseSelector = index => (HttpStatusCode.OK, $"{{\"id\":\"<chunk{index}@mg>\",\"message\":\"Queued.\"}}"),
        };
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
    public async Task Returns_one_result_per_chunk_in_chunk_order()
    {
        var (client, _) = BuildClient();

        var results = await client.SendBatchAsync(NewBatch(2500));

        Assert.Equal(3, results.Count);
        Assert.Equal("<chunk0@mg>", results[0].Id);
        Assert.Equal("<chunk1@mg>", results[1].Id);
        Assert.Equal("<chunk2@mg>", results[2].Id);
    }

    [Fact]
    public async Task Empty_list_yields_an_empty_result_set()
    {
        var (client, _) = BuildClient();

        var results = await client.SendBatchAsync(NewBatch(0));

        Assert.Empty(results);
    }
}
