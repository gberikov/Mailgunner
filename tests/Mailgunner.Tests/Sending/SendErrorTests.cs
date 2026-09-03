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

    [Fact]
    public async Task Exception_message_includes_the_service_message_from_a_json_body()
    {
        var client = BuildClient(HttpStatusCode.BadRequest, "{\"message\":\"'from' parameter is missing\"}");

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(NewMessage()));

        Assert.Equal("The Mailgun request failed (HTTP 400): 'from' parameter is missing", ex.Message);
    }

    [Fact]
    public async Task Exception_message_stays_generic_for_a_non_json_body()
    {
        var client = BuildClient(HttpStatusCode.BadGateway, "<html>502</html>");

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(NewMessage()));

        Assert.Equal("The Mailgun request did not yield a usable result (HTTP 502).", ex.Message);
        Assert.Null(ex.FailedChunkIndex);
        Assert.Empty(ex.AcceptedResults);
    }

    [Fact]
    public void A_long_service_message_is_truncated_to_200_characters()
    {
        var body = "{\"message\":\"" + new string('x', 500) + "\"}";

        var ex = new MailgunnerException(400, body);

        Assert.Equal("The Mailgun request failed (HTTP 400): " + new string('x', 200) + "…", ex.Message);
    }
}
