using Xunit;

namespace Mailgunner.Tests;

public class EmailAddressTests
{
    [Fact]
    public void ToString_formats_display_name_and_address()
    {
        var address = new EmailAddress("a@b.com", "Bob");

        Assert.Equal("Bob <a@b.com>", address.ToString());
    }

    [Fact]
    public void ToString_formats_bare_address_without_display_name()
    {
        var address = new EmailAddress("a@b.com");

        Assert.Equal("a@b.com", address.ToString());
    }

    [Fact]
    public void Implicit_conversion_from_string_carries_the_address()
    {
        EmailAddress address = "a@b.com";

        Assert.Equal("a@b.com", address.Address);
        Assert.Null(address.DisplayName);
    }

    [Fact]
    public void Value_equality_holds_for_matching_address_and_display_name()
    {
        var first = new EmailAddress("a@b.com", "Bob");
        var second = new EmailAddress("a@b.com", "Bob");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Value_equality_distinguishes_different_display_names()
    {
        var first = new EmailAddress("a@b.com", "Bob");
        var second = new EmailAddress("a@b.com", "Alice");

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_address_throws_ArgumentException(string? address)
    {
        Assert.Throws<ArgumentException>(() => new EmailAddress(address!));
    }
}
