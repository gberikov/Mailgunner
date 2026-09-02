namespace Mailgunner.Internal;

/// <summary>Character-class checks shared by the address, header, and option validators.</summary>
internal static class TextGuards
{
    /// <summary>Returns whether <paramref name="value"/> contains any Unicode control character (including CR/LF and TAB).</summary>
    /// <param name="value">The text to inspect.</param>
    /// <returns><see langword="true"/> when a control character is present.</returns>
    public static bool ContainsControlCharacter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether <paramref name="value"/> contains a carriage return or line feed.</summary>
    /// <param name="value">The text to inspect.</param>
    /// <returns><see langword="true"/> when a line break is present.</returns>
    public static bool ContainsLineBreak(string value)
    {
        foreach (var c in value)
        {
            if (c == '\r' || c == '\n')
            {
                return true;
            }
        }

        return false;
    }
}
