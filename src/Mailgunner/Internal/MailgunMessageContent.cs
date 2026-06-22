namespace Mailgunner.Internal;

/// <summary>
/// Validates a <see cref="MailgunMessage"/> and builds the <c>multipart/form-data</c> request body
/// Mailgun expects: one <c>from</c> part, one repeated recipient part per recipient (never
/// comma-joined), and <c>subject</c>/<c>text</c>/<c>html</c> parts only when present.
/// </summary>
internal static class MailgunMessageContent
{
    /// <summary>
    /// Validates <paramref name="message"/> and builds its multipart body.
    /// </summary>
    /// <param name="message">The message to render.</param>
    /// <returns>The multipart content to POST.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="System.ArgumentException">
    /// The message is missing a sender, has no recipient across to/cc/bcc, or has no text or HTML body.
    /// </exception>
    public static System.Net.Http.MultipartFormDataContent Build(MailgunMessage message)
    {
        Guard.NotNull(message, nameof(message));
        Validate(message);

        var content = new System.Net.Http.MultipartFormDataContent();
        Add(content, "from", message.From.ToString());

        foreach (var recipient in message.To)
        {
            AddRecipient(content, "to", recipient);
        }

        foreach (var recipient in message.Cc)
        {
            AddRecipient(content, "cc", recipient);
        }

        foreach (var recipient in message.Bcc)
        {
            AddRecipient(content, "bcc", recipient);
        }

        if (message.Subject is not null)
        {
            Add(content, "subject", message.Subject);
        }

        if (!string.IsNullOrEmpty(message.Text))
        {
            Add(content, "text", message.Text!);
        }

        if (!string.IsNullOrEmpty(message.Html))
        {
            Add(content, "html", message.Html!);
        }

        return content;
    }

    private static void Validate(MailgunMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.From.Address))
        {
            throw new System.ArgumentException("A sender (From) is required.", nameof(message));
        }

        if (!HasAnyRecipient(message))
        {
            throw new System.ArgumentException(
                "At least one recipient across To, Cc, or Bcc is required.", nameof(message));
        }

        if (string.IsNullOrEmpty(message.Text) && string.IsNullOrEmpty(message.Html))
        {
            throw new System.ArgumentException(
                "At least one body part (Text or Html) is required.", nameof(message));
        }
    }

    private static bool HasAnyRecipient(MailgunMessage message)
    {
        foreach (var recipient in EnumerateAll(message))
        {
            if (!string.IsNullOrWhiteSpace(recipient.Address))
            {
                return true;
            }
        }

        return false;
    }

    private static System.Collections.Generic.IEnumerable<EmailAddress> EnumerateAll(MailgunMessage message)
    {
        foreach (var recipient in message.To)
        {
            yield return recipient;
        }

        foreach (var recipient in message.Cc)
        {
            yield return recipient;
        }

        foreach (var recipient in message.Bcc)
        {
            yield return recipient;
        }
    }

    private static void AddRecipient(
        System.Net.Http.MultipartFormDataContent content, string field, EmailAddress recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient.Address))
        {
            return;
        }

        Add(content, field, recipient.ToString());
    }

    private static void Add(System.Net.Http.MultipartFormDataContent content, string name, string value) =>
        content.Add(new System.Net.Http.StringContent(value), name);
}
