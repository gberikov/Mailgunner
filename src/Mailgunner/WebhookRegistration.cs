namespace Mailgunner;

/// <summary>
/// A domain webhook registration: a single <see cref="WebhookEventType"/> associated with the callback
/// URL(s) Mailgun invokes when that event occurs for the domain. Returned by reading one registration and
/// by updating one, and the per-event-type element of a list. The library builds instances from parsed
/// responses; the constructor is public so a hand-written test double of <see cref="IMailgunWebhooks"/>
/// can build them too.
/// </summary>
public sealed record WebhookRegistration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookRegistration"/> class.
    /// </summary>
    /// <param name="eventType">The event type this registration is keyed by.</param>
    /// <param name="urls">The callback URL(s) for this event type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="urls"/> is <see langword="null"/>.</exception>
    public WebhookRegistration(
        WebhookEventType eventType, IReadOnlyList<string> urls)
    {
        Internal.Guard.NotNull(urls, nameof(urls));
        EventType = eventType;
        Urls = urls;
    }

    /// <summary>
    /// Gets the event type this registration is keyed by.
    /// </summary>
    public WebhookEventType EventType { get; }

    /// <summary>
    /// Gets the callback URL(s) Mailgun invokes for this event type (Mailgun allows up to three). Never
    /// null; never empty for a registration returned by the service.
    /// </summary>
    public IReadOnlyList<string> Urls { get; }
}
