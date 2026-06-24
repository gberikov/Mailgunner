# Contract: Suppression Lists (Bounces, Unsubscribes, Complaints)

This contract fixes the **public surface** and the **observable per-operation HTTP behavior** for the
suppressions capability. `{base}` is the region base URL from feature 002 (US `https://api.mailgun.net`,
EU `https://api.eu.mailgun.net`); `{domain}` is the configured sending domain; `{list}` is one of
`bounces`, `unsubscribes`, `complaints`. All requests carry the existing HTTP Basic auth header
(`api:<sending-key>`). Bodies are **JSON** (`application/json`), in contrast to the multipart sends.

## Public API surface (additive; SemVer-safe, pre-1.0)

```csharp
namespace Mailgunner;

public interface IMailgunnerClient
{
    // … existing SendAsync / SendBatchAsync …
    IMailgunSuppressions Suppressions { get; }            // NEW
}

public interface IMailgunSuppressions
{
    ISuppressionList<Bounce> Bounces { get; }
    ISuppressionList<Unsubscribe> Unsubscribes { get; }
    ISuppressionList<Complaint> Complaints { get; }
}

public interface ISuppressionList<TEntry>
{
    IAsyncEnumerable<TEntry> ListAsync(int? pageSize = null, CancellationToken cancellationToken = default);
    Task<SuppressionPage<TEntry>> ListPageAsync(int? pageSize = null, CancellationToken cancellationToken = default);
    Task<SuppressionPage<TEntry>> ListPageAsync(string cursor, CancellationToken cancellationToken = default);
    Task<TEntry> GetAsync(string address, CancellationToken cancellationToken = default);
    Task AddAsync(TEntry entry, CancellationToken cancellationToken = default);
    Task RemoveAsync(string address, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class SuppressionPage<TEntry>
{
    public IReadOnlyList<TEntry> Items { get; }
    public string? NextCursor { get; }   // opaque
    public bool HasMore { get; }
}

public sealed class Bounce      { public string Address {get;init;} public string? Code {get;init;}
                                  public string? Error {get;init;} public DateTimeOffset? CreatedAt {get;init;} }
public sealed class Unsubscribe { public string Address {get;init;} public IReadOnlyList<string> Tags {get;init;}
                                  public DateTimeOffset? CreatedAt {get;init;} }
public sealed class Complaint   { public string Address {get;init;} public DateTimeOffset? CreatedAt {get;init;} }
```

## Operation → request contract

| Operation | HTTP request | Success | Failure |
|-----------|--------------|---------|---------|
| `ListPageAsync(pageSize?)` (first) | `GET {base}/v3/{domain}/{list}` plus `?limit={pageSize}` **iff** pageSize supplied | 2xx JSON `{ items, paging }` → page parsed into `Items` + `NextCursor = paging.next` | non-2xx → `MailgunnerException(status, body)` |
| `ListPageAsync(cursor)` (follow) | `GET {cursor}` (the opaque next URL, verbatim; no added params) | as above | as above |
| `ListAsync(pageSize?)` | first request as the first-page rule, then repeatedly `GET paging.next` | yields every page's items in order; **stops when a page's `items` is empty or `next` is absent** | first non-2xx → `MailgunnerException` |
| `GetAsync(address)` | `GET {base}/v3/{domain}/{list}/{address}` | 2xx JSON single object → typed model | non-2xx (incl. **404 not-found**) → `MailgunnerException` |
| `AddAsync(entry)` | `POST {base}/v3/{domain}/{list}` with `application/json` body carrying `address` + the type's optional fields | 2xx → returns (success) | non-2xx → `MailgunnerException` |
| `RemoveAsync(address)` | `DELETE {base}/v3/{domain}/{list}/{address}` | 2xx → returns | non-2xx (incl. 404) → `MailgunnerException` |
| `ClearAsync()` | `DELETE {base}/v3/{domain}/{list}` (no address) | 2xx → returns | non-2xx → `MailgunnerException` |

### Add JSON body shape (only non-null fields emitted)

```text
bounces:       { "address": "a@b.com", "code": "550", "error": "No such mailbox" }
unsubscribes:  { "address": "a@b.com", "tags": ["newsletter"] }      # tags omitted when empty
complaints:    { "address": "a@b.com" }
```

## Response shape (list page)

```json
{
  "items": [
    { "address": "a@b.com", "code": "550", "error": "No such mailbox", "created_at": "Fri, 21 Oct 2011 11:02:55 GMT" }
  ],
  "paging": {
    "next": "https://api.mailgun.net/v3/<domain>/bounces?page=next&address=a%40b.com",
    "previous": "…", "first": "…", "last": "…"
  }
}
```

- Last page: `items` is `[]` (empty) even though `paging.next` is still present → enumeration stops.
- `created_at` parses to `DateTimeOffset?` (assume-universal); unparseable/absent → `null`, never an error.

## Observable invariants (assertable offline via the fake transport)

1. **Single page**: a list whose first page has no further entries (empty `items` on the *next* fetch, or
   `next` absent) yields exactly that page's items and issues no extra request beyond the stop probe per
   the stop rule. (SC-001)
2. **Multi-page**: a 3-page sequence yields all items across pages in order, none dropped or duplicated,
   and stops after the empty trailing page. (SC-002)
3. **Empty list**: first page `items` empty → zero items, no follow-up. (SC-003)
4. **Typed models**: each list type's items populate that type's distinct fields. (SC-004)
5. **Page size**: with `pageSize = N`, the first request URI contains `limit=N`; followed `next` URLs are
   issued verbatim with no library-added `limit`. (FR-015)
6. **Get**: `GetAsync` on a present address returns the typed model; a 404 throws `MailgunnerException`
   with `StatusCode == 404` and the raw body. (SC-008)
7. **Add**: `AddAsync` issues `POST {list}` with `Content-Type: application/json` and a body containing the
   address (+ supplied optional fields). (SC-005)
8. **Remove / clear**: `RemoveAsync` issues `DELETE {list}/{address}` (address in the path);
   `ClearAsync` issues `DELETE {list}` (no address segment). (SC-006)
9. **Errors**: any non-2xx on list/get/add/remove/clear surfaces `MailgunnerException(status, body)`;
   the sending key never appears in any field, model, or error. (SC-007)
10. **Independence**: every assertion above is reachable through `client.Suppressions.*` with no send call
    made. (SC-009, FR-011)
11. **Cancellation**: cancelling during `ListAsync` enumeration stops before the next page is fetched.
    (FR-013)
