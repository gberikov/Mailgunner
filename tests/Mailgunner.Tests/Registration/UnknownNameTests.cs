using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class UnknownNameTests
{
    [Fact]
    public void Resolving_an_unknown_name_throws_naming_it_and_listing_registered_names()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IMailgunnerClientFactory>();

        var ex = Assert.Throws<ArgumentException>(() => factory.Get("nope"));

        Assert.Equal("name", ex.ParamName);
        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
        Assert.Contains("transactional", ex.Message, StringComparison.Ordinal);
        // FR-016: an unknown-name lookup is a standard .NET error, never a MailgunnerException.
        Assert.IsNotType<MailgunnerException>(ex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolving_a_null_or_blank_name_throws_argument_exception(string? name)
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IMailgunnerClientFactory>();

        var ex = Assert.Throws<ArgumentException>(() => factory.Get(name!));

        Assert.Equal("name", ex.ParamName);
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("transactional", "tx.example.com", "key-tx", MailgunRegion.Us);
        return services.BuildServiceProvider();
    }
}
