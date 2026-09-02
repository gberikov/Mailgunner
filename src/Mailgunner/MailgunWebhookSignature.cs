namespace Mailgunner;

/// <summary>
/// Verifies that a Mailgun event webhook (a bounce, complaint, or unsubscribe notification)
/// genuinely originates from Mailgun before a consumer acts on it. This is a pure, network-free
/// primitive: it performs no HTTP, requires no dependency injection or configured client, and holds
/// no state. The signing key is supplied by the caller per invocation (sourced from the caller's own
/// configuration — use the Mailgun HTTP webhook signing key, not the sending key).
/// </summary>
/// <remarks>
/// Verification answers only "was this signed by the holder of the signing key?". The 4-argument
/// <see cref="Verify(string, string, string, string)"/> overload performs no timestamp-freshness
/// check; use the <see cref="Verify(string, string, string, string, System.TimeSpan, System.TimeProvider)"/>
/// overload to also reject a stale or future timestamp. Token-reuse (replay) checks remain the
/// consumer's responsibility either way.
/// </remarks>
public static class MailgunWebhookSignature
{
    private const string HexDigits = "0123456789abcdef";

    /// <summary>
    /// Determines whether a webhook is authentic by validating its signature. The webhook is
    /// authentic only when <paramref name="signature"/> equals the HMAC-SHA256 of
    /// <paramref name="timestamp"/> concatenated with <paramref name="token"/>, keyed by
    /// <paramref name="signingKey"/> and rendered as lowercase hexadecimal. The comparison is
    /// constant-time: it examines the full width of the candidate and does not short-circuit on the
    /// first differing character, so timing cannot reveal how many leading characters matched.
    /// </summary>
    /// <param name="signingKey">
    /// The Mailgun HTTP webhook signing key, supplied by the caller. Required.
    /// </param>
    /// <param name="timestamp">The webhook's timestamp field (untrusted input).</param>
    /// <param name="token">The webhook's token field (untrusted input).</param>
    /// <param name="signature">The webhook's hex signature field to validate (untrusted input).</param>
    /// <returns>
    /// <see langword="true"/> when the webhook is authentic; otherwise <see langword="false"/>. Any
    /// missing or malformed webhook-supplied value (a <see langword="null"/> timestamp/token, or a
    /// <see langword="null"/>, empty, wrong-length, or non-hexadecimal signature) yields
    /// <see langword="false"/> rather than throwing.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="signingKey"/> is <see langword="null"/>, empty, or whitespace. This is a
    /// configuration error surfaced to the caller, distinct from a forged webhook.
    /// </exception>
    public static bool Verify(string signingKey, string timestamp, string token, string signature)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new System.ArgumentException("A webhook signing key is required.", nameof(signingKey));
        }

        // The three webhook-supplied values are untrusted: any missing field fails closed to
        // "not authentic" rather than throwing.
        if (timestamp is null || token is null || signature is null)
        {
            return false;
        }

        byte[] mac;
        using (var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(signingKey)))
        {
            mac = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(timestamp + token));
        }

        // Compare in the produced-lowercase-hex domain. A wrong-length signature is rejected by the
        // length check inside the fixed-time comparison; a non-hexadecimal signature simply fails to
        // match — there is no hex-decode path that could throw.
        byte[] expected = System.Text.Encoding.ASCII.GetBytes(ToLowerHex(mac));
        byte[] provided = System.Text.Encoding.ASCII.GetBytes(signature);

        return FixedTimeEquals(expected, provided);
    }

    /// <summary>
    /// Verifies the signature exactly as <see cref="Verify(string, string, string, string)"/> and additionally
    /// requires the webhook's <paramref name="timestamp"/> (Unix seconds) to be within
    /// <paramref name="maxAge"/> of the current time in either direction, which defeats replay of an old
    /// capture. Both checks always run; the result is authentic only when both pass.
    /// </summary>
    /// <param name="signingKey">The Mailgun HTTP webhook signing key. Required.</param>
    /// <param name="timestamp">The webhook's timestamp field, Unix seconds (untrusted input).</param>
    /// <param name="token">The webhook's token field (untrusted input).</param>
    /// <param name="signature">The webhook's hex signature field (untrusted input).</param>
    /// <param name="maxAge">The largest accepted distance between <paramref name="timestamp"/> and now. Must be positive.</param>
    /// <param name="timeProvider">The clock to use; <see cref="System.TimeProvider.System"/> when null.</param>
    /// <returns><see langword="true"/> when the signature is valid and the timestamp is fresh; otherwise <see langword="false"/>.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="signingKey"/> is null, empty, or whitespace.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="maxAge"/> is not positive.</exception>
    public static bool Verify(
        string signingKey,
        string timestamp,
        string token,
        string signature,
        System.TimeSpan maxAge,
        System.TimeProvider? timeProvider = null)
    {
        if (maxAge <= System.TimeSpan.Zero)
        {
            throw new System.ArgumentOutOfRangeException(nameof(maxAge), maxAge, "The maximum age must be positive.");
        }

        // Evaluate the signature first so the key precondition (signingKey required) is enforced
        // identically to the base overload, regardless of whether the timestamp also turns out to be
        // fresh. Both the authenticity and freshness checks always run — neither short-circuits the
        // other — so timing cannot reveal which one failed.
        var authentic = Verify(signingKey, timestamp, token, signature);
        var fresh = IsFresh(timestamp, maxAge, timeProvider);

        return authentic && fresh;
    }

    private static bool IsFresh(string timestamp, System.TimeSpan maxAge, System.TimeProvider? timeProvider)
    {
        // The timestamp is attacker-controlled, so the parse is strict: NumberStyles.None rejects a
        // leading sign, whitespace, a decimal point, thousands separators, and hex — only plain ASCII
        // digits pass.
        if (timestamp is null
            || !long.TryParse(timestamp, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return false;
        }

        System.DateTimeOffset issued;
        try
        {
            issued = System.DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (System.ArgumentOutOfRangeException)
        {
            return false;
        }

        var now = (timeProvider ?? System.TimeProvider.System).GetUtcNow();
        var distance = now > issued ? now - issued : issued - now;

        return distance <= maxAge;
    }

    private static string ToLowerHex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i * 2] = HexDigits[b >> 4];
            chars[(i * 2) + 1] = HexDigits[b & 0xF];
        }

        return new string(chars);
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
#if NET8_0_OR_GREATER
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
#else
        // CryptographicOperations is unavailable on netstandard2.0, so do the constant-time compare
        // by hand: bail on a length mismatch (length is not secret), then XOR-accumulate every byte
        // pair with no early return so the work is independent of where the first difference occurs.
        if (left.Length != right.Length)
        {
            return false;
        }

        var difference = 0;
        for (var i = 0; i < left.Length; i++)
        {
            difference |= left[i] ^ right[i];
        }

        return difference == 0;
#endif
    }
}
