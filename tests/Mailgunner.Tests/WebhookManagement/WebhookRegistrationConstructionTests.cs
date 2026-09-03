using Xunit;

namespace Mailgunner.Tests.WebhookManagement;

/// <summary>
/// A consumer hand-writing a test double for <see cref="IMailgunWebhooks"/> must be able to build the
/// registrations it returns, so the constructor is part of the public surface.
/// </summary>
public class WebhookRegistrationConstructionTests
{
    [Fact]
    public void Registration_is_publicly_constructible()
    {
        Assert.Contains(typeof(WebhookRegistration).GetConstructors(), c => c.IsPublic);
    }

    [Fact]
    public void Registration_exposes_the_supplied_event_type_and_urls()
    {
        var urls = new[] { "https://a", "https://b" };

        var registration = new WebhookRegistration(WebhookEventType.Clicked, urls);

        Assert.Equal(WebhookEventType.Clicked, registration.EventType);
        Assert.Equal(urls, registration.Urls);
    }

    [Fact]
    public void Registration_rejects_null_urls()
    {
        Assert.Throws<ArgumentNullException>(() => new WebhookRegistration(WebhookEventType.Clicked, null!));
    }
}
