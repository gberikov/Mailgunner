namespace Mailgunner;

/// <summary>
/// An email to send: a sender, recipients across to/cc/bcc, an optional subject, and a text
/// and/or HTML body. At least one recipient (across <see cref="To"/>, <see cref="Cc"/>, and
/// <see cref="Bcc"/>) and at least one of <see cref="Text"/> or <see cref="Html"/> are required;
/// these are validated when the message is sent.
/// </summary>
public sealed class MailgunMessage
{
    /// <summary>
    /// Gets or sets the sender. Required.
    /// </summary>
    public EmailAddress From { get; set; }

    /// <summary>
    /// Gets the primary recipients. At least one recipient across <see cref="To"/>,
    /// <see cref="Cc"/>, and <see cref="Bcc"/> is required.
    /// </summary>
    public System.Collections.Generic.IList<EmailAddress> To { get; } = new System.Collections.Generic.List<EmailAddress>();

    /// <summary>
    /// Gets the carbon-copy recipients.
    /// </summary>
    public System.Collections.Generic.IList<EmailAddress> Cc { get; } = new System.Collections.Generic.List<EmailAddress>();

    /// <summary>
    /// Gets the blind-carbon-copy recipients.
    /// </summary>
    public System.Collections.Generic.IList<EmailAddress> Bcc { get; } = new System.Collections.Generic.List<EmailAddress>();

    /// <summary>
    /// Gets or sets the optional subject.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Gets or sets the plain-text body part. Optional when <see cref="Html"/> is set.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets the HTML body part. Optional when <see cref="Text"/> is set.
    /// </summary>
    public string? Html { get; set; }
}
