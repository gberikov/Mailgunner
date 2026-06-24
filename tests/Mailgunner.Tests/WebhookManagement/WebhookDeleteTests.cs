using System.Net;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

public class WebhookDeleteTests
{
    [Fact]
    public async Task Delete_issues_delete_to_the_per_event_type_endpoint()
    {
        var (client, stub) = WebhookHarness.BuildClient(HttpStatusCode.OK, "{\"message\":\"Webhook has been deleted\"}");

        await client.Webhooks.DeleteAsync(WebhookEventType.Clicked);

        Assert.Equal(HttpMethod.Delete, stub.LastMethod);
        Assert.EndsWith($"/v3/{WebhookHarness.Domain}/webhooks/clicked", stub.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Delete_of_an_unregistered_event_type_surfaces_the_typed_error()
    {
        const string body = "{\"message\":\"Webhook not found\"}";
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.NotFound, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Webhooks.DeleteAsync(WebhookEventType.Clicked));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }
}
