using Xunit;

namespace Mailgunner.IntegrationTests;

public class SendLiveTests
{
    [Fact]
    public async Task Test_mode_send_is_accepted()
    {
        if (Live.Client is null)
        {
            Assert.Skip(Live.NotConfigured);
        }
        if (Live.Recipient is null)
        {
            Assert.Skip("Mailgun__Recipients__0__Address not set");
        }
        var ct = TestContext.Current.CancellationToken;
        var client = Live.Client!;
        var message = new MailgunMessage
        {
            From = $"postmaster@{Live.Domain}",
            Subject = "Mailgunner live check",
            Text = "test mode, not delivered",
        };
        message.To.Add(Live.Recipient);
        message.Options.TestMode = true;

        var result = await client.SendAsync(message, ct);

        Assert.False(string.IsNullOrEmpty(result.Id));
    }
}
