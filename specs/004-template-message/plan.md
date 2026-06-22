# Implementation Plan: Send a Templated Email

**Branch**: `004-template-message` (feature dir `004-template-message`) | **Date**: 2026-06-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/004-template-message/spec.md`

## Summary

Extend the existing single send (feature 003) so a message can be rendered from a server-side
**stored template** instead of an inline body. A consumer sets a template name on `MailgunMessage`,
optionally pins a version, optionally requests a generated plain-text part, and supplies a map of
**global** template variables. The multipart builder emits `template`, conditionally `t:version`
and `t:text=yes`, and — when variables are present — a single `t:variables` part whose value is one
`System.Text.Json`-serialized JSON object keyed by variable name. The body requirement becomes
"at least one body part **or** a template name"; a message that carries **both** a template and an
inline body, or template data **without** a template name, is rejected with `ArgumentException`
before any request. Everything else — endpoint, `multipart/form-data`, repeated recipient fields,
`SendResult` parsing, the single typed `MailgunnerException`, cancellation — is reused unchanged, so
plain sends keep working exactly as before. Verified entirely offline through the existing fake
`HttpMessageHandler`, asserting the captured `t:variables` payload parses as valid JSON of the
expected shape.

## Technical Context

**Language/Version**: C# (`LangVersion=latest`), `Nullable=enable` — inherited from `Directory.Build.props`.

**Primary Dependencies**: **No new dependency.** `System.Text.Json` (already referenced by feature
003, pinned at 10.0.9 in `Directory.Packages.props`) is reused to serialize the template-variables
object. `Microsoft.Extensions.Http` and the registered typed client from feature 002 are reused.
`Polly` remains provisioned-but-unused (resilience still deferred — see Constitution Check).

**Storage**: N/A (library; no persistence).

**Testing**: xUnit, fully offline. Templated sends are exercised via the existing
`StubHttpMessageHandler`, which already buffers each multipart field into `LastFormData`
(`IReadOnlyList<FormField>`); tests assert on the presence/absence and values of `template`,
`t:version`, `t:text`, and `t:variables` fields, and parse the `t:variables` value with
`JsonDocument` to confirm shape and types. No network, no credentials.

**Target Platform**: Cross-platform .NET. Library multi-targets `net8.0` and `netstandard2.0`; the
test project runs on `net8.0`.

**Project Type**: Single class-library + test project (NuGet-distributable library).

**Performance Goals**: N/A. Templated send builds an O(recipients) multipart body plus one
O(variables) JSON serialization, and issues one request.

**Constraints**: Offline tests; warnings-as-errors; XML docs on every public member; English-only;
multi-target compatible; the sending key must never appear in a result or error; `multipart/form-data`
only; recipients as repeated fields; `CancellationToken` honored with `ConfigureAwait(false)`.

**Key environment facts (verified 2026-06):**
- `System.Text.Json.JsonSerializer.Serialize` on an `IDictionary<string, object?>` produces a single
  JSON object and is available on both `net8.0` and `netstandard2.0`. The library does **not** set
  `IsTrimmable`/`PublishTrimmed`, so reflection-based serialization of arbitrary value types emits
  **no** trim/AOT (IL2026/IL3050) analyzer warnings under warnings-as-errors. (A source-generated
  context would only matter under trimming; not needed now.)
- `object?`-valued variables serialize to their correct JSON kinds (string → `"..."`, number, bool,
  array, nested object), satisfying FR-005. A `null` value serializes to JSON `null`.
- No new public-surface type is required: the feature is delivered by adding members to the existing
  `MailgunMessage` and extending the internal multipart builder. `IMailgunnerClient.SendAsync` is
  unchanged.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Constitution v1.1.0 (2026-06-22). Gates derived from its principles:

| Principle | Gate for this feature | Status |
|-----------|------------------------|--------|
| I. Minimal Dependencies & Modern .NET | Adds **no** dependency; JSON serialization uses `System.Text.Json` exclusively; no `Newtonsoft.Json`/FluentEmail. | ✅ PASS |
| II. Managed HTTP & Resilience | Reuses the registered typed `HttpClient`; `SendAsync` already accepts a `CancellationToken` and uses `ConfigureAwait(false)`. **Polly transient-fault handling remains unwired** (still scoped out; carried forward from 003). | ⚠️ PARTIAL — resilience deferred (tracked in Complexity Tracking) |
| III. Test-First, Network-Free (NON-NEGOTIABLE) | All behavior covered by xUnit via the fake `HttpMessageHandler`. This feature satisfies the constitution's explicitly required coverage for **"multipart construction for … templated messages"**. No network, no credentials; default `dotnet test` stays green. | ✅ PASS |
| IV. Documented, Strict Public API | Every new public member on `MailgunMessage` (`Template`, `TemplateVersion`, `GenerateTextFromTemplate`, `TemplateVariables`) carries XML docs. No new exception types — invalid input continues to use standard `ArgumentException`; API failures continue through the single `MailgunnerException`. Pre-1.0/unreleased, so adding members is additive and SemVer-safe; CHANGELOG (Unreleased) updated. | ✅ PASS |
| V. Security & Scope Discipline | No secrets in code/tests; the sending key never appears in a result or error; scope strictly the templated message send (global `t:variables` only). Per-recipient `recipient-variables`, batch, attachments, options, headers, custom vars stay out. | ✅ PASS |
| Mailgun API Fidelity | Stored-template fields per the contract: `template`, `t:version`, `t:text`, `t:variables` (constitution "Mailgun API Fidelity" bullet). Same `POST {base}/v3/{domain}/messages`, `multipart/form-data`, repeated recipients, Basic auth + region base URL from feature 002. | ✅ PASS |
| Dev Workflow & Quality Gates | Conventional Commits; `dotnet build`/`dotnet test` green offline; no secrets committed. | ✅ PASS |

**Result:** One justified, tracked deviation (Principle II resilience deferral, carried forward).
No new violations introduced. See Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/004-template-message/
├── plan.md                       # This file (/speckit-plan output)
├── research.md                   # Phase 0 output (decisions: variables serialization, validation, wire fields)
├── data-model.md                 # Phase 1 output (MailgunMessage template members + validation rules)
├── quickstart.md                 # Phase 1 output (validation/run guide)
├── contracts/
│   └── template-send-contract.md # Phase 1 output (public template surface + observable HTTP contract)
└── tasks.md                      # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
└── Mailgunner/
    ├── MailgunMessage.cs               # MODIFIED: add Template, TemplateVersion,
    │                                    #           GenerateTextFromTemplate, TemplateVariables
    └── Internal/
        └── MailgunMessageContent.cs    # MODIFIED: template-aware validation + emit
                                         #           template / t:version / t:text / t:variables

    # (all other 002/003 files unchanged: MailgunnerClient, IMailgunnerClient, SendResult,
    #  MailgunnerException, EmailAddress, options, region, DI ext, Guard)

tests/
└── Mailgunner.Tests/
    └── Sending/
        ├── TemplateSendTests.cs         # NEW US1: template name carried; t:variables single JSON object
        │                                #          of expected shape; absent when no/empty variables
        ├── TemplateVersionTests.cs      # NEW US2: t:version present with value / omitted when none/blank
        ├── TemplateTextTests.cs         # NEW US3: t:text=yes when requested / omitted otherwise
        ├── TemplateValidationTests.cs   # NEW: template+inline body → ArgumentException; template data
        │                                #      without name → ArgumentException; template alone is valid
        └── PlainSendRegressionTests.cs  # NEW US4: plain send carries no template fields (coexistence)

        # existing 003 Sending/*.cs tests remain and must still pass unchanged
```

**Structure Decision**: Continue the established single-library layout with **no new files in
`src/`** — the feature is two focused modifications (`MailgunMessage` gains four members; the
internal `MailgunMessageContent` gains template-aware validation and field emission). Variable
serialization lives inside `MailgunMessageContent` (it is a wire-format concern) using
`System.Text.Json.JsonSerializer`. Tests are added under the existing `tests/.../Sending/` folder
and reuse the existing `StubHttpMessageHandler` (which already captures multipart fields). No change
to `MailgunnerClient`, `IMailgunnerClient`, `SendResult`, `MailgunnerException`, or any csproj
package reference.

## Complexity Tracking

> One justified deviation from Principle II (resilience), carried forward from feature 003.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Polly transient-fault retry still not wired (Principle II) | This feature is additive to the 003 send path and does not change the error contract; wiring retries here is out of scope and would be a separate, cross-cutting resilience feature applied to the typed client. | Adding Polly now is not simpler and is orthogonal to templated sending. Resilience remains layerable later as a `DelegatingHandler` on the already-registered typed client **without changing send code**, so deferring incurs no rework. Polly stays pinned and ready. |
