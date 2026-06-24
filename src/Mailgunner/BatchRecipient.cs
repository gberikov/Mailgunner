namespace Mailgunner;

/// <summary>
/// One recipient of a <see cref="MailgunBatchMessage"/> together with that recipient's own
/// personalization values. The bare <see cref="EmailAddress.Address"/> becomes this recipient's key
/// in the per-chunk <c>recipient-variables</c> JSON object; <see cref="EmailAddress.ToString"/> is its
/// repeated <c>to</c> value. A bare address string converts implicitly to an
/// <see cref="EmailAddress"/>, so callers may write <c>new BatchRecipient("alice@example.com")</c>.
/// </summary>
public sealed class BatchRecipient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BatchRecipient"/> class.
    /// </summary>
    /// <param name="address">The recipient address. Required (non-empty, enforced by <see cref="EmailAddress"/>).</param>
    public BatchRecipient(EmailAddress address) => Address = address;

    /// <summary>
    /// Gets the recipient address. The bare <see cref="EmailAddress.Address"/> is this recipient's
    /// <c>recipient-variables</c> key; <see cref="EmailAddress.ToString"/> is its <c>to</c> value.
    /// </summary>
    public EmailAddress Address { get; }

    /// <summary>
    /// Gets this recipient's own named values (for example <c>name</c>, <c>ticket</c>, <c>link</c>).
    /// They become this recipient's entry in the request's <c>recipient-variables</c> object and are
    /// independent of the global <see cref="MailgunBatchMessage.TemplateVariables"/>. An empty map is
    /// valid and serializes to <c>{}</c>.
    /// </summary>
    public System.Collections.Generic.IDictionary<string, object?> Variables { get; }
        = new System.Collections.Generic.Dictionary<string, object?>();
}
