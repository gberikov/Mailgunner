# Quickstart & Validation: Personalized Mass Send

This guide shows how to use the batch send and how to validate the feature **entirely offline**. It
references the [contract](./contracts/batch-send-contract.md) and [data model](./data-model.md) rather
than restating them.

## Prerequisites

- The Mailgunner client registered via `AddMailgunner(domain, sendingKey, region)` (feature 002).
- A stored template (e.g. `conference-invite`) on the Mailgun domain.

## Usage — invite thousands, each personalized, in one call

```csharp
var batch = new MailgunBatchMessage
{
    From = new EmailAddress("invites@mg.example.com", "Acme Conf"),
    Subject = "Your personal invitation",
    Template = "conference-invite",
};

// Global variables shared by everyone (reused on every chunk):
batch.TemplateVariables["event"] = "Acme Conf 2026";

// One entry per recipient, each with their own values:
foreach (var attendee in attendees) // however many — chunked automatically at 1000
{
    var r = new BatchRecipient(new EmailAddress(attendee.Email, attendee.Name));
    r.Variables["name"]   = attendee.Name;
    r.Variables["ticket"] = attendee.TicketNumber;
    r.Variables["link"]   = attendee.PersonalLink;
    batch.Recipients.Add(r);
}

IReadOnlyList<SendResult> results = await client.SendBatchAsync(batch, cancellationToken);
// results.Count == ceil(attendees / 1000); one SendResult per request issued.
```

Putting recipients in `to` together with `recipient-variables` is what makes Mailgun deliver an
**individual** message to each person — each recipient sees only their own address. That isolation is
performed by Mailgun, not by client-side filtering.

## Build & test

```bash
dotnet build
dotnet test          # stays green with NO network and NO Mailgun credentials
```

## Validation scenarios (offline, via the extended fake transport)

The fake `StubHttpMessageHandler` records **every** request (per-request multipart fields, URI, method)
and can return a failing status for a chosen chunk index. Each scenario below maps to acceptance
criteria / success criteria.

| # | Scenario | Expected (assert on recorded requests) |
|---|----------|----------------------------------------|
| 1 | 2500 recipients (SC-001) | Exactly **3** requests; recipient counts 1000, 1000, 500 in order; every recipient in exactly one chunk. |
| 2 | Exactly 1000 (SC-002) | Exactly **1** request. |
| 3 | Exactly 2000 (SC-002) | Exactly **2** requests; no trailing empty request. |
| 4 | Empty `Recipients` (SC-003) | **Zero** requests; `results` is empty; no exception. |
| 5 | Per-recipient values (SC-005) | Each chunk has one `recipient-variables` JSON object keyed by bare email; each value = that recipient's vars; empty vars → `{}`. |
| 6 | Repeated `to` membership | Each chunk's `to` parts = exactly that chunk's recipients (formatted), one part each, never comma-joined. |
| 7 | Global reuse (SC-006) | `template` (and `t:variables` when present) identical across all chunks. |
| 8 | Duplicate address (FR-014) | `SendBatchAsync` throws `ArgumentException`; **zero** requests recorded. |
| 9 | Missing template / missing From | `ArgumentException` before any request. |
| 10 | Fail-fast (FR-011a) | Make chunk #2 of 3 return 500 → `MailgunnerException` (status 500, body) thrown; only **2** requests recorded (chunk #3 never sent). |
| 11 | Cancellation (FR-010) | Cancel after the first chunk → `OperationCanceledException`; remaining chunks not sent. |
| 12 | Secret safety | Sending key absent from every recorded field, every `SendResult`, and any thrown `MailgunnerException`. |

### Expected outcome

All scenarios pass offline; `dotnet test` is green without credentials. This demonstrates SC-001…SC-007
and the full functional-requirement set for the feature.
