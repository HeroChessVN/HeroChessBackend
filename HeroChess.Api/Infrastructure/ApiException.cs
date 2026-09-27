// Vai trò file: Exception nghiệp vụ mang HTTP status, mã lỗi và details; middleware chuyển thành JSON cho client.
namespace HeroChess.Api.Infrastructure;

public sealed class ApiException(int statusCode, string code, string message, object? details = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public object? Details { get; } = details;
}
