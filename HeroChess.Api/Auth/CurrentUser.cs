// Vai trò file: Adapter đọc user đang đăng nhập từ HttpContext; gần với lấy principal từ SecurityContext trong Spring.
using System.Security.Claims;

namespace HeroChess.Api.Auth;

public interface ICurrentUser { Guid UserId { get; } }

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    // UserId: Đọc NameIdentifier trong claims đã xác thực; thiếu GUID hợp lệ thì ném UnauthorizedAccessException.
    public Guid UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : throw new UnauthorizedAccessException("Authenticated user identity is missing.");
        }
    }
}
