using System.Net;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

/// <summary>
/// Confirms every operation surfaces the single <see cref="MailgunnerException"/> on a non-2xx response,
/// exposing the status code and the verbatim response body — and no other exception type.
/// </summary>
public class WebhookErrorTests
{
    private const string Body = "{\"message\":\"boom\"}";

    [Fact]
    public async Task List_surfaces_status_and_raw_body()
    {
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.BadRequest, Body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.Webhooks.ListAsync());

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(Body, ex.ResponseBody);
    }

    [Fact]
    public async Task Get_surfaces_status_and_raw_body()
    {
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.UnprocessableEntity, Body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Webhooks.GetAsync(WebhookEventType.Delivered));

        Assert.Equal(422, ex.StatusCode);
        Assert.Equal(Body, ex.ResponseBody);
    }

    [Fact]
    public async Task Create_surfaces_status_and_raw_body()
    {
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.BadRequest, Body);

        var urls = new[] { "https://a" };
        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Webhooks.CreateAsync(WebhookEventType.Delivered, urls));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(Body, ex.ResponseBody);
    }

    [Fact]
    public async Task Update_surfaces_status_and_raw_body()
    {
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.BadRequest, Body);

        var urls = new[] { "https://a" };
        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Webhooks.UpdateAsync(WebhookEventType.Delivered, urls));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(Body, ex.ResponseBody);
    }

    [Fact]
    public async Task Delete_surfaces_status_and_raw_body()
    {
        var (client, _) = WebhookHarness.BuildClient(HttpStatusCode.BadRequest, Body);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(
            () => client.Webhooks.DeleteAsync(WebhookEventType.Delivered));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(Body, ex.ResponseBody);
    }
}
