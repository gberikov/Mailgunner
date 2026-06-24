using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

public class WebhookCancellationTests
{
    [Fact]
    public async Task A_cancelled_token_stops_a_single_operation()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, WebhookHarness.EmptyList);
        var cts = new CancellationTokenSource();
        stub.OnSend = _ => cts.Cancel();
        var client = WebhookHarness.BuildClient(stub);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Webhooks.ListAsync(cts.Token));
    }

    [Fact]
    public async Task Cancellation_mid_fan_out_issues_no_further_creates()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, WebhookHarness.Envelope("https://a"));
        var cts = new CancellationTokenSource();
        var sends = 0;
        stub.OnSend = _ =>
        {
            sends++;
            if (sends == 1)
            {
                cts.Cancel();
            }
        };
        var client = WebhookHarness.BuildClient(stub);
        var events = new[] { WebhookEventType.Delivered, WebhookEventType.Opened, WebhookEventType.Clicked };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.Webhooks.CreateAsync(events, "https://a", cts.Token));

        // The first create was attempted and cancelled; no further creates were issued.
        Assert.Single(stub.Requests);
    }
}
