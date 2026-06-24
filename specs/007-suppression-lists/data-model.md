# Phase 1 Data Model: Suppression Lists Management

All names below are the proposed public/internal C# shapes. Public types carry XML docs and live in
namespace `Mailgunner`; internal wire DTOs live in `Mailgunner.Internal` and never appear on the public
surface. Multi-target rules: nullable enabled; `IAsyncEnumerable`/`IReadOnlyList` available on both TFMs
(see research D2).

## Public read models (one per list type)

Each model is the typed result of parsing a list/get response **and** the input to `AddAsync` (the
server-populated `CreatedAt` is ignored on add — see add rules). All are plain classes with `get; init;`
properties; `Address` is required and non-blank.

### `Bounce`
| Member | Type | Notes |
|--------|------|-------|
| `Address` | `string` | Required, non-blank. The bounced recipient address. |
| `Code` | `string?` | The SMTP/bounce failure code (e.g. `"550"`); optional on add. |
| `Error` | `string?` | Human-readable failure detail; optional on add. |
| `CreatedAt` | `DateTimeOffset?` | Server-set on read (parsed per research D6); ignored on add. |

### `Unsubscribe`
| Member | Type | Notes |
|--------|------|-------|
| `Address` | `string` | Required, non-blank. |
| `Tags` | `IReadOnlyList<string>` | Tags the address unsubscribed from; defaults to empty; on add, sent only when non-empty. `"*"` means all. |
| `CreatedAt` | `DateTimeOffset?` | Server-set on read; ignored on add. |

### `Complaint`
| Member | Type | Notes |
|--------|------|-------|
| `Address` | `string` | Required, non-blank. |
| `CreatedAt` | `DateTimeOffset?` | Server-set on read; ignored on add. |

> The three models are distinct types (FR-006); an entry from one list never deserializes into another's
> shape. A `Complaint` has no `Code`/`Error`; an `Unsubscribe` carries `Tags`; a `Bounce` carries
> `Code`/`Error`.

## Public page primitive

### `SuppressionPage<TEntry>`
| Member | Type | Notes |
|--------|------|-------|
| `Items` | `IReadOnlyList<TEntry>` | The parsed entries on this page, in service order; possibly empty. |
| `NextCursor` | `string?` | **Opaque** pointer to the next page (the raw `paging.next` URL); `null`/absent at end. |
| `HasMore` | `bool` | `true` when this page is non-empty **and** `NextCursor` is present (drives the stop condition). |

## Public capability surface

### `IMailgunSuppressions` (facade)
| Member | Type | Notes |
|--------|------|-------|
| `Bounces` | `ISuppressionList<Bounce>` | The bounces list operations. |
| `Unsubscribes` | `ISuppressionList<Unsubscribe>` | The unsubscribes list operations. |
| `Complaints` | `ISuppressionList<Complaint>` | The complaints list operations. |

Reached via `IMailgunnerClient.Suppressions` (new get-only member on the existing interface).

### `ISuppressionList<TEntry>` (generic operations)
| Operation | Signature (CancellationToken omitted for brevity) | Maps to |
|-----------|---------------------------------------------------|---------|
| Auto-follow list | `IAsyncEnumerable<TEntry> ListAsync(int? pageSize = null)` | US1 default; FR-002(b), FR-015 |
| Single-page (first) | `Task<SuppressionPage<TEntry>> ListPageAsync(int? pageSize = null)` | US1 primitive; FR-002(a) |
| Single-page (follow) | `Task<SuppressionPage<TEntry>> ListPageAsync(string cursor)` | FR-003 (opaque follow) |
| Get one | `Task<TEntry> GetAsync(string address)` | FR-017 (404 → `MailgunnerException`) |
| Add | `Task AddAsync(TEntry entry)` | FR-007, FR-008 |
| Remove one | `Task RemoveAsync(string address)` | FR-009 |
| Clear all | `Task ClearAsync()` | FR-016 |

Every method takes a trailing `CancellationToken cancellationToken = default` and uses
`ConfigureAwait(false)`; `ListAsync` flows cancellation via `[EnumeratorCancellation]` (FR-013).

## Internal wire DTOs (`Mailgunner.Internal`, never public)

Mirror Mailgun JSON with `[JsonPropertyName]`; covered by the source-gen `SuppressionJsonContext`.

```text
PageDto<TItem>      { items: TItem[]?, paging: PagingDto? }
PagingDto           { next: string?, previous: string?, first: string?, last: string? }
BounceDto           { address: string?, code: string?, error: string?, created_at: string? }
UnsubscribeDto      { address: string?, tags: string[]?, created_at: string? }
ComplaintDto        { address: string?, created_at: string? }
AddBounceDto        { address, code?, error? }          # add bodies (only non-null fields serialized)
AddUnsubscribeDto   { address, tags? }
AddComplaintDto     { address }
```

DTO → model projection: copy `address`; map `code`/`error`/`tags` as present; parse `created_at` →
`DateTimeOffset?` (research D6). The generic `MailgunSuppressionList<TEntry, TDto, TAddDto>` (which
implements the public single-parameter `ISuppressionList<TEntry>`) is parameterized with the read DTO
type, the add-body DTO type, the DTO→model projection, and the add-body factory, so one implementation
serves all three lists.

## Validation rules (pre-request; standard `ArgumentException` family)

| Rule | Trigger | Exception |
|------|---------|-----------|
| Non-null entry on add | `AddAsync(null)` | `ArgumentNullException` |
| Non-blank address on add | `entry.Address` null/whitespace | `ArgumentException` |
| Non-blank address on get/remove | `address` null/whitespace | `ArgumentException` |
| Non-blank cursor on follow | `ListPageAsync(cursor)` cursor null/whitespace | `ArgumentException` |

`pageSize`, when supplied, is passed through as `limit`; the library does not clamp or validate the
numeric range (the service rejects an invalid value, surfaced as `MailgunnerException`).

## State / lifecycle

No client-side state. `MailgunSuppressions` and the three `MailgunSuppressionList<TEntry, TDto, TAddDto>` instances are
constructed from the client's existing `HttpClient` + trimmed domain and may be cached on the client.
Listing holds at most one page in memory at a time under `ListAsync`.

## Error model

Unchanged: every non-2xx response (including 404 on get/remove and any add/clear/list failure) throws the
single `MailgunnerException` exposing `StatusCode` (int) and `ResponseBody` (raw). The sending key never
appears in any model, request field, or error.
