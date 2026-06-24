using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class NamedValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_is_rejected_at_registration(string? name)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() =>
            services.AddMailgunner(name!, "mg.example.com", "key-123", MailgunRegion.Us));

        Assert.Equal("name", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_is_rejected_on_the_callback_overload(string name)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() =>
            services.AddMailgunner(name, o =>
            {
                o.Domain = "mg.example.com";
                o.SendingKey = "key-123";
                o.Region = MailgunRegion.Us;
            }));

        Assert.Equal("name", ex.ParamName);
    }
}
