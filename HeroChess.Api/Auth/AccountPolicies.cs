// Vai trò file: Policy phân quyền. Role đọc từ DB mỗi request; quyền fine-grained đọc từ bảng admin_permission.
using System.Security.Claims;
using HeroChess.Api.Data;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Auth;

// Policy đọc loại tài khoản hiện tại từ DB: token cũ có role player không giúp Admin vào gameplay.
public static class AccountPolicies
{
    public const string Account = "active-account";
    public const string Player = "player-only";
    public const string Admin = "admin-only";

    // Policy quyền fine-grained: "admin-read:{miền}" cho đọc, "admin-write:{miền}" cho ghi.
    // Quyền 'read' là quyền xem toàn bộ; quyền ghi của một miền cũng cho đọc miền đó.
    public const string ReadPrefix = "admin-read:";
    public const string WritePrefix = "admin-write:";

    public static string Read(string domain) => ReadPrefix + domain;
    public static string Write(string domain) => WritePrefix + domain;

    // Const policy names: attribute [Authorize(Policy = ...)] chỉ nhận hằng số biên dịch.
    public const string ReadAll = ReadPrefix + AdminPermissions.Read;
    public const string HeroRead = ReadPrefix + AdminPermissions.Hero;
    public const string HeroWrite = WritePrefix + AdminPermissions.Hero;
    public const string ShopRead = ReadPrefix + AdminPermissions.Shop;
    public const string ShopWrite = WritePrefix + AdminPermissions.Shop;
    public const string PlayerRead = ReadPrefix + AdminPermissions.Player;
    public const string PlayerWrite = WritePrefix + AdminPermissions.Player;
    public const string GiftCodeRead = ReadPrefix + AdminPermissions.GiftCode;
    public const string GiftCodeWrite = WritePrefix + AdminPermissions.GiftCode;
    public const string AnalyticsRead = ReadPrefix + AdminPermissions.Analytics;
    public const string RulesetRead = ReadPrefix + AdminPermissions.Ruleset;
    public const string AuditRead = ReadPrefix + AdminPermissions.Audit;
    public const string AdminRead = ReadPrefix + AdminPermissions.Admin;
    public const string AdminWrite = WritePrefix + AdminPermissions.Admin;

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(Account, p => p.RequireAuthenticatedUser().AddRequirements(new AccountRequirement(null)));
        options.AddPolicy(Player, p => p.RequireAuthenticatedUser().AddRequirements(new AccountRequirement("player")));
        options.AddPolicy(Admin, p => p.RequireAuthenticatedUser().AddRequirements(new AccountRequirement("admin")));
        foreach (var permission in AdminPermissions.All)
        {
            options.AddPolicy(Read(permission), p => p.RequireAuthenticatedUser().AddRequirements(new AccountRequirement("admin", permission, Write: false)));
            options.AddPolicy(Write(permission), p => p.RequireAuthenticatedUser().AddRequirements(new AccountRequirement("admin", permission, Write: true)));
        }
    }
}

public sealed record AccountRequirement(string? Role, string? Permission = null, bool Write = false) : IAuthorizationRequirement;

public sealed class AccountAuthorizationHandler(AppDbContext db) : AuthorizationHandler<AccountRequirement>
{
    // Cả role và status phải khớp DB; không chỉ tin claim đã phát hành từ trước.
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AccountRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return;
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.Status == "active" &&
            (requirement.Role == null || x.Role == requirement.Role))) return;
        if (requirement.Permission is null) { context.Succeed(requirement); return; }
        if (!await HasPermissionAsync(id, requirement.Permission, requirement.Write)) return;
        context.Succeed(requirement);
    }

    // HasPermissionAsync: Không có dòng quyền = admin toàn quyền (tương thích ngược). Có dòng thì theo luật:
    // đọc/ghi đều cần đúng key của miền. Quyền 'read' chỉ mở miền dashboard ('read'), không mở miền khác.
    private async Task<bool> HasPermissionAsync(Guid adminId, string permission, bool write)
    {
        var row = await db.AdminPermissions.AsNoTracking().SingleOrDefaultAsync(x => x.AdminUserId == adminId);
        if (row is null) return true;
        if (row.Permissions.Contains(AdminPermissions.Wildcard)) return true;
        if (row.Permissions.Contains(permission)) return true;
        // Quyền 'read' chỉ cho xem dashboard tổng quan, không đọc được miền khác (chỉ đọc vẫn lộ PII).
        return !write && permission == AdminPermissions.Read && row.Permissions.Contains(AdminPermissions.Read);
    }
}
