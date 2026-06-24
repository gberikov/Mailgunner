# Phase 0 Research: One-Click List-Unsubscribe

All Technical Context items were known from the existing codebase; the items below record the design
decisions that resolve the spec's open shapes so Phase 1 can proceed without `NEEDS CLARIFICATION`.

## 1. Header format (RFC 8058 / RFC 2369)

- **Decision**: Emit a single `List-Unsubscribe` header whose value is each target wrapped in angle
  brackets, multiple targets joined by `", "` (comma + space). When one-click is requested, additionally
  emit `List-Unsubscribe-Post` with the exact literal value `List-Unsubscribe=One-Click`.
  - URL only → `<https://example.com/unsub?id=abc>`
  - mailto only → `<mailto:unsub@example.com>`
  - both → `<https://example.com/unsub?id=abc>, <mailto:unsub@example.com>`
- **Rationale**: This matches RFC 2369 (angle-bracket URI list) and RFC 8058 §2 (the one-click POST
  marker is a fixed token). Gmail/Yahoo bulk-sender one-click requires an `https` URI in
  `List-Unsubscribe` plus the `List-Unsubscribe-Post` line. The fixed token must be emitted verbatim.
- **Alternatives considered**: Letting the consumer hand-assemble the header (the status quo) — rejected
  as the very error source the feature removes. Emitting separate `List-Unsubscribe` headers per target —
  invalid; the standard expects one header listing all URIs.

## 2. Emission order when both URL and mailto are present

- **Decision**: URL first, then mailto, in a single header.
- **Rationale**: Order is immaterial to receiving mail clients (they pick a preferred scheme), but a
  fixed order makes the emitted value byte-stable for exact-match unit tests (Constitution III). Putting
  the `https` URI first also aligns with one-click, where the `https` endpoint is the relevant target.
- **Alternatives considered**: mailto-first (historically common) — equally valid but no advantage;
  consistency and one-click-first ordering favor URL-first.

## 3. Transport mechanism (how the header reaches Mailgun)

- **Decision**: Emit the headers as Mailgun custom headers — multipart fields `h:List-Unsubscribe` and
  `h:List-Unsubscribe-Post` — appended inside the existing `MailgunOptionsContent.Append`.
- **Rationale**: Mailgun forwards any `h:<Name>` field as a message header (`h:` is not limited to
  `X-*`). Reusing the established custom-header path keeps the feature within the messages scope and
  inherits the same emission helper shared by single, templated, and batch sends. No new endpoint or
  transport. The 16KB combined `o:`/`h:`/`v:`/`t:` cap applies and is documented, not enforced
  (consistent with existing options).
- **Alternatives considered**: A bespoke top-level field — Mailgun has none for List-Unsubscribe; `h:`
  is the correct channel.

## 4. URL validation and verbatim emission

- **Decision**: Model the URL as `string?`. Validate: non-blank, parses as an absolute URI whose scheme
  is `https` (ordinal-ignore-case), and contains no control characters or line breaks. Emit the original
  string verbatim inside the angle brackets.
- **Rationale**: `https`-only is mandated for one-click and is the safe default for the URL form
  generally (FR-007). Reusing a raw string (rather than round-tripping through `System.Uri`) guarantees
  the emitted value matches what the consumer supplied, which keeps unit-test assertions exact and avoids
  surprising percent-encoding normalization. Scheme is still checked by parsing with
  `Uri.TryCreate(..., UriKind.Absolute, ...)`. The control-character/line-break check mirrors the
  existing `CustomHeaders` value guard, blocking header injection.
- **Alternatives considered**: Typed `System.Uri` property — rejected because `Uri` may re-encode/
  normalize the string, making exact emission and test assertions fragile.

## 5. Mailto shape and validation

- **Decision**: Model the mailto target as `EmailAddress?` (bare address; the library prepends
  `mailto:`). Validation reuses the existing `EmailAddress` rules (non-blank, no control characters),
  which run at assignment time via the struct constructor / implicit `string` conversion. Only the
  `Address` component is used for the header; any display name is ignored.
- **Rationale**: Resolved by clarification — the consumer supplies a bare email address and the library
  forms the `mailto:` URI. Reusing `EmailAddress` gives the same injection-safe validation already
  trusted for senders/recipients and the ergonomic implicit `string` → `EmailAddress` conversion. Full
  `mailto:` URIs with `subject`/`body` are out of scope for v1.
- **Alternatives considered**: `string?` mailto validated ad hoc — duplicates logic `EmailAddress`
  already owns. Accepting a full `mailto:` URI — out of scope per clarification; larger validation
  surface.

## 6. Duplicate-header conflict guard

- **Decision**: When `ListUnsubscribe` is set, reject the send with `ArgumentException` if `CustomHeaders`
  contains a key equal (ordinal-ignore-case) to `List-Unsubscribe` or `List-Unsubscribe-Post`. The check
  runs before any field is added, so no request is issued.
- **Rationale**: Resolved by clarification — fail-fast on conflict rather than silently preferring one
  source. Case-insensitive because HTTP header names are case-insensitive, so a manual `list-unsubscribe`
  in any casing would otherwise yield a true on-the-wire duplicate. `ArgumentException` matches the
  library's existing input-validation contract (`CustomHeaders`, `EmailAddress`).
- **Alternatives considered**: Silent skip of the typed target, or typed-target override — both rejected
  in clarification as surprising/ambiguous.

## 7. Error contract

- **Decision**: All invalid configurations throw `System.ArgumentException` (paramref `options`) before
  any network call; one-click without a valid `https` URL, non-`https` URL, control characters/line
  breaks, a "set but empty" target (neither URL nor mailto), and the manual-header conflict all use it.
- **Rationale**: Constitution IV mandates a single HTTP-error type (`MailgunnerException`) and no
  proliferation of bespoke exception types. Input validation across the library already uses
  `ArgumentException`; this feature follows suit. `MailgunnerException` remains reserved for non-2xx
  responses.
- **Alternatives considered**: A new `ListUnsubscribeException` — rejected; violates the single-error-
  type discipline.
