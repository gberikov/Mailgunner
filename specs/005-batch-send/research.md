# Phase 0 Research: Personalized Mass Send (Batched Recipient Variables)

All Technical Context items were resolvable from the existing 002/003/004 code and the constitution;
no `NEEDS CLARIFICATION` remained. The two behavioral ambiguities (partial-failure, duplicates) were
already resolved in the spec's Clarifications session. The decisions below record the design choices.

## Decision 1 — Batch input model: two small data-only public types

- **Decision**: Add `MailgunBatchMessage` (sender, subject, `Template`, `TemplateVersion`,
  `GenerateTextFromTemplate`, global `TemplateVariables`, and an ordered `Recipients` list) plus
  `BatchRecipient` (an `EmailAddress` and that recipient's own `Variables` map). Add one method
  `IMailgunnerClient.SendBatchAsync(MailgunBatchMessage, CancellationToken)` returning
  `IReadOnlyList<SendResult>`.
- **Rationale**: A batch genuinely carries data `MailgunMessage` cannot model — per-recipient variable
  bundles. Modeling each recipient as `(address, variables)` makes the `recipient-variables` JSON map
  fall out directly and keeps the headline call type-safe. Naming the global map `TemplateVariables`
  matches feature 004 (same `t:variables` wire field); per-recipient `Variables` maps to
  `recipient-variables`.
- **Alternatives considered**:
  - *Reuse `MailgunMessage` + a side dictionary param* — rejected: untyped, easy to misalign keys with
    recipients, and pollutes the single-send type with batch-only concepts.
  - *One mega-type for single + batch* — rejected: widens the public surface and the validation matrix;
    violates the "small public surface" principle.

## Decision 2 — Templated batch (Template required)

- **Decision**: `Template` is **required** (non-blank) for a batch send; otherwise `ArgumentException`
  before any request. Global `TemplateVariables`, `TemplateVersion`, and `GenerateTextFromTemplate`
  reuse feature 004's exact emission rules.
- **Rationale**: The feature is defined as sending "one **templated** email" to thousands (spec
  headline, FR-001/FR-005). Requiring a template keeps scope tight and the wire contract unambiguous.
- **Alternatives considered**: *Allow inline-body batches with `%recipient.var%` substitution* —
  rejected as out of scope; can be added later without breaking the templated path.

## Decision 3 — Chunking: consecutive slices of ≤1000, ceil(N/1000) requests

- **Decision**: Partition `Recipients` into consecutive slices of at most `MaxRecipientsPerRequest =
  1000`, preserving order; chunk *k* holds recipients `[k·1000, (k+1)·1000)`. Number of requests =
  `ceil(N/1000)`. `N = 0` → zero chunks (no-op). Exact multiples (1000, 2000) produce no trailing empty
  chunk.
- **Rationale**: Directly satisfies the acceptance criteria (2500→1000/1000/500; 1000→1; 2000→2; 0→0)
  and the constitution's "max 1000 recipients per request via automatic chunking." Order preservation
  makes behavior deterministic and testable.
- **Alternatives considered**: *Configurable chunk size* — rejected: the 1000 limit is a fixed external
  constraint, not a tuning knob (kept as an internal constant).

## Decision 4 — `recipient-variables` shape and keys

- **Decision**: Per chunk, emit one `recipient-variables` field whose value is a single
  `System.Text.Json`-serialized object keyed by each recipient's **bare `Address`** (not the
  display-name-formatted form), each value being that recipient's `Variables` map (an empty map
  serializes to `{}`). The chunk's recipients are also emitted as repeated `to` parts (formatted via
  `EmailAddress.ToString()`).
- **Rationale**: Mailgun matches `recipient-variables` keys to the recipient address; pairing repeated
  `to` parts with `recipient-variables` is precisely what makes Mailgun send an individual message per
  recipient (each sees only their own address — FR-007). Keys must therefore be the bare address.
- **Alternatives considered**: *Key by the formatted `"Name <addr>"` value* — rejected: would not match
  Mailgun's recipient address and would break personalization.

## Decision 5 — Address privacy is a Mailgun-side guarantee

- **Decision**: The library's responsibility for FR-007 ("each recipient sees only their own address")
  is limited to emitting the correct wire shape (recipients in `to` + `recipient-variables`). The
  per-recipient delivery isolation is performed by Mailgun. Tests assert the wire shape (exact `to`
  membership per chunk + correctly keyed `recipient-variables`), not Mailgun's delivery.
- **Rationale**: Delivery isolation cannot be observed offline; the testable contract is the request.
  This is documented in `quickstart.md` so the guarantee is not mistaken for client-side filtering.

## Decision 6 — Fail-fast sequential sending; return one `SendResult` per chunk

- **Decision**: Send chunks **sequentially** in order; the first non-2xx throws `MailgunnerException`
  (status + body) and stops the batch (already-sent chunks are not rolled back). On full success return
  `IReadOnlyList<SendResult>` with one entry per chunk; an empty recipient list returns an empty list.
  Reuse the existing response→result/exception logic by extracting it into a private
  `SendContentAsync(content, ct)` on `MailgunnerClient`, shared with single `SendAsync` (no behavior
  change to single send).
- **Rationale**: Matches the spec clarification (fail-fast) and the constitution's single-exception
  contract; sequential sends keep fail-fast meaningful and avoid request bursts. Extracting the shared
  helper avoids duplicating parse-or-throw logic.
- **Alternatives considered**: *Parallel chunk sends* — rejected: complicates fail-fast/cancellation and
  risks rate bursts, with no requirement to justify it. *Continue-on-error / aggregate* — rejected by
  the spec clarification.

## Decision 7 — Duplicate-address handling: reject up front

- **Decision**: If two recipients share the same bare `Address` (ordinal comparison), throw
  `ArgumentException` before any request — consistent with feature 003's client-side validation
  contract (`MailgunnerException` stays reserved for HTTP responses).
- **Rationale**: Spec clarification; a duplicate address would otherwise silently collide as a single
  `recipient-variables` key. Ordinal match mirrors JSON object key identity (case-sensitive), so two
  addresses differing only by case are *not* duplicates.

## Decision 8 — Test transport: extend `StubHttpMessageHandler` additively

- **Decision**: Extend `StubHttpMessageHandler` to record **every** request (each request's captured
  multipart fields, URI, method, content media type) and to accept an optional per-request-index
  response selector (so a test can make chunk *k* return a failing status). Keep all existing
  `Last*`/`LastFormData` members pointing at the most recent request.
- **Rationale**: Batch assertions need request count and per-chunk field capture; the existing fake only
  keeps the last request. Extending additively keeps all 003/004 sending tests green and avoids a second
  near-duplicate fake.
- **Alternatives considered**: *New dedicated recording fake* — rejected: duplicates capture logic and
  fragments the test infrastructure.
