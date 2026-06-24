# Implementation Plan: Domain Webhook Management (Register, List, Read, Update, Delete)

**Branch**: `014-webhook-management` | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/014-webhook-management/spec.md`

## Summary

Add a domain-webhook management capability area to the client — `client.Webhooks` — mirroring the shape
of the existing `client.Suppressions` surface. It exposes create, list, read-one, update, and delete of
a domain's webhook registrations over Mailgun's **v3** webhook surface
(`{base}/v3/{domain}/webhooks`), the event-type-centric model the spec's user stories describe: one
webhook is keyed by exactly one supported event type and carries one or more callback URLs (Mailgun caps
it at 3 per event type). A typed `WebhookEventType` enum closes the supported set; a `WebhookRegistration`
record is the unit returned by read-one and update and the per-event-type element of a list.

Technical approach: a new internal `MailgunWebhooks` (constructed lazily by `MailgunnerClient` over the
same configured typed `HttpClient` + sending domain, exactly like `MailgunSuppressions`) issues the wire
requests. Create and update send **form** parts (`id` = event-type token, one or more `url`) consistent
with the multipart send pipeline; list, read-one, and delete carry no request body. **Responses are
JSON**, deserialized with a new source-generated `WebhookJsonContext` (trim/AOT-safe, reflection-free),
projected into the public `WebhookRegistration` model. The single-URL-across-many-event-types convenience
fans out to one create per event type, issued sequentially with fail-fast / no-rollback semantics. Every
public async method takes a `CancellationToken` and uses `ConfigureAwait(false)`; every non-2xx surfaces
the single `MailgunnerException` (status + raw body) with no new exception type. The addition is purely
additive (SemVer **MINOR**) with full XML docs and a CHANGELOG entry.

> **Wire-surface note (resolved during planning).** The spec input and a transient constitution amendment
> named **v4**. Verification against the live Mailgun 2026 contract showed the event-type-centric CRUD
> model these stories require (per-event-type `.../{name}` endpoints, a `GET` list/read, one-or-more URLs
> per event type) is the **v3** API; the real **v4** surface is URL-centric (one URL ↔ many event types,
> on the collection root) with no `GET` and no `.../{name}` endpoints, and cannot back the stories as
> written. The constitution was corrected to v3 (v1.4.0) and the spec aligned, both in this change. See
> [research.md](./research.md) §1.

## Technical Context

**Language/Version**: C# (latest lang version), `nullable` enabled, `TreatWarningsAsErrors`

**Primary Dependencies**: None new. Uses only `System.Net.Http` (`HttpClient`,
`MultipartFormDataContent`) and `System.Text.Json` source generation — both already in use by the send
and suppression paths. Permitted runtime deps unchanged (`System.Text.Json`, `Polly`,
`Microsoft.Extensions.Http`, and from feature 012 `Microsoft.Extensions.Options.ConfigurationExtensions`).

**Storage**: N/A (stateless HTTP client library)

**Testing**: xUnit, network-free via the existing `StubHttpMessageHandler` / `CapturingHttpMessageHandler`
fakes. Assert per-operation wire format (method, path, multipart `id`/`url` fields, JSON response
projection), region/domain routing, the create-fan-out request count/order on partial failure, the typed
registration model, and the `MailgunnerException` surface on non-2xx. No test touches the network.

**Target Platform**: `net8.0` and `netstandard2.0` (multi-target; no platform-specific APIs introduced)

**Project Type**: Single .NET class library (`src/Mailgunner`) with a test project (`tests/Mailgunner.Tests`)

**Performance Goals**: N/A — a handful of small JSON/form requests per operation; no hot path.

**Constraints**: Additive (SemVer MINOR); reuses the registered client's base URL + Basic auth (feature
002), constructs no new `HttpClient`; one HTTP-error contract (`MailgunnerException`), input validation via
`ArgumentException`; responses parsed with source-gen only (no reflection-based `System.Text.Json`);
network-free tests; CHANGELOG entry required.

**Scale/Scope**: Small — one new public capability area (`IMailgunWebhooks` + one client property), one
public enum (`WebhookEventType`), one public record (`WebhookRegistration`), and the internal
implementation + response DTOs + source-gen context. No change to sending, DI, resilience, or
suppressions.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Minimal Dependencies & Modern .NET | ✅ Pass | No new runtime dependency. JSON via `System.Text.Json` **source generation** only (new `WebhookJsonContext`); no `Newtonsoft`/reflection. Stays `net8.0`/`netstandard2.0`. |
| II. Managed HTTP & Resilience | ✅ Pass | All requests flow through the client's existing typed `HttpClient` (region base URL + Basic auth); no `HttpClient` construction. Resilience pipeline inherited unchanged. Every public async method takes a `CancellationToken` and uses `ConfigureAwait(false)`. |
| III. Test-First, Network-Free Tests | ✅ Pass | New behavior lands with xUnit tests via the existing fake `HttpMessageHandler`. Tests assert wire format, routing, fan-out order on partial failure, and `MailgunnerException` on non-2xx — all offline. No live test added; CI green without credentials. |
| IV. Documented, Strict Public API | ✅ Pass | Small additive surface (`IMailgunWebhooks`, `WebhookEventType`, `WebhookRegistration`, one client property), every member XML-documented; `nullable` + warnings-as-errors honored. SemVer MINOR + CHANGELOG. Non-2xx surfaces the single `MailgunnerException`; input errors use `ArgumentException`; no new bespoke exception type. |
| V. Security & Scope Discipline | ✅ Pass | No secrets; reuses configured auth. In scope: **domain-level webhook CRUD** is an explicitly in-scope v1 capability (Principle V). Targets the **v3** surface per Mailgun API Fidelity (corrected to v3, constitution v1.4.0). Status tracking stays push (webhooks), not pull. No out-of-scope surface (no Events/Logs/Metrics, no account-level v1 webhooks, no domain management, no inbound-payload parsing). |

**Gate result**: PASS. The only constitution interaction — the wire version — was reconciled by amending
the constitution back to v3 (v1.4.0) and aligning the spec, both in this change; the corrected guidance
is internally consistent and matches the live API. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/014-webhook-management/
├── plan.md              # This file
├── research.md          # Phase 0 output: wire-surface verification + design decisions
├── data-model.md        # Phase 1 output: entities, validation, wire mapping
├── quickstart.md        # Phase 1 output: usage + offline validation scenarios
├── contracts/
│   └── public-api.md     # Phase 1 output: the added public surface + behavioral contract
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Mailgunner/
├── IMailgunnerClient.cs                  # MODIFIED: add `IMailgunWebhooks Webhooks { get; }`
├── MailgunnerClient.cs                   # MODIFIED: lazy `Webhooks` over (HttpClient, domain), like Suppressions
├── IMailgunWebhooks.cs                   # NEW public: the webhook CRUD capability area
├── WebhookEventType.cs                   # NEW public: closed enum of the 7 supported event types
├── WebhookRegistration.cs                # NEW public: record { EventType, Urls }
└── Internal/
    ├── MailgunWebhooks.cs                # NEW: IMailgunWebhooks impl (form requests, JSON responses,
    │                                      #      fan-out create, MailgunnerException on non-2xx)
    ├── WebhookWireDtos.cs                # NEW: response DTOs + event-type ↔ wire-token mapping
    └── WebhookJsonContext.cs             # NEW: source-generated JsonSerializerContext (responses)

tests/Mailgunner.Tests/
└── WebhookManagement/                    # NEW folder (kept separate from the signature-verification
    ├── WebhookCreateTests.cs             #      tests already under Webhooks/)
    ├── WebhookCreateMultiEventTests.cs   # NEW: fan-out, request count/order, fail-fast no-rollback
    ├── WebhookListTests.cs               # NEW: full map → registrations; empty domain → empty result
    ├── WebhookGetTests.cs                # NEW: read-one; not-found → MailgunnerException
    ├── WebhookUpdateTests.cs             # NEW: PUT .../{name} with new url(s); not-found → error
    ├── WebhookDeleteTests.cs             # NEW: DELETE .../{name}; not-found → error
    ├── WebhookRoutingTests.cs            # NEW: region/domain routing + Basic auth reuse
    ├── WebhookErrorTests.cs             # NEW: non-2xx → MailgunnerException (status + raw body)
    ├── WebhookValidationTests.cs         # NEW: empty url(s), empty event-type set → ArgumentException, no request
    ├── WebhookEventTypeMappingTests.cs   # NEW: enum ↔ wire token (incl. permanent_fail/temporary_fail)
    ├── WebhookCancellationTests.cs       # NEW: cancellation stops promptly, mid-fan-out too
    └── WebhookIndependenceTests.cs       # NEW: usable with only feature-002 config (no send/suppression dep)
```

**Structure Decision**: Single-project library. The feature is a self-contained capability area that
mirrors `MailgunSuppressions`/`IMailgunSuppressions`: a lazy property on `MailgunnerClient` over the
already-configured typed `HttpClient` and trimmed domain, an internal implementation that owns the wire
format and the `MailgunnerException` error contract, response DTOs, and a source-generated JSON context.
The send and suppression paths are untouched; the only edits to existing files add the `Webhooks` property
to the client interface and its lazy backing field to the implementation.

## Complexity Tracking

> No constitution violations — section intentionally empty.
