using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Suppressions;

public class SuppressionModelTests
{
    private const string Domain = "mg.example.com";

    private static IMailgunnerClient BuildClient(string body)
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, body);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        return services.BuildServiceProvider().GetRequiredService<IMailgunnerClient>();
    }

    [Fact]
    public async Task Bounce_parses_address_code_error_and_created_at()
    {
        var client = BuildClient(
            "{\"items\":[{\"address\":\"a@x.com\",\"code\":\"550\",\"error\":\"Mailbox full\","
            + "\"created_at\":\"Fri, 21 Oct 2011 11:02:55 GMT\"}],\"paging\":{}}");

        var page = await client.Suppressions.Bounces.ListPageAsync();

        var b = Assert.Single(page.Items);
        Assert.Equal("a@x.com", b.Address);
        Assert.Equal("550", b.Code);
        Assert.Equal("Mailbox full", b.Error);
        Assert.Equal(new DateTimeOffset(2011, 10, 21, 11, 2, 55, TimeSpan.Zero), b.CreatedAt);
    }

    [Fact]
    public async Task Unsubscribe_parses_address_tags_and_created_at()
    {
        var client = BuildClient(
            "{\"items\":[{\"address\":\"u@x.com\",\"tags\":[\"newsletter\",\"promos\"],"
            + "\"created_at\":\"Fri, 21 Oct 2011 11:02:55 GMT\"}],\"paging\":{}}");

        var page = await client.Suppressions.Unsubscribes.ListPageAsync();

        var u = Assert.Single(page.Items);
        Assert.Equal("u@x.com", u.Address);
        Assert.Equal(2, u.Tags.Count);
        Assert.Equal("newsletter", u.Tags[0]);
        Assert.Equal("promos", u.Tags[1]);
        Assert.NotNull(u.CreatedAt);
    }

    [Fact]
    public async Task Complaint_parses_address_and_created_at()
    {
        var client = BuildClient(
            "{\"items\":[{\"address\":\"c@x.com\",\"created_at\":\"Fri, 21 Oct 2011 11:02:55 GMT\"}],\"paging\":{}}");

        var page = await client.Suppressions.Complaints.ListPageAsync();

        var c = Assert.Single(page.Items);
        Assert.Equal("c@x.com", c.Address);
        Assert.Equal(new DateTimeOffset(2011, 10, 21, 11, 2, 55, TimeSpan.Zero), c.CreatedAt);
    }

    [Fact]
    public async Task Absent_created_at_yields_null()
    {
        var client = BuildClient("{\"items\":[{\"address\":\"a@x.com\",\"code\":\"550\"}],\"paging\":{}}");

        var page = await client.Suppressions.Bounces.ListPageAsync();

        Assert.Null(Assert.Single(page.Items).CreatedAt);
    }

    [Fact]
    public async Task Unparseable_created_at_yields_null_and_does_not_throw()
    {
        var client = BuildClient(
            "{\"items\":[{\"address\":\"a@x.com\",\"created_at\":\"not-a-real-date\"}],\"paging\":{}}");

        var page = await client.Suppressions.Bounces.ListPageAsync();

        Assert.Null(Assert.Single(page.Items).CreatedAt);
    }
}
