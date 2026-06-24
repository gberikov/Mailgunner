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

    /// <summary>
    /// Sends one personalized stored-template message to a large recipient list, automatically
    /// splitting it into the fewest possible <c>multipart/form-data</c> requests (chunks of at most
    /// 1000 recipients, <c>ceil(N / 1000)</c> requests). Each request reuses the same template and
    /// global variables and carries a <c>recipient-variables</c> object keyed by recipient address, so
    /// Mailgun delivers an individual message to each recipient. Chunks are issued sequentially in
    /// recipient order and the operation is fail-fast: the first non-success response stops the batch.
    /// </summary>
    /// <param name="message">The batch to send, including the template, optional global variables, and the ordered recipient list.</param>
    /// <param name="cancellationToken">A token that cancels the batch; honored between and during chunks, after which no further chunks are issued.</param>
    /// <returns>
    /// One <see cref="SendResult"/> per chunk actually sent, in chunk order. An empty
    /// <see cref="MailgunBatchMessage.Recipients"/> list is a no-op that returns an empty list.
    /// </returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException">
    /// The batch is missing a sender, is missing a template, or contains a duplicate recipient address.
    /// Thrown before any request is issued.
    /// </exception>
    /// <exception cref="MailgunnerException">
    /// A request returned a non-success response, or a success response whose body could not be parsed
    /// into a result. Exposes the HTTP status code and the raw response body; chunks already accepted
    /// have been sent and are not rolled back.
    /// </exception>
    System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<SendResult>> SendBatchAsync(
        MailgunBatchMessage message,
        System.Threading.CancellationToken cancellationToken = default);
}
