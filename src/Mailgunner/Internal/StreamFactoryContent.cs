namespace Mailgunner.Internal;

/// <summary>
/// An <see cref="System.Net.Http.HttpContent"/> that opens a fresh stream from a factory each time it is
/// serialized, so the same part can be sent again on a retry without buffering the whole file.
/// </summary>
internal sealed class StreamFactoryContent : System.Net.Http.HttpContent
{
    private readonly System.Func<System.IO.Stream> _open;
    private readonly long? _length;

    /// <summary>Initializes a new instance of the <see cref="StreamFactoryContent"/> class.</summary>
    /// <param name="open">Opens a fresh stream over the content; invoked once per serialization.</param>
    /// <param name="length">The optional declared content length.</param>
    public StreamFactoryContent(System.Func<System.IO.Stream> open, long? length)
    {
        _open = open;
        _length = length;
    }

    /// <inheritdoc/>
    protected override async System.Threading.Tasks.Task SerializeToStreamAsync(
        System.IO.Stream stream, System.Net.TransportContext? context)
    {
        using var source = _open() ?? throw new System.InvalidOperationException(
            "The MailgunFile stream factory (OpenContent) returned null; it must return a fresh readable stream.");
        await source.CopyToAsync(stream).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override bool TryComputeLength(out long length)
    {
        length = _length ?? 0;
        return _length.HasValue;
    }
}
