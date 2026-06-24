using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class NamedResolutionTests
{
    [Fact]
    public void Get_returns_a_full_client_exposing_send_and_suppressions()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IMailgunnerClientFactory>();

        var client = factory.Get("transactional");

        Assert.NotNull(client);
        Assert.NotNull(client.Suppressions);
    }

    [Fact]
    public void Each_name_resolves_to_its_own_client()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IMailgunnerClientFactory>();

        var tx = factory.Get("transactional");
        var mkt = factory.Get("marketing");

        Assert.NotNull(tx);
        Assert.NotNull(mkt);
        Assert.NotSame(tx, mkt);
    }

    [Fact]
    public void Name_matching_is_ordinal_and_case_sensitive()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IMailgunnerClientFactory>();

        // Registered as "transactional"; a different-cased name is a different, unregistered name.
        Assert.Throws<ArgumentException>(() => factory.Get("Transactional"));
        Assert.NotNull(factory.Get("transactional"));
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("transactional", "tx.example.com", "key-tx", MailgunRegion.Us);
        services.AddMailgunner("marketing", "news.example.com", "key-mkt", MailgunRegion.Eu);
        return services.BuildServiceProvider();
    }
}
