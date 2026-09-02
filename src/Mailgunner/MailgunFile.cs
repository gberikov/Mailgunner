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
    /// <exception cref="System.ArgumentException"><paramref name="fileName"/> is null, empty, or whitespace.</exception>
    /// <exception cref="System.ArgumentNullException"><paramref name="content"/> is null.</exception>
    public MailgunFile(string fileName, byte[] content, string? contentType = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new System.ArgumentException("A file name is required.", nameof(fileName));
        }

        Content = content ?? throw new System.ArgumentNullException(nameof(content));
        FileName = fileName;
        ContentType = contentType;
    }

    /// <summary>
    /// Initializes a stream-backed file. <paramref name="openContent"/> is invoked once per request that
    /// carries the file (each batch chunk and each retry), so it must return a fresh readable stream every
    /// time; the library disposes each stream after copying it.
    /// </summary>
    /// <param name="fileName">The file name carried on the file part. Required, non-blank.</param>
    /// <param name="openContent">Opens a fresh stream over the content. Required.</param>
    /// <param name="contentType">The optional MIME type; <c>application/octet-stream</c> when null/blank.</param>
    /// <param name="length">The optional content length, letting the request carry <c>Content-Length</c> instead of chunked encoding.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fileName"/> is null, empty, or whitespace.</exception>
    /// <exception cref="System.ArgumentNullException"><paramref name="openContent"/> is null.</exception>
    public MailgunFile(string fileName, System.Func<System.IO.Stream> openContent, string? contentType = null, long? length = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new System.ArgumentException("A file name is required.", nameof(fileName));
        }

        OpenContent = openContent ?? throw new System.ArgumentNullException(nameof(openContent));
        FileName = fileName;
        ContentType = contentType;
        Length = length;
    }

    /// <summary>
    /// Gets the file name carried on the file part.
    /// </summary>
    public string FileName { get; }

    /// <summary>Gets the raw file bytes, or <see langword="null"/> for a stream-backed file.</summary>
    public byte[]? Content { get; }

    /// <summary>Gets the stream factory, or <see langword="null"/> for a byte-array file.</summary>
    public System.Func<System.IO.Stream>? OpenContent { get; }

    /// <summary>Gets the declared length of a stream-backed file, when known.</summary>
    public long? Length { get; }

    /// <summary>
    /// Gets the optional content (MIME) type. When null or blank, <c>application/octet-stream</c> is
    /// used on the wire.
    /// </summary>
    public string? ContentType { get; }
}
