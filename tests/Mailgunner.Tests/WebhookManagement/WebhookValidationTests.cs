using System.Net;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

public class WebhookValidationTests
{
    [Fact]
    public async Task Create_with_empty_urls_throws_and_issues_no_request()
    {
        var (client, stub) = WebhookHarness.BuildClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Webhooks.CreateAsync(WebhookEventType.Delivered, Array.Empty<string>()));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Create_with_all_blank_urls_throws_and_issues_no_request()
    {
        var (client, stub) = WebhookHarness.BuildClient();
        var blanks = new[] { "  ", "" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Webhooks.CreateAsync(WebhookEventType.Delivered, blanks));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Create_with_null_urls_throws_and_issues_no_request()
    {
        var (client, stub) = WebhookHarness.BuildClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Webhooks.CreateAsync(WebhookEventType.Delivered, null!));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Fan_out_with_empty_event_type_set_throws_and_issues_no_request()
    {
        var (client, stub) = WebhookHarness.BuildClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Webhooks.CreateAsync(Array.Empty<WebhookEventType>(), "https://a"));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Fan_out_with_blank_url_throws_and_issues_no_request()
    {
        var (client, stub) = WebhookHarness.BuildClient();
        var events = new[] { WebhookEventType.Delivered };

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Webhooks.CreateAsync(events, "  "));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Undefined_event_type_on_get_throws_and_issues_no_request()
    {
        var (client, stub) = WebhookHarness.BuildClient();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.Webhooks.GetAsync((WebhookEventType)999));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Undefined_event_type_on_create_throws_and_issues_no_request()
    {
        var (client, stub) = WebhookHarness.BuildClient();
        var urls = new[] { "https://a" };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.Webhooks.CreateAsync((WebhookEventType)999, urls));

        Assert.Empty(stub.Requests);
    }
}
