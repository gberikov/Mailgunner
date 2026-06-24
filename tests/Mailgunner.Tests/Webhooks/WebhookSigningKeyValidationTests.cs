using Xunit;

namespace Mailgunner.Tests.Webhooks;

public class WebhookSigningKeyValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_or_missing_signing_key_throws_argument_exception(string? signingKey)
    {
        var signature = WebhookTestVectors.Sign(
            WebhookTestVectors.SigningKey, WebhookTestVectors.Timestamp, WebhookTestVectors.Token);

        var ex = Assert.Throws<ArgumentException>(() => MailgunWebhookSignature.Verify(
            signingKey!, WebhookTestVectors.Timestamp, WebhookTestVectors.Token, signature));

        Assert.Equal("signingKey", ex.ParamName);
    }
}
