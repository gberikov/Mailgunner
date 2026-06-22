using System.Net;
using System.Text.Json;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class TemplateSendTests
{
    private const string Domain = "mg.example.com";
    private const string SendingKey = "key-123";
    private const string SuccessBody = "{\"id\":\"<20260622.1@mg.example.com>\",\"message\":\"Queued. Thank you.\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient(
        HttpStatusCode statusCode = HttpStatusCode.OK, string responseBody = SuccessBody)
    {
        var stub = new StubHttpMessageHandler(statusCode, responseBody);
        var services = new ServiceCollection();
        services.AddMailgunner(Domain, SendingKey, MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMailgunnerClient>();
        return (client, stub);
    }

    private static MailgunMessage NewTemplatedMessage()
    {
        var message = new MailgunMessage
        {
            From = new EmailAddress("noreply@mg.example.com", "Example"),
            Subject = "Welcome",
            Template = "welcome",
        };
        message.To.Add("alice@example.com");
        return message;
    }

    private static string? FieldValue(StubHttpMessageHandler stub, string name)
    {
        foreach (var field in stub.LastFormData)
        {
            if (field.Name == name)
            {
                return field.Value;
            }
        }

        return null;
    }

    private static int FieldCount(StubHttpMessageHandler stub, string name)
    {
        var count = 0;
        foreach (var field in stub.LastFormData)
        {
            if (field.Name == name)
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task Templated_send_without_body_carries_template_and_returns_result()
    {
        var (client, stub) = BuildClient();

        var result = await client.SendAsync(NewTemplatedMessage());

        Assert.Equal("<20260622.1@mg.example.com>", result.Id);
        Assert.Equal("Queued. Thank you.", result.Message);
        Assert.Equal("welcome", FieldValue(stub, "template"));
    }

    [Fact]
    public async Task Templated_send_targets_messages_endpoint_with_multipart_content()
    {
        var (client, stub) = BuildClient();

        await client.SendAsync(NewTemplatedMessage());

        Assert.Equal(HttpMethod.Post, stub.LastMethod);
        Assert.Equal($"/v3/{Domain}/messages", stub.LastRequestUri!.AbsolutePath);
        Assert.Equal("multipart/form-data", stub.LastContentMediaType);
    }

    [Fact]
    public async Task Global_variables_are_sent_as_a_single_json_object_of_expected_shape()
    {
        var (client, stub) = BuildClient();
        var message = NewTemplatedMessage();
        message.TemplateVariables["product"] = "Acme";
        message.TemplateVariables["seats"] = 5;
        message.TemplateVariables["owner"] = new { name = "Alice" };

        await client.SendAsync(message);

        Assert.Equal(1, FieldCount(stub, "t:variables"));
        var payload = FieldValue(stub, "t:variables");
        Assert.NotNull(payload);

        using var document = JsonDocument.Parse(payload!);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);

        Assert.Equal(JsonValueKind.String, root.GetProperty("product").ValueKind);
        Assert.Equal("Acme", root.GetProperty("product").GetString());

        Assert.Equal(JsonValueKind.Number, root.GetProperty("seats").ValueKind);
        Assert.Equal(5, root.GetProperty("seats").GetInt32());

        var owner = root.GetProperty("owner");
        Assert.Equal(JsonValueKind.Object, owner.ValueKind);
        Assert.Equal("Alice", owner.GetProperty("name").GetString());
    }

    [Fact]
    public async Task No_variables_supplied_omits_the_variables_field()
    {
        var (client, stub) = BuildClient();

        await client.SendAsync(NewTemplatedMessage());

        Assert.Equal(0, FieldCount(stub, "t:variables"));
        Assert.Equal("welcome", FieldValue(stub, "template"));
    }

    [Fact]
    public async Task Empty_variables_map_omits_the_variables_field()
    {
        var (client, stub) = BuildClient();
        var message = NewTemplatedMessage();
        message.TemplateVariables.Clear(); // explicitly empty

        await client.SendAsync(message);

        Assert.Equal(0, FieldCount(stub, "t:variables"));
        Assert.Equal("welcome", FieldValue(stub, "template"));
    }

    [Fact]
    public async Task Templated_send_against_non_success_raises_the_typed_error()
    {
        var (client, _) = BuildClient(HttpStatusCode.BadRequest, "{\"message\":\"template not found\"}");

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(NewTemplatedMessage()));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("{\"message\":\"template not found\"}", ex.ResponseBody);
    }

    [Fact]
    public async Task Sending_key_never_appears_in_fields_result_or_error()
    {
        // Success path: key absent from captured fields and the result.
        var (client, stub) = BuildClient();
        var message = NewTemplatedMessage();
        message.TemplateVariables["product"] = "Acme";

        var result = await client.SendAsync(message);

        foreach (var field in stub.LastFormData)
        {
            Assert.DoesNotContain(SendingKey, field.Value);
        }

        Assert.DoesNotContain(SendingKey, result.Id);
        Assert.DoesNotContain(SendingKey, result.Message);

        // Error path: key absent from the raised exception.
        var (failing, _) = BuildClient(HttpStatusCode.InternalServerError, "server error");
        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => failing.SendAsync(NewTemplatedMessage()));
        Assert.DoesNotContain(SendingKey, ex.ResponseBody);
        Assert.DoesNotContain(SendingKey, ex.Message);
    }
}
