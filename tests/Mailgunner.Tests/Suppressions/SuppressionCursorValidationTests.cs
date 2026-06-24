using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Suppressions;

/// <summary>
/// A caller-supplied pagination cursor is sent verbatim and the client carries HTTP Basic auth on
/// every request, so an absolute URL pointing at a foreign host would leak the sending key. These
/// tests pin the guard: only an https cursor on the configured Mailgun host, addressing this very
/// list, is followed; anything else is rejected before any request is issued.
/// </summary>
public class SuppressionCursorValidationTests
{
    private const string Domain = "mg.example.com";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"items\":[],\"paging\":{}}");
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    [Fact]
    public async Task Cursor_to_a_foreign_host_is_rejected_before_any_request()
    {
        var (client, stub) = BuildClient();
        const string foreign = "https://evil.example.com/v3/mg.example.com/bounces?page=next&p=2";

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Suppressions.Bounces.ListPageAsync(foreign));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Cursor_with_a_non_https_scheme_is_rejected_before_any_request()
    {
        var (client, stub) = BuildClient();
        const string insecure = "http://api.mailgun.net/v3/mg.example.com/bounces?page=next&p=2";

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Suppressions.Bounces.ListPageAsync(insecure));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Cursor_for_a_different_list_path_is_rejected_before_any_request()
    {
        var (client, stub) = BuildClient();
        // Same host, but addresses the complaints list rather than this (bounces) list.
        const string otherList = "https://api.mailgun.net/v3/mg.example.com/complaints?page=next&p=2";

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Suppressions.Bounces.ListPageAsync(otherList));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Malformed_cursor_is_rejected_as_argument_exception()
    {
        var (client, stub) = BuildClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Suppressions.Bounces.ListPageAsync("not-a-url"));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Valid_same_host_cursor_for_this_list_is_followed_verbatim()
    {
        var (client, stub) = BuildClient();
        const string valid = "https://api.mailgun.net/v3/mg.example.com/bounces?page=next&p=2";

        var page = await client.Suppressions.Bounces.ListPageAsync(valid);

        Assert.Single(stub.Requests);
        Assert.Equal(valid, stub.Requests[0].RequestUri!.ToString());
        Assert.False(page.HasMore);
    }
}
