using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Mailgunner.Tests.WebhookManagement;

/// <summary>
/// Shared offline harness for the webhook-management tests: builds a real <see cref="IMailgunnerClient"/>
/// whose transport is a <see cref="StubHttpMessageHandler"/>, so the wire format, routing, and error
/// contract can be asserted without any network. Mirrors the suppression tests' client-building pattern.
/// </summary>
internal static class WebhookHarness
{
    public const string Domain = "mg.example.com";

    /// <summary>A single-webhook response envelope with the supplied URLs.</summary>
    public static string Envelope(params string[] urls) =>
        "{\"webhook\":{\"urls\":[" + string.Join(",", urls.Select(u => "\"" + u + "\"")) + "]}}";

    /// <summary>The empty list response (no event types registered).</summary>
    public const string EmptyList = "{\"webhooks\":{}}";

    public static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient(
        HttpStatusCode status = HttpStatusCode.OK,
        string body = "{}",
        MailgunRegion region = MailgunRegion.Us,
        string domain = Domain)
    {
        var stub = new StubHttpMessageHandler(status, body);
        return (BuildClient(stub, region, domain), stub);
    }

    public static IMailgunnerClient BuildClient(
        StubHttpMessageHandler stub, MailgunRegion region = MailgunRegion.Us, string domain = Domain)
    {
        var services = new ServiceCollection();
        services.AddMailgunner(domain, "key-123", region)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        return services.BuildServiceProvider().GetRequiredService<IMailgunnerClient>();
    }
}
