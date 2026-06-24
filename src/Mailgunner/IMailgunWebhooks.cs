namespace Mailgunner;

/// <summary>
/// Domain webhook management for the configured domain — list, read, create, update, and delete the
/// callback URLs Mailgun invokes for each delivery event. Reached via
/// <see cref="IMailgunnerClient.Webhooks"/>. A webhook is keyed by exactly one <see cref="WebhookEventType"/>
/// and carries one or more callback URLs. These operations target Mailgun's v3 webhook surface (JSON
/// responses; create/update send form-encoded fields) and are independent of the sending pipeline and of
/// webhook signature verification (<see cref="MailgunWebhookSignature"/>).
/// </summary>
public interface IMailgunWebhooks
{
    /// <summary>
    /// Lists every event type that has a webhook registration for the domain.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>One registration per registered event type; an empty list when none are configured.</returns>
    /// <exception cref="MailgunnerException">The service returned a non-success response.</exception>
    System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<WebhookRegistration>> ListAsync(
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the registration for one event type.
    /// </summary>
    /// <param name="eventType">The event type to read.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The registration (its callback URL(s)) for the event type.</returns>
    /// <exception cref="MailgunnerException">The event type has no registration (not-found) or any other non-success response.</exception>
    System.Threading.Tasks.Task<WebhookRegistration> GetAsync(
        WebhookEventType eventType,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a registration for one event type with one or more callback URLs (Mailgun allows up to
    /// three).
    /// </summary>
    /// <param name="eventType">The event type to register.</param>
    /// <param name="urls">The callback URL(s); at least one non-blank URL is required.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The created registration.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="urls"/> is null/empty or every entry is blank.</exception>
    /// <exception cref="MailgunnerException">The service returned a non-success response.</exception>
    System.Threading.Tasks.Task<WebhookRegistration> CreateAsync(
        WebhookEventType eventType,
        System.Collections.Generic.IEnumerable<string> urls,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a single callback URL across several event types in one call. Because each create targets
    /// one event type, this fans out to one create per event type, issued sequentially in the supplied
    /// order. The behavior on partial failure is fail-fast with no rollback: the first non-success response
    /// throws and stops the remaining creates, leaving any already-created registrations in place.
    /// </summary>
    /// <param name="eventTypes">The event types to register the URL for; must be non-empty.</param>
    /// <param name="url">The single callback URL to register for each event type; must be non-blank.</param>
    /// <param name="cancellationToken">A token that cancels the operation; honored between creates.</param>
    /// <returns>One registration per event type, in order, on full success.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="eventTypes"/> is null/empty or <paramref name="url"/> is blank.</exception>
    /// <exception cref="MailgunnerException">The first non-success response; earlier creates are not rolled back.</exception>
    System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<WebhookRegistration>> CreateAsync(
        System.Collections.Generic.IEnumerable<WebhookEventType> eventTypes,
        string url,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the callback URL(s) for an event type's registration with the supplied URL(s).
    /// </summary>
    /// <param name="eventType">The event type whose registration is updated.</param>
    /// <param name="urls">The new callback URL(s); at least one non-blank URL is required.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The updated registration.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="urls"/> is null/empty or every entry is blank.</exception>
    /// <exception cref="MailgunnerException">The event type has no registration (not-found) or any other non-success response.</exception>
    System.Threading.Tasks.Task<WebhookRegistration> UpdateAsync(
        WebhookEventType eventType,
        System.Collections.Generic.IEnumerable<string> urls,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an event type's registration.
    /// </summary>
    /// <param name="eventType">The event type whose registration is deleted.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A task that completes when the service accepts the deletion.</returns>
    /// <exception cref="MailgunnerException">The event type has no registration (not-found) or any other non-success response.</exception>
    System.Threading.Tasks.Task DeleteAsync(
        WebhookEventType eventType,
        System.Threading.CancellationToken cancellationToken = default);
}
