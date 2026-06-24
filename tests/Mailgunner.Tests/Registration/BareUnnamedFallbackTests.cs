using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class BareUnnamedFallbackTests
{
    [Fact]
    public void Only_named_clients_means_a_bare_unnamed_client_is_not_resolvable()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("transactional", "tx.example.com", "key-tx", MailgunRegion.Us);
        using var provider = services.BuildServiceProvider();

        // No implicit default: a bare IMailgunnerClient request fails, but Get(name) works.
        Assert.Null(provider.GetService<IMailgunnerClient>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IMailgunnerClient>());
        Assert.NotNull(provider.GetRequiredService<IMailgunnerClientFactory>().Get("transactional"));
    }
}
