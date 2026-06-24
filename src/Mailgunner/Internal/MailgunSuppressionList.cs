namespace Mailgunner.Internal;

/// <summary>
/// The single generic implementation of <see cref="ISuppressionList{TEntry}"/> shared by all three list
/// types. It is parameterized with the read DTO type, the add-body DTO type, a DTO-to-model projection,
/// an entry-to-add-body factory, and the source-generated JSON type metadata, so one class serves
/// bounces, unsubscribes, and complaints. All requests reuse the client's configured
/// <see cref="System.Net.Http.HttpClient"/> (region base URL + Basic auth); failures surface the single
/// <see cref="MailgunnerException"/>.
/// </summary>
/// <typeparam name="TEntry">The public entry model (e.g. <see cref="Bounce"/>).</typeparam>
/// <typeparam name="TDto">The read wire DTO (e.g. <see cref="BounceDto"/>).</typeparam>
/// <typeparam name="TAddDto">The add-body wire DTO (e.g. <see cref="AddBounceDto"/>).</typeparam>
internal sealed class MailgunSuppressionList<TEntry, TDto, TAddDto> : ISuppressionList<TEntry>
{
    private readonly System.Net.Http.HttpClient _httpClient;
    private readonly string _domain;
    private readonly string _listSegment;
    private readonly System.Func<TDto, TEntry> _project;
    private readonly System.Func<TEntry, TAddDto> _toAddBody;
    private readonly System.Func<TEntry, string?> _addressOf;
    private readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<PageDto<TDto>> _pageTypeInfo;
    private readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<TDto> _entryTypeInfo;
    private readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<TAddDto> _addTypeInfo;

    /// <summary>Initializes a new instance of the <see cref="MailgunSuppressionList{TEntry, TDto, TAddDto}"/> class.</summary>
    /// <param name="httpClient">The configured typed HTTP client.</param>
    /// <param name="domain">The sending domain (already trimmed).</param>
    /// <param name="listSegment">The list path segment: <c>bounces</c>, <c>unsubscribes</c>, or <c>complaints</c>.</param>
    /// <param name="project">Maps a read DTO to its public entry model.</param>
    /// <param name="toAddBody">Maps an entry to its add-body DTO.</param>
    /// <param name="addressOf">Extracts an entry's address for pre-request validation on add.</param>
    /// <param name="pageTypeInfo">JSON metadata for the paged read DTO.</param>
    /// <param name="entryTypeInfo">JSON metadata for the single read DTO.</param>
    /// <param name="addTypeInfo">JSON metadata for the add-body DTO.</param>
    public MailgunSuppressionList(
        System.Net.Http.HttpClient httpClient,
        string domain,
        string listSegment,
        System.Func<TDto, TEntry> project,
        System.Func<TEntry, TAddDto> toAddBody,
        System.Func<TEntry, string?> addressOf,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<PageDto<TDto>> pageTypeInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TDto> entryTypeInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TAddDto> addTypeInfo)
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
    public async System.Collections.Generic.IAsyncEnumerable<TEntry> ListAsync(
        int? pageSize = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
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
    public System.Threading.Tasks.Task<SuppressionPage<TEntry>> ListPageAsync(
        int? pageSize = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        ValidatePageSize(pageSize);
        return FetchPageAsync(ListUri(pageSize), cancellationToken);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<SuppressionPage<TEntry>> ListPageAsync(
        string cursor,
        System.Threading.CancellationToken cancellationToken = default) =>
        FetchPageAsync(ValidateCursor(cursor), cancellationToken);

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<TEntry> GetAsync(
        string address,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new System.ArgumentException("An address is required.", nameof(address));
        }

        var (status, body) = await SendCoreAsync(
            new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, ItemUri(address)),
            cancellationToken).ConfigureAwait(false);

        var dto = System.Text.Json.JsonSerializer.Deserialize(body, _entryTypeInfo);
        if (dto is null)
        {
            throw new MailgunnerException(status, body);
        }

        return _project(dto);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task AddAsync(
        TEntry entry,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (entry is null)
        {
            throw new System.ArgumentNullException(nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(_addressOf(entry)))
        {
            throw new System.ArgumentException("An address is required.", nameof(entry));
        }

        var json = System.Text.Json.JsonSerializer.Serialize(_toAddBody(entry), _addTypeInfo);
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, RootUri())
        {
            Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

        await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task RemoveAsync(
        string address,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new System.ArgumentException("An address is required.", nameof(address));
        }

        await SendCoreAsync(
            new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Delete, ItemUri(address)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task ClearAsync(
        System.Threading.CancellationToken cancellationToken = default)
    {
        await SendCoreAsync(
            new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Delete, RootUri()),
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidatePageSize(int? pageSize)
    {
        if (pageSize is int n && (n < 1 || n > MaxPageSize))
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(pageSize), n, $"Page size must be between 1 and {MaxPageSize}.");
        }
    }

    private System.Uri ListUri(int? pageSize) => new System.Uri(
        pageSize is int n
            ? $"v3/{_domain}/{_listSegment}?limit={n.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"v3/{_domain}/{_listSegment}",
        System.UriKind.Relative);

    private System.Uri RootUri() => new System.Uri($"v3/{_domain}/{_listSegment}", System.UriKind.Relative);

    private System.Uri ItemUri(string address) => new System.Uri(
        $"v3/{_domain}/{_listSegment}/{System.Uri.EscapeDataString(address)}", System.UriKind.Relative);

    /// <summary>
    /// Validates a caller-supplied pagination cursor before it is followed. The cursor is sent
    /// verbatim and the client carries HTTP Basic auth on every request, so a cursor pointing at any
    /// other origin would leak the sending key. Only an absolute <c>https</c> URL on the configured
    /// Mailgun host (matching <see cref="System.Net.Http.HttpClient.BaseAddress"/>) and addressing this
    /// very list (<c>/v3/{domain}/{listSegment}</c>) is accepted; anything else throws
    /// <see cref="System.ArgumentException"/> with no request issued.
    /// </summary>
    private System.Uri ValidateCursor(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            throw new System.ArgumentException("A pagination cursor is required.", nameof(cursor));
        }

        if (!System.Uri.TryCreate(cursor, System.UriKind.Absolute, out var uri))
        {
            throw new System.ArgumentException(
                "The pagination cursor must be an absolute URL.", nameof(cursor));
        }

        var baseAddress = _httpClient.BaseAddress;
        var sameOrigin = baseAddress is not null
            && string.Equals(uri.Scheme, System.Uri.UriSchemeHttps, System.StringComparison.Ordinal)
            && string.Equals(uri.Scheme, baseAddress.Scheme, System.StringComparison.Ordinal)
            && string.Equals(uri.Host, baseAddress.Host, System.StringComparison.OrdinalIgnoreCase)
            && uri.Port == baseAddress.Port;

        var expectedPrefix = $"/v3/{_domain}/{_listSegment}";
        if (!sameOrigin
            || !uri.AbsolutePath.StartsWith(expectedPrefix, System.StringComparison.Ordinal))
        {
            throw new System.ArgumentException(
                "The pagination cursor must reference this domain's suppression list on the configured Mailgun host.",
                nameof(cursor));
        }

        return uri;
    }

    private async System.Threading.Tasks.Task<SuppressionPage<TEntry>> FetchPageAsync(
        System.Uri uri, System.Threading.CancellationToken cancellationToken)
    {
        var (_, body) = await SendCoreAsync(
            new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri),
            cancellationToken).ConfigureAwait(false);

        var page = System.Text.Json.JsonSerializer.Deserialize(body, _pageTypeInfo);
        var items = new System.Collections.Generic.List<TEntry>();
        if (page?.Items is not null)
        {
            foreach (var dto in page.Items)
            {
                items.Add(_project(dto));
            }
        }

        return new SuppressionPage<TEntry>(items, page?.Paging?.Next);
    }

    /// <summary>
    /// Issues <paramref name="request"/>, reads the body, and throws <see cref="MailgunnerException"/> on
    /// any non-success response (mirroring the send path's error contract). Returns the status code and
    /// raw body on success.
    /// </summary>
    private async System.Threading.Tasks.Task<(int Status, string Body)> SendCoreAsync(
        System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
        {
#if NET8_0_OR_GREATER
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
            if (!response.IsSuccessStatusCode)
            {
                throw new MailgunnerException((int)response.StatusCode, body);
            }

            return ((int)response.StatusCode, body);
        }
    }
}
