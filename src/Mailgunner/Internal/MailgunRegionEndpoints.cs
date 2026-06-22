namespace Mailgunner.Internal;

/// <summary>
/// Maps each <see cref="MailgunRegion"/> to its Mailgun API base URL.
/// </summary>
internal static class MailgunRegionEndpoints
{
    /// <summary>
    /// Gets the absolute base URL for the specified region.
    /// </summary>
    /// <param name="region">The configured region.</param>
    /// <returns>The base URL (with a trailing slash) for the region.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">The region is not a known value.</exception>
    public static System.Uri ForRegion(MailgunRegion region) => region switch
    {
        MailgunRegion.Us => new System.Uri("https://api.mailgun.net/"),
        MailgunRegion.Eu => new System.Uri("https://api.eu.mailgun.net/"),
        _ => throw new System.ArgumentOutOfRangeException(nameof(region), region, "Unknown Mailgun region."),
    };
}
