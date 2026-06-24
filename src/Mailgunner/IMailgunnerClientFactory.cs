namespace Mailgunner;

/// <summary>
/// Resolves a registered, named <see cref="IMailgunnerClient"/> at runtime. Obtain it from the
/// dependency-injection container when more than one Mailgunner client is registered (each under a
/// distinct name via an <c>AddMailgunner(name, ...)</c> overload). This factory is the single
/// supported way to resolve a named client; each call returns a client bound to that name's own
/// domain, region, authentication, and retry settings.
/// </summary>
public interface IMailgunnerClientFactory
{
    /// <summary>
    /// Gets the fully configured client registered under <paramref name="name"/>.
    /// </summary>
    /// <param name="name">
    /// The registration name. Compared case-sensitively (ordinal) against the names supplied at
    /// registration.
    /// </param>
    /// <returns>
    /// A ready <see cref="IMailgunnerClient"/> (sending and suppressions) bound to that name's
    /// configuration. Never <see langword="null"/>.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="name"/> is <see langword="null"/>, empty, or whitespace-only; or no client is
    /// registered under <paramref name="name"/>. This is a standard configuration/lookup error, never
    /// a <see cref="MailgunnerException"/> (which is reserved for HTTP API responses).
    /// </exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "'Get(name)' mirrors the established BCL keyed-lookup shape (IOptionsMonitor<T>.Get(string)) and is the documented public contract; renaming would hurt discoverability for the common case.")]
    IMailgunnerClient Get(string name);
}
