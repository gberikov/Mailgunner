# Webhooks
[← All Mailgunner docs](https://github.com/gberikov/Mailgunner/blob/master/README.md#documentation)

## Domain webhooks

`client.Webhooks` manages the callback URLs Mailgun invokes for each delivery event of the domain
(Mailgun's v3 domain-webhook endpoints). A registration is keyed by one `WebhookEventType` (`Accepted`,
`Delivered`, `Opened`, `Clicked`, `Unsubscribed`, `Complained`, `PermanentFail`, `TemporaryFail`) and
carries up to three absolute `http(s)` URLs:

```csharp
await client.Webhooks.CreateAsync(WebhookEventType.Delivered, new[] { "https://app.example.com/hooks/mailgun" }, ct);
await client.Webhooks.CreateAsync(new[] { WebhookEventType.Complained, WebhookEventType.Unsubscribed }, "https://app.example.com/hooks/mailgun", ct);
IReadOnlyList<WebhookRegistration> all = await client.Webhooks.ListAsync(ct);
WebhookRegistration one = await client.Webhooks.GetAsync(WebhookEventType.Delivered, ct); // 404 → MailgunnerException
await client.Webhooks.UpdateAsync(WebhookEventType.Delivered, new[] { "https://app.example.com/hooks/v2" }, ct);
await client.Webhooks.DeleteAsync(WebhookEventType.Delivered, ct);
```

The multi-event overload issues one create per distinct event type, in order, and is fail-fast with no
rollback. A URL that is not an absolute `http`/`https` URL is rejected with `ArgumentException` before any
request.

List results preserve future event types as `WebhookEventType.Unknown`, with the exact wire name
in `WebhookRegistration.EventToken` and the same typed `Urls` collection. Known types retain their
enum values. The `Unknown` sentinel is response-only and cannot be passed to management methods.
Missing response envelopes or invalid URL lists throw `MailgunnerException`; create/update also
accept a non-blank message-only acknowledgement, using the submitted URLs in the result.

## Webhook signature verification

Mailgun signs each event webhook (bounces, complaints, unsubscribes) so consumers can confirm it
genuinely came from Mailgun before acting on it. Acting on a forged event would corrupt your
suppression state and reputation handling, so verify first. `MailgunWebhookSignature.Verify` is a
pure, network-free primitive — no client, no dependency injection, no state:

```csharp
using Mailgunner;

// Extract the three signed fields from the incoming webhook request (you own the parsing),
// and supply YOUR webhook signing key from configuration — the webhook signing key, not the
// sending key, and never hard-coded.
bool authentic = MailgunWebhookSignature.Verify(
    signingKey: configuration["Mailgun:WebhookSigningKey"]!,
    timestamp:  timestamp,
    token:      token,
    signature:  signature);

if (!authentic)
    return Results.Unauthorized(); // forged or tampered — do not touch suppression state
```

- The signature is validated as the **HMAC-SHA256** of `timestamp + token`, keyed by your signing
  key and rendered as lowercase hexadecimal. The comparison is **constant-time** — it never
  short-circuits on the first differing character, so timing reveals nothing about how many leading
  characters matched.
- Only the **signing key** is a precondition: a `null`, empty, or whitespace `signingKey` throws
  `ArgumentException` (a configuration error). Every malformed or missing webhook-supplied field — a
  `null` timestamp/token, or a `null`, empty, wrong-length, or non-hexadecimal signature — returns
  `false` rather than throwing.
- Verification answers only "was this signed with the signing key?". Pass `maxAge` (e.g.
  `TimeSpan.FromMinutes(5)`) to the second overload to also reject stale or future timestamps;
  token-reuse tracking remains yours:

```csharp
bool authentic = MailgunWebhookSignature.Verify(
    signingKey: configuration["Mailgun:WebhookSigningKey"]!,
    timestamp:  timestamp,
    token:      token,
    signature:  signature,
    maxAge:     TimeSpan.FromMinutes(5));
```

