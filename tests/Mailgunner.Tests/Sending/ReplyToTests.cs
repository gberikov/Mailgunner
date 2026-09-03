using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class ReplyToTests
{
    private const string SuccessBody = "{\"id\":\"<x@mg>\",\"message\":\"Queued.\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        return (services.BuildServiceProvider().GetRequiredService<IMailgunnerClient>(), stub);
    }

    private static MailgunMessage NewMessage()
    {
        var message = new MailgunMessage { From = "noreply@mg.example.com", Text = "Hi" };
        message.To.Add("alice@example.com");
        return message;
    }

    [Fact]
    public async Task ReplyTo_is_emitted_as_the_Reply_To_header()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.ReplyTo = new EmailAddress("support@example.com", "Support");

        await client.SendAsync(message);

        Assert.Equal("Support <support@example.com>", stub.LastFormData.Single(f => f.Name == "h:Reply-To").Value);
    }

    [Fact]
    public async Task ReplyTo_is_omitted_when_unset()
    {
        var (client, stub) = BuildClient();

        await client.SendAsync(NewMessage());

        Assert.DoesNotContain(stub.LastFormData, f => f.Name == "h:Reply-To");
    }

    [Fact]
    public async Task ReplyTo_conflicting_with_a_manual_header_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.ReplyTo = "support@example.com";
        message.Options.CustomHeaders["reply-to"] = "other@example.com";

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(message));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Batch_ReplyTo_is_repeated_on_every_chunk()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage { From = "noreply@mg.example.com", Template = "t", ReplyTo = "support@example.com" };
        for (var i = 0; i < 1001; i++)
        {
            batch.Recipients.Add(new BatchRecipient($"u{i}@example.com"));
        }

        await client.SendBatchAsync(batch);

        Assert.All(stub.Requests, r => Assert.Equal("support@example.com", r.Value("h:Reply-To")));
    }

    [Fact]
    public async Task Options_can_be_replaced_with_a_shared_instance()
    {
        var (client, stub) = BuildClient();
        var shared = new MailgunSendOptions { TestMode = true };
        var message = NewMessage();
        message.Options = shared;

        await client.SendAsync(message);

        Assert.Equal("yes", stub.LastFormData.Single(f => f.Name == "o:testmode").Value);
    }

    [Fact]
    public async Task Null_options_throw_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options = null!;

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(message));

        Assert.Empty(stub.Requests);
    }
}
