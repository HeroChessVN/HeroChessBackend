using System.Security.Claims;
using HeroChess.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Auth;

// Policy đọc loại tài khoản hiện tại từ DB: token cũ có role player không giúp Admin vào gameplay.
public static class AccountPolicies
{
    public const string Account = "active-account";
    public const string Player = "player-only";
    public const string Admin = "admin-only";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(Account, p => p.RequireAuthenticatedUser().AddRequirements(new AccountRequirement(null)));
        options.AddPolicy(Player, p => p.RequireAuthenticatedUser().AddRequirements(new AccountRequirement("player")));
        options.AddPolicy(Admin, p => p.RequireAuthenticatedUser().AddRequirements(new AccountRequirement("admin")));
    }
}

public sealed record AccountRequirement(string? Role) : IAuthorizationRequirement;

public sealed class AccountAuthorizationHandler(AppDbContext db) : AuthorizationHandler<AccountRequirement>
{
    // Cả role và status phải khớp DB; không chỉ tin claim đã phát hành từ trước.
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AccountRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return;
        if (await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.Status == "active" &&
            (requirement.Role == null || x.Role == requirement.Role))) context.Succeed(requirement);
    }
}
