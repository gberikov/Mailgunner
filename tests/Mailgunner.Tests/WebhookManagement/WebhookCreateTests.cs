using System.Net;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

public class WebhookCreateTests
{
    [Fact]
    public async Task Create_single_url_posts_id_and_url_and_returns_the_registration()
    {
        var (client, stub) = WebhookHarness.BuildClient(HttpStatusCode.OK, WebhookHarness.Envelope("https://a"));
        var urls = new[] { "https://a" };

        var registration = await client.Webhooks.CreateAsync(WebhookEventType.Delivered, urls);

        Assert.Equal(HttpMethod.Post, stub.LastMethod);
        Assert.EndsWith($"/v3/{WebhookHarness.Domain}/webhooks", stub.LastRequestUri!.AbsolutePath);
        Assert.Equal(1, stub.Requests[0].Count("id"));
        Assert.Equal("delivered", stub.Requests[0].Value("id"));
        Assert.Equal(urls, stub.Requests[0].Values("url"));
        Assert.Equal(WebhookEventType.Delivered, registration.EventType);
        Assert.Equal(urls, registration.Urls);
    }

    [Fact]
    public async Task Create_multiple_urls_posts_one_id_and_each_url()
    {
        var (client, stub) = WebhookHarness.BuildClient(
            HttpStatusCode.OK, WebhookHarness.Envelope("https://a", "https://b"));
        var urls = new[] { "https://a", "https://b" };

        var registration = await client.Webhooks.CreateAsync(WebhookEventType.Clicked, urls);

        Assert.Equal("clicked", stub.Requests[0].Value("id"));
        Assert.Equal(2, stub.Requests[0].Count("url"));
        Assert.Equal(urls, stub.Requests[0].Values("url"));
        Assert.Equal(urls, registration.Urls);
    }

    [Fact]
    public async Task Create_non_success_surfaces_the_typed_error_with_status_and_body()
    {
        const string body = "{\"message\":\"Webhook already exists\"}";
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.BadRequest, body);
        var urls = new[] { "https://a" };

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Webhooks.CreateAsync(WebhookEventType.Delivered, urls));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }
}
