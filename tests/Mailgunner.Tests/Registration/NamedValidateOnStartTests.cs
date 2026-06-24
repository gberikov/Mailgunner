using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class NamedValidateOnStartTests
{
    [Fact]
    public void Blank_domain_for_a_named_client_fails_at_startup_identifying_name_and_setting()
    {
        var ex = ValidateThrows("transactional", o =>
        {
            o.Domain = string.Empty;
            o.SendingKey = "key-secret-abc";
            o.Region = MailgunRegion.Us;
        });

        Assert.Equal("transactional", ex.OptionsName);
        Assert.Contains(ex.Failures, f => f.Contains("domain", StringComparison.OrdinalIgnoreCase));
        // FR-016: a validation failure is a standard .NET error, never a MailgunnerException.
        Assert.IsNotType<MailgunnerException>(ex);
    }

    [Fact]
    public void Undefined_region_for_a_named_client_fails_at_startup()
    {
        var ex = ValidateThrows("marketing", o =>
        {
            o.Domain = "news.example.com";
            o.SendingKey = "key-secret-abc";
            o.Region = (MailgunRegion)999;
        });

        Assert.Equal("marketing", ex.OptionsName);
        Assert.Contains(ex.Failures, f => f.Contains("region", StringComparison.OrdinalIgnoreCase));
    }

    private static OptionsValidationException ValidateThrows(string name, Action<MailgunnerOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddMailgunner(name, configure);
        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        return Assert.Throws<OptionsValidationException>(() => validator.Validate());
    }
}
