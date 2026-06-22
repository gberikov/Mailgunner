# Phase 0 Research: Send a Templated Email

All Technical Context items resolved; no open `NEEDS CLARIFICATION`. The three spec ambiguities
were resolved in `/speckit-clarify` (template/body exclusivity, variable value types, empty-map
handling) and are reflected below.

## Decision 1 — How global template variables are serialized

**Decision**: Accept variables as `IDictionary<string, object?>` on `MailgunMessage` and serialize
the whole map **once** with `System.Text.Json.JsonSerializer.Serialize(...)` into a single string,
emitted as the value of one `t:variables` multipart field.

**Rationale**:
- The clarified contract is "arbitrary JSON-representable values supplied as a map of name → value"
  (FR-005a). `object?` values let `System.Text.Json` emit the correct JSON kind per value (string →
  `"..."`, number, bool, array, nested object), satisfying FR-005 without callers pre-serializing.
- One serialization call yields exactly one JSON object → exactly one `t:variables` field (FR-004),
  never one field per variable.
- `System.Text.Json` is already a referenced dependency (feature 003) and is the constitution's
  mandated JSON library (Principle I). No new dependency.
- Reflection-based serialization is safe here: the library does not enable trimming/AOT, so no
  IL2026/IL3050 warnings under warnings-as-errors.

**Alternatives considered**:
- `IDictionary<string, string>` (string-only values) — rejected by clarification Q2 (Option B); it
  would force callers to hand-serialize numbers/arrays/objects and risk producing non-JSON or
  double-encoded values.
- Caller supplies a pre-built JSON string (Q2 Option C) — rejected; pushes correctness onto the
  caller and makes "valid JSON of the expected shape" unverifiable at the library boundary.
- Manual JSON string building — rejected; error-prone (escaping) and duplicates what
  `System.Text.Json` does correctly.

**Note on serialization options**: default `JsonSerializerOptions` are used (no camelCase renaming;
variable names are emitted verbatim as supplied, since template variable names are caller-defined
and must reach the template engine unchanged). Property naming policy is therefore intentionally
**not** applied to the dictionary keys.

## Decision 2 — Where template fields and validation live

**Decision**: Add the template fields to the existing public `MailgunMessage`, and put all
template-aware validation and field emission in the existing internal
`MailgunMessageContent.Build(...)`. No new source files.

**Rationale**:
- `MailgunMessageContent` is already the single place that validates a message and composes the
  multipart body; templated sending is the same concern (wire-format construction). Keeping it there
  preserves one validation path and one place tests target.
- `MailgunnerClient.SendAsync` already calls `MailgunMessageContent.Build` and needs **no change**:
  the new fields ride along on the same `MultipartFormDataContent`, same endpoint, same parsing and
  error path. This is what makes plain sends provably unchanged (FR-010).

**Alternatives considered**:
- A separate `TemplatedMailgunMessage` subtype or a parallel builder — rejected; it would split the
  send surface, complicate `SendAsync`, and break the "one message type" simplicity for no benefit.

## Decision 3 — Body requirement and mutual exclusivity

**Decision**: Replace 003's "at least one body part" rule with "at least one body part **OR** a
template name", and add a rule that a template and inline body parts are **mutually exclusive**.
Both checks throw `ArgumentException` before any request (FR-003, FR-003a). Template data
(`t:variables`/`t:version`/`t:text` request) supplied **without** a template name also throws
`ArgumentException`.

**Rationale**:
- Matches clarification Q1 (Option A): a message is either templated or inline, never both — an
  unambiguous, fail-fast contract consistent with 003's validation philosophy.
- Rejecting "template data without a name" prevents silently emitting `t:*` fields the service
  cannot apply, which would otherwise produce confusing server-side behavior.
- Keeps `MailgunnerException` reserved for actual HTTP responses (Principle IV; carried from 003).

**Validation order (within `Build`)**: sender present → at least one recipient → body rule
(template XOR body, and at least one of them) → template-name-required-when-template-data-present.
The exact messages are caller-actionable and name `nameof(message)`.

**Alternatives considered**:
- Allow both and let Mailgun decide (Q1 Option B) / silently drop the body (Q1 Option C) — rejected
  by clarification; both hide caller mistakes.

## Decision 4 — Conditional emission of `t:version`, `t:text`, `t:variables`

**Decision**:
- `template` → emitted whenever a non-blank template name is set.
- `t:version` → emitted only when `TemplateVersion` is non-null and not whitespace; a blank version
  is treated as "no version pinned" and omitted (FR-007, edge case).
- `t:text` → emitted as the literal value `yes` only when `GenerateTextFromTemplate` is `true`;
  omitted entirely otherwise (no `no`/`off` value) (FR-008, edge case).
- `t:variables` → emitted only when the variables map is non-null and **non-empty**; an empty map is
  treated identically to "no variables" and omits the field (FR-006, clarification Q3).

**Rationale**: Minimal, deterministic wire output makes the offline assertions exact (a field is
either present with a known value or absent), and matches Mailgun's documented `t:` flags. `yes` is
Mailgun's documented affirmative for `t:text`.

**Alternatives considered**:
- Emit `t:variables: {}` for an empty map (Q3 Option B) — rejected by clarification; carries no
  information and adds a second "empty" representation to test.
- Emit `t:text: no` when not requested — rejected; the field's absence is the documented "off".

## Decision 5 — Test strategy for the variables payload

**Decision**: Reuse the existing `StubHttpMessageHandler`; it already exposes `LastFormData` as an
ordered `IReadOnlyList<FormField>(Name, Value)`. Tests locate the `t:variables` field, `JsonDocument.Parse`
its value, and assert the root is a JSON object with the expected keys and value kinds (string vs
number vs nested object). Presence/absence of `template`/`t:version`/`t:text` is asserted by
inspecting field names. No changes to the fake are required.

**Rationale**: Satisfies FR-011 ("the captured template-variables payload MUST be assertable as
valid JSON of the expected shape") and SC-002 by parsing rather than brittle string comparison,
which is robust to property ordering and whitespace.

**Alternatives considered**:
- String-equality assertion on the serialized JSON — rejected; brittle to key ordering and spacing.
- Extending the fake to pre-parse JSON — rejected; unnecessary, the raw captured value is enough.
