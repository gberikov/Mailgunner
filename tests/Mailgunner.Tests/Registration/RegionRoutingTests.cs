using System.Net;
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

    [Fact]
    public async Task Domain_is_percent_encoded_in_the_request_path()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"items\":[],\"paging\":{}}");
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com/../other", "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMailgunnerClient>();

        await client.Suppressions.Bounces.ListPageAsync();

        Assert.NotNull(stub.LastRequest);
        Assert.Equal("/v3/mg.example.com%2F..%2Fother/bounces", stub.LastRequestUri!.AbsolutePath);
    }
}
