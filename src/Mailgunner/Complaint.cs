namespace Mailgunner;

/// <summary>
/// A typed entry on a domain's <c>complaints</c> suppression list: an address that filed a spam
/// complaint. Used both as the result of reading the list and as the input to
/// <see cref="ISuppressionList{TEntry}.AddAsync"/> (on add, <see cref="CreatedAt"/> is server-populated
/// and ignored).
/// </summary>
public sealed class Complaint
{
    /// <summary>
    /// Gets the complaining recipient address. Required on add (non-blank).
    /// </summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// Gets the instant the entry was recorded, or <see langword="null"/> when absent or unparseable.
    /// Server-populated on read; ignored on add.
    /// </summary>
    public System.DateTimeOffset? CreatedAt { get; init; }
}
