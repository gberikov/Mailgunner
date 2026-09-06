using System.Net;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

public class WebhookResponseValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"webhooks\":null}")]
    [InlineData("{\"webhooks\":{\"delivered\":null}}")]
    [InlineData("{\"webhooks\":{\"delivered\":{}}}")]
    [InlineData("{\"webhooks\":{\"delivered\":{\"urls\":[null]}}}")]
    [InlineData("{\"webhooks\":{\"delivered\":{\"urls\":[\"not-a-url\"]}}}")]
    public async Task Invalid_list_responses_are_not_treated_as_unregistered_webhooks(string body)
    {
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.OK, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.Webhooks.ListAsync());

        Assert.Equal(200, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"webhook\":{}}")]
    [InlineData("{\"webhook\":{\"urls\":[]}}")]
    [InlineData("{\"webhook\":{\"urls\":[null]}}")]
    public async Task Invalid_envelopes_do_not_fabricate_successful_registrations(string body)
    {
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.OK, body);
        var urls = new[] { "https://example.com/webhook" };
        Func<Task>[] operations =
        {
            () => client.Webhooks.GetAsync(WebhookEventType.Delivered),
            () => client.Webhooks.CreateAsync(WebhookEventType.Delivered, urls),
            () => client.Webhooks.UpdateAsync(WebhookEventType.Delivered, urls),
        };

        foreach (var operation in operations)
        {
            var ex = await Assert.ThrowsAsync<MailgunnerException>(operation);
            Assert.Equal(body, ex.ResponseBody);
        }
    }

    [Fact]
    public async Task Message_only_acknowledgements_remain_supported_for_mutations()
    {
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.OK, "{\"message\":\"Webhook updated\"}");
        var urls = new[] { "https://example.com/webhook" };

        var created = await client.Webhooks.CreateAsync(WebhookEventType.Delivered, urls);
        var updated = await client.Webhooks.UpdateAsync(WebhookEventType.Delivered, urls);

        Assert.Equal(urls, created.Urls);
        Assert.Equal(urls, updated.Urls);
        await Assert.ThrowsAsync<MailgunnerException>(() => client.Webhooks.GetAsync(WebhookEventType.Delivered));
    }

    [Fact]
    public async Task Future_event_tokens_keep_their_typed_urls_in_the_list()
    {
        const string body = "{\"webhooks\":{\"future_event\":{\"urls\":[\"https://example.com/webhook\"]},\"delivered\":{\"urls\":[]}}}";
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.OK, body);

        var registration = Assert.Single(await client.Webhooks.ListAsync());

        Assert.Equal(WebhookEventType.Unknown, registration.EventType);
        Assert.Equal("future_event", registration.EventToken);
        Assert.Equal("https://example.com/webhook", Assert.Single(registration.Urls));
    }

    [Fact]
    public async Task Invalid_event_in_multi_create_is_rejected_before_any_registration_is_created()
    {
        var (client, stub) = WebhookHarness.BuildClient(HttpStatusCode.OK, WebhookHarness.Envelope("https://example.com/webhook"));
        var events = new[] { WebhookEventType.Delivered, WebhookEventType.Unknown };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Webhooks.CreateAsync(events, "https://example.com/webhook"));

        Assert.Empty(stub.Requests);
    }
}
