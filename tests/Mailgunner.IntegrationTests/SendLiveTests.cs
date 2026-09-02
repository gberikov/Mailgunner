using Xunit;

namespace Mailgunner.IntegrationTests;

public class SendLiveTests
{
    [Fact]
    public async Task Test_mode_send_is_accepted()
    {
        if (Live.Client is not { } client || Live.Recipient is null) { return; }
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
