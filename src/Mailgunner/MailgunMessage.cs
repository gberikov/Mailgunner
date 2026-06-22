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
    public System.Collections.Generic.IDictionary<string, object?> TemplateVariables { get; }
        = new System.Collections.Generic.Dictionary<string, object?>();
}
