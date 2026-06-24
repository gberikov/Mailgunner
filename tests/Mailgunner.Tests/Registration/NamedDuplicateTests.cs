using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class NamedDuplicateTests
{
    [Fact]
    public void Registering_the_same_name_twice_is_rejected_naming_the_duplicate()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("transactional", "tx.example.com", "key-tx", MailgunRegion.Us);

        var ex = Assert.Throws<ArgumentException>(() =>
            services.AddMailgunner("transactional", "other.example.com", "key-other", MailgunRegion.Eu));

        Assert.Equal("name", ex.ParamName);
        Assert.Contains("transactional", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Different_names_do_not_collide()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("transactional", "tx.example.com", "key-tx", MailgunRegion.Us);

        var exception = Record.Exception(() =>
            services.AddMailgunner("marketing", "news.example.com", "key-mkt", MailgunRegion.Eu));

        Assert.Null(exception);
    }
}
