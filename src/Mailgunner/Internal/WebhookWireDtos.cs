namespace Mailgunner.Internal;

/// <summary>
/// The wire shape of a single webhook's callback URLs: a <c>urls</c> array. Shared by the read-one,
/// create, and update response envelopes and by each event type's entry in a list response. Internal;
/// projected to <see cref="WebhookRegistration"/> before leaving the library.
/// </summary>
internal sealed class WebhookUrlsDto
{
    /// <summary>Gets or sets the callback URL(s) registered for an event type.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("urls")]
    public System.Collections.Generic.List<string>? Urls { get; set; }
}

/// <summary>
/// The wire shape of a single-webhook response: <c>{ "webhook": { "urls": [...] } }</c>. Returned by
/// read-one, create, and update.
/// </summary>
internal sealed class WebhookEnvelopeDto
{
    /// <summary>Gets or sets the single webhook's URL set.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("webhook")]
    public WebhookUrlsDto? Webhook { get; set; }
}

/// <summary>
/// The wire shape of the list response: <c>{ "webhooks": { "delivered": { "urls": [...] }, ... } }</c>,
/// an object keyed by event-type token. Event types with no registration are absent or carry an empty
/// <c>urls</c>.
/// </summary>
internal sealed class WebhookListDto
{
    /// <summary>Gets or sets the per-event-type webhook map, keyed by wire token.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("webhooks")]
    public System.Collections.Generic.Dictionary<string, WebhookUrlsDto?>? Webhooks { get; set; }
}

/// <summary>
/// Maps each <see cref="WebhookEventType"/> to and from its Mailgun wire token (the snake_case event
/// name used in the path and the list response). Isolating the wire vocabulary here keeps the public
/// enum names independent of the wire spelling.
/// </summary>
internal static class WebhookEventTypes
{
    /// <summary>
    /// Returns the wire token for an event type (for example <see cref="WebhookEventType.PermanentFail"/>
    /// → <c>permanent_fail</c>).
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <returns>The wire token.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="eventType"/> is not a defined value.</exception>
    public static string ToToken(WebhookEventType eventType) => eventType switch
    {
        WebhookEventType.Delivered => "delivered",
        WebhookEventType.Opened => "opened",
        WebhookEventType.Clicked => "clicked",
        WebhookEventType.Unsubscribed => "unsubscribed",
        WebhookEventType.Complained => "complained",
        WebhookEventType.PermanentFail => "permanent_fail",
        WebhookEventType.TemporaryFail => "temporary_fail",
        _ => throw new System.ArgumentOutOfRangeException(
            nameof(eventType), eventType, "Unknown webhook event type."),
    };

    /// <summary>
    /// Parses a wire token into its <see cref="WebhookEventType"/>, or returns <see langword="null"/> for
    /// an unrecognized token (so an unknown key in a list response is ignored rather than failing the
    /// parse).
    /// </summary>
    /// <param name="token">The wire token.</param>
    /// <returns>The event type, or <see langword="null"/> when the token is not a supported event.</returns>
    public static WebhookEventType? TryParseToken(string? token) => token switch
    {
        "delivered" => WebhookEventType.Delivered,
        "opened" => WebhookEventType.Opened,
        "clicked" => WebhookEventType.Clicked,
        "unsubscribed" => WebhookEventType.Unsubscribed,
        "complained" => WebhookEventType.Complained,
        "permanent_fail" => WebhookEventType.PermanentFail,
        "temporary_fail" => WebhookEventType.TemporaryFail,
        _ => null,
    };
}
