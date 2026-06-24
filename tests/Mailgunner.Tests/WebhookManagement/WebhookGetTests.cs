using System.Net;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

public class WebhookGetTests
{
    [Fact]
    public async Task Get_issues_get_to_the_per_event_type_endpoint_and_returns_the_registration()
    {
        var (client, stub) = WebhookHarness.BuildClient(HttpStatusCode.OK, WebhookHarness.Envelope("https://a"));

        var registration = await client.Webhooks.GetAsync(WebhookEventType.Opened);

        Assert.Equal(HttpMethod.Get, stub.LastMethod);
        Assert.EndsWith($"/v3/{WebhookHarness.Domain}/webhooks/opened", stub.LastRequestUri!.AbsolutePath);
        Assert.Equal(WebhookEventType.Opened, registration.EventType);
        Assert.Equal("https://a", Assert.Single(registration.Urls));
    }

    [Fact]
    public async Task Get_of_an_unregistered_event_type_surfaces_the_typed_error()
    {
        const string body = "{\"message\":\"Webhook not found\"}";
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.NotFound, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Webhooks.GetAsync(WebhookEventType.Opened));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }
}
