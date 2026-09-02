using Xunit;

namespace Mailgunner.IntegrationTests;

public class SuppressionsLiveTests
{
    [Fact]
    public async Task Bounce_add_get_list_remove_round_trip()
    {
        if (Live.Client is not { } client) { return; }
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
            // Runs even on assertion failure above, so the suppression entry never outlives the test.
            await client.Suppressions.Bounces.RemoveAsync(address);
        }

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.Suppressions.Bounces.GetAsync(address));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Unsubscribe_add_range_and_clear_entries()
    {
        if (Live.Client is not { } client) { return; }
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
            // Runs even on assertion failure above, so both entries never outlive the test.
            await client.Suppressions.Unsubscribes.RemoveAsync(a);
            await client.Suppressions.Unsubscribes.RemoveAsync(b);
        }
    }
}
