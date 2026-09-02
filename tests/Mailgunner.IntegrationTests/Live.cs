using Microsoft.Extensions.DependencyInjection;

namespace Mailgunner.IntegrationTests;

/// <summary>
/// Reads live credentials from the environment. When any is absent, <see cref="Client"/> is null and every
/// test reports Skipped via <c>Skip.If</c>, so the suite is green with no secrets and never runs in CI.
/// </summary>
internal static class Live
{
    /// <summary>Reason xunit reports for a test skipped because live credentials are not configured.</summary>
    public const string NotConfigured = "Mailgun__Domain / Mailgun__SendingKey / Mailgun__Region not set";

    public static readonly string? Domain = Env("Mailgun__Domain");
    public static readonly string? Recipient = Env("Mailgun__Recipients__0__Address");

    public static readonly IMailgunnerClient? Client = Build();

    /// <summary>
    /// Runs a best-effort cleanup step. Failures are swallowed so a cleanup problem never replaces the
    /// test's real assertion failure in its stack trace, and callers isolate each cleanup step in its own
    /// call so one throwing does not stop a sibling cleanup step from running.
    /// </summary>
    public static async System.Threading.Tasks.Task CleanupAsync(System.Func<System.Threading.Tasks.Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (MailgunnerException)
        {
        }
    }

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
