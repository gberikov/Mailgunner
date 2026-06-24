namespace Mailgunner.Internal;

/// <summary>
/// Source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> for the
/// suppression wire DTOs. Using source generation keeps JSON handling trim/AOT-safe and
/// reflection-free, and works on every target framework. Null members are omitted on write so add
/// bodies carry only the supplied fields.
/// </summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
[System.Text.Json.Serialization.JsonSerializable(typeof(PageDto<BounceDto>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(PageDto<UnsubscribeDto>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(PageDto<ComplaintDto>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BounceDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(UnsubscribeDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(ComplaintDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(AddBounceDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(AddUnsubscribeDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(AddComplaintDto))]
internal sealed partial class SuppressionJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
