# Implementation Plan: Suppression Lists Management (Bounces, Unsubscribes, Complaints)

**Branch**: `007-suppression-lists` (feature dir `007-suppression-lists`) | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/007-suppression-lists/spec.md`

## Summary

Add a new, sending-independent capability area: managing a domain's three Mailgun suppression lists —
**bounces**, **unsubscribes**, and **complaints**. Unlike the multipart sending pipeline (003–006),
these are **JSON** endpoints and large lists are read by following an **opaque pagination pointer**
(`paging.next`) returned with each page. The design surfaces the capability on the existing client via a
new `IMailgunnerClient.Suppressions` accessor that returns an `IMailgunSuppressions` exposing three typed
sub-lists — `Bounces`, `Unsubscribes`, `Complaints` — each an `ISuppressionList<TEntry>`. Each sub-list
offers: an auto-following `ListAsync` (the ergonomic default — an `IAsyncEnumerable<TEntry>` that
transparently follows the next pointer), a caller-driven single-page primitive `ListPageAsync` (returns a
`SuppressionPage<TEntry>` of items + opaque cursor) that the auto-follow is built on, a single-entry
`GetAsync(address)`, an `AddAsync(entry)` (JSON create carrying the address plus each type's optional
fields), a `RemoveAsync(address)` (single-address delete), and a `ClearAsync()` (delete-all). An optional
page size is applied to the **first** request only; every subsequent page is fetched by following the
service's next pointer unchanged. Responses deserialize via **`System.Text.Json` source generation** into
three distinct typed models (`Bounce` with code/error/created-at; `Unsubscribe` with tags/created-at;
`Complaint` with address/created-at). Failures surface the existing single `MailgunnerException` (status +
raw body); a not-found get/remove is a non-2xx that surfaces that same error. Everything is verified
offline against the existing fake transport, extended additively to serve per-request JSON page bodies
(via the existing `ResponseSelector` index hook) and to capture JSON request bodies for add/clear
assertions. No `src/` file from 002–006 is rewritten beyond adding the `Suppressions` accessor to the
client and its interface; the send paths, error contract, auth, and region routing carry over unchanged.

## Technical Context

**Language/Version**: C# (`LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`) — inherited from `Directory.Build.props`.

**Primary Dependencies**: **No new direct dependency.** `System.Text.Json` (already directly referenced,
pinned 10.0.9) provides typed (de)serialization via a source-generated `JsonSerializerContext`. The
auto-following `ListAsync` returns `IAsyncEnumerable<T>`; on `net8.0` this is intrinsic, and on
`netstandard2.0` the `IAsyncEnumerable`/`IAsyncEnumerator`/`IAsyncDisposable`/`EnumeratorCancellation`
types are supplied **transitively by `System.Text.Json`'s dependency on `Microsoft.Bcl.AsyncInterfaces`**
— i.e. already in the graph via an allowed package, adding nothing new (verified in research). `Polly`
remains provisioned-but-unwired (resilience still deferred — see Constitution Check).

**Storage**: N/A (library; no persistence).

**Testing**: xUnit, fully offline. The existing `StubHttpMessageHandler` is extended **additively** to
(a) capture each request's **raw body string** for non-multipart (JSON) requests — alongside the existing
multipart capture — so add/clear bodies and methods/URIs can be asserted, and (b) it already supports
per-request-index response selection (`ResponseSelector`), which is reused to serve a **sequence of JSON
page bodies** for multi-page listing. All existing members and multipart behavior are preserved. Tests
assert: a single page parses and stops with no follow-up; a 3-page list returns every item in order with
none dropped/duplicated and stops on the final (empty) page; an empty list yields zero items and no
follow-up; each list type deserializes into its distinct typed model; the optional page size appears as
the first request's limit and is **not** re-applied to followed next pointers; `GetAsync` returns the
typed model and a 404 surfaces `MailgunnerException`; `AddAsync` issues a JSON `POST` to the per-type
endpoint carrying the address (+ optional fields) with `application/json` content; `RemoveAsync` issues
`DELETE /{list}/{address}` and `ClearAsync` issues `DELETE /{list}`; non-2xx on any operation surfaces the
typed error; cancellation mid-pagination stops promptly; and the capability works with no send involved.

**Target Platform**: Cross-platform .NET. Library multi-targets `net8.0` and `netstandard2.0`; tests run on `net8.0`.

**Project Type**: Single class-library + test project (NuGet-distributable library).

**Performance Goals**: N/A. Listing is O(total entries) with constant per-page memory under the
auto-following path (one page held at a time; `IAsyncEnumerable` streams — it does not materialize the
whole list). Add/remove/clear/get are single requests.

**Constraints**: Offline tests; warnings-as-errors; XML docs on every public member; English-only;
multi-target compatible; sending key must never appear in a field/result/error; **JSON** request/response
bodies (distinct from sending's `multipart/form-data`); pagination pointer treated as **opaque** and
followed exactly; optional page size on first request only; suppressions are explicitly **in v1 scope**
per the constitution.

**Key environment facts (verified against the 002–006 code):**
- The typed client (`MailgunnerClient`) already holds the configured `HttpClient` (with region
  `BaseAddress` + Basic auth) and the trimmed `_domain`. The `Suppressions` accessor is constructed from
  exactly those two values — no new HTTP plumbing, auth, or region logic.
- The existing success/error contract is `response.IsSuccessStatusCode` → parse, else
  `throw new MailgunnerException((int)response.StatusCode, body)`. The suppressions operations reuse this
  exact shape (a shared internal send-JSON helper), so the single-typed-error guarantee holds.
- Response reading already branches on TFM for `ReadAsStringAsync(cancellationToken)` (net8) vs
  `ReadAsStringAsync()` (netstandard2.0); the suppressions code follows the same `#if NET8_0_OR_GREATER`
  pattern.
- JSON is currently parsed with `JsonDocument` (manual) for the small `SendResult`. For the richer
  suppression models the plan upgrades to a **source-generated** `JsonSerializerContext` (still
  `System.Text.Json` only, per Principle I) — trim/AOT-safe and netstandard2.0-friendly. The existing
  `SendResult` parsing is left untouched (no regression).
- Mailgun returns list pages as `{ "items": [ … ], "paging": { "next": "<url>", "previous", "first",
  "last" } }`. The `paging.next` URL is present even on the last page but that page's `items` is empty;
  therefore the stop condition is **`items` empty (or `next` absent)** — exactly the two conditions in
  FR-004. The next URL is followed as an **absolute** URI, opaque to the library.
- `created_at` arrives as an RFC-2822/RFC-1123-style timestamp (e.g. `Fri, 21 Oct 2011 11:02:55 GMT`);
  it is parsed to `DateTimeOffset` (assume-universal). This is the read-side inverse of 006's
  delivery-time formatting; the two do not share code.
- The fake transport's `ResponseSelector : Func<int,(HttpStatusCode,string)?>` already keys responses by
  zero-based request index — directly usable to return page 1, page 2, … for a paginated listing.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Constitution v1.1.0 (2026-06-22). Gates derived from its principles:

| Principle | Gate for this feature | Status |
|-----------|------------------------|--------|
| I. Minimal Dependencies & Modern .NET | Adds **no new direct dependency**. JSON via `System.Text.Json` source generation only (no `Newtonsoft.Json`). `IAsyncEnumerable` on `netstandard2.0` resolves via `Microsoft.Bcl.AsyncInterfaces` **already pulled transitively by the allowed `System.Text.Json`** — nothing new enters the graph (research-verified; if a direct `PackageReference` is ever required for clarity it pins the same transitive package, staying within the permitted catalog). | ✅ PASS |
| II. Managed HTTP & Resilience | Reuses the registered typed `HttpClient` (region base URL + Basic auth) via the existing client; every new public async method takes a `CancellationToken` and uses `ConfigureAwait(false)`; the auto-follow iterator honors `[EnumeratorCancellation]`. **Polly transient-fault handling remains unwired** (carried forward from 003–006). | ⚠️ PARTIAL — resilience deferred (tracked in Complexity Tracking) |
| III. Test-First, Network-Free (NON-NEGOTIABLE) | All behavior exercised through the fake `HttpMessageHandler`; default `dotnet test` stays green with no network/credentials. Adds the first JSON + pagination coverage; new behavior lands with tests. | ✅ PASS |
| IV. Documented, Strict Public API | New public types (`IMailgunSuppressions`, `ISuppressionList<TEntry>`, `SuppressionPage<TEntry>`, `Bounce`, `Unsubscribe`, `Complaint`) and the `Suppressions` member carry XML docs. Invalid input (null entry, blank address) → standard `ArgumentException` before any request; all API failures → the single `MailgunnerException`. Pre-1.0 additive surface, SemVer-safe; CHANGELOG (Unreleased) + README updated. | ✅ PASS |
| V. Security & Scope Discipline | Suppressions are explicitly inside the v1 scope ("messages, suppressions, and webhooks"). No secrets in code/tests; the sending key never appears in any model, field, or error. No out-of-scope endpoints touched. | ✅ PASS |
| Mailgun API Fidelity | Implements the constitution's bullet **"Provide suppressions list/add/delete for `bounces`, `unsubscribes`, and `complaints`"** verbatim, plus the in-scope single-entry get and clear-all. Uses `GET/POST/DELETE /v3/{domain}/{list}[/{address}]`, JSON bodies, Basic auth + region base URL from 002. | ✅ PASS |
| Dev Workflow & Quality Gates | Conventional Commits; `dotnet build`/`dotnet test` green offline; no secrets committed. | ✅ PASS |

**Result:** One justified, tracked deviation (Principle II resilience deferral, carried forward from
003–006). No new violations. See Complexity Tracking.

**Post-Phase-1 re-check:** The design adds one accessor on the existing client/interface, one public
suppressions facade, one generic public list interface, one page type, and three small data-only read
models, plus internal wire DTOs, a source-gen JSON context, a generic internal list implementation, and
an additive test-fake extension. It changes no existing send, error, recipient, or template logic, and
adds no dependency to the graph. No principle status changes. Gate still passes.

## Project Structure

### Documentation (this feature)

```text
specs/007-suppression-lists/
├── plan.md                          # This file (/speckit-plan output)
├── research.md                      # Phase 0 output (decisions: surface shape, async-stream on netstandard2.0,
│                                     #   source-gen JSON, pagination stop condition, created_at parsing, fake-transport reuse)
├── data-model.md                    # Phase 1 output (Bounce/Unsubscribe/Complaint + SuppressionPage + wire DTOs + rules)
├── quickstart.md                    # Phase 1 output (validation/run guide)
├── contracts/
│   └── suppressions-contract.md     # Phase 1 output (public surface + observable per-operation request/response contract)
├── checklists/
│   └── requirements.md              # Spec quality checklist (from /speckit-specify, re-validated by /speckit-clarify)
└── tasks.md                         # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
└── Mailgunner/
    ├── IMailgunnerClient.cs            # MODIFIED: add `IMailgunSuppressions Suppressions { get; }`
    ├── MailgunnerClient.cs             # MODIFIED: add `Suppressions` (lazily built from HttpClient + _domain);
    │                                    #           add a shared internal JSON send/parse helper (mirrors SendContentAsync's contract)
    ├── IMailgunSuppressions.cs         # NEW: facade exposing Bounces / Unsubscribes / Complaints
    ├── ISuppressionList.cs             # NEW: generic ISuppressionList<TEntry> — ListAsync (IAsyncEnumerable),
    │                                    #      ListPageAsync (page primitive: first + by-cursor), GetAsync, AddAsync, RemoveAsync, ClearAsync
    ├── SuppressionPage.cs              # NEW: SuppressionPage<TEntry> { Items, NextCursor (opaque), HasMore }
    ├── Bounce.cs                       # NEW: Address (required), Code?, Error?, CreatedAt? (DateTimeOffset)
    ├── Unsubscribe.cs                  # NEW: Address (required), Tags (IReadOnlyList<string>), CreatedAt?
    ├── Complaint.cs                    # NEW: Address (required), CreatedAt?
    └── Internal/
        ├── MailgunSuppressions.cs      # NEW: IMailgunSuppressions impl; constructs the three typed lists
        ├── MailgunSuppressionList.cs   # NEW: generic ISuppressionList<TEntry> impl over (HttpClient, domain, listSegment,
        │                                #      DTO↔model mapping, JSON type info) — pagination loop, get/add/remove/clear
        ├── SuppressionWireDtos.cs      # NEW: internal wire records (PageDto<T>, PagingDto, BounceDto, UnsubscribeDto, ComplaintDto)
        └── SuppressionJsonContext.cs   # NEW: [JsonSerializable] source-gen JsonSerializerContext for the DTOs / add bodies

    # (002–006 files otherwise unchanged: send paths, MailgunMessage(+Batch), options/content builders,
    #  MailgunnerException, EmailAddress, region, DI, Guard, SendResult all reused as-is)

tests/
└── Mailgunner.Tests/
    ├── Fakes/
    │   └── StubHttpMessageHandler.cs   # MODIFIED (additive): also capture raw request Body for non-multipart (JSON)
    │                                    #   requests (new optional CapturedRequest.Body / LastBody); existing members preserved
    └── Suppressions/
        ├── SuppressionListPaginationTests.cs # NEW US1: single page (no follow-up); 3 pages followed in order, no dup/skip;
        │                                       #          stop on empty final page; empty list → 0 items, no follow-up
        ├── SuppressionModelTests.cs           # NEW US1: each list type deserializes into its distinct typed model fields
        ├── SuppressionPageSizeTests.cs        # NEW FR-015: optional page size on first request only; not re-applied to next pointer
        ├── SuppressionPagePrimitiveTests.cs   # NEW US1: ListPageAsync returns items + opaque cursor; caller-driven follow
        ├── SuppressionGetTests.cs             # NEW FR-017: GetAsync returns model; 404 → MailgunnerException
        ├── SuppressionAddTests.cs             # NEW US2: POST JSON to per-type endpoint w/ address + optional fields; app/json
        ├── SuppressionRemoveClearTests.cs     # NEW US3: DELETE /{list}/{address} single; DELETE /{list} clear
        ├── SuppressionErrorTests.cs           # NEW FR-012: non-2xx on list/get/add/remove/clear → typed error (status+body)
        ├── SuppressionCancellationTests.cs    # NEW FR-013: cancel mid-pagination stops without fetching further pages
        └── SuppressionIndependenceTests.cs    # NEW FR-011: capability usable with no send path involved

        # existing 002–006 tests remain and must still pass unchanged
```

**Structure Decision**: Continue the established single-library layout. Because list/get/remove/clear are
uniform across the three list types while only the entry shape (and add fields) differ, the capability is
modeled as **one generic `ISuppressionList<TEntry>`** exposed three times (`Bounces`/`Unsubscribes`/
`Complaints`) through a small `IMailgunSuppressions` facade reached via `IMailgunnerClient.Suppressions`.
This keeps the public surface minimal (one generic interface + one page type + three plain read models)
while giving each type its own typed model — the read entry doubles as the add input, with its
server-populated `CreatedAt` ignored on add. All pagination, JSON, and HTTP logic is centralized in the
internal `MailgunSuppressionList<TEntry, TDto, TAddDto>` (implementing the public `ISuppressionList<TEntry>`, with a source-gen JSON context and internal wire DTOs kept off
the public surface), so the three lists share one implementation and the existing client gains only the
`Suppressions` accessor and a shared JSON send/parse helper. The test fake is extended additively to
capture JSON request bodies and (via its existing index-keyed `ResponseSelector`) to serve multi-page
responses — no new fake type and no rewrite of the multipart capture.

## Complexity Tracking

> One justified deviation from Principle II (resilience), carried forward from features 003–006.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Polly transient-fault retry still not wired (Principle II) | This feature adds new request paths on the existing typed client but does not change the transport or the error contract; wiring retries is an orthogonal cross-cutting concern best applied once for the whole client. | Adding Polly now is unrelated to suppressions and no simpler. Resilience remains layerable later as a `DelegatingHandler` on the already-registered typed client **without changing suppressions or send code**, so deferring incurs no rework. Polly stays pinned and ready. |
