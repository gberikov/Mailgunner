using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Suppressions;

public class SuppressionPageSizeTests
{
    private const string Domain = "mg.example.com";

    // A next pointer that deliberately carries NO limit, so we can prove the library does not re-apply one.
    private const string NextNoLimit = "https://api.mailgun.net/v3/mg.example.com/bounces?page=next&p=2";

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
    public async Task Page_size_is_applied_to_the_first_request()
    {
        var (client, stub) = BuildClient("{\"items\":[],\"paging\":{}}");

        await client.Suppressions.Bounces.ListPageAsync(pageSize: 250);

        Assert.Contains("limit=250", stub.LastRequestUri!.Query);
    }

    [Fact]
    public async Task Omitting_page_size_sends_no_limit()
    {
        var (client, stub) = BuildClient("{\"items\":[],\"paging\":{}}");

        await client.Suppressions.Bounces.ListPageAsync();

        Assert.DoesNotContain("limit", stub.LastRequestUri!.Query);
    }

    [Fact]
    public async Task Page_size_is_not_re_applied_to_followed_next_pointer()
    {
        var (client, stub) = BuildClient("{\"items\":[],\"paging\":{}}");
        stub.ResponseSelector = i => i == 0
            ? (HttpStatusCode.OK, $"{{\"items\":[{{\"address\":\"a@x.com\"}}],\"paging\":{{\"next\":\"{NextNoLimit}\"}}}}")
            : (HttpStatusCode.OK, "{\"items\":[],\"paging\":{}}");

        await foreach (var _ in client.Suppressions.Bounces.ListAsync(pageSize: 250))
        {
        }

        Assert.Contains("limit=250", stub.Requests[0].RequestUri!.Query);
        Assert.Equal(NextNoLimit, stub.Requests[1].RequestUri!.ToString());
    }
}
