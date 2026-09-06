# Suppression lists
[← All Mailgunner docs](https://github.com/gberikov/Mailgunner/blob/master/README.md#documentation)

Mailgun maintains three suppression lists per domain — **bounces**, **unsubscribes**, and
**complaints** — and Mailgunner exposes them through `client.Suppressions`. Unlike sending, these are
JSON endpoints, and they are completely independent of the send pipeline. Each list
(`Suppressions.Bounces`, `Suppressions.Unsubscribes`, `Suppressions.Complaints`) offers the same set of
operations over its own typed entry (`Bounce`, `Unsubscribe`, `Complaint`):

```csharp
// List every entry — pagination is followed transparently (streams large lists).
await foreach (Bounce b in client.Suppressions.Bounces.ListAsync(cancellationToken: ct))
{
    Console.WriteLine($"{b.Address} {b.Code} {b.CreatedAt:u}");
}

// Optional page size cuts round-trips on big lists (applied to the first request only).
await foreach (Unsubscribe u in client.Suppressions.Unsubscribes.ListAsync(pageSize: 1000, cancellationToken: ct)) { }

// Caller-driven paging via the single-page primitive and its opaque cursor.
SuppressionPage<Complaint> page = await client.Suppressions.Complaints.ListPageAsync(cancellationToken: ct);
while (page.HasMore)
{
    page = await client.Suppressions.Complaints.ListPageAsync(page.NextCursor!, ct);
}

await client.Suppressions.Unsubscribes.AddAsync(
    new Unsubscribe { Address = "user@example.com", Tags = new[] { "newsletter" } }, ct);
Bounce one = await client.Suppressions.Bounces.GetAsync("user@example.com", ct); // 404 → MailgunnerException
await client.Suppressions.Bounces.RemoveAsync("user@example.com", ct);            // remove one address
await client.Suppressions.Complaints.ClearAsync(ct);                              // clear the whole list
```

- **`ListAsync`** is the ergonomic default: it returns an `IAsyncEnumerable<T>` and follows the service's
  next pointer across pages until the list is exhausted. **`ListPageAsync`** returns one
  `SuppressionPage<T>` (its `Items` plus an opaque `NextCursor`) for callers that drive paging themselves.
- An optional **page size** is applied only to the first request; subsequent pages follow the service's
  next pointer verbatim.
- **`AddAsync`** sends the address plus that list's optional fields (a bounce's `Code`/`Error`, an
  unsubscribe's `Tags`) as JSON. **`AddRangeAsync`** sends many entries as a JSON array (chunked by 1000
  per request). **`RemoveAsync`** deletes a single address; **`ClearAsync`** deletes every entry on the
  list.
- Any non-success response — including a not-found `GetAsync`/`RemoveAsync` — surfaces a
  `MailgunnerException` carrying the HTTP status code and raw response body.
- Malformed success responses also throw `MailgunnerException`: a missing `items` array or an entry
  without an address is a protocol error, not an empty list. A valid `items: []` is an empty page.

