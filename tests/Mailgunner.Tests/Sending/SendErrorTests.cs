using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class SendErrorTests
{
    private const string SendingKey = "key-super-secret-123";

    private static IMailgunnerClient BuildClient(HttpStatusCode statusCode, string responseBody)
    {
        var stub = new StubHttpMessageHandler(statusCode, responseBody);
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", SendingKey, MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        // Keep retryable-status error tests instant: complete any backoff wait immediately.
        services.AddSingleton<TimeProvider>(new RecordingTimeProvider());
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMailgunnerClient>();
    }

    private static MailgunMessage NewMessage()
    {
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com"),
            Text = "Hi",
        };
        message.To.Add("alice@example.com");
        return message;
    }

    [Fact]
    public async Task Client_error_4xx_throws_with_status_and_raw_body()
    {
        const string body = "{\"message\":\"'from' parameter is not a valid address\"}";
        var client = BuildClient(HttpStatusCode.BadRequest, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(NewMessage()));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }

    [Fact]
    public async Task Server_error_5xx_throws_same_type_with_status_and_raw_body()
    {
        const string body = "Bad Gateway";
        var client = BuildClient(HttpStatusCode.BadGateway, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(NewMessage()));

        Assert.Equal(502, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }

    [Fact]
    public async Task Success_with_unparseable_body_throws_with_status_and_raw_body()
    {
        const string body = "not json at all";
        var client = BuildClient(HttpStatusCode.OK, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(NewMessage()));

        Assert.Equal(200, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }

    [Fact]
    public async Task Success_with_missing_fields_throws_and_returns_no_result()
    {
        const string body = "{\"id\":\"<x>\"}"; // missing "message"
        var client = BuildClient(HttpStatusCode.OK, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(NewMessage()));

        Assert.Equal(200, ex.StatusCode);
        Assert.Equal(body, ex.ResponseBody);
    }

    [Fact]
    public async Task Non_success_with_empty_body_carries_non_null_empty_body()
    {
        var client = BuildClient(HttpStatusCode.InternalServerError, string.Empty);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(NewMessage()));

        Assert.Equal(500, ex.StatusCode);
        Assert.NotNull(ex.ResponseBody);
        Assert.Equal(string.Empty, ex.ResponseBody);
    }

    [Fact]
    public async Task Sending_key_never_appears_in_the_exception()
    {
        const string body = "{\"message\":\"forbidden\"}";
        var client = BuildClient(HttpStatusCode.Unauthorized, body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(NewMessage()));

        Assert.DoesNotContain(SendingKey, ex.ResponseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(SendingKey, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SendingKey, ex.ToString(), StringComparison.Ordinal);
    }
}
