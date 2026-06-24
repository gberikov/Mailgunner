namespace Mailgunner;

/// <summary>
/// An email address with an optional display name. Used for the sender and every recipient of a
/// <see cref="MailgunMessage"/>. A bare address string converts implicitly to an
/// <see cref="EmailAddress"/>.
/// </summary>
public readonly struct EmailAddress : System.IEquatable<EmailAddress>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmailAddress"/> struct.
    /// </summary>
    /// <param name="address">The email address. Required, non-empty.</param>
    /// <param name="displayName">The optional display name.</param>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="address"/> is null, empty, or whitespace, or either argument contains a control
    /// character (for example a carriage return or line feed, which could otherwise inject headers).
    /// </exception>
    public EmailAddress(string address, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new System.ArgumentException("An email address is required.", nameof(address));
        }

        if (ContainsControlCharacter(address))
        {
            throw new System.ArgumentException(
                "An email address must not contain control characters.", nameof(address));
        }

        if (displayName is not null && ContainsControlCharacter(displayName))
        {
            throw new System.ArgumentException(
                "A display name must not contain control characters.", nameof(displayName));
        }

        Address = address;
        DisplayName = displayName;
    }

    /// <summary>
    /// Gets the email address.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Gets the optional display name.
    /// </summary>
    public string? DisplayName { get; }

    /// <summary>
    /// Converts a bare address string into an <see cref="EmailAddress"/>.
    /// </summary>
    /// <param name="address">The email address.</param>
    public static implicit operator EmailAddress(string address) => new(address);

    /// <summary>
    /// Determines whether two addresses are equal by value.
    /// </summary>
    /// <param name="left">The first address.</param>
    /// <param name="right">The second address.</param>
    /// <returns><see langword="true"/> when the addresses are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(EmailAddress left, EmailAddress right) => left.Equals(right);

    /// <summary>
    /// Determines whether two addresses differ by value.
    /// </summary>
    /// <param name="left">The first address.</param>
    /// <param name="right">The second address.</param>
    /// <returns><see langword="true"/> when the addresses differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(EmailAddress left, EmailAddress right) => !left.Equals(right);

    /// <summary>
    /// Creates an <see cref="EmailAddress"/> from a bare address string. Equivalent to the implicit
    /// conversion; provided as a named alternative for languages that do not support operators.
    /// </summary>
    /// <param name="address">The email address.</param>
    /// <returns>The created <see cref="EmailAddress"/>.</returns>
    public static EmailAddress FromString(string address) => new(address);

    /// <summary>
    /// Formats the wire value: <c>Display Name &lt;address&gt;</c> when a display name is set,
    /// otherwise just the address. A display name containing RFC 5322 special characters is emitted
    /// as a quoted string (with embedded <c>"</c> and <c>\</c> escaped) so it cannot break address-list
    /// parsing.
    /// </summary>
    /// <returns>The formatted address.</returns>
    public override string ToString() =>
        string.IsNullOrEmpty(DisplayName) ? Address : $"{FormatDisplayName(DisplayName!)} <{Address}>";

    private static string FormatDisplayName(string displayName)
    {
        if (!NeedsQuoting(displayName))
        {
            return displayName;
        }

        // The two-argument string overload replaces ordinally and is available on every target
        // framework (the StringComparison overload is missing on netstandard2.0).
        var escaped = displayName
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
        return "\"" + escaped + "\"";
    }

    private static bool NeedsQuoting(string displayName)
    {
        foreach (var c in displayName)
        {
            switch (c)
            {
                case '(':
                case ')':
                case '<':
                case '>':
                case '[':
                case ']':
                case ':':
                case ';':
                case '@':
                case '\\':
                case ',':
                case '"':
                    return true;
                default:
                    break;
            }
        }

        return false;
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether this address equals another by value.
    /// </summary>
    /// <param name="other">The address to compare with.</param>
    /// <returns><see langword="true"/> when the addresses are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(EmailAddress other) =>
        string.Equals(Address, other.Address, System.StringComparison.Ordinal)
        && string.Equals(DisplayName, other.DisplayName, System.StringComparison.Ordinal);

    /// <summary>
    /// Determines whether this address equals another object by value.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal <see cref="EmailAddress"/>; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj) => obj is EmailAddress other && Equals(other);

    /// <summary>
    /// Returns a hash code consistent with value equality.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + (Address is null ? 0 : System.StringComparer.Ordinal.GetHashCode(Address));
            hash = (hash * 31) + (DisplayName is null ? 0 : System.StringComparer.Ordinal.GetHashCode(DisplayName));
            return hash;
        }
    }
}
