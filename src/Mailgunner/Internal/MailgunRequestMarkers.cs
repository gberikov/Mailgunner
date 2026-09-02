namespace Mailgunner.Internal;

/// <summary>
/// Tags a message-send request so the resilience handler can apply <see cref="SendRetryMode"/> to it and
/// the full policy to everything else. Uses <c>HttpRequestMessage.Options</c> on modern targets and the
/// legacy <c>Properties</c> bag on netstandard2.0.
/// </summary>
internal static class MailgunRequestMarkers
{
    private const string SendKeyName = "Mailgunner.IsSend";

#if NET8_0_OR_GREATER
    private static readonly System.Net.Http.HttpRequestOptionsKey<bool> SendKey = new(SendKeyName);

    public static void MarkAsSend(System.Net.Http.HttpRequestMessage request) => request.Options.Set(SendKey, true);

    public static bool IsSend(System.Net.Http.HttpRequestMessage request) =>
        request.Options.TryGetValue(SendKey, out var isSend) && isSend;
#else
    public static void MarkAsSend(System.Net.Http.HttpRequestMessage request) => request.Properties[SendKeyName] = true;

    public static bool IsSend(System.Net.Http.HttpRequestMessage request) =>
        request.Properties.TryGetValue(SendKeyName, out var value) && value is true;
#endif
}
