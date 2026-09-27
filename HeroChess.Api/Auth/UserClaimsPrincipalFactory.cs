// Vai trò file: Tạo danh tính đăng nhập từ user; Player và Admin là hai loại tài khoản riêng; role admin lấy từ DB, không nhận từ request.
using System.Security.Claims;
using HeroChess.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HeroChess.Api.Auth;

public sealed class UserClaimsPrincipalFactory(AppDbContext db, IOptions<IdentityOptions> options) : IUserClaimsPrincipalFactory<User>
{
    // CreateAsync: Tạo ClaimsPrincipal gồm user ID, tên, email, role và security stamp để Identity phát hành token.
    public async Task<ClaimsPrincipal> CreateAsync(User user)
    {
        var account = await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
        var claims = options.Value.ClaimsIdentity;
        var identity = new ClaimsIdentity(IdentityConstants.ApplicationScheme, claims.UserNameClaimType, claims.RoleClaimType);
        identity.AddClaim(new Claim(claims.UserIdClaimType, user.Id.ToString("D")));
        identity.AddClaim(new Claim(claims.UserNameClaimType, account.DisplayName));
        identity.AddClaim(new Claim(ClaimTypes.Email, user.Email ?? ""));
        identity.AddClaim(new Claim(claims.RoleClaimType, account.Role));
        identity.AddClaim(new Claim(claims.SecurityStampClaimType, user.SecurityStamp));
        return new ClaimsPrincipal(identity);
    }
}
