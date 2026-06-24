using Mailgunner.Internal;
using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

public class WebhookEventTypeMappingTests
{
    [Theory]
    [InlineData(WebhookEventType.Delivered, "delivered")]
    [InlineData(WebhookEventType.Opened, "opened")]
    [InlineData(WebhookEventType.Clicked, "clicked")]
    [InlineData(WebhookEventType.Unsubscribed, "unsubscribed")]
    [InlineData(WebhookEventType.Complained, "complained")]
    [InlineData(WebhookEventType.PermanentFail, "permanent_fail")]
    [InlineData(WebhookEventType.TemporaryFail, "temporary_fail")]
    public void Each_event_type_round_trips_through_its_wire_token(WebhookEventType eventType, string token)
    {
        Assert.Equal(token, WebhookEventTypes.ToToken(eventType));
        Assert.Equal(eventType, WebhookEventTypes.TryParseToken(token));
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("not-a-real-event")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_tokens_parse_to_null(string? token)
    {
        Assert.Null(WebhookEventTypes.TryParseToken(token));
    }

    [Fact]
    public void Undefined_event_type_throws_argument_out_of_range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WebhookEventTypes.ToToken((WebhookEventType)999));
    }
}
