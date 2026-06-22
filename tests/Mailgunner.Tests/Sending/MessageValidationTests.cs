using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class MessageValidationTests
{
    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"id\":\"<x>\",\"message\":\"Queued.\"}");
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    [Fact]
    public async Task Missing_sender_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = new MailgunMessage { Text = "Hi" };
        message.To.Add("alice@example.com");

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(message));
        Assert.Null(stub.LastRequest);
    }

    [Fact]
    public async Task No_recipient_across_to_cc_bcc_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com"),
            Text = "Hi",
        };

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(message));
        Assert.Null(stub.LastRequest);
    }

    [Fact]
    public async Task No_body_part_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = new MailgunMessage { From = new EmailAddress("noreply@mg.example.com") };
        message.To.Add("alice@example.com");

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(message));
        Assert.Null(stub.LastRequest);
    }

    [Fact]
    public async Task Null_message_throws_ArgumentNullException()
    {
        var (client, stub) = BuildClient();

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendAsync(null!));
        Assert.Null(stub.LastRequest);
    }
}
