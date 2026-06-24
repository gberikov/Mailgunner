namespace Mailgunner;

/// <summary>
/// A domain webhook registration: a single <see cref="WebhookEventType"/> associated with the callback
/// URL(s) Mailgun invokes when that event occurs for the domain. Returned by reading one registration and
/// by updating one, and the per-event-type element of a list. Constructed by the library from a parsed
/// response; consumers do not create instances.
/// </summary>
public sealed record WebhookRegistration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookRegistration"/> class.
    /// </summary>
    /// <param name="eventType">The event type this registration is keyed by.</param>
    /// <param name="urls">The callback URL(s) for this event type; never null.</param>
    internal WebhookRegistration(
        WebhookEventType eventType, System.Collections.Generic.IReadOnlyList<string> urls)
    {
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
    public System.Collections.Generic.IReadOnlyList<string> Urls { get; }
}
