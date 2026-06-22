using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class RecipientFieldsTests
{
    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"id\":\"<x>\",\"message\":\"Queued.\"}");
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    private static MailgunMessage NewMessage()
    {
        return new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com"),
            Text = "Hi",
        };
    }

    [Fact]
    public async Task Three_to_recipients_produce_three_distinct_to_parts()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.To.Add("alice@example.com");
        message.To.Add("bob@example.com");
        message.To.Add("carol@example.com");

        await client.SendAsync(message);

        var toFields = stub.LastFormData.Where(f => f.Name == "to").ToList();
        Assert.Equal(3, toFields.Count);
        var expected = new[] { "alice@example.com", "bob@example.com", "carol@example.com" };
        Assert.Equal(expected, toFields.Select(f => f.Value));
        Assert.DoesNotContain(toFields, f => f.Value.Contains(',', StringComparison.Ordinal));
    }

    [Fact]
    public async Task Each_cc_and_bcc_appears_as_its_own_distinct_field()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.To.Add("alice@example.com");
        message.Cc.Add("carol@example.com");
        message.Cc.Add("dave@example.com");
        message.Bcc.Add("erin@example.com");

        await client.SendAsync(message);

        var expectedCc = new[] { "carol@example.com", "dave@example.com" };
        var expectedBcc = new[] { "erin@example.com" };
        Assert.Equal(expectedCc, stub.LastFormData.Where(f => f.Name == "cc").Select(f => f.Value));
        Assert.Equal(expectedBcc, stub.LastFormData.Where(f => f.Name == "bcc").Select(f => f.Value));
    }

    [Fact]
    public async Task Total_recipient_field_count_equals_number_of_distinct_recipients()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.To.Add("alice@example.com");
        message.To.Add("bob@example.com");
        message.Cc.Add("carol@example.com");
        message.Bcc.Add("erin@example.com");

        await client.SendAsync(message);

        var recipientFields = stub.LastFormData
            .Count(f => f.Name is "to" or "cc" or "bcc");
        Assert.Equal(4, recipientFields);
    }

    [Fact]
    public async Task Blank_recipient_entries_are_not_turned_into_empty_fields()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.To.Add("alice@example.com");
        message.To.Add(default(EmailAddress)); // default struct has a null/blank address
        message.Cc.Add(default(EmailAddress));

        await client.SendAsync(message);

        var recipientFields = stub.LastFormData.Where(f => f.Name is "to" or "cc" or "bcc").ToList();
        Assert.Single(recipientFields);
        Assert.Equal("alice@example.com", recipientFields[0].Value);
        Assert.DoesNotContain(recipientFields, f => string.IsNullOrWhiteSpace(f.Value));
    }
}
