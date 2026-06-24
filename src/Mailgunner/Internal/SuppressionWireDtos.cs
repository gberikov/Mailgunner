namespace Mailgunner.Internal;

/// <summary>
/// The wire shape of a paginated suppression-list response: <c>items</c> plus a <c>paging</c> object.
/// Internal; mapped to public models before leaving the library.
/// </summary>
/// <typeparam name="TItem">The per-entry wire DTO type.</typeparam>
internal sealed class PageDto<TItem>
{
    /// <summary>Gets or sets the entries on this page.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public System.Collections.Generic.List<TItem>? Items { get; set; }

    /// <summary>Gets or sets the pagination pointers for this page.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("paging")]
    public PagingDto? Paging { get; set; }
}

/// <summary>The wire <c>paging</c> object. Only <see cref="Next"/> is consumed; the rest round-trip for fidelity.</summary>
internal sealed class PagingDto
{
    /// <summary>Gets or sets the opaque next-page URL, present even on the final (empty) page.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("next")]
    public string? Next { get; set; }

    /// <summary>Gets or sets the previous-page URL.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("previous")]
    public string? Previous { get; set; }

    /// <summary>Gets or sets the first-page URL.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("first")]
    public string? First { get; set; }

    /// <summary>Gets or sets the last-page URL.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("last")]
    public string? Last { get; set; }
}

/// <summary>The wire shape of a bounce entry.</summary>
internal sealed class BounceDto
{
    /// <summary>Gets or sets the bounced address.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("address")]
    public string? Address { get; set; }

    /// <summary>Gets or sets the failure code.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Gets or sets the failure detail.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Gets or sets the recorded timestamp.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

/// <summary>The wire shape of an unsubscribe entry.</summary>
internal sealed class UnsubscribeDto
{
    /// <summary>Gets or sets the unsubscribed address.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("address")]
    public string? Address { get; set; }

    /// <summary>Gets or sets the tags the address unsubscribed from.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tags")]
    public System.Collections.Generic.List<string>? Tags { get; set; }

    /// <summary>Gets or sets the recorded timestamp.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

/// <summary>The wire shape of a complaint entry.</summary>
internal sealed class ComplaintDto
{
    /// <summary>Gets or sets the complaining address.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("address")]
    public string? Address { get; set; }

    /// <summary>Gets or sets the recorded timestamp.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

/// <summary>The JSON request body for adding a bounce. Null fields are omitted by the serializer.</summary>
internal sealed class AddBounceDto
{
    /// <summary>Gets or sets the address to add.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("address")]
    public string? Address { get; set; }

    /// <summary>Gets or sets the optional failure code.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Gets or sets the optional failure detail.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>The JSON request body for adding an unsubscribe. Null/empty tags are omitted by the serializer.</summary>
internal sealed class AddUnsubscribeDto
{
    /// <summary>Gets or sets the address to add.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("address")]
    public string? Address { get; set; }

    /// <summary>Gets or sets the optional tags to unsubscribe from.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tags")]
    public System.Collections.Generic.List<string>? Tags { get; set; }
}

/// <summary>The JSON request body for adding a complaint.</summary>
internal sealed class AddComplaintDto
{
    /// <summary>Gets or sets the address to add.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("address")]
    public string? Address { get; set; }
}
