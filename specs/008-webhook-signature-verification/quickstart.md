# Quickstart: Webhook Signature Verification

A validation/run guide for the `MailgunWebhookSignature.Verify(...)` primitive. Implementation
details live in `tasks.md` and the source; this file shows how to use and validate the feature.

## Prerequisites

- The repository builds: `dotnet build` (multi-targets `net8.0` + `netstandard2.0`).
- No credentials, no network, and no running service are needed — this feature is a pure function.

## What it does

Given the three fields Mailgun sends with an event webhook (`timestamp`, `token`, `signature`) and
your Mailgun **HTTP webhook signing key**, `Verify` returns `true` only if the signature is the
HMAC-SHA256 of `timestamp + token` keyed by your signing key, compared in constant time. Use it to
gate any action on incoming bounce/complaint/unsubscribe events.

## Usage sketch (consumer side)

The consumer parses the webhook request and passes the extracted fields. Pseudocode for an ASP.NET
Core handler (the parsing belongs to the consumer, not the library):

```csharp
// signingKey comes from YOUR configuration (e.g. IConfiguration / env), never hard-coded.
var ok = MailgunWebhookSignature.Verify(
    signingKey: signingKey,
    timestamp:  form["signature.timestamp"],
    token:      form["signature.token"],
    signature:  form["signature.signature"]);

if (!ok)
    return Results.Unauthorized();   // forged or tampered → do not touch suppression state

// authentic → safe to act on the event
```

Notes:
- `Verify` does **not** check whether the event is stale or replayed — add your own
  timestamp-freshness / token-reuse checks if you need them.
- Only the **signing key** is a precondition: a blank/whitespace/null key throws
  `ArgumentException` (a deployment misconfiguration). Any malformed or missing
  webhook-supplied field simply returns `false`.

## Validation scenarios (run via `dotnet test`)

Run the suite (offline, no credentials):

```bash
dotnet test
```

Expected: all tests green, including the new `Webhooks/` tests. The tests independently compute a
reference HMAC-SHA256 hex (with a test-only key) and assert:

| Scenario | Expected |
|----------|----------|
| Correct signature for the (timestamp, token) under the matching key | `Verify` → `true` |
| Same triple, different signing key | `false` |
| Signature / timestamp / token each altered by one character | `false` (each case) |
| Empty, null, wrong-length, or non-hex signature | `false`, no exception |
| Null timestamp or token | `false`, no exception |
| Blank / whitespace / null signing key | throws `ArgumentException` |
| Signature equal to expected except the final character | `false` (full-width comparison) |
| Called with no client, DI, or `HttpClient` configured | works (pure, independent) |

See `contracts/webhook-signature-contract.md` (cases C1–C14) and `data-model.md` (rules R1–R7) for
the authoritative behavior. The constant-time guarantee is provided by construction
(`CryptographicOperations.FixedTimeEquals` on `net8.0`; an internal fixed-width XOR-accumulate
comparison on `netstandard2.0`) and confirmed by review — it is not asserted via wall-clock timing,
which is not a reliable unit measurement.

## Definition of done

- `dotnet build` and `dotnet test` are green offline on both target frameworks.
- The public surface added is exactly `Mailgunner.MailgunWebhookSignature.Verify(...)`, fully
  XML-documented.
- README documents webhook signature verification; CHANGELOG (Unreleased) notes the addition.
