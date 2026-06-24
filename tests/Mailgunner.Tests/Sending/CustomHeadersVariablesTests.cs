using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class CustomHeadersVariablesTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";
    private const string SuccessBody = "{\"id\":\"<20260624.4@mg.example.com>\",\"message\":\"Queued. Thank you.\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, SendingKey, MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    private static MailgunMessage NewMessage()
    {
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com", "Example"),
            Subject = "Hi",
            Text = "Body",
        };
        message.To.Add("alice@example.com");
        return message;
    }

    [Fact]
    public async Task Custom_header_and_variable_appear_under_documented_prefixes()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.CustomHeaders["X-Correlation-Id"] = "abc-123";
        message.Options.CustomVariables["campaign_id"] = "42";

        await client.SendAsync(message);

        var request = stub.Requests[0];
        Assert.Equal("abc-123", request.Value("h:X-Correlation-Id"));
        Assert.Equal("42", request.Value("v:campaign_id"));
    }

    [Fact]
    public async Task Multiple_headers_and_variables_each_appear_without_collision()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.CustomHeaders["X-A"] = "1";
        message.Options.CustomHeaders["X-B"] = "2";
        message.Options.CustomVariables["k1"] = "v1";
        message.Options.CustomVariables["k2"] = "v2";

        await client.SendAsync(message);

        var request = stub.Requests[0];
        Assert.Equal("1", request.Value("h:X-A"));
        Assert.Equal("2", request.Value("h:X-B"));
        Assert.Equal("v1", request.Value("v:k1"));
        Assert.Equal("v2", request.Value("v:k2"));
        // No collision between the h:/v: namespaces.
        Assert.Equal(0, request.Count("v:X-A"));
        Assert.Equal(0, request.Count("h:k1"));
    }

    [Fact]
    public async Task Reassigning_a_name_replaces_its_value_without_duplicates()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.CustomHeaders["X-A"] = "first";
        message.Options.CustomHeaders["X-A"] = "second";

        await client.SendAsync(message);

        var request = stub.Requests[0];
        Assert.Equal(1, request.Count("h:X-A"));
        Assert.Equal("second", request.Value("h:X-A"));
    }

    [Fact]
    public async Task Blank_header_name_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.CustomHeaders["   "] = "x";

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Blank_variable_name_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.CustomVariables[" "] = "x";

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Empty(stub.Requests);
    }

    [Theory]
    [InlineData("X Bad")]                  // space is not a token character
    [InlineData("X:Bad")]                  // colon is not a token character
    [InlineData("X-Bad\r\nInjected-Header")] // CRLF header-injection attempt
    public async Task Invalid_header_name_throws_before_any_request(string name)
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.CustomHeaders[name] = "value";

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Header_value_with_line_breaks_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.CustomHeaders["X-Ok"] = "line1\r\nInjected: evil";

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Variable_name_with_control_characters_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.CustomVariables["k\r\nInjected"] = "v";

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Empty(stub.Requests);
    }
}
