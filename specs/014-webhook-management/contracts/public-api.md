# Phase 1 Contract: Public API surface

The feature adds one public capability interface, one enum, one record, and one client property. All
members carry XML documentation. The change is additive (SemVer **MINOR**); no existing signature changes.

## New enum: `Mailgunner.WebhookEventType`

```csharp
namespace Mailgunner;

/// <summary>
/// The closed set of Mailgun delivery events a domain webhook can be registered for. A webhook is keyed
/// by exactly one of these. (Mailgun's <c>accepted</c> event is intentionally not included.)
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
    /// <summary>The message permanently failed — a.k.a. "failed" (<c>permanent_fail</c>).</summary>
    PermanentFail,
    /// <summary>The message temporarily failed (<c>temporary_fail</c>).</summary>
    TemporaryFail,
}
```

## New record: `Mailgunner.WebhookRegistration`

```csharp
namespace Mailgunner;

/// <summary>
/// A domain webhook registration: a single <see cref="WebhookEventType"/> associated with the callback
/// URL(s) Mailgun invokes when that event occurs. Returned by read-one and update, and the per-event-type
/// element of a list.
/// </summary>
public sealed record WebhookRegistration
{
    /// <summary>Gets the event type this registration is keyed by.</summary>
    public WebhookEventType EventType { get; }

    /// <summary>Gets the callback URL(s) for this event type (1–3); never null or empty.</summary>
    public System.Collections.Generic.IReadOnlyList<string> Urls { get; }

    // Constructed by the library from a parsed response.
}
```

## New capability: `Mailgunner.IMailgunWebhooks`

```csharp
namespace Mailgunner;

/// <summary>
/// Domain webhook management for the configured domain — list, read, create, update, and delete the
/// callback URLs Mailgun invokes for each delivery event. Reached via
/// <see cref="IMailgunnerClient.Webhooks"/>. These are v3 JSON-response endpoints, independent of the
/// sending pipeline and of webhook signature verification (<see cref="MailgunWebhookSignature"/>).
/// </summary>
public interface IMailgunWebhooks
{
    /// <summary>Lists every event type that has a webhook registration for the domain.</summary>
    /// <returns>One registration per registered event type; empty when none are configured.</returns>
    /// <exception cref="MailgunnerException">The service returned a non-success response.</exception>
    System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<WebhookRegistration>> ListAsync(
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>Reads the registration for one event type.</summary>
    /// <exception cref="MailgunnerException">The event type has no registration (not-found) or any other non-success response.</exception>
    System.Threading.Tasks.Task<WebhookRegistration> GetAsync(
        WebhookEventType eventType,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>Creates a registration for one event type with one or more callback URLs (max 3).</summary>
    /// <exception cref="System.ArgumentException"><paramref name="urls"/> is null/empty or every entry is blank.</exception>
    /// <exception cref="MailgunnerException">The service returned a non-success response.</exception>
    System.Threading.Tasks.Task<WebhookRegistration> CreateAsync(
        WebhookEventType eventType,
        System.Collections.Generic.IEnumerable<string> urls,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a single callback URL across several event types in one call. Fans out to one create per
    /// event type, issued sequentially in order; fail-fast with no rollback (the first non-success throws
    /// and stops the rest, leaving earlier creates in place).
    /// </summary>
    /// <returns>One registration per event type, in order, on full success.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="eventTypes"/> is null/empty or <paramref name="url"/> is blank.</exception>
    /// <exception cref="MailgunnerException">The first non-success response; earlier creates are not rolled back.</exception>
    System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<WebhookRegistration>> CreateAsync(
        System.Collections.Generic.IEnumerable<WebhookEventType> eventTypes,
        string url,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>Replaces the callback URL(s) for an event type's registration.</summary>
    /// <exception cref="System.ArgumentException"><paramref name="urls"/> is null/empty or every entry is blank.</exception>
    /// <exception cref="MailgunnerException">The event type has no registration (not-found) or any other non-success response.</exception>
    System.Threading.Tasks.Task<WebhookRegistration> UpdateAsync(
        WebhookEventType eventType,
        System.Collections.Generic.IEnumerable<string> urls,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>Deletes an event type's registration.</summary>
    /// <exception cref="MailgunnerException">The event type has no registration (not-found) or any other non-success response.</exception>
    System.Threading.Tasks.Task DeleteAsync(
        WebhookEventType eventType,
        System.Threading.CancellationToken cancellationToken = default);
}
```

## New property: `Mailgunner.IMailgunnerClient.Webhooks`

```csharp
/// <summary>
/// Gets access to the domain's webhook registrations (list, read, create, update, delete). These are
/// v3 JSON-response endpoints, independent of the sending methods and of signature verification.
/// </summary>
IMailgunWebhooks Webhooks { get; }
```

## Behavioral contract

| # | Given | When | Then |
|---|-------|------|------|
| C1 | `eventType=Delivered`, `urls=["https://a"]` | `CreateAsync` | `POST v3/{domain}/webhooks` with form `id=delivered` and one `url=https://a`; returns `{Delivered, [https://a]}` |
| C2 | `eventType=Clicked`, `urls=["https://a","https://b"]` | `CreateAsync` | one `POST` with `id=clicked` and two `url` parts; returns both URLs |
| C3 | `eventTypes=[Delivered,Opened,Clicked]`, `url="https://a"` | fan-out `CreateAsync` | three sequential `POST`s (`id=delivered`, then `opened`, then `clicked`), each with `url=https://a`; returns 3 registrations in order |
| C4 | fan-out, 2nd create returns 500 | `CreateAsync` | exactly 2 requests issued (`delivered` ok, `opened` fails); throws `MailgunnerException(500, body)`; `clicked` not issued; no rollback |
| C5 | domain with several event types registered | `ListAsync` | `GET v3/{domain}/webhooks`; returns one registration per present event type with its URL(s) |
| C6 | domain with no webhooks | `ListAsync` | returns empty list; no error |
| C7 | `eventType=Opened` registered | `GetAsync` | `GET v3/{domain}/webhooks/opened`; returns `{Opened, [urls…]}` |
| C8 | `eventType=Opened` not registered (404) | `GetAsync` | throws `MailgunnerException(404, body)` |
| C9 | `eventType=Delivered`, `urls=["https://new"]` | `UpdateAsync` | `PUT v3/{domain}/webhooks/delivered` with `url=https://new`; returns updated registration |
| C10 | update of unregistered event type (404) | `UpdateAsync` | throws `MailgunnerException(404, body)` |
| C11 | `eventType=Delivered` | `DeleteAsync` | `DELETE v3/{domain}/webhooks/delivered`; completes on success |
| C12 | delete of unregistered event type (404) | `DeleteAsync` | throws `MailgunnerException(404, body)` |
| C13 | any operation, non-2xx | any | throws `MailgunnerException` exposing status code + raw body; no other exception type |
| C14 | `urls=[]` or all-blank, or empty `eventTypes` set | create/update | throws `ArgumentException`; **no request issued** |
| C15 | any operation | any | routed to the region base URL + configured domain, Basic auth reused; no new `HttpClient` |
| C16 | a cancelled token | any | the operation stops promptly; mid-fan-out, no further creates are issued |

## Error contract

- Input/validation failures (C14) throw `System.ArgumentException` (or `ArgumentOutOfRangeException` for an
  undefined `WebhookEventType`) before any HTTP request — no new exception type.
- `MailgunnerException` continues to be the sole HTTP-error type, exposing `StatusCode` and `ResponseBody`.

## Versioning

- SemVer **MINOR** (additive). CHANGELOG `Unreleased` → `Added` entry required.
