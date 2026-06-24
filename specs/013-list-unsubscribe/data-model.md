# Phase 1 Data Model: One-Click List-Unsubscribe

The feature adds one public type and one property; no persistence or wire DTO is involved beyond the
emitted multipart fields.

## Entity: `ListUnsubscribeOptions` (new public type)

An opt-in declaration of how recipients can unsubscribe from a send. Attached to a send via
`MailgunSendOptions.ListUnsubscribe`.

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `Url` | `string?` | Conditionally | `null` | The `https` unsubscribe endpoint. Required when `OneClick` is true. When set, must be an absolute `https` URI free of control characters / line breaks. Emitted verbatim inside `<…>`. |
| `MailtoAddress` | `EmailAddress?` | No | `null` | The unsubscribe email address (bare; library forms `mailto:`). Reuses `EmailAddress` validation; only `Address` is used (display name ignored). |
| `OneClick` | `bool` | No | `false` | When true, also emits `List-Unsubscribe-Post: List-Unsubscribe=One-Click`. Requires a valid `Url`. |

### Validity rules (enforced at request-build time, before any HTTP call)

1. **Non-empty target**: at least one of `Url` or `MailtoAddress` must be present; a target object with
   neither (or a blank/whitespace `Url` and no mailto) is rejected → `ArgumentException`.
2. **HTTPS URL**: when `Url` is present it must parse as an absolute URI with scheme `https`
   (ordinal-ignore-case); any other scheme (e.g. `http`) is rejected → `ArgumentException`.
3. **No injection**: `Url` must contain no control characters or line breaks → `ArgumentException`.
   (`MailtoAddress` is already control-character-validated by the `EmailAddress` constructor at
   assignment time.)
4. **One-click requires URL**: when `OneClick` is true, `Url` must be present and valid `https`;
   otherwise rejected → `ArgumentException`.
5. **No duplicate header**: if `CustomHeaders` contains a key equal (ordinal-ignore-case) to
   `List-Unsubscribe` or `List-Unsubscribe-Post`, the send is rejected → `ArgumentException`.

### Relationships

- `MailgunSendOptions.ListUnsubscribe : ListUnsubscribeOptions?` — new nullable property. `null` (the
  default) emits nothing; existing sends are unaffected.
- Reuses the existing `EmailAddress` struct for the mailto form.
- Shares the existing `MailgunSendOptions.CustomHeaders` dictionary for conflict detection.

### State / lifecycle

Stateless value holder. No transitions. Validated each time a request body is built (single, templated,
and batch — the same `MailgunOptionsContent.Append` path), so a batch repeats the identical header(s) on
every chunk.

## Emitted multipart fields (output, not stored)

| Condition | Field name | Field value |
|-----------|-----------|-------------|
| `Url` set | `h:List-Unsubscribe` | `<URL>` (or combined with mailto, URL first) |
| `MailtoAddress` set | (same single `h:List-Unsubscribe`) | `<mailto:ADDRESS>` appended after URL with `", "` separator when both present |
| `OneClick` true | `h:List-Unsubscribe-Post` | `List-Unsubscribe=One-Click` |
| `ListUnsubscribe` null | (none) | (no fields emitted) |

Examples:

- URL + one-click → `h:List-Unsubscribe = <https://x/u?id=1>` and
  `h:List-Unsubscribe-Post = List-Unsubscribe=One-Click`
- mailto only → `h:List-Unsubscribe = <mailto:unsub@example.com>` (no `-Post`)
- both, not one-click → `h:List-Unsubscribe = <https://x/u?id=1>, <mailto:unsub@example.com>`
