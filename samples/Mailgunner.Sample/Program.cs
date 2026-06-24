using Mailgunner;
using Mailgunner.Sample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The runnable conference-invitation sample — also the project's single environment-gated live
// check (Principle III). It reads credentials from configuration/environment only and is *skipped,
// not failed*, when they are absent. No secret is ever printed or committed.
//
// Configuration is layered (appsettings.json → user-secrets in Development → environment variables)
// by the host builder. We resolve and pre-check it BEFORE registering/starting anything, because
// AddMailgunner validates on host start and would otherwise throw instead of skipping cleanly.

// Anchor the content root to the build output (where appsettings.json is copied) so the sample's
// non-secret defaults load no matter the working directory it is launched from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Logging.SetMinimumLevel(LogLevel.Warning); // keep sample output clean; still surfaces retry-exhaustion warnings

var result = SampleConfiguration.Resolve(builder.Configuration);

if (!result.IsResolved)
{
    // B2 — one or more required settings absent → clean skip, no send.
    Console.WriteLine("Skipping the live send: the following Mailgun settings are missing.");
    Console.WriteLine("Supply them via environment variables or user-secrets (never edit source), then re-run.");
    Console.WriteLine();
    foreach (var missing in result.Missing)
    {
        Console.WriteLine($"  - {missing.Key}: {missing.Guidance}");
    }

    Console.WriteLine();
    Console.WriteLine("No request was made.");
    return 0;
}

var settings = result.Settings!;

// B1 — all required settings present → register the real client and send.
builder.Services.AddMailgunner(settings.Domain, settings.SendingKey, settings.Region);

using var host = builder.Build();
var client = host.Services.GetRequiredService<IMailgunnerClient>();

var batch = ConferenceInvitation.Build(settings);

Console.WriteLine(
    $"Sending the '{settings.Template}' conference invitation to {batch.Recipients.Count} recipient(s) " +
    $"in region {settings.Region} from {settings.From}…");

try
{
    var results = await client.SendBatchAsync(batch);

    foreach (var sent in results)
    {
        Console.WriteLine($"  sent: id={sent.Id} status={sent.Message}");
    }

    Console.WriteLine("Done. Each recipient received their own name, ticket, and link.");
    return 0;
}
catch (MailgunnerException ex)
{
    // B3 — service rejected the send. Surface status + body with the likeliest causes.
    Console.Error.WriteLine($"The send failed (HTTP {ex.StatusCode}). Response body:");
    Console.Error.WriteLine(ex.ResponseBody);
    Console.Error.WriteLine();
    Console.Error.WriteLine("Likely causes:");
    Console.Error.WriteLine("  - The region must match the sandbox domain's region (a mismatch returns 404).");
    Console.Error.WriteLine("  - Each recipient must be an authorized recipient on the sandbox domain.");
    Console.Error.WriteLine("  - The stored Handlebars template (referencing {{name}}/{{ticket}}/{{link}}) must exist.");
    return 1;
}
