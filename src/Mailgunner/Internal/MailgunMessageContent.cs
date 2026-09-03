namespace Mailgunner.Internal;

/// <summary>
/// Validates a <see cref="MailgunMessage"/> and builds the <c>multipart/form-data</c> request body
/// Mailgun expects: one <c>from</c> part, one repeated recipient part per recipient (never
/// comma-joined), and <c>subject</c>/<c>text</c>/<c>html</c> parts only when present. Also owns the
/// body-or-template rules and wire fields shared with the batch builder (<see cref="MailgunBatchContent"/>),
/// so single and batch sends can never encode the same message differently.
/// </summary>
internal static class MailgunMessageContent
{
    /// <summary>
    /// Validates <paramref name="message"/> and builds its multipart body.
    /// </summary>
    /// <param name="message">The message to render.</param>
    /// <returns>The multipart content to POST.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The message is missing a sender, has no recipient across to/cc/bcc, or has no text or HTML body.
    /// </exception>
    public static MultipartFormDataContent Build(MailgunMessage message)
    {
        Guard.NotNull(message, nameof(message));
        Validate(message);

        var content = new MultipartFormDataContent();
        MailgunHttp.AddField(content, "from", message.From.ToString());

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
            MailgunHttp.AddField(content, "subject", message.Subject);
        }

        AppendBody(
            content, message.Text, message.Html, message.Template, message.TemplateVersion,
            message.GenerateTextFromTemplate, message.TemplateVariables);

        if (!string.IsNullOrEmpty(message.AmpHtml))
        {
            MailgunHttp.AddField(content, "amp-html", message.AmpHtml!);
        }

        MailgunOptionsContent.Append(content, message.Options, message.Attachments, message.InlineFiles, message.ReplyTo);

        return content;
    }

    /// <summary>
    /// Enforces the shared body rules: exactly one of an inline body (<paramref name="text"/> /
    /// <paramref name="html"/>) or a stored <paramref name="template"/>, and template data (variables, a
    /// version, or a generated-text request) only alongside a template name.
    /// </summary>
    /// <param name="text">The plain-text body, if any.</param>
    /// <param name="html">The HTML body, if any.</param>
    /// <param name="template">The stored-template name, if any.</param>
    /// <param name="templateVersion">The pinned template version, if any.</param>
    /// <param name="generateText">Whether a generated plain-text part was requested.</param>
    /// <param name="variableCount">The number of global template variables supplied.</param>
    /// <param name="paramName">The parameter name reported in the exception.</param>
    /// <exception cref="ArgumentException">A rule is violated.</exception>
    public static void ValidateBodyOrTemplate(
        string? text,
        string? html,
        string? template,
        string? templateVersion,
        bool generateText,
        int variableCount,
        string paramName)
    {
        var hasBody = !string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(html);
        var hasTemplate = !string.IsNullOrWhiteSpace(template);

        if (!hasBody && !hasTemplate)
        {
            throw new ArgumentException(
                "At least one body part (Text or Html) or a Template name is required.", paramName);
        }

        if (hasBody && hasTemplate)
        {
            throw new ArgumentException(
                "A message cannot have both a Template and an inline body (Text or Html); supply one or the other.",
                paramName);
        }

        var hasTemplateData = variableCount > 0 || !string.IsNullOrWhiteSpace(templateVersion) || generateText;
        if (hasTemplateData && !hasTemplate)
        {
            throw new ArgumentException(
                "Template variables, a template version, or a generated-text request require a Template name.",
                paramName);
        }
    }

    /// <summary>
    /// Appends the shared body fields: <c>text</c>/<c>html</c> when set, or <c>template</c> with its
    /// optional <c>t:version</c>, <c>t:text=yes</c>, and <c>t:variables</c> (a single JSON object, omitted
    /// when empty).
    /// </summary>
    /// <param name="content">The multipart body being built.</param>
    /// <param name="text">The plain-text body, if any.</param>
    /// <param name="html">The HTML body, if any.</param>
    /// <param name="template">The stored-template name, if any.</param>
    /// <param name="templateVersion">The pinned template version, if any.</param>
    /// <param name="generateText">Whether to request a generated plain-text part.</param>
    /// <param name="variables">The global template variables.</param>
    public static void AppendBody(
        MultipartFormDataContent content,
        string? text,
        string? html,
        string? template,
        string? templateVersion,
        bool generateText,
        IDictionary<string, object?> variables)
    {
        if (!string.IsNullOrEmpty(text))
        {
            MailgunHttp.AddField(content, "text", text!);
        }

        if (!string.IsNullOrEmpty(html))
        {
            MailgunHttp.AddField(content, "html", html!);
        }

        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        MailgunHttp.AddField(content, "template", template!);

        if (!string.IsNullOrWhiteSpace(templateVersion))
        {
            MailgunHttp.AddField(content, "t:version", templateVersion!);
        }

        if (generateText)
        {
            MailgunHttp.AddField(content, "t:text", "yes");
        }

        if (variables.Count > 0)
        {
            MailgunHttp.AddField(content, "t:variables", System.Text.Json.JsonSerializer.Serialize(variables));
        }
    }

    private static void Validate(MailgunMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.From.Address))
        {
            throw new ArgumentException("A sender (From) is required.", nameof(message));
        }

        if (!HasAnyRecipient(message))
        {
            throw new ArgumentException(
                "At least one recipient across To, Cc, or Bcc is required.", nameof(message));
        }

        ValidateBodyOrTemplate(
            message.Text, message.Html, message.Template, message.TemplateVersion,
            message.GenerateTextFromTemplate, message.TemplateVariables.Count, nameof(message));
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

    private static IEnumerable<EmailAddress> EnumerateAll(MailgunMessage message)
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
        MultipartFormDataContent content, string field, EmailAddress recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient.Address))
        {
            return;
        }

        MailgunHttp.AddField(content, field, recipient.ToString());
    }
}
