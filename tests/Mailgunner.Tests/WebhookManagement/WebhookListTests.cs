using System.Net;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

public class WebhookListTests
{
    [Fact]
    public async Task List_returns_one_registration_per_registered_event_type()
    {
        const string body =
            "{\"webhooks\":{\"delivered\":{\"urls\":[\"https://a\"]},"
            + "\"opened\":{\"urls\":[\"https://b\",\"https://c\"]}}}";
        var (client, stub) = WebhookHarness.BuildClient(HttpStatusCode.OK, body);

        var registrations = await client.Webhooks.ListAsync();

        Assert.Equal(HttpMethod.Get, stub.LastMethod);
        Assert.EndsWith($"/v3/{WebhookHarness.Domain}/webhooks", stub.LastRequestUri!.AbsolutePath);
        Assert.Equal(2, registrations.Count);

        var delivered = Assert.Single(registrations, r => r.EventType == WebhookEventType.Delivered);
        Assert.Equal("https://a", Assert.Single(delivered.Urls));

        var opened = Assert.Single(registrations, r => r.EventType == WebhookEventType.Opened);
        var expectedOpened = new[] { "https://b", "https://c" };
        Assert.Equal(expectedOpened, opened.Urls);
    }

    [Fact]
    public async Task List_of_a_domain_with_no_webhooks_returns_empty_without_error()
    {
        var (client, stub) = WebhookHarness.BuildClient(HttpStatusCode.OK, WebhookHarness.EmptyList);

        var registrations = await client.Webhooks.ListAsync();

        Assert.Empty(registrations);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task List_non_success_surfaces_the_typed_error()
    {
        const string body = "{\"message\":\"Domain not found\"}";
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.BadRequest, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.Webhooks.ListAsync());

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }
}
