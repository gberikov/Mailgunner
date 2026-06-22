using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class ClientResolutionTests
{
    [Fact]
    public void Valid_settings_make_the_client_resolvable()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us);
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IMailgunnerClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void Repeated_resolution_yields_a_usable_client()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us);
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IMailgunnerClient>();
        var second = provider.GetRequiredService<IMailgunnerClient>();

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task Last_registration_wins()
    {
        var fake = new CapturingHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us);
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Eu)
                .ConfigurePrimaryHttpMessageHandler(() => fake);
        using var provider = services.BuildServiceProvider();

        var client = (MailgunnerClient)provider.GetRequiredService<IMailgunnerClient>();
        using (await client.HttpClient.GetAsync(new Uri("v3/mg.example.com/messages", UriKind.Relative)))
        {
        }

        Assert.NotNull(fake.LastRequest);
        Assert.Equal("api.eu.mailgun.net", fake.LastRequest!.RequestUri!.Host);
    }
}
