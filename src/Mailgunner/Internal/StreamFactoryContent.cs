namespace Mailgunner.Internal;

/// <summary>
/// An <see cref="HttpContent"/> that opens a fresh stream from a factory each time it is
/// serialized, so the same part can be sent again on a retry without buffering the whole file.
/// </summary>
internal sealed class StreamFactoryContent : HttpContent
{
    private readonly Func<Stream> _open;
    private readonly long? _length;

    /// <summary>Initializes a new instance of the <see cref="StreamFactoryContent"/> class.</summary>
    /// <param name="open">Opens a fresh stream over the content; invoked once per serialization.</param>
    /// <param name="length">The optional declared content length.</param>
    public StreamFactoryContent(Func<Stream> open, long? length)
    {
        _open = open;
        _length = length;
    }

    /// <inheritdoc/>
    protected override async Task SerializeToStreamAsync(
        Stream stream, System.Net.TransportContext? context)
    {
        using var source = _open() ?? throw new InvalidOperationException(
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
