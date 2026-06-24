namespace Mailgunner.Internal;

/// <summary>
/// Default <see cref="IMailgunSuppressions"/> implementation. Constructs the three typed
/// <see cref="MailgunSuppressionList{TEntry, TDto, TAddDto}"/> instances over the client's configured
/// <see cref="System.Net.Http.HttpClient"/> and sending domain, supplying each with its list segment,
/// DTO-to-model projection, entry-to-add-body factory, and source-generated JSON metadata.
/// </summary>
internal sealed class MailgunSuppressions : IMailgunSuppressions
{
    /// <summary>Initializes a new instance of the <see cref="MailgunSuppressions"/> class.</summary>
    /// <param name="httpClient">The configured typed HTTP client (region base URL + Basic auth).</param>
    /// <param name="domain">The sending domain (already trimmed).</param>
    public MailgunSuppressions(System.Net.Http.HttpClient httpClient, string domain)
    {
        Bounces = new MailgunSuppressionList<Bounce, BounceDto, AddBounceDto>(
            httpClient, domain, "bounces", ProjectBounce, ToAddBounce, static b => b.Address,
            SuppressionJsonContext.Default.PageDtoBounceDto,
            SuppressionJsonContext.Default.BounceDto,
            SuppressionJsonContext.Default.AddBounceDto);

        Unsubscribes = new MailgunSuppressionList<Unsubscribe, UnsubscribeDto, AddUnsubscribeDto>(
            httpClient, domain, "unsubscribes", ProjectUnsubscribe, ToAddUnsubscribe, static u => u.Address,
            SuppressionJsonContext.Default.PageDtoUnsubscribeDto,
            SuppressionJsonContext.Default.UnsubscribeDto,
            SuppressionJsonContext.Default.AddUnsubscribeDto);

        Complaints = new MailgunSuppressionList<Complaint, ComplaintDto, AddComplaintDto>(
            httpClient, domain, "complaints", ProjectComplaint, ToAddComplaint, static c => c.Address,
            SuppressionJsonContext.Default.PageDtoComplaintDto,
            SuppressionJsonContext.Default.ComplaintDto,
            SuppressionJsonContext.Default.AddComplaintDto);
    }

    /// <inheritdoc />
    public ISuppressionList<Bounce> Bounces { get; }

    /// <inheritdoc />
    public ISuppressionList<Unsubscribe> Unsubscribes { get; }

    /// <inheritdoc />
    public ISuppressionList<Complaint> Complaints { get; }

    private static Bounce ProjectBounce(BounceDto dto) => new()
    {
        Address = dto.Address ?? string.Empty,
        Code = dto.Code,
        Error = dto.Error,
        CreatedAt = SuppressionTime.Parse(dto.CreatedAt),
    };

    private static Unsubscribe ProjectUnsubscribe(UnsubscribeDto dto) => new()
    {
        Address = dto.Address ?? string.Empty,
        Tags = dto.Tags is null
            ? System.Array.Empty<string>()
            : (System.Collections.Generic.IReadOnlyList<string>)dto.Tags,
        CreatedAt = SuppressionTime.Parse(dto.CreatedAt),
    };

    private static Complaint ProjectComplaint(ComplaintDto dto) => new()
    {
        Address = dto.Address ?? string.Empty,
        CreatedAt = SuppressionTime.Parse(dto.CreatedAt),
    };

    private static AddBounceDto ToAddBounce(Bounce entry) => new()
    {
        Address = entry.Address,
        Code = entry.Code,
        Error = entry.Error,
    };

    private static AddUnsubscribeDto ToAddUnsubscribe(Unsubscribe entry) => new()
    {
        Address = entry.Address,
        Tags = entry.Tags is { Count: > 0 }
            ? new System.Collections.Generic.List<string>(entry.Tags)
            : null,
    };

    private static AddComplaintDto ToAddComplaint(Complaint entry) => new()
    {
        Address = entry.Address,
    };
}

/// <summary>
/// Parses Mailgun's RFC-2822/RFC-1123-style <c>created_at</c> timestamps (for example
/// <c>Fri, 21 Oct 2011 11:02:55 GMT</c>) into a UTC <see cref="System.DateTimeOffset"/>. Returns
/// <see langword="null"/> for absent or unparseable values so a single odd row never fails a page parse.
/// </summary>
internal static class SuppressionTime
{
    public static System.DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (System.DateTimeOffset.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var result))
        {
            return result;
        }

        return null;
    }
}
