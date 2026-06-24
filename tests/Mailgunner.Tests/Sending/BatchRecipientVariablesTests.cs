using System.Net;
using System.Text.Json;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class BatchRecipientVariablesTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";
    private const string SuccessBody = "{\"id\":\"<x@mg>\",\"message\":\"Queued.\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, SendingKey, MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    private static MailgunBatchMessage NewBatch()
    {
        return new MailgunBatchMessage
        {
            From = new EmailAddress("invites@mg.example.com"),
            Template = "conference-invite",
        };
    }

    private static BatchRecipient Recipient(string address, params (string Key, object? Value)[] vars)
    {
        var r = new BatchRecipient(new EmailAddress(address));
        foreach (var (key, value) in vars)
        {
            r.Variables[key] = value;
        }

        return r;
    }

    [Fact]
    public async Task Recipient_variables_is_one_json_object_keyed_by_bare_address_with_each_recipients_values()
    {
        var (client, stub) = BuildClient();
        var batch = NewBatch();
        batch.Recipients.Add(Recipient("alice@example.com", ("name", "Alice"), ("ticket", 1024), ("vip", true)));
        batch.Recipients.Add(Recipient("bob@example.com", ("name", "Bob"), ("owner", new { team = "blue" })));

        await client.SendBatchAsync(batch);

        Assert.Equal(1, stub.Requests[0].Count("recipient-variables"));
        using var document = JsonDocument.Parse(stub.Requests[0].Value("recipient-variables")!);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(2, root.EnumerateObject().Count());

        var alice = root.GetProperty("alice@example.com");
        Assert.Equal("Alice", alice.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Number, alice.GetProperty("ticket").ValueKind);
        Assert.Equal(1024, alice.GetProperty("ticket").GetInt32());
        Assert.Equal(JsonValueKind.True, alice.GetProperty("vip").ValueKind);

        var bob = root.GetProperty("bob@example.com");
        Assert.Equal("Bob", bob.GetProperty("name").GetString());
        Assert.Equal("blue", bob.GetProperty("owner").GetProperty("team").GetString());
    }

    [Fact]
    public async Task Recipient_with_no_variables_serializes_to_empty_object()
    {
        var (client, stub) = BuildClient();
        var batch = NewBatch();
        batch.Recipients.Add(Recipient("nobody@example.com"));

        await client.SendBatchAsync(batch);

        using var document = JsonDocument.Parse(stub.Requests[0].Value("recipient-variables")!);
        var entry = document.RootElement.GetProperty("nobody@example.com");
        Assert.Equal(JsonValueKind.Object, entry.ValueKind);
        Assert.Empty(entry.EnumerateObject());
    }

    [Fact]
    public async Task Per_recipient_values_are_independent_of_global_variables()
    {
        var (client, stub) = BuildClient();
        var batch = NewBatch();
        batch.TemplateVariables["event"] = "Acme Conf 2026";
        batch.Recipients.Add(Recipient("alice@example.com", ("name", "Alice")));

        await client.SendBatchAsync(batch);

        using var recipientVars = JsonDocument.Parse(stub.Requests[0].Value("recipient-variables")!);
        var alice = recipientVars.RootElement.GetProperty("alice@example.com");
        Assert.False(alice.TryGetProperty("event", out _));

        using var globalVars = JsonDocument.Parse(stub.Requests[0].Value("t:variables")!);
        Assert.Equal("Acme Conf 2026", globalVars.RootElement.GetProperty("event").GetString());
    }

    [Fact]
    public async Task Global_template_and_variables_are_identical_across_all_chunks()
    {
        var (client, stub) = BuildClient();
        var batch = NewBatch();
        batch.TemplateVariables["event"] = "Acme Conf 2026";
        for (var i = 0; i < 1500; i++) // two chunks
        {
            batch.Recipients.Add(Recipient($"user{i}@example.com", ("name", $"User {i}")));
        }

        await client.SendBatchAsync(batch);

        Assert.Equal(2, stub.Requests.Count);
        Assert.Equal(stub.Requests[0].Value("template"), stub.Requests[1].Value("template"));
        Assert.Equal(stub.Requests[0].Value("t:variables"), stub.Requests[1].Value("t:variables"));
    }

    [Fact]
    public async Task Global_variables_field_is_omitted_when_the_global_map_is_empty()
    {
        var (client, stub) = BuildClient();
        var batch = NewBatch(); // no TemplateVariables
        batch.Recipients.Add(Recipient("alice@example.com", ("name", "Alice")));

        await client.SendBatchAsync(batch);

        Assert.Equal(0, stub.Requests[0].Count("t:variables"));
        // recipient-variables is still present even when globals are empty.
        Assert.Equal(1, stub.Requests[0].Count("recipient-variables"));
    }
}
