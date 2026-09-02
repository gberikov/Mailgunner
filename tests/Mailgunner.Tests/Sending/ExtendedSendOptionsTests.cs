using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class ExtendedSendOptionsTests
{
    private const string SuccessBody = "{\"id\":\"<x@mg>\",\"message\":\"Queued.\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        return (services.BuildServiceProvider().GetRequiredService<IMailgunnerClient>(), stub);
    }

    private static MailgunMessage NewMessage()
    {
        var message = new MailgunMessage { From = "noreply@mg.example.com", Text = "Hi" };
        message.To.Add("alice@example.com");
        return message;
    }

    [Theory]
    [InlineData(true, "yes")]
    [InlineData(false, "no")]
    public async Task Boolean_options_are_emitted_as_yes_or_no(bool value, string wire)
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.RequireTls = value;
        message.Options.SkipVerification = value;
        message.Options.Tracking = value;

        await client.SendAsync(message);

        Assert.Equal(wire, stub.LastFormData.Single(f => f.Name == "o:require-tls").Value);
        Assert.Equal(wire, stub.LastFormData.Single(f => f.Name == "o:skip-verification").Value);
        Assert.Equal(wire, stub.LastFormData.Single(f => f.Name == "o:tracking").Value);
    }

    [Fact]
    public async Task String_options_are_emitted_verbatim()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.SendingIp = "192.0.2.10";
        message.Options.SendingIpPool = "pool-a";
        message.Options.TimeZoneLocalize = "09:00";

        await client.SendAsync(message);

        Assert.Equal("192.0.2.10", stub.LastFormData.Single(f => f.Name == "o:sending-ip").Value);
        Assert.Equal("pool-a", stub.LastFormData.Single(f => f.Name == "o:sending-ip-pool").Value);
        Assert.Equal("09:00", stub.LastFormData.Single(f => f.Name == "o:time-zone-localize").Value);
    }

    [Fact]
    public async Task Unset_options_are_omitted()
    {
        var (client, stub) = BuildClient();

        await client.SendAsync(NewMessage());

        foreach (var name in new[] { "o:require-tls", "o:skip-verification", "o:tracking", "o:sending-ip", "o:sending-ip-pool", "o:time-zone-localize", "amp-html" })
        {
            Assert.DoesNotContain(stub.LastFormData, f => f.Name == name);
        }
    }

    [Fact]
    public async Task Amp_html_is_emitted_as_its_own_part()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.AmpHtml = "<!doctype html><html ⚡4email></html>";

        await client.SendAsync(message);

        Assert.Equal(message.AmpHtml, stub.LastFormData.Single(f => f.Name == "amp-html").Value);
    }
}
