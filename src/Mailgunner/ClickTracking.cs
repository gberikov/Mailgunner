namespace Mailgunner;

/// <summary>
/// Click-tracking mode for a send. Maps to Mailgun's <c>o:tracking-clicks</c> option. When the
/// corresponding option is left unset (null), the field is omitted entirely and the account default
/// applies.
/// </summary>
public enum ClickTracking
{
    /// <summary>Track clicks in both HTML and plain-text parts. Emitted as <c>yes</c>.</summary>
    Yes,

    /// <summary>Disable click tracking for this send. Emitted as <c>no</c>.</summary>
    No,

    /// <summary>Track clicks in HTML parts only, leaving plain-text links untouched. Emitted as <c>htmlonly</c>.</summary>
    HtmlOnly,
}
