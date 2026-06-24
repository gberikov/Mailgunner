using System.Net;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

public class WebhookUpdateTests
{
    [Fact]
    public async Task Update_puts_the_new_urls_to_the_per_event_type_endpoint_and_returns_the_registration()
    {
        var (client, stub) = WebhookHarness.BuildClient(HttpStatusCode.OK, WebhookHarness.Envelope("https://new"));
        var urls = new[] { "https://new" };

        var registration = await client.Webhooks.UpdateAsync(WebhookEventType.Delivered, urls);

        Assert.Equal(HttpMethod.Put, stub.LastMethod);
        Assert.EndsWith($"/v3/{WebhookHarness.Domain}/webhooks/delivered", stub.LastRequestUri!.AbsolutePath);
        Assert.Equal(urls, stub.Requests[0].Values("url"));
        Assert.Equal(0, stub.Requests[0].Count("id"));
        Assert.Equal(WebhookEventType.Delivered, registration.EventType);
        Assert.Equal(urls, registration.Urls);
    }

    [Fact]
    public async Task Update_carries_multiple_urls()
    {
        var (client, stub) = WebhookHarness.BuildClient(
            HttpStatusCode.OK, WebhookHarness.Envelope("https://a", "https://b"));
        var urls = new[] { "https://a", "https://b" };

        await client.Webhooks.UpdateAsync(WebhookEventType.Clicked, urls);

        Assert.Equal(2, stub.Requests[0].Count("url"));
        Assert.Equal(urls, stub.Requests[0].Values("url"));
    }

    [Fact]
    public async Task Update_of_an_unregistered_event_type_surfaces_the_typed_error()
    {
        const string body = "{\"message\":\"Webhook not found\"}";
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.NotFound, body);
        var urls = new[] { "https://new" };

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Webhooks.UpdateAsync(WebhookEventType.Delivered, urls));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }

    [Fact]
    public async Task Update_with_empty_urls_throws_and_issues_no_request()
    {
        var (client, stub) = WebhookHarness.BuildClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Webhooks.UpdateAsync(WebhookEventType.Delivered, Array.Empty<string>()));

        Assert.Empty(stub.Requests);
    }
}
