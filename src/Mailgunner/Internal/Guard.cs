namespace Mailgunner.Internal;

/// <summary>
/// Internal argument-guard helpers that compile on every target framework. Centralizes the one
/// API difference (<c>ArgumentNullException.ThrowIfNull</c> exists only on modern targets).
/// </summary>
internal static class Guard
{
    /// <summary>
    /// Throws <see cref="System.ArgumentNullException"/> when <paramref name="argument"/> is null.
    /// </summary>
    /// <typeparam name="T">The reference type of the argument.</typeparam>
    /// <param name="argument">The argument to check.</param>
    /// <param name="paramName">The parameter name reported in the exception.</param>
    public static void NotNull<T>(T? argument, string paramName)
        where T : class
    {
#if NET8_0_OR_GREATER
        System.ArgumentNullException.ThrowIfNull(argument, paramName);
#else
        if (argument is null)
        {
            throw new System.ArgumentNullException(paramName);
        }
#endif
    }
}
