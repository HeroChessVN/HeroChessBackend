// Vai trò file: Cấu hình JSON dùng chung: tên thuộc tính và enum camelCase để API, DB snapshot và Rules đọc cùng định dạng.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroChess.Api.Infrastructure;

public static class GameJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Document: Serialize object thành JsonDocument để lưu cột jsonb.
    public static JsonDocument Document<T>(T value) => JsonDocument.Parse(JsonSerializer.Serialize(value, Options));
    // Element: Serialize object thành JsonElement phục vụ payload/DTO.
    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
    // Read: Deserialize jsonb đã lưu sang model C#; dữ liệu không hợp lệ thì báo lỗi.
    public static T Read<T>(JsonDocument value) => value.RootElement.Deserialize<T>(Options)
        ?? throw new InvalidOperationException($"Stored JSON is not a valid {typeof(T).Name}.");
}
