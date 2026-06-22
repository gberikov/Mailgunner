namespace Mailgunner;

/// <summary>
/// Settings used to register and configure the Mailgunner client. Supply these through an
/// <c>AddMailgunner</c> registration call or bind them from configuration.
/// </summary>
public sealed class MailgunnerOptions
{
    /// <summary>
    /// Gets or sets the Mailgun sending domain (for example, <c>"mg.example.com"</c>). Required.
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Mailgun sending key used for HTTP Basic authentication. Required, and
    /// treated as a secret: supply it from configuration or environment, never hard-coded.
    /// </summary>
    public string SendingKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Mailgun hosting region that selects the API base URL. Required.
    /// </summary>
    public MailgunRegion Region { get; set; }
}
