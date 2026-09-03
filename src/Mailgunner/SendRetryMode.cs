namespace Mailgunner;

/// <summary>
/// How automatic retry treats a message send (<c>POST /messages</c>), which is not idempotent: if the
/// service accepted the message but the response was lost, a retry delivers the email again. Requests
/// to the suppression and webhook endpoints are unaffected by this setting and always use the full policy.
/// </summary>
public enum SendRetryMode
{
    /// <summary>
    /// Retry a send only on HTTP <c>429</c> (rate limited, the message was not accepted). Timeouts,
    /// transport faults, <c>408</c>, and <c>5xx</c> surface after a single attempt. The default.
    /// </summary>
    Safe = 0,

    /// <summary>
    /// Retry a send under the same rules as every other request (<c>429</c>/<c>408</c>/<c>5xx</c> and
    /// transient transport faults). Accepts the risk of duplicate delivery in exchange for fewer surfaced failures.
    /// </summary>
    Full = 1,
}
