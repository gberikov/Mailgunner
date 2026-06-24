using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Suppressions;

/// <summary>
/// Demonstrates that the suppressions capability is independent of the sending pipeline: it is reachable
/// through <c>client.Suppressions.*</c> and exercised end-to-end without any send call, targeting the
/// suppression endpoints (never <c>/messages</c>).
/// </summary>
public class SuppressionIndependenceTests
{
    private const string Domain = "mg.example.com";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient(string body)
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, body);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    [Fact]
    public async Task Listing_works_without_any_send_and_targets_the_suppression_endpoint()
    {
        var (client, stub) = BuildClient(
            "{\"items\":[{\"address\":\"a@x.com\"}],\"paging\":{}}");

        var page = await client.Suppressions.Bounces.ListPageAsync();

        Assert.Single(page.Items);
        Assert.Contains("/bounces", stub.LastRequestUri!.AbsolutePath);
        Assert.DoesNotContain("/messages", stub.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task All_operations_are_reachable_through_the_suppressions_accessor()
    {
        var (client, _) = BuildClient("{\"items\":[],\"paging\":{}}");

        // Each call resolving and completing against the fake demonstrates the surface is wired,
        // with no dependency on SendAsync/SendBatchAsync.
        await client.Suppressions.Bounces.ListPageAsync();
        await client.Suppressions.Unsubscribes.ListPageAsync();
        await client.Suppressions.Complaints.ListPageAsync();

        Assert.Same(client.Suppressions, client.Suppressions);
    }
}
