namespace Mailgunner.Internal;

/// <summary>
/// Source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> for the webhook
/// response wire DTOs. Using source generation keeps JSON handling trim/AOT-safe and reflection-free, and
/// works on every target framework. Only response DTOs are registered — webhook create/update requests
/// are form-encoded (<c>id</c>/<c>url</c> parts), not JSON.
/// </summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
[System.Text.Json.Serialization.JsonSerializable(typeof(WebhookListDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(WebhookEnvelopeDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(WebhookUrlsDto))]
internal sealed partial class WebhookJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
