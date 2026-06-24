# Contract: Sample Runtime (CLI)

The observable behavior of `samples/Mailgunner.Sample` — the runnable artifact a developer executes
directly and the project's single environment-gated live check. This contract is what the offline
resolver test pins and what the gated live run verifies.

## Inputs (configuration only — never source edits, never committed secrets)

Resolved from environment variables, optional user-secrets (Development), and a non-secret
`appsettings.json`. Bound from the `Mailgun` section:

| Key | Required | Meaning |
|-----|----------|---------|
| `Mailgun:Domain` | Yes | Sandbox sending domain. |
| `Mailgun:SendingKey` | Yes | Sending key (secret). Prefer a Domain Sending Key. |
| `Mailgun:Region` | Yes | `Us` or `Eu` (must match the domain's region). |
| `Mailgun:From` | No | Sender; defaults to `postmaster@{Domain}`. |
| `Mailgun:Template` | No | Stored Handlebars template name; defaults to `conference-invitation`. |
| `Mailgun:Recipients:N:Address` | Yes (≥1) | Authorized sandbox recipient address(es); 2–3 recommended. |

Equivalent environment-variable form uses `__` as the section separator (e.g.
`Mailgun__SendingKey`).

## Behavior

### B1 — All required settings present → live send
1. Pre-check resolves a complete configuration.
2. Registers the client via `AddMailgunner(domain, sendingKey, region)`.
3. Builds one `MailgunBatchMessage` (template + global `t:variables` bridge) with one
   `BatchRecipient` per configured address, each carrying its own in-source `name`/`ticket`/`link`.
4. Calls `SendBatchAsync` and prints a **clear success indication**: one line per chunk with
   Mailgun's message id and status. Exit code `0`.

### B2 — One or more required settings absent → clean skip (no send)
1. Pre-check returns the ordered list of missing keys.
2. Prints a message that **names each missing setting** and **where to supply it** (env var name +
   user-secrets command). It does **not** print or hint at any secret value.
3. **No HTTP request is made**; no partial or silent send. Exit code `0` (skipped, not failed).

### B3 — Service rejects the send (e.g. region/domain mismatch → 404, unauthorized recipient)
- The `MailgunnerException` (HTTP status + body) is surfaced with a readable message pointing at the
  likely cause (region must match the domain; recipients must be sandbox-authorized). Non-zero exit.

## Guarantees (testable)

| ID | Guarantee | Verified by |
|----|-----------|-------------|
| G1 | With no credentials, the sample never sends and names every missing setting. | Offline `SampleConfigurationTests` (SC-002); manual run (quickstart). |
| G2 | The sample **compiles and the default build/test stay green** with no credentials present. | `dotnet build`/`dotnet test` in CI (SC-006 / FR-004). |
| G3 | Personalization differs per recipient (each gets own name/ticket/link). | Gated live run / test-mode output (SC-004 / US1 Independent Test). |
| G4 | No secret appears in sample source, `appsettings.json`, or output. | Review + grep (Principle V / Edge "Secrets in source"). |

## Non-goals

- No production sending guidance; sandbox only.
- No replay/freshness or webhook handling (covered elsewhere).
- The send path is **not** part of the automated/CI test suite (it is the gated live check).
