using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class TemplateSenderAndAmpTests
{
    [Fact]
    public async Task Amp_only_body_is_sent()
    {
        var stub = NewStub();
        using var provider = BuildProvider(stub);
        var message = new MailgunMessage { From = "a@example.com", AmpHtml = "<html amp4email>AMP</html>" };
        message.To.Add("b@example.com");

        await provider.GetRequiredService<IMailgunnerClient>().SendAsync(message);

        var request = Assert.Single(stub.Requests);
        Assert.Equal(message.AmpHtml, request.Value("amp-html"));
        Assert.Null(request.Value("html"));
        Assert.Null(request.Value("text"));
    }

    [Fact]
    public async Task Template_sender_is_inherited_when_from_is_omitted()
    {
        var stub = NewStub();
        using var provider = BuildProvider(stub);
        var message = new MailgunMessage { Template = "with-sender" };
        message.To.Add("b@example.com");

        await provider.GetRequiredService<IMailgunnerClient>().SendAsync(message);

        var request = Assert.Single(stub.Requests);
        Assert.Null(request.Value("from"));
        Assert.Equal("with-sender", request.Value("template"));
    }

    [Fact]
    public async Task Batch_omits_from_on_every_chunk_when_the_template_supplies_it()
    {
        var stub = NewStub();
        using var provider = BuildProvider(stub);
        var batch = new MailgunBatchMessage { Template = "with-sender" };
        for (var i = 0; i < 1001; i++)
        {
            batch.Recipients.Add(new BatchRecipient($"user{i}@example.com"));
        }

        await provider.GetRequiredService<IMailgunnerClient>().SendBatchAsync(batch);

        Assert.Equal(2, stub.Requests.Count);
        Assert.All(stub.Requests, request => Assert.Null(request.Value("from")));
    }

    private static StubHttpMessageHandler NewStub() => new(HttpStatusCode.OK, "{\"id\":\"id\",\"message\":\"Queued\"}");

    private static ServiceProvider BuildProvider(StubHttpMessageHandler stub)
    {
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-test", MailgunRegion.Us)
            .ConfigurePrimaryHttpMessageHandler(() => stub);
        return services.BuildServiceProvider();
    }
}
