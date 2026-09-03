using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Mailgunner.Tests.Fakes;
using Mailgunner.Tests.Retry;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class AttachmentTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";
    private const string SuccessBody = "{\"id\":\"<20260624.1@mg.example.com>\",\"message\":\"Queued. Thank you.\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, SendingKey, MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMailgunnerClient>(), stub);
    }

    private static MailgunMessage NewMessage()
    {
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com", "Example"),
            Subject = "Hi",
            Text = "Body",
        };
        message.To.Add("alice@example.com");
        return message;
    }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task Attachment_appears_as_file_part_with_filename_and_content_type()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Attachments.Add(new MailgunFile("ticket.pdf", Bytes("%PDF-1.7"), "application/pdf"));

        await client.SendAsync(message);

        var parts = stub.Requests[0].Fields("attachment");
        var part = Assert.Single(parts);
        Assert.Equal("ticket.pdf", part.FileName);
        Assert.Equal("application/pdf", part.ContentType);
    }

    [Fact]
    public async Task Attachment_without_content_type_defaults_to_octet_stream()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Attachments.Add(new MailgunFile("data.bin", Bytes("binary-data")));

        await client.SendAsync(message);

        var part = Assert.Single(stub.Requests[0].Fields("attachment"));
        Assert.Equal("data.bin", part.FileName);
        Assert.Equal("application/octet-stream", part.ContentType);
    }

    [Fact]
    public async Task Inline_file_appears_under_inline_field_distinct_from_attachment()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.InlineFiles.Add(new MailgunFile("logo.png", Bytes("PNG"), "image/png"));

        await client.SendAsync(message);

        var request = stub.Requests[0];
        Assert.Empty(request.Fields("attachment"));
        var inline = Assert.Single(request.Fields("inline"));
        Assert.Equal("logo.png", inline.FileName);
        Assert.Equal("image/png", inline.ContentType);
    }

    [Fact]
    public async Task Multiple_attachments_and_inline_files_each_get_their_own_part()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Attachments.Add(new MailgunFile("a.pdf", Bytes("a"), "application/pdf"));
        message.Attachments.Add(new MailgunFile("b.csv", Bytes("b"), "text/csv"));
        message.InlineFiles.Add(new MailgunFile("c.png", Bytes("c"), "image/png"));
        message.InlineFiles.Add(new MailgunFile("d.gif", Bytes("d"), "image/gif"));

        await client.SendAsync(message);

        var request = stub.Requests[0];
        var attachments = request.Fields("attachment");
        var inline = request.Fields("inline");
        Assert.Equal(2, attachments.Count);
        Assert.Equal(2, inline.Count);
        Assert.Equal("a.pdf", attachments[0].FileName);
        Assert.Equal("b.csv", attachments[1].FileName);
        Assert.Equal("text/csv", attachments[1].ContentType);
        Assert.Equal("c.png", inline[0].FileName);
        Assert.Equal("d.gif", inline[1].FileName);
    }

    [Fact]
    public void MailgunFile_rejects_blank_filename_and_null_content()
    {
        Assert.Throws<System.ArgumentException>(() => new MailgunFile("", Bytes("x")));
        Assert.Throws<System.ArgumentException>(() => new MailgunFile("   ", Bytes("x")));
        Assert.Throws<System.ArgumentNullException>(() => new MailgunFile("f.bin", (byte[])null!));
    }

    [Fact]
    public async Task Stream_backed_attachment_is_read_from_a_fresh_stream_per_request()
    {
        var (client, stub) = BuildClient();
        var opened = 0;
        var batch = new MailgunBatchMessage { From = "noreply@mg.example.com", Template = "t" };
        for (var i = 0; i < 1001; i++)
        {
            batch.Recipients.Add(new BatchRecipient($"u{i}@example.com"));
        }

        batch.Attachments.Add(new MailgunFile(
            "report.txt",
            () => { opened++; return new MemoryStream(Encoding.UTF8.GetBytes("hello")); },
            "text/plain"));

        await client.SendBatchAsync(batch);

        Assert.Equal(2, stub.Requests.Count);
        Assert.Equal(2, opened);
        Assert.All(stub.Requests, r => Assert.Equal("hello", r.Fields("attachment").Single().Value));
    }

    [Fact]
    public void Stream_file_requires_a_factory()
    {
        Assert.Throws<ArgumentNullException>(() => new MailgunFile("a.txt", (Func<Stream>)null!));
    }

    [Fact]
    public async Task Stream_backed_attachment_survives_a_retry_with_correct_content_on_both_attempts()
    {
        // The first attempt is rejected as transient (429); the resilience pipeline retries with a
        // fresh request. Each request re-serializes the multipart body, so the factory must be called
        // — and produce the right bytes — on both the failed attempt and the retry.
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"id\":\"<20260624.1@mg.example.com>\",\"message\":\"Queued. Thank you.\"}")
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.TooManyRequests, "{\"message\":\"slow down\"}") : null,
        };
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider());
        var opened = 0;
        var message = RetryTestHarness.NewMessage();
        message.Attachments.Add(new MailgunFile(
            "report.txt",
            () => { opened++; return new MemoryStream(Encoding.UTF8.GetBytes("hello")); },
            "text/plain"));

        var result = await client.SendAsync(message);

        Assert.NotNull(result);
        Assert.Equal(2, stub.Requests.Count);
        Assert.Equal(2, opened);
        Assert.All(stub.Requests, r => Assert.Equal("hello", r.Fields("attachment").Single().Value));
    }
}
