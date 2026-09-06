using Microsoft.Extensions.Options;

namespace Mailgunner.Internal;

/// <summary>
/// Validates <see cref="MailgunnerOptions"/> at startup, producing clear, secret-safe messages.
/// The sending key value is never included in any failure message.
/// </summary>
internal sealed class MailgunnerOptionsValidator : IValidateOptions<MailgunnerOptions>
{
    // Both TFMs' timer APIs reject a delay above their ceiling (Int32.MaxValue ms ≈ 24.9 days on
    // .NET Framework, ~49.7 days on .NET 8). Bound well below the lower one so any accepted
    // configuration is valid on every target the library ships for.
    private static readonly TimeSpan MaxAllowedDuration = TimeSpan.FromDays(1);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MailgunnerOptions options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("Mailgunner options must be provided.");
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Domain))
        {
            failures.Add("A Mailgun domain must be provided (MailgunnerOptions.Domain).");
        }

        if (string.IsNullOrWhiteSpace(options.SendingKey))
        {
            failures.Add("A Mailgun sending key must be provided (MailgunnerOptions.SendingKey).");
        }

#if NET8_0_OR_GREATER
        if (!System.Enum.IsDefined(options.Region))
#else
        if (!System.Enum.IsDefined(typeof(MailgunRegion), options.Region))
#endif
        {
            failures.Add("A valid Mailgun region must be specified (MailgunnerOptions.Region): US or EU.");
        }

        var retry = options.Retry;
        if (retry is null)
        {
            failures.Add("Retry options must be provided (MailgunnerOptions.Retry).");
        }
        else
        {
#if NET8_0_OR_GREATER
            if (!Enum.IsDefined(retry.SendRetryMode))
#else
            if (!Enum.IsDefined(typeof(SendRetryMode), retry.SendRetryMode))
#endif
            {
                failures.Add("A valid send retry mode must be specified (MailgunnerOptions.Retry.SendRetryMode): Safe or Full.");
            }

            if (retry.MaxRetryAttempts < 0)
            {
                failures.Add("The maximum retry attempts must be zero or greater (MailgunnerOptions.Retry.MaxRetryAttempts).");
            }

            if (retry.MaxRetryAttempts > RetryPolicyOptions.MaxAllowedRetryAttempts)
            {
                failures.Add($"The maximum retry attempts must not exceed {RetryPolicyOptions.MaxAllowedRetryAttempts} (MailgunnerOptions.Retry.MaxRetryAttempts).");
            }

            if (retry.BaseDelay <= TimeSpan.Zero)
            {
                failures.Add("The retry base delay must be greater than zero (MailgunnerOptions.Retry.BaseDelay).");
            }

            if (retry.MaxSingleWait < retry.BaseDelay)
            {
                failures.Add("The maximum single wait must be greater than or equal to the base delay (MailgunnerOptions.Retry.MaxSingleWait).");
            }

            if (retry.MaxSingleWait > MaxAllowedDuration)
            {
                failures.Add($"The maximum single wait must not exceed {MaxAllowedDuration} (MailgunnerOptions.Retry.MaxSingleWait): above that, .NET Framework 4.8's ~24.9-day timer ceiling is exceeded and the wait cannot be scheduled.");
            }

            if (retry.AttemptTimeout <= TimeSpan.Zero)
            {
                failures.Add("The attempt timeout must be greater than zero (MailgunnerOptions.Retry.AttemptTimeout).");
            }

            if (retry.AttemptTimeout > MaxAllowedDuration)
            {
                failures.Add($"The attempt timeout must not exceed {MaxAllowedDuration} (MailgunnerOptions.Retry.AttemptTimeout): above that, .NET Framework 4.8's ~24.9-day timer ceiling is exceeded and the first attempt of every request fails.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
