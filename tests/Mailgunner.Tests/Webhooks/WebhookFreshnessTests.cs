using System.Globalization;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Webhooks;

public class WebhookFreshnessTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static (string Timestamp, string Signature) Signed(long unixSeconds)
    {
        var ts = unixSeconds.ToString(CultureInfo.InvariantCulture);
        return (ts, WebhookTestVectors.Sign(WebhookTestVectors.SigningKey, ts, WebhookTestVectors.Token));
    }

    [Fact]
    public void A_recent_valid_signature_is_accepted()
    {
        var (ts, sig) = Signed(Now.ToUnixTimeSeconds() - 30);

        Assert.True(MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, ts, WebhookTestVectors.Token, sig, TimeSpan.FromMinutes(5), new RecordingTimeProvider(Now)));
    }

    [Fact]
    public void A_stale_valid_signature_is_rejected()
    {
        var (ts, sig) = Signed(Now.ToUnixTimeSeconds() - 600);

        Assert.False(MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, ts, WebhookTestVectors.Token, sig, TimeSpan.FromMinutes(5), new RecordingTimeProvider(Now)));
    }

    [Fact]
    public void A_far_future_timestamp_is_rejected()
    {
        var (ts, sig) = Signed(Now.ToUnixTimeSeconds() + 600);

        Assert.False(MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, ts, WebhookTestVectors.Token, sig, TimeSpan.FromMinutes(5), new RecordingTimeProvider(Now)));
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData("1.5")]
    public void A_non_integer_timestamp_is_rejected(string timestamp)
    {
        var sig = WebhookTestVectors.Sign(WebhookTestVectors.SigningKey, timestamp, WebhookTestVectors.Token);

        Assert.False(MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, timestamp, WebhookTestVectors.Token, sig, TimeSpan.FromMinutes(5), new RecordingTimeProvider(Now)));
    }

    [Fact]
    public void A_forged_signature_is_rejected_even_when_fresh()
    {
        var (ts, _) = Signed(Now.ToUnixTimeSeconds());

        Assert.False(MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, ts, WebhookTestVectors.Token, new string('0', 64), TimeSpan.FromMinutes(5), new RecordingTimeProvider(Now)));
    }

    [Fact]
    public void Non_positive_max_age_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, "1", WebhookTestVectors.Token, new string('0', 64), TimeSpan.Zero));
    }
}
