namespace Mailgunner;

/// <summary>
/// Operations on a single suppression list type (bounces, unsubscribes, or complaints) for the
/// configured domain. These are JSON endpoints, independent of the sending pipeline. Large lists are
/// read by following an opaque pagination pointer; <see cref="ListAsync"/> does this transparently while
/// <see cref="ListPageAsync(int?, System.Threading.CancellationToken)"/> exposes a single page for
/// callers that drive paging themselves.
/// </summary>
/// <typeparam name="TEntry">The entry type for this list (<see cref="Bounce"/>, <see cref="Unsubscribe"/>, or <see cref="Complaint"/>).</typeparam>
public interface ISuppressionList<TEntry>
{
    /// <summary>
    /// Lists every entry, transparently following pagination across pages. This is the ergonomic default:
    /// the returned sequence streams entries one page at a time and stops at the end of the list.
    /// </summary>
    /// <param name="pageSize">An optional page size applied to the first request only; when omitted the service default applies.</param>
    /// <param name="cancellationToken">A token that stops enumeration; honored between page fetches.</param>
    /// <returns>An asynchronous sequence of every entry on the list, in service order.</returns>
    /// <exception cref="MailgunnerException">A page request returned a non-success response.</exception>
    System.Collections.Generic.IAsyncEnumerable<TEntry> ListAsync(
        int? pageSize = null,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the first page of the list. Use <see cref="SuppressionPage{TEntry}.NextCursor"/> with
    /// <see cref="ListPageAsync(string, System.Threading.CancellationToken)"/> to follow pagination manually.
    /// </summary>
    /// <param name="pageSize">An optional page size for this request; when omitted the service default applies.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The first page of entries plus an opaque next-page pointer.</returns>
    /// <exception cref="MailgunnerException">The service returned a non-success response.</exception>
    System.Threading.Tasks.Task<SuppressionPage<TEntry>> ListPageAsync(
        int? pageSize = null,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the page identified by an opaque cursor previously returned as
    /// <see cref="SuppressionPage{TEntry}.NextCursor"/>. The cursor is followed verbatim.
    /// </summary>
    /// <param name="cursor">The opaque next-page pointer from a prior page.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The next page of entries plus an opaque next-page pointer.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="cursor"/> is null or blank.</exception>
    /// <exception cref="MailgunnerException">The service returned a non-success response.</exception>
    System.Threading.Tasks.Task<SuppressionPage<TEntry>> ListPageAsync(
        string cursor,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a single entry by address.
    /// </summary>
    /// <param name="address">The address to look up.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The typed entry for the address.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="address"/> is null or blank.</exception>
    /// <exception cref="MailgunnerException">The address is not on the list (not-found) or the service returned any other non-success response.</exception>
    System.Threading.Tasks.Task<TEntry> GetAsync(
        string address,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an entry to the list. The address plus any list-appropriate optional fields are sent as JSON;
    /// any server-populated fields on <paramref name="entry"/> (such as <c>CreatedAt</c>) are ignored.
    /// </summary>
    /// <param name="entry">The entry to add; its address is required (non-blank).</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A task that completes when the service accepts the add.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException">The entry's address is null or blank.</exception>
    /// <exception cref="MailgunnerException">The service returned a non-success response.</exception>
    System.Threading.Tasks.Task AddAsync(
        TEntry entry,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a single address from the list.
    /// </summary>
    /// <param name="address">The address to remove.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A task that completes when the service accepts the removal.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="address"/> is null or blank.</exception>
    /// <exception cref="MailgunnerException">The address is not on the list (not-found) or the service returned any other non-success response.</exception>
    System.Threading.Tasks.Task RemoveAsync(
        string address,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the entire list (deletes all entries) in a single call.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A task that completes when the service accepts the clear.</returns>
    /// <exception cref="MailgunnerException">The service returned a non-success response.</exception>
    System.Threading.Tasks.Task ClearAsync(
        System.Threading.CancellationToken cancellationToken = default);
}
