using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Suppressions;

public class SuppressionAddTests
{
    private const string Domain = "mg.example.com";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient(
        HttpStatusCode status = HttpStatusCode.OK, string body = "{\"message\":\"Address has been added\"}")
    {
        var stub = new StubHttpMessageHandler(status, body);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    [Fact]
    public async Task Add_bounce_posts_json_to_the_bounces_endpoint_with_address_code_and_error()
    {
        var (client, stub) = BuildClient();

        await client.Suppressions.Bounces.AddAsync(
            new Bounce { Address = "a@x.com", Code = "550", Error = "Mailbox full" });

        Assert.Equal(HttpMethod.Post, stub.LastMethod);
        Assert.Equal($"/v3/{Domain}/bounces", stub.LastRequestUri!.AbsolutePath);
        Assert.Equal("application/json", stub.LastContentMediaType);
        Assert.Contains("\"address\":\"a@x.com\"", stub.LastBody);
        Assert.Contains("\"code\":\"550\"", stub.LastBody);
        Assert.Contains("\"error\":\"Mailbox full\"", stub.LastBody);
    }

    [Fact]
    public async Task Add_bounce_without_optional_fields_omits_code_and_error()
    {
        var (client, stub) = BuildClient();

        await client.Suppressions.Bounces.AddAsync(new Bounce { Address = "a@x.com" });

        Assert.Contains("\"address\":\"a@x.com\"", stub.LastBody);
        Assert.DoesNotContain("\"code\"", stub.LastBody);
        Assert.DoesNotContain("\"error\"", stub.LastBody);
    }

    [Fact]
    public async Task Add_unsubscribe_includes_tags_when_supplied()
    {
        var (client, stub) = BuildClient();

        await client.Suppressions.Unsubscribes.AddAsync(
            new Unsubscribe { Address = "u@x.com", Tags = new List<string> { "newsletter" } });

        Assert.Equal($"/v3/{Domain}/unsubscribes", stub.LastRequestUri!.AbsolutePath);
        Assert.Contains("\"address\":\"u@x.com\"", stub.LastBody);
        Assert.Contains("\"tags\":[\"newsletter\"]", stub.LastBody);
    }

    [Fact]
    public async Task Add_unsubscribe_omits_tags_when_empty()
    {
        var (client, stub) = BuildClient();

        await client.Suppressions.Unsubscribes.AddAsync(new Unsubscribe { Address = "u@x.com" });

        Assert.DoesNotContain("\"tags\"", stub.LastBody);
    }

    [Fact]
    public async Task Add_complaint_posts_address_only()
    {
        var (client, stub) = BuildClient();

        await client.Suppressions.Complaints.AddAsync(new Complaint { Address = "c@x.com" });

        Assert.Equal($"/v3/{Domain}/complaints", stub.LastRequestUri!.AbsolutePath);
        Assert.Contains("\"address\":\"c@x.com\"", stub.LastBody);
    }

    [Fact]
    public async Task Add_null_entry_throws_argument_null_and_issues_no_request()
    {
        var (client, stub) = BuildClient();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.Suppressions.Bounces.AddAsync(null!));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Add_blank_address_throws_argument_exception_and_issues_no_request()
    {
        var (client, stub) = BuildClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Suppressions.Bounces.AddAsync(new Bounce { Address = "   " }));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Add_surfaces_the_typed_error_on_non_success()
    {
        const string body = "{\"message\":\"bad request\"}";
        var (client, _) = BuildClient(HttpStatusCode.BadRequest, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Suppressions.Bounces.AddAsync(new Bounce { Address = "a@x.com" }));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }
}
