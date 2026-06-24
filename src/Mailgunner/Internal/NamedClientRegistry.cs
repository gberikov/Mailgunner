namespace Mailgunner.Internal;

/// <summary>
/// The single source of truth for which named Mailgunner clients have been registered in a
/// container. Populated at registration time (so duplicate and blank names are caught early) and
/// consulted by <see cref="MailgunnerClientFactory"/> at resolution time (so an unknown name fails
/// clearly instead of silently yielding a default <see cref="System.Net.Http.HttpClient"/>). Names
/// are compared with <see cref="System.StringComparer.Ordinal"/> (case-sensitive).
/// </summary>
internal sealed class NamedClientRegistry
{
    /// <summary>The prefix applied to a logical client name to form its typed-<c>HttpClient</c> name.</summary>
    public const string HttpClientNamePrefix = "Mailgunner:";

    private readonly System.Collections.Generic.HashSet<string> _names =
        new(System.StringComparer.Ordinal);

    /// <summary>
    /// Records <paramref name="name"/> as registered.
    /// </summary>
    /// <param name="name">The client name to add.</param>
    /// <returns><see langword="true"/> if newly added; <see langword="false"/> if already present.</returns>
    public bool Add(string name) => _names.Add(name);

    /// <summary>Returns whether a client is registered under <paramref name="name"/>.</summary>
    /// <param name="name">The client name to look up.</param>
    /// <returns><see langword="true"/> if registered; otherwise <see langword="false"/>.</returns>
    public bool Contains(string name) => _names.Contains(name);

    /// <summary>
    /// Gets an ordinal-sorted snapshot of the registered names, used to enrich the unknown-name error
    /// message. Never contains any secret material.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<string> RegisteredNames
    {
        get
        {
            var snapshot = new System.Collections.Generic.List<string>(_names);
            snapshot.Sort(System.StringComparer.Ordinal);
            return snapshot;
        }
    }

    /// <summary>Maps a logical client name to the name of its underlying typed <c>HttpClient</c>.</summary>
    /// <param name="name">The logical client name.</param>
    /// <returns>The typed-<c>HttpClient</c> name (the prefix plus <paramref name="name"/>).</returns>
    public static string HttpClientName(string name) => HttpClientNamePrefix + name;
}
