namespace Mailgunner;

/// <summary>
/// One personalized mass send: a stored-template or inline-body message plus an ordered recipient list
/// where each recipient carries its own variables. <see cref="IMailgunnerClient.SendBatchAsync"/>
/// automatically splits the <see cref="Recipients"/> into consecutive chunks of at most 1000 and issues
/// one <c>multipart/form-data</c> request per chunk, reusing the same <see cref="Template"/> (or
/// <see cref="Text"/>/<see cref="Html"/>) and <see cref="TemplateVariables"/> on every request. Exactly
/// one of <see cref="Template"/> or an inline body (<see cref="Text"/>/<see cref="Html"/>) is required.
/// </summary>
public sealed class MailgunBatchMessage
{
    /// <summary>
    /// Gets or sets the sender. Required for inline messages; omit it to use the stored template's From
    /// header. Mailgun rejects a templated send if neither the batch nor the template supplies a sender.
    /// </summary>
    public EmailAddress From { get; set; }

    /// <summary>
    /// Gets or sets the optional subject. Emitted as <c>subject</c> when non-null.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Gets or sets the plain-text body for an inline (non-template) batch. Use <c>%recipient.var%</c>
    /// placeholders that Mailgun fills from each recipient's <see cref="BatchRecipient.Variables"/>.
    /// Mutually exclusive with <see cref="Template"/>.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>Gets or sets the HTML body for an inline (non-template) batch; see <see cref="Text"/>.</summary>
    public string? Html { get; set; }

    /// <summary>
    /// Gets or sets the name of the server-side stored template to render. Required unless
    /// <see cref="Text"/> or <see cref="Html"/> is set; emitted as <c>template</c> on every chunk.
    /// </summary>
    public string? Template { get; set; }

    /// <summary>
    /// Gets or sets the optional stored-template version to pin. When omitted (null or blank), the
    /// template's active version is used. Sent as the <c>t:version</c> field, identical on every chunk.
    /// </summary>
    public string? TemplateVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the service should generate a plain-text part from the
    /// template. When <see langword="true"/>, the <c>t:text</c> field is sent as <c>yes</c> on every
    /// chunk; when <see langword="false"/>, the field is omitted entirely.
    /// </summary>
    public bool GenerateTextFromTemplate { get; set; }

    /// <summary>
    /// Gets the global template variables shared by every recipient in the batch. The map is
    /// serialized once into a single JSON object sent in the <c>t:variables</c> field, identical on
    /// every chunk; the field is omitted when the map is empty. Per-recipient values belong on
    /// <see cref="BatchRecipient.Variables"/> instead.
    /// </summary>
    public IDictionary<string, object?> TemplateVariables { get; }
        = new Dictionary<string, object?>();

    /// <summary>
    /// Gets the ordered recipient list. Each entry pairs an address with that recipient's own
    /// variables and appears in exactly one chunk; the supplied order is preserved across chunk
    /// boundaries. An empty list is a valid no-op (zero requests). Duplicate addresses are rejected.
    /// </summary>
    public IList<BatchRecipient> Recipients { get; }
        = new List<BatchRecipient>();

    /// <summary>
    /// Gets or sets the optional send enrichments (tags, test mode, tracking toggles, scheduled delivery
    /// time, custom headers, and custom variables) applied to the batch. Empty by default; every member
    /// is optional and is repeated identically on every chunk. Never set to null; a null value is
    /// rejected when the request is built.
    /// </summary>
    public MailgunSendOptions Options { get; set; } = new MailgunSendOptions();

    /// <summary>
    /// Gets or sets the optional reply-to address, emitted as the <c>Reply-To</c> header
    /// (<c>h:Reply-To</c>) on every chunk. Setting it and also supplying a <c>Reply-To</c> entry in
    /// <see cref="MailgunSendOptions.CustomHeaders"/> (matched case-insensitively) throws
    /// <see cref="ArgumentException"/> when the request is built.
    /// </summary>
    public EmailAddress? ReplyTo { get; set; }

    /// <summary>
    /// Gets the file attachments delivered alongside every chunk's message. Each is emitted as a
    /// downloadable <c>attachment</c> file part carrying its file name and content type.
    /// </summary>
    public IList<MailgunFile> Attachments { get; }
        = new List<MailgunFile>();

    /// <summary>
    /// Gets the inline (embedded) files included on every chunk. Each is emitted as an <c>inline</c>
    /// file part — distinct from <see cref="Attachments"/> — referenceable from the HTML body by content id.
    /// </summary>
    public IList<MailgunFile> InlineFiles { get; }
        = new List<MailgunFile>();
}
