namespace Mailgunner;

/// <summary>
/// A snapshot of a failed or canceled batch. Retrieve it with <see cref="FromException"/> without
/// changing how transport, HTTP, serialization, or cancellation exceptions are caught.
/// </summary>
public sealed class BatchSendProgress
{
    private const string ExceptionDataKey = "Mailgunner.BatchSendProgress";

    private BatchSendProgress(int failedChunkIndex, IReadOnlyList<SendResult> acceptedResults, bool requestStarted)
    {
        FailedChunkIndex = failedChunkIndex;
        AcceptedResults = new List<SendResult>(acceptedResults).AsReadOnly();
        RequestStarted = requestStarted;
    }

    /// <summary>Gets the zero-based index of the chunk at which processing stopped.</summary>
    public int FailedChunkIndex { get; }

    /// <summary>
    /// Gets an immutable snapshot of the successfully parsed acceptance responses for preceding chunks.
    /// These chunks must not be sent again when resuming the batch.
    /// </summary>
    public IReadOnlyList<SendResult> AcceptedResults { get; }

    /// <summary>
    /// Gets whether the failing chunk entered the HTTP send path. When false, it was not sent.
    /// When true, acceptance may be unknown: a timeout, cancellation, lost response, or malformed
    /// success response does not prove that Mailgun rejected the chunk. Do not retry it blindly.
    /// </summary>
    public bool RequestStarted { get; }

    /// <summary>
    /// Reads the progress attached to an exception thrown while processing a batch chunk. Returns null
    /// for unrelated exceptions and for initial batch-validation errors, before any chunks are processed.
    /// The original exception type, stack trace, and cancellation token are preserved.
    /// </summary>
    /// <param name="exception">The exception caught from a batch operation.</param>
    /// <returns>The batch progress, or null when this exception carries no batch progress.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static BatchSendProgress? FromException(Exception exception)
    {
        Internal.Guard.NotNull(exception, nameof(exception));
        return exception.Data[ExceptionDataKey] as BatchSendProgress;
    }

    internal static void Attach(Exception exception, int chunkIndex, IReadOnlyList<SendResult> results, bool requestStarted) =>
        exception.Data[ExceptionDataKey] = new BatchSendProgress(chunkIndex, results, requestStarted);
}
