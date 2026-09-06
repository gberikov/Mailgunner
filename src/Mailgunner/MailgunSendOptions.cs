namespace Mailgunner;

/// <summary>
/// Optional production "knobs" that enrich any send — single, templated, or batched. Every member is
/// optional; an unset member is omitted from the request entirely, leaving the account default in
/// effect. The same options object shape is exposed by both <see cref="MailgunMessage"/> and
/// <see cref="MailgunBatchMessage"/> (on a batch, the options are repeated identically on every chunk).
/// </summary>
/// <remarks>
/// The combined size of the option (<c>o:</c>), custom-header (<c>h:</c>), custom-variable (<c>v:</c>),
/// and template (<c>t:</c>) parameters per request is capped at 16KB by Mailgun. The library does not
/// enforce this limit; exceeding it causes the service to reject the request, surfaced as a
/// <see cref="MailgunnerException"/>.
/// </remarks>
public sealed class MailgunSendOptions
{
    /// <summary>
    /// Gets the tags applied to the send for grouping and reporting. Each entry is emitted as its own
    /// repeated <c>o:tag</c> field, in order; the library does not de-duplicate. Blank or
    /// whitespace-only entries are skipped.
    /// </summary>
    public IList<string> Tags { get; } = new List<string>();

    /// <summary>
    /// Gets or sets a value indicating whether the send runs in test mode (the pipeline is exercised
    /// without delivering). When <see langword="true"/>, <c>o:testmode</c> is sent as <c>yes</c>; when
    /// <see langword="false"/>, the field is omitted entirely.
    /// </summary>
    public bool TestMode { get; set; }

    /// <summary>
    /// Gets or sets the open-tracking toggle. <see langword="true"/> emits <c>o:tracking-opens=yes</c>,
    /// <see langword="false"/> emits <c>no</c>; <see langword="null"/> omits the field (account default).
    /// </summary>
    public bool? TrackingOpens { get; set; }

    /// <summary>
    /// Gets or sets the click-tracking mode. When non-null, <c>o:tracking-clicks</c> is sent as
    /// <c>yes</c>/<c>no</c>/<c>htmlonly</c>; when <see langword="null"/>, the field is omitted (account default).
    /// </summary>
    public ClickTracking? TrackingClicks { get; set; }

    /// <summary>
    /// Gets or sets the scheduled future delivery time. When non-null, it is emitted as
    /// <c>o:deliverytime</c> formatted as RFC 2822 with a numeric timezone offset (for example
    /// <c>Thu, 25 Jun 2026 14:00:00 +0000</c>), never a named zone; when <see langword="null"/>, the
    /// field is omitted.
    /// </summary>
    public DateTimeOffset? DeliveryTime { get; set; }

    /// <summary>
    /// Gets the arbitrary custom message headers. Each entry is emitted as <c>h:&lt;name&gt;</c> carrying
    /// the supplied value. Names are unique and, like mail header names, compared case-insensitively
    /// (re-assigning a name in any casing replaces its value); the relative emission order is immaterial.
    /// A blank name is rejected with an <see cref="ArgumentException"/> when the request is built.
    /// </summary>
    public IDictionary<string, string> CustomHeaders { get; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the arbitrary custom variables that travel with the message and surface in later
    /// tracking/webhook data. Each entry is emitted as <c>v:&lt;name&gt;</c> carrying the supplied string
    /// value verbatim (the library does not serialize structured objects; pre-encode any structured
    /// data into the string). Names are unique; emission order is immaterial. A blank name is rejected
    /// with an <see cref="ArgumentException"/> when the request is built.
    /// These values are visible to recipients in the delivered email's <c>X-Mailgun-Variables</c>
    /// header. Do not put credentials or confidential metadata here. Values over 4KB are truncated
    /// in events/webhooks.
    /// </summary>
    public IDictionary<string, string> CustomVariables { get; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the opt-in unsubscribe target emitted as the RFC 8058 / RFC 2369
    /// <c>List-Unsubscribe</c> header (and, when <see cref="ListUnsubscribeOptions.OneClick"/> is set,
    /// <c>List-Unsubscribe-Post: List-Unsubscribe=One-Click</c>). <see langword="null"/> by default — no
    /// header is emitted and transactional sends are unaffected. The <c>Url</c> form must be <c>https</c>;
    /// one-click requires an <c>https</c> <c>Url</c>. Setting this <em>and</em> also supplying a
    /// <c>List-Unsubscribe</c> / <c>List-Unsubscribe-Post</c> entry in <see cref="CustomHeaders"/> (matched
    /// case-insensitively) is a conflict that throws an <see cref="ArgumentException"/> when the
    /// request is built; use exactly one mechanism.
    /// </summary>
    public ListUnsubscribeOptions? ListUnsubscribe { get; set; }

    /// <summary>Gets or sets whether TLS is required for delivery (<c>o:require-tls</c>); null omits the field.</summary>
    public bool? RequireTls { get; set; }

    /// <summary>Gets or sets whether certificate/hostname verification is skipped (<c>o:skip-verification</c>); null omits the field.</summary>
    public bool? SkipVerification { get; set; }

    /// <summary>Gets or sets the master tracking toggle (<c>o:tracking</c>) covering opens and clicks; null omits the field.</summary>
    public bool? Tracking { get; set; }

    /// <summary>Gets or sets the dedicated sending IP to use (<c>o:sending-ip</c>); null/blank omits the field.</summary>
    public string? SendingIp { get; set; }

    /// <summary>Gets or sets the IP pool to send from (<c>o:sending-ip-pool</c>); null/blank omits the field.</summary>
    public string? SendingIpPool { get; set; }

    /// <summary>
    /// Gets or sets the recipient-local delivery time (<c>o:time-zone-localize</c>, e.g. <c>"09:00"</c> or
    /// <c>"9:00AM"</c>) applied on top of <see cref="DeliveryTime"/>; null/blank omits the field.
    /// </summary>
    public string? TimeZoneLocalize { get; set; }

    /// <summary>Gets or sets whether the message is DKIM-signed (<c>o:dkim</c>); null omits the field (account default).</summary>
    public bool? Dkim { get; set; }

    /// <summary>Gets or sets a second domain key to sign the message with (<c>o:secondary-dkim</c>); null/blank omits the field.</summary>
    public string? SecondaryDkim { get; set; }

    /// <summary>Gets or sets the public alias of the <see cref="SecondaryDkim"/> domain key (<c>o:secondary-dkim-public</c>); null/blank omits the field.</summary>
    public string? SecondaryDkimPublic { get; set; }

    /// <summary>
    /// Gets or sets the maximum window Mailgun may take to deliver the message (<c>o:deliver-within</c>,
    /// e.g. <c>"1h30m"</c>); null/blank omits the field.
    /// </summary>
    public string? DeliverWithin { get; set; }

    /// <summary>
    /// Gets or sets the Send Time Optimization window (<c>o:deliverytime-optimize-period</c>, <c>24h</c> to
    /// <c>72h</c> in <c>[0-9]+h</c> form); null/blank omits the field.
    /// </summary>
    public string? DeliveryTimeOptimizePeriod { get; set; }

    /// <summary>Gets or sets whether the open-tracking pixel is placed at the top of the body (<c>o:tracking-pixel-location-top</c>); null omits the field.</summary>
    public bool? TrackingPixelLocationTop { get; set; }

    /// <summary>Gets or sets a URL that receives a copy of the message by HTTP POST (<c>o:archive-to</c>); null/blank omits the field.</summary>
    public string? ArchiveTo { get; set; }

    /// <summary>Gets or sets a comma-separated list of <c>X-Mailgun-*</c> headers to strip from the delivered message (<c>o:suppress-headers</c>); null/blank omits the field.</summary>
    public string? SuppressHeaders { get; set; }
}
