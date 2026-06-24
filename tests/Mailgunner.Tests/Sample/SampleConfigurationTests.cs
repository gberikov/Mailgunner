using Mailgunner.Sample;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Mailgunner.Tests.Sample;

/// <summary>
/// Offline coverage for the sample's credential-presence resolver (FR-003 / SC-002). No network,
/// no live send: a complete configuration resolves to typed settings; any absent/blank/unparseable
/// required setting yields an ordered list of the exact missing keys plus where-to-supply guidance
/// and never resolves.
/// </summary>
public sealed class SampleConfigurationTests
{
    private static IConfiguration Build(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> CompleteValues() => new()
    {
        ["Mailgun:Domain"] = "sandbox123.mailgun.org",
        ["Mailgun:SendingKey"] = "key-secret",
        ["Mailgun:Region"] = "Us",
        ["Mailgun:Recipients:0:Address"] = "dev1@example.com",
        ["Mailgun:Recipients:1:Address"] = "dev2@example.com",
    };

    [Fact]
    public void Resolve_CompleteConfig_ResolvesTypedSettings()
    {
        var result = SampleConfiguration.Resolve(Build(CompleteValues()));

        Assert.True(result.IsResolved);
        Assert.Empty(result.Missing);
        var s = Assert.IsType<SampleSettings>(result.Settings);
        Assert.Equal("sandbox123.mailgun.org", s.Domain);
        Assert.Equal("key-secret", s.SendingKey);
        Assert.Equal(MailgunRegion.Us, s.Region);
        Assert.Equal(2, s.Recipients.Count);
        Assert.Equal("dev1@example.com", s.Recipients[0].Address);
        Assert.Equal("dev2@example.com", s.Recipients[1].Address);
    }

    [Fact]
    public void Resolve_CompleteConfig_AppliesFromAndTemplateDefaults()
    {
        var values = CompleteValues();
        // No From, no Template supplied → defaults apply.
        var result = SampleConfiguration.Resolve(Build(values));

        Assert.True(result.IsResolved);
        Assert.Equal("postmaster@sandbox123.mailgun.org", result.Settings!.From.Address);
        Assert.Equal("conference-invitation", result.Settings.Template);
    }

    [Fact]
    public void Resolve_HonorsExplicitFromAndTemplate()
    {
        var values = CompleteValues();
        values["Mailgun:From"] = "events@sandbox123.mailgun.org";
        values["Mailgun:Template"] = "custom-template";

        var result = SampleConfiguration.Resolve(Build(values));

        Assert.True(result.IsResolved);
        Assert.Equal("events@sandbox123.mailgun.org", result.Settings!.From.Address);
        Assert.Equal("custom-template", result.Settings.Template);
    }

    [Theory]
    [InlineData("Mailgun:Domain")]
    [InlineData("Mailgun:SendingKey")]
    [InlineData("Mailgun:Region")]
    public void Resolve_MissingRequiredKey_NeverResolvesAndNamesIt(string missingKey)
    {
        var values = CompleteValues();
        values.Remove(missingKey);

        var result = SampleConfiguration.Resolve(Build(values));

        Assert.False(result.IsResolved);
        Assert.Null(result.Settings);
        var entry = Assert.Single(result.Missing, m => m.Key == missingKey);
        Assert.False(string.IsNullOrWhiteSpace(entry.Guidance));
    }

    [Theory]
    [InlineData("Mailgun:Domain")]
    [InlineData("Mailgun:SendingKey")]
    [InlineData("Mailgun:Region")]
    public void Resolve_BlankRequiredKey_NeverResolvesAndNamesIt(string blankKey)
    {
        var values = CompleteValues();
        values[blankKey] = "   ";

        var result = SampleConfiguration.Resolve(Build(values));

        Assert.False(result.IsResolved);
        Assert.Contains(result.Missing, m => m.Key == blankKey);
    }

    [Fact]
    public void Resolve_UnparseableRegion_ReportedAsMissing()
    {
        var values = CompleteValues();
        values["Mailgun:Region"] = "Mars";

        var result = SampleConfiguration.Resolve(Build(values));

        Assert.False(result.IsResolved);
        Assert.Contains(result.Missing, m => m.Key == "Mailgun:Region");
    }

    [Fact]
    public void Resolve_MissingRecipients_NeverResolvesAndNamesThem()
    {
        var values = CompleteValues();
        values.Remove("Mailgun:Recipients:0:Address");
        values.Remove("Mailgun:Recipients:1:Address");

        var result = SampleConfiguration.Resolve(Build(values));

        Assert.False(result.IsResolved);
        Assert.Null(result.Settings);
        Assert.Contains(result.Missing, m => m.Key.StartsWith("Mailgun:Recipients", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_PartialConfig_NeverResolvesAndListsAllMissing()
    {
        // Only the domain is present.
        var values = new Dictionary<string, string?> { ["Mailgun:Domain"] = "sandbox123.mailgun.org" };

        var result = SampleConfiguration.Resolve(Build(values));

        Assert.False(result.IsResolved);
        Assert.Null(result.Settings);
        Assert.Contains(result.Missing, m => m.Key == "Mailgun:SendingKey");
        Assert.Contains(result.Missing, m => m.Key == "Mailgun:Region");
        Assert.Contains(result.Missing, m => m.Key.StartsWith("Mailgun:Recipients", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Missing, m => m.Key == "Mailgun:Domain");
        Assert.All(result.Missing, m => Assert.False(string.IsNullOrWhiteSpace(m.Guidance)));
    }

    [Fact]
    public void Resolve_EmptyConfig_NamesEveryRequiredSettingInOrder()
    {
        var result = SampleConfiguration.Resolve(Build(new Dictionary<string, string?>()));

        Assert.False(result.IsResolved);
        var keys = result.Missing.Select(m => m.Key).ToList();
        Assert.Equal("Mailgun:Domain", keys[0]);
        Assert.Equal("Mailgun:SendingKey", keys[1]);
        Assert.Equal("Mailgun:Region", keys[2]);
        Assert.Contains(keys, k => k.StartsWith("Mailgun:Recipients", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_NullConfiguration_Throws() =>
        Assert.Throws<ArgumentNullException>(() => SampleConfiguration.Resolve(null!));
}
