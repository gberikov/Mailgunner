using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class SendMessageTests
{
    private const string Domain = "mg.example.com";
    private const string SuccessBody = "{\"id\":\"<20260622.1@mg.example.com>\",\"message\":\"Queued. Thank you.\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient(
        HttpStatusCode statusCode, string responseBody)
    {
        var stub = new StubHttpMessageHandler(statusCode, responseBody);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMailgunnerClient>();
        return (client, stub);
    }

    private static MailgunMessage NewMessage()
    {
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com", "Example"),
            Subject = "Hello",
            Text = "Hi there!",
        };
        message.To.Add("alice@example.com");
        return message;
    }

    [Fact]
    public async Task Success_response_yields_result_with_id_and_message()
    {
        var (client, _) = BuildClient(HttpStatusCode.OK, SuccessBody);

        var result = await client.SendAsync(NewMessage());

        Assert.Equal("<20260622.1@mg.example.com>", result.Id);
        Assert.Equal("Queued. Thank you.", result.Message);
    }

    [Fact]
    public async Task Html_body_without_text_is_carried_and_returns_a_result()
    {
        var (client, stub) = BuildClient(HttpStatusCode.OK, SuccessBody);
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com"),
            Subject = "Hello",
            Html = "<p>Hi there!</p>",
        };
        message.To.Add("alice@example.com");

        var result = await client.SendAsync(message);

        Assert.Equal("<20260622.1@mg.example.com>", result.Id);
        var html = Assert.Single(stub.LastFormData, f => f.Name == "html");
        Assert.Equal("<p>Hi there!</p>", html.Value);
        Assert.DoesNotContain(stub.LastFormData, f => f.Name == "text");
    }

    [Fact]
    public async Task Html_and_text_are_both_carried_when_both_are_set()
    {
        var (client, stub) = BuildClient(HttpStatusCode.OK, SuccessBody);
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com"),
            Text = "Hi there!",
            Html = "<p>Hi there!</p>",
        };
        message.To.Add("alice@example.com");

        await client.SendAsync(message);

        Assert.Contains(stub.LastFormData, f => f.Name == "text" && f.Value == "Hi there!");
        Assert.Contains(stub.LastFormData, f => f.Name == "html" && f.Value == "<p>Hi there!</p>");
    }

    [Fact]
    public async Task Request_is_a_multipart_post_to_the_messages_endpoint()
    {
        var (client, stub) = BuildClient(HttpStatusCode.OK, SuccessBody);

        await client.SendAsync(NewMessage());

        Assert.Equal(HttpMethod.Post, stub.LastMethod);
        Assert.Equal($"/v3/{Domain}/messages", stub.LastRequestUri!.AbsolutePath);
        Assert.Equal("multipart/form-data", stub.LastContentMediaType);
    }

    [Fact]
    public async Task Subject_part_is_emitted_when_set()
    {
        var (client, stub) = BuildClient(HttpStatusCode.OK, SuccessBody);

        await client.SendAsync(NewMessage());

        var subject = Assert.Single(stub.LastFormData, f => f.Name == "subject");
        Assert.Equal("Hello", subject.Value);
    }

    [Fact]
    public async Task Subject_part_is_absent_when_not_set()
    {
        var (client, stub) = BuildClient(HttpStatusCode.OK, SuccessBody);
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com"),
            Text = "Hi there!",
        };
        message.To.Add("alice@example.com");

        await client.SendAsync(message);

        Assert.DoesNotContain(stub.LastFormData, f => f.Name == "subject");
    }

    [Fact]
    public async Task From_part_carries_the_formatted_sender()
    {
        var (client, stub) = BuildClient(HttpStatusCode.OK, SuccessBody);

        await client.SendAsync(NewMessage());

        var from = Assert.Single(stub.LastFormData, f => f.Name == "from");
        Assert.Equal("Example <noreply@mg.example.com>", from.Value);
    }
}
