# Quickstart & Validation: Domain Webhook Management

This guide shows how to use the webhook-management capability and how to validate it offline. It
references [contracts/public-api.md](./contracts/public-api.md) and [data-model.md](./data-model.md) for
the exact surface and rules.

## Prerequisites

- Repo builds and tests green: `dotnet build` and `dotnet test` from the repo root.
- A registered Mailgunner client (feature 002) — `AddMailgunner(...)`. No Mailgun credentials are needed
  for the tests; all validation is network-free via the test fakes.

## Usage (consumer perspective)

```csharp
// client is an injected IMailgunnerClient
var webhooks = client.Webhooks;

// Register one event type with a single callback URL (the common case)
WebhookRegistration created = await webhooks.CreateAsync(
    WebhookEventType.Delivered,
    new[] { "https://app.example.com/hooks/delivered" });

// Register one URL across several event types in a single call (fans out, sequential, fail-fast)
IReadOnlyList<WebhookRegistration> many = await webhooks.CreateAsync(
    new[] { WebhookEventType.Opened, WebhookEventType.Clicked, WebhookEventType.Complained },
    "https://app.example.com/hooks/engagement");

// List everything currently configured for the domain
IReadOnlyList<WebhookRegistration> all = await webhooks.ListAsync();

// Read one event type's registration
WebhookRegistration opened = await webhooks.GetAsync(WebhookEventType.Opened);

// Repoint an event type to a new URL (replaces the existing URL list)
WebhookRegistration updated = await webhooks.UpdateAsync(
    WebhookEventType.Delivered,
    new[] { "https://app.example.com/hooks/delivered-v2" });

// Stop receiving an event type
await webhooks.DeleteAsync(WebhookEventType.Clicked);
```

Wire effect (US region, domain `mg.example.com`):

```
POST   https://api.mailgun.net/v3/mg.example.com/webhooks         id=delivered & url=https://app.example.com/hooks/delivered
GET    https://api.mailgun.net/v3/mg.example.com/webhooks
GET    https://api.mailgun.net/v3/mg.example.com/webhooks/opened
PUT    https://api.mailgun.net/v3/mg.example.com/webhooks/delivered  url=https://app.example.com/hooks/delivered-v2
DELETE https://api.mailgun.net/v3/mg.example.com/webhooks/clicked
```

> Requests carry HTTP Basic auth and route to the configured region's host automatically (reusing the
> feature-002 typed client). A non-2xx response (including not-found on read/update/delete of an
> unregistered event type) throws `MailgunnerException` exposing `StatusCode` and `ResponseBody`.

## Validation scenarios (offline tests)

Run `dotnet test`. The new `WebhookManagement` tests must cover the following, asserting against the
captured request(s) via `StubHttpMessageHandler` / `CapturingHttpMessageHandler` (see the existing
`SuppressionGetTests` / `BatchFailureTests` for the patterns):

| Scenario | Expected |
|----------|----------|
| Create, single URL | `POST .../webhooks`, multipart `id=delivered`, one `url`; returns the registration |
| Create, multiple URLs | one `POST`, `id=clicked`, two `url` parts; both URLs returned |
| Create fan-out, full success | one `POST` per event type, in order; one registration each |
| Create fan-out, partial failure | requests stop at the first non-2xx; exact count/order asserted; `MailgunnerException`; no rollback |
| List, several registered | `GET .../webhooks`; map → one registration per present event type with URL(s) |
| List, none registered | empty list, no error, single request |
| Read one, registered | `GET .../webhooks/{token}`; returns that registration |
| Read one, not-found (404) | `MailgunnerException(404, body)` |
| Update, registered | `PUT .../webhooks/{token}` with new `url`(s); returns updated registration |
| Update, not-found (404) | `MailgunnerException(404, body)` |
| Delete, registered | `DELETE .../webhooks/{token}`; success |
| Delete, not-found (404) | `MailgunnerException(404, body)` |
| Non-2xx on any op | `MailgunnerException` with status + raw body; no other exception type |
| Empty/all-blank `urls`, or empty `eventTypes` | `ArgumentException`, `stub.Requests` empty |
| Event-type ↔ token mapping | each enum value maps to its exact wire token (incl. `permanent_fail`, `temporary_fail`) |
| Region/domain routing | request host matches region; path carries the configured domain; Basic auth present |
| Cancellation | a cancelled token stops the operation; mid-fan-out, no further creates issued |
| Independence | works with only feature-002 config; no dependency on send/suppression paths |

## Definition of done

- All scenarios above pass network-free; no real Mailgun call, no credentials.
- `dotnet build` and `dotnet test` green with no new warnings (warnings-as-errors).
- XML docs present on `IMailgunWebhooks`, `WebhookEventType`, `WebhookRegistration`, and the new
  `IMailgunnerClient.Webhooks` property.
- CHANGELOG `Unreleased` → `Added` entry describing the domain webhook management surface (SemVer MINOR).
