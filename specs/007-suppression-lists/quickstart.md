# Quickstart & Validation Guide: Suppression Lists

This guide shows how to use the suppressions capability and how to validate it offline. Implementation
details (model bodies, the generic list, the JSON context) live in `data-model.md` / `contracts/` and the
tasks; this file is the run/verify guide.

## Prerequisites

- Repo builds and the existing suite is green: `dotnet test` (offline, no credentials — Constitution III).
- Feature 002 registration in place (`AddMailgunner(...)`), which configures region base URL + Basic auth.

## Usage (consumer perspective)

```csharp
IMailgunnerClient client = /* resolved from DI (feature 002) */;

// 1) List every bounce, transparently following pagination (the ergonomic default).
await foreach (Bounce b in client.Suppressions.Bounces.ListAsync(cancellationToken: ct))
{
    Console.WriteLine($"{b.Address} {b.Code} {b.CreatedAt:u}");
}

// 1b) Larger pages to cut round-trips on a big list (applied to the first request only).
await foreach (Unsubscribe u in client.Suppressions.Unsubscribes.ListAsync(pageSize: 1000, cancellationToken: ct))
{
    // u.Address, u.Tags …
}

// 1c) Caller-driven paging via the single-page primitive.
SuppressionPage<Complaint> page = await client.Suppressions.Complaints.ListPageAsync(cancellationToken: ct);
while (page.HasMore)
{
    foreach (var c in page.Items) { /* … */ }
    page = await client.Suppressions.Complaints.ListPageAsync(page.NextCursor!, ct);
}

// 2) Add — record an unsubscribe captured on your own preference page.
await client.Suppressions.Unsubscribes.AddAsync(
    new Unsubscribe { Address = "user@example.com", Tags = new[] { "newsletter" } }, ct);

// 3a) Remove a single address (e.g. clear a fixed bounce).
await client.Suppressions.Bounces.RemoveAsync("user@example.com", ct);

// 3b) Get one entry; a missing address throws MailgunnerException (404).
Bounce one = await client.Suppressions.Bounces.GetAsync("user@example.com", ct);

// 3c) Clear an entire list.
await client.Suppressions.Complaints.ClearAsync(ct);
```

## Offline validation scenarios

All run against `StubHttpMessageHandler` (extended additively) — **no network, no credentials**. Map each
to the contract invariants and spec acceptance criteria.

| # | Scenario | Setup | Assert |
|---|----------|-------|--------|
| 1 | Single page | Stub returns one page (`items` filled, then an empty page) | `ListAsync` yields exactly the page's items; stops on the empty page (SC-001) |
| 2 | Multi-page | `ResponseSelector`: index 0→page1(+next), 1→page2(+next), 2→empty | all items in order, none dropped/dup'd; stops after empty page (SC-002) |
| 3 | Empty list | Stub returns `{items:[],paging:{…}}` | zero items; only one request issued (SC-003) |
| 4 | Typed models | Page bodies for each list type | `Bounce.Code/Error/CreatedAt`, `Unsubscribe.Tags/CreatedAt`, `Complaint.Address/CreatedAt` populated (SC-004) |
| 5 | Page size | `ListAsync(pageSize: 250)` | first captured request URI contains `limit=250`; followed `next` URL issued verbatim (no added `limit`) (FR-015) |
| 6 | Page primitive | `ListPageAsync()` then `ListPageAsync(cursor)` | first returns items + `NextCursor`; follow GETs the cursor URL verbatim |
| 7 | Get found / 404 | 200 single body / 404 body | returns typed model / throws `MailgunnerException` with `StatusCode==404` and raw body (SC-008) |
| 8 | Add | `AddAsync(entry)` per type | captured request: `POST {list}`, `Content-Type: application/json`, body has `address` (+ optional fields) (SC-005) |
| 9 | Remove / clear | `RemoveAsync(addr)` / `ClearAsync()` | `DELETE {list}/{addr}` (address in path) / `DELETE {list}` (no address) (SC-006) |
| 10 | Errors | non-2xx for list/get/add/remove/clear | each throws `MailgunnerException(status, body)`; sending key absent from all captures (SC-007) |
| 11 | Independence | only `client.Suppressions.*` exercised | all of the above pass with no send call (SC-009, FR-011) |
| 12 | Cancellation | cancel during `ListAsync` enumeration | enumeration stops before the next page is fetched; `OperationCanceledException` (FR-013) |

## Commands

```bash
dotnet build          # must be clean (warnings-as-errors); netstandard2.0 + net8.0 both compile
dotnet test           # must be green offline (no Mailgun credentials)
```

## Done signals

- New `tests/Mailgunner.Tests/Suppressions/*` cover scenarios 1–12; all green.
- Existing 002–006 tests still pass unchanged.
- README gains a "Suppressions" section; CHANGELOG `[Unreleased] → Added` notes the capability.
