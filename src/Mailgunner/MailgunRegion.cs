namespace Mailgunner;

/// <summary>
/// Identifies the Mailgun hosting region, which determines the API base URL that the client's
/// requests target.
/// </summary>
/// <remarks>
/// The region is independent of the sending domain: configuring a region that does not match
/// where the domain is hosted is accepted at registration (both values are individually valid)
/// but routes requests to a host where the domain is not found, yielding HTTP 404 from Mailgun.
/// </remarks>
public enum MailgunRegion
{
    /// <summary>
    /// The United States region. Requests target <c>https://api.mailgun.net</c>.
    /// </summary>
    Us = 1,

    /// <summary>
    /// The European Union region. Requests target <c>https://api.eu.mailgun.net</c>.
    /// </summary>
    Eu = 2,
}
