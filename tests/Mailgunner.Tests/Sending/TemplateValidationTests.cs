using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class TemplateValidationTests
{
    private const string Domain = "mg.example.com";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"id\":\"<x>\",\"message\":\"Queued. Thank you.\"}");
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
        };
        message.To.Add("alice@example.com");
        return message;
    }

    [Fact]
    public async Task Template_with_inline_text_is_rejected_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Template = "welcome";
        message.Text = "Hello";

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Null(stub.LastRequest);
    }

    [Fact]
    public async Task Template_with_inline_html_is_rejected_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Template = "welcome";
        message.Html = "<p>Hello</p>";

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Null(stub.LastRequest);
    }

    [Fact]
    public async Task Variables_without_a_template_name_are_rejected_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.TemplateVariables["product"] = "Acme";

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Null(stub.LastRequest);
    }

    [Fact]
    public async Task Version_without_a_template_name_is_rejected_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.TemplateVersion = "v2";

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Null(stub.LastRequest);
    }

    [Fact]
    public async Task Generate_text_request_without_a_template_name_is_rejected_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.GenerateTextFromTemplate = true;

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Null(stub.LastRequest);
    }

    [Fact]
    public async Task Template_alone_without_inline_body_is_valid()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Template = "welcome";

        var result = await client.SendAsync(message);

        Assert.Equal("<x>", result.Id);
        Assert.NotNull(stub.LastRequest);
    }

    [Fact]
    public async Task Neither_template_nor_body_is_rejected_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage(); // no Template, no Text/Html

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Null(stub.LastRequest);
    }
}
