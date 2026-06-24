using System.Net;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

public class WebhookRoutingTests
{
    [Fact]
    public async Task Us_region_routes_to_the_us_host_with_the_configured_domain_and_basic_auth()
    {
        var (client, stub) = WebhookHarness.BuildClient(
            HttpStatusCode.OK, WebhookHarness.EmptyList, MailgunRegion.Us);

        await client.Webhooks.ListAsync();

        Assert.Equal("api.mailgun.net", stub.LastRequestUri!.Host);
        Assert.Contains($"/v3/{WebhookHarness.Domain}/webhooks", stub.LastRequestUri!.AbsolutePath);
        Assert.Equal("Basic", stub.LastRequest!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task Eu_region_routes_to_the_eu_host()
    {
        var (client, stub) = WebhookHarness.BuildClient(
            HttpStatusCode.OK, WebhookHarness.EmptyList, MailgunRegion.Eu);

        await client.Webhooks.ListAsync();

        Assert.Equal("api.eu.mailgun.net", stub.LastRequestUri!.Host);
    }

    [Fact]
    public async Task Per_event_type_operations_carry_the_configured_domain()
    {
        var (client, stub) = WebhookHarness.BuildClient(
            HttpStatusCode.OK, WebhookHarness.Envelope("https://a"), MailgunRegion.Us, "other.example.org");

        await client.Webhooks.GetAsync(WebhookEventType.Delivered);

        Assert.EndsWith("/v3/other.example.org/webhooks/delivered", stub.LastRequestUri!.AbsolutePath);
    }
}
