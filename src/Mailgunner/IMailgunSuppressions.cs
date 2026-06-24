namespace Mailgunner;

/// <summary>
/// Access to a domain's three Mailgun suppression lists — bounces, unsubscribes, and complaints. Reached
/// via <see cref="IMailgunnerClient.Suppressions"/>. Each list exposes the same operations (list, get,
/// add, remove, clear) over its own typed entry. These are JSON endpoints, independent of the sending
/// pipeline.
/// </summary>
public interface IMailgunSuppressions
{
    /// <summary>
    /// Gets the operations on the domain's <c>bounces</c> list.
    /// </summary>
    ISuppressionList<Bounce> Bounces { get; }

    /// <summary>
    /// Gets the operations on the domain's <c>unsubscribes</c> list.
    /// </summary>
    ISuppressionList<Unsubscribe> Unsubscribes { get; }

    /// <summary>
    /// Gets the operations on the domain's <c>complaints</c> list.
    /// </summary>
    ISuppressionList<Complaint> Complaints { get; }
}
