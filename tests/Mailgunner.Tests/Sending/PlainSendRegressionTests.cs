using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class PlainSendRegressionTests
{
    private const string Domain = "mg.example.com";
    private const string SuccessBody = "{\"id\":\"<x>\",\"message\":\"Queued. Thank you.\"}";

    private static readonly string[] TemplateFieldNames =
        { "template", "t:version", "t:text", "t:variables" };

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMailgunnerClient>();
        return (client, stub);
    }

    private static bool HasField(StubHttpMessageHandler stub, string name)
    {
        foreach (var field in stub.LastFormData)
        {
            if (field.Name == name)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public async Task Plain_text_send_carries_no_template_fields()
    {
        var (client, stub) = BuildClient();
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com", "Example"),
            Subject = "Hello",
            Text = "Hi there!",
        };
        message.To.Add("alice@example.com");

        var result = await client.SendAsync(message);

        Assert.Equal("<x>", result.Id);
        Assert.True(HasField(stub, "text"));
        foreach (var name in TemplateFieldNames)
        {
            Assert.False(HasField(stub, name), $"plain send must not emit '{name}'");
        }
    }

    [Fact]
    public async Task Plain_html_send_carries_no_template_fields()
    {
        var (client, stub) = BuildClient();
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com", "Example"),
            Html = "<p>Hi</p>",
        };
        message.To.Add("alice@example.com");

        await client.SendAsync(message);

        Assert.True(HasField(stub, "html"));
        foreach (var name in TemplateFieldNames)
        {
            Assert.False(HasField(stub, name), $"plain send must not emit '{name}'");
        }
    }
}
