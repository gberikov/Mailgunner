namespace Mailgunner;

/// <summary>
/// A typed entry on a domain's <c>unsubscribes</c> suppression list: an address that unsubscribed,
/// together with the tags it unsubscribed from. Used both as the result of reading the list and as the
/// input to <see cref="ISuppressionList{TEntry}.AddAsync"/> (on add, <see cref="CreatedAt"/> is
/// server-populated and ignored).
/// </summary>
public sealed class Unsubscribe
{
    /// <summary>
    /// Gets the unsubscribed recipient address. Required on add (non-blank).
    /// </summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tags the address unsubscribed from. A single <c>"*"</c> tag means all mail. Defaults to
    /// an empty list; on add the tags are sent only when non-empty.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<string> Tags { get; init; }
        = System.Array.Empty<string>();

    /// <summary>
    /// Gets the instant the entry was recorded, or <see langword="null"/> when absent or unparseable.
    /// Server-populated on read; ignored on add.
    /// </summary>
    public System.DateTimeOffset? CreatedAt { get; init; }
}
