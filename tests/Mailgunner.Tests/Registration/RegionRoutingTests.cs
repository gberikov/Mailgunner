using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class RegionRoutingTests
{
    [Theory]
    [InlineData(MailgunRegion.Us, "api.mailgun.net")]
    [InlineData(MailgunRegion.Eu, "api.eu.mailgun.net")]
    public async Task Region_routes_to_the_matching_host(MailgunRegion region, string expectedHost)
    {
        var fake = new CapturingHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", region)
                .ConfigurePrimaryHttpMessageHandler(() => fake);
        using var provider = services.BuildServiceProvider();

        var client = (MailgunnerClient)provider.GetRequiredService<IMailgunnerClient>();
        using (await client.HttpClient.GetAsync(new Uri("v3/mg.example.com/messages", UriKind.Relative)))
        {
        }

        Assert.NotNull(fake.LastRequest);
        Assert.Equal(expectedHost, fake.LastRequest!.RequestUri!.Host);
    }
}
