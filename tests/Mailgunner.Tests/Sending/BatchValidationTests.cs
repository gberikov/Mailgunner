using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class BatchValidationTests
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

    [Fact]
    public async Task Null_message_throws_argument_null_and_issues_no_request()
    {
        var (client, stub) = BuildClient();

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendBatchAsync(null!));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Missing_sender_throws_argument_exception_and_issues_no_request()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage { Template = "conference-invite" };
        batch.Recipients.Add(new BatchRecipient(new EmailAddress("alice@example.com")));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendBatchAsync(batch));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Missing_template_throws_argument_exception_and_issues_no_request()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage { From = new EmailAddress("invites@mg.example.com") };
        batch.Recipients.Add(new BatchRecipient(new EmailAddress("alice@example.com")));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendBatchAsync(batch));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Duplicate_recipient_address_throws_argument_exception_and_issues_no_request()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage
        {
            From = new EmailAddress("invites@mg.example.com"),
            Template = "conference-invite",
        };
        batch.Recipients.Add(new BatchRecipient(new EmailAddress("alice@example.com")));
        batch.Recipients.Add(new BatchRecipient(new EmailAddress("alice@example.com")));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendBatchAsync(batch));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Recipient_with_a_default_address_throws_argument_exception_and_issues_no_request()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage
        {
            From = new EmailAddress("invites@mg.example.com"),
            Template = "conference-invite",
        };
        // A default EmailAddress bypasses the EmailAddress constructor's non-blank guard.
        batch.Recipients.Add(new BatchRecipient(default(EmailAddress)));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendBatchAsync(batch));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Addresses_differing_only_by_case_are_not_duplicates()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage
        {
            From = new EmailAddress("invites@mg.example.com"),
            Template = "conference-invite",
        };
        batch.Recipients.Add(new BatchRecipient(new EmailAddress("alice@example.com")));
        batch.Recipients.Add(new BatchRecipient(new EmailAddress("Alice@example.com")));

        await client.SendBatchAsync(batch);

        Assert.Single(stub.Requests);
        Assert.Equal(2, stub.Requests[0].Count("to"));
    }
}
