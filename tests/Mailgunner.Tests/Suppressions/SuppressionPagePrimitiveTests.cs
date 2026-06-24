using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Suppressions;

public class SuppressionPagePrimitiveTests
{
    private const string Domain = "mg.example.com";
    private const string Next = "https://api.mailgun.net/v3/mg.example.com/bounces?page=next&p=2";

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
    public async Task First_page_returns_items_and_opaque_next_cursor()
    {
        var (client, _) = BuildClient(
            $"{{\"items\":[{{\"address\":\"a@x.com\"}},{{\"address\":\"b@x.com\"}}],\"paging\":{{\"next\":\"{Next}\"}}}}");

        var page = await client.Suppressions.Bounces.ListPageAsync();

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(Next, page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Following_the_cursor_issues_a_get_to_the_cursor_url_verbatim()
    {
        var (client, stub) = BuildClient("{\"items\":[{\"address\":\"c@x.com\"}],\"paging\":{}}");

        var page = await client.Suppressions.Bounces.ListPageAsync(Next);

        Assert.Equal(Next, stub.LastRequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, stub.LastMethod);
        Assert.Equal("c@x.com", Assert.Single(page.Items).Address);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Blank_cursor_throws_argument_exception_and_issues_no_request()
    {
        var (client, stub) = BuildClient("{\"items\":[],\"paging\":{}}");

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Suppressions.Bounces.ListPageAsync("  "));

        Assert.Empty(stub.Requests);
    }
}
