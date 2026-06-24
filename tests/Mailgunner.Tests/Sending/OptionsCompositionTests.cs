using System.Net;
using System.Text;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class OptionsCompositionTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";
    private const string SuccessBody = "{\"id\":\"<20260624.5@mg.example.com>\",\"message\":\"Queued. Thank you.\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, SendingKey, MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    private static void Enrich(MailgunSendOptions options, System.Collections.Generic.IList<MailgunFile> attachments)
    {
        options.Tags.Add("conf-2026");
        options.CustomHeaders["X-Trace"] = "t-1";
        options.CustomVariables["uid"] = "9";
        attachments.Add(new MailgunFile("agenda.pdf", Encoding.UTF8.GetBytes("agenda"), "application/pdf"));
    }

    private static void AssertEnriched(CapturedRequest request)
    {
        Assert.Contains("conf-2026", request.Values("o:tag"));
        Assert.Equal("t-1", request.Value("h:X-Trace"));
        Assert.Equal("9", request.Value("v:uid"));
        var file = Assert.Single(request.Fields("attachment"));
        Assert.Equal("agenda.pdf", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);
    }

    [Fact]
    public async Task Options_ride_a_plain_send()
    {
        var (client, stub) = BuildClient();
        var message = new MailgunMessage { From = "noreply@mg.example.com", Subject = "Hi", Text = "Body" };
        message.To.Add("alice@example.com");
        Enrich(message.Options, message.Attachments);

        await client.SendAsync(message);

        AssertEnriched(stub.Requests[0]);
    }

    [Fact]
    public async Task Options_ride_a_templated_send()
    {
        var (client, stub) = BuildClient();
        var message = new MailgunMessage { From = "noreply@mg.example.com", Subject = "Hi", Template = "welcome" };
        message.To.Add("alice@example.com");
        Enrich(message.Options, message.Attachments);

        await client.SendAsync(message);

        AssertEnriched(stub.Requests[0]);
    }

    [Fact]
    public async Task Options_repeat_identically_on_every_chunk_of_a_batch()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage { From = "noreply@mg.example.com", Subject = "Invite", Template = "invite" };
        for (var i = 0; i < 2500; i++)
        {
            batch.Recipients.Add(new BatchRecipient($"user{i}@example.com"));
        }

        Enrich(batch.Options, batch.Attachments);

        var results = await client.SendBatchAsync(batch);

        Assert.Equal(3, results.Count);
        Assert.Equal(3, stub.Requests.Count);
        foreach (var request in stub.Requests)
        {
            AssertEnriched(request);
        }
    }

    [Fact]
    public async Task A_send_with_no_options_emits_no_enrichment_parts()
    {
        var (client, stub) = BuildClient();
        var message = new MailgunMessage { From = "noreply@mg.example.com", Subject = "Hi", Text = "Body" };
        message.To.Add("alice@example.com");

        await client.SendAsync(message);

        foreach (var field in stub.Requests[0].FormData)
        {
            Assert.False(field.Name.StartsWith("o:", System.StringComparison.Ordinal), $"unexpected option {field.Name}");
            Assert.False(field.Name.StartsWith("h:", System.StringComparison.Ordinal), $"unexpected header {field.Name}");
            Assert.False(field.Name.StartsWith("v:", System.StringComparison.Ordinal), $"unexpected variable {field.Name}");
            Assert.NotEqual("attachment", field.Name);
            Assert.NotEqual("inline", field.Name);
        }
    }

    [Fact]
    public async Task Sending_key_never_appears_in_any_enriched_field()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage { From = "noreply@mg.example.com", Subject = "Invite", Template = "invite" };
        for (var i = 0; i < 1500; i++)
        {
            batch.Recipients.Add(new BatchRecipient($"user{i}@example.com"));
        }

        Enrich(batch.Options, batch.Attachments);

        await client.SendBatchAsync(batch);

        foreach (var request in stub.Requests)
        {
            foreach (var field in request.FormData)
            {
                Assert.DoesNotContain(SendingKey, field.Value);
            }
        }
    }
}
