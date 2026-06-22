using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class CancellationTests
{
    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"id\":\"<x>\",\"message\":\"Queued.\"}");
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    private static MailgunMessage NewMessage()
    {
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com"),
            Text = "Hi",
        };
        message.To.Add("alice@example.com");
        return message;
    }

    [Fact]
    public async Task Already_canceled_token_surfaces_cancellation_and_returns_no_result()
    {
        var (client, _) = BuildClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync(NewMessage(), cts.Token));
    }

    [Fact]
    public async Task Token_canceled_in_flight_surfaces_cancellation_cooperatively()
    {
        var (client, stub) = BuildClient();
        using var cts = new CancellationTokenSource();
        stub.OnSend = _ => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync(NewMessage(), cts.Token));
    }
}
