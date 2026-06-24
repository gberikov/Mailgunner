using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class NamedRetryIsolationTests
{
    [Fact]
    public async Task Each_named_client_honors_only_its_own_retry_budget()
    {
        var noRetry = new StubHttpMessageHandler(HttpStatusCode.InternalServerError);
        var retries = new StubHttpMessageHandler(HttpStatusCode.InternalServerError);
        var time = new RecordingTimeProvider();

        var services = new ServiceCollection();
        services.AddMailgunner("no-retry", o =>
        {
            o.Domain = "a.example.com";
            o.SendingKey = "key-a";
            o.Region = MailgunRegion.Us;
            o.Retry.MaxRetryAttempts = 0;
        }).ConfigurePrimaryHttpMessageHandler(() => noRetry);
        services.AddMailgunner("retry-twice", o =>
        {
            o.Domain = "b.example.com";
            o.SendingKey = "key-b";
            o.Region = MailgunRegion.Us;
            o.Retry.MaxRetryAttempts = 2;
            o.Retry.UseJitter = false;
        }).ConfigurePrimaryHttpMessageHandler(() => retries);
        // Registered after AddMailgunner so it wins over the TryAdd default and waits are instant.
        services.AddSingleton<TimeProvider>(time);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IMailgunnerClientFactory>();

        var a = (MailgunnerClient)factory.Get("no-retry");
        var b = (MailgunnerClient)factory.Get("retry-twice");
        using (await a.HttpClient.GetAsync(new Uri("v3/a.example.com/messages", UriKind.Relative))) { }
        using (await b.HttpClient.GetAsync(new Uri("v3/b.example.com/messages", UriKind.Relative))) { }

        Assert.Single(noRetry.Requests);          // 0 retries → 1 attempt
        Assert.Equal(3, retries.Requests.Count);   // 2 retries → 3 attempts
    }
}
