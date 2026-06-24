using System.Text;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class UnnamedBackwardCompatTests
{
    [Fact]
    public async Task Unnamed_registration_keeps_its_routing_and_auth_when_a_named_client_is_added()
    {
        var fake = new CapturingHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => fake);
        // Adding a named client must not disturb the unnamed one.
        services.AddMailgunner("marketing", "news.example.com", "key-mkt", MailgunRegion.Eu);
        using var provider = services.BuildServiceProvider();

        var client = (MailgunnerClient)provider.GetRequiredService<IMailgunnerClient>();
        using (await client.HttpClient.GetAsync(new Uri("v3/mg.example.com/messages", UriKind.Relative))) { }

        Assert.Equal("api.mailgun.net", fake.LastRequest!.RequestUri!.Host);
        Assert.Equal(
            Convert.ToBase64String(Encoding.ASCII.GetBytes("api:key-123")),
            fake.LastRequest!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public void Unnamed_client_remains_resolvable_alongside_named_clients()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us);
        services.AddMailgunner("marketing", "news.example.com", "key-mkt", MailgunRegion.Eu);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IMailgunnerClient>());
        Assert.NotNull(provider.GetRequiredService<IMailgunnerClientFactory>().Get("marketing"));
    }
}
