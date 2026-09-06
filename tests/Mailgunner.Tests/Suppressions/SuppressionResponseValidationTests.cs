using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Suppressions;

public class SuppressionResponseValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[null]}")]
    [InlineData("{\"items\":[{}]}")]
    [InlineData("{\"items\":[{\"address\":\" \"}]}")]
    public async Task Invalid_pages_are_not_treated_as_empty_lists(string body)
    {
        using var provider = BuildProvider(body);
        var lists = provider.GetRequiredService<IMailgunnerClient>().Suppressions;
        Func<Task>[] operations =
        {
            () => lists.Bounces.ListPageAsync(),
            () => lists.Complaints.ListPageAsync(),
            () => lists.Unsubscribes.ListPageAsync(),
        };

        foreach (var operation in operations)
        {
            var ex = await Assert.ThrowsAsync<MailgunnerException>(operation);
            Assert.Equal(200, ex.StatusCode);
            Assert.Equal(body, ex.ResponseBody);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"address\":null}")]
    [InlineData("{\"address\":\"\"}")]
    public async Task Single_entries_require_an_address(string body)
    {
        using var provider = BuildProvider(body);
        var lists = provider.GetRequiredService<IMailgunnerClient>().Suppressions;
        Func<Task>[] operations =
        {
            () => lists.Bounces.GetAsync("a@example.com"),
            () => lists.Complaints.GetAsync("a@example.com"),
            () => lists.Unsubscribes.GetAsync("a@example.com"),
        };

        foreach (var operation in operations)
        {
            var ex = await Assert.ThrowsAsync<MailgunnerException>(operation);
            Assert.Equal(body, ex.ResponseBody);
        }
    }

    private static ServiceProvider BuildProvider(string body)
    {
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-test", MailgunRegion.Us)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(HttpStatusCode.OK, body));
        return services.BuildServiceProvider();
    }
}
