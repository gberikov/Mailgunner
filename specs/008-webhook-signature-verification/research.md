# Phase 0 Research: Webhook Signature Verification

All Technical-Context unknowns are resolved below. There were no open `NEEDS CLARIFICATION`
markers after `/speckit-clarify` (the public-surface shape was decided there). The remaining
research is confirming the cryptographic primitives behave identically across both target
frameworks without adding a dependency.

## Decision 1 — Public surface: one static pure method

- **Decision**: Expose a single public static class `MailgunWebhookSignature` (namespace
  `Mailgunner`) with one method:
  `public static bool Verify(string signingKey, string timestamp, string token, string signature)`.
  No instance, no DI, no configuration object, no `IMailgunnerClient` member.
- **Rationale**: The `/speckit-clarify` session (Session 2026-06-24) selected exactly this shape;
  it matches the acceptance criterion "a pure function with no network dependency; built
  independently" and keeps the public surface minimal (Principle IV). The caller sources its key
  from its own configuration (Principle V) and passes it in.
- **Alternatives considered**:
  - *Instance service `IWebhookSignatureVerifier` with the key from `IOptions`* — rejected by
    clarification; adds DI/state for a stateless one-shot computation.
  - *Method on `IMailgunnerClient`* — rejected: couples a network-free primitive to the HTTP client
    and forces consumers to construct a client just to verify a signature.
  - *A grouping input struct for (timestamp, token, signature)* — rejected for v1 to keep the
    surface to one type/one method; the four-string signature is the clarified contract. Can be
    added later additively if ergonomics demand it.

## Decision 2 — Cryptographic primitive and message construction

- **Decision**: Compute `HMACSHA256` with `key = UTF8(signingKey)` over
  `message = UTF8(timestamp + token)` (ordinal string concatenation, no separator), then hex-encode
  the 32-byte MAC as **lowercase** to produce the expected signature.
- **Rationale**: This is Mailgun's documented webhook signing scheme and the construction the
  constitution mandates verbatim ("HMAC-SHA256 over `timestamp + token`"). `HMACSHA256` lives in
  `System.Security.Cryptography` and is in-box on both `net8.0` and `netstandard2.0` — **no new
  dependency** (Principle I).
- **Alternatives considered**:
  - *Decode the provided hex to bytes and compare against the raw 32-byte MAC* — viable, but
    requires hex-parsing untrusted input (malformed input must then fail closed). Comparing in the
    **hex domain** (expected lowercase-hex bytes vs the provided signature's ASCII bytes) avoids a
    parse step and trivially satisfies FR-005 (any non-hex/garbage simply does not match). Chosen
    for simplicity and robustness.
  - *Adding a separator between timestamp and token* — rejected: Mailgun concatenates directly;
    inserting a separator would never match a genuine signature.

## Decision 3 — Constant-time comparison across both target frameworks

- **Decision**: Compare the expected lowercase-hex bytes against the provided signature's bytes
  using a constant-time equality:
  - `net8.0`: `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, provided)`.
  - `netstandard2.0`: an internal `static bool FixedTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)`
    that returns `false` immediately on length mismatch (length is not secret) and otherwise
    XOR-accumulates every byte pair into an accumulator, returning `accumulator == 0` — **no early
    return inside the loop**.
  Both branches are selected with `#if NET8_0_OR_GREATER`, the project's established TFM-bridging
  pattern.
- **Rationale**: `CryptographicOperations` was introduced in .NET Core 2.1 / .NET Standard 2.1 and
  is **not available on `netstandard2.0`**, so a hand-rolled fixed-width comparison is required for
  that target. The XOR-accumulate idiom is the canonical constant-time string/byte compare and
  guarantees FR-004 / SC-004 (the loop examines the full width and never short-circuits on the first
  differing character). Comparing equal-length inputs only after a length check leaks at most the
  length, which is public (signatures are a fixed 64 hex chars).
- **Alternatives considered**:
  - `string.Equals` / `SequenceEqual` / a naive `for` returning on first mismatch — rejected:
    short-circuits and is timing-observable (the exact attack FR-004 forbids).
  - Referencing a polyfill package for `CryptographicOperations` on `netstandard2.0` — rejected:
    violates the minimal-dependency principle for ~10 lines of well-understood code.

## Decision 4 — Input contract: precondition vs fail-closed

- **Decision**:
  - `signingKey`: a **precondition**. `null`, empty, or whitespace → throw `ArgumentException`
    (the spec's "signing key empty or missing → configuration error surfaced to caller").
  - `timestamp`, `token`, `signature`: **untrusted webhook input**. Any `null`, any empty value, a
    wrong-length signature, or a non-hexadecimal signature → return `false`; **never throw**. (If
    `timestamp` or `token` is `null`, return `false` rather than computing over a null.)
- **Rationale**: Cleanly separates the consumer's own misconfiguration (worth surfacing loudly)
  from adversary-controlled request fields (which must fail closed, per FR-005 and the edge cases).
  This makes `Verify` total over all webhook-supplied inputs and safe to call directly on raw
  request values.
- **Alternatives considered**:
  - *Throw on null timestamp/token/signature* — rejected: a forged request with a missing field
    should be a quiet "not authentic," not an exception the consumer must catch.
  - *Return `false` for a blank signing key too* — rejected: a missing key is a deployment bug, not
    a forged event; silently returning `false` would mask it and reject every genuine webhook.

## Decision 5 — Hex casing and comparison exactness

- **Decision**: Produce the expected signature as **lowercase** hex and compare exactly (case
  sensitive) against the provided signature.
- **Rationale**: Mailgun emits the signature as lowercase hex, so an exact lowercase comparison
  matches all genuine signatures while keeping the comparison a single fixed-width pass. Documented
  as an assumption in the spec/contract.
- **Alternatives considered**:
  - *Case-insensitive comparison (lowercase the provided value first)* — harmless to timing (it
    operates on attacker-supplied data, not the secret), but adds normalization complexity for a
    case Mailgun does not produce. Deferred; can be relaxed later without breaking callers.

## Decision 6 — No HTTP, no transport, no fake handler in tests

- **Decision**: Implement and test the feature with **no `HttpClient`, no `IHttpClientFactory`, and
  no fake `HttpMessageHandler`**. Unit tests compute a reference HMAC-SHA256 hex locally (with a
  test-only key) and assert `Verify`'s boolean results across valid, tampered, malformed, and
  independence scenarios.
- **Rationale**: FR-006/FR-007 require the function to be pure and usable with nothing else
  configured. There is no network surface to fake; introducing one would contradict the design.
- **Alternatives considered**: none — a transport-free pure function has no HTTP to exercise.

## Decision 7 — Replay/freshness explicitly out of scope

- **Decision**: `Verify` answers only "is this signature valid for these values and this key?" It
  performs **no** timestamp-age or token-reuse checks.
- **Rationale**: The spec assigns replay protection and freshness to the consumer; conflating it
  with signature validity would overload a single-purpose primitive and force a clock/state
  dependency into a pure function.
- **Alternatives considered**: *Reject old timestamps inside `Verify`* — rejected: requires a clock
  (impure) and a policy the consumer should own.

## Summary of dependency impact

- **Runtime dependencies added: none.** Uses only `System.Security.Cryptography` (BCL).
- **`System.Text.Json` / `Polly` / `Microsoft.Extensions.Http`**: present from earlier features,
  **unused** by this one.
- **Public surface added**: one static class, one method (additive, SemVer-safe pre-1.0).
