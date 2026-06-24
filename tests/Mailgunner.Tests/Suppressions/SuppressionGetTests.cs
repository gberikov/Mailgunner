using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Suppressions;

public class SuppressionGetTests
{
    private const string Domain = "mg.example.com";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient(
        HttpStatusCode status, string body)
    {
        var stub = new StubHttpMessageHandler(status, body);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    [Fact]
    public async Task Get_issues_get_to_the_per_address_endpoint_and_returns_the_typed_model()
    {
        var (client, stub) = BuildClient(
            HttpStatusCode.OK,
            "{\"address\":\"a@x.com\",\"code\":\"550\",\"error\":\"Mailbox full\","
            + "\"created_at\":\"Fri, 21 Oct 2011 11:02:55 GMT\"}");

        var bounce = await client.Suppressions.Bounces.GetAsync("a@x.com");

        Assert.Equal(HttpMethod.Get, stub.LastMethod);
        Assert.EndsWith(
            $"/v3/{Domain}/bounces/a@x.com",
            Uri.UnescapeDataString(stub.LastRequestUri!.AbsolutePath));
        Assert.Equal("a@x.com", bounce.Address);
        Assert.Equal("550", bounce.Code);
        Assert.Equal("Mailbox full", bounce.Error);
    }

    [Fact]
    public async Task Get_of_a_missing_address_surfaces_the_typed_error()
    {
        const string body = "{\"message\":\"Address not found\"}";
        var (client, _) = BuildClient(HttpStatusCode.NotFound, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Suppressions.Bounces.GetAsync("missing@x.com"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }

    [Fact]
    public async Task Blank_address_throws_argument_exception_and_issues_no_request()
    {
        var (client, stub) = BuildClient(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Suppressions.Bounces.GetAsync(" "));

        Assert.Empty(stub.Requests);
    }
}
