# Phase 1 Data Model: Send a Templated Email

This feature adds **members to an existing type** and introduces **no new public type**. The model
below describes the additions to `MailgunMessage` and the validation rules enforced by the internal
`MailgunMessageContent` builder.

## Modified entity: `MailgunMessage` (public, `Mailgunner`)

Existing members (from feature 003) are unchanged: `From`, `To`, `Cc`, `Bcc`, `Subject`, `Text`,
`Html`. New members:

| Member | Type | Default | Meaning |
|--------|------|---------|---------|
| `Template` | `string?` | `null` | Name of the server-side stored template to render. When non-blank, the message is a **templated** send and an inline body is not required (and not allowed). |
| `TemplateVersion` | `string?` | `null` | Optional pinned template version. Emitted as `t:version` only when non-blank; otherwise the service's active version is used. |
| `GenerateTextFromTemplate` | `bool` | `false` | When `true`, asks the service to generate a plain-text part from the template (emits `t:text=yes`). When `false`, no `t:text` field is emitted. |
| `TemplateVariables` | `IDictionary<string, object?>` | empty `Dictionary<string, object?>` (get-only, pre-initialized) | Global variables applied to the whole send. Serialized **once** into a single JSON object emitted as `t:variables`. Values may be any JSON-representable type. Emitted only when non-empty. |

**Conventions (consistent with existing members):**
- `TemplateVariables` uses the same get-only, pre-initialized collection **style** as `To`/`Cc`/`Bcc`
  (those are `IList<EmailAddress>`; this is an `IDictionary<string, object?>` — the analogy is the
  property style, not the type), so callers do `message.TemplateVariables["k"] = v`.
- `Template`, `TemplateVersion` are settable `string?` like `Subject`/`Text`/`Html`.
- `GenerateTextFromTemplate` is a settable `bool`.
- Every new member carries XML doc comments (Principle IV).

## Derived classification

A message is exactly one of:
- **Inline** — has at least one of `Text`/`Html`, and no `Template`.
- **Templated** — has a non-blank `Template`, and no `Text`/`Html`.

"Has a body part" means `Text` or `Html` is non-empty. "Is templated" means `Template` is non-blank
(`!string.IsNullOrWhiteSpace`).

## Validation rules (enforced in `MailgunMessageContent.Build`, throw `ArgumentException` pre-request)

In order:

1. **Null message** → `ArgumentNullException` (unchanged from 003).
2. **Sender required** → `From.Address` non-blank, else `ArgumentException` (unchanged).
3. **At least one recipient** across To/Cc/Bcc (non-blank address), else `ArgumentException` (unchanged).
4. **Body requirement (revised, FR-003)** → must be inline (has `Text`/`Html`) **OR** templated
   (non-blank `Template`); a message that is neither → `ArgumentException`.
5. **Mutual exclusivity (new, FR-003a)** → must **not** be both: if `Template` is non-blank **and**
   (`Text` or `Html` is non-empty) → `ArgumentException`.
6. **Template name required when template data present (new)** → if `TemplateVersion` is non-blank,
   or `GenerateTextFromTemplate` is `true`, or `TemplateVariables` is non-empty, then `Template`
   must be non-blank → else `ArgumentException`.

All messages name `nameof(message)` and are caller-actionable English sentences. `MailgunnerException`
is **not** used for any of these (reserved for HTTP responses).

## Wire mapping (emitted by `MailgunMessageContent.Build` into `multipart/form-data`)

Existing fields (`from`, repeated `to`/`cc`/`bcc`, `subject`, `text`, `html`) are unchanged.
Template fields, emitted **after** the existing fields:

| Field | Emitted when | Value |
|-------|--------------|-------|
| `template` | `Template` is non-blank | the template name (trimmed of surrounding handling consistent with other string fields; emitted verbatim otherwise) |
| `t:version` | `TemplateVersion` is non-blank | the version string |
| `t:text` | `GenerateTextFromTemplate` is `true` | literal `yes` |
| `t:variables` | `TemplateVariables` is non-null and non-empty | `JsonSerializer.Serialize(TemplateVariables)` — a single JSON object keyed by variable name |

For an **inline** (plain) message, none of `template`/`t:version`/`t:text`/`t:variables` is emitted
(FR-010) — guaranteed because all four are gated on template-related state that a plain message
leaves at its default.

## `t:variables` JSON shape (the "expected shape", FR-005/SC-002)

- Root is a JSON **object**.
- Each key is a supplied variable name (verbatim, no naming-policy transformation).
- Each value is the JSON representation of the supplied value: string → JSON string, integral/real →
  JSON number, `bool` → JSON `true`/`false`, array/enumerable → JSON array, nested object/POCO/dictionary
  → JSON object, `null` → JSON `null`.

Example (illustrative): `{ "product": "Acme", "seats": 5, "trial": true, "owner": { "name": "Alice" } }`.
