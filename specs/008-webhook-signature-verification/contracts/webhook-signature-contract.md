# Contract: Webhook Signature Verification

The library exposes one network-free public primitive. This contract pins the observable behavior
the unit tests assert. There is **no HTTP request/response** for this feature — the "contract" is
the function's input/output behavior.

## Public surface

```csharp
namespace Mailgunner;

/// <summary>
/// Verifies that a Mailgun event webhook genuinely originates from Mailgun by validating its
/// signature. Pure and network-free: no HTTP, no dependency injection, no shared state.
/// </summary>
public static class MailgunWebhookSignature
{
    /// <summary>
    /// Returns <see langword="true"/> only when <paramref name="signature"/> matches the
    /// HMAC-SHA256 of <paramref name="timestamp"/> concatenated with <paramref name="token"/>,
    /// keyed by <paramref name="signingKey"/>, compared in constant time.
    /// </summary>
    /// <param name="signingKey">The Mailgun HTTP webhook signing key. Supplied by the caller.</param>
    /// <param name="timestamp">The webhook's timestamp field (untrusted).</param>
    /// <param name="token">The webhook's token field (untrusted).</param>
    /// <param name="signature">The webhook's hex signature field to validate (untrusted).</param>
    /// <returns><see langword="true"/> if authentic; otherwise <see langword="false"/>.</returns>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="signingKey"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    public static bool Verify(string signingKey, string timestamp, string token, string signature);
}
```

- **Signature algorithm** (fixed by Mailgun + constitution): expected =
  lowercase-hex( HMACSHA256( key = UTF8(signingKey), message = UTF8(timestamp + token) ) ).
- **Comparison**: constant-time over the full width (`CryptographicOperations.FixedTimeEquals` on
  `net8.0`; internal fixed-width XOR-accumulate on `netstandard2.0`). No first-mismatch
  short-circuit.

## Behavioral contract (per input)

| # | Scenario | `signingKey` | `timestamp` / `token` / `signature` | Result | Maps to |
|---|----------|--------------|--------------------------------------|--------|---------|
| C1 | Genuine webhook | valid key | values + the signature that key produces for them | `true` | US1, FR-001/002, SC-001 |
| C2 | Wrong key | valid but different key | otherwise-correct triple | `false` | US1, FR-003 |
| C3 | Tampered signature | valid key | correct ts/token, altered signature | `false` | US2, FR-003, SC-002 |
| C4 | Tampered timestamp | valid key | altered timestamp, original token+signature | `false` | US2, FR-003, SC-002 |
| C5 | Tampered token | valid key | altered token, original ts+signature | `false` | US2, FR-003, SC-002 |
| C6 | Empty signature | valid key | `signature = ""` | `false`, no throw | FR-005, SC-003 |
| C7 | Null signature | valid key | `signature = null` | `false`, no throw | FR-005, SC-003 |
| C8 | Wrong-length signature | valid key | hex but not 64 chars | `false`, no throw | FR-005, SC-003, edge case |
| C9 | Non-hex signature | valid key | 64 chars of non-hex / garbage | `false`, no throw | FR-005, SC-003, edge case |
| C10 | Null timestamp or token | valid key | `timestamp` or `token` = `null` | `false`, no throw | FR-005, edge case |
| C11 | Empty timestamp/token | valid key | empty strings, signature signed over them | result follows the signature (definite, no throw) | edge case |
| C12 | Blank/null signing key | `null`/`""`/`"   "` | any | **throws** `ArgumentException` | edge case, FR-008 |
| C13 | Last-character-only difference | valid key | signature equal to expected except final char | `false`; comparison is full-width (no early exit) | US3, FR-004, SC-004 |
| C14 | Independence | valid key | genuine triple | `true`, with no client / DI / `HttpClient` constructed | FR-006/007, SC-005 |

## Invariants

- **Pure**: same inputs → same output; no network, I/O, clock, or shared state. *(SC-005)*
- **Fail-closed on untrusted input**: only `signingKey` can raise; the three webhook-supplied
  values never throw — they resolve to `true`/`false`. *(FR-005)*
- **Constant-time**: timing does not reveal how many leading characters of the signature matched.
  *(FR-004 — verified by construction/review; not asserted via wall-clock timing.)*
- **No secret leakage**: the signing key never appears in output, exceptions, or logs.
- **Out of scope**: replay/freshness/timestamp-age — not performed. *(Assumptions)*
