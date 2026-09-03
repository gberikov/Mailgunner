using Xunit;

namespace Mailgunner.IntegrationTests;

public class SuppressionsLiveTests
{
    [SkippableFact]
    public async Task Bounce_add_get_list_remove_round_trip()
    {
        Skip.If(Live.Client is null, Live.NotConfigured);
        var client = Live.Client!;
        var address = $"live-{Guid.NewGuid():N}@example.com";

        await client.Suppressions.Bounces.AddAsync(new Bounce { Address = address, Code = "550", Error = "live test" });
        try
        {
            var fetched = await client.Suppressions.Bounces.GetAsync(address);
            Assert.Equal(address, fetched.Address);
            Assert.NotNull(fetched.CreatedAt); // "UTC" timestamps must parse

            var listed = new List<Bounce>();
            await foreach (var b in client.Suppressions.Bounces.ListAsync(pageSize: 1000)) { listed.Add(b); }
            Assert.Contains(listed, b => b.Address == address);
        }
        finally
        {
            // Best-effort: never lets a cleanup failure mask the assertions above.
            await Live.CleanupAsync(() => client.Suppressions.Bounces.RemoveAsync(address));
        }

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.Suppressions.Bounces.GetAsync(address));
        Assert.Equal(404, ex.StatusCode);
    }

    [SkippableFact]
    public async Task Unsubscribe_add_range_and_clear_entries()
    {
        Skip.If(Live.Client is null, Live.NotConfigured);
        var client = Live.Client!;
        var a = $"live-{Guid.NewGuid():N}@example.com";
        var b = $"live-{Guid.NewGuid():N}@example.com";
        var allTags = new[] { "*" };
        var newsletterTags = new[] { "newsletter" };

        await client.Suppressions.Unsubscribes.AddRangeAsync(new[]
        {
            new Unsubscribe { Address = a, Tags = allTags },
            new Unsubscribe { Address = b, Tags = newsletterTags },
        });
        try
        {
            Assert.Equal(a, (await client.Suppressions.Unsubscribes.GetAsync(a)).Address);
        }
        finally
        {
            // Each removal is isolated: one throwing does not stop the other from being attempted,
            // and neither can mask the assertion above.
            await Live.CleanupAsync(() => client.Suppressions.Unsubscribes.RemoveAsync(a));
            await Live.CleanupAsync(() => client.Suppressions.Unsubscribes.RemoveAsync(b));
        }
    }
}
