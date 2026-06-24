# Phase 1 Data Model: Webhook Signature Verification

This feature persists nothing and defines no public data types. Its "model" is the set of **input
values** the verification reads and the **boolean result** it returns. There are no entities,
relationships, or lifecycle/state transitions. The conceptual entities from the spec map to plain
method parameters.

## Conceptual inputs

### Webhook Signing Key (`signingKey`)

- **Represents**: The consumer's secret Mailgun **HTTP webhook signing key** (distinct from the
  account/sending key), shared with Mailgun so both sides can compute the same MAC.
- **Type**: `string` (method parameter; UTF-8 encoded when used as the HMAC key).
- **Validation rule**: **Precondition.** `null`, empty, or whitespace-only → `ArgumentException`.
  Never logged, stored, or embedded; supplied by the caller per invocation.

### Signed triple (the webhook's authenticity proof)

The three values Mailgun sends with each event. Together they are the "Webhook Signature" entity
from the spec, passed as three parameters. All three are **untrusted** input and never cause an
exception.

| Field | Type | Meaning | Validation / failure behavior |
|-------|------|---------|-------------------------------|
| `timestamp` | `string` | Epoch seconds string Mailgun signed the event at. Part of the HMAC message. | `null` → result `false`. Otherwise used verbatim (ordinal). No freshness/age check (out of scope). |
| `token` | `string` | One-time random value Mailgun generated for the event. Part of the HMAC message. | `null` → result `false`. Otherwise used verbatim (ordinal). No reuse/replay check (out of scope). |
| `signature` | `string` | Lowercase hex HMAC-SHA256 Mailgun computed over `timestamp + token` with the signing key. The value being checked. | `null` / empty / wrong length / non-hexadecimal → result `false`, no throw. |

### Derivation (not a stored field)

- **Expected signature** = lowercase-hex( `HMACSHA256( key = UTF8(signingKey), message = UTF8(timestamp + token) )` ).
  Computed internally per call; never exposed.

## Output

### Verification Result

- **Represents**: The authentic / not-authentic decision the consumer acts on.
- **Type**: `bool` — `true` = authentic (provided signature equals the expected signature under a
  constant-time comparison); `false` = not authentic (mismatch, or any malformed/missing
  webhook-supplied value).
- **Determinism**: Pure — identical inputs always yield the identical result; no I/O, no clock, no
  shared state.

## Validation & behavior rules (consolidated)

- **R1 (precondition)**: blank/whitespace/`null` `signingKey` → `ArgumentException` before any
  computation. *(spec edge case; FR-008)*
- **R2 (authentic)**: `true` **iff** the provided `signature` equals the expected lowercase-hex
  signature, compared in constant time. *(FR-002, FR-001)*
- **R3 (tamper/mismatch)**: any change to `signature`, `timestamp`, or `token` relative to what was
  signed → `false`. *(FR-003)*
- **R4 (fail-closed input)**: `null` `timestamp`/`token`, or empty/wrong-length/non-hex
  `signature` → `false`, never an exception. *(FR-005; edge cases)*
- **R5 (constant-time)**: the signature comparison examines the full candidate width and does not
  return on the first differing character. *(FR-004; SC-004)*
- **R6 (purity/independence)**: depends only on its four parameters; no network, I/O, DI, client,
  or shared state required to call it. *(FR-006, FR-007)*
- **R7 (scope)**: no replay, freshness, or timestamp-age logic — that is the consumer's
  responsibility. *(Assumptions)*

## Public type added

| Type | Kind | Surface |
|------|------|---------|
| `Mailgunner.MailgunWebhookSignature` | `public static class` | `public static bool Verify(string signingKey, string timestamp, string token, string signature)` |

No other public type is added. Hex-encoding and the constant-time comparison (including the
`netstandard2.0` `FixedTimeEquals` shim) are **private** helpers within this class.
