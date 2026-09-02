namespace Mailgunner.Internal;

/// <summary>
/// The single generic implementation of <see cref="ISuppressionList{TEntry}"/> shared by all three list
/// types. It is parameterized with the read DTO type, the add-body DTO type, a DTO-to-model projection,
/// an entry-to-add-body factory, and the source-generated JSON type metadata, so one class serves
/// bounces, unsubscribes, and complaints. All requests reuse the client's configured
/// <see cref="HttpClient"/> (region base URL + Basic auth); failures surface the single
/// <see cref="MailgunnerException"/>.
/// </summary>
/// <typeparam name="TEntry">The public entry model (e.g. <see cref="Bounce"/>).</typeparam>
/// <typeparam name="TDto">The read wire DTO (e.g. <see cref="BounceDto"/>).</typeparam>
/// <typeparam name="TAddDto">The add-body wire DTO (e.g. <see cref="AddBounceDto"/>).</typeparam>
internal sealed class MailgunSuppressionList<TEntry, TDto, TAddDto> : ISuppressionList<TEntry>
{
    private readonly HttpClient _httpClient;
    private readonly string _domain;
    private readonly string _listSegment;
    private readonly Func<TDto, TEntry> _project;
    private readonly Func<TEntry, TAddDto> _toAddBody;
    private readonly Func<TEntry, string?> _addressOf;
    private readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<PageDto<TDto>> _pageTypeInfo;
    private readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<TDto> _entryTypeInfo;
    private readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<TAddDto>> _addTypeInfo;

    /// <summary>Initializes a new instance of the <see cref="MailgunSuppressionList{TEntry, TDto, TAddDto}"/> class.</summary>
    /// <param name="httpClient">The configured typed HTTP client.</param>
    /// <param name="domain">The sending domain (already trimmed).</param>
    /// <param name="listSegment">The list path segment: <c>bounces</c>, <c>unsubscribes</c>, or <c>complaints</c>.</param>
    /// <param name="project">Maps a read DTO to its public entry model.</param>
    /// <param name="toAddBody">Maps an entry to its add-body DTO.</param>
    /// <param name="addressOf">Extracts an entry's address for pre-request validation on add.</param>
    /// <param name="pageTypeInfo">JSON metadata for the paged read DTO.</param>
    /// <param name="entryTypeInfo">JSON metadata for the single read DTO.</param>
    /// <param name="addTypeInfo">JSON metadata for the add-body DTO list (the wire body is a JSON array).</param>
    public MailgunSuppressionList(
        HttpClient httpClient,
        string domain,
        string listSegment,
        Func<TDto, TEntry> project,
        Func<TEntry, TAddDto> toAddBody,
        Func<TEntry, string?> addressOf,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<PageDto<TDto>> pageTypeInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TDto> entryTypeInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<TAddDto>> addTypeInfo)
    {
        _httpClient = httpClient;
        _domain = domain;
        _listSegment = listSegment;
        _project = project;
        _toAddBody = toAddBody;
        _addressOf = addressOf;
        _pageTypeInfo = pageTypeInfo;
        _entryTypeInfo = entryTypeInfo;
        _addTypeInfo = addTypeInfo;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TEntry> ListAsync(
        int? pageSize = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var page = await ListPageAsync(pageSize, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            foreach (var item in page.Items)
            {
                yield return item;
            }

            if (!page.HasMore)
            {
                yield break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            page = await ListPageAsync(page.NextCursor!, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The Mailgun-documented maximum page size (<c>limit</c>) for a suppression-list request.</summary>
    private const int MaxPageSize = 1000;

    /// <inheritdoc />
    public Task<SuppressionPage<TEntry>> ListPageAsync(
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePageSize(pageSize);
        return FetchPageAsync(ListUri(pageSize), cancellationToken);
    }

    /// <inheritdoc />
    public Task<SuppressionPage<TEntry>> ListPageAsync(
        string cursor,
        CancellationToken cancellationToken = default) =>
        FetchPageAsync(ValidateCursor(cursor), cancellationToken);

    /// <inheritdoc />
    public async Task<TEntry> GetAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("An address is required.", nameof(address));
        }

        var (status, body) = await MailgunHttp.SendAsync(
            _httpClient,
            new HttpRequestMessage(HttpMethod.Get, ItemUri(address)),
            cancellationToken).ConfigureAwait(false);

        var dto = System.Text.Json.JsonSerializer.Deserialize(body, _entryTypeInfo);
        if (dto is null)
        {
            throw new MailgunnerException(status, body);
        }

        return _project(dto);
    }

    /// <summary>The Mailgun-documented maximum number of add entries accepted per JSON-array request.</summary>
    private const int MaxAddPerRequest = 1000;

    /// <inheritdoc />
    public Task AddAsync(
        TEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        return AddRangeAsync(new[] { entry }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(
        IEnumerable<TEntry> entries,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(entries, nameof(entries));

        var bodies = new List<TAddDto>();
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(_addressOf(entry)))
            {
                throw new ArgumentException("Every entry must be non-null with a non-blank address.", nameof(entries));
            }

            bodies.Add(_toAddBody(entry));
        }

        foreach (var chunk in MailgunBatchContent.Chunk(bodies, MaxAddPerRequest))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var json = System.Text.Json.JsonSerializer.Serialize(
                new List<TAddDto>(chunk), _addTypeInfo);
            var request = new HttpRequestMessage(HttpMethod.Post, RootUri())
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };

            await MailgunHttp.SendAsync(_httpClient, request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("An address is required.", nameof(address));
        }

        await MailgunHttp.SendAsync(
            _httpClient,
            new HttpRequestMessage(HttpMethod.Delete, ItemUri(address)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        await MailgunHttp.SendAsync(
            _httpClient,
            new HttpRequestMessage(HttpMethod.Delete, RootUri()),
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidatePageSize(int? pageSize)
    {
        if (pageSize is int n && (n < 1 || n > MaxPageSize))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize), n, $"Page size must be between 1 and {MaxPageSize}.");
        }
    }

    private Uri ListUri(int? pageSize) => new Uri(
        pageSize is int n
            ? $"v3/{_domain}/{_listSegment}?limit={n.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"v3/{_domain}/{_listSegment}",
        UriKind.Relative);

    private Uri RootUri() => new Uri($"v3/{_domain}/{_listSegment}", UriKind.Relative);

    private Uri ItemUri(string address) => new Uri(
        $"v3/{_domain}/{_listSegment}/{Uri.EscapeDataString(address)}", UriKind.Relative);

    /// <summary>
    /// Validates a caller-supplied pagination cursor before it is followed. The cursor is sent
    /// verbatim and the client carries HTTP Basic auth on every request, so a cursor pointing at any
    /// other origin would leak the sending key. Only an absolute <c>https</c> URL on the configured
    /// Mailgun host (matching <see cref="HttpClient.BaseAddress"/>) and addressing this
    /// very list (<c>/v3/{domain}/{listSegment}</c>) is accepted; anything else throws
    /// <see cref="ArgumentException"/> with no request issued.
    /// </summary>
    private Uri ValidateCursor(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            throw new ArgumentException("A pagination cursor is required.", nameof(cursor));
        }

        if (!Uri.TryCreate(cursor, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException(
                "The pagination cursor must be an absolute URL.", nameof(cursor));
        }

        var baseAddress = _httpClient.BaseAddress;
        var sameOrigin = baseAddress is not null
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            && string.Equals(uri.Scheme, baseAddress.Scheme, StringComparison.Ordinal)
            && string.Equals(uri.Host, baseAddress.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == baseAddress.Port;

        var expectedPrefix = $"/v3/{_domain}/{_listSegment}";
        if (!sameOrigin
            || !uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The pagination cursor must reference this domain's suppression list on the configured Mailgun host.",
                nameof(cursor));
        }

        return uri;
    }

    private async Task<SuppressionPage<TEntry>> FetchPageAsync(
        Uri uri, CancellationToken cancellationToken)
    {
        var (_, body) = await MailgunHttp.SendAsync(
            _httpClient,
            new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken).ConfigureAwait(false);

        var page = System.Text.Json.JsonSerializer.Deserialize(body, _pageTypeInfo);
        var items = new List<TEntry>();
        if (page?.Items is not null)
        {
            foreach (var dto in page.Items)
            {
                items.Add(_project(dto));
            }
        }

        return new SuppressionPage<TEntry>(items, page?.Paging?.Next);
    }
}
