using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Suppressions;

public class SuppressionListPaginationTests
{
    private const string Domain = "mg.example.com";
    private const string Next1 = "https://api.mailgun.net/v3/mg.example.com/bounces?page=next&p=2";
    private const string Next2 = "https://api.mailgun.net/v3/mg.example.com/bounces?page=next&p=3";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"items\":[],\"paging\":{}}");
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    private static string Page(string items, string? next) =>
        next is null
            ? $"{{\"items\":[{items}],\"paging\":{{}}}}"
            : $"{{\"items\":[{items}],\"paging\":{{\"next\":\"{next}\"}}}}";

    private static string BounceJson(string address) =>
        $"{{\"address\":\"{address}\",\"code\":\"550\",\"error\":\"e\",\"created_at\":\"Fri, 21 Oct 2011 11:02:55 GMT\"}}";

    [Fact]
    public async Task Single_page_with_no_next_pointer_yields_items_and_issues_one_request()
    {
        var (client, stub) = BuildClient();
        stub.ResponseSelector = _ =>
            (HttpStatusCode.OK, Page($"{BounceJson("a@x.com")},{BounceJson("b@x.com")}", next: null));

        var items = new List<Bounce>();
        await foreach (var b in client.Suppressions.Bounces.ListAsync())
        {
            items.Add(b);
        }

        Assert.Equal(2, items.Count);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Multi_page_follows_next_in_order_until_empty_final_page()
    {
        var (client, stub) = BuildClient();
        stub.ResponseSelector = i => i switch
        {
            0 => (HttpStatusCode.OK, Page($"{BounceJson("a@x.com")},{BounceJson("b@x.com")}", Next1)),
            1 => (HttpStatusCode.OK, Page(BounceJson("c@x.com"), Next2)),
            _ => (HttpStatusCode.OK, Page("", Next2)),
        };

        var addresses = new List<string>();
        await foreach (var b in client.Suppressions.Bounces.ListAsync())
        {
            addresses.Add(b.Address);
        }

        Assert.Equal(3, addresses.Count);
        Assert.Equal("a@x.com", addresses[0]);
        Assert.Equal("b@x.com", addresses[1]);
        Assert.Equal("c@x.com", addresses[2]);
        Assert.Equal(3, stub.Requests.Count);
        Assert.Equal(Next1, stub.Requests[1].RequestUri!.ToString());
        Assert.Equal(Next2, stub.Requests[2].RequestUri!.ToString());
    }

    [Fact]
    public async Task Empty_list_yields_zero_items_and_one_request()
    {
        var (client, stub) = BuildClient();
        stub.ResponseSelector = _ => (HttpStatusCode.OK, Page("", next: null));

        var items = new List<Bounce>();
        await foreach (var b in client.Suppressions.Bounces.ListAsync())
        {
            items.Add(b);
        }

        Assert.Empty(items);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Cancellation_during_enumeration_stops_before_fetching_the_next_page()
    {
        var (client, stub) = BuildClient();
        stub.ResponseSelector = i => i switch
        {
            0 => (HttpStatusCode.OK, Page($"{BounceJson("a@x.com")},{BounceJson("b@x.com")}", Next1)),
            _ => (HttpStatusCode.OK, Page(BounceJson("c@x.com"), Next2)),
        };
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var b in client.Suppressions.Bounces.ListAsync(cancellationToken: cts.Token))
            {
                cts.Cancel();
            }
        });

        Assert.Single(stub.Requests);
    }
}
