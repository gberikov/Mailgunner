namespace Mailgunner.Internal;

/// <summary>
/// Emits the optional send enrichments (tags, test mode, tracking toggles, scheduled delivery time,
/// custom headers, custom variables, and attachment/inline file parts) onto an existing
/// <c>multipart/form-data</c> body. Shared by the single/templated builder (<see cref="MailgunMessageContent"/>)
/// and the per-chunk batch builder (<see cref="MailgunBatchContent"/>) so the same enrichments compose
/// with every send. Parts are appended in a fixed, deterministic order (order is immaterial to the
/// service; fixed here for stable assertions).
/// </summary>
internal static class MailgunOptionsContent
{
    private const string DefaultContentType = "application/octet-stream";

    /// <summary>
    /// Appends the enrichment parts for <paramref name="options"/>, <paramref name="attachments"/>, and
    /// <paramref name="inlineFiles"/> to <paramref name="content"/>. Each unset option is omitted.
    /// </summary>
    /// <param name="content">The multipart body being built.</param>
    /// <param name="options">The send options (tags, test mode, tracking, delivery time, headers, variables).</param>
    /// <param name="attachments">The downloadable attachments, emitted as <c>attachment</c> file parts.</param>
    /// <param name="inlineFiles">The embedded files, emitted as <c>inline</c> file parts.</param>
    /// <exception cref="System.ArgumentException">A custom header or variable has a null/blank name.</exception>
    public static void Append(
        System.Net.Http.MultipartFormDataContent content,
        MailgunSendOptions options,
        System.Collections.Generic.IEnumerable<MailgunFile> attachments,
        System.Collections.Generic.IEnumerable<MailgunFile> inlineFiles)
    {
        // 1. Tags — one repeated o:tag per non-blank entry, in order, not de-duplicated.
        foreach (var tag in options.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                Add(content, "o:tag", tag);
            }
        }

        // 2. Test mode — present only when enabled.
        if (options.TestMode)
        {
            Add(content, "o:testmode", "yes");
        }

        // 3. Open tracking — omitted when null.
        if (options.TrackingOpens is bool trackOpens)
        {
            Add(content, "o:tracking-opens", trackOpens ? "yes" : "no");
        }

        // 4. Click tracking — omitted when null; supports htmlonly.
        if (options.TrackingClicks is ClickTracking trackClicks)
        {
            Add(content, "o:tracking-clicks", ClickTrackingValue(trackClicks));
        }

        // 5. Scheduled delivery time — RFC 2822 with a numeric offset.
        if (options.DeliveryTime is System.DateTimeOffset deliveryTime)
        {
            Add(content, "o:deliverytime", FormatRfc2822(deliveryTime));
        }

        // 6. Custom headers — h:<name>; unique names; name must be a valid header token and the
        // value must carry no line breaks (both guard against header injection on the service side).
        foreach (var header in options.CustomHeaders)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || !IsValidHeaderToken(header.Key))
            {
                throw new System.ArgumentException(
                    "A custom header name must be a non-blank HTTP header token (RFC 7230).", nameof(options));
            }

            var headerValue = header.Value ?? string.Empty;
            if (ContainsLineBreak(headerValue))
            {
                throw new System.ArgumentException(
                    "A custom header value must not contain line breaks.", nameof(options));
            }

            Add(content, "h:" + header.Key, headerValue);
        }

        // 7. Custom variables — v:<name>; string values verbatim; blank or control-bearing name rejected.
        foreach (var variable in options.CustomVariables)
        {
            if (string.IsNullOrWhiteSpace(variable.Key) || ContainsControlCharacter(variable.Key))
            {
                throw new System.ArgumentException(
                    "A custom variable name must be non-blank and free of control characters.", nameof(options));
            }

            Add(content, "v:" + variable.Key, variable.Value);
        }

        // 8. Attachments — downloadable file parts.
        foreach (var file in attachments)
        {
            AddFile(content, "attachment", file);
        }

        // 9. Inline files — embedded file parts, distinct from attachments.
        foreach (var file in inlineFiles)
        {
            AddFile(content, "inline", file);
        }
    }

    private static string ClickTrackingValue(ClickTracking mode) => mode switch
    {
        ClickTracking.Yes => "yes",
        ClickTracking.No => "no",
        ClickTracking.HtmlOnly => "htmlonly",
        _ => throw new System.ArgumentOutOfRangeException(nameof(mode), mode, "Unknown click-tracking mode."),
    };

    /// <summary>
    /// Formats <paramref name="value"/> as an RFC 2822 date-time with a numeric timezone offset (no
    /// colon, no named zone), for example <c>Thu, 25 Jun 2026 14:00:00 +0000</c>. Uses the invariant
    /// culture so day/month abbreviations are English regardless of the host locale.
    /// </summary>
    private static string FormatRfc2822(System.DateTimeOffset value)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var body = value.ToString("ddd, dd MMM yyyy HH:mm:ss ", inv);
        var offset = value.Offset;
        var sign = offset < System.TimeSpan.Zero ? "-" : "+";
        var hours = System.Math.Abs(offset.Hours).ToString("00", inv);
        var minutes = System.Math.Abs(offset.Minutes).ToString("00", inv);
        return body + sign + hours + minutes;
    }

    private static void AddFile(System.Net.Http.MultipartFormDataContent content, string field, MailgunFile file)
    {
        var fileContent = new System.Net.Http.ByteArrayContent(file.Content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? DefaultContentType : file.ContentType!);
        content.Add(fileContent, field, file.FileName);
    }

    /// <summary>
    /// Returns whether every character of <paramref name="name"/> is an RFC 7230 header-field token
    /// character (letters, digits, and <c>!#$%&amp;'*+-.^_`|~</c>). This excludes spaces, colons, and
    /// line breaks, so a malicious header name cannot inject additional headers.
    /// </summary>
    private static bool IsValidHeaderToken(string name)
    {
        foreach (var c in name)
        {
            var isTokenChar =
                (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                || c == '!' || c == '#' || c == '$' || c == '%' || c == '&' || c == '\''
                || c == '*' || c == '+' || c == '-' || c == '.' || c == '^' || c == '_'
                || c == '`' || c == '|' || c == '~';
            if (!isTokenChar)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsLineBreak(string value)
    {
        foreach (var c in value)
        {
            if (c == '\r' || c == '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    private static void Add(System.Net.Http.MultipartFormDataContent content, string name, string value) =>
        content.Add(new System.Net.Http.StringContent(value), name);
}
