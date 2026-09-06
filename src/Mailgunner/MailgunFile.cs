namespace Mailgunner;

/// <summary>
/// A file carried by a send — either an <see cref="MailgunMessage.Attachments"/> entry (delivered as a
/// downloadable attachment) or an <see cref="MailgunMessage.InlineFiles"/> entry (delivered as an
/// embedded file referenceable from the HTML body by content id). Each file is emitted as its own
/// <c>multipart/form-data</c> file part carrying its <see cref="FileName"/> and content type. Backed
/// either by an in-memory <see cref="Content"/> byte array or, for large files, by an
/// <see cref="OpenContent"/> stream factory that is opened fresh for every request that carries it.
/// </summary>
public sealed class MailgunFile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MailgunFile"/> class.
    /// </summary>
    /// <param name="fileName">The file name carried on the file part. Required, non-blank.</param>
    /// <param name="content">The raw file bytes. Required (may be empty). The array is referenced, not copied.</param>
    /// <param name="contentType">
    /// The optional content (MIME) type. When null or blank, <c>application/octet-stream</c> is used on
    /// the wire; the file name is not inspected to infer a type.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is null, empty, or whitespace, or contains a control character or a double quote.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public MailgunFile(string fileName, byte[] content, string? contentType = null)
    {
        ValidateFileName(fileName);
        Content = content ?? throw new ArgumentNullException(nameof(content));
        FileName = fileName;
        ContentType = contentType;
    }

    /// <summary>
    /// Initializes a stream-backed file. <paramref name="openContent"/> is invoked once per request that
    /// carries the file (each batch chunk and each retry), so it must return a fresh readable stream every
    /// time; the library disposes each stream after copying it, including on cancellation or failure.
    /// On .NET 8 and later the source copy receives the request/attempt cancellation token. The
    /// netstandard2.0 HttpContent API has no cancellation-aware serialization overload, so a stalled
    /// source read on that target must enforce its own timeout. The synchronous factory itself must
    /// return promptly on every target.
    /// </summary>
    /// <param name="fileName">The file name carried on the file part. Required, non-blank.</param>
    /// <param name="openContent">Opens a fresh stream over the content. Required.</param>
    /// <param name="contentType">The optional MIME type; <c>application/octet-stream</c> when null/blank.</param>
    /// <param name="length">The optional content length, letting the request carry <c>Content-Length</c> instead of chunked encoding.</param>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is null, empty, or whitespace, or contains a control character or a double quote.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="openContent"/> is null.</exception>
    public MailgunFile(string fileName, Func<Stream> openContent, string? contentType = null, long? length = null)
    {
        ValidateFileName(fileName);
        OpenContent = openContent ?? throw new ArgumentNullException(nameof(openContent));
        FileName = fileName;
        ContentType = contentType;
        Length = length;
    }

    /// <summary>
    /// The file name is carried in the part's <c>Content-Disposition</c> header, where a control character
    /// or a double quote is either a header-injection vector or a value the transport rejects late, at send
    /// time, with a <see cref="FormatException"/>. Reject both up front under the library's own contract.
    /// </summary>
    private static readonly char[] Quote = { '"' };

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("A file name is required.", nameof(fileName));
        }

        // IndexOfAny: string.Contains(char) does not exist on netstandard2.0.
        if (Internal.TextGuards.ContainsControlCharacter(fileName) || fileName.IndexOfAny(Quote) >= 0)
        {
            throw new ArgumentException(
                "A file name must not contain control characters or double quotes.", nameof(fileName));
        }
    }

    /// <summary>
    /// Gets the file name carried on the file part.
    /// </summary>
    public string FileName { get; }

    /// <summary>Gets the raw file bytes, or <see langword="null"/> for a stream-backed file.</summary>
    public byte[]? Content { get; }

    /// <summary>Gets the stream factory, or <see langword="null"/> for a byte-array file.</summary>
    public Func<Stream>? OpenContent { get; }

    /// <summary>Gets the declared length of a stream-backed file, when known.</summary>
    public long? Length { get; }

    /// <summary>
    /// Gets the optional content (MIME) type. When null or blank, <c>application/octet-stream</c> is
    /// used on the wire.
    /// </summary>
    public string? ContentType { get; }
}
