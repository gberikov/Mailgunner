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

    [Theory]
    [InlineData("a@b.com\r\nbcc: evil@x.com")]
    [InlineData("a@b.com\nbcc: evil@x.com")]
    [InlineData("a@b.com\tx")]
    public void Address_with_control_characters_throws_ArgumentException(string address)
    {
        Assert.Throws<ArgumentException>(() => new EmailAddress(address));
    }

    [Theory]
    [InlineData("Bob\r\nbcc: evil@x.com")]
    [InlineData("Bob\nEvil")]
    public void Display_name_with_control_characters_throws_ArgumentException(string displayName)
    {
        Assert.Throws<ArgumentException>(() => new EmailAddress("a@b.com", displayName));
    }

    [Fact]
    public void ToString_quotes_a_display_name_containing_specials()
    {
        var address = new EmailAddress("a@b.com", "Doe, John");

        Assert.Equal("\"Doe, John\" <a@b.com>", address.ToString());
    }

    [Fact]
    public void ToString_escapes_quotes_and_backslashes_in_a_quoted_display_name()
    {
        var address = new EmailAddress("a@b.com", "A\"B\\C");

        // Wrapped in quotes; the embedded " and \ are backslash-escaped.
        Assert.Equal("\"A\\\"B\\\\C\" <a@b.com>", address.ToString());
    }
}
