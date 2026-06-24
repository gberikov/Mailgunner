using Xunit;

namespace Mailgunner.Tests.Webhooks;

public class WebhookSignatureVerificationTests
{
    [Fact]
    public void Correct_signature_validates_as_authentic()
    {
        var signature = WebhookTestVectors.Sign(
            WebhookTestVectors.SigningKey, WebhookTestVectors.Timestamp, WebhookTestVectors.Token);

        var authentic = MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey,
            WebhookTestVectors.Timestamp,
            WebhookTestVectors.Token,
            signature);

        Assert.True(authentic);
    }

    [Fact]
    public void Signature_signed_with_a_different_key_is_not_authentic()
    {
        // Signed with one key, verified with another: the MAC differs, so it must not validate.
        var signature = WebhookTestVectors.Sign(
            "a-different-signing-key", WebhookTestVectors.Timestamp, WebhookTestVectors.Token);

        var authentic = MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey,
            WebhookTestVectors.Timestamp,
            WebhookTestVectors.Token,
            signature);

        Assert.False(authentic);
    }
}
