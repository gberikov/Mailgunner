# Automatic retry & backoff
[← All Mailgunner docs](https://github.com/gberikov/Mailgunner/blob/master/README.md#documentation)

Resilience is **on by default**. Sends retry rate-limit rejections; suppression and webhook operations
also retry transient server and transport failures:

```csharp
services.AddMailgunner("mg.example.com", sendingKey, MailgunRegion.Us);
// Sends retry only 429 by default. Management requests also retry 408/5xx and transport failures.
// Retry-After is honored up to MaxSingleWait.
```

- **Sends are special** — `POST /messages` is not idempotent, so by default a send is retried **only on 429**
  (`Retry.SendRetryMode = SendRetryMode.Safe`). Set `SendRetryMode.Full` to retry sends on 408/5xx and transport
  faults too, accepting the risk of duplicate delivery. Suppression and webhook requests always use the full policy.
- **Retried** — HTTP `429`, `408`, and any `5xx`, plus transport-level faults with no response
  (timeout, connection reset/refused, DNS failure).
- **Never retried** — a `4xx` other than `408`/`429` (for example `400`/`401`/`403`/`404`) surfaces immediately as
  a `MailgunnerException` after exactly one attempt, with no wait.
- **Backoff** — exponential growth with bounded additive jitter, capped by `MaxSingleWait`.
  Waits may be equal once capped; server-provided waits can differ from the exponential schedule.
- **`Retry-After`** — when a retryable response carries `Retry-After` (delta-seconds **or** an
  HTTP-date), that value takes precedence for the next wait.
- **Mandatory cap** — *every* single wait is clamped to `MaxSingleWait`, so a hostile or far-future
  `Retry-After` cannot stall a send.
- **Bounded & observable** — the retry budget is finite; the final failure surfaces as a
  `MailgunnerException` for an HTTP response or the underlying transport exception when no response
  was obtained. Exhaustion logs a Warning (status/exception type and attempt count only).
- **Cancelable** — the caller's `CancellationToken` abandons a pending wait promptly.

A first-attempt success makes exactly one attempt with zero waiting, and an eventual success is
indistinguishable from one.

Tuning is optional (the defaults are production-ready):

```csharp
services.AddMailgunner(o =>
{
    o.Domain = "mg.example.com";
    o.SendingKey = sendingKey;
    o.Region = MailgunRegion.Us;
    o.Retry.MaxRetryAttempts = 3;                       // retries after the first attempt (>= 0; 0 disables)
    o.Retry.BaseDelay = TimeSpan.FromMilliseconds(500); // starting backoff (> 0)
    o.Retry.MaxSingleWait = TimeSpan.FromSeconds(30);   // mandatory cap on any single wait (>= BaseDelay)
    o.Retry.UseJitter = true;                           // bounded additive jitter
    o.Retry.SendRetryMode = SendRetryMode.Safe;         // Safe (429 only) or Full
    o.Retry.AttemptTimeout = TimeSpan.FromSeconds(100);  // cap on a single attempt, up to the response headers
});
```

The typed `HttpClient.Timeout` is set to the worst case over every attempt and wait,
`(MaxRetryAttempts + 1) × AttemptTimeout + MaxRetryAttempts × MaxSingleWait` (490 s with the defaults), so a
stalled response body, which `HttpClient` reads outside the per-attempt timeout, can never hang a caller
that passed no `CancellationToken`.

