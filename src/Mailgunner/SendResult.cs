namespace Mailgunner;

/// <summary>
/// The success outcome of a send: Mailgun's message id and accompanying status message. Returned
/// only when a success (2xx) response body parses into both an <c>id</c> and a <c>message</c>.
/// </summary>
public sealed class SendResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SendResult"/> class.
    /// </summary>
    /// <param name="id">Mailgun's message id.</param>
    /// <param name="message">Mailgun's accompanying status message.</param>
    public SendResult(string id, string message)
    {
        Id = id;
        Message = message;
    }

    /// <summary>
    /// Gets Mailgun's message id.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets Mailgun's accompanying status message (for example, a queued acknowledgment).
    /// </summary>
    public string Message { get; }
}
