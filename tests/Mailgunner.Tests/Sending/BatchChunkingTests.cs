using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class BatchChunkingTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";
    private const string SuccessBody = "{\"id\":\"<20260622.1@mg.example.com>\",\"message\":\"Queued. Thank you.\"}";

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
            From = new EmailAddress("invites@mg.example.com", "Acme Conf"),
            Subject = "Your personal invitation",
            Template = "conference-invite",
        };

        for (var i = 0; i < recipientCount; i++)
        {
            batch.Recipients.Add(new BatchRecipient(new EmailAddress($"user{i}@example.com")));
        }

        return batch;
    }

    [Fact]
    public async Task Send_of_2500_produces_three_requests_split_1000_1000_500()
    {
        var (client, stub) = BuildClient();

        var results = await client.SendBatchAsync(NewBatch(2500));

        Assert.Equal(3, stub.Requests.Count);
        Assert.Equal(1000, stub.Requests[0].Count("to"));
        Assert.Equal(1000, stub.Requests[1].Count("to"));
        Assert.Equal(500, stub.Requests[2].Count("to"));
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Send_of_exactly_1000_produces_one_request()
    {
        var (client, stub) = BuildClient();

        await client.SendBatchAsync(NewBatch(1000));

        Assert.Single(stub.Requests);
        Assert.Equal(1000, stub.Requests[0].Count("to"));
    }

    [Fact]
    public async Task Send_of_exactly_2000_produces_two_requests_with_no_empty_trailing_request()
    {
        var (client, stub) = BuildClient();

        await client.SendBatchAsync(NewBatch(2000));

        Assert.Equal(2, stub.Requests.Count);
        Assert.Equal(1000, stub.Requests[0].Count("to"));
        Assert.Equal(1000, stub.Requests[1].Count("to"));
    }

    [Fact]
    public async Task Each_request_targets_messages_endpoint_via_multipart_with_the_same_template()
    {
        var (client, stub) = BuildClient();

        await client.SendBatchAsync(NewBatch(2500));

        foreach (var request in stub.Requests)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/v3/{Domain}/messages", request.RequestUri!.AbsolutePath);
            Assert.Equal("multipart/form-data", request.ContentMediaType);
            Assert.Equal("conference-invite", request.Value("template"));
        }
    }

    [Fact]
    public async Task Per_chunk_to_parts_are_exactly_that_chunks_recipients_in_order()
    {
        var (client, stub) = BuildClient();

        await client.SendBatchAsync(NewBatch(2500));

        var firstChunk = stub.Requests[0].Values("to");
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal($"user{i}@example.com", firstChunk[i]);
        }

        var lastChunk = stub.Requests[2].Values("to");
        Assert.Equal(500, lastChunk.Count);
        Assert.Equal("user2000@example.com", lastChunk[0]);
        Assert.Equal("user2499@example.com", lastChunk[499]);
    }

    // --- T014: User Story 3 boundary cases ---

    [Fact]
    public async Task Empty_recipient_list_issues_zero_requests_and_returns_empty_results()
    {
        var (client, stub) = BuildClient();

        var results = await client.SendBatchAsync(NewBatch(0));

        Assert.Empty(stub.Requests);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Recipient_order_is_preserved_across_the_chunk_boundary()
    {
        var (client, stub) = BuildClient();

        await client.SendBatchAsync(NewBatch(2000));

        // Recipient #1000 ends chunk 1; #1001 begins chunk 2.
        Assert.Equal("user999@example.com", stub.Requests[0].Values("to")[999]);
        Assert.Equal("user1000@example.com", stub.Requests[1].Values("to")[0]);
    }
}
