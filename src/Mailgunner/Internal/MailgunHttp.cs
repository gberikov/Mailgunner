namespace Mailgunner.Internal;

/// <summary>
/// The single request/response primitive shared by sending, suppressions, and webhooks, so every
/// capability honors one error contract: any non-success response surfaces as
/// <see cref="MailgunnerException"/> carrying the status code and raw body.
/// </summary>
internal static class MailgunHttp
{
    /// <summary>
    /// Issues <paramref name="request"/>, reads the body, and throws <see cref="MailgunnerException"/> on a
    /// non-success status. Disposes the request and the response.
    /// </summary>
    /// <param name="httpClient">The configured typed client.</param>
    /// <param name="request">The request to send (disposed by this method).</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The status code and raw body of a success response.</returns>
    public static async Task<(int Status, string Body)> SendAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
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

    /// <summary>
    /// Deserializes a success body with source-generated metadata. A body that is not valid JSON surfaces
    /// as <see cref="MailgunnerException"/> (status + raw body), the same contract as the send path, rather
    /// than as a raw <see cref="System.Text.Json.JsonException"/>.
    /// </summary>
    /// <typeparam name="T">The wire DTO type.</typeparam>
    /// <param name="body">The raw response body.</param>
    /// <param name="typeInfo">The source-generated metadata for <typeparamref name="T"/>.</param>
    /// <param name="status">The response status code, carried on the exception.</param>
    /// <returns>The deserialized value, or <see langword="null"/> for a JSON <c>null</c> or empty body.</returns>
    public static T? Deserialize<T>(string body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, int status)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize(body, typeInfo);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new MailgunnerException(status, body);
        }
    }

    /// <summary>Appends one string field to a multipart body.</summary>
    /// <param name="content">The multipart body being built.</param>
    /// <param name="name">The field name.</param>
    /// <param name="value">The field value.</param>
    public static void AddField(MultipartFormDataContent content, string name, string value) =>
        content.Add(new StringContent(value), name);
}
