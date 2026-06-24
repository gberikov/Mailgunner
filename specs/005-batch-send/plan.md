# Implementation Plan: Personalized Mass Send (Batched Recipient Variables)

**Branch**: `005-batch-send` (feature dir `005-batch-send`) | **Date**: 2026-06-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/005-batch-send/spec.md`

## Summary

Deliver the library's headline capability: send **one stored-template email to a large recipient
list**, each recipient receiving their own personalized values, in the fewest possible requests.
A consumer builds a `MailgunBatchMessage` (sender, subject, template + optional version/generated-
text, optional **global** `t:variables`, and an ordered list of `BatchRecipient` entries — each an
address plus its own variables) and calls a new `IMailgunnerClient.SendBatchAsync(...)`. The client
**chunks** the recipient list into consecutive slices of at most **1000** (ceil(N/1000) requests),
and for each chunk POSTs `multipart/form-data` to `v3/{domain}/messages` carrying: `from`, one
repeated `to` part per recipient in the chunk, the reused `template`/`t:version`/`t:text`/`t:variables`
(global) fields, and a single **`recipient-variables`** field — one `System.Text.Json` object keyed
by recipient email address whose value is that recipient's variables. Putting recipients in `to`
together with `recipient-variables` is exactly what makes Mailgun deliver an individual message to
each person (each sees only their own address). Sending is **fail-fast**: chunks go out sequentially
and the first non-2xx throws the single `MailgunnerException`; the method returns
`IReadOnlyList<SendResult>` (one per chunk sent). An **empty** recipient list is a no-op returning an
empty list; a **duplicate** address throws `ArgumentException` before any request. Verified entirely
offline by extending the existing fake transport to record **all** requests, asserting request count,
per-chunk recipient membership, and the `recipient-variables` JSON shape.

## Technical Context

**Language/Version**: C# (`LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`) — inherited from `Directory.Build.props`.

**Primary Dependencies**: **No new dependency.** `System.Text.Json` (pinned 10.0.9) serializes the
`recipient-variables` and global `t:variables` objects. `Microsoft.Extensions.Http` + the typed
client registered in feature 002 are reused. `Polly` remains provisioned-but-unwired (resilience
still deferred — see Constitution Check).

**Storage**: N/A (library; no persistence).

**Testing**: xUnit, fully offline. The existing `StubHttpMessageHandler` is extended **additively**
to record *every* request (each request's captured multipart fields, URI, method, media type) and to
allow a per-request-index response selector (so a test can make, e.g., the 2nd chunk return 500 to
exercise fail-fast). Existing `Last*`/`LastFormData` members are preserved so all 003/004 sending
tests stay green. Tests assert: request count for 0/1000/2000/2500 recipient lists; the 2500 split is
1000/1000/500; per-chunk `to` membership and order; `recipient-variables` parses to a JSON object
keyed by email with each recipient's own values; global `template`/`t:variables` repeat identically
across chunks; duplicate address → `ArgumentException` (no request); empty list → zero requests;
cancellation stops further chunks; non-2xx mid-batch throws `MailgunnerException` and the sending key
never appears in fields/results/errors.

**Target Platform**: Cross-platform .NET. Library multi-targets `net8.0` and `netstandard2.0`; tests run on `net8.0`.

**Project Type**: Single class-library + test project (NuGet-distributable library).

**Performance Goals**: N/A. Per batch the work is O(recipients) multipart parts + ceil(N/1000) JSON
serializations + ceil(N/1000) sequential requests. Sequential (not parallel) sends keep the fail-fast
contract and avoid bursts; parallelism is out of scope.

**Constraints**: Offline tests; warnings-as-errors; XML docs on every public member; English-only;
multi-target compatible; the sending key must never appear in a result or error; `multipart/form-data`
only; recipients as repeated `to` fields; `CancellationToken` honored with `ConfigureAwait(false)`;
at most 1000 recipients per request.

**Key environment facts (verified against the 002/003/004 code):**
- `EmailAddress` already converts implicitly from `string`, exposes the bare `Address`, formats
  display-name addresses via `ToString()`, and has ordinal value equality — so the `to` parts use
  `ToString()` while the `recipient-variables` **keys use the bare `Address`** (Mailgun matches keys
  to the recipient address).
- `MailgunnerClient.SendAsync` already contains the response→`SendResult`-or-`MailgunnerException`
  logic (`TryParseResult`); this is refactored into a private `SendContentAsync(content, ct)` helper
  reused by both `SendAsync` and `SendBatchAsync` (no behavior change to single send).
- `System.Text.Json.JsonSerializer.Serialize` on a `Dictionary<string, IDictionary<string, object?>>`
  emits a single nested JSON object on both target frameworks; the library does not enable trimming,
  so reflection-based serialization raises no IL2026/IL3050 warnings under warnings-as-errors
  (identical situation to feature 004's `t:variables`).
- The existing template-field emission (`template`/`t:version`/`t:text`/`t:variables`) lives in
  `MailgunMessageContent`; the batch builder reuses the same field names and the same `t:variables`
  serialization rule (omit when the global map is empty).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Constitution v1.1.0 (2026-06-22). Gates derived from its principles:

| Principle | Gate for this feature | Status |
|-----------|------------------------|--------|
| I. Minimal Dependencies & Modern .NET | Adds **no** dependency; JSON via `System.Text.Json` exclusively; no `Newtonsoft.Json`/FluentEmail. | ✅ PASS |
| II. Managed HTTP & Resilience | Reuses the registered typed `HttpClient`; `SendBatchAsync` accepts a `CancellationToken`, honors it between/within chunks, and uses `ConfigureAwait(false)`. **Polly transient-fault handling remains unwired** (carried forward from 003/004). | ⚠️ PARTIAL — resilience deferred (tracked in Complexity Tracking) |
| III. Test-First, Network-Free (NON-NEGOTIABLE) | Covers the **two** items the constitution explicitly names: *"batch auto-chunking at the 1000-recipient boundary"* and *"the `recipient-variables` JSON shape (keyed by recipient email)."* All via the fake `HttpMessageHandler`; default `dotnet test` stays green with no network/credentials. | ✅ PASS (directly fulfills mandated coverage) |
| IV. Documented, Strict Public API | New public types `MailgunBatchMessage`, `BatchRecipient`, and method `SendBatchAsync` all carry XML docs. Invalid input → standard `ArgumentException` before any request; API failures → the single `MailgunnerException` (no new exception types). Pre-1.0; additive surface, SemVer-safe; CHANGELOG (Unreleased) updated. | ✅ PASS |
| V. Security & Scope Discipline | No secrets in code/tests; sending key never appears in fields/result/error; scope strictly the batched templated send with `recipient-variables` + chunking. Attachments, options (`o:`), headers (`h:`), custom vars (`v:`), suppressions, and webhooks stay out. | ✅ PASS |
| Mailgun API Fidelity | `recipient-variables` keyed by recipient email; ≤1000 recipients/request enforced by automatic chunking; same `POST {base}/v3/{domain}/messages`, `multipart/form-data`, repeated `to`, Basic auth + region base URL from 002. | ✅ PASS (matches the constitution's chunking + recipient-variables bullets) |
| Dev Workflow & Quality Gates | Conventional Commits; `dotnet build`/`dotnet test` green offline; no secrets committed. | ✅ PASS |

**Result:** One justified, tracked deviation (Principle II resilience deferral, carried forward from
003/004). No new violations. See Complexity Tracking.

**Post-Phase-1 re-check:** The design adds two small data-only public types, one interface method, and
one internal builder; it refactors (without changing) the single-send response handling and extends a
test fake additively. No principle status changes. Gate still passes.

## Project Structure

### Documentation (this feature)

```text
specs/005-batch-send/
├── plan.md                       # This file (/speckit-plan output)
├── research.md                   # Phase 0 output (decisions: API shape, chunking, recipient-variables, fake-transport strategy)
├── data-model.md                 # Phase 1 output (MailgunBatchMessage / BatchRecipient + validation & chunking rules)
├── quickstart.md                 # Phase 1 output (validation/run guide)
├── contracts/
│   └── batch-send-contract.md    # Phase 1 output (public batch surface + observable HTTP contract per chunk)
└── tasks.md                      # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
└── Mailgunner/
    ├── IMailgunnerClient.cs            # MODIFIED: add SendBatchAsync(MailgunBatchMessage, CancellationToken)
    ├── MailgunnerClient.cs             # MODIFIED: implement SendBatchAsync (chunk → build → send sequentially,
    │                                    #           fail-fast); extract private SendContentAsync(content, ct)
    │                                    #           reused by SendAsync (no behavior change)
    ├── MailgunBatchMessage.cs          # NEW: sender, subject, template (+version/generate-text),
    │                                    #      global TemplateVariables, ordered Recipients
    ├── BatchRecipient.cs               # NEW: an EmailAddress + that recipient's own Variables map
    └── Internal/
        └── MailgunBatchContent.cs      # NEW: validate batch; build one chunk's multipart body
                                         #      (from, repeated to, template/t:* global, recipient-variables);
                                         #      Chunk(recipients, 1000) partition helper

    # (002/003/004 files unchanged except the two MODIFIED above; MailgunMessage,
    #  MailgunMessageContent, SendResult, MailgunnerException, EmailAddress, options, region, DI,
    #  Guard all reused as-is)

tests/
└── Mailgunner.Tests/
    ├── Fakes/
    │   └── StubHttpMessageHandler.cs   # MODIFIED (additive): record ALL requests (per-request fields,
    │                                    #   URI, method, media type) + optional per-index response selector;
    │                                    #   existing Last*/LastFormData preserved
    └── Sending/
        ├── BatchChunkingTests.cs       # NEW US1/US3: 2500→3 (1000/1000/500); 1000→1; 2000→2 (no empty tail);
        │                                #             0→0 requests; recipient order preserved per chunk
        ├── BatchRecipientVariablesTests.cs # NEW US2: recipient-variables is one JSON object keyed by email,
        │                                #            each value the recipient's own vars; empty vars → {};
        │                                #            global template + t:variables identical across chunks
        ├── BatchValidationTests.cs      # NEW: null message; missing From; missing/blank Template;
        │                                #      duplicate address → ArgumentException (no request)
        ├── BatchSendResultTests.cs      # NEW: returns one SendResult per chunk; empty list → empty result set
        ├── BatchFailureTests.cs         # NEW: non-2xx on chunk k throws MailgunnerException (status+body),
        │                                #      no further chunks sent; key never in fields/result/error
        └── BatchCancellationTests.cs    # NEW: cancellation stops further chunks (OperationCanceledException)

        # existing 003/004 Sending/*.cs tests remain and must still pass unchanged
```

**Structure Decision**: Continue the established single-library layout. The batch capability needs
per-recipient variable bundling that `MailgunMessage` does not model, so it gets **two small, data-only
public types** (`MailgunBatchMessage`, `BatchRecipient`) and **one new interface method**
(`SendBatchAsync`) — the minimum additive surface. Wire construction for a chunk is a new internal
`MailgunBatchContent` (sibling to `MailgunMessageContent`) so the working single/template build path is
untouched; it reuses the same `template`/`t:*` field names and `t:variables` rule and adds the
`recipient-variables` field and the `Chunk(...)` partition. `MailgunnerClient` gains `SendBatchAsync`
and a small refactor that extracts the existing response-handling into a private `SendContentAsync`
shared with single send. The test fake is extended additively to capture all requests; no `src/` file
from 002/003/004 is rewritten beyond the two listed modifications.

## Complexity Tracking

> One justified deviation from Principle II (resilience), carried forward from features 003 and 004.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Polly transient-fault retry still not wired (Principle II) | Batch send is built on the same typed-client send path and does not change the error contract; wiring retries is a separate cross-cutting resilience concern applied to the typed client. | Adding Polly now is not simpler and is orthogonal to batching. Resilience remains layerable later as a `DelegatingHandler` on the already-registered typed client **without changing send code**, so deferring incurs no rework. Polly stays pinned and ready. |
