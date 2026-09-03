using Mailgunner.Internal;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mailgunner.Tests.Registration;

/// <summary>
/// The typed <see cref="HttpClient"/> is hardened at registration: its overall timeout is the worst-case
/// bound over every attempt and wait (so a stalled response body can never hang a caller that passed no
/// token), the <c>Authorization</c> header is redacted from the HTTP client factory's own logging, and
/// every request identifies the library via <c>User-Agent</c>.
/// </summary>
public class HttpClientHardeningTests
{
    private static void Configure(MailgunnerOptions o)
    {
        o.Domain = "mg.example.com";
        o.SendingKey = "key-123";
        o.Region = MailgunRegion.Us;
        o.Retry.MaxRetryAttempts = 2;
        o.Retry.AttemptTimeout = TimeSpan.FromSeconds(10);
        o.Retry.MaxSingleWait = TimeSpan.FromSeconds(5);
    }

    [Fact]
    public void Unnamed_client_timeout_is_the_worst_case_bound_over_all_attempts_and_waits()
    {
        var services = new ServiceCollection();
        services.AddMailgunner(Configure);
        using var provider = services.BuildServiceProvider();

        var client = (MailgunnerClient)provider.GetRequiredService<IMailgunnerClient>();

        // 3 attempts x 10 s + 2 waits x 5 s.
        Assert.Equal(TimeSpan.FromSeconds(40), client.HttpClient.Timeout);
    }

    [Fact]
    public void Named_client_timeout_is_the_worst_case_bound_over_all_attempts_and_waits()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("tx", Configure);
        using var provider = services.BuildServiceProvider();

        var client = (MailgunnerClient)provider.GetRequiredService<IMailgunnerClientFactory>().Get("tx");

        Assert.Equal(TimeSpan.FromSeconds(40), client.HttpClient.Timeout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tx")]
    public void Authorization_header_is_redacted_from_http_client_logging(string? name)
    {
        var services = new ServiceCollection();
        if (name is null)
        {
            services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us);
        }
        else
        {
            services.AddMailgunner(name, "mg.example.com", "key-123", MailgunRegion.Us);
        }

        using var provider = services.BuildServiceProvider();
        var httpClientName = name is null ? nameof(IMailgunnerClient) : NamedClientRegistry.HttpClientName(name);

        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(httpClientName);

        Assert.True(options.ShouldRedactHeaderValue("Authorization"));
        Assert.False(options.ShouldRedactHeaderValue("Content-Type"));
    }

    [Fact]
    public async Task Requests_carry_a_mailgunner_user_agent_with_a_version()
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

        var product = Assert.Single(fake.LastRequest!.Headers.UserAgent);
        Assert.Equal("Mailgunner", product.Product!.Name);
        Assert.False(string.IsNullOrEmpty(product.Product.Version));
    }
}
