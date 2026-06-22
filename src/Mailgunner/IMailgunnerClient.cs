namespace Mailgunner;

/// <summary>
/// The Mailgunner client resolved from the dependency-injection container. This is the entry
/// point that every other Mailgunner capability builds on; operational members (sending,
/// suppressions, webhooks) are introduced by later features.
/// </summary>
public interface IMailgunnerClient
{
    /// <summary>
    /// Sends a single email.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">A token that cancels the send.</param>
    /// <returns>
    /// A <see cref="SendResult"/> exposing Mailgun's message id and status message when the
    /// service accepts the message.
    /// </returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException">
    /// The message is missing a sender, has no recipient across to/cc/bcc, or has no text or HTML
    /// body. Thrown before any request is issued.
    /// </exception>
    /// <exception cref="MailgunnerException">
    /// The service returned a non-success response, or a success response whose body could not be
    /// parsed into a result. Exposes the HTTP status code and the raw response body.
    /// </exception>
    System.Threading.Tasks.Task<SendResult> SendAsync(
        MailgunMessage message,
        System.Threading.CancellationToken cancellationToken = default);
}
