using System.Text;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class NamedUnnamedIsolationTests
{
    [Fact]
    public async Task Unnamed_and_named_clients_do_not_leak_host_domain_or_auth()
    {
        var fakeUnnamed = new CapturingHttpMessageHandler();
        var fakeNamed = new CapturingHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddMailgunner("default.example.com", "key-default", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => fakeUnnamed);
        services.AddMailgunner("marketing", "news.example.com", "key-mkt", MailgunRegion.Eu)
                .ConfigurePrimaryHttpMessageHandler(() => fakeNamed);
        using var provider = services.BuildServiceProvider();

        var unnamed = (MailgunnerClient)provider.GetRequiredService<IMailgunnerClient>();
        var named = (MailgunnerClient)provider.GetRequiredService<IMailgunnerClientFactory>().Get("marketing");
        using (await unnamed.HttpClient.GetAsync(new Uri("v3/default.example.com/messages", UriKind.Relative))) { }
        using (await named.HttpClient.GetAsync(new Uri("v3/news.example.com/messages", UriKind.Relative))) { }

        Assert.Equal("api.mailgun.net", fakeUnnamed.LastRequest!.RequestUri!.Host);
        Assert.Equal(
            Convert.ToBase64String(Encoding.ASCII.GetBytes("api:key-default")),
            fakeUnnamed.LastRequest!.Headers.Authorization!.Parameter);

        Assert.Equal("api.eu.mailgun.net", fakeNamed.LastRequest!.RequestUri!.Host);
        Assert.Equal(
            Convert.ToBase64String(Encoding.ASCII.GetBytes("api:key-mkt")),
            fakeNamed.LastRequest!.Headers.Authorization!.Parameter);
    }
}
