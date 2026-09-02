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
    public static async System.Threading.Tasks.Task<(int Status, string Body)> SendAsync(
        System.Net.Http.HttpClient httpClient,
        System.Net.Http.HttpRequestMessage request,
        System.Threading.CancellationToken cancellationToken)
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

    /// <summary>Appends one string field to a multipart body.</summary>
    /// <param name="content">The multipart body being built.</param>
    /// <param name="name">The field name.</param>
    /// <param name="value">The field value.</param>
    public static void AddField(System.Net.Http.MultipartFormDataContent content, string name, string value) =>
        content.Add(new System.Net.Http.StringContent(value), name);
}
