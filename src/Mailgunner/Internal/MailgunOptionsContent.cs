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
    /// <param name="replyTo">The optional typed reply-to address, emitted as <c>h:Reply-To</c>.</param>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="options"/> is null, a custom header or variable has a null/blank name, or
    /// <paramref name="replyTo"/> conflicts with a manual <c>Reply-To</c> entry in
    /// <see cref="MailgunSendOptions.CustomHeaders"/>.
    /// </exception>
    public static void Append(
        System.Net.Http.MultipartFormDataContent content,
        MailgunSendOptions options,
        System.Collections.Generic.IEnumerable<MailgunFile> attachments,
        System.Collections.Generic.IEnumerable<MailgunFile> inlineFiles,
        EmailAddress? replyTo)
    {
        if (options is null)
        {
            throw new System.ArgumentException("Message options must not be null.", nameof(options));
        }

        // 1. Tags — one repeated o:tag per non-blank entry, in order, not de-duplicated.
        foreach (var tag in options.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                MailgunHttp.AddField(content, "o:tag", tag);
            }
        }

        // 2. Test mode — present only when enabled.
        if (options.TestMode)
        {
            MailgunHttp.AddField(content, "o:testmode", "yes");
        }

        // 3. Open tracking — omitted when null.
        if (options.TrackingOpens is bool trackOpens)
        {
            MailgunHttp.AddField(content, "o:tracking-opens", trackOpens ? "yes" : "no");
        }

        // 4. Click tracking — omitted when null; supports htmlonly.
        if (options.TrackingClicks is ClickTracking trackClicks)
        {
            MailgunHttp.AddField(content, "o:tracking-clicks", ClickTrackingValue(trackClicks));
        }

        // 4b. Additional o: toggles and strings — omitted when null/blank.
        AddYesNo(content, "o:require-tls", options.RequireTls);
        AddYesNo(content, "o:skip-verification", options.SkipVerification);
        AddYesNo(content, "o:tracking", options.Tracking);
        AddIfPresent(content, "o:sending-ip", options.SendingIp);
        AddIfPresent(content, "o:sending-ip-pool", options.SendingIpPool);
        AddIfPresent(content, "o:time-zone-localize", options.TimeZoneLocalize);

        // 5. Scheduled delivery time — RFC 2822 with a numeric offset.
        if (options.DeliveryTime is System.DateTimeOffset deliveryTime)
        {
            MailgunHttp.AddField(content, "o:deliverytime", FormatRfc2822(deliveryTime));
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
            if (TextGuards.ContainsLineBreak(headerValue))
            {
                throw new System.ArgumentException(
                    "A custom header value must not contain line breaks.", nameof(options));
            }

            MailgunHttp.AddField(content, "h:" + header.Key, headerValue);
        }

        // 7. Custom variables — v:<name>; string values verbatim; blank or control-bearing name rejected.
        foreach (var variable in options.CustomVariables)
        {
            if (string.IsNullOrWhiteSpace(variable.Key) || TextGuards.ContainsControlCharacter(variable.Key))
            {
                throw new System.ArgumentException(
                    "A custom variable name must be non-blank and free of control characters.", nameof(options));
            }

            MailgunHttp.AddField(content, "v:" + variable.Key, variable.Value);
        }

        // 7b. List-Unsubscribe (RFC 8058 / RFC 2369) — opt-in; emits h:List-Unsubscribe and, when
        // one-click, h:List-Unsubscribe-Post. Validates and guards against a manual duplicate before
        // emitting; any failure throws ArgumentException so no request is issued.
        if (options.ListUnsubscribe is ListUnsubscribeOptions unsubscribe)
        {
            AppendListUnsubscribe(content, options, unsubscribe);
        }

        // 7c. Reply-To — typed; conflicts with a manual header of the same name.
        if (replyTo is EmailAddress reply && !string.IsNullOrWhiteSpace(reply.Address))
        {
            foreach (var key in options.CustomHeaders.Keys)
            {
                if (string.Equals(key, "Reply-To", System.StringComparison.OrdinalIgnoreCase))
                {
                    throw new System.ArgumentException(
                        "Reply-To is set both via ReplyTo and a manual CustomHeaders entry; use only one.", nameof(options));
                }
            }

            MailgunHttp.AddField(content, "h:Reply-To", reply.ToString());
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

    /// <summary>
    /// Validates and emits the <c>List-Unsubscribe</c> (and, for one-click, <c>List-Unsubscribe-Post</c>)
    /// headers for <paramref name="unsubscribe"/>. The header value lists the <c>https</c> URL first then
    /// the <c>mailto:</c> address, each in angle brackets, joined by <c>", "</c>. All invalid inputs throw
    /// before any field is added.
    /// </summary>
    /// <param name="content">The multipart body being built.</param>
    /// <param name="options">The owning options (its <c>CustomHeaders</c> are checked for a duplicate).</param>
    /// <param name="unsubscribe">The unsubscribe target to validate and emit.</param>
    /// <exception cref="System.ArgumentException">
    /// The target is empty (no URL and no mailto), the URL is not an absolute <c>https</c> URI or carries
    /// control characters / line breaks, one-click is set without an <c>https</c> URL, or a
    /// <c>List-Unsubscribe</c> / <c>List-Unsubscribe-Post</c> header is also set manually via
    /// <c>CustomHeaders</c> (matched case-insensitively).
    /// </exception>
    private static void AppendListUnsubscribe(
        System.Net.Http.MultipartFormDataContent content,
        MailgunSendOptions options,
        ListUnsubscribeOptions unsubscribe)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(unsubscribe.Url);
        var hasMailto = unsubscribe.MailtoAddress.HasValue
            && !string.IsNullOrWhiteSpace(unsubscribe.MailtoAddress.Value.Address);

        if (!hasUrl && !hasMailto)
        {
            throw new System.ArgumentException(
                "A List-Unsubscribe target must have an https Url, a MailtoAddress, or both.", nameof(options));
        }

        // Fail-fast on a conflicting manual header so no duplicate reaches the wire. Header names are
        // case-insensitive, so the match is ordinal-ignore-case.
        foreach (var key in options.CustomHeaders.Keys)
        {
            if (string.Equals(key, "List-Unsubscribe", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "List-Unsubscribe-Post", System.StringComparison.OrdinalIgnoreCase))
            {
                throw new System.ArgumentException(
                    "List-Unsubscribe is set both via ListUnsubscribe and a manual CustomHeaders entry; use only one.",
                    nameof(options));
            }
        }

        if (hasUrl)
        {
            var url = unsubscribe.Url!;
            if (TextGuards.ContainsLineBreak(url) || TextGuards.ContainsControlCharacter(url))
            {
                throw new System.ArgumentException(
                    "A List-Unsubscribe Url must not contain control characters or line breaks.", nameof(options));
            }

            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, "https", System.StringComparison.OrdinalIgnoreCase))
            {
                throw new System.ArgumentException(
                    "A List-Unsubscribe Url must be an absolute https URI.", nameof(options));
            }
        }

        if (unsubscribe.OneClick && !hasUrl)
        {
            throw new System.ArgumentException(
                "One-click List-Unsubscribe requires an https Url.", nameof(options));
        }

        var targets = new System.Collections.Generic.List<string>(2);
        if (hasUrl)
        {
            targets.Add("<" + unsubscribe.Url + ">");
        }

        if (hasMailto)
        {
            targets.Add("<mailto:" + unsubscribe.MailtoAddress!.Value.Address + ">");
        }

        MailgunHttp.AddField(content, "h:List-Unsubscribe", string.Join(", ", targets));

        if (unsubscribe.OneClick)
        {
            MailgunHttp.AddField(content, "h:List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }
    }

    /// <summary>Emits <paramref name="name"/> as <c>yes</c>/<c>no</c> when <paramref name="value"/> is set; omitted when null.</summary>
    private static void AddYesNo(System.Net.Http.MultipartFormDataContent content, string name, bool? value)
    {
        if (value is bool flag)
        {
            MailgunHttp.AddField(content, name, flag ? "yes" : "no");
        }
    }

    /// <summary>Emits <paramref name="name"/> verbatim when <paramref name="value"/> is non-blank; omitted otherwise.</summary>
    private static void AddIfPresent(System.Net.Http.MultipartFormDataContent content, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            MailgunHttp.AddField(content, name, value!);
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
        System.Net.Http.HttpContent fileContent = file.OpenContent is { } open
            ? new StreamFactoryContent(open, file.Length)
            : new System.Net.Http.ByteArrayContent(file.Content!);
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
}
