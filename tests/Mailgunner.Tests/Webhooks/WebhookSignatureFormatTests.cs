using Xunit;

namespace Mailgunner.Tests.Webhooks;

public class WebhookSignatureFormatTests
{
    private static string ValidSignature() => WebhookTestVectors.Sign(
        WebhookTestVectors.SigningKey, WebhookTestVectors.Timestamp, WebhookTestVectors.Token);

    [Fact]
    public void Empty_signature_is_not_authentic_without_throwing()
    {
        Assert.False(Verify(string.Empty));
    }

    [Fact]
    public void Null_signature_is_not_authentic_without_throwing()
    {
        Assert.False(Verify(null!));
    }

    [Fact]
    public void Wrong_length_signature_is_not_authentic_without_throwing()
    {
        var valid = ValidSignature();

        // One hex digit short of the genuine 64-character signature.
        Assert.False(Verify(valid.Substring(0, valid.Length - 1)));
    }

    [Fact]
    public void Non_hex_signature_of_correct_length_is_not_authentic_without_throwing()
    {
        // 64 characters, none of them hexadecimal.
        Assert.False(Verify(new string('z', 64)));
    }

    [Fact]
    public void Null_timestamp_is_not_authentic_without_throwing()
    {
        var authentic = MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, null!, WebhookTestVectors.Token, ValidSignature());

        Assert.False(authentic);
    }

    [Fact]
    public void Null_token_is_not_authentic_without_throwing()
    {
        var authentic = MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, WebhookTestVectors.Timestamp, null!, ValidSignature());

        Assert.False(authentic);
    }

    [Fact]
    public void Empty_timestamp_and_token_with_a_matching_signature_is_authentic()
    {
        // Contract C11: empty (as opposed to null) fields still yield a definite, signature-driven
        // answer — here, a signature correctly computed over empty timestamp + empty token.
        var signature = WebhookTestVectors.Sign(WebhookTestVectors.SigningKey, string.Empty, string.Empty);

        var authentic = MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, string.Empty, string.Empty, signature);

        Assert.True(authentic);
    }

    private static bool Verify(string signature) => MailgunWebhookSignature.Verify(
        WebhookTestVectors.SigningKey, WebhookTestVectors.Timestamp, WebhookTestVectors.Token, signature);
}
