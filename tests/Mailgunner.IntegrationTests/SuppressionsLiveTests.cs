using Xunit;

namespace Mailgunner.IntegrationTests;

public class SuppressionsLiveTests
{
    [Fact]
    public async Task Bounce_add_get_list_remove_round_trip()
    {
        if (Live.Client is null)
        {
            Assert.Skip(Live.NotConfigured);
        }
        var ct = TestContext.Current.CancellationToken;
        var client = Live.Client!;
        var address = $"live-{Guid.NewGuid():N}@example.com";

        await client.Suppressions.Bounces.AddAsync(new Bounce { Address = address, Code = "550", Error = "live test" }, ct);
        try
        {
            var fetched = await client.Suppressions.Bounces.GetAsync(address, ct);
            Assert.Equal(address, fetched.Address);
            Assert.NotNull(fetched.CreatedAt); // "UTC" timestamps must parse

            var listed = new List<Bounce>();
            await foreach (var b in client.Suppressions.Bounces.ListAsync(pageSize: 1000, cancellationToken: ct)) { listed.Add(b); }
            Assert.Contains(listed, b => b.Address == address);
        }
        finally
        {
            // Best-effort: never lets a cleanup failure mask the assertions above.
            await Live.CleanupAsync(() => client.Suppressions.Bounces.RemoveAsync(address, ct));
        }

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.Suppressions.Bounces.GetAsync(address, ct));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Unsubscribe_add_range_and_clear_entries()
    {
        if (Live.Client is null)
        {
            Assert.Skip(Live.NotConfigured);
        }
        var ct = TestContext.Current.CancellationToken;
        var client = Live.Client!;
        var a = $"live-{Guid.NewGuid():N}@example.com";
        var b = $"live-{Guid.NewGuid():N}@example.com";
        var allTags = new[] { "*" };
        var newsletterTags = new[] { "newsletter" };

        await client.Suppressions.Unsubscribes.AddRangeAsync(new[]
        {
            new Unsubscribe { Address = a, Tags = allTags },
            new Unsubscribe { Address = b, Tags = newsletterTags },
        }, ct);
        try
        {
            Assert.Equal(a, (await client.Suppressions.Unsubscribes.GetAsync(a, ct)).Address);
        }
        finally
        {
            // Each removal is isolated: one throwing does not stop the other from being attempted,
            // and neither can mask the assertion above.
            await Live.CleanupAsync(() => client.Suppressions.Unsubscribes.RemoveAsync(a, ct));
            await Live.CleanupAsync(() => client.Suppressions.Unsubscribes.RemoveAsync(b, ct));
        }
    }
}
