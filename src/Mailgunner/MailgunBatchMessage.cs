namespace Mailgunner;

/// <summary>
/// One personalized mass send: a stored-template message plus an ordered recipient list where each
/// recipient carries its own variables. <see cref="IMailgunnerClient.SendBatchAsync"/> automatically
/// splits the <see cref="Recipients"/> into consecutive chunks of at most 1000 and issues one
/// <c>multipart/form-data</c> request per chunk, reusing the same <see cref="Template"/> and
/// <see cref="TemplateVariables"/> on every request. A <see cref="Template"/> is required.
/// </summary>
public sealed class MailgunBatchMessage
{
    /// <summary>
    /// Gets or sets the sender. Required.
    /// </summary>
    public EmailAddress From { get; set; }

    /// <summary>
    /// Gets or sets the optional subject. Emitted as <c>subject</c> when non-null.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Gets or sets the name of the server-side stored template to render. Required (non-blank);
    /// emitted as <c>template</c> on every chunk.
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
    public System.Collections.Generic.IDictionary<string, object?> TemplateVariables { get; }
        = new System.Collections.Generic.Dictionary<string, object?>();

    /// <summary>
    /// Gets the ordered recipient list. Each entry pairs an address with that recipient's own
    /// variables and appears in exactly one chunk; the supplied order is preserved across chunk
    /// boundaries. An empty list is a valid no-op (zero requests). Duplicate addresses are rejected.
    /// </summary>
    public System.Collections.Generic.IList<BatchRecipient> Recipients { get; }
        = new System.Collections.Generic.List<BatchRecipient>();
}
