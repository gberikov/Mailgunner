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
        EventToken = Internal.WebhookEventTypes.ToToken(eventType);
        Urls = urls;
    }

    /// <summary>Initializes a registration from a wire token, preserving future event types.</summary>
    /// <param name="eventToken">The non-blank Mailgun event token, such as <c>delivered</c>.</param>
    /// <param name="urls">The callback URLs.</param>
    /// <exception cref="ArgumentException"><paramref name="eventToken"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="urls"/> is null.</exception>
    public WebhookRegistration(string eventToken, IReadOnlyList<string> urls)
    {
        if (string.IsNullOrWhiteSpace(eventToken))
        {
            throw new ArgumentException("An event token is required.", nameof(eventToken));
        }

        Internal.Guard.NotNull(urls, nameof(urls));
        EventToken = eventToken;
        EventType = Internal.WebhookEventTypes.TryParseToken(eventToken) ?? WebhookEventType.Unknown;
        Urls = urls;
    }

    /// <summary>
    /// Gets the event type this registration is keyed by.
    /// </summary>
    public WebhookEventType EventType { get; }

    /// <summary>Gets the original Mailgun event token, including when <see cref="EventType"/> is unknown.</summary>
    public string EventToken { get; }

    /// <summary>
    /// Gets the callback URL(s) Mailgun invokes for this event type (Mailgun allows up to three). Never
    /// null; never empty for a registration returned by the service.
    /// </summary>
    public IReadOnlyList<string> Urls { get; }
}
