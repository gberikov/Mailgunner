using Microsoft.Extensions.Configuration;

namespace Mailgunner.Sample;

/// <summary>
/// One required setting that was absent, blank, or unparseable, together with guidance on where to
/// supply it. Carries no secret value.
/// </summary>
public sealed class MissingSetting
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MissingSetting"/> class.
    /// </summary>
    /// <param name="key">The configuration key (for example <c>Mailgun:Domain</c>).</param>
    /// <param name="guidance">Where to supply the value (environment variable + user-secrets command).</param>
    public MissingSetting(string key, string guidance)
    {
        Key = key;
        Guidance = guidance;
    }

    /// <summary>Gets the configuration key that is missing.</summary>
    public string Key { get; }

    /// <summary>Gets human-readable guidance on where to supply the value. Never contains a secret.</summary>
    public string Guidance { get; }
}

/// <summary>
/// The resolved, typed settings the sample needs to perform a personalized batch send. Produced only
/// when every required setting is present and valid.
/// </summary>
public sealed class SampleSettings
{
    /// <summary>Initializes a new instance of the <see cref="SampleSettings"/> class.</summary>
    public SampleSettings(
        string domain,
        string sendingKey,
        MailgunRegion region,
        EmailAddress from,
        string template,
        IReadOnlyList<EmailAddress> recipients)
    {
        Domain = domain;
        SendingKey = sendingKey;
        Region = region;
        From = from;
        Template = template;
        Recipients = recipients;
    }

    /// <summary>Gets the Mailgun sandbox sending domain.</summary>
    public string Domain { get; }

    /// <summary>Gets the Mailgun sending key (secret; never logged or printed).</summary>
    public string SendingKey { get; }

    /// <summary>Gets the hosting region (must match the domain's region).</summary>
    public MailgunRegion Region { get; }

    /// <summary>Gets the sender address. Defaults to <c>postmaster@{Domain}</c> when not supplied.</summary>
    public EmailAddress From { get; }

    /// <summary>Gets the stored Handlebars template name. Defaults to <c>conference-invitation</c>.</summary>
    public string Template { get; }

    /// <summary>Gets the configured recipient addresses (at least one).</summary>
    public IReadOnlyList<EmailAddress> Recipients { get; }
}

/// <summary>
/// The outcome of resolving the sample's configuration: either a complete <see cref="SampleSettings"/>
/// (<see cref="IsResolved"/> is <see langword="true"/>) or an ordered list of <see cref="Missing"/>
/// settings. Never both.
/// </summary>
public sealed class SampleConfigurationResult
{
    private SampleConfigurationResult(SampleSettings? settings, IReadOnlyList<MissingSetting> missing)
    {
        Settings = settings;
        Missing = missing;
    }

    /// <summary>Gets a value indicating whether every required setting was present and valid.</summary>
    public bool IsResolved => Settings is not null;

    /// <summary>Gets the resolved settings when <see cref="IsResolved"/>; otherwise <see langword="null"/>.</summary>
    public SampleSettings? Settings { get; }

    /// <summary>Gets the ordered list of missing/invalid settings. Empty when <see cref="IsResolved"/>.</summary>
    public IReadOnlyList<MissingSetting> Missing { get; }

    internal static SampleConfigurationResult Resolved(SampleSettings settings) =>
        new(settings, Array.Empty<MissingSetting>());

    internal static SampleConfigurationResult Incomplete(IReadOnlyList<MissingSetting> missing) =>
        new(null, missing);
}

/// <summary>
/// Pure, network-free resolver that maps an <see cref="IConfiguration"/> (bound from environment
/// variables, optional user-secrets, and <c>appsettings.json</c>) to either typed
/// <see cref="SampleSettings"/> or an ordered list of the exact missing settings and where to supply
/// them (FR-003 / SC-002). Performs no I/O and issues no request.
/// </summary>
public static class SampleConfiguration
{
    /// <summary>The configuration section every sample setting is bound from.</summary>
    public const string SectionName = "Mailgun";

    /// <summary>
    /// Resolves the sample's configuration. A complete, valid configuration yields typed settings;
    /// any absent, blank, or unparseable required setting yields an ordered list of missing keys with
    /// guidance and never resolves (a partial configuration lists exactly what is missing).
    /// </summary>
    /// <param name="configuration">The configuration to resolve from.</param>
    /// <returns>The resolution outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public static SampleConfigurationResult Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var missing = new List<MissingSetting>();

        var domain = Trimmed(configuration["Mailgun:Domain"]);
        if (domain is null)
        {
            missing.Add(Missing("Mailgun:Domain", "the Mailgun sandbox sending domain"));
        }

        var sendingKey = Trimmed(configuration["Mailgun:SendingKey"]);
        if (sendingKey is null)
        {
            missing.Add(Missing("Mailgun:SendingKey", "your Mailgun sending key (prefer a Domain Sending Key)"));
        }

        var regionRaw = Trimmed(configuration["Mailgun:Region"]);
        MailgunRegion region = default;
        if (regionRaw is null)
        {
            missing.Add(Missing("Mailgun:Region", "the hosting region: Us or Eu (must match the domain)"));
        }
        else if (!TryParseRegion(regionRaw, out region))
        {
            missing.Add(Missing(
                "Mailgun:Region",
                $"a valid hosting region: Us or Eu (got \"{regionRaw}\"; must match the domain)"));
        }

        var recipients = ReadRecipients(configuration);
        if (recipients.Count == 0)
        {
            missing.Add(Missing(
                "Mailgun:Recipients:0:Address",
                "at least one authorized sandbox recipient address"));
        }

        if (missing.Count > 0)
        {
            return SampleConfigurationResult.Incomplete(missing);
        }

        var template = Trimmed(configuration["Mailgun:Template"]) ?? "conference-invitation";
        var fromRaw = Trimmed(configuration["Mailgun:From"]);
        var from = fromRaw is not null ? new EmailAddress(fromRaw) : new EmailAddress($"postmaster@{domain}");

        return SampleConfigurationResult.Resolved(
            new SampleSettings(domain!, sendingKey!, region, from, template, recipients));
    }

    private static List<EmailAddress> ReadRecipients(IConfiguration configuration) =>
        configuration.GetSection("Mailgun:Recipients").GetChildren()
            .Select(child => Trimmed(child["Address"]))
            .Where(address => address is not null)
            .Select(address => new EmailAddress(address!))
            .ToList();

    private static bool TryParseRegion(string value, out MailgunRegion region) =>
        Enum.TryParse(value, ignoreCase: true, out region) && Enum.IsDefined(region);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MissingSetting Missing(string key, string what)
    {
        var envVar = key.Replace(":", "__", StringComparison.Ordinal);
        var guidance =
            $"supply {what} — set the {envVar} environment variable, " +
            $"or run: dotnet user-secrets set \"{key}\" \"<value>\"";
        return new MissingSetting(key, guidance);
    }
}
