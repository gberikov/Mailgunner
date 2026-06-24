using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class NamedSecretHygieneTests
{
    private const string Secret = "super-secret-sending-key-value";

    [Fact]
    public void Named_validation_failure_never_exposes_the_sending_key()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("transactional", o =>
        {
            o.Domain = string.Empty; // invalid → forces a failure
            o.SendingKey = Secret;   // valid value that must not be echoed
            o.Region = MailgunRegion.Us;
        });
        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        var ex = Assert.Throws<OptionsValidationException>(() => validator.Validate());

        Assert.DoesNotContain(Secret, ex.Message, StringComparison.Ordinal);
        Assert.All(ex.Failures, f => Assert.DoesNotContain(Secret, f, StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_name_error_never_exposes_the_sending_key()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("transactional", "tx.example.com", Secret, MailgunRegion.Us);

        var ex = Assert.Throws<ArgumentException>(() =>
            services.AddMailgunner("transactional", "tx.example.com", Secret, MailgunRegion.Us));

        Assert.DoesNotContain(Secret, ex.Message, StringComparison.Ordinal);
    }
}
