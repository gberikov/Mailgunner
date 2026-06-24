using Xunit;

namespace Mailgunner.Tests;

/// <summary>
/// Toolchain smoke tests. These run fully offline — no network and no credentials — and
/// prove that the library is referenced, built, and packaged correctly (constitution
/// Principle III: Test-First, Network-Free Tests).
/// </summary>
public class SmokeTests
{
    [Fact]
    public void Library_is_referenced_and_exposes_its_public_client_contract()
    {
        var assembly = typeof(IMailgunnerClient).Assembly;

        Assert.Equal("Mailgunner", assembly.GetName().Name);
    }
}
