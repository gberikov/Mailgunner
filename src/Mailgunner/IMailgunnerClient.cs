namespace Mailgunner;

/// <summary>
/// The Mailgunner client resolved from the dependency-injection container. It is the entry point
/// for sending single and batch messages and for working with the domain's suppression lists.
/// Webhook signature verification is a standalone, network-free primitive
/// (<see cref="MailgunWebhookSignature"/>), not a member of this client.
/// </summary>
/// <remarks>
/// A response from the service, success or failure, is always mapped to a result or a
/// <see cref="MailgunnerException"/>. A failure that yields <em>no</em> response is not: after the retry
/// budget (see <see cref="RetryPolicyOptions"/>) it surfaces as the underlying transport exception, an
/// <see cref="HttpRequestException"/> (connection refused/reset, DNS failure), a
/// <see cref="TimeoutException"/> (a single attempt exceeded <see cref="RetryPolicyOptions.AttemptTimeout"/>),
/// or a <see cref="TaskCanceledException"/> (the overall worst-case <c>HttpClient.Timeout</c> elapsed).
/// The same applies to every operation under <see cref="Suppressions"/> and <see cref="Webhooks"/>.
/// </remarks>
public interface IMailgunnerClient
{
    /// <summary>
    /// Gets access to the domain's suppression lists (bounces, unsubscribes, complaints): listing with
    /// pagination, fetching, adding, removing, and clearing entries. These are JSON endpoints,
    /// independent of the sending methods below.
    /// </summary>
    IMailgunSuppressions Suppressions { get; }

    /// <summary>
    /// Gets access to the domain's webhook registrations: listing, reading, creating, updating, and
    /// deleting the callback URLs Mailgun invokes for each delivery event. These target Mailgun's v3
    /// webhook surface (JSON responses; create/update send form-encoded fields) and are independent of
    /// the sending methods below and of signature verification.
    /// </summary>
    IMailgunWebhooks Webhooks { get; }

    /// <summary>
    /// Sends a single email.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">A token that cancels the send.</param>
    /// <returns>
    /// A <see cref="SendResult"/> exposing Mailgun's message id and status message when the
    /// service accepts the message.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The message is missing a sender, has no recipient across to/cc/bcc, or has no text or HTML
    /// body. Thrown before any request is issued.
    /// </exception>
    /// <exception cref="MailgunnerException">
    /// The service returned a non-success response, or a success response whose body could not be
    /// parsed into a result. Exposes the HTTP status code and the raw response body.
    /// </exception>
    /// <exception cref="HttpRequestException">No response was obtained because of a transport fault (see the type remarks).</exception>
    /// <exception cref="TimeoutException">No response was obtained within <see cref="RetryPolicyOptions.AttemptTimeout"/> (see the type remarks).</exception>
    Task<SendResult> SendAsync(
        MailgunMessage message,
        CancellationToken cancellationToken = default);

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
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The batch is missing a sender, is missing a template, or contains a duplicate recipient address.
    /// Thrown before any request is issued.
    /// </exception>
    /// <exception cref="MailgunnerException">
    /// A request returned a non-success response, or a success response whose body could not be parsed
    /// into a result. Exposes the HTTP status code and the raw response body; chunks already accepted
    /// have been sent and are not rolled back. <see cref="MailgunnerException.FailedChunkIndex"/> and
    /// <see cref="MailgunnerException.AcceptedResults"/> show which chunks were already accepted.
    /// </exception>
    /// <exception cref="HttpRequestException">A chunk obtained no response because of a transport fault (see the type remarks); earlier chunks are not rolled back.</exception>
    /// <exception cref="TimeoutException">A chunk obtained no response within <see cref="RetryPolicyOptions.AttemptTimeout"/> (see the type remarks); earlier chunks are not rolled back.</exception>
    Task<IReadOnlyList<SendResult>> SendBatchAsync(
        MailgunBatchMessage message,
        CancellationToken cancellationToken = default);
}
