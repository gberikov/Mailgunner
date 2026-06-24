using System.Text;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class NamedRoutingIsolationTests
{
    [Fact]
    public async Task Each_named_client_targets_its_own_host_domain_and_auth()
    {
        var fakeTx = new CapturingHttpMessageHandler();
        var fakeMkt = new CapturingHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddMailgunner("transactional", "tx.example.com", "key-tx", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => fakeTx);
        services.AddMailgunner("marketing", "news.example.com", "key-mkt", MailgunRegion.Eu)
                .ConfigurePrimaryHttpMessageHandler(() => fakeMkt);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IMailgunnerClientFactory>();

        var tx = (MailgunnerClient)factory.Get("transactional");
        var mkt = (MailgunnerClient)factory.Get("marketing");
        using (await tx.HttpClient.GetAsync(new Uri("v3/tx.example.com/messages", UriKind.Relative))) { }
        using (await mkt.HttpClient.GetAsync(new Uri("v3/news.example.com/messages", UriKind.Relative))) { }

        // Transactional → US host, its own domain path, its own key.
        Assert.Equal("api.mailgun.net", fakeTx.LastRequest!.RequestUri!.Host);
        Assert.Equal("/v3/tx.example.com/messages", fakeTx.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(
            Convert.ToBase64String(Encoding.ASCII.GetBytes("api:key-tx")),
            fakeTx.LastRequest!.Headers.Authorization!.Parameter);

        // Marketing → EU host, its own domain path, its own key.
        Assert.Equal("api.eu.mailgun.net", fakeMkt.LastRequest!.RequestUri!.Host);
        Assert.Equal("/v3/news.example.com/messages", fakeMkt.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(
            Convert.ToBase64String(Encoding.ASCII.GetBytes("api:key-mkt")),
            fakeMkt.LastRequest!.Headers.Authorization!.Parameter);
    }
}
