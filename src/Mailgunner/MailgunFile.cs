namespace Mailgunner;

/// <summary>
/// A file carried by a send — either an <see cref="MailgunMessage.Attachments"/> entry (delivered as a
/// downloadable attachment) or an <see cref="MailgunMessage.InlineFiles"/> entry (delivered as an
/// embedded file referenceable from the HTML body by content id). Each file is emitted as its own
/// <c>multipart/form-data</c> file part carrying its <see cref="FileName"/> and content type.
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
    /// Gets the file name carried on the file part.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the raw file bytes.
    /// </summary>
    public byte[] Content { get; }

    /// <summary>
    /// Gets the optional content (MIME) type. When null or blank, <c>application/octet-stream</c> is
    /// used on the wire.
    /// </summary>
    public string? ContentType { get; }
}
