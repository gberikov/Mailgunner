using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Mailgunner.Tests.Webhooks;

/// <summary>
/// Test-only helper that independently computes a Mailgun webhook signature (no production code is
/// reused), so the verification tests assert against a reference value rather than the code under
/// test. The signing key here is a throwaway test value — never a real Mailgun key.
/// </summary>
internal static class WebhookTestVectors
{
    public const string SigningKey = "test-webhook-signing-key-0123456789";
    public const string Timestamp = "1529006854";
    public const string Token = "7e2e5f0a8b1c4d3e9f6a2b5c8d1e4f7a0b3c6d9e";

    /// <summary>
    /// Computes lowercase-hex <c>HMACSHA256(UTF8(signingKey), UTF8(timestamp + token))</c> — the value
    /// Mailgun would send as the signature.
    /// </summary>
    public static string Sign(string signingKey, string timestamp, string token)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + token));
        var builder = new StringBuilder(mac.Length * 2);
        foreach (var b in mac)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
