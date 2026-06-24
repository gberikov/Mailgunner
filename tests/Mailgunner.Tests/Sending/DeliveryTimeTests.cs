using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class DeliveryTimeTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";
    private const string SuccessBody = "{\"id\":\"<20260624.3@mg.example.com>\",\"message\":\"Queued. Thank you.\"}";

    // RFC 2822 date-time with a NUMERIC offset (no colon, no named zone).
    private const string Rfc2822 = @"^[A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2}:\d{2} [+-]\d{4}$";

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
    public async Task Utc_delivery_time_is_rfc2822_with_zero_numeric_offset()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.DeliveryTime = new System.DateTimeOffset(2026, 6, 25, 14, 0, 0, System.TimeSpan.Zero);

        await client.SendAsync(message);

        var value = stub.Requests[0].Value("o:deliverytime");
        Assert.NotNull(value);
        Assert.Matches(Rfc2822, value!);
        Assert.Equal("Thu, 25 Jun 2026 14:00:00 +0000", value);
    }

    [Fact]
    public async Task Non_utc_offset_uses_numeric_offset_never_a_named_zone()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.DeliveryTime = new System.DateTimeOffset(2026, 6, 25, 14, 0, 0, System.TimeSpan.FromHours(3));

        await client.SendAsync(message);

        var value = stub.Requests[0].Value("o:deliverytime")!;
        Assert.Matches(Rfc2822, value);
        Assert.EndsWith("+0300", value);
        Assert.DoesNotContain(":", value.Substring(value.Length - 5)); // offset has no colon
        Assert.DoesNotContain("UTC", value);
        Assert.DoesNotContain("GMT", value);
        Assert.DoesNotContain("EST", value);
    }

    [Fact]
    public async Task Delivery_time_absent_when_unset()
    {
        var (client, stub) = BuildClient();

        await client.SendAsync(NewMessage());

        Assert.Equal(0, stub.Requests[0].Count("o:deliverytime"));
    }
}
