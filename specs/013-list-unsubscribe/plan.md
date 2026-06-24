# Implementation Plan: One-Click List-Unsubscribe (RFC 8058)

**Branch**: `013-list-unsubscribe` | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/013-list-unsubscribe/spec.md`

## Summary

Add a typed, opt-in unsubscribe target to `MailgunSendOptions` so a consumer can declare an `https`
unsubscribe URL and/or a `mailto` address — optionally flagged one-click — and have the library emit a
correctly formatted `List-Unsubscribe` header (RFC 8058 / RFC 2369) plus, when one-click, the
`List-Unsubscribe-Post: List-Unsubscribe=One-Click` header. Technical approach: a new small public type
`ListUnsubscribeOptions` carried by a nullable `MailgunSendOptions.ListUnsubscribe` property (unset by
default ⇒ no headers, transactional mail unchanged). Emission and validation live in the existing
shared `MailgunOptionsContent.Append`, so single, templated, and batch sends all inherit the behavior
through the one code path they already share. Headers are emitted via the same `h:`-prefixed custom-
header mechanism Mailgun uses, and a case-insensitive guard rejects a send that *also* sets
`List-Unsubscribe`/`List-Unsubscribe-Post` manually through `CustomHeaders`, so no duplicate header can
ever reach the wire. All invalid inputs (non-`https` URL, control characters/line breaks, one-click
without a URL, conflicting manual header) throw `ArgumentException` before any request — no new
exception type, `MailgunnerException` stays reserved for HTTP responses.

## Technical Context

**Language/Version**: C# (latest lang version), `nullable` enabled, `TreatWarningsAsErrors`

**Primary Dependencies**: None new. Uses only the existing `System.Net.Http.MultipartFormDataContent`
already used by the message builders. (Permitted runtime deps unchanged: `System.Text.Json`, `Polly`,
`Microsoft.Extensions.Http`, and — from feature 012 — `Microsoft.Extensions.Options.ConfigurationExtensions`.)

**Storage**: N/A (stateless HTTP client library)

**Testing**: xUnit, network-free via the existing `StubHttpMessageHandler` fake; assert exact emitted
`h:List-Unsubscribe` / `h:List-Unsubscribe-Post` field values and the empty-request rejection paths.

**Target Platform**: `net8.0` and `netstandard2.0` (multi-target; no platform-specific APIs introduced)

**Project Type**: Single .NET class library (`src/Mailgunner`) with a test project (`tests/Mailgunner.Tests`)

**Performance Goals**: N/A — adds at most two multipart string parts per send; no hot-path change and
the existing wire format is otherwise untouched.

**Constraints**: Purely additive (SemVer MINOR); opt-in and unset by default; stays within the messages
scope (no new Mailgun endpoint, no transport change); one error contract (`ArgumentException` for input
validation, `MailgunnerException` only for HTTP responses); network-free tests; CHANGELOG entry required.

**Scale/Scope**: Small — one new public type (`ListUnsubscribeOptions`), one new nullable property on
`MailgunSendOptions`, and one new emission/validation step inside `MailgunOptionsContent`. No change to
the client, DI, resilience, or suppression surfaces.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Minimal Dependencies & Modern .NET | ✅ Pass | No new runtime dependency; the feature is pure `MultipartFormDataContent` part emission with `System.*` only. Stays `net8.0`/`netstandard2.0`. |
| II. Managed HTTP & Resilience | ✅ Pass | No HTTP-layer change. Sends still flow through the typed `HttpClient`; no `HttpClient` construction; `CancellationToken`/`ConfigureAwait(false)` untouched. |
| III. Test-First, Network-Free Tests | ✅ Pass | New behavior lands with xUnit tests using the existing fake `HttpMessageHandler`. Tests assert exact headers and every rejection path with no request issued. No new live test; CI green without credentials. |
| IV. Documented, Strict Public API | ✅ Pass | New public surface is tiny (`ListUnsubscribeOptions` + one property), each member with XML docs; `nullable` + warnings-as-errors honored. SemVer MINOR + CHANGELOG. Input errors are standard `ArgumentException`; no new bespoke exception type; `MailgunnerException` stays the sole HTTP-error contract. |
| V. Security & Scope Discipline | ✅ Pass | No secrets. Header-injection hardening: reject control characters/line breaks in URL and mailto and enforce `https` (consistent with `EmailAddress`/`CustomHeaders` sanitization). Scope stays within messages (custom-header emission); no new or out-of-scope endpoint; inbound click/POST and suppression management explicitly excluded. |

**Gate result**: PASS. No violations; Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/013-list-unsubscribe/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── public-api.md     # Phase 1 output: the added public surface contract
├── checklists/
│   └── requirements.md   # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Mailgunner/
├── MailgunSendOptions.cs                          # MODIFIED: add nullable `ListUnsubscribe` property
├── ListUnsubscribeOptions.cs                      # NEW public: Url (https) + MailtoAddress + OneClick
└── Internal/
    └── MailgunOptionsContent.cs                   # MODIFIED: validate + emit List-Unsubscribe / -Post;
                                                    #           case-insensitive manual-header conflict guard

tests/Mailgunner.Tests/
└── Sending/
    └── ListUnsubscribeTests.cs                    # NEW tests: url-only, mailto-only, both, one-click on/off,
                                                    #            and every rejection path (non-https, control
                                                    #            chars/line breaks, one-click w/o URL, conflict)
```

**Structure Decision**: Single-project library; the feature is a small additive enrichment concentrated
in the shared options-emission helper plus one new public options type. The existing `MailgunMessage` /
`MailgunBatchMessage` paths reach it for free because both already call `MailgunOptionsContent.Append`
with their `Options` (verified in `MailgunMessageContent.Build` and `MailgunBatchContent.BuildChunk`).

## Complexity Tracking

> No constitution violations — section intentionally empty.
