using Xunit;

namespace Mailgunner.Tests.Sending;

/// <summary>
/// A file name travels inside the multipart part's <c>Content-Disposition</c> header. A control character
/// or a double quote there would otherwise surface late, at send time, as a transport-level
/// <see cref="FormatException"/> instead of the library's <see cref="ArgumentException"/> contract.
/// </summary>
public class FileNameValidationTests
{
    [Theory]
    [InlineData("a.txt\r\nX-Injected: yes")]
    [InlineData("a\"b.txt")]
    [InlineData("a\tb.txt")]
    public void Byte_array_file_rejects_control_characters_and_quotes_in_the_file_name(string fileName)
    {
        Assert.Throws<ArgumentException>(() => new MailgunFile(fileName, new byte[] { 1 }));
    }

    [Theory]
    [InlineData("a.txt\r\nX-Injected: yes")]
    [InlineData("a\"b.txt")]
    [InlineData("a\tb.txt")]
    public void Stream_backed_file_rejects_control_characters_and_quotes_in_the_file_name(string fileName)
    {
        Assert.Throws<ArgumentException>(() => new MailgunFile(fileName, () => Stream.Null));
    }

    [Theory]
    [InlineData("report 2026 (final).pdf")]
    [InlineData("отчёт;итог.pdf")]
    public void Ordinary_file_names_are_accepted(string fileName)
    {
        Assert.Equal(fileName, new MailgunFile(fileName, new byte[] { 1 }).FileName);
    }
}
