using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.NetFxTests;

public class NetStandardBuildTests
{
    private const string Key = "netfx-test-signing-key";

    private static IMailgunnerClient BuildClient(NetFxStubHandler stub, Action<MailgunnerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddMailgunner(o =>
        {
            o.Domain = "mg.example.com";
            o.SendingKey = "key-123";
            o.Region = MailgunRegion.Us;
            o.Retry.BaseDelay = TimeSpan.FromMilliseconds(1);
            configure?.Invoke(o);
        }).ConfigurePrimaryHttpMessageHandler(() => stub);
        return services.BuildServiceProvider().GetRequiredService<IMailgunnerClient>();
    }

    private static string Sign(string timestamp, string token)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Key));
        return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + token))).Replace("-", string.Empty).ToLowerInvariant();
    }

    [Fact]
    public void Manual_fixed_time_compare_accepts_a_valid_signature_and_rejects_a_tampered_one()
    {
        var signature = Sign("1529006854", "tok");

        Assert.True(MailgunWebhookSignature.Verify(Key, "1529006854", "tok", signature));
        Assert.False(MailgunWebhookSignature.Verify(Key, "1529006854", "tok", "0" + signature.Substring(1)));
        Assert.False(MailgunWebhookSignature.Verify(Key, "1529006854", "tok", signature.Substring(1)));
    }

    [Fact]
    public async Task Send_round_trips_through_the_netstandard_build()
    {
        var stub = new NetFxStubHandler((HttpStatusCode.OK, "{\"id\":\"<1@mg>\",\"message\":\"Queued.\"}"));
        var client = BuildClient(stub);
        var message = new MailgunMessage { From = "noreply@mg.example.com", Text = "Hi" };
        message.To.Add("alice@example.com");

        var result = await client.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal("<1@mg>", result.Id);
        Assert.Contains("name=to", stub.Bodies[0]);
    }

    [Fact]
    public async Task Retry_with_thread_static_random_jitter_works_on_net_framework()
    {
        var stub = new NetFxStubHandler(
            (HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}"),
            (HttpStatusCode.OK, "{\"items\":[{\"address\":\"a@x.com\",\"created_at\":\"Thu, 11 Dec 2025 01:49:40 UTC\"}],\"paging\":{}}"));
        var client = BuildClient(stub);

        var page = await client.Suppressions.Bounces.ListPageAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, stub.Requests);
        Assert.Equal(new DateTimeOffset(2025, 12, 11, 1, 49, 40, TimeSpan.Zero), page.Items[0].CreatedAt);
    }

    [Fact]
    public void Jitter_sources_started_on_the_same_tick_do_not_share_a_sequence()
    {
        // On .NET Framework `new Random()` seeds from Environment.TickCount, so per-thread instances
        // created within the same millisecond would draw identical jitter. Release 8 threads together
        // and require at least two distinct first draws.
        const int threads = 8;
        var values = new double[threads];
        using var gate = new Barrier(threads);
        var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
        {
            gate.SignalAndWait();
            values[i] = new Mailgunner.Internal.DefaultRetryRandom().NextDouble();
        })).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());

        Assert.True(values.Distinct().Count() > 1, "all threads drew the same first jitter value");
    }

    [Fact]
    public async Task Safe_send_mode_marks_requests_via_the_properties_bag()
    {
        var stub = new NetFxStubHandler((HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}"));
        var client = BuildClient(stub);
        var message = new MailgunMessage { From = "noreply@mg.example.com", Text = "Hi" };
        message.To.Add("alice@example.com");

        await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(message, TestContext.Current.CancellationToken));

        Assert.Equal(1, stub.Requests);
    }
}
