using Microsoft.Extensions.Options;

namespace Mailgunner.Internal;

/// <summary>
/// Validates <see cref="MailgunnerOptions"/> at startup, producing clear, secret-safe messages.
/// The sending key value is never included in any failure message.
/// </summary>
internal sealed class MailgunnerOptionsValidator : IValidateOptions<MailgunnerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MailgunnerOptions options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("Mailgunner options must be provided.");
        }

        var failures = new System.Collections.Generic.List<string>();

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
            if (retry.MaxRetryAttempts < 0)
            {
                failures.Add("The maximum retry attempts must be zero or greater (MailgunnerOptions.Retry.MaxRetryAttempts).");
            }

            if (retry.BaseDelay <= System.TimeSpan.Zero)
            {
                failures.Add("The retry base delay must be greater than zero (MailgunnerOptions.Retry.BaseDelay).");
            }

            if (retry.MaxSingleWait < retry.BaseDelay)
            {
                failures.Add("The maximum single wait must be greater than or equal to the base delay (MailgunnerOptions.Retry.MaxSingleWait).");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
