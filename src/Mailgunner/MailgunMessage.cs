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
    public IList<EmailAddress> To { get; } = new List<EmailAddress>();

    /// <summary>
    /// Gets the carbon-copy recipients.
    /// </summary>
    public IList<EmailAddress> Cc { get; } = new List<EmailAddress>();

    /// <summary>
    /// Gets the blind-carbon-copy recipients.
    /// </summary>
    public IList<EmailAddress> Bcc { get; } = new List<EmailAddress>();

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

    /// <summary>
    /// Gets or sets the name of a server-side stored template to render. When set, the message is
    /// sent as a templated message and an inline body (<see cref="Text"/>/<see cref="Html"/>) must
    /// not also be supplied. A template name satisfies the body requirement on its own.
    /// </summary>
    public string? Template { get; set; }

    /// <summary>
    /// Gets or sets the optional stored-template version to pin. When omitted (null or blank), the
    /// template's active version is used. Sent as the <c>t:version</c> field when present.
    /// </summary>
    public string? TemplateVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the service should generate a plain-text part from
    /// the template. When <see langword="true"/>, the <c>t:text</c> field is sent as <c>yes</c>;
    /// when <see langword="false"/>, the field is omitted entirely.
    /// </summary>
    public bool GenerateTextFromTemplate { get; set; }

    /// <summary>
    /// Gets the global template variables applied to the whole send (not per recipient). The map is
    /// serialized once into a single JSON object sent in the <c>t:variables</c> field; values may be
    /// any JSON-representable type. The field is omitted when the map is empty.
    /// </summary>
    public IDictionary<string, object?> TemplateVariables { get; }
        = new Dictionary<string, object?>();

    /// <summary>
    /// Gets or sets the optional send enrichments (tags, test mode, tracking toggles, scheduled delivery
    /// time, custom headers, and custom variables) applied to this send. Empty by default; every member
    /// is optional. Never set to null; a null value is rejected when the request is built.
    /// </summary>
    public MailgunSendOptions Options { get; set; } = new MailgunSendOptions();

    /// <summary>
    /// Gets or sets the optional reply-to address, emitted as the <c>Reply-To</c> header
    /// (<c>h:Reply-To</c>). Setting it and also supplying a <c>Reply-To</c> entry in
    /// <see cref="MailgunSendOptions.CustomHeaders"/> (matched case-insensitively) throws
    /// <see cref="ArgumentException"/> when the request is built.
    /// </summary>
    public EmailAddress? ReplyTo { get; set; }

    /// <summary>
    /// Gets the file attachments delivered alongside the message. Each is emitted as a downloadable
    /// <c>attachment</c> file part carrying its file name and content type.
    /// </summary>
    public IList<MailgunFile> Attachments { get; }
        = new List<MailgunFile>();

    /// <summary>
    /// Gets the inline (embedded) files. Each is emitted as an <c>inline</c> file part — distinct from
    /// <see cref="Attachments"/> — so it can be referenced from the HTML body by its content id.
    /// </summary>
    public IList<MailgunFile> InlineFiles { get; }
        = new List<MailgunFile>();

    /// <summary>Gets or sets the optional AMP-HTML body part, emitted as <c>amp-html</c>. Requires <see cref="Html"/> or <see cref="Text"/> as well.</summary>
    public string? AmpHtml { get; set; }
}
