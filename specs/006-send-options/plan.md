# Implementation Plan: Send Enrichment Options (Attachments, Tags, Scheduling, Tracking, Custom Headers & Variables)

**Branch**: `006-send-options` (feature dir `006-send-options`) | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/006-send-options/spec.md`

## Summary

Let a consumer enrich **any** send — single (003), templated (004), and personalized batch (005) — with
the everyday production knobs: **file attachments** and **inline (embedded) files**, one or more
**tags**, a **test-mode** flag, **open/click tracking** toggles, a **scheduled delivery time**, and
arbitrary **custom headers** and **custom variables**. The design adds one reusable, data-only options
container (`MailgunSendOptions`) plus a file type (`MailgunFile`) and a small `ClickTracking` enum, and
surfaces them on both `MailgunMessage` and `MailgunBatchMessage` as `Options`, `Attachments`, and
`InlineFiles` (mirroring how `TemplateVariables` is already duplicated across both message types). A new
internal `MailgunOptionsContent` emits the enrichment parts onto an existing `MultipartFormDataContent`:
each tag as a repeated `o:tag`; `o:testmode=yes` when enabled; `o:tracking-opens`/`o:tracking-clicks`
(the latter supporting `yes`/`no`/`htmlonly`); `o:deliverytime` formatted as **RFC 2822 with a numeric
offset** (e.g. `Thu, 25 Jun 2026 14:00:00 +0000`, never a named zone); each custom header as `h:<name>`;
each custom variable as `v:<name>` (string values, verbatim); and each attachment/inline file as a file
part (`attachment`/`inline`) carrying its filename and content type (defaulting to
`application/octet-stream` when omitted). `MailgunMessageContent.Build` and
`MailgunBatchContent.BuildChunk` call this shared emitter, so the same enrichments compose with single,
templated, and every chunk of a batch send. The 16KB combined cap on `o:`/`h:`/`v:`/`t:` parameters is
**documented only** (no client-side size check; the service rejects oversize requests and the existing
single `MailgunnerException` surfaces unchanged). Verified entirely offline by extending the fake
transport additively to capture each part's **filename and content type** alongside its name and value.

## Technical Context

**Language/Version**: C# (`LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`) — inherited from `Directory.Build.props`.

**Primary Dependencies**: **No new dependency.** `System.Text.Json` (already pinned) serializes nothing
new for options (custom variables are emitted as raw strings; only the existing `t:variables` path uses
JSON). `Microsoft.Extensions.Http` + the typed client from feature 002 are reused. `Polly` remains
provisioned-but-unwired (resilience still deferred — see Constitution Check).

**Storage**: N/A (library; no persistence).

**Testing**: xUnit, fully offline. The existing `StubHttpMessageHandler`/`CapturedRequest`/`FormField`
are extended **additively** so each captured multipart part records its **filename**
(`ContentDisposition.FileName`) and **content type** (`Content.Headers.ContentType`) in addition to its
name and string value. Existing `Last*`/`LastFormData`/`Requests` members and the positional
`FormField(Name, Value)` usages stay working (new fields are optional/defaulted). Tests assert: an
attachment appears as a file part with the supplied filename and content type (and
`application/octet-stream` when omitted); an inline file appears under `inline`, distinct from
`attachment`; multiple files each get their own part; the same tag supplied N times yields N `o:tag`
parts; `o:testmode`, `o:tracking-opens`, `o:tracking-clicks` (incl. `htmlonly`) appear with the
requested values and are **absent** when unset; `o:deliverytime` is exactly RFC 2822 with a numeric
offset (`+0000`, `+0300`) and never a named zone; custom headers/variables appear under `h:`/`v:`;
options compose onto a plain send, a templated send, and **every chunk** of a batch; a send with no
options is byte-for-byte equivalent to the pre-006 request (no stray parts); and the sending key never
appears in any captured field.

**Target Platform**: Cross-platform .NET. Library multi-targets `net8.0` and `netstandard2.0`; tests run on `net8.0`.

**Project Type**: Single class-library + test project (NuGet-distributable library).

**Performance Goals**: N/A. Per send the work is O(options + headers + variables + files) extra
multipart parts; attachments are held as `byte[]` (no streaming) — acceptable for transactional
attachments and `netstandard2.0`-friendly.

**Constraints**: Offline tests; warnings-as-errors; XML docs on every public member; English-only;
multi-target compatible; sending key must never appear in a field/result/error; `multipart/form-data`
only; `o:`/`h:`/`v:` namespaces per Mailgun fidelity; `o:deliverytime` RFC 2822 + numeric offset;
combined `o:`/`h:`/`v:`/`t:` size capped at 16KB (documented, not enforced).

**Key environment facts (verified against the 002–005 code):**
- `MultipartFormDataContent.Add(content, name, fileName)` sets a `Content-Disposition` with `filename`;
  file parts use `ByteArrayContent` with `Headers.ContentType` set explicitly so the part always
  carries a content type (default `application/octet-stream`).
- `DateTimeOffset` formatted with the invariant `"ddd, dd MMM yyyy HH:mm:ss"` pattern plus a numeric
  offset yields RFC 2822; .NET's `zzz` specifier emits `+03:00` (with a colon), so the colon is
  **stripped** to produce Mailgun's `+0300`/`+0000` form. This is the one formatting subtlety (research).
- `TemplateVariables` is already duplicated across `MailgunMessage` and `MailgunBatchMessage`; adding
  `Options`/`Attachments`/`InlineFiles` to both follows that exact precedent (no shared base class).
- The two content builders are the only emit sites: `MailgunMessageContent.Build(MailgunMessage)` and
  `MailgunBatchContent.BuildChunk(message, chunk)`. Both gain one call to the shared
  `MailgunOptionsContent.Append(...)`; no change to recipient/template/recipient-variables logic.
- `MailgunnerClient.SendContentAsync` (shared by single and batch) is untouched — the error contract,
  endpoint, Basic auth, and region base URL all carry over unchanged.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Constitution v1.1.0 (2026-06-22). Gates derived from its principles:

| Principle | Gate for this feature | Status |
|-----------|------------------------|--------|
| I. Minimal Dependencies & Modern .NET | Adds **no** dependency; no new JSON usage beyond the existing `t:variables`; no `Newtonsoft.Json`/FluentEmail. | ✅ PASS |
| II. Managed HTTP & Resilience | Reuses the registered typed `HttpClient` and the existing `SendAsync`/`SendBatchAsync` paths (both already take a `CancellationToken` and `ConfigureAwait(false)`); options add only request parts. **Polly transient-fault handling remains unwired** (carried forward from 003–005). | ⚠️ PARTIAL — resilience deferred (tracked in Complexity Tracking) |
| III. Test-First, Network-Free (NON-NEGOTIABLE) | Extends the constitution's named **"multipart construction for plain and templated messages"** coverage to the option/header/variable/file parts; all via the fake `HttpMessageHandler`; default `dotnet test` stays green with no network/credentials. New/changed behavior lands with tests. | ✅ PASS |
| IV. Documented, Strict Public API | New public types `MailgunSendOptions`, `MailgunFile`, enum `ClickTracking`, and the `Options`/`Attachments`/`InlineFiles` members all carry XML docs. Invalid input (blank filename / blank header or variable name) → standard `ArgumentException`; API failures → the single `MailgunnerException` (no new exception types). Pre-1.0 additive surface, SemVer-safe; CHANGELOG (Unreleased) updated; the 16KB cap documented in README + contract. | ✅ PASS |
| V. Security & Scope Discipline | No secrets in code/tests; sending key never appears in fields/result/error; scope is exactly the message options the constitution enumerates. Suppressions and webhooks stay out. | ✅ PASS |
| Mailgun API Fidelity | Emits precisely the constitution's listed fields: `o:tag`, `o:testmode`, `o:tracking-opens`, `o:tracking-clicks`, `o:deliverytime`, `h:X-*`, `v:*`, and attachment/inline file parts; documents the combined 16KB limit. Same `POST {base}/v3/{domain}/messages`, `multipart/form-data`, Basic auth + region base URL from 002. | ✅ PASS (matches the fidelity bullet verbatim) |
| Dev Workflow & Quality Gates | Conventional Commits; `dotnet build`/`dotnet test` green offline; no secrets committed. | ✅ PASS |

**Result:** One justified, tracked deviation (Principle II resilience deferral, carried forward from
003–005). No new violations. See Complexity Tracking.

**Post-Phase-1 re-check:** The design adds three small data-only/enum public types and three duplicated
data members on the two existing message types, plus one internal emitter and one additive test-fake
extension. It changes no existing send, error, recipient, or template logic. No principle status
changes. Gate still passes.

## Project Structure

### Documentation (this feature)

```text
specs/006-send-options/
├── plan.md                        # This file (/speckit-plan output)
├── research.md                    # Phase 0 output (decisions: options shape, RFC 2822 offset, file parts, tracking tri-state, fake-transport capture)
├── data-model.md                  # Phase 1 output (MailgunSendOptions / MailgunFile / ClickTracking + emission & validation rules)
├── quickstart.md                  # Phase 1 output (validation/run guide)
├── contracts/
│   └── send-options-contract.md   # Phase 1 output (public surface + observable per-request field/part contract)
├── checklists/
│   └── requirements.md            # Spec quality checklist (from /speckit-specify, re-validated by /speckit-clarify)
└── tasks.md                       # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
└── Mailgunner/
    ├── MailgunMessage.cs              # MODIFIED: add Options (MailgunSendOptions, get-only init),
    │                                   #           Attachments (IList<MailgunFile>), InlineFiles (IList<MailgunFile>)
    ├── MailgunBatchMessage.cs         # MODIFIED: add the same Options / Attachments / InlineFiles members
    ├── MailgunSendOptions.cs          # NEW: Tags, TestMode, TrackingOpens (bool?), TrackingClicks (ClickTracking?),
    │                                   #      DeliveryTime (DateTimeOffset?), CustomHeaders, CustomVariables
    ├── MailgunFile.cs                 # NEW: FileName (required, non-blank), Content (byte[]), ContentType (optional)
    ├── ClickTracking.cs               # NEW: enum { Yes, No, HtmlOnly }
    └── Internal/
        ├── MailgunOptionsContent.cs   # NEW: Append(content, options, attachments, inlineFiles) — emits o:/h:/v: parts
        │                                #      and attachment/inline file parts; RFC 2822 numeric-offset formatting;
        │                                #      validates blank filename / blank header|variable names
        ├── MailgunMessageContent.cs   # MODIFIED: call MailgunOptionsContent.Append(...) after body/template fields
        └── MailgunBatchContent.cs     # MODIFIED: call MailgunOptionsContent.Append(...) in BuildChunk (every chunk)

    # (002–005 files otherwise unchanged: MailgunnerClient, IMailgunnerClient, SendResult,
    #  MailgunnerException, EmailAddress, BatchRecipient, region, DI, Guard all reused as-is)

tests/
└── Mailgunner.Tests/
    ├── Fakes/
    │   └── StubHttpMessageHandler.cs  # MODIFIED (additive): FormField/CapturedRequest also capture per-part
    │                                   #   FileName + ContentType; existing members & positional usage preserved
    └── Sending/
        ├── AttachmentTests.cs         # NEW US1: attachment → file part w/ filename + content type; default
        │                               #          application/octet-stream when omitted; inline under `inline`
        │                               #          distinct from `attachment`; multiple files each own part
        ├── OptionsTagsTrackingTests.cs# NEW US2: N tags → N o:tag parts; o:testmode=yes; tracking-opens/-clicks
        │                               #          incl. htmlonly; all absent when unset
        ├── DeliveryTimeTests.cs        # NEW US3: o:deliverytime exactly RFC 2822 + numeric offset (+0000/+0300),
        │                               #          never a named zone; absent when unset
        ├── CustomHeadersVariablesTests.cs # NEW US4: h:<name> / v:<name> under documented prefixes; string values
        │                               #          verbatim; multiples; no collision with options
        └── OptionsCompositionTests.cs  # NEW FR-001/FR-015: options ride a plain send, a templated send, and EVERY
                                         #          chunk of a batch; no-options send == pre-006 request (no stray parts)

        # existing 003/004/005 Sending/*.cs tests remain and must still pass unchanged
```

**Structure Decision**: Continue the established single-library layout. The enrichments are the same
regardless of which send they ride, so they live in **one reusable, data-only `MailgunSendOptions`** plus
a `MailgunFile` type and a `ClickTracking` enum — the minimum additive surface — and are exposed on both
message types via `Options`/`Attachments`/`InlineFiles`, mirroring the existing `TemplateVariables`
duplication (no new base class, keeping the public shape predictable). All wire emission is centralized
in a new internal `MailgunOptionsContent` so both the single/templated builder
(`MailgunMessageContent`) and the per-chunk batch builder (`MailgunBatchContent`) gain exactly one call
and stay otherwise untouched. The test fake is extended additively to capture each part's filename and
content type so file-part assertions are possible offline; no `src/` file from 002–005 is rewritten
beyond the listed modifications.

## Complexity Tracking

> One justified deviation from Principle II (resilience), carried forward from features 003–005.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Polly transient-fault retry still not wired (Principle II) | This feature only adds request parts to the existing send paths; it does not change the typed-client transport or the error contract, so wiring retries is an orthogonal cross-cutting concern. | Adding Polly now is not simpler and is unrelated to send options. Resilience remains layerable later as a `DelegatingHandler` on the already-registered typed client **without changing send code**, so deferring incurs no rework. Polly stays pinned and ready. |
