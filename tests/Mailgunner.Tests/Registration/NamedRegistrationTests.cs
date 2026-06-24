using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class NamedRegistrationTests
{
    [Fact]
    public void Explicit_and_callback_forms_register_two_distinct_names_that_both_resolve()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("transactional", "tx.example.com", "key-tx", MailgunRegion.Us);
        services.AddMailgunner("marketing", o =>
        {
            o.Domain = "news.example.com";
            o.SendingKey = "key-mkt";
            o.Region = MailgunRegion.Eu;
        });
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IMailgunnerClientFactory>();

        Assert.NotNull(factory.Get("transactional"));
        Assert.NotNull(factory.Get("marketing"));
    }

    [Fact]
    public async Task Neither_named_registration_overwrites_the_other()
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
        using (await Send(tx, "tx.example.com")) { }
        using (await Send(mkt, "news.example.com")) { }

        Assert.Equal("api.mailgun.net", fakeTx.LastRequest!.RequestUri!.Host);
        Assert.Equal("api.eu.mailgun.net", fakeMkt.LastRequest!.RequestUri!.Host);
    }

    [Fact]
    public void Repeated_resolution_of_a_name_yields_a_usable_client_each_time()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("transactional", "tx.example.com", "key-tx", MailgunRegion.Us);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IMailgunnerClientFactory>();

        Assert.NotNull(factory.Get("transactional"));
        Assert.NotNull(factory.Get("transactional"));
    }

    private static Task<HttpResponseMessage> Send(MailgunnerClient client, string domain) =>
        client.HttpClient.GetAsync(new Uri($"v3/{domain}/messages", UriKind.Relative));
}
