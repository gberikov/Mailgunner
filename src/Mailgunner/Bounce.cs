namespace Mailgunner;

/// <summary>
/// A typed entry on a domain's <c>bounces</c> suppression list: an address that hard-bounced, together
/// with the failure code and error detail Mailgun recorded. Used both as the result of reading the list
/// and as the input to <see cref="ISuppressionList{TEntry}.AddAsync"/> (on add, <see cref="CreatedAt"/>
/// is server-populated and ignored).
/// </summary>
public sealed class Bounce
{
    /// <summary>
    /// Gets the bounced recipient address. Required on add (non-blank).
    /// </summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// Gets the SMTP/bounce failure code (for example <c>"550"</c>), or <see langword="null"/> when not
    /// supplied. Optional on add.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Gets the human-readable failure detail, or <see langword="null"/> when not supplied. Optional on add.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the instant the entry was recorded, or <see langword="null"/> when absent or unparseable.
    /// Server-populated on read; ignored on add.
    /// </summary>
    public System.DateTimeOffset? CreatedAt { get; init; }
}
