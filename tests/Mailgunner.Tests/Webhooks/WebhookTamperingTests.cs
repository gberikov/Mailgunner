using Xunit;

namespace Mailgunner.Tests.Webhooks;

public class WebhookTamperingTests
{
    private static string ValidSignature() => WebhookTestVectors.Sign(
        WebhookTestVectors.SigningKey, WebhookTestVectors.Timestamp, WebhookTestVectors.Token);

    [Fact]
    public void Tampered_signature_is_not_authentic()
    {
        var tampered = FlipLastHexChar(ValidSignature());

        var authentic = MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey,
            WebhookTestVectors.Timestamp,
            WebhookTestVectors.Token,
            tampered);

        Assert.False(authentic);
    }

    [Fact]
    public void Tampered_timestamp_is_not_authentic()
    {
        // Original token + original signature, but a changed timestamp.
        var authentic = MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey,
            WebhookTestVectors.Timestamp + "1",
            WebhookTestVectors.Token,
            ValidSignature());

        Assert.False(authentic);
    }

    [Fact]
    public void Tampered_token_is_not_authentic()
    {
        // Original timestamp + original signature, but a changed token.
        var authentic = MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey,
            WebhookTestVectors.Timestamp,
            WebhookTestVectors.Token + "0",
            ValidSignature());

        Assert.False(authentic);
    }

    private static string FlipLastHexChar(string signature)
    {
        var last = signature[signature.Length - 1];
        var replacement = last == '0' ? '1' : '0';
        return signature.Substring(0, signature.Length - 1) + replacement;
    }
}
