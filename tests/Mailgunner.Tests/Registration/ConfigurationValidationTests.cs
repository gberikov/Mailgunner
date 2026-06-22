using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class ConfigurationValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_or_blank_domain_fails_at_startup_naming_the_domain(string domain)
    {
        var ex = ValidateThrows(o =>
        {
            o.Domain = domain;
            o.SendingKey = "key-123";
            o.Region = MailgunRegion.Us;
        });

        Assert.Contains(ex.Failures, f => f.Contains("domain", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_or_blank_sending_key_fails_at_startup_naming_the_key(string sendingKey)
    {
        var ex = ValidateThrows(o =>
        {
            o.Domain = "mg.example.com";
            o.SendingKey = sendingKey;
            o.Region = MailgunRegion.Us;
        });

        Assert.Contains(ex.Failures, f => f.Contains("sending key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validation_failure_never_exposes_the_sending_key_value()
    {
        const string secret = "super-secret-sending-key-value";

        var ex = ValidateThrows(o =>
        {
            o.Domain = string.Empty; // invalid, forces a failure
            o.SendingKey = secret;   // valid, must not be echoed
            o.Region = MailgunRegion.Us;
        });

        Assert.DoesNotContain(secret, ex.Message);
        Assert.All(ex.Failures, f => Assert.DoesNotContain(secret, f));
    }

    [Fact]
    public void Unrecognized_region_fails_at_startup_naming_the_region()
    {
        var ex = ValidateThrows(o =>
        {
            o.Domain = "mg.example.com";
            o.SendingKey = "key-123";
            o.Region = (MailgunRegion)999;
        });

        Assert.Contains(ex.Failures, f => f.Contains("region", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unspecified_region_fails_at_startup_naming_the_region()
    {
        // Region left at its default value, which is not a defined MailgunRegion.
        var ex = ValidateThrows(o =>
        {
            o.Domain = "mg.example.com";
            o.SendingKey = "key-123";
        });

        Assert.Contains(ex.Failures, f => f.Contains("region", StringComparison.OrdinalIgnoreCase));
    }

    private static OptionsValidationException ValidateThrows(Action<MailgunnerOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddMailgunner(configure);
        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        return Assert.Throws<OptionsValidationException>(() => validator.Validate());
    }
}
