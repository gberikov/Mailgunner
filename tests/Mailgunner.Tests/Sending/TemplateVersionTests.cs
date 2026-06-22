using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class TemplateVersionTests
{
    private const string Domain = "mg.example.com";
    private const string SuccessBody = "{\"id\":\"<x>\",\"message\":\"Queued. Thank you.\"}";

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

    private static MailgunMessage NewTemplatedMessage()
    {
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com", "Example"),
            Template = "welcome",
        };
        message.To.Add("alice@example.com");
        return message;
    }

    private static int FieldCount(StubHttpMessageHandler stub, string name)
    {
        var count = 0;
        foreach (var field in stub.LastFormData)
        {
            if (field.Name == name)
            {
                count++;
            }
        }

        return count;
    }

    private static string? FieldValue(StubHttpMessageHandler stub, string name)
    {
        foreach (var field in stub.LastFormData)
        {
            if (field.Name == name)
            {
                return field.Value;
            }
        }

        return null;
    }

    [Fact]
    public async Task Version_is_emitted_when_set()
    {
        var (client, stub) = BuildClient();
        var message = NewTemplatedMessage();
        message.TemplateVersion = "v2";

        await client.SendAsync(message);

        Assert.Equal("v2", FieldValue(stub, "t:version"));
    }

    [Fact]
    public async Task Version_is_omitted_when_null()
    {
        var (client, stub) = BuildClient();

        await client.SendAsync(NewTemplatedMessage());

        Assert.Equal(0, FieldCount(stub, "t:version"));
        Assert.Equal("welcome", FieldValue(stub, "template"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Version_is_omitted_when_blank(string version)
    {
        var (client, stub) = BuildClient();
        var message = NewTemplatedMessage();
        message.TemplateVersion = version;

        await client.SendAsync(message);

        Assert.Equal(0, FieldCount(stub, "t:version"));
    }
}
