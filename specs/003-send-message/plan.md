# Implementation Plan: Send a Single Email

**Branch**: `003-send-message` (feature dir `003-send-message`) | **Date**: 2026-06-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/003-send-message/spec.md`

## Summary

Add the core capability the whole library extends: sending one email through the registered
client (feature 002). A consumer composes a message — a sender, recipients across to/cc/bcc
(each a dedicated `EmailAddress` value), an optional subject, and a text and/or HTML body — and
calls `SendAsync`. The client POSTs `multipart/form-data` to `{base}/v3/{domain}/messages`,
expressing multiple recipients as **repeated** distinct fields. A 2xx whose body parses into an
id + message yields a `SendResult`; any non-success response — or a 2xx body that cannot be
parsed — raises the single typed `MailgunnerException` exposing the HTTP status code and raw
body. Invalid input (missing sender/recipient/body) throws a standard `ArgumentException` before
any request. The send honors a `CancellationToken`. Everything is verified offline through a fake
`HttpMessageHandler`.

## Technical Context

**Language/Version**: C# (`LangVersion=latest`), `Nullable=enable` — inherited from `Directory.Build.props`.

**Primary Dependencies**: Adds `System.Text.Json` (permitted by constitution Principle I; already
pinned at 10.0.9 in `Directory.Packages.props`) for response parsing. Reuses
`Microsoft.Extensions.Http` and the registered typed client from feature 002. **No other new
dependency.** `Polly` remains provisioned-but-unused (see Constitution Check — resilience deferred).

**Storage**: N/A (library; no persistence).

**Testing**: xUnit, fully offline. Sending is exercised via a configurable fake
`HttpMessageHandler` (`StubHttpMessageHandler`) that captures the outgoing request and returns a
chosen status + body, and honors cancellation. No network, no credentials.

**Target Platform**: Cross-platform .NET. Library multi-targets `net8.0` and `netstandard2.0`; the
test project runs on `net8.0`.

**Project Type**: Single class-library + test project (NuGet-distributable library).

**Performance Goals**: N/A. The send builds an O(recipients) multipart body and issues one request.

**Constraints**: Offline tests; warnings-as-errors; XML docs on every public member; English-only;
multi-target compatible; the sending key must never appear in a result or error; `multipart/form-data`
only; recipients as repeated fields (never comma-joined); `CancellationToken` honored with
`ConfigureAwait(false)` on awaits.

**Scale/Scope**: A single message send. Templates, per-recipient variables, batch chunking,
attachments, sending options, custom headers/variables, suppressions, and webhooks are out of
scope (separate features).

**Key environment facts (verified 2026-06):**
- `HttpContent.ReadAsStringAsync(CancellationToken)` exists only on .NET 5+; the `netstandard2.0`
  overload takes no token → guard the token-bearing call behind `#if NET8_0_OR_GREATER`.
- `HttpClient.PostAsync(Uri, HttpContent, CancellationToken)` and `MultipartFormDataContent` exist
  on `netstandard2.0` — no gap there.
- `System.Text.Json` `JsonDocument` (reflection-free parsing) is available on both TFMs and emits
  no trim/AOT analyzer warnings (the library does not set `IsTrimmable`).
- `EmailAddress` will be a `readonly struct` implementing `IEquatable<EmailAddress>` — this
  satisfies CA1815 (value-type equality) and avoids needing an `IsExternalInit` polyfill that a
  `record` with `init` accessors would require on `netstandard2.0`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Constitution v1.1.0 (2026-06-22). Gates derived from its principles:

| Principle | Gate for this feature | Status |
|-----------|------------------------|--------|
| I. Minimal Dependencies & Modern .NET | Adds only `System.Text.Json` (permitted, pinned); JSON parsing uses `System.Text.Json` exclusively; no `Newtonsoft.Json`/FluentEmail. | ✅ PASS |
| II. Managed HTTP & Resilience | Outbound send flows through the registered typed `HttpClient`; `SendAsync` accepts a `CancellationToken` and uses `ConfigureAwait(false)`. **Polly transient-fault handling is NOT wired in this feature** (the spec scopes resilience out; acceptance treats a single 5xx as an immediate typed error). | ⚠️ PARTIAL — resilience deferred (tracked in Complexity Tracking) |
| III. Test-First, Network-Free (NON-NEGOTIABLE) | All behavior covered by xUnit via a fake `HttpMessageHandler`; this feature satisfies the constitution's required coverage for "multipart construction for plain messages" and "`MailgunnerException` on non-2xx responses". No network, no credentials. | ✅ PASS |
| IV. Documented, Strict Public API | Every new public type/member (`EmailAddress`, `MailgunMessage`, `SendResult`, `MailgunnerException`, `IMailgunnerClient.SendAsync`) carries XML docs. Introduces the single typed `MailgunnerException` (HTTP status + raw body); invalid input uses standard `ArgumentException`, keeping one exception per concern. Pre-1.0/unreleased, so adding a member to `IMailgunnerClient` is acceptable; CHANGELOG updated. | ✅ PASS |
| V. Security & Scope Discipline | No secrets in code/tests; the sending key never appears in the result or error; scope strictly the single-message send. | ✅ PASS |
| Mailgun API Fidelity | `POST {base}/v3/{domain}/messages` with `multipart/form-data`; multiple recipients as repeated `to`/`cc`/`bcc` fields; Basic auth + region base URL reused from feature 002. | ✅ PASS |
| Dev Workflow & Quality Gates | Conventional Commits; `dotnet build`/`dotnet test` green offline; no secrets committed. | ✅ PASS |

**Result:** One justified, tracked deviation (Principle II resilience deferral). See Complexity Tracking.

**Note on `MailgunnerException` and CA1032:** A single-purpose exception that always carries an HTTP
status code and response body intentionally does **not** provide the standard parameterless /
message-only constructors (they would create an instance in an invalid state). CA1032 is suppressed
at the type with this documented justification rather than weakening the contract.

## Project Structure

### Documentation (this feature)

```text
specs/003-send-message/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (message/result/error/address model)
├── quickstart.md        # Phase 1 output (validation/run guide)
├── contracts/
│   └── send-contract.md # Phase 1 output (public send surface + observable HTTP contract)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
└── Mailgunner/
    ├── EmailAddress.cs                  # NEW public readonly struct (address + optional display name)
    ├── MailgunMessage.cs               # NEW public message POCO (From, To/Cc/Bcc, Subject, Text, Html)
    ├── SendResult.cs                   # NEW public result (Id, Message)
    ├── MailgunnerException.cs          # NEW public single typed error (StatusCode, ResponseBody)
    ├── IMailgunnerClient.cs            # MODIFIED: add SendAsync(MailgunMessage, CancellationToken)
    ├── MailgunnerClient.cs             # MODIFIED: implement SendAsync; ctor also takes IOptions<MailgunnerOptions>
    ├── Internal/
    │   └── MailgunMessageContent.cs    # NEW internal: validates input + builds MultipartFormDataContent
    └── (existing 002 files unchanged: MailgunRegion, MailgunnerOptions, region endpoints, validator, Guard, DI ext)

tests/
└── Mailgunner.Tests/
    ├── Fakes/
    │   └── StubHttpMessageHandler.cs   # NEW configurable fake (status + body, captures request, honors cancellation)
    ├── EmailAddressTests.cs            # NEW: formatting, value equality, implicit-from-string
    └── Sending/
        ├── SendMessageTests.cs         # US1: success → id+message; multipart/form-data content type
        ├── RecipientFieldsTests.cs     # US2: repeated to/cc/bcc fields, never comma-joined
        ├── SendErrorTests.cs           # US3: 4xx/5xx & unparseable-2xx & empty-body → MailgunnerException
        ├── CancellationTests.cs        # US4: canceled token reports cancellation
        └── MessageValidationTests.cs   # FR-002: missing from/recipient/body → ArgumentException
```

**Structure Decision**: Continue the established single-library layout. New public types live at
the `Mailgunner` namespace root; the multipart builder + input validation are `internal`
(`Mailgunner.Internal`) and reachable by tests via the existing `InternalsVisibleTo`. `MailgunnerClient`
gains the domain by taking `IOptions<MailgunnerOptions>` (resolved by the typed-client factory), and
builds the request path `v3/{domain}/messages` relative to the region base URL set in feature 002.
`Mailgunner.csproj` gains a single `PackageReference Include="System.Text.Json"` (centrally versioned).

## Complexity Tracking

> One justified deviation from Principle II (resilience), tracked below.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Polly transient-fault retry not wired in this feature (Principle II) | The feature spec explicitly scopes resilience out, and its acceptance criteria treat a single 4xx/5xx as an immediate typed error; wiring retries now would change the error-path tests and pre-empt a dedicated resilience feature. | Adding Polly now is **not** simpler and would contradict the spec's acceptance tests. Resilience is additive (a `DelegatingHandler` on the already-registered typed client) and can be layered later **without changing the send code**, so deferring incurs no rework. Polly versions remain pinned and ready. |
