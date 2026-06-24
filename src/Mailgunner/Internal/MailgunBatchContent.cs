namespace Mailgunner.Internal;

/// <summary>
/// Validates a <see cref="MailgunBatchMessage"/>, partitions its recipients into chunks of at most
/// <see cref="MaxRecipientsPerRequest"/>, and builds the <c>multipart/form-data</c> body for one chunk.
/// Each chunk reuses the same <c>template</c>/<c>t:*</c> fields (mirroring feature 004's rules) and adds
/// a single <c>recipient-variables</c> JSON object keyed by each recipient's bare address.
/// </summary>
internal static class MailgunBatchContent
{
    /// <summary>
    /// The fixed Mailgun limit on recipients per request. The recipient list is split into consecutive
    /// chunks of at most this size; not configurable.
    /// </summary>
    public const int MaxRecipientsPerRequest = 1000;

    /// <summary>
    /// Validates the batch before any request is issued: null message, missing sender, missing/blank
    /// template, and duplicate recipient addresses are all rejected. An empty recipient list is valid.
    /// </summary>
    /// <param name="message">The batch to validate.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="System.ArgumentException">
    /// The batch is missing a sender, is missing a template, or contains a duplicate recipient address.
    /// </exception>
    public static void Validate(MailgunBatchMessage message)
    {
        Guard.NotNull(message, nameof(message));

        if (string.IsNullOrWhiteSpace(message.From.Address))
        {
            throw new System.ArgumentException("A sender (From) is required.", nameof(message));
        }

        if (string.IsNullOrWhiteSpace(message.Template))
        {
            throw new System.ArgumentException("A batch send requires a Template name.", nameof(message));
        }

        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var recipient in message.Recipients)
        {
            if (!seen.Add(recipient.Address.Address))
            {
                throw new System.ArgumentException(
                    $"Duplicate recipient address: '{recipient.Address.Address}'.", nameof(message));
            }
        }
    }

    /// <summary>
    /// Partitions <paramref name="recipients"/> into consecutive, order-preserving slices of at most
    /// <paramref name="size"/>. Chunk <c>k</c> holds recipients <c>[k·size, min((k+1)·size, N))</c>; an
    /// empty list yields no chunks and exact multiples produce no trailing empty slice.
    /// </summary>
    /// <param name="recipients">The ordered recipient list.</param>
    /// <param name="size">The maximum chunk size.</param>
    /// <returns>The chunks, in order.</returns>
    public static System.Collections.Generic.IEnumerable<System.Collections.Generic.IReadOnlyList<BatchRecipient>> Chunk(
        System.Collections.Generic.IList<BatchRecipient> recipients, int size)
    {
        for (var start = 0; start < recipients.Count; start += size)
        {
            var end = System.Math.Min(start + size, recipients.Count);
            var slice = new System.Collections.Generic.List<BatchRecipient>(end - start);
            for (var i = start; i < end; i++)
            {
                slice.Add(recipients[i]);
            }

            yield return slice;
        }
    }

    /// <summary>
    /// Builds one chunk's multipart body: <c>from</c>, one repeated <c>to</c> part per recipient (never
    /// comma-joined), optional <c>subject</c>, the reused <c>template</c>/<c>t:version</c>/<c>t:text</c>
    /// and global <c>t:variables</c> (omitted when empty), and a single <c>recipient-variables</c> JSON
    /// object keyed by each recipient's bare address.
    /// </summary>
    /// <param name="message">The batch supplying the shared template fields and global variables.</param>
    /// <param name="chunk">The recipients in this chunk, in order.</param>
    /// <returns>The multipart content to POST for this chunk.</returns>
    public static System.Net.Http.MultipartFormDataContent BuildChunk(
        MailgunBatchMessage message,
        System.Collections.Generic.IReadOnlyList<BatchRecipient> chunk)
    {
        var content = new System.Net.Http.MultipartFormDataContent();

        Add(content, "from", message.From.ToString());

        foreach (var recipient in chunk)
        {
            Add(content, "to", recipient.Address.ToString());
        }

        if (message.Subject is not null)
        {
            Add(content, "subject", message.Subject);
        }

        Add(content, "template", message.Template!);

        if (!string.IsNullOrWhiteSpace(message.TemplateVersion))
        {
            Add(content, "t:version", message.TemplateVersion!);
        }

        if (message.GenerateTextFromTemplate)
        {
            Add(content, "t:text", "yes");
        }

        if (message.TemplateVariables.Count > 0)
        {
            Add(content, "t:variables", System.Text.Json.JsonSerializer.Serialize(message.TemplateVariables));
        }

        Add(content, "recipient-variables", SerializeRecipientVariables(chunk));

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

    private static void Add(System.Net.Http.MultipartFormDataContent content, string name, string value) =>
        content.Add(new System.Net.Http.StringContent(value), name);
}
