using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class BatchInlineBodyTests
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

    [Fact]
    public async Task Inline_text_and_html_batch_emits_body_parts_and_recipient_variables_without_a_template()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage
        {
            From = "noreply@mg.example.com",
            Subject = "Hi %recipient.name%",
            Text = "Hello %recipient.name%",
            Html = "<p>Hello %recipient.name%</p>",
        };
        var ada = new BatchRecipient("ada@example.com");
        ada.Variables["name"] = "Ada";
        batch.Recipients.Add(ada);

        await client.SendBatchAsync(batch);

        var request = Assert.Single(stub.Requests);
        Assert.Equal("Hello %recipient.name%", request.Value("text"));
        Assert.Equal("<p>Hello %recipient.name%</p>", request.Value("html"));
        Assert.Equal("{\"ada@example.com\":{\"name\":\"Ada\"}}", request.Value("recipient-variables"));
        Assert.Null(request.Value("template"));
    }

    [Fact]
    public async Task Template_and_inline_body_together_throw_before_any_request()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage { From = "noreply@mg.example.com", Template = "t", Text = "x" };
        batch.Recipients.Add(new BatchRecipient("a@example.com"));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendBatchAsync(batch));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Template_data_without_a_template_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage { From = "noreply@mg.example.com", Text = "x", GenerateTextFromTemplate = true };
        batch.Recipients.Add(new BatchRecipient("a@example.com"));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendBatchAsync(batch));

        Assert.Empty(stub.Requests);
    }
}
