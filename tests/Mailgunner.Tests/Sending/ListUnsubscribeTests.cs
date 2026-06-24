using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class ListUnsubscribeTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";
    private const string SuccessBody = "{\"id\":\"<20260624.7@mg.example.com>\",\"message\":\"Queued. Thank you.\"}";
    private const string Url = "https://example.com/unsubscribe?id=abc123";
    private const string Mailto = "unsubscribe@mg.example.com";

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
            From = new EmailAddress("news@mg.example.com", "Example"),
            Subject = "Hi",
            Text = "Body",
        };
        message.To.Add("alice@example.com");
        return message;
    }

    private static MailgunBatchMessage NewBatch(int recipientCount)
    {
        var batch = new MailgunBatchMessage
        {
            From = new EmailAddress("news@mg.example.com", "Example"),
            Subject = "Hi",
            Template = "newsletter",
        };

        for (var i = 0; i < recipientCount; i++)
        {
            batch.Recipients.Add(new BatchRecipient(new EmailAddress($"user{i}@example.com")));
        }

        return batch;
    }

    // --- T004 [US1]: one-click emission + one-click-without-URL rejection ---

    [Fact]
    public async Task One_click_url_emits_both_headers_exactly_once()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.ListUnsubscribe = new ListUnsubscribeOptions { Url = Url, OneClick = true };

        await client.SendAsync(message);

        var request = stub.Requests[0];
        Assert.Equal(1, request.Count("h:List-Unsubscribe"));
        Assert.Equal($"<{Url}>", request.Value("h:List-Unsubscribe"));
        Assert.Equal(1, request.Count("h:List-Unsubscribe-Post"));
        Assert.Equal("List-Unsubscribe=One-Click", request.Value("h:List-Unsubscribe-Post"));
    }

    [Fact]
    public async Task One_click_without_url_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.ListUnsubscribe = new ListUnsubscribeOptions { MailtoAddress = Mailto, OneClick = true };

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Empty(stub.Requests);
    }

    // --- T007 [US2]: non-one-click target combinations ---

    [Fact]
    public async Task Mailto_only_emits_single_header_without_post()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.ListUnsubscribe = new ListUnsubscribeOptions { MailtoAddress = Mailto };

        await client.SendAsync(message);

        var request = stub.Requests[0];
        Assert.Equal($"<mailto:{Mailto}>", request.Value("h:List-Unsubscribe"));
        Assert.Equal(0, request.Count("h:List-Unsubscribe-Post"));
    }

    [Fact]
    public async Task Url_only_not_one_click_emits_single_header_without_post()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.ListUnsubscribe = new ListUnsubscribeOptions { Url = Url };

        await client.SendAsync(message);

        var request = stub.Requests[0];
        Assert.Equal($"<{Url}>", request.Value("h:List-Unsubscribe"));
        Assert.Equal(0, request.Count("h:List-Unsubscribe-Post"));
    }

    [Fact]
    public async Task Both_targets_emit_url_first_comma_separated()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.ListUnsubscribe = new ListUnsubscribeOptions { Url = Url, MailtoAddress = Mailto };

        await client.SendAsync(message);

        var request = stub.Requests[0];
        Assert.Equal($"<{Url}>, <mailto:{Mailto}>", request.Value("h:List-Unsubscribe"));
        Assert.Equal(0, request.Count("h:List-Unsubscribe-Post"));
    }

    // --- T009 [US3]: rejection paths ---

    [Fact]
    public async Task Non_https_url_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.ListUnsubscribe = new ListUnsubscribeOptions { Url = "http://example.com/unsub" };

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Empty(stub.Requests);
    }

    [Theory]
    [InlineData("https://example.com/unsub\r\nInjected: evil")] // CRLF header-injection attempt
    [InlineData("https://example.com/unsub\u0001")]             // embedded control character (U+0001)
    public async Task Url_with_control_or_line_break_throws_before_any_request(string url)
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.ListUnsubscribe = new ListUnsubscribeOptions { Url = url };

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Empty_target_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.ListUnsubscribe = new ListUnsubscribeOptions();

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Whitespace_only_url_with_no_mailto_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.ListUnsubscribe = new ListUnsubscribeOptions { Url = "   " };

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Empty(stub.Requests);
    }

    [Theory]
    [InlineData("List-Unsubscribe")]
    [InlineData("list-unsubscribe")]
    [InlineData("List-Unsubscribe-Post")]
    public async Task Manual_header_conflict_throws_before_any_request(string manualHeaderName)
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.CustomHeaders[manualHeaderName] = "<https://manual.example.com/unsub>";
        message.Options.ListUnsubscribe = new ListUnsubscribeOptions { Url = Url, OneClick = true };

        await Assert.ThrowsAsync<System.ArgumentException>(() => client.SendAsync(message));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public void Mailto_with_control_characters_is_rejected_at_assignment()
    {
        // The mailto half of the injection guard is delegated to the EmailAddress constructor, which
        // throws on control characters when the bare address is assigned (see EmailAddressTests).
        Assert.Throws<System.ArgumentException>(() =>
            new ListUnsubscribeOptions { MailtoAddress = "bad\r\nInjected@example.com" });
    }

    // --- T010 [US3]: opt-in regression + batch repetition ---

    [Fact]
    public async Task Unset_target_emits_no_unsubscribe_headers()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();

        await client.SendAsync(message);

        var request = stub.Requests[0];
        Assert.Equal(0, request.Count("h:List-Unsubscribe"));
        Assert.Equal(0, request.Count("h:List-Unsubscribe-Post"));
    }

    [Fact]
    public async Task Batch_repeats_both_headers_on_every_chunk()
    {
        var (client, stub) = BuildClient();

        // >1000 recipients spans two chunks (MailgunBatchContent.MaxRecipientsPerRequest == 1000).
        var batch = NewBatch(1500);
        batch.Options.ListUnsubscribe = new ListUnsubscribeOptions { Url = Url, OneClick = true };

        await client.SendBatchAsync(batch);

        Assert.Equal(2, stub.Requests.Count);
        foreach (var request in stub.Requests)
        {
            Assert.Equal($"<{Url}>", request.Value("h:List-Unsubscribe"));
            Assert.Equal("List-Unsubscribe=One-Click", request.Value("h:List-Unsubscribe-Post"));
        }
    }
}
