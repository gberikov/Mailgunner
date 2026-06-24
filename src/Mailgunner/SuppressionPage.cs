namespace Mailgunner;

/// <summary>
/// One page of a suppression-list response: the parsed entries plus an opaque pointer to the next page.
/// Returned by <see cref="ISuppressionList{TEntry}.ListPageAsync(int?, System.Threading.CancellationToken)"/>
/// so callers can drive pagination themselves; the auto-following
/// <see cref="ISuppressionList{TEntry}.ListAsync"/> is built on top of it.
/// </summary>
/// <typeparam name="TEntry">The suppression entry type for this list (e.g. <see cref="Bounce"/>).</typeparam>
public sealed class SuppressionPage<TEntry>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SuppressionPage{TEntry}"/> class.
    /// </summary>
    /// <param name="items">The entries on this page, in service order; may be empty.</param>
    /// <param name="nextCursor">The opaque next-page pointer, or <see langword="null"/> at the end of the list.</param>
    public SuppressionPage(System.Collections.Generic.IReadOnlyList<TEntry> items, string? nextCursor)
    {
        Items = items;
        NextCursor = nextCursor;
        HasMore = items.Count > 0 && !string.IsNullOrEmpty(nextCursor);
    }

    /// <summary>
    /// Gets the entries parsed from this page, in service order. May be empty (which marks the end of the list).
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<TEntry> Items { get; }

    /// <summary>
    /// Gets the opaque pointer to the next page (the service's next link), or <see langword="null"/> when
    /// the service returned no next pointer. Pass it to
    /// <see cref="ISuppressionList{TEntry}.ListPageAsync(string, System.Threading.CancellationToken)"/> to
    /// fetch the next page. The value is opaque and must be followed verbatim.
    /// </summary>
    public string? NextCursor { get; }

    /// <summary>
    /// Gets a value indicating whether another page should be fetched: <see langword="true"/> when this
    /// page is non-empty <em>and</em> a <see cref="NextCursor"/> is present. An empty page marks the end
    /// of the list even if a next pointer is present.
    /// </summary>
    public bool HasMore { get; }
}
