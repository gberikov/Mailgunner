using System.Collections.Generic;
using System.Text;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class NamedConfigurationBindingTests
{
    [Fact]
    public async Task Config_section_binding_registers_a_resolvable_named_client()
    {
        var fake = new CapturingHttpMessageHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Domain"] = "audit.example.com",
                ["SendingKey"] = "key-audit",
                ["Region"] = "Eu",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMailgunner("audit", configuration)
                .ConfigurePrimaryHttpMessageHandler(() => fake);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IMailgunnerClientFactory>();

        var client = (MailgunnerClient)factory.Get("audit");
        using (await client.HttpClient.GetAsync(new Uri("v3/audit.example.com/messages", UriKind.Relative)))
        {
        }

        Assert.Equal("api.eu.mailgun.net", fake.LastRequest!.RequestUri!.Host);
        var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes("api:key-audit"));
        Assert.Equal(expected, fake.LastRequest!.Headers.Authorization!.Parameter);
    }
}
