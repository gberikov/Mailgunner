# Building from source
[← All Mailgunner docs](https://github.com/gberikov/Mailgunner/blob/master/README.md#documentation)

Requires a [.NET SDK](https://dotnet.microsoft.com/download) matching `global.json`
(a `slnx`-capable SDK; .NET 10 recommended).

```bash
dotnet restore
dotnet build Mailgunner.slnx -c Release
dotnet test Mailgunner.slnx -c Release
```

Tests run fully offline — no network access or Mailgun credentials are required.

Live integration tests (`tests/Mailgunner.IntegrationTests`) run only when the `Mailgun__*`
variables from the [sample section](https://github.com/gberikov/Mailgunner/blob/master/docs/getting-started.md#run-the-sample) are set; without them every test reports
`Skipped` and the suite stays green. They are not part of `Mailgunner.slnx`'s CI/release runs —
CI and the release workflow invoke `dotnet test` scoped to the offline projects only — so opting
in is a manual, local `dotnet test tests/Mailgunner.IntegrationTests` with the environment
variables exported. Sends use `MailgunSendOptions.TestMode`, so nothing is actually delivered,
and every test removes whatever suppression entry or webhook it created, even when it fails
partway; the webhook test restores (rather than deletes) any registration that already existed
for the event type it exercises, since a webhook is a single whole-domain registration per event
type with no way to namespace it — run these against a sandbox/test domain, not one serving real
traffic on that event type.


## Project layout

| Path | Purpose |
|------|---------|
| `src/Mailgunner/` | The publishable library. |
| `tests/Mailgunner.Tests/` | Offline xUnit test suite. |
| `tests/Mailgunner.NetFxTests/` | net48 tests exercising the netstandard2.0 build (Windows CI leg). |
| `tests/Mailgunner.IntegrationTests/` | Opt-in live tests against a real Mailgun account (see above). |
| `Directory.Build.props` | Shared build/quality/package settings. |
| `Directory.Packages.props` | Central Package Management (pinned versions). |
| `.editorconfig` | Build-enforced style & analyzer rules. |

