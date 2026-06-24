using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class OptionsTagsTrackingTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";
    private const string SuccessBody = "{\"id\":\"<20260624.2@mg.example.com>\",\"message\":\"Queued. Thank you.\"}";

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
    public async Task Tags_supplied_multiple_times_all_appear_in_order()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.Tags.Add("june-campaign");
        message.Options.Tags.Add("tickets");
        message.Options.Tags.Add("vip");

        await client.SendAsync(message);

        var tags = stub.Requests[0].Values("o:tag");
        string[] expected = { "june-campaign", "tickets", "vip" };
        Assert.Equal(expected, tags);
    }

    [Fact]
    public async Task Blank_tag_entries_are_skipped()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.Tags.Add("keep");
        message.Options.Tags.Add("   ");
        message.Options.Tags.Add("");

        await client.SendAsync(message);

        string[] expected = { "keep" };
        Assert.Equal(expected, stub.Requests[0].Values("o:tag"));
    }

    [Fact]
    public async Task Test_mode_present_only_when_enabled()
    {
        var (enabledClient, enabledStub) = BuildClient();
        var enabled = NewMessage();
        enabled.Options.TestMode = true;
        await enabledClient.SendAsync(enabled);
        Assert.Equal("yes", enabledStub.Requests[0].Value("o:testmode"));

        var (disabledClient, disabledStub) = BuildClient();
        await disabledClient.SendAsync(NewMessage());
        Assert.Equal(0, disabledStub.Requests[0].Count("o:testmode"));
    }

    [Fact]
    public async Task Tracking_toggles_emit_requested_values_including_htmlonly()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.TrackingOpens = true;
        message.Options.TrackingClicks = ClickTracking.HtmlOnly;

        await client.SendAsync(message);

        var request = stub.Requests[0];
        Assert.Equal("yes", request.Value("o:tracking-opens"));
        Assert.Equal("htmlonly", request.Value("o:tracking-clicks"));
    }

    [Fact]
    public async Task Tracking_toggles_emit_no_when_disabled()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.TrackingOpens = false;
        message.Options.TrackingClicks = ClickTracking.No;

        await client.SendAsync(message);

        var request = stub.Requests[0];
        Assert.Equal("no", request.Value("o:tracking-opens"));
        Assert.Equal("no", request.Value("o:tracking-clicks"));
    }

    [Fact]
    public async Task Unset_options_are_absent()
    {
        var (client, stub) = BuildClient();

        await client.SendAsync(NewMessage());

        var request = stub.Requests[0];
        Assert.Equal(0, request.Count("o:tag"));
        Assert.Equal(0, request.Count("o:testmode"));
        Assert.Equal(0, request.Count("o:tracking-opens"));
        Assert.Equal(0, request.Count("o:tracking-clicks"));
    }
}
