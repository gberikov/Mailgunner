namespace Mailgunner;

/// <summary>
/// An opt-in declaration of how recipients can unsubscribe from a send, emitted as RFC 8058 / RFC 2369
/// <c>List-Unsubscribe</c> (and, when one-click, <c>List-Unsubscribe-Post</c>) message headers. Attach it
/// via <see cref="MailgunSendOptions.ListUnsubscribe"/>. At least one of <see cref="Url"/> or
/// <see cref="MailtoAddress"/> must be present; supplying neither, a non-<c>https</c> <see cref="Url"/>, a
/// control character / line break in the <see cref="Url"/>, or <see cref="OneClick"/> without an
/// <c>https</c> <see cref="Url"/> is rejected with an <see cref="System.ArgumentException"/> when the
/// request is built (before any network call).
/// </summary>
public sealed class ListUnsubscribeOptions
{
    /// <summary>
    /// Gets or sets the <c>https</c> unsubscribe endpoint. Required when <see cref="OneClick"/> is
    /// <see langword="true"/>. When present it must be an absolute <c>https</c> URI free of control
    /// characters and line breaks; it is emitted verbatim inside angle brackets in the
    /// <c>List-Unsubscribe</c> header. A null, empty, or whitespace-only value is treated as "no URL".
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the unsubscribe email address. Supplied as a bare address; the library forms the
    /// <c>mailto:</c> URI itself and emits it inside angle brackets. Only the
    /// <see cref="EmailAddress.Address"/> component is used (any display name is ignored). The address is
    /// validated by the <see cref="EmailAddress"/> rules (non-blank, no control characters) at assignment.
    /// </summary>
    public EmailAddress? MailtoAddress { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether one-click unsubscribe is requested. When
    /// <see langword="true"/>, the send additionally emits
    /// <c>List-Unsubscribe-Post: List-Unsubscribe=One-Click</c> and requires a valid <c>https</c>
    /// <see cref="Url"/>. Defaults to <see langword="false"/>.
    /// </summary>
    public bool OneClick { get; set; }
}
