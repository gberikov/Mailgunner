using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class BatchFailureTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-super-secret-123";
    private const string SuccessBody = "{\"id\":\"<x@mg>\",\"message\":\"Queued.\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient(
        Func<int, (HttpStatusCode StatusCode, string Body)?> responseSelector)
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody)
        {
            ResponseSelector = responseSelector,
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
    public async Task Non_success_on_second_chunk_fails_fast_with_status_and_body_and_sends_no_further_chunks()
    {
        const string failureBody = "{\"message\":\"'from' parameter is not a valid address\"}";
        // Chunk index 1 (the 2nd of 3) returns a permanent 400 (not retried); others succeed.
        var (client, stub) = BuildClient(index => index == 1 ? (HttpStatusCode.BadRequest, failureBody) : null);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendBatchAsync(NewBatch(2500)));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(failureBody, ex.ResponseBody);
        // Only 2 requests issued; the 3rd chunk is never sent (fail-fast).
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Sending_key_never_appears_in_any_field_or_the_thrown_error()
    {
        var (client, stub) = BuildClient(index => index == 1 ? (HttpStatusCode.BadRequest, "bad request") : null);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendBatchAsync(NewBatch(2500)));

        foreach (var request in stub.Requests)
        {
            foreach (var field in request.FormData)
            {
                Assert.DoesNotContain(SendingKey, field.Value, StringComparison.Ordinal);
            }
        }

        Assert.DoesNotContain(SendingKey, ex.ResponseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(SendingKey, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SendingKey, ex.ToString(), StringComparison.Ordinal);
    }
}
