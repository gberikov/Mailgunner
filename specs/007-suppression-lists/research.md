# Phase 0 Research: Suppression Lists Management

All decisions resolve the Technical Context to a buildable, constitution-compliant design. No
`NEEDS CLARIFICATION` remained from the spec (the five clarify answers fixed the API shape, page size,
remove scope, add fields, and single-entry lookup). The items below resolve the remaining *technical*
unknowns.

## D1 — Public surface shape: one generic list, three typed accessors

**Decision**: Expose the capability as `IMailgunnerClient.Suppressions` → `IMailgunSuppressions` with
three accessors `Bounces` / `Unsubscribes` / `Complaints`, each an `ISuppressionList<TEntry>` (generic).
The read entry model doubles as the `AddAsync` input (server-set `CreatedAt` ignored on add).

**Rationale**: List / get / remove / clear are byte-identical across the three list types — only the
entry shape and the optional add fields differ. A single generic `ISuppressionList<TEntry>` collapses six
operations × three types into one interface + three plain models, the minimum surface (Principle IV)
while still giving each type its own typed model (FR-006). Reaching it via a `Suppressions` accessor keeps
the capability discoverable and **independent of the send methods** (FR-011) — the interface doc already
anticipates "operational members (sending, suppressions, webhooks)".

**Alternatives considered**:
- *Bespoke per-type interfaces* (`IBounceList`/`IUnsubscribeList`/`IComplaintList` with tailored
  `AddAsync` signatures): more precise add ergonomics but triples the public interface count and the XML
  doc surface for no behavioral gain; pagination/HTTP logic is shared internally regardless. Rejected as
  heavier than Principle IV warrants.
- *Separate `*Create` input types* (`ISuppressionList<TEntry,TCreate>`): cleanly separates read vs write
  shape but doubles the model count (six types). Rejected; a nullable `CreatedAt` on the read model, unset
  on add, is simpler and idiomatic.
- *Flat methods on `IMailgunnerClient`* (`ListBouncesAsync`, …): ~18 methods on the core interface, poor
  cohesion, and couples suppressions to the send entry point. Rejected.

## D2 — Auto-following `ListAsync` as `IAsyncEnumerable<T>` across `net8.0` + `netstandard2.0`

**Decision**: `ListAsync(int? pageSize = null, CancellationToken)` returns `IAsyncEnumerable<TEntry>` and
streams entries page-by-page, transparently following `paging.next`. It is implemented as a C# async
iterator with `[EnumeratorCancellation]`, built on top of the single-page primitive `ListPageAsync`.

**Rationale**: `IAsyncEnumerable<T>` is the idiomatic .NET shape for "yield all entries without writing a
paging loop" and streams with constant per-page memory (handles tens of thousands of entries — the spec's
core large-list concern). **Verified**: `Microsoft.Bcl.AsyncInterfaces/10.0.9` (which supplies
`IAsyncEnumerable`/`IAsyncEnumerator`/`IAsyncDisposable`/`EnumeratorCancellationAttribute` on
`netstandard2.0`) is already present in the `netstandard2.0` restore graph **transitively via the allowed
`System.Text.Json`** — and `net8.0` has these intrinsically:

```text
project.assets.json targets:
  netstandard2.0 -> ['Microsoft.Bcl.AsyncInterfaces/10.0.9']   # transitive via System.Text.Json
  net8.0         -> []                                          # intrinsic
```

So the auto-follow path adds **no new dependency to the graph** (Principle I). The async iterator
compiles on both TFMs; cancellation flows through `[EnumeratorCancellation]`.

**Alternatives considered**:
- *`Task<IReadOnlyList<T>>` that aggregates all pages*: avoids any async-stream type but materializes the
  whole list in memory — contradicts the streaming-large-lists goal and the clarify answer (auto-following
  stream). Rejected. (The caller-driven `ListPageAsync` primitive remains available for those who want
  manual control.)
- *Adding a direct `Microsoft.Bcl.AsyncInterfaces` `PackageReference`*: unnecessary (already transitive)
  and would read like a new dependency. Kept implicit; if a future change ever needs it explicit, it pins
  the same package already in the catalog's transitive closure — still within Principle I.

## D3 — Typed JSON via `System.Text.Json` source generation + internal wire DTOs

**Decision**: Add a source-generated `JsonSerializerContext` (`SuppressionJsonContext`) over **internal
wire DTO records** (`PageDto<T>`, `PagingDto`, `BounceDto`, `UnsubscribeDto`, `ComplaintDto`) that mirror
Mailgun's JSON with `[JsonPropertyName]`. Deserialize into DTOs, then project to the public models
(`Bounce`/`Unsubscribe`/`Complaint`). Add bodies serialize from a small DTO too.

**Rationale**: Source generation is trim/AOT-safe, avoids reflection-based serialization warnings under
warnings-as-errors, and works cleanly on `netstandard2.0` — all `System.Text.Json` only (Principle I).
Keeping wire attributes on **internal** DTOs leaves the public models attribute-free and decoupled from
the wire format (Principle IV: clean strict surface). The existing `SendResult` `JsonDocument` parse is
left untouched (no regression; it is intentionally not migrated).

**Alternatives considered**:
- *`[JsonPropertyName]` directly on public models*: fewer types but leaks wire naming onto the public API
  and mixes read-only server fields with serialization concerns. Rejected for surface cleanliness.
- *Manual `JsonDocument` parsing* (as `SendResult` does): fine for one tiny object but verbose and
  error-prone across three models + a generic page envelope, and not reusable for the generic list.
  Rejected.

## D4 — Pagination: opaque next URL, stop on empty page

**Decision**: `SuppressionPage<TEntry>` carries `Items` and an **opaque** `NextCursor` (the raw
`paging.next` URL string) plus `HasMore`. The auto-follow loop fetches a page, yields its items, and
continues to the next URL **only while the page returned a non-empty `items`** (and `next` is present);
it stops on an empty page or a missing next pointer. `ListPageAsync` has two forms: first page
(`pageSize?`) and by-cursor (`ListPageAsync(string cursor)`), the latter issuing a GET to the cursor URL
verbatim.

**Rationale**: Mailgun always returns a `paging.next` URL — even past the end — but the trailing page's
`items` is empty; relying on `next` absence alone would loop forever. Stopping on an empty page (or absent
next) is exactly FR-004's two conditions and avoids the unbounded-loop edge case. Treating the cursor as
an opaque absolute URL satisfies FR-003 ("followed exactly as provided; library does not fabricate paging
params"). `HttpClient.GetAsync(absoluteUri)` works even with a `BaseAddress` set (absolute wins).

**Alternatives considered**:
- *Stop only when `next` is null*: would loop on Mailgun's always-present next. Rejected.
- *Parse the cursor out of the URL and re-issue against the base path*: fabricates paging params, violates
  FR-003, and couples to Mailgun's internal cursor format. Rejected.

## D5 — Page size applied to the first request only

**Decision**: When `pageSize` is supplied, the **first** list request is `GET /v3/{domain}/{list}?limit={pageSize}`.
Subsequent pages are fetched by following `paging.next` unchanged (the service echoes the effective limit
into the next URL). When omitted, the first request carries no `limit` and the service default applies.

**Rationale**: Satisfies FR-015 and FR-003's single exception. Mailgun's `next` URL already encodes the
limit, so re-applying it would both duplicate and risk fabricating a paging param.

**Alternatives considered**: re-appending `limit` to each followed URL — redundant and FR-003-violating.
Rejected.

## D6 — `created_at` parsing to `DateTimeOffset`

**Decision**: Parse `created_at` (e.g. `Fri, 21 Oct 2011 11:02:55 GMT`) with
`DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, …)`.
Expose `CreatedAt` as `DateTimeOffset?` (null when absent/unparseable rather than throwing — a malformed
timestamp must not fail an otherwise valid list parse).

**Rationale**: This is the read-side inverse of 006's RFC-2822 *formatting*; invariant + assume-universal
handles the `GMT`/offset forms Mailgun emits. Nullable-on-unparseable keeps listing robust (a single odd
row never breaks a page). The two date paths intentionally share no code (formatting vs parsing differ).

**Alternatives considered**: a strict exact-format parse that throws — too brittle for a third-party feed.
Rejected.

## D7 — Error contract reuse + input validation

**Decision**: All operations go through a shared internal JSON send/parse helper mirroring
`SendContentAsync`: success (2xx) parses the body; any non-2xx (including 404 on get/remove) throws
`MailgunnerException((int)status, body)`. Input guards run **before** any request: `AddAsync(null)` →
`ArgumentNullException`; blank `address` on add/get/remove → `ArgumentException`.

**Rationale**: Preserves the single-typed-error guarantee (Principle IV) and the "not-found surfaces the
typed error, not a null/empty success" requirement (FR-012, FR-017, SC-008). Pre-request `ArgumentException`
matches the established 003–006 validation style.

## D8 — Offline verification: reuse and additively extend the fake transport

**Decision**: Reuse `StubHttpMessageHandler`. Use its existing index-keyed `ResponseSelector` to return a
**sequence of JSON page bodies** (index 0 → page 1, 1 → page 2, …) for multi-page listing. Extend it
**additively** to also record each request's **raw body string** for non-multipart content (new optional
`CapturedRequest.Body` / `LastBody`), so add/clear JSON bodies, methods, and URIs can be asserted. All
existing members, multipart capture, and the positional `FormField` ctor stay intact.

**Rationale**: Mirrors 006's additive fake extension; no second fake type, and the existing per-index
response hook is already exactly what multi-page listing needs (Principle III, no network).

**Alternatives considered**: a separate `JsonStubHttpMessageHandler` — duplicate plumbing for cancellation
and capture. Rejected in favor of one additive extension.
