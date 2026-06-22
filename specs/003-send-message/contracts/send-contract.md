# Contract: Send Surface & Observable HTTP Behavior

The public contract this feature must satisfy. Signatures, namespaces, and observable behavior are
the contract; method bodies belong to `tasks.md` / implementation.

---

## 1. Public types

### `Mailgunner.EmailAddress` (readonly struct)

```csharp
namespace Mailgunner;

/// <summary>An email address with an optional display name.</summary>
public readonly struct EmailAddress : System.IEquatable<EmailAddress>
{
    /// <summary>Creates an address. Throws <see cref="System.ArgumentException"/> if blank.</summary>
    public EmailAddress(string address, string? displayName = null);

    /// <summary>The email address.</summary>
    public string Address { get; }

    /// <summary>The optional display name.</summary>
    public string? DisplayName { get; }

    /// <summary>Converts a bare address string into an <see cref="EmailAddress"/>.</summary>
    public static implicit operator EmailAddress(string address);

    /// <summary>Formats the wire value: "Display Name &lt;address&gt;" or just the address.</summary>
    public override string ToString();

    // value equality: Equals/GetHashCode/== / != / IEquatable<EmailAddress>
}
```

### `Mailgunner.MailgunMessage` (class)

```csharp
namespace Mailgunner;

/// <summary>An email to send: sender, recipients, optional subject, and a text and/or HTML body.</summary>
public sealed class MailgunMessage
{
    /// <summary>The sender. Required.</summary>
    public EmailAddress From { get; set; }

    /// <summary>The primary recipients.</summary>
    public System.Collections.Generic.IList<EmailAddress> To { get; }

    /// <summary>The carbon-copy recipients.</summary>
    public System.Collections.Generic.IList<EmailAddress> Cc { get; }

    /// <summary>The blind-carbon-copy recipients.</summary>
    public System.Collections.Generic.IList<EmailAddress> Bcc { get; }

    /// <summary>The optional subject.</summary>
    public string? Subject { get; set; }

    /// <summary>The plain-text body part (optional if Html is set).</summary>
    public string? Text { get; set; }

    /// <summary>The HTML body part (optional if Text is set).</summary>
    public string? Html { get; set; }
}
```

### `Mailgunner.SendResult` (class)

```csharp
namespace Mailgunner;

/// <summary>The success outcome of a send: Mailgun's message id and status message.</summary>
public sealed class SendResult
{
    public SendResult(string id, string message);

    /// <summary>Mailgun's message id.</summary>
    public string Id { get; }

    /// <summary>Mailgun's accompanying status message.</summary>
    public string Message { get; }
}
```

### `Mailgunner.MailgunnerException` (exception)

```csharp
namespace Mailgunner;

/// <summary>The single typed error raised when a Mailgun request does not yield a usable result.</summary>
public sealed class MailgunnerException : System.Exception
{
    public MailgunnerException(int statusCode, string responseBody);

    /// <summary>The HTTP status code of the response.</summary>
    public int StatusCode { get; }

    /// <summary>The raw response body (never null; empty when the response had no body).</summary>
    public string ResponseBody { get; }
}
```

### `Mailgunner.IMailgunnerClient` (new member)

```csharp
namespace Mailgunner;

public interface IMailgunnerClient
{
    /// <summary>
    /// Sends a single email. Returns a <see cref="SendResult"/> on success; throws
    /// <see cref="MailgunnerException"/> on any non-success response or an unparseable success body;
    /// throws <see cref="System.ArgumentException"/> for invalid input before any request.
    /// </summary>
    System.Threading.Tasks.Task<SendResult> SendAsync(
        MailgunMessage message,
        System.Threading.CancellationToken cancellationToken = default);
}
```

---

## 2. Behavioral contract

| ID | Given | When | Then |
|----|-------|------|------|
| C-01 | Valid message (sender, 1 recipient, text body) | `SendAsync` and service returns 2xx `{id, message}` | Returns `SendResult` exposing that id and message (FR-005). |
| C-02 | HTML body (with/without text) | `SendAsync` | Request carries the `html` part; result returned the same way (US1-2). |
| C-03 | Any send | request issued | `POST` to `v3/{domain}/messages` with `multipart/form-data`; no real network (fake transport) (FR-003, SC-006). |
| C-04 | 3 `to` recipients | `SendAsync` | Outgoing request has 3 distinct `to` fields, none comma-joined (FR-004, SC-002). |
| C-05 | Cc and Bcc recipients present | `SendAsync` | Each cc and each bcc appears as its own distinct field (FR-004). |
| C-06 | Service returns 4xx with body | `SendAsync` | Throws `MailgunnerException` exposing that status code and raw body (FR-006). |
| C-07 | Service returns 5xx with body | `SendAsync` | Throws the same `MailgunnerException` type with the 5xx code and raw body (FR-006, FR-007). |
| C-08 | 2xx with an unparseable/missing-field body | `SendAsync` | Throws `MailgunnerException` with the status and raw body; never returns a partial result (FR-006a). |
| C-09 | Non-success with an empty body | `SendAsync` | Throws `MailgunnerException` with the status and a non-null empty body (FR-011). |
| C-10 | Missing sender, or no recipient, or no body part | `SendAsync` | Throws `ArgumentException` before any request is issued (FR-002). |
| C-11 | `message` is null | `SendAsync` | Throws `ArgumentNullException` (FR-002). |
| C-12 | An already-canceled (or in-flight-canceled) token | `SendAsync` | Surfaces cancellation (`OperationCanceledException`); no `SendResult` returned (FR-008, SC-004). |
| C-13 | Any send/result/error | observed | The sending key never appears in the result or the error (FR-010, SC-007). |
| C-14 | All of the above | test run | No real network call occurs; a fake `HttpMessageHandler` stands in (FR-009, SC-005). |

---

## 3. Out of scope (explicit)

- Stored templates and per-recipient variables; batch sending with auto-chunking.
- Attachments / inline files; sending options (tags, test mode, tracking, scheduled delivery);
  custom headers and variables.
- Polly resilience / retry (deferred — see plan Complexity Tracking).
- Suppressions and webhooks.
