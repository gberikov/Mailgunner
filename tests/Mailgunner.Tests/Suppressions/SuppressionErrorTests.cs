using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Suppressions;

public class SuppressionErrorTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";
    private const string ErrorBody = "{\"message\":\"server error\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient(HttpStatusCode status)
    {
        var stub = new StubHttpMessageHandler(status, ErrorBody);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, SendingKey, MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        // Keep retryable-status error tests instant: complete any backoff wait immediately.
        services.AddSingleton<TimeProvider>(new RecordingTimeProvider());
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    [Fact]
    public async Task List_surfaces_the_typed_error()
    {
        var (client, _) = BuildClient(HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Suppressions.Bounces.ListPageAsync());

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal(ErrorBody, ex.ResponseBody);
    }

    [Fact]
    public async Task Get_surfaces_the_typed_error()
    {
        var (client, _) = BuildClient(HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Suppressions.Unsubscribes.GetAsync("a@x.com"));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal(ErrorBody, ex.ResponseBody);
    }

    [Fact]
    public async Task Add_surfaces_the_typed_error()
    {
        var (client, _) = BuildClient(HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Suppressions.Complaints.AddAsync(new Complaint { Address = "a@x.com" }));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal(ErrorBody, ex.ResponseBody);
    }

    [Fact]
    public async Task Remove_surfaces_the_typed_error()
    {
        var (client, _) = BuildClient(HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Suppressions.Bounces.RemoveAsync("a@x.com"));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal(ErrorBody, ex.ResponseBody);
    }

    [Fact]
    public async Task Clear_surfaces_the_typed_error()
    {
        var (client, _) = BuildClient(HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Suppressions.Unsubscribes.ClearAsync());

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal(ErrorBody, ex.ResponseBody);
    }

    [Fact]
    public async Task Enumerating_a_failing_list_surfaces_the_typed_error()
    {
        var (client, _) = BuildClient(HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<MailgunnerException>(async () =>
        {
            await foreach (var _ in client.Suppressions.Complaints.ListAsync())
            {
            }
        });
    }

    [Fact]
    public async Task The_sending_key_never_appears_in_the_request_or_the_error()
    {
        var (client, stub) = BuildClient(HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Suppressions.Bounces.AddAsync(new Bounce { Address = "a@x.com", Code = "550" }));

        Assert.DoesNotContain(SendingKey, ex.Message);
        Assert.DoesNotContain(SendingKey, ex.ResponseBody);
        Assert.DoesNotContain(SendingKey, stub.LastRequestUri!.ToString());
        Assert.DoesNotContain(SendingKey, stub.LastBody!);
    }
}
