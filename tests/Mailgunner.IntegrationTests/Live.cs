using Microsoft.Extensions.DependencyInjection;

namespace Mailgunner.IntegrationTests;

/// <summary>
/// Reads live credentials from the environment. When any is absent, <see cref="Client"/> is null and every
/// test returns early, so the suite is green with no secrets and never runs in CI.
/// </summary>
internal static class Live
{
    public static readonly string? Domain = Env("Mailgun__Domain");
    public static readonly string? Recipient = Env("Mailgun__Recipients__0__Address");

    public static readonly IMailgunnerClient? Client = Build();

    private static IMailgunnerClient? Build()
    {
        var key = Env("Mailgun__SendingKey");
        var region = Env("Mailgun__Region");
        if (Domain is null || key is null || region is null || !Enum.TryParse<MailgunRegion>(region, ignoreCase: true, out var parsed))
        {
            return null;
        }

        var services = new ServiceCollection();
        services.AddMailgunner(Domain, key, parsed);
        return services.BuildServiceProvider().GetRequiredService<IMailgunnerClient>();
    }

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
