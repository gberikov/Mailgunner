using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class TemplateTextTests
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
    public async Task Generated_text_request_sets_the_flag_to_yes()
    {
        var (client, stub) = BuildClient();
        var message = NewTemplatedMessage();
        message.GenerateTextFromTemplate = true;

        await client.SendAsync(message);

        Assert.Equal("yes", FieldValue(stub, "t:text"));
    }

    [Fact]
    public async Task No_generated_text_request_omits_the_flag()
    {
        var (client, stub) = BuildClient();

        await client.SendAsync(NewTemplatedMessage());

        Assert.Equal(0, FieldCount(stub, "t:text"));
    }
}
