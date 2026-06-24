# Feature Specification: Webhook Signature Verification

**Feature Branch**: `008-webhook-signature-verification`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "A consumer receiving Mailgun event webhooks (bounces, complaints, unsubscribes) verifies that an incoming webhook genuinely originates from Mailgun before acting on it, by validating its signature against their webhook signing key. Acting on forged events would corrupt suppression state and reputation handling, so verification must be trivial and correct. External constraints (requirements): the signature is an HMAC-SHA256 over the concatenation of the event timestamp and token, keyed by the signing key, compared against the provided hex signature; the comparison must be constant-time. Acceptance criteria: A correctly signed payload validates as authentic. Tampering with signature, timestamp, or token fails validation. The comparison does not short-circuit on the first differing character. A pure function with no network dependency; built independently."

## Clarifications

### Session 2026-06-24

- Q: In what form is the public verification function provided? → A: A static, stateless pure method that takes the signing key as a parameter (e.g. `bool Verify(signingKey, timestamp, token, signature)`); no dependency injection, configuration object, or instance state is required to call it. The caller is responsible for obtaining its signing key from its own configuration and passing it in.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Accept a genuine Mailgun webhook (Priority: P1)

A consumer of the library receives an incoming Mailgun event webhook (a bounce,
complaint, or unsubscribe notification) carrying a signature made up of a timestamp, a
token, and a signature value. Before the consumer updates its suppression state or
reputation handling, it asks the library to confirm the webhook is authentic by checking
the signature against the consumer's own webhook signing key. When the signature is the
one Mailgun would have produced for that timestamp and token with that signing key, the
library reports the webhook as authentic and the consumer proceeds to act on the event.

**Why this priority**: This is the entire reason the feature exists — without a trustworthy
"is this really from Mailgun?" decision, a consumer cannot safely act on any webhook. It
is the minimum viable slice: a single correct yes/no answer delivers the full value.

**Independent Test**: Provide a timestamp, token, and the signature computed from those two
values with a known signing key; confirm the library reports the webhook as authentic.
Fully testable on its own with no other story present.

**Acceptance Scenarios**:

1. **Given** a timestamp, a token, and the signature that the signing key produces for that
   timestamp and token, **When** the consumer verifies the webhook with that signing key,
   **Then** the library reports the webhook as authentic.
2. **Given** the same inputs but a signing key that does not match the one used to produce
   the signature, **When** the consumer verifies the webhook, **Then** the library reports
   the webhook as not authentic.

---

### User Story 2 - Reject a forged or tampered webhook (Priority: P1)

An attacker (or a corrupted transmission) sends a webhook whose signature, timestamp, or
token has been altered so it no longer matches what the signing key would have produced.
The consumer asks the library to verify it, and the library reports the webhook as not
authentic so the consumer discards it without touching suppression state or reputation
handling.

**Why this priority**: Accepting a forged event is the failure this feature is built to
prevent; the rejection behaviour is as essential as the acceptance behaviour and must ship
together with it.

**Independent Test**: Start from a valid (timestamp, token, signature) triple, alter exactly
one of the three values, verify, and confirm the library reports "not authentic" for each
altered field.

**Acceptance Scenarios**:

1. **Given** a valid triple in which the signature value has been changed, **When** the
   consumer verifies the webhook, **Then** the library reports it as not authentic.
2. **Given** a valid triple in which the timestamp has been changed, **When** the consumer
   verifies the webhook, **Then** the library reports it as not authentic.
3. **Given** a valid triple in which the token has been changed, **When** the consumer
   verifies the webhook, **Then** the library reports it as not authentic.
4. **Given** a webhook whose signature is empty, malformed, or not in the expected hex form,
   **When** the consumer verifies the webhook, **Then** the library reports it as not
   authentic without raising an error.

---

### User Story 3 - Verification resists timing-based probing (Priority: P2)

A determined attacker repeatedly submits guessed signatures and measures how long
verification takes, hoping to learn the correct signature one character at a time. The
library compares the expected and provided signatures in a way that does not reveal, through
timing, how many leading characters matched, so such probing yields no useful information.

**Why this priority**: The correctness of the yes/no decision (P1) is usable on its own, but
without timing resistance the verification is exploitable; this hardens the primitive against
a known, practical attack class. It refines rather than enables the core value.

**Independent Test**: Confirm that the comparison examines the full width of the candidate
rather than returning as soon as the first differing position is found — i.e. the decision
does not short-circuit on the first mismatching character.

**Acceptance Scenarios**:

1. **Given** a provided signature that differs from the expected signature only at the last
   position, **When** the consumer verifies the webhook, **Then** the library reports it as
   not authentic, and the comparison does not stop at the first differing character.
2. **Given** two incorrect signatures that differ from the expected value at different
   positions, **When** each is verified, **Then** the comparison performs the same
   position-independent work for both rather than short-circuiting.

---

### Edge Cases

- What happens when the provided signature is the correct length but composed of invalid
  (non-hexadecimal) characters? → Reported as not authentic, no error raised.
- What happens when the provided signature is shorter or longer than the value the signing
  key produces? → Reported as not authentic.
- What happens when the timestamp or token is empty? → Verification still produces a
  definite authentic / not-authentic answer based on the signature; no error is raised.
- What happens when the signing key is empty or missing? → Treated as a configuration error
  surfaced to the caller rather than a silent "authentic" result.
- Does this feature judge whether a webhook is stale (old timestamp / replayed)? → Out of
  scope; see Assumptions. The feature answers only "was this signed by the holder of the
  signing key?".

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The library MUST provide a way for a consumer to decide whether an incoming
  webhook is authentic given the webhook's timestamp, token, and provided signature, plus the
  consumer's webhook signing key. This MUST be exposed as a single static, stateless pure
  method that accepts the signing key as a parameter and returns the authentic / not-authentic
  result; it MUST NOT require dependency injection, an instance, or a configuration object to
  be invoked.
- **FR-002**: The library MUST treat a webhook as authentic only when the provided signature
  equals the signature derived from the timestamp and token using the signing key, as defined
  by the external Mailgun contract (an HMAC-SHA256 over the concatenation of timestamp and
  token, keyed by the signing key, expressed as a hex string).
- **FR-003**: The library MUST report a webhook as not authentic whenever the signature,
  timestamp, or token has been altered relative to the values the signing key signed.
- **FR-004**: The comparison of the provided signature against the expected signature MUST be
  constant-time with respect to the content of the signatures: it MUST NOT short-circuit on
  the first differing character, so verification time does not reveal how many leading
  characters matched.
- **FR-005**: The library MUST return a definite authentic / not-authentic result for any
  syntactically unexpected provided signature (empty, malformed, wrong length, or
  non-hexadecimal) without raising an error for the caller to handle.
- **FR-006**: Verification MUST be a pure operation: it MUST depend only on its inputs
  (timestamp, token, provided signature, signing key) and MUST NOT perform any network call,
  I/O, or hidden shared state access.
- **FR-007**: Verification MUST be usable independently of the rest of the client — it MUST
  NOT require a configured HTTP client, account credentials, or any send/suppression feature
  to be set up first.
- **FR-008**: The signing key MUST be supplied by the caller as a parameter to the
  verification method at the time of verification. The caller obtains the key from its own
  configuration; the library MUST NOT read, store, hard-code, log, or embed the key in the
  library, tests, or samples.

### Key Entities *(include if feature involves data)*

- **Webhook Signature**: The authenticity proof attached to a Mailgun event, consisting of a
  timestamp (when Mailgun signed the event), a token (a one-time random value), and a
  signature value (the hex result Mailgun computed over timestamp and token with the signing
  key).
- **Webhook Signing Key**: A secret held by the consumer and shared with Mailgun, used to both
  produce (by Mailgun) and verify (by the consumer) signatures. Distinct from the account/send
  API keys.
- **Verification Result**: The yes/no outcome — authentic or not authentic — that the consumer
  uses to decide whether to act on the event.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A webhook signed with the matching signing key is reported as authentic in 100%
  of cases.
- **SC-002**: Altering any single one of the three signed values (signature, timestamp, token)
  causes the webhook to be reported as not authentic in 100% of cases.
- **SC-003**: A malformed, empty, wrong-length, or non-hexadecimal signature is reported as not
  authentic 100% of the time and never causes the verification call to fail with an error.
- **SC-004**: The signature comparison examines the full candidate width on every call,
  performing the same per-position work regardless of where the first difference occurs (no
  early exit on first mismatch).
- **SC-005**: Verification completes using only its inputs, with zero network requests and zero
  external dependencies required to call it, so it can be exercised entirely offline.

## Assumptions

- The cryptographic construction (HMAC-SHA256 over the timestamp-and-token concatenation,
  keyed by the signing key, compared against a hex-encoded signature) is fixed by the external
  Mailgun webhook contract and is therefore stated here as a given requirement rather than a
  design choice.
- Replay protection and timestamp-freshness checks (rejecting old or previously-seen webhooks)
  are the consumer's responsibility and are out of scope for this feature, which answers only
  "is this signature valid for these values and this key?".
- Parsing the webhook HTTP request to extract the timestamp, token, and signature fields is the
  consumer's responsibility; this feature operates on those already-extracted values.
- The signing key is the dedicated webhook signing key (not the account or sending key). The
  caller sources it from its own configuration (in line with the project's security discipline)
  and passes it to the static verification method as a parameter.
- The verification is intended to be built and validated independently of the network-touching
  parts of the client, consistent with the project's network-free unit-test requirement.
