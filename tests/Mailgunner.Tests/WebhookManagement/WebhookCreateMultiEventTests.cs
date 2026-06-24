using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

public class WebhookCreateMultiEventTests
{
    private static readonly WebhookEventType[] ThreeEvents =
    {
        WebhookEventType.Delivered, WebhookEventType.Opened, WebhookEventType.Clicked,
    };

    [Fact]
    public async Task One_url_across_several_event_types_fans_out_to_one_create_each_in_order()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, WebhookHarness.Envelope("https://a"));
        var client = WebhookHarness.BuildClient(stub);

        var registrations = await client.Webhooks.CreateAsync(ThreeEvents, "https://a");

        Assert.Equal(3, stub.Requests.Count);
        Assert.Equal("delivered", stub.Requests[0].Value("id"));
        Assert.Equal("opened", stub.Requests[1].Value("id"));
        Assert.Equal("clicked", stub.Requests[2].Value("id"));
        foreach (var request in stub.Requests)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://a", Assert.Single(request.Values("url")));
        }

        Assert.Equal(ThreeEvents, registrations.Select(r => r.EventType));
    }

    [Fact]
    public async Task Fan_out_is_fail_fast_with_no_rollback_on_the_first_non_success()
    {
        const string failureBody = "{\"message\":\"bad url\"}";
        // The 2nd create (index 1) returns a permanent 400 (not retried); others would succeed.
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, WebhookHarness.Envelope("https://a"))
        {
            ResponseSelector = index => index == 1 ? (HttpStatusCode.BadRequest, failureBody) : null,
        };
        var client = WebhookHarness.BuildClient(stub);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Webhooks.CreateAsync(ThreeEvents, "https://a"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(failureBody, ex.ResponseBody);
        // Exactly two requests issued: 'delivered' succeeded, 'opened' failed; 'clicked' never sent.
        Assert.Equal(2, stub.Requests.Count);
        Assert.Equal("delivered", stub.Requests[0].Value("id"));
        Assert.Equal("opened", stub.Requests[1].Value("id"));
    }
}
