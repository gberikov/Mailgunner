namespace Mailgunner.Internal;

/// <summary>
/// Validates a <see cref="MailgunBatchMessage"/>, partitions its recipients into chunks of at most
/// <see cref="MaxRecipientsPerRequest"/>, and builds the <c>multipart/form-data</c> body for one chunk.
/// Each chunk reuses the same <c>template</c>/<c>t:*</c> fields, or the same inline <c>text</c>/<c>html</c>
/// body (mirroring feature 004's rules), and adds a single <c>recipient-variables</c> JSON object keyed
/// by each recipient's bare address.
/// </summary>
internal static class MailgunBatchContent
{
    /// <summary>
    /// The fixed Mailgun limit on recipients per request. The recipient list is split into consecutive
    /// chunks of at most this size; not configurable.
    /// </summary>
    public const int MaxRecipientsPerRequest = 1000;

    /// <summary>
    /// Validates the batch before any request is issued: null message, missing sender, a missing or
    /// conflicting template/inline-body combination, template data without a template, and duplicate
    /// recipient addresses are all rejected. An empty recipient list is valid.
    /// </summary>
    /// <param name="message">The batch to validate.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="System.ArgumentException">
    /// The batch is missing a sender, has neither a Template nor an inline body (or both), has template
    /// data without a Template, or contains a duplicate recipient address.
    /// </exception>
    public static void Validate(MailgunBatchMessage message)
    {
        Guard.NotNull(message, nameof(message));

        if (string.IsNullOrWhiteSpace(message.From.Address))
        {
            throw new System.ArgumentException("A sender (From) is required.", nameof(message));
        }

        var hasBody = !string.IsNullOrEmpty(message.Text) || !string.IsNullOrEmpty(message.Html);
        var hasTemplate = !string.IsNullOrWhiteSpace(message.Template);

        if (!hasBody && !hasTemplate)
        {
            throw new System.ArgumentException(
                "A batch send requires a Template name or an inline body (Text or Html).", nameof(message));
        }

        if (hasBody && hasTemplate)
        {
            throw new System.ArgumentException(
                "A batch cannot have both a Template and an inline body (Text or Html); supply one or the other.",
                nameof(message));
        }

        var hasTemplateData = message.TemplateVariables.Count > 0
            || !string.IsNullOrWhiteSpace(message.TemplateVersion)
            || message.GenerateTextFromTemplate;

        if (hasTemplateData && !hasTemplate)
        {
            throw new System.ArgumentException(
                "Template variables, a template version, or a generated-text request require a Template name.",
                nameof(message));
        }

        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var recipient in message.Recipients)
        {
            // A default(EmailAddress) bypasses the EmailAddress constructor's non-blank guard, so each
            // recipient address is re-checked here rather than failing late during multipart build.
            if (string.IsNullOrWhiteSpace(recipient.Address.Address))
            {
                throw new System.ArgumentException(
                    "Each batch recipient must have a non-blank address.", nameof(message));
            }

            if (!seen.Add(recipient.Address.Address))
            {
                throw new System.ArgumentException(
                    $"Duplicate recipient address: '{recipient.Address.Address}'.", nameof(message));
            }
        }
    }

    /// <summary>
    /// Partitions <paramref name="items"/> into consecutive, order-preserving slices of at most
    /// <paramref name="size"/>. Chunk <c>k</c> holds items <c>[k·size, min((k+1)·size, N))</c>; an
    /// empty list yields no chunks and exact multiples produce no trailing empty slice.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The ordered item list.</param>
    /// <param name="size">The maximum chunk size.</param>
    /// <returns>The chunks, in order.</returns>
    public static System.Collections.Generic.IEnumerable<System.Collections.Generic.IReadOnlyList<T>> Chunk<T>(
        System.Collections.Generic.IList<T> items, int size)
    {
        for (var start = 0; start < items.Count; start += size)
        {
            var end = System.Math.Min(start + size, items.Count);
            var slice = new System.Collections.Generic.List<T>(end - start);
            for (var i = start; i < end; i++)
            {
                slice.Add(items[i]);
            }

            yield return slice;
        }
    }

    /// <summary>
    /// Builds one chunk's multipart body: <c>from</c>, one repeated <c>to</c> part per recipient (never
    /// comma-joined), optional <c>subject</c>, either the reused <c>text</c>/<c>html</c> inline body or
    /// the reused <c>template</c>/<c>t:version</c>/<c>t:text</c> and global <c>t:variables</c> (omitted
    /// when empty), and a single <c>recipient-variables</c> JSON object keyed by each recipient's bare
    /// address.
    /// </summary>
    /// <param name="message">The batch supplying the shared body/template fields and global variables.</param>
    /// <param name="chunk">The recipients in this chunk, in order.</param>
    /// <returns>The multipart content to POST for this chunk.</returns>
    public static System.Net.Http.MultipartFormDataContent BuildChunk(
        MailgunBatchMessage message,
        System.Collections.Generic.IReadOnlyList<BatchRecipient> chunk)
    {
        var content = new System.Net.Http.MultipartFormDataContent();

        MailgunHttp.AddField(content, "from", message.From.ToString());

        foreach (var recipient in chunk)
        {
            MailgunHttp.AddField(content, "to", recipient.Address.ToString());
        }

        if (message.Subject is not null)
        {
            MailgunHttp.AddField(content, "subject", message.Subject);
        }

        if (!string.IsNullOrEmpty(message.Text))
        {
            MailgunHttp.AddField(content, "text", message.Text!);
        }

        if (!string.IsNullOrEmpty(message.Html))
        {
            MailgunHttp.AddField(content, "html", message.Html!);
        }

        if (!string.IsNullOrWhiteSpace(message.Template))
        {
            MailgunHttp.AddField(content, "template", message.Template!);

            if (!string.IsNullOrWhiteSpace(message.TemplateVersion))
            {
                MailgunHttp.AddField(content, "t:version", message.TemplateVersion!);
            }

            if (message.GenerateTextFromTemplate)
            {
                MailgunHttp.AddField(content, "t:text", "yes");
            }

            if (message.TemplateVariables.Count > 0)
            {
                MailgunHttp.AddField(content, "t:variables", System.Text.Json.JsonSerializer.Serialize(message.TemplateVariables));
            }
        }

        MailgunHttp.AddField(content, "recipient-variables", SerializeRecipientVariables(chunk));

        MailgunOptionsContent.Append(content, message.Options, message.Attachments, message.InlineFiles, message.ReplyTo);

        return content;
    }

    private static string SerializeRecipientVariables(
        System.Collections.Generic.IReadOnlyList<BatchRecipient> chunk)
    {
        var map = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IDictionary<string, object?>>(
            System.StringComparer.Ordinal);

        foreach (var recipient in chunk)
        {
            map[recipient.Address.Address] = recipient.Variables;
        }

        return System.Text.Json.JsonSerializer.Serialize(map);
    }
}
