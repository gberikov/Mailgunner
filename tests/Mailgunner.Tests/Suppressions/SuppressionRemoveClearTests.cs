using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Suppressions;

public class SuppressionRemoveClearTests
{
    private const string Domain = "mg.example.com";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient(
        HttpStatusCode status = HttpStatusCode.OK, string body = "{\"message\":\"Address has been removed\"}")
    {
        var stub = new StubHttpMessageHandler(status, body);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    [Fact]
    public async Task Remove_issues_delete_to_the_per_address_endpoint()
    {
        var (client, stub) = BuildClient();

        await client.Suppressions.Bounces.RemoveAsync("a@x.com");

        Assert.Equal(HttpMethod.Delete, stub.LastMethod);
        Assert.EndsWith(
            $"/v3/{Domain}/bounces/a@x.com",
            Uri.UnescapeDataString(stub.LastRequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task Clear_issues_delete_to_the_list_endpoint_with_no_address()
    {
        var (client, stub) = BuildClient();

        await client.Suppressions.Unsubscribes.ClearAsync();

        Assert.Equal(HttpMethod.Delete, stub.LastMethod);
        Assert.Equal($"/v3/{Domain}/unsubscribes", stub.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Remove_blank_address_throws_argument_exception_and_issues_no_request()
    {
        var (client, stub) = BuildClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Suppressions.Bounces.RemoveAsync(""));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Remove_of_a_missing_address_surfaces_the_typed_error()
    {
        const string body = "{\"message\":\"Address not found\"}";
        var (client, _) = BuildClient(HttpStatusCode.NotFound, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Suppressions.Bounces.RemoveAsync("missing@x.com"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }

    [Fact]
    public async Task Clear_surfaces_the_typed_error_on_non_success()
    {
        const string body = "{\"message\":\"server error\"}";
        var (client, _) = BuildClient(HttpStatusCode.InternalServerError, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Suppressions.Complaints.ClearAsync());

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }
}
