using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class UnnamedReRegistrationTests
{
    [Fact]
    public void Registering_the_unnamed_client_twice_wires_a_single_resilience_handler()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("a.example.com", "key-1", MailgunRegion.Us);
        services.AddMailgunner("b.example.com", "key-2", MailgunRegion.Eu);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(nameof(IMailgunnerClient));

        Assert.Single(options.HttpMessageHandlerBuilderActions);
    }

    [Fact]
    public async Task Registering_twice_does_not_multiply_retry_attempts()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}");
        var time = new RecordingTimeProvider();
        var services = new ServiceCollection();
        services.AddMailgunner(o => { o.Domain = "a.example.com"; o.SendingKey = "key-1"; o.Region = MailgunRegion.Us; o.Retry.MaxRetryAttempts = 1; })
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        services.AddMailgunner(o => { o.Domain = "b.example.com"; o.SendingKey = "key-2"; o.Region = MailgunRegion.Us; o.Retry.MaxRetryAttempts = 1; });
        services.AddSingleton<TimeProvider>(time);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMailgunnerClient>();

        await Assert.ThrowsAsync<MailgunnerException>(() => client.Suppressions.Bounces.ListPageAsync());

        Assert.Equal(2, stub.Requests.Count); // 1 attempt + 1 retry, not (1+1)*(1+1)
        Assert.Equal("b.example.com", stub.LastRequestUri!.AbsolutePath.Split('/')[2]); // last options win
    }
}
