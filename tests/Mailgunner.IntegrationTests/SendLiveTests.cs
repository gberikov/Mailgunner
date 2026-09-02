using Xunit;

namespace Mailgunner.IntegrationTests;

public class SendLiveTests
{
    [SkippableFact]
    public async Task Test_mode_send_is_accepted()
    {
        Skip.If(Live.Client is null, Live.NotConfigured);
        Skip.If(Live.Recipient is null, "Mailgun__Recipients__0__Address not set");
        var client = Live.Client!;
        var message = new MailgunMessage
        {
            From = $"postmaster@{Live.Domain}",
            Subject = "Mailgunner live check",
            Text = "test mode, not delivered",
        };
        message.To.Add(Live.Recipient);
        message.Options.TestMode = true;

        var result = await client.SendAsync(message);

        Assert.False(string.IsNullOrEmpty(result.Id));
    }
}
