using Xunit;

namespace Mailgunner.Tests.Webhooks;

// Verification is a pure static primitive: it is called directly here with no IMailgunnerClient, no
// dependency-injection container, no HttpClient, and no configuration constructed — proving it is
// usable independently of the rest of the library (FR-006, FR-007, SC-005).
public class WebhookIndependenceTests
{
    [Fact]
    public void Verify_is_callable_without_any_client_or_dependency_injection()
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
    public void Verify_is_pure_and_returns_the_same_result_for_the_same_inputs()
    {
        var signature = WebhookTestVectors.Sign(
            WebhookTestVectors.SigningKey, WebhookTestVectors.Timestamp, WebhookTestVectors.Token);

        var first = MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, WebhookTestVectors.Timestamp, WebhookTestVectors.Token, signature);
        var second = MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, WebhookTestVectors.Timestamp, WebhookTestVectors.Token, signature);

        Assert.Equal(first, second);
        Assert.True(first);
    }
}
