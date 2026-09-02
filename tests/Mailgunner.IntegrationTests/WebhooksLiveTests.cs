using Xunit;

namespace Mailgunner.IntegrationTests;

public class WebhooksLiveTests
{
    [Fact]
    public async Task Create_get_update_list_delete_round_trip()
    {
        if (Live.Client is not { } client) { return; }
        const WebhookEventType type = WebhookEventType.TemporaryFail;
        var url = $"https://example.com/hooks/{Guid.NewGuid():N}";

        try { await client.Webhooks.DeleteAsync(type); } catch (MailgunnerException cleanupEx) when (cleanupEx.StatusCode == 404) { }

        try
        {
            var created = await client.Webhooks.CreateAsync(type, new[] { url });
            Assert.Contains(url, created.Urls);

            var updated = await client.Webhooks.UpdateAsync(type, new[] { url + "/v2" });
            Assert.Contains(url + "/v2", updated.Urls);

            var listed = await client.Webhooks.ListAsync();
            Assert.Contains(listed, r => r.EventType == type);
        }
        finally
        {
            // Runs even on assertion failure above, so the test registration never outlives the test.
            try { await client.Webhooks.DeleteAsync(type); } catch (MailgunnerException cleanupEx) when (cleanupEx.StatusCode == 404) { }
        }

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.Webhooks.GetAsync(type));
        Assert.Equal(404, ex.StatusCode);
    }
}
