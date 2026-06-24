using System.Net;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

/// <summary>
/// Demonstrates that the webhook-management capability is independent of the sending pipeline and of
/// signature verification: it is reachable through <c>client.Webhooks.*</c> and exercised end-to-end
/// with no send call, targeting only the webhook endpoints (never <c>/messages</c>).
/// </summary>
public class WebhookIndependenceTests
{
    [Fact]
    public async Task Full_crud_round_trip_works_without_any_send_and_targets_only_webhook_endpoints()
    {
        var (client, stub) = WebhookHarness.BuildClient(HttpStatusCode.OK, WebhookHarness.Envelope("https://a"));

        var createUrls = new[] { "https://a" };
        var updateUrls = new[] { "https://b" };
        await client.Webhooks.CreateAsync(WebhookEventType.Delivered, createUrls);
        await client.Webhooks.ListAsync();
        await client.Webhooks.GetAsync(WebhookEventType.Delivered);
        await client.Webhooks.UpdateAsync(WebhookEventType.Delivered, updateUrls);
        await client.Webhooks.DeleteAsync(WebhookEventType.Delivered);

        foreach (var request in stub.Requests)
        {
            Assert.Contains("/webhooks", request.RequestUri!.AbsolutePath);
            Assert.DoesNotContain("/messages", request.RequestUri!.AbsolutePath);
        }
    }

    [Fact]
    public void Webhooks_accessor_is_stable()
    {
        var (client, _) = WebhookHarness.BuildClient();

        Assert.Same(client.Webhooks, client.Webhooks);
    }
}
