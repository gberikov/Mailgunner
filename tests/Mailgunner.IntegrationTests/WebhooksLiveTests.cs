using Xunit;

namespace Mailgunner.IntegrationTests;

public class WebhooksLiveTests
{
    [SkippableFact]
    public async Task Create_get_update_list_delete_round_trip()
    {
        Skip.If(Live.Client is null, Live.NotConfigured);
        var client = Live.Client!;
        const WebhookEventType type = WebhookEventType.TemporaryFail;
        var url = $"https://example.com/hooks/{Guid.NewGuid():N}";

        // Snapshot whatever is already registered for this event type (there is no way to
        // namespace a webhook the way a random suppression address namespaces itself — it is one
        // whole-domain registration per event type) so it can be restored, not destroyed.
        WebhookRegistration? previous = null;
        try
        {
            previous = await client.Webhooks.GetAsync(type);
            await client.Webhooks.DeleteAsync(type);
        }
        catch (MailgunnerException getEx) when (getEx.StatusCode == 404)
        {
        }

        try
        {
            var created = await client.Webhooks.CreateAsync(type, new[] { url });
            Assert.Contains(url, created.Urls);

            var updated = await client.Webhooks.UpdateAsync(type, new[] { url + "/v2" });
            Assert.Contains(url + "/v2", updated.Urls);

            var listed = await client.Webhooks.ListAsync();
            Assert.Contains(listed, r => r.EventType == type);

            await client.Webhooks.DeleteAsync(type);
            var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.Webhooks.GetAsync(type));
            Assert.Equal(404, ex.StatusCode);
        }
        finally
        {
            // Restore the pre-existing registration (if any); otherwise leave it deleted, which is
            // the state it was already in. Best-effort: never lets a restore failure mask the
            // assertions above.
            if (previous is { } original)
            {
                await Live.CleanupAsync(() => client.Webhooks.CreateAsync(type, original.Urls));
            }
        }
    }
}
