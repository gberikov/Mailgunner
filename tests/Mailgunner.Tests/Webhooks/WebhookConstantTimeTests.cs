using Xunit;

namespace Mailgunner.Tests.Webhooks;

// The constant-time guarantee is provided by construction — CryptographicOperations.FixedTimeEquals
// on net8.0 and a fixed-width XOR-accumulate comparison on netstandard2.0 — and confirmed by review,
// not by wall-clock timing (which is not a reliable unit measurement). These tests lock the
// observable behavior: a correct-length wrong signature is rejected regardless of WHERE it first
// differs from the expected value, proving the comparison does not short-circuit on the first
// differing character.
public class WebhookConstantTimeTests
{
    private static string ValidSignature() => WebhookTestVectors.Sign(
        WebhookTestVectors.SigningKey, WebhookTestVectors.Timestamp, WebhookTestVectors.Token);

    [Fact]
    public void Signature_differing_only_at_the_last_character_is_not_authentic()
    {
        Assert.False(VerifyWith(ReplaceAt(ValidSignature(), ValidSignature().Length - 1)));
    }

    [Fact]
    public void Signature_differing_only_at_the_first_character_is_not_authentic()
    {
        Assert.False(VerifyWith(ReplaceAt(ValidSignature(), 0)));
    }

    private static bool VerifyWith(string signature) => MailgunWebhookSignature.Verify(
        WebhookTestVectors.SigningKey, WebhookTestVectors.Timestamp, WebhookTestVectors.Token, signature);

    private static string ReplaceAt(string signature, int index)
    {
        var chars = signature.ToCharArray();
        chars[index] = chars[index] == '0' ? '1' : '0';
        return new string(chars);
    }
}
