# Sending messages
[← All Mailgunner docs](https://github.com/gberikov/Mailgunner/blob/master/README.md#documentation)

Personalized batches, the optional per-send knobs, and how to resume a batch that failed
partway.

## Personalized batch

Register the client, then send a **personalized conference-invitation batch** — each recipient
gets their own name, ticket, and personal link from one stored Handlebars template. Adapt only the
domain, key, region, and recipients; everything else is the scenario the [sample](https://github.com/gberikov/Mailgunner/blob/master/docs/getting-started.md#run-the-sample)
runs verbatim.

```csharp
using Mailgunner;
using Microsoft.Extensions.DependencyInjection;

// 1. Register the client (adapt domain / key / region; supply the key from configuration).
var services = new ServiceCollection();
services.AddMailgunner(
    domain: "sandbox123.mailgun.org",
    sendingKey: configuration["Mailgun:SendingKey"]!,
    region: MailgunRegion.Us);

IMailgunnerClient client = services.BuildServiceProvider().GetRequiredService<IMailgunnerClient>();

// 2. Build the batch from a stored Handlebars template that references {{name}} / {{ticket}} / {{link}}.
var batch = new MailgunBatchMessage
{
    From = "postmaster@sandbox123.mailgun.org",
    Subject = "You're invited!",
    Template = "conference-invitation",
    GenerateTextFromTemplate = true,
};

// 3. The bridge: each template variable reads its per-recipient value from recipient-variables.
batch.TemplateVariables["name"] = "%recipient.name%";
batch.TemplateVariables["ticket"] = "%recipient.ticket%";
batch.TemplateVariables["link"] = "%recipient.link%";

// 4. Per-recipient values — each attendee gets their own name / ticket / link.
var ada = new BatchRecipient("dev1@example.com");
ada.Variables["name"] = "Ada Lovelace";
ada.Variables["ticket"] = "A-1024";
ada.Variables["link"] = "https://conf.example/t/A-1024";
batch.Recipients.Add(ada);

var alan = new BatchRecipient("dev2@example.com");
alan.Variables["name"] = "Alan Turing";
alan.Variables["ticket"] = "A-2048";
alan.Variables["link"] = "https://conf.example/t/A-2048";
batch.Recipients.Add(alan);

// 5. Send — automatically chunked; Mailgun delivers one personalized message per recipient.
IReadOnlyList<SendResult> results = await client.SendBatchAsync(batch);
foreach (SendResult result in results)
    Console.WriteLine($"sent: id={result.Id} status={result.Message}");
```

Why the bridge (step 3)? A batch can use a stored template (this example) **or** inline `Text`/`Html`
with `%recipient.var%` placeholders. This example's stored-template path emits the global
`t:variables` (which a Handlebars template reads as `{{var}}`) and a per-recipient
`recipient-variables` object (addressed as `%recipient.var%`). Mapping each `{{var}}` to its
`%recipient.var%` token in `TemplateVariables` is what makes Mailgun render a **distinct** value per
recipient — no library change required.

## Send options & limits

Any send — single, templated, or a personalized batch — can be enriched with optional production
"knobs" via `MailgunMessage.Options` / `MailgunBatchMessage.Options` (a `MailgunSendOptions`), plus
the `Attachments` and `InlineFiles` collections. Every knob is optional; omitting one leaves your
Mailgun account default in effect.

- **Attachments & inline files** — add `MailgunFile(fileName, content, contentType?)` to `Attachments`
  (downloadable) or `InlineFiles` (embeddable, referenced from HTML by content id), or
  `MailgunFile(fileName, () => File.OpenRead(path), contentType)` to stream large files without
  buffering; the factory is called once per request. When the content type is omitted it defaults to
  `application/octet-stream`.
- **Tags** — `Options.Tags` may carry several values; all are sent (not de-duplicated).
- **Test mode** — `Options.TestMode = true` exercises the pipeline without delivering.
- **Tracking** — `Options.TrackingOpens` (on/off) and `Options.TrackingClicks`
  (`ClickTracking.Yes`/`No`/`HtmlOnly`).
- **Scheduled delivery** — `Options.DeliveryTime` (a `DateTimeOffset`) is sent as an **RFC 2822**
  date-time with a **numeric** timezone offset (for example `Thu, 25 Jun 2026 14:00:00 +0000`), never
  a named zone.
- **Custom headers & variables** — `Options.CustomHeaders` (`h:` prefix; names are case-insensitive, like
  mail headers) and `Options.CustomVariables` (`v:` prefix, string values). Custom variables are visible
  to recipients in the delivered email's `X-Mailgun-Variables` header: do not store secrets or confidential
  metadata there. Mailgun truncates values above 4KB in events/webhooks.
- **Reply-To** — `message.ReplyTo = "support@example.com"` emits the `Reply-To` header.
- **Delivery controls** — `RequireTls`, `SkipVerification`, `Tracking` (master toggle),
  `TrackingPixelLocationTop`, `SendingIp`, `SendingIpPool`, `DeliverWithin`, `DeliveryTimeOptimizePeriod`,
  `TimeZoneLocalize`, `Dkim`, `SecondaryDkim`/`SecondaryDkimPublic`, `ArchiveTo`, `SuppressHeaders`;
  `MailgunMessage.AmpHtml` for an AMP part.

`MailgunMessage.AmpHtml` can satisfy the body requirement on its own. For stored-template sends,
`From` can be omitted on either message type to inherit the template's From header; Mailgun rejects
the request if the template does not supply it either. An explicit `From` overrides the template.

> **16KB limit.** Mailgun caps the **combined** size of the option (`o:`), custom-header (`h:`),
> custom-variable (`v:`), and template (`t:`) parameters at **16KB per request**. Mailgunner does not
> enforce this client-side; exceeding it causes the service to reject the request, surfaced as a
> `MailgunnerException` carrying the HTTP status code and response body.

## Recovering batch progress

```csharp
try
{
    await client.SendBatchAsync(batch, cancellationToken);
}
catch (Exception ex) when (BatchSendProgress.FromException(ex) is not null)
{
    var progress = BatchSendProgress.FromException(ex)!;
    foreach (SendResult accepted in progress.AcceptedResults)
        Console.WriteLine($"Already accepted: {accepted.Id}"); // persist in your job's checkpoint

    Console.WriteLine($"Stopped at chunk {progress.FailedChunkIndex}; request started: {progress.RequestStarted}");
    throw; // preserve cancellation and the original failure for your job runner
}
```

The preceding accepted chunks must not be sent again. If `RequestStarted` is false, the current chunk
was not sent. If true, it **may have been accepted** even on timeout/cancellation or a malformed 2xx
response. Reconcile that uncertain chunk with your delivery records before resuming; do not blindly
retry the entire batch or assume `FailedChunkIndex` identifies an unaccepted request. Recipient order
and message data must remain unchanged while a batch is running and when interpreting its checkpoints.

