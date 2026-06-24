# Quickstart & Validation: Send Enrichment Options

How to exercise feature 006 and what proves it works. All validation is **offline** against the fake
transport — no Mailgun credentials or network required.

## Prerequisites

- .NET 8 SDK (repo pins it via `global.json`).
- Features 002–005 in place (DI registration, single/templated/batch send).

## Build & test

```bash
rtk dotnet build
rtk dotnet test
```

`dotnet test` MUST stay green with no environment variables set (Constitution III).

## Using the options (illustrative)

```csharp
// Attach a ticket PDF and embed a logo, tag the campaign, schedule it, and add a custom header/variable.
var message = new MailgunMessage
{
    From = new EmailAddress("noreply@mg.example.com", "Example"),
    Subject = "Your ticket",
    Template = "ticket",
};
message.To.Add("alice@example.com");

message.Attachments.Add(new MailgunFile("ticket.pdf", pdfBytes, "application/pdf"));
message.InlineFiles.Add(new MailgunFile("logo.png", logoBytes, "image/png")); // reference via cid in HTML

message.Options.Tags.Add("june-campaign");
message.Options.Tags.Add("tickets");
message.Options.TestMode = false;
message.Options.TrackingOpens = true;
message.Options.TrackingClicks = ClickTracking.HtmlOnly;
message.Options.DeliveryTime = new DateTimeOffset(2026, 6, 25, 14, 0, 0, TimeSpan.Zero); // → +0000
message.Options.CustomHeaders["X-Correlation-Id"] = "abc-123";
message.Options.CustomVariables["campaign_id"] = "42";

await client.SendAsync(message);

// The SAME options object shape works on a batch send; every chunk carries the enrichments:
var batch = new MailgunBatchMessage { From = "noreply@mg.example.com", Template = "invite" };
batch.Options.Tags.Add("conf-2026");
batch.Attachments.Add(new MailgunFile("agenda.pdf", agendaBytes, "application/pdf"));
// ... add recipients ...
await client.SendBatchAsync(batch);
```

## Validation scenarios (map to acceptance criteria & user stories)

Run against `StubHttpMessageHandler` (extended to capture each part's filename and content type). See
[contracts/send-options-contract.md](./contracts/send-options-contract.md) and
[data-model.md](./data-model.md) for exact field/part rules.

| # | Scenario (US) | Expected observation |
|---|---------------|----------------------|
| 1 | Attachment with filename + content type (US1) | A file part named `attachment` carries `filename="ticket.pdf"` and `Content-Type: application/pdf`. |
| 2 | Attachment without content type (US1) | The file part's `Content-Type` is `application/octet-stream`. |
| 3 | Inline file (US1) | A file part named `inline` (distinct from `attachment`) carries its filename + content type. |
| 4 | Multiple files (US1) | Each attachment and inline file appears as its own part with its own filename/content type. |
| 5 | Tags supplied 3× (US2) | Exactly 3 `o:tag` parts, all values present, in order. |
| 6 | Test mode + tracking (US2) | `o:testmode=yes`; `o:tracking-opens=yes`; `o:tracking-clicks=htmlonly`. |
| 7 | No options supplied (US2) | None of `o:tag`/`o:testmode`/`o:tracking-*`/`o:deliverytime`/`h:`/`v:` appears; request equals pre-006 request. |
| 8 | Scheduled delivery (US3) | `o:deliverytime` matches `^…[+-]\d{4}$`, e.g. `Thu, 25 Jun 2026 14:00:00 +0000`; no colon, no named zone. |
| 9 | Non-UTC offset (US3) | A `+03:00` `DateTimeOffset` emits `… +0300`. |
| 10 | Custom header + variable (US4) | `h:X-Correlation-Id=abc-123` and `v:campaign_id=42` under their prefixes; values verbatim. |
| 11 | Composition on templated send (FR-001) | The same options ride a `Template`-based `SendAsync` request. |
| 12 | Composition on batch (FR-015) | A 2500-recipient batch → 3 chunks; **every** chunk carries the identical option/header/variable/file parts. |
| 13 | Invalid input (data-model) | `MailgunFile("", bytes)` → `ArgumentException`; `MailgunFile("f", null!)` → `ArgumentNullException`; blank header/variable name → `ArgumentException`. |
| 14 | Secret safety | The sending key appears in no captured field/part, no `SendResult`, no `MailgunnerException`. |

## Definition of done

- All scenarios above pass offline; existing 003/004/005 tests remain green (no-options requests
  unchanged).
- README documents the **16KB combined cap** on `o:`/`h:`/`v:`/`t:` parameters.
- CHANGELOG (Unreleased) records the new public surface.
- `dotnet build`/`dotnet test` green with warnings-as-errors and no credentials.
