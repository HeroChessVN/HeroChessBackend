// Vai trò file: Quản lý tài khoản admin và quyền fine-grained. Tạo admin qua API cần quyền 'admin' và luôn ghi audit.
using HeroChess.Api.Auth;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class AdminAccountService(AppDbContext db, AdminAuditWriter audit, UserManager<User> users, TimeProvider clock)
{
    // ListAsync: Danh sách tài khoản admin kèm quyền đang có.
    public async Task<AdminAccountPageDto> ListAsync(CancellationToken ct)
    {
        var admins = await db.Admins.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct);
        var ids = admins.Select(x => x.Id).ToArray();
        var rows = await db.AdminPermissions.AsNoTracking().Where(x => ids.Contains(x.AdminUserId)).ToDictionaryAsync(x => x.AdminUserId, ct);
        var items = admins.Select(x => new AdminAccountDto(x.Id, x.DisplayName, x.Email, x.Status,
            rows.TryGetValue(x.Id, out var row) ? row.Permissions : [AdminPermissions.Wildcard], x.CreatedAt)).ToArray();
        return new(items);
    }

    // CreateAsync: Tạo tài khoản admin mới bằng UserManager để mật khẩu được hash đúng chuẩn Identity.
    public async Task<AdminAccountDto> CreateAsync(Guid actorId, AdminCreateAccountRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email)) throw new ApiException(400, "EMAIL_REQUIRED", "An email is required.");
        var displayName = AdminValidation.RequireName(request.DisplayName, 80);
        var permissions = Normalize(request.Permissions);
        var account = new Admin { DisplayName = displayName, Email = email, UserName = email };
        var result = await users.CreateAsync(account, request.Password);
        if (!result.Succeeded)
            throw new ApiException(422, "ACCOUNT_CREATE_FAILED",
                string.Join(" ", result.Errors.Select(x => x.Description)));
        await db.SaveChangesAsync(ct);
        await SetPermissionsAsync(actorId, account.Id, permissions, "admin.account.create", request.Email, ct);
        return new(account.Id, account.DisplayName, account.Email, account.Status, permissions, account.CreatedAt);
    }

    // UpdatePermissionsAsync: Thay toàn bộ quyền của một admin; quyền rỗng = mất tất cả, admin phải giữ ít nhất một quyền.
    public async Task<AdminAccountDto> UpdatePermissionsAsync(Guid actorId, Guid targetAdminId, AdminUpdatePermissionsRequest request, CancellationToken ct)
    {
        var permissions = Normalize(request.Permissions);
        var target = await db.Admins.AsNoTracking().SingleOrDefaultAsync(x => x.Id == targetAdminId, ct)
            ?? throw new ApiException(404, "ADMIN_NOT_FOUND", "The administrator account was not found.");
        await SetPermissionsAsync(actorId, targetAdminId, permissions, "admin.permissions.update", target.Email, ct);
        return new(target.Id, target.DisplayName, target.Email, target.Status, permissions, target.CreatedAt);
    }

    // SetPermissionsAsync: Ghi hoặc xoá dòng quyền của admin trong cùng transaction với audit.
    private async Task SetPermissionsAsync(Guid actorId, Guid targetAdminId, string[] permissions, string action, string? entityRef, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.AdminPermissions.FromSqlInterpolated($"SELECT * FROM hero_chess.admin_permission WHERE admin_user_id={targetAdminId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        var before = existing?.Permissions ?? [AdminPermissions.Wildcard];
        if (existing is null)
            db.AdminPermissions.Add(new AdminPermission { AdminUserId = targetAdminId, Permissions = permissions, UpdatedAt = clock.GetUtcNow(), UpdatedBy = actorId });
        else { existing.Permissions = permissions; existing.UpdatedAt = clock.GetUtcNow(); existing.UpdatedBy = actorId; }
        audit.Write(actorId, action, "admin", targetAdminId.ToString("D"), before, permissions, entityRef);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    // Normalize: Chuẩn hóa danh sách quyền; chỉ nhận key đã biết và giữ wildcard.
    private static string[] Normalize(IReadOnlyList<string>? permissions)
    {
        if (permissions is null || permissions.Count == 0) return [AdminPermissions.Wildcard];
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in permissions)
        {
            var key = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (key == AdminPermissions.Wildcard) return [AdminPermissions.Wildcard];
            if (!AdminPermissions.All.Contains(key))
                throw new ApiException(400, "UNKNOWN_PERMISSION", $"Unknown permission '{key}'.");
            set.Add(key);
        }
        if (set.Count == 0) throw new ApiException(400, "EMPTY_PERMISSIONS", "At least one permission is required.");
        return set.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
}
