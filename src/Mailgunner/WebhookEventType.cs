namespace Mailgunner;

/// <summary>
/// The closed set of Mailgun delivery events a domain webhook can be registered for. A webhook is keyed
/// by exactly one of these event types and carries one or more callback URLs Mailgun invokes when the
/// event occurs. (Mailgun's <c>accepted</c> event is intentionally not part of this set.)
/// </summary>
public enum WebhookEventType
{
    /// <summary>The message was delivered (<c>delivered</c>).</summary>
    Delivered,

    /// <summary>The recipient opened the message (<c>opened</c>).</summary>
    Opened,

    /// <summary>The recipient clicked a tracked link (<c>clicked</c>).</summary>
    Clicked,

    /// <summary>The recipient unsubscribed (<c>unsubscribed</c>).</summary>
    Unsubscribed,

    /// <summary>The recipient marked the message as spam (<c>complained</c>).</summary>
    Complained,

    /// <summary>The message permanently failed — also known as "failed" (<c>permanent_fail</c>).</summary>
    PermanentFail,

    /// <summary>The message temporarily failed (<c>temporary_fail</c>).</summary>
    TemporaryFail,
}
