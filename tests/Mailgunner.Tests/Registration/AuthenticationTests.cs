using System.Text;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class AuthenticationTests
{
    [Fact]
    public async Task Requests_carry_http_basic_auth_derived_from_the_sending_key()
    {
        var fake = new CapturingHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => fake);
        using var provider = services.BuildServiceProvider();

        var client = (MailgunnerClient)provider.GetRequiredService<IMailgunnerClient>();
        using (await client.HttpClient.GetAsync(new Uri("v3/mg.example.com/messages", UriKind.Relative)))
        {
        }

        var authorization = fake.LastRequest!.Headers.Authorization;
        Assert.NotNull(authorization);
        Assert.Equal("Basic", authorization!.Scheme);

        var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes("api:key-123"));
        Assert.Equal(expected, authorization.Parameter);
    }
}
